using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ArchiSteamFarm {
	internal sealed class Trading {
		private Bot Bot;
		private volatile byte ParsingTasks = 0;
		private SemaphoreSlim semaphore = new SemaphoreSlim(1);

		internal Trading(Bot bot) {
			Bot = bot;
		}

		internal void CheckTrades() {
			if (ParsingTasks < 2) {
				ParsingTasks++;
				Task.Run(() => ParseActiveTrades());
			}
		}

		private async Task ParseActiveTrades() {
			await semaphore.WaitAsync().ConfigureAwait(false);

			List<SteamTradeOffer> tradeOffers = Bot.ArchiWebHandler.GetTradeOffers();
			if (tradeOffers != null) {
				List<Task> tasks = new List<Task>();
				foreach (SteamTradeOffer tradeOffer in tradeOffers) {
					if (tradeOffer.trade_offer_state == SteamTradeOffer.ETradeOfferState.Active) {
						Task task = Task.Run(async () => {
							await ParseTrade(tradeOffer).ConfigureAwait(false);
						});
						tasks.Add(task);
					}
				}

				await Task.WhenAll(tasks).ConfigureAwait(false);
			}

			ParsingTasks--;
			semaphore.Release();
		}

		private async Task ParseTrade(SteamTradeOffer tradeOffer) {
			if (tradeOffer == null) {
				return;
			}

			ulong tradeID;
			if (!ulong.TryParse(tradeOffer.tradeofferid, out tradeID)) {
				return;
			}

			ulong steamID = tradeOffer.OtherSteamID64;
			bool success = false;
			bool tradeAccepted = false;

			if (tradeOffer.items_to_give.Count == 0 || steamID == Bot.SteamMasterID) {
				tradeAccepted = true;
				success = await Bot.ArchiWebHandler.AcceptTradeOffer(tradeID).ConfigureAwait(false);
			} else {
				success = Bot.ArchiWebHandler.DeclineTradeOffer(tradeID);
			}

			if (!success) {
				Logging.LogGenericWarning(Bot.BotName, "Response to trade " + tradeID + " failed!");
			}

			if (tradeAccepted && success) {
				// Do whatever we want with success
			}
		}
	}
}
