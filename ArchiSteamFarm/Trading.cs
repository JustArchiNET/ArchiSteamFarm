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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.JSON;

namespace ArchiSteamFarm {
	internal sealed class Trading {
		internal const byte MaxItemsPerTrade = 150; // This is due to limit on POST size in WebBrowser
		internal const byte MaxTradesPerAccount = 5; // This is limit introduced by Valve

		private static readonly SemaphoreSlim InventorySemaphore = new SemaphoreSlim(1);

		private readonly Bot Bot;
		private readonly SemaphoreSlim TradesSemaphore = new SemaphoreSlim(1);

		private byte ParsingTasks;

		internal static async Task LimitInventoryRequestsAsync() {
			await InventorySemaphore.WaitAsync().ConfigureAwait(false);
			Task.Run(async () => {
				await Utilities.SleepAsync(Program.GlobalConfig.InventoryLimiterDelay * 1000).ConfigureAwait(false);
				InventorySemaphore.Release();
			}).Forget();
		}

		internal Trading(Bot bot) {
			if (bot == null) {
				throw new ArgumentNullException(nameof(bot));
			}

			Bot = bot;
		}

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

			HashSet<Steam.TradeOffer> tradeOffers = Bot.ArchiWebHandler.GetTradeOffers();
			if ((tradeOffers == null) || (tradeOffers.Count == 0)) {
				return;
			}

			if (tradeOffers.RemoveWhere(tradeoffer => tradeoffer.State != Steam.TradeOffer.ETradeOfferState.Active) > 0) {
				tradeOffers.TrimExcess();
				if (tradeOffers.Count == 0) {
					return;
				}
			}

			List<Task<bool>> tasks = tradeOffers.Select(ParseTrade).ToList();
			bool[] results = await Task.WhenAll(tasks).ConfigureAwait(false);

			if (results.Any(result => result)) {
				HashSet<ulong> tradeIDs = new HashSet<ulong>(tradeOffers.Select(tradeOffer => tradeOffer.TradeOfferID));
				await Bot.AcceptConfirmations(true, Steam.ConfirmationDetails.EType.Trade, 0, tradeIDs).ConfigureAwait(false);
			}
		}

		private async Task<bool> ParseTrade(Steam.TradeOffer tradeOffer) {
			if (tradeOffer == null) {
				Logging.LogNullError(nameof(tradeOffer), Bot.BotName);
				return false;
			}

			if (tradeOffer.State != Steam.TradeOffer.ETradeOfferState.Active) {
				return false;
			}

			if (await ShouldAcceptTrade(tradeOffer).ConfigureAwait(false)) {
				Logging.LogGenericInfo("Accepting trade: " + tradeOffer.TradeOfferID, Bot.BotName);
				return await Bot.ArchiWebHandler.AcceptTradeOffer(tradeOffer.TradeOfferID).ConfigureAwait(false);
			}

			if (Bot.BotConfig.IsBotAccount) {
				Logging.LogGenericInfo("Rejecting trade: " + tradeOffer.TradeOfferID, Bot.BotName);
				return Bot.ArchiWebHandler.DeclineTradeOffer(tradeOffer.TradeOfferID);
			}

			Logging.LogGenericInfo("Ignoring trade: " + tradeOffer.TradeOfferID, Bot.BotName);
			return false;
		}

		private async Task<bool> ShouldAcceptTrade(Steam.TradeOffer tradeOffer) {
			if (tradeOffer == null) {
				Logging.LogNullError(nameof(tradeOffer), Bot.BotName);
				return false;
			}

			// Always accept trades when we're not losing anything
			if (tradeOffer.ItemsToGive.Count == 0) {
				// Unless it's steam fuckup and we're dealing with broken trade
				return tradeOffer.ItemsToReceive.Count > 0;
			}

			// Always accept trades from SteamMasterID
			if ((tradeOffer.OtherSteamID64 != 0) && (tradeOffer.OtherSteamID64 == Bot.BotConfig.SteamMasterID)) {
				return true;
			}

			// If we don't have SteamTradeMatcher enabled, this is the end for us
			if (!Bot.BotConfig.SteamTradeMatcher) {
				return false;
			}

			// Decline trade if we're giving more count-wise
			if (tradeOffer.ItemsToGive.Count > tradeOffer.ItemsToReceive.Count) {
				return false;
			}

			// Decline trade if we're losing anything but steam cards, or if it's non-dupes trade
			if (!tradeOffer.IsSteamCardsOnlyTradeForUs() || !tradeOffer.IsPotentiallyDupesTradeForUs()) {
				return false;
			}

			// At this point we're sure that STM trade is valid

			// If we're dealing with special cards with short lifespan, accept the trade only if user doesn't have trade holds
			if (tradeOffer.ItemsToGive.Any(item => GlobalConfig.GlobalBlacklist.Contains(item.RealAppID))) {
				byte? holdDuration = await Bot.ArchiWebHandler.GetTradeHoldDuration(tradeOffer.TradeOfferID).ConfigureAwait(false);
				if (holdDuration.GetValueOrDefault() > 0) {
					return false;
				}
			}

			// Now check if it's worth for us to do the trade
			await LimitInventoryRequestsAsync().ConfigureAwait(false);

			HashSet<Steam.Item> inventory = await Bot.ArchiWebHandler.GetMyInventory(false).ConfigureAwait(false);
			if ((inventory == null) || (inventory.Count == 0)) {
				return true; // OK, assume that this trade is valid, we can't check our EQ
			}

			// Get appIDs we're interested in
			HashSet<uint> appIDs = new HashSet<uint>(tradeOffer.ItemsToGive.Select(item => item.RealAppID));

			// Now remove from our inventory all items we're NOT interested in
			inventory.RemoveWhere(item => !appIDs.Contains(item.RealAppID));
			inventory.TrimExcess();

			// If for some reason Valve is talking crap and we can't find mentioned items, assume OK
			if (inventory.Count == 0) {
				return true;
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
			foreach (ulong key in tradeOffer.ItemsToGive.Select(item => item.ClassID)) {
				uint amount;
				if (!amountMap.TryGetValue(key, out amount)) {
					amountsToGive.Add(0);
					continue;
				}

				amountsToGive.Add(amount);
				amountMap[key] = amount - 1; // We're giving one, so we have one less
			}

			// Sort it ascending
			amountsToGive.Sort();

			// Calculate our value of items to receive
			List<uint> amountsToReceive = new List<uint>(tradeOffer.ItemsToReceive.Count);
			foreach (ulong key in tradeOffer.ItemsToReceive.Select(item => item.ClassID)) {
				uint amount;
				if (!amountMap.TryGetValue(key, out amount)) {
					amountsToReceive.Add(0);
					continue;
				}

				amountsToReceive.Add(amount);
				amountMap[key] = amount + 1; // We're getting one, so we have one more
			}

			// Sort it ascending
			amountsToReceive.Sort();

			// Check actual difference
			int difference = amountsToGive.Select((t, i) => (int) (t - amountsToReceive[i])).Sum();

			// Trade is worth for us if the difference is greater than 0
			return difference > 0;
		}
	}
}
