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
				return;
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
			List<Steam.TradeOffer> tradeOffers = Bot.ArchiWebHandler.GetTradeOffers();
			if (tradeOffers == null) {
				return;
			}

			await tradeOffers.ForEachAsync(ParseTrade).ConfigureAwait(false);
			await Bot.AcceptConfirmations(Confirmation.ConfirmationType.Trade).ConfigureAwait(false);
		}

		private async Task ParseTrade(Steam.TradeOffer tradeOffer) {
			if (tradeOffer == null || tradeOffer.trade_offer_state != Steam.TradeOffer.ETradeOfferState.Active) {
				return;
			}

			ulong tradeID;
			if (!ulong.TryParse(tradeOffer.tradeofferid, out tradeID)) {
				return;
			}

			if (ShouldAcceptTrade(tradeOffer)) {
				Logging.LogGenericInfo("Accepting trade: " + tradeID, Bot.BotName);
				await Bot.ArchiWebHandler.AcceptTradeOffer(tradeID).ConfigureAwait(false);
			} else {
				Logging.LogGenericInfo("Ignoring trade: " + tradeID, Bot.BotName);
			}
		}

		private bool ShouldAcceptTrade(Steam.TradeOffer tradeOffer) {
			if (tradeOffer == null) {
				return false;
			}

			// Always accept trades when we're not losing anything
			if (tradeOffer.items_to_give.Count == 0) {
				return true;
			}

			// Always accept trades from SteamMasterID
			if (tradeOffer.OtherSteamID64 != 0 && tradeOffer.OtherSteamID64 == Bot.BotConfig.SteamMasterID) {
				return true;
			}

			// TODO: Add optional SteamTradeMatcher integration here

			// If no rule above matched this trade, reject it
			return false;
		}
	}
}
