//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// 
//  Copyright 2015-2018 Łukasz "JustArchi" Domeradzki
//  Contact: JustArchi@JustArchi.net
// 
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
// 
//  http://www.apache.org/licenses/LICENSE-2.0
//      
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Json;
using ArchiSteamFarm.Localization;

namespace ArchiSteamFarm {
	internal sealed class Trading : IDisposable {
		internal const byte MaxItemsPerTrade = byte.MaxValue; // This is due to limit on POST size in WebBrowser
		internal const byte MaxTradesPerAccount = 5; // This is limit introduced by Valve

		private readonly Bot Bot;
		private readonly ConcurrentHashSet<ulong> IgnoredTrades = new ConcurrentHashSet<ulong>();
		private readonly SemaphoreSlim TradesSemaphore = new SemaphoreSlim(1, 1);

		private bool ParsingScheduled;

		internal Trading(Bot bot) => Bot = bot ?? throw new ArgumentNullException(nameof(bot));

		public void Dispose() => TradesSemaphore.Dispose();

		internal void OnDisconnected() => IgnoredTrades.Clear();

		internal async Task OnNewTrade() {
			// We aim to have a maximum of 2 tasks, one already working, and one waiting in the queue
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

		private static bool IsTradeNeutralOrBetter(IReadOnlyCollection<Steam.Asset> inventory, IReadOnlyCollection<Steam.Asset> itemsToGive, IReadOnlyCollection<Steam.Asset> itemsToReceive) {
			if ((inventory == null) || (inventory.Count == 0) || (itemsToGive == null) || (itemsToGive.Count == 0) || (itemsToReceive == null) || (itemsToReceive.Count == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(inventory) + " || " + nameof(itemsToGive) + " || " + nameof(itemsToReceive));
				return false;
			}

			// Now let's create a map which maps items to their amount in our EQ
			// This has to be done as we might have multiple items of given ClassID with multiple amounts
			Dictionary<ulong, uint> itemAmounts = new Dictionary<ulong, uint>();
			foreach (Steam.Asset item in inventory) {
				itemAmounts[item.ClassID] = itemAmounts.TryGetValue(item.ClassID, out uint amount) ? amount + item.Amount : item.Amount;
			}

			// Calculate our value of items to give on per-game basis
			Dictionary<(Steam.Asset.EType Type, uint AppID), List<uint>> itemAmountToGivePerGame = new Dictionary<(Steam.Asset.EType Type, uint AppID), List<uint>>();
			Dictionary<ulong, uint> itemAmountsToGive = new Dictionary<ulong, uint>(itemAmounts);
			foreach (Steam.Asset item in itemsToGive) {
				if (!itemAmountToGivePerGame.TryGetValue((item.Type, item.RealAppID), out List<uint> amountsToGive)) {
					amountsToGive = new List<uint>();
					itemAmountToGivePerGame[(item.Type, item.RealAppID)] = amountsToGive;
				}

				for (uint i = 0; i < item.Amount; i++) {
					amountsToGive.Add(itemAmountsToGive.TryGetValue(item.ClassID, out uint amount) ? amount : 0);
					itemAmountsToGive[item.ClassID] = --amount; // We're giving one, so we have one less
				}
			}

			// Sort all the lists of amounts to give on per-game basis ascending
			foreach (List<uint> amountsToGive in itemAmountToGivePerGame.Values) {
				amountsToGive.Sort();
			}

			// Calculate our value of items to receive on per-game basis
			Dictionary<(Steam.Asset.EType Type, uint AppID), List<uint>> itemAmountToReceivePerGame = new Dictionary<(Steam.Asset.EType Type, uint AppID), List<uint>>();
			Dictionary<ulong, uint> itemAmountsToReceive = new Dictionary<ulong, uint>(itemAmounts);
			foreach (Steam.Asset item in itemsToReceive) {
				if (!itemAmountToReceivePerGame.TryGetValue((item.Type, item.RealAppID), out List<uint> amountsToReceive)) {
					amountsToReceive = new List<uint>();
					itemAmountToReceivePerGame[(item.Type, item.RealAppID)] = amountsToReceive;
				}

				for (uint i = 0; i < item.Amount; i++) {
					amountsToReceive.Add(itemAmountsToReceive.TryGetValue(item.ClassID, out uint amount) ? amount : 0);
					itemAmountsToReceive[item.ClassID] = ++amount; // We're getting one, so we have one more
				}
			}

			// Sort all the lists of amounts to receive on per-game basis ascending
			foreach (List<uint> amountsToReceive in itemAmountToReceivePerGame.Values) {
				amountsToReceive.Sort();
			}

			// Calculate final neutrality result
			// This is quite complex operation of taking minimum difference from all differences on per-game basis
			// When calculating per-game difference, we sum only amounts at proper indexes, because user might be overpaying
			int difference = itemAmountToGivePerGame.Min(kv => kv.Value.Select((t, i) => (int) (t - itemAmountToReceivePerGame[kv.Key][i])).Sum());
			return difference > 0;
		}

		private async Task ParseActiveTrades() {
			HashSet<Steam.TradeOffer> tradeOffers = await Bot.ArchiWebHandler.GetActiveTradeOffers(IgnoredTrades).ConfigureAwait(false);
			if ((tradeOffers == null) || (tradeOffers.Count == 0)) {
				return;
			}

			IEnumerable<Task<ParseTradeResult>> tasks = tradeOffers.Select(ParseTrade);
			ICollection<ParseTradeResult> results;

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
				HashSet<ulong> acceptedWithItemLoseTradeIDs = results.Where(result => (result != null) && (result.Result == ParseTradeResult.EResult.AcceptedWithItemLose)).Select(result => result.TradeID).ToHashSet();
				if (acceptedWithItemLoseTradeIDs.Count > 0) {
					// Give Steam network some time to generate confirmations
					await Task.Delay(3000).ConfigureAwait(false);
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

					if (await Bot.ArchiWebHandler.AcceptTradeOffer(tradeOffer.TradeOfferID).ConfigureAwait(false)) {
						if (tradeOffer.ItemsToReceive.Sum(item => item.Amount) > tradeOffer.ItemsToGive.Sum(item => item.Amount)) {
							Bot.ArchiLogger.LogGenericTrace(string.Format(Strings.BotAcceptedDonationTrade, tradeOffer.TradeOfferID));
						}
					}

					break;
				case ParseTradeResult.EResult.RejectedPermanently:
				case ParseTradeResult.EResult.RejectedTemporarily:
					if (result.Result == ParseTradeResult.EResult.RejectedPermanently) {
						if (Bot.BotConfig.BotBehaviour.HasFlag(BotConfig.EBotBehaviour.RejectInvalidTrades)) {
							goto case ParseTradeResult.EResult.RejectedAndBlacklisted;
						}

						IgnoredTrades.Add(tradeOffer.TradeOfferID);
					}

					Bot.ArchiLogger.LogGenericInfo(string.Format(Strings.IgnoringTrade, tradeOffer.TradeOfferID));
					break;
				case ParseTradeResult.EResult.RejectedAndBlacklisted:
					Bot.ArchiLogger.LogGenericInfo(string.Format(Strings.RejectingTrade, tradeOffer.TradeOfferID));
					await Bot.ArchiWebHandler.DeclineTradeOffer(tradeOffer.TradeOfferID).ConfigureAwait(false);
					break;
				default:
					Bot.ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(result.Result), result.Result));
					return null;
			}

			return result;
		}

		private async Task<ParseTradeResult> ShouldAcceptTrade(Steam.TradeOffer tradeOffer) {
			if (tradeOffer == null) {
				Bot.ArchiLogger.LogNullError(nameof(tradeOffer));
				return null;
			}

			if (tradeOffer.OtherSteamID64 != 0) {
				// Always accept trades from SteamMasterID
				if (Bot.IsMaster(tradeOffer.OtherSteamID64)) {
					return new ParseTradeResult(tradeOffer.TradeOfferID, tradeOffer.ItemsToGive.Count > 0 ? ParseTradeResult.EResult.AcceptedWithItemLose : ParseTradeResult.EResult.AcceptedWithoutItemLose);
				}

				// Always deny trades from blacklisted steamIDs
				if (Bot.IsBlacklistedFromTrades(tradeOffer.OtherSteamID64)) {
					return new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.RejectedAndBlacklisted);
				}
			}

			// Check if it's donation trade
			switch (tradeOffer.ItemsToGive.Count) {
				case 0 when tradeOffer.ItemsToReceive.Count == 0:
					// If it's steam fuckup, temporarily ignore it
					return new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.RejectedTemporarily);
				case 0:
					// Otherwise react accordingly, depending on our preference
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
					bool isBotTrade = (tradeOffer.OtherSteamID64 != 0) && Bot.Bots.Values.Any(bot => bot.CachedSteamID == tradeOffer.OtherSteamID64);
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

			// Decline trade if we're requested to handle any not-accepted item type or if it's not fair games/types exchange
			if (!tradeOffer.IsValidSteamItemsRequest(Bot.BotConfig.MatchableTypes) || !tradeOffer.IsFairTypesExchange()) {
				return new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.RejectedPermanently);
			}

			// At this point we're sure that STM trade is valid

			// Fetch trade hold duration
			byte? holdDuration = await Bot.GetTradeHoldDuration(tradeOffer.OtherSteamID64, tradeOffer.TradeOfferID).ConfigureAwait(false);
			if (!holdDuration.HasValue) {
				// If we can't get trade hold duration, reject trade temporarily
				return new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.RejectedTemporarily);
			}

			// If user has a trade hold, we add extra logic
			if (holdDuration.Value > 0) {
				// If trade hold duration exceeds our max, or user asks for cards with short lifespan, reject the trade
				if ((holdDuration.Value > Program.GlobalConfig.MaxTradeHoldDuration) || tradeOffer.ItemsToGive.Any(item => ((item.Type == Steam.Asset.EType.FoilTradingCard) || (item.Type == Steam.Asset.EType.TradingCard)) && GlobalConfig.SalesBlacklist.Contains(item.RealAppID))) {
					return new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.RejectedPermanently);
				}
			}

			// If we're matching everything, this is enough for us
			if (Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.MatchEverything)) {
				return new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.AcceptedWithItemLose);
			}

			// Get appIDs/types we're interested in
			HashSet<uint> appIDs = new HashSet<uint>();
			HashSet<Steam.Asset.EType> types = new HashSet<Steam.Asset.EType>();

			foreach (Steam.Asset item in tradeOffer.ItemsToGive) {
				appIDs.Add(item.RealAppID);
				types.Add(item.Type);
			}

			// Now check if it's worth for us to do the trade
			HashSet<Steam.Asset> inventory = await Bot.ArchiWebHandler.GetInventory(Bot.CachedSteamID, wantedTypes: types, wantedRealAppIDs: appIDs).ConfigureAwait(false);
			if ((inventory == null) || (inventory.Count == 0)) {
				// If we can't check our inventory when not using MatchEverything, this is a temporary failure
				Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorIsEmpty, nameof(inventory)));
				return new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.RejectedTemporarily);
			}

			bool accept = IsTradeNeutralOrBetter(inventory, tradeOffer.ItemsToGive, tradeOffer.ItemsToReceive);

			// Even if trade is not neutral+ for us right now, it might be in the future, unless we're bot account where we assume that inventory doesn't change
			return new ParseTradeResult(tradeOffer.TradeOfferID, accept ? ParseTradeResult.EResult.AcceptedWithItemLose : (Bot.BotConfig.BotBehaviour.HasFlag(BotConfig.EBotBehaviour.RejectInvalidTrades) ? ParseTradeResult.EResult.RejectedPermanently : ParseTradeResult.EResult.RejectedTemporarily));
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
				RejectedPermanently,
				RejectedAndBlacklisted
			}
		}
	}
}