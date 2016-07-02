/*
    _                _      _  ____   _                           _____
   / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
  / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
 / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
/_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|

 Copyright 2015-2016 Łukasz "JustArchi" Domeradzki
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
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.JSON;

namespace ArchiSteamFarm {
	internal sealed class Trading {
		private enum ParseTradeResult : byte {
			[SuppressMessage("ReSharper", "UnusedMember.Local")]
			Unknown,
			Error,
			AcceptedWithItemLose,
			AcceptedWithoutItemLose,
			RejectedTemporarily,
			RejectedPermanently
		}

		internal const byte MaxItemsPerTrade = 150; // This is due to limit on POST size in WebBrowser
		internal const byte MaxTradesPerAccount = 5; // This is limit introduced by Valve

		private static readonly SemaphoreSlim InventorySemaphore = new SemaphoreSlim(1);

		private readonly Bot Bot;
		private readonly ConcurrentHashSet<ulong> IgnoredTrades = new ConcurrentHashSet<ulong>();
		private readonly SemaphoreSlim TradesSemaphore = new SemaphoreSlim(1);

		private byte ParsingTasks;

		internal static async Task LimitInventoryRequestsAsync() {
			await InventorySemaphore.WaitAsync().ConfigureAwait(false);
			Task.Run(async () => {
				await Task.Delay(Program.GlobalConfig.InventoryLimiterDelay * 1000).ConfigureAwait(false);
				InventorySemaphore.Release();
			}).Forget();
		}

		internal Trading(Bot bot) {
			if (bot == null) {
				throw new ArgumentNullException(nameof(bot));
			}

			Bot = bot;
		}

		internal void OnDisconnected() => IgnoredTrades.ClearAndTrim();

		internal async Task CheckTrades() {
			lock (TradesSemaphore) {
				if (ParsingTasks >= 2) {
					return;
				}

				ParsingTasks++;
			}

			await TradesSemaphore.WaitAsync().ConfigureAwait(false);

			await ParseActiveTrades().ConfigureAwait(false);
			lock (TradesSemaphore) {
				ParsingTasks--;
			}

			TradesSemaphore.Release();
		}

		private async Task ParseActiveTrades() {
			if (string.IsNullOrEmpty(Bot.BotConfig.SteamApiKey)) {
				return;
			}

			HashSet<Steam.TradeOffer> tradeOffers = Bot.ArchiWebHandler.GetActiveTradeOffers();
			if ((tradeOffers == null) || (tradeOffers.Count == 0)) {
				return;
			}

			if (tradeOffers.RemoveWhere(tradeoffer => IgnoredTrades.Contains(tradeoffer.TradeOfferID)) > 0) {
				tradeOffers.TrimExcess();
				if (tradeOffers.Count == 0) {
					return;
				}
			}

			List<Task<ParseTradeResult>> tasks = tradeOffers.Select(ParseTrade).ToList();
			ParseTradeResult[] results = await Task.WhenAll(tasks).ConfigureAwait(false);

			if (results.Any(result => result == ParseTradeResult.AcceptedWithItemLose)) {
				await Task.Delay(1000).ConfigureAwait(false); // Sometimes we can be too fast for Steam servers to generate confirmations, wait a short moment
				HashSet<ulong> tradeIDs = new HashSet<ulong>(tradeOffers.Select(tradeOffer => tradeOffer.TradeOfferID));
				await Bot.AcceptConfirmations(true, Steam.ConfirmationDetails.EType.Trade, 0, tradeIDs).ConfigureAwait(false);
			}
		}

		private async Task<ParseTradeResult> ParseTrade(Steam.TradeOffer tradeOffer) {
			if (tradeOffer == null) {
				Logging.LogNullError(nameof(tradeOffer), Bot.BotName);
				return ParseTradeResult.Error;
			}

			if (tradeOffer.State != Steam.TradeOffer.ETradeOfferState.Active) {
				return ParseTradeResult.Error;
			}

			ParseTradeResult result = await ShouldAcceptTrade(tradeOffer).ConfigureAwait(false);
			switch (result) {
				case ParseTradeResult.AcceptedWithItemLose:
				case ParseTradeResult.AcceptedWithoutItemLose:
					Logging.Log("Accepting trade: " + tradeOffer.TradeOfferID, LogSeverity.Info, Bot.BotName);
					return await Bot.ArchiWebHandler.AcceptTradeOffer(tradeOffer.TradeOfferID).ConfigureAwait(false) ? result : ParseTradeResult.Error;
				case ParseTradeResult.RejectedPermanently:
				case ParseTradeResult.RejectedTemporarily:
					if (result == ParseTradeResult.RejectedPermanently) {
						if (Bot.BotConfig.IsBotAccount) {
							Logging.Log("Rejecting trade: " + tradeOffer.TradeOfferID, LogSeverity.Info, Bot.BotName);
							return Bot.ArchiWebHandler.DeclineTradeOffer(tradeOffer.TradeOfferID) ? result : ParseTradeResult.Error;
						}

						IgnoredTrades.Add(tradeOffer.TradeOfferID);
					}

					Logging.Log("Ignoring trade: " + tradeOffer.TradeOfferID, LogSeverity.Info, Bot.BotName);
					return result;
				default:
					return result;
			}
		}

		private async Task<ParseTradeResult> ShouldAcceptTrade(Steam.TradeOffer tradeOffer) {
			if (tradeOffer == null) {
				Logging.LogNullError(nameof(tradeOffer), Bot.BotName);
				return ParseTradeResult.Error;
			}

			// Always accept trades when we're not losing anything
			if (tradeOffer.ItemsToGive.Count == 0) {
				// Unless it's steam fuckup and we're dealing with broken trade
				return tradeOffer.ItemsToReceive.Count > 0 ? ParseTradeResult.AcceptedWithoutItemLose : ParseTradeResult.RejectedTemporarily;
			}

			// Always accept trades from SteamMasterID
			if ((tradeOffer.OtherSteamID64 != 0) && (tradeOffer.OtherSteamID64 == Bot.BotConfig.SteamMasterID)) {
				return ParseTradeResult.AcceptedWithItemLose;
			}

			// If we don't have SteamTradeMatcher enabled, this is the end for us
			if (!Bot.BotConfig.SteamTradeMatcher) {
				return ParseTradeResult.RejectedPermanently;
			}

			// Decline trade if we're giving more count-wise
			if (tradeOffer.ItemsToGive.Count > tradeOffer.ItemsToReceive.Count) {
				return ParseTradeResult.RejectedPermanently;
			}

			// Decline trade if we're losing anything but steam cards, or if it's non-dupes trade
			if (!tradeOffer.IsSteamCardsOnlyTradeForUs() || !tradeOffer.IsPotentiallyDupesTradeForUs()) {
				return ParseTradeResult.RejectedPermanently;
			}

			// At this point we're sure that STM trade is valid

			// Fetch trade hold duration
			byte? holdDuration = await Bot.ArchiWebHandler.GetTradeHoldDuration(tradeOffer.TradeOfferID).ConfigureAwait(false);
			if (!holdDuration.HasValue) {
				// If we can't get trade hold duration, reject trade temporarily
				return ParseTradeResult.RejectedTemporarily;
			}

			// If user has a trade hold, we add extra logic
			if (holdDuration.Value > 0) {
				// If trade hold duration exceeds our max, or user asks for cards with short lifespan, reject the trade
				if ((holdDuration.Value > Program.GlobalConfig.MaxTradeHoldDuration) || tradeOffer.ItemsToGive.Any(item => GlobalConfig.GlobalBlacklist.Contains(item.RealAppID))) {
					return ParseTradeResult.RejectedPermanently;
				}
			}

			// Now check if it's worth for us to do the trade
			await LimitInventoryRequestsAsync().ConfigureAwait(false);

			HashSet<Steam.Item> inventory = await Bot.ArchiWebHandler.GetMyInventory(false).ConfigureAwait(false);
			if ((inventory == null) || (inventory.Count == 0)) {
				return ParseTradeResult.AcceptedWithItemLose; // OK, assume that this trade is valid, we can't check our EQ
			}

			// Get appIDs we're interested in
			HashSet<uint> appIDs = new HashSet<uint>(tradeOffer.ItemsToGive.Select(item => item.RealAppID));

			// Now remove from our inventory all items we're NOT interested in
			inventory.RemoveWhere(item => !appIDs.Contains(item.RealAppID));
			inventory.TrimExcess();

			// If for some reason Valve is talking crap and we can't find mentioned items, assume OK
			if (inventory.Count == 0) {
				return ParseTradeResult.AcceptedWithItemLose;
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
			return difference > 0 ? ParseTradeResult.AcceptedWithItemLose : ParseTradeResult.RejectedTemporarily;
		}
	}
}
