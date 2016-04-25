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

using SteamAuth;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
				throw new ArgumentNullException("bot");
			}

			Bot = bot;
		}

		internal async Task CheckTrades() {
			bool shouldRun = false;
			lock (TradesSemaphore) {
				if (ParsingTasks < 2) {
					ParsingTasks++;
					shouldRun = true;
				}
			}

			if (!shouldRun) {
				return;
			}

			await TradesSemaphore.WaitAsync().ConfigureAwait(false);

			await ParseActiveTrades().ConfigureAwait(false);
			lock (TradesSemaphore) {
				ParsingTasks--;
			}

			TradesSemaphore.Release();
		}

		private async Task ParseActiveTrades() {
			HashSet<Steam.TradeOffer> tradeOffers = Bot.ArchiWebHandler.GetTradeOffers();
			if (tradeOffers == null || tradeOffers.Count == 0) {
				return;
			}

			await tradeOffers.ForEachAsync(ParseTrade).ConfigureAwait(false);
			await Bot.AcceptConfirmations(true, Confirmation.ConfirmationType.Trade).ConfigureAwait(false);
		}

		private async Task ParseTrade(Steam.TradeOffer tradeOffer) {
			if (tradeOffer == null || tradeOffer.State != Steam.TradeOffer.ETradeOfferState.Active) {
				return;
			}

			if (await ShouldAcceptTrade(tradeOffer).ConfigureAwait(false)) {
				Logging.LogGenericInfo("Accepting trade: " + tradeOffer.TradeOfferID, Bot.BotName);
				await Bot.ArchiWebHandler.AcceptTradeOffer(tradeOffer.TradeOfferID).ConfigureAwait(false);
			} else {
				Logging.LogGenericInfo("Ignoring trade: " + tradeOffer.TradeOfferID, Bot.BotName);
			}
		}

		private async Task<bool> ShouldAcceptTrade(Steam.TradeOffer tradeOffer) {
			if (tradeOffer == null) {
				return false;
			}

			// Always accept trades when we're not losing anything
			if (tradeOffer.ItemsToGive.Count == 0) {
				// Unless it's steam fuckup and we're dealing with broken trade
				return tradeOffer.ItemsToReceive.Count > 0;
			}

			// Always accept trades from SteamMasterID
			if (tradeOffer.OtherSteamID64 != 0 && tradeOffer.OtherSteamID64 == Bot.BotConfig.SteamMasterID) {
				return true;
			}

			// If we don't have SteamTradeMatcher enabled, this is the end for us
			if (!Bot.BotConfig.SteamTradeMatcher) {
				return false;
			}

			// Rule 1 - We always trade the same amount of items
			if (tradeOffer.ItemsToGive.Count != tradeOffer.ItemsToReceive.Count) {
				return false;
			}

			// Rule 2 - We always trade steam cards and only for the same set
			if (!tradeOffer.IsSteamCardsOnlyTrade() || !tradeOffer.IsPotentiallyDupesTrade()) {
				return false;
			}

			// At this point we're sure that STM trade is valid
			// Now check if it's worth for us to do the trade
			HashSet<Steam.Item> inventory = await Bot.ArchiWebHandler.GetMyTradableInventory().ConfigureAwait(false);
			if (inventory == null || inventory.Count == 0) {
				return true; // OK, assume that this trade is valid, we can't check our EQ
			}

			// Get appIDs we're interested in
			HashSet<uint> appIDs = new HashSet<uint>();
			foreach (Steam.Item item in tradeOffer.ItemsToGive) {
				appIDs.Add(item.RealAppID);
			}

			// Now remove from our inventory all items we're NOT interested in
			inventory.RemoveWhere(item => !appIDs.Contains(item.RealAppID));
			inventory.TrimExcess();

			// If for some reason Valve is talking crap and we can't find mentioned items, assume OK
			if (inventory.Count == 0) {
				return true;
			}

			// Now let's create a map which maps items to their amount in our EQ
			Dictionary<Tuple<ulong, ulong>, uint> amountMap = new Dictionary<Tuple<ulong, ulong>, uint>();
			foreach (Steam.Item item in inventory) {
				Tuple<ulong, ulong> key = new Tuple<ulong, ulong>(item.ClassID, item.InstanceID);

				uint amount;
				if (amountMap.TryGetValue(key, out amount)) {
					amountMap[key] = amount + item.Amount;
				} else {
					amountMap[key] = item.Amount;
				}
			}

			// Calculate our value of items to give
			uint itemsToGiveDupesValue = 0;
			foreach (Steam.Item item in tradeOffer.ItemsToGive) {
				Tuple<ulong, ulong> key = new Tuple<ulong, ulong>(item.ClassID, item.InstanceID);

				uint amount;
				if (!amountMap.TryGetValue(key, out amount)) {
					continue;
				}

				itemsToGiveDupesValue += amount;
			}

			// Calculate our value of items to receive
			uint itemsToReceiveDupesValue = 0;
			foreach (Steam.Item item in tradeOffer.ItemsToReceive) {
				Tuple<ulong, ulong> key = new Tuple<ulong, ulong>(item.ClassID, item.InstanceID);

				uint amount;
				if (!amountMap.TryGetValue(key, out amount)) {
					continue;
				}

				itemsToReceiveDupesValue += amount;
			}

			// Trade is worth for us if we're in total trading more of our dupes for less of our dupes (or at least same amount)
			// Which means that itemsToGiveDupesValue should be greater than itemsToReceiveDupesValue
			return itemsToGiveDupesValue > itemsToReceiveDupesValue;
		}
	}
}
