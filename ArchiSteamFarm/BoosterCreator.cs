using System;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using ArchiSteamFarm.Json;
using System.Collections.Generic;
using System.Linq;
using SteamKit2;

namespace ArchiSteamFarm {
	internal sealed class BoosterCreator : IDisposable {
		internal uint GooAmount { get; private set; }
		internal uint TradableGooAmount { get; private set; }
		internal uint UnTradableGooAmount { get; private set; }

		private readonly Bot Bot;
		private readonly Timer BoosterPackTimer;

		internal BoosterCreator(Bot bot) {
			Bot = bot ?? throw new ArgumentNullException(nameof(bot));

			BoosterPackTimer = new Timer(
				async e => await AutoCreateBooster().ConfigureAwait(false),
				null,
				TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(Program.LoadBalancingDelay * Bot.Bots.Count), // Delay
				TimeSpan.FromHours(8.1) // Period
			);
		}

		public void Dispose() => BoosterPackTimer.Dispose();

		private async Task AutoCreateBooster() {
			if (!Bot.IsConnectedAndLoggedOn) {
				return;
			}

			Dictionary<uint, Steam.BoosterPack> boosterInfos = await GetBoosterInfo().ConfigureAwait(false);

			if (boosterInfos == null) {
				return;
			}

			uint created = 0, used = 0;

			foreach (uint gameID in Bot.BotConfig.GamesToBooster) {
				await Task.Delay(500).ConfigureAwait(false);

				if (!boosterInfos.ContainsKey(gameID)) {
					Bot.ArchiLogger.LogGenericInfo($"ID: {gameID} | Status: NotEligible");

					continue;
				}

				Steam.BoosterPack boosterPack = boosterInfos[gameID];

				if (GooAmount < boosterPack.Price) {
					Bot.ArchiLogger.LogGenericInfo($"ID: {boosterPack.AppID} | Status: NotEnoughGems");

					continue;
				}

				if (boosterPack.Unavailable) {
					Bot.ArchiLogger.LogGenericInfo($"ID: {boosterPack.AppID} | Status: Available at {boosterPack.AvailableAtTime}");

					continue;
				}

				uint nTp;

				if (UnTradableGooAmount > 0) {
					nTp = TradableGooAmount > boosterPack.Price ? (uint)1 : 3;
				}
				else {
					nTp = 2;
				}

				Steam.BoosterResponse boosterResponse = await Bot.ArchiWebHandler.CreateBooster(boosterPack.AppID, boosterPack.Series, nTp).ConfigureAwait(false);

				if (boosterResponse?.PurchaseResultDetail?.Result != EResult.OK) {
					continue;
				}

				used += boosterPack.Price;
				created++;
				GooAmount = boosterResponse.GooAmount;
				TradableGooAmount = boosterResponse.TradableGooAmount;
				UnTradableGooAmount = boosterResponse.UntradableGooAmount;
				Bot.ArchiLogger.LogGenericInfo($"ID: {boosterPack.AppID} | Status: OK | Items: {boosterPack.Name}");
			}

			if (created > 0) {
				Bot.ArchiLogger.LogGenericInfo($"BoosterPack: {created} | Gems Used: {used} | Gems Remain: {GooAmount}");
			}
		}

		internal async Task<Dictionary<uint, Steam.BoosterPack>> GetBoosterInfo() {
			HtmlDocument htmlDocument = await Bot.ArchiWebHandler.GetBoosterCreatorPage().ConfigureAwait(false);

			if (htmlDocument == null) {
				Bot.ArchiLogger.LogNullError(nameof(htmlDocument));

				return null;
			}

			MatchCollection gooAmount = Regex.Matches(htmlDocument.Text, "(?<=parseFloat\\( \")[\\d]+");
			Match info = Regex.Match(htmlDocument.Text, "\\[\\{\"[\\s\\S]*\"}]");

			if (!info.Success || (gooAmount.Count != 3)) {
				return null;
			}

			GooAmount = uint.Parse(gooAmount[0].Value);
			TradableGooAmount = uint.Parse(gooAmount[1].Value);
			UnTradableGooAmount = uint.Parse(gooAmount[2].Value);

			return JsonConvert.DeserializeObject<IEnumerable<Steam.BoosterPack>>(info.Value).ToDictionary(boosterInfo => boosterInfo.AppID);
		}
	}
}
