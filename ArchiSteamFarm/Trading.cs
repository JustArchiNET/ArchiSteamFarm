/*
    _                _      _  ____   _                           _____
   / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
  / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
 / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
/_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|

 Copyright 2015-2017 Łukasz "JustArchi" Domeradzki
 Contact: JustArchi@JustArchi.net

 Licensed under the Apache License, Version 2.0 (the "License");
 you may not use this file except in compliance with the License.
 You may obtain a copy of the License at

 http://www.apache.org/licenses/LICENSE-2.0
					
 Unless required by applicable law or agreed to in writing, software
 distributed under the License is distributed on an "AS IS" BASIS,
 WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 See the License for the specific language governing permissions and
 limitations under the License.

*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.JSON;
using ArchiSteamFarm.Localization;

namespace ArchiSteamFarm {
	internal sealed class Trading : IDisposable {
		internal const byte MaxItemsPerTrade = 150; // This is due to limit on POST size in WebBrowser
		internal const byte MaxTradesPerAccount = 5; // This is limit introduced by Valve

		private static readonly SemaphoreSlim InventorySemaphore = new SemaphoreSlim(1);

		private readonly Bot Bot;
		private readonly ConcurrentHashSet<ulong> IgnoredTrades = new ConcurrentHashSet<ulong>();
		private readonly SemaphoreSlim TradesSemaphore = new SemaphoreSlim(1);

		private bool ParsingScheduled;

		internal Trading(Bot bot) {
			if (bot == null) {
				throw new ArgumentNullException(nameof(bot));
			}

			Bot = bot;
		}

		public void Dispose() => TradesSemaphore.Dispose();

		internal async Task CheckTrades() {
			// We aim to have a maximum of 2 tasks, one already parsing, and one waiting in the queue
			// This way we can call this function as many times as needed e.g. because of Steam events
			lock (TradesSemaphore) {
				if (ParsingScheduled) {
					return;
				}

				ParsingScheduled = true;
			}

			await TradesSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				lock (TradesSemaphore) {
					ParsingScheduled = false;
				}

				await ParseActiveTrades().ConfigureAwait(false);
			} finally {
				TradesSemaphore.Release();
			}
		}

		internal static async Task LimitInventoryRequestsAsync() {
			await InventorySemaphore.WaitAsync().ConfigureAwait(false);
			Task.Run(async () => {
				await Task.Delay(Program.GlobalConfig.InventoryLimiterDelay * 1000).ConfigureAwait(false);
				InventorySemaphore.Release();
			}).Forget();
		}

		internal void OnDisconnected() => IgnoredTrades.ClearAndTrim();

		private async Task ParseActiveTrades() {
			HashSet<Steam.TradeOffer> tradeOffers = await Bot.ArchiWebHandler.GetActiveTradeOffers().ConfigureAwait(false);
			if ((tradeOffers == null) || (tradeOffers.Count == 0)) {
				return;
			}

			if (tradeOffers.RemoveWhere(tradeoffer => IgnoredTrades.Contains(tradeoffer.TradeOfferID)) > 0) {
				if (tradeOffers.Count == 0) {
					return;
				}
			}

			ICollection<ParseTradeResult> results;
			IEnumerable<Task<ParseTradeResult>> tasks = tradeOffers.Select(ParseTrade);

			switch (Program.GlobalConfig.OptimizationMode) {
				case GlobalConfig.EOptimizationMode.MinMemoryUsage:
					results = new List<ParseTradeResult>(tradeOffers.Count);
					foreach (Task<ParseTradeResult> task in tasks) {
						results.Add(await task.ConfigureAwait(false));
					}

					break;
				default:
					results = await Task.WhenAll(tasks).ConfigureAwait(false);
					break;
			}

			if (Bot.HasMobileAuthenticator) {
				HashSet<ulong> acceptedWithItemLoseTradeIDs = new HashSet<ulong>(results.Where(result => (result != null) && (result.Result == ParseTradeResult.EResult.AcceptedWithItemLose)).Select(result => result.TradeID));
				if (acceptedWithItemLoseTradeIDs.Count > 0) {
					await Task.Delay(3000).ConfigureAwait(false); // Sometimes we can be too fast for Steam servers to generate confirmations, wait a short moment
					await Bot.AcceptConfirmations(true, Steam.ConfirmationDetails.EType.Trade, 0, acceptedWithItemLoseTradeIDs).ConfigureAwait(false);
				}
			}

			if (results.Any(result => (result != null) && ((result.Result == ParseTradeResult.EResult.AcceptedWithItemLose) || (result.Result == ParseTradeResult.EResult.AcceptedWithoutItemLose)))) {
				// If we finished a trade, perform a loot if user wants to do so
				await Bot.LootIfNeeded().ConfigureAwait(false);
			}
		}

		private async Task<ParseTradeResult> ParseTrade(Steam.TradeOffer tradeOffer) {
			if (tradeOffer == null) {
				Bot.ArchiLogger.LogNullError(nameof(tradeOffer));
				return null;
			}

			if (tradeOffer.State != Steam.TradeOffer.ETradeOfferState.Active) {
				Bot.ArchiLogger.LogGenericError(string.Format(Strings.ErrorIsInvalid, tradeOffer.State));
				return null;
			}

			ParseTradeResult result = await ShouldAcceptTrade(tradeOffer).ConfigureAwait(false);
			if (result == null) {
				Bot.ArchiLogger.LogNullError(nameof(result));
				return null;
			}

			switch (result.Result) {
				case ParseTradeResult.EResult.AcceptedWithItemLose:
				case ParseTradeResult.EResult.AcceptedWithoutItemLose:
					Bot.ArchiLogger.LogGenericInfo(string.Format(Strings.AcceptingTrade, tradeOffer.TradeOfferID));
					await Bot.ArchiWebHandler.AcceptTradeOffer(tradeOffer.TradeOfferID).ConfigureAwait(false);
					break;
				case ParseTradeResult.EResult.RejectedPermanently:
				case ParseTradeResult.EResult.RejectedTemporarily:
					if (result.Result == ParseTradeResult.EResult.RejectedPermanently) {
						if (Bot.BotConfig.IsBotAccount) {
							Bot.ArchiLogger.LogGenericInfo(string.Format(Strings.RejectingTrade, tradeOffer.TradeOfferID));
							await Bot.ArchiWebHandler.DeclineTradeOffer(tradeOffer.TradeOfferID).ConfigureAwait(false);
							break;
						}

						IgnoredTrades.Add(tradeOffer.TradeOfferID);
					}

					Bot.ArchiLogger.LogGenericInfo(string.Format(Strings.IgnoringTrade, tradeOffer.TradeOfferID));
					break;
			}

			return result;
		}

		private async Task<ParseTradeResult> ShouldAcceptTrade(Steam.TradeOffer tradeOffer) {
			if (tradeOffer == null) {
				Bot.ArchiLogger.LogNullError(nameof(tradeOffer));
				return null;
			}

			// Always accept trades from SteamMasterID
			if ((tradeOffer.OtherSteamID64 != 0) && ((tradeOffer.OtherSteamID64 == Bot.BotConfig.SteamMasterID) || (tradeOffer.OtherSteamID64 == Program.GlobalConfig.SteamOwnerID))) {
				return new ParseTradeResult(tradeOffer.TradeOfferID, tradeOffer.ItemsToGive.Count > 0 ? ParseTradeResult.EResult.AcceptedWithItemLose : ParseTradeResult.EResult.AcceptedWithoutItemLose);
			}

			// Check if it's donation trade
			if (tradeOffer.ItemsToGive.Count == 0) {
				// If it's steam fuckup, temporarily ignore it, otherwise react accordingly, depending on our preference
				if (tradeOffer.ItemsToReceive.Count == 0) {
					return new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.RejectedTemporarily);
				}

				bool acceptDonations = Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.AcceptDonations);
				bool acceptBotTrades = !Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.DontAcceptBotTrades);

				// If we accept donations and bot trades, accept it right away
				if (acceptDonations && acceptBotTrades) {
					return new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.AcceptedWithoutItemLose);
				}

				// If we don't accept donations, neither bot trades, deny it right away
				if (!acceptDonations && !acceptBotTrades) {
					return new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.RejectedPermanently);
				}

				// Otherwise we either accept donations but not bot trades, or we accept bot trades but not donations
				bool isBotTrade = (tradeOffer.OtherSteamID64 != 0) && Bot.Bots.Values.Any(bot => bot.SteamID == tradeOffer.OtherSteamID64);
				return new ParseTradeResult(tradeOffer.TradeOfferID, (acceptDonations && !isBotTrade) || (acceptBotTrades && isBotTrade) ? ParseTradeResult.EResult.AcceptedWithoutItemLose : ParseTradeResult.EResult.RejectedPermanently);
			}

			// If we don't have SteamTradeMatcher enabled, this is the end for us
			if (!Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.SteamTradeMatcher)) {
				return new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.RejectedPermanently);
			}

			// Decline trade if we're giving more count-wise
			if (tradeOffer.ItemsToGive.Count > tradeOffer.ItemsToReceive.Count) {
				return new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.RejectedPermanently);
			}

			// Decline trade if we're losing anything but steam cards, or if it's non-dupes trade
			if (!tradeOffer.IsSteamCardsRequest() || !tradeOffer.IsFairTypesExchange()) {
				return new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.RejectedPermanently);
			}

			// At this point we're sure that STM trade is valid

			// Fetch trade hold duration
			byte? holdDuration = await Bot.ArchiWebHandler.GetTradeHoldDuration(tradeOffer.TradeOfferID).ConfigureAwait(false);
			if (!holdDuration.HasValue) {
				// If we can't get trade hold duration, reject trade temporarily
				return new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.RejectedTemporarily);
			}

			// If user has a trade hold, we add extra logic
			if (holdDuration.Value > 0) {
				// If trade hold duration exceeds our max, or user asks for cards with short lifespan, reject the trade
				if ((holdDuration.Value > Program.GlobalConfig.MaxTradeHoldDuration) || tradeOffer.ItemsToGive.Any(item => GlobalConfig.GlobalBlacklist.Contains(item.RealAppID))) {
					return new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.RejectedPermanently);
				}
			}

			// If we're matching everything, this is enough for us
			if (Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.MatchEverything)) {
				return new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.AcceptedWithItemLose);
			}

			// Now check if it's worth for us to do the trade
			await LimitInventoryRequestsAsync().ConfigureAwait(false);

			HashSet<Steam.Item> inventory = await Bot.ArchiWebHandler.GetMySteamInventory(false, new HashSet<Steam.Item.EType> { Steam.Item.EType.TradingCard }).ConfigureAwait(false);
			if ((inventory == null) || (inventory.Count == 0)) {
				// If we can't check our inventory when not using MatchEverything, this is a temporary failure
				Bot.ArchiLogger.LogGenericWarning(string.Join(Strings.ErrorIsEmpty, nameof(inventory)));
				return new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.RejectedTemporarily);
			}

			// Get appIDs we're interested in
			HashSet<uint> appIDs = new HashSet<uint>(tradeOffer.ItemsToGive.Select(item => item.RealAppID));

			// Now remove from our inventory all items we're NOT interested in
			inventory.RemoveWhere(item => !appIDs.Contains(item.RealAppID));

			// If for some reason Valve is talking crap and we can't find mentioned items, this is a temporary failure
			if (inventory.Count == 0) {
				Bot.ArchiLogger.LogGenericWarning(string.Join(Strings.ErrorIsEmpty, nameof(inventory)));
				return new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.RejectedTemporarily);
			}

			// Now let's create a map which maps items to their amount in our EQ
			Dictionary<ulong, uint> amountMap = new Dictionary<ulong, uint>();
			foreach (Steam.Item item in inventory) {
				uint amount;
				if (amountMap.TryGetValue(item.ClassID, out amount)) {
					amountMap[item.ClassID] = amount + item.Amount;
				} else {
					amountMap[item.ClassID] = item.Amount;
				}
			}

			// Calculate our value of items to give
			List<uint> amountsToGive = new List<uint>(tradeOffer.ItemsToGive.Count);
			Dictionary<ulong, uint> amountMapToGive = new Dictionary<ulong, uint>(amountMap);
			foreach (ulong key in tradeOffer.ItemsToGive.Select(item => item.ClassID)) {
				uint amount;
				if (!amountMapToGive.TryGetValue(key, out amount)) {
					amountsToGive.Add(0);
					continue;
				}

				amountsToGive.Add(amount);
				amountMapToGive[key] = amount - 1; // We're giving one, so we have one less
			}

			// Sort it ascending
			amountsToGive.Sort();

			// Calculate our value of items to receive
			List<uint> amountsToReceive = new List<uint>(tradeOffer.ItemsToReceive.Count);
			Dictionary<ulong, uint> amountMapToReceive = new Dictionary<ulong, uint>(amountMap);
			foreach (ulong key in tradeOffer.ItemsToReceive.Select(item => item.ClassID)) {
				uint amount;
				if (!amountMapToReceive.TryGetValue(key, out amount)) {
					amountsToReceive.Add(0);
					continue;
				}

				amountsToReceive.Add(amount);
				amountMapToReceive[key] = amount + 1; // We're getting one, so we have one more
			}

			// Sort it ascending
			amountsToReceive.Sort();

			// Check actual difference
			// We sum only values at proper indexes of giving, because user might be overpaying
			int difference = amountsToGive.Select((t, i) => (int) (t - amountsToReceive[i])).Sum();

			// Trade is worth for us if the difference is greater than 0
			// If not, we assume that the trade might be good for us in the future, unless we're bot account where we assume that inventory doesn't change
			return new ParseTradeResult(tradeOffer.TradeOfferID, difference > 0 ? ParseTradeResult.EResult.AcceptedWithItemLose : (Bot.BotConfig.IsBotAccount ? ParseTradeResult.EResult.RejectedPermanently : ParseTradeResult.EResult.RejectedTemporarily));
		}

		private sealed class ParseTradeResult {
			internal readonly EResult Result;

			internal readonly ulong TradeID;

			internal ParseTradeResult(ulong tradeID, EResult result) {
				if ((tradeID == 0) || (result == EResult.Unknown)) {
					throw new ArgumentNullException(nameof(tradeID) + " || " + nameof(result));
				}

				TradeID = tradeID;
				Result = result;
			}

			internal enum EResult : byte {
				Unknown,
				AcceptedWithItemLose,
				AcceptedWithoutItemLose,
				RejectedTemporarily,
				RejectedPermanently
			}
		}
	}
}