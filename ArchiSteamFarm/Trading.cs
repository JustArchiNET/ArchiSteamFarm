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
		private readonly HashSet<ulong> RecentlyParsedTrades = new HashSet<ulong>();

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

		private async Task ForgetRecentTrade(ulong tradeID) {
			await Utilities.SleepAsync(24 * 60 * 60 * 1000).ConfigureAwait(false);
			lock (RecentlyParsedTrades) {
				RecentlyParsedTrades.Remove(tradeID);
				RecentlyParsedTrades.TrimExcess();
			}
		}

		private async Task ParseActiveTrades() {
			HashSet<Steam.TradeOffer> tradeOffers = Bot.ArchiWebHandler.GetTradeOffers();
			if (tradeOffers == null || tradeOffers.Count == 0) {
				return;
			}

			lock (RecentlyParsedTrades) {
				tradeOffers.RemoveWhere(trade => RecentlyParsedTrades.Contains(trade.TradeOfferID));
			}

			if (tradeOffers.Count == 0) {
				return;
			}

			foreach (Steam.TradeOffer tradeOffer in tradeOffers) {
				lock (RecentlyParsedTrades) {
					RecentlyParsedTrades.Add(tradeOffer.TradeOfferID);
				}

				ForgetRecentTrade(tradeOffer.TradeOfferID).Forget();
			}

			await tradeOffers.ForEachAsync(ParseTrade).ConfigureAwait(false);
			await Bot.AcceptConfirmations(true, Confirmation.ConfirmationType.Trade).ConfigureAwait(false);
		}

		private async Task ParseTrade(Steam.TradeOffer tradeOffer) {
			if (tradeOffer == null || tradeOffer.State != Steam.TradeOffer.ETradeOfferState.Active) {
				return;
			}

			if (ShouldAcceptTrade(tradeOffer)) {
				Logging.LogGenericInfo("Accepting trade: " + tradeOffer.TradeOfferID, Bot.BotName);
				await Bot.ArchiWebHandler.AcceptTradeOffer(tradeOffer.TradeOfferID).ConfigureAwait(false);
			} else {
				Logging.LogGenericInfo("Ignoring trade: " + tradeOffer.TradeOfferID, Bot.BotName);
			}
		}

		private bool ShouldAcceptTrade(Steam.TradeOffer tradeOffer) {
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
			if (!tradeOffer.IsSteamCardsOnlyTrade || !tradeOffer.IsPotentiallyDupesTrade) {
				return false;
			}

			// This STM trade SHOULD be fine
			// Potential TODO: Ensure that our inventory in fact has proper amount of both received and given cards
			// This way we could calculate amounts before and after trade, ensuring that we're in fact trading dupes and not 1 + 2 -> 0 + 3
			return true;
		}
	}
}
