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

using HtmlAgilityPack;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using System.Linq;

namespace ArchiSteamFarm {
	internal sealed class CardsFarmer {
		private const byte StatusCheckSleep = 5; // In minutes, how long to wait before checking the appID again
		private const ushort MaxFarmingTime = 600; // In minutes, how long ASF is allowed to farm one game in solo mode

		private readonly ManualResetEvent FarmResetEvent = new ManualResetEvent(false);
		private readonly SemaphoreSlim Semaphore = new SemaphoreSlim(1);

		private readonly Bot Bot;
		private readonly Timer Timer;

		internal readonly ConcurrentDictionary<uint, float> GamesToFarm = new ConcurrentDictionary<uint, float>();
		internal readonly List<uint> CurrentGamesFarming = new List<uint>();

		private bool ManualMode = false;
		private bool NowFarming = false;

		internal CardsFarmer(Bot bot) {
			Bot = bot;

			Timer = new Timer(
				async e => await CheckGamesForFarming().ConfigureAwait(false),
				null,
				TimeSpan.FromMinutes(15), // Delay
				TimeSpan.FromMinutes(15) // Period
			);
		}

		internal static List<uint> GetGamesToFarmSolo(ConcurrentDictionary<uint, float> gamesToFarm) {
			if (gamesToFarm == null) {
				return null;
			}

			List<uint> result = new List<uint>();
			foreach (KeyValuePair<uint, float> keyValue in gamesToFarm) {
				if (keyValue.Value >= 2) {
					result.Add(keyValue.Key);
				}
			}

			return result;
		}

		internal static uint GetAnyGameToFarm(ConcurrentDictionary<uint, float> gamesToFarm) {
			if (gamesToFarm == null) {
				return 0;
			}

			foreach (uint appID in gamesToFarm.Keys) {
				return appID;
			}

			return 0;
		}

		internal async Task SwitchToManualMode(bool manualMode) {
			if (ManualMode == manualMode) {
				return;
			}

			ManualMode = manualMode;

			if (ManualMode) {
				Logging.LogGenericInfo(Bot.BotName, "Now running in Manual Farming mode");
				await StopFarming().ConfigureAwait(false);
			} else {
				Logging.LogGenericInfo(Bot.BotName, "Now running in Automatic Farming mode");
				var start = Task.Run(async () => await StartFarming().ConfigureAwait(false));
			}
		}

		internal bool FarmMultiple(ConcurrentDictionary<uint, float> appIDs) {
			if (appIDs.Count == 0) {
				return true;
			}

			float maxHour = 0;

			foreach (float hour in appIDs.Values) {
				if (hour > maxHour) {
					maxHour = hour;
				}
			}

			CurrentGamesFarming.Clear();
			foreach (uint appID in appIDs.Keys) {
				CurrentGamesFarming.Add(appID);
			}

			Logging.LogGenericInfo(Bot.BotName, "Now farming: " + string.Join(", ", appIDs.Keys));
			if (Farm(maxHour, appIDs.Keys)) {
				CurrentGamesFarming.Clear();
				return true;
			} else {
				CurrentGamesFarming.Clear();
				return false;
			}
		}

		internal async Task<bool> FarmSolo(uint appID) {
			if (appID == 0) {
				return true;
			}

			CurrentGamesFarming.Clear();
			CurrentGamesFarming.Add(appID);

			Logging.LogGenericInfo(Bot.BotName, "Now farming: " + appID);
			if (await Farm(appID).ConfigureAwait(false)) {
				float hours;
				GamesToFarm.TryRemove(appID, out hours);
				return true;
			} else {
				CurrentGamesFarming.Clear();
				return false;
			}
		}

		internal async Task RestartFarming() {
			await StopFarming().ConfigureAwait(false);
			await StartFarming().ConfigureAwait(false);
		}

		internal async Task ForcedFarming(uint appID)
		{
			await StopFarming().ConfigureAwait(false);

			Logging.LogGenericInfo(Bot.BotName, "Forced farming for appid " + appID.ToString() + "!");
			NowFarming = true;
			if (await FarmSolo(appID).ConfigureAwait(false))
			{
				Logging.LogGenericInfo(Bot.BotName, "Done farming: " + appID);
			}
			else
			{
				NowFarming = false;
				return;
			}

			CurrentGamesFarming.Clear();
			NowFarming = false;
			await Bot.OnFarmingFinished().ConfigureAwait(false);
			Logging.LogGenericInfo(Bot.BotName, "Forced farming finished!");

			await StartFarming().ConfigureAwait(false);
		}

		private async Task<int> GetCardsNum(ulong appID)
		{
			if (appID == 0)
				return 0;

			HtmlDocument htmlDocument = await Bot.ArchiWebHandler.GetGameCardsPage(appID).ConfigureAwait(false);
			HtmlNode htmlNode = htmlDocument.DocumentNode.SelectSingleNode("//span[@class='progress_info_bold']");

			if (htmlNode != null)
			{
				int num;
				if (int.TryParse(Regex.Match(htmlNode.InnerText, @"[0-9]+").Value, out num))
					return num;
			}

			return 0;
		}

		internal async Task<List<uint>> SortGames(List<uint> gamesToFarm)
		{
			Dictionary<uint, float> result = new Dictionary<uint, float>();

			switch (Bot.SortingMethod)
			{
				case "avg_prices":
					string uri = string.Format("http://api.enhancedsteam.com/market_data/average_card_prices/im.php?appids={0}",
						 string.Join(",", gamesToFarm.ToArray()));

					var webClient = new System.Net.WebClient()
					{
						Encoding = System.Text.Encoding.UTF8
					};

					var response = webClient.DownloadString(uri);
					var json = Newtonsoft.Json.Linq.JObject.Parse(response);
					webClient.Dispose();

					var avgArray = json["avg_values"].Values<Newtonsoft.Json.Linq.JObject>();

					foreach (var avgValue in avgArray)
					{
						uint appID;
						if (!uint.TryParse((string)avgValue["appid"], out appID))
							continue;
						result.Add(appID, (float)avgValue["avg_price"]);
					}
					return (from item in result orderby item.Value descending select item.Key).ToList();
				case "cards_asc":
					foreach (uint appID in gamesToFarm)
					{
						result.Add(appID, await GetCardsNum(appID));
					}
					return (from item in result orderby item.Value ascending select item.Key).ToList();
				case "cards_desc":
					foreach (uint appID in gamesToFarm)
					{
						result.Add(appID, await GetCardsNum(appID));
					}
					return (from item in result orderby item.Value descending select item.Key).ToList();
				default:
					return gamesToFarm;
			}
		}

		internal async Task StartFarming() {
			await Semaphore.WaitAsync().ConfigureAwait(false);

			if (ManualMode) {
				Semaphore.Release(); // We have nothing to do, don't forget to release semaphore
				return;
			}

			if (!await IsAnythingToFarm().ConfigureAwait(false)) {
				Semaphore.Release(); // We have nothing to do, don't forget to release semaphore
				return;
			}

			Logging.LogGenericInfo(Bot.BotName, "We have a total of " + GamesToFarm.Count + " games to farm on this account...");
			NowFarming = true;
			Semaphore.Release(); // From this point we allow other calls to shut us down

			// Now the algorithm used for farming depends on whether account is restricted or not
			if (Bot.CardDropsRestricted) { // If we have restricted card drops, we use complex algorithm
				Logging.LogGenericInfo(Bot.BotName, "Chosen farming algorithm: Complex");
				while (GamesToFarm.Count > 0) {
					List<uint> gamesToFarmSolo = GetGamesToFarmSolo(GamesToFarm);
					if (gamesToFarmSolo.Count > 0) {
						gamesToFarmSolo = await SortGames(gamesToFarmSolo);
						while (gamesToFarmSolo.Count > 0) {
							uint appID = gamesToFarmSolo[0];
							if (await FarmSolo(appID).ConfigureAwait(false)) {
								Logging.LogGenericInfo(Bot.BotName, "Done farming: " + appID);
								gamesToFarmSolo.Remove(appID);
							} else {
								NowFarming = false;
								return;
							}
						}
					} else {
						if (FarmMultiple(GamesToFarm)) {
							Logging.LogGenericInfo(Bot.BotName, "Done farming: " + string.Join(", ", GamesToFarm.Keys));
						} else {
							NowFarming = false;
							return;
						}
					}
				}
			} else { // If we have unrestricted card drops, we use simple algorithm
				Logging.LogGenericInfo(Bot.BotName, "Chosen farming algorithm: Simple");
				List<uint> gamesToFarm = await SortGames(GamesToFarm.Keys.ToList());
				while (gamesToFarm.Count > 0) {
					uint appID = gamesToFarm[0];
					if (await FarmSolo(appID).ConfigureAwait(false)) {
						Logging.LogGenericInfo(Bot.BotName, "Done farming: " + appID);
						gamesToFarm.Remove(appID);
					} else {
						NowFarming = false;
						return;
					}
				}
			}

			CurrentGamesFarming.Clear();
			NowFarming = false;
			Logging.LogGenericInfo(Bot.BotName, "Farming finished!");
			await Bot.OnFarmingFinished().ConfigureAwait(false);
		}

		internal async Task StopFarming() {
			await Semaphore.WaitAsync().ConfigureAwait(false);

			if (!NowFarming) {
				Semaphore.Release();
				return;
			}

			Logging.LogGenericInfo(Bot.BotName, "Sending signal to stop farming");
			FarmResetEvent.Set();
			for (byte i = 0; i < 5 && NowFarming; i++) {
				Logging.LogGenericInfo(Bot.BotName, "Waiting for reaction...");
				await Utilities.SleepAsync(1000).ConfigureAwait(false);
			}
			FarmResetEvent.Reset();
			Logging.LogGenericInfo(Bot.BotName, "Farming stopped!");
			Semaphore.Release();
		}

		private async Task<bool> IsAnythingToFarm() {
			if (NowFarming) {
				return false;
			}

			if (await Bot.ArchiWebHandler.ReconnectIfNeeded().ConfigureAwait(false)) {
				return false;
			}

			Logging.LogGenericInfo(Bot.BotName, "Checking badges...");

			// Find the number of badge pages
			Logging.LogGenericInfo(Bot.BotName, "Checking first page...");
			HtmlDocument htmlDocument = await Bot.ArchiWebHandler.GetBadgePage(1).ConfigureAwait(false);
			if (htmlDocument == null) {
				Logging.LogGenericWarning(Bot.BotName, "Could not get badges information, will try again later!");
				return false;
			}

			byte maxPages = 1;
			HtmlNodeCollection htmlNodeCollection = htmlDocument.DocumentNode.SelectNodes("//a[@class='pagelink']");
			if (htmlNodeCollection != null && htmlNodeCollection.Count > 0) {
				HtmlNode htmlNode = htmlNodeCollection[htmlNodeCollection.Count - 1];
				if (!byte.TryParse(htmlNode.InnerText, out maxPages)) {
					maxPages = 1; // Should never happen
				}
			}

			GamesToFarm.Clear();

			// Find APPIDs we need to farm
			Logging.LogGenericInfo(Bot.BotName, "Checking other pages...");

			List<Task> checkPagesTasks = new List<Task>();
			for (byte page = 1; page <= maxPages; page++) {
				if (page == 1) {
					CheckPage(htmlDocument); // Because we fetched page number 1 already
				} else {
					checkPagesTasks.Add(Task.Run(async () => await CheckPage(page).ConfigureAwait(false)));
				}
			}
			await Task.WhenAll(checkPagesTasks).ConfigureAwait(false);

			if (GamesToFarm.Count == 0) {
				return true;
			}

			// If we have restricted card drops, actually do check hours of all games that are left to farm
			if (Bot.CardDropsRestricted) {
				List<Task> checkHoursTasks = new List<Task>();
				Logging.LogGenericInfo(Bot.BotName, "Checking hours...");
				foreach (uint appID in GamesToFarm.Keys) {
					checkHoursTasks.Add(Task.Run(async () => await CheckHours(appID).ConfigureAwait(false)));
				}
				await Task.WhenAll(checkHoursTasks).ConfigureAwait(false);
			}

			return true;
		}

		private void CheckPage(HtmlDocument htmlDocument) {
			if (htmlDocument == null) {
				return;
			}

			HtmlNodeCollection htmlNodeCollection = htmlDocument.DocumentNode.SelectNodes("//a[@class='btn_green_white_innerfade btn_small_thin']");
			if (htmlNodeCollection == null) {
				return;
			}

			foreach (HtmlNode htmlNode in htmlNodeCollection) {
				string steamLink = htmlNode.GetAttributeValue("href", null);
				if (steamLink == null) {
					continue;
				}

				uint appID = (uint) Utilities.OnlyNumbers(steamLink);
				if (appID == 0) {
					continue;
				}

				if (Bot.GlobalBlacklist.Contains(appID) || Bot.Blacklist.Contains(appID)) {
					continue;
				}

				// We assume that every game has at least 2 hours played, until we actually check them
				GamesToFarm[appID] = 2;
			}
		}

		private async Task CheckPage(byte page) {
			if (page == 0) {
				return;
			}

			HtmlDocument htmlDocument = await Bot.ArchiWebHandler.GetBadgePage(page).ConfigureAwait(false);
			if (htmlDocument == null) {
				return;
			}

			CheckPage(htmlDocument);
		}

		private async Task CheckHours(uint appID) {
			if (appID == 0) {
				return;
			}

			HtmlDocument htmlDocument = await Bot.ArchiWebHandler.GetGameCardsPage(appID).ConfigureAwait(false);
			if (htmlDocument == null) {
				Logging.LogNullError(Bot.BotName, "htmlDocument");
				return;
			}

			HtmlNode htmlNode = htmlDocument.DocumentNode.SelectSingleNode("//div[@class='badge_title_stats_playtime']");
			if (htmlNode == null) {
				Logging.LogNullError(Bot.BotName, "htmlNode");
				return;
			}

			string hoursString = htmlNode.InnerText;
			if (string.IsNullOrEmpty(hoursString)) {
				Logging.LogNullError(Bot.BotName, "hoursString");
				return;
			}

			hoursString = Regex.Match(hoursString, @"[0-9\.,]+").Value;
			float hours;

			if (string.IsNullOrEmpty(hoursString)) {
				hours = 0;
			} else {
				hours = float.Parse(hoursString, CultureInfo.InvariantCulture);
			}

			GamesToFarm[appID] = hours;
		}

		private async Task CheckGamesForFarming() {
			if (NowFarming || GamesToFarm.Count > 0 || !Bot.SteamClient.IsConnected) {
				return;
			}

			await StartFarming().ConfigureAwait(false);
		}

		private async Task<bool?> ShouldFarm(ulong appID) {
			if (appID == 0) {
				return false;
			}

			HtmlDocument htmlDocument = await Bot.ArchiWebHandler.GetGameCardsPage(appID).ConfigureAwait(false);
			if (htmlDocument == null) {
				return null;
			}

			HtmlNode htmlNode = htmlDocument.DocumentNode.SelectSingleNode("//span[@class='progress_info_bold']");
			if (htmlNode == null) {
				await Bot.ArchiWebHandler.ReconnectIfNeeded().ConfigureAwait(false);
				return null;
			}

			return !htmlNode.InnerText.Contains("No card drops");
		}

		private async Task<bool> Farm(uint appID) {
			Bot.ArchiHandler.PlayGames(appID);

			bool success = true;

			bool? keepFarming = await ShouldFarm(appID).ConfigureAwait(false);
			for (ushort farmingTime = 0; farmingTime <= MaxFarmingTime && (!keepFarming.HasValue || keepFarming.Value); farmingTime += StatusCheckSleep) {
				Logging.LogGenericInfo(Bot.BotName, "Still farming: " + appID);
				if (FarmResetEvent.WaitOne(1000 * 60 * StatusCheckSleep)) {
					success = false;
					break;
				}
				keepFarming = await ShouldFarm(appID).ConfigureAwait(false);
			}

			Bot.ArchiHandler.PlayGames(0);
			Logging.LogGenericInfo(Bot.BotName, "Stopped farming: " + appID);
			return success;
		}

		private bool Farm(float maxHour, ICollection<uint> appIDs) {
			if (maxHour >= 2) {
				return true;
			}

			Bot.ArchiHandler.PlayGames(appIDs);

			bool success = true;
			while (maxHour < 2) {
				Logging.LogGenericInfo(Bot.BotName, "Still farming: " + string.Join(", ", appIDs));
				if (FarmResetEvent.WaitOne(1000 * 60 * StatusCheckSleep)) {
					success = false;
					break;
				}

				// Don't forget to update our GamesToFarm hours
				float timePlayed = StatusCheckSleep / 60.0F;
				foreach (KeyValuePair<uint, float> gameToFarm in GamesToFarm) {
					if (!appIDs.Contains(gameToFarm.Key)) {
						continue;
					}

					GamesToFarm[gameToFarm.Key] = gameToFarm.Value + timePlayed;
				}

				maxHour += timePlayed;
			}

			Bot.ArchiHandler.PlayGames(0);
			Logging.LogGenericInfo(Bot.BotName, "Stopped farming: " + string.Join(", ", appIDs));
			return success;
		}
	}
}
