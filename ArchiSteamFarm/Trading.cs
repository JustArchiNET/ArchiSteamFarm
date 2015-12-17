/*
    _                _      _  ____   _                           _____
   / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
  / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
 / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
/_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|

 Copyright 2015 Łukasz "JustArchi" Domeradzki
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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ArchiSteamFarm {
	internal sealed class Trading {
		private readonly Bot Bot;
		private readonly SemaphoreSlim Semaphore = new SemaphoreSlim(1);
		private volatile byte ParsingTasks = 0;

		internal Trading(Bot bot) {
			Bot = bot;
		}

		internal async void CheckTrades() {
			if (ParsingTasks < 2) {
				ParsingTasks++;

				await Semaphore.WaitAsync().ConfigureAwait(false);
				await ParseActiveTrades().ConfigureAwait(false);
				Semaphore.Release();

				ParsingTasks--;
			}
		}

		private async Task ParseActiveTrades() {
			List<SteamTradeOffer> tradeOffers = Bot.ArchiWebHandler.GetTradeOffers();
			if (tradeOffers == null) {
				return;
			}

			List<Task> tasks = new List<Task>();
			foreach (SteamTradeOffer tradeOffer in tradeOffers) {
				if (tradeOffer.trade_offer_state == SteamTradeOffer.ETradeOfferState.Active) {
					tasks.Add(Task.Run(async () => await ParseTrade(tradeOffer).ConfigureAwait(false)));
				}
			}

			await Task.WhenAll(tasks).ConfigureAwait(false);
			await Bot.AcceptAllConfirmations().ConfigureAwait(false);
		}

		private async Task ParseTrade(SteamTradeOffer tradeOffer) {
			if (tradeOffer == null) {
				return;
			}

			ulong tradeID;
			if (!ulong.TryParse(tradeOffer.tradeofferid, out tradeID)) {
				return;
			}

			if (tradeOffer.items_to_give.Count == 0 || tradeOffer.OtherSteamID64 == Bot.SteamMasterID) {
				Logging.LogGenericInfo(Bot.BotName, "Accepting trade: " + tradeID);
				await Bot.ArchiWebHandler.AcceptTradeOffer(tradeID).ConfigureAwait(false);
			} else {
				Logging.LogGenericInfo(Bot.BotName, "Ignoring trade: " + tradeID);
			}
		}
	}
}
