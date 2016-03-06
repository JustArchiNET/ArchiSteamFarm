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

namespace ArchiSteamFarm {
	internal sealed class CardsFarmer {
		private const byte StatusCheckSleep = 5; // In minutes, how long to wait before checking the appID again
		private const ushort MaxFarmingTime = 600; // In minutes, how long ASF is allowed to farm one game in solo mode

		internal readonly ConcurrentDictionary<uint, float> GamesToFarm = new ConcurrentDictionary<uint, float>();
		internal readonly List<uint> CurrentGamesFarming = new List<uint>();

		private readonly ManualResetEvent FarmResetEvent = new ManualResetEvent(false);
		private readonly SemaphoreSlim Semaphore = new SemaphoreSlim(1);

		private readonly Bot Bot;
		private readonly Timer Timer;

		private bool ManualMode = false;
		private bool NowFarming = false;

		internal CardsFarmer(Bot bot) {
			Bot = bot;

			if (Timer == null) {
				Timer = new Timer(
					async e => await CheckGamesForFarming().ConfigureAwait(false),
					null,
					TimeSpan.FromMinutes(15), // Delay
					TimeSpan.FromMinutes(60) // Period
				);
			}
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

		internal async Task<bool> SwitchToManualMode(bool manualMode) {
			if (ManualMode == manualMode) {
				return false;
			}

			ManualMode = manualMode;

			if (ManualMode) {
				Logging.LogGenericInfo("Now running in Manual Farming mode", Bot.BotName);
				await StopFarming().ConfigureAwait(false);
			} else {
				Logging.LogGenericInfo("Now running in Automatic Farming mode", Bot.BotName);
				Task.Run(async () => await StartFarming().ConfigureAwait(false)).Forget();
			}

			return true;
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
			CurrentGamesFarming.TrimExcess();
			foreach (uint appID in appIDs.Keys) {
				CurrentGamesFarming.Add(appID);
			}

			Logging.LogGenericInfo("Now farming: " + string.Join(", ", appIDs.Keys), Bot.BotName);
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
			CurrentGamesFarming.TrimExcess();
			CurrentGamesFarming.Add(appID);

			Logging.LogGenericInfo("Now farming: " + appID, Bot.BotName);
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

			Logging.LogGenericInfo("We have a total of " + GamesToFarm.Count + " games to farm on this account...", Bot.BotName);
			NowFarming = true;
			Semaphore.Release(); // From this point we allow other calls to shut us down

			bool farmedSomething = false;

			// Now the algorithm used for farming depends on whether account is restricted or not
			if (Bot.BotConfig.CardDropsRestricted) { // If we have restricted card drops, we use complex algorithm
				Logging.LogGenericInfo("Chosen farming algorithm: Complex", Bot.BotName);
				while (GamesToFarm.Count > 0) {
					List<uint> gamesToFarmSolo = GetGamesToFarmSolo(GamesToFarm);
					if (gamesToFarmSolo.Count > 0) {
						while (gamesToFarmSolo.Count > 0) {
							uint appID = gamesToFarmSolo[0];
							if (await FarmSolo(appID).ConfigureAwait(false)) {
								farmedSomething = true;
								Logging.LogGenericInfo("Done farming: " + appID, Bot.BotName);
								gamesToFarmSolo.Remove(appID);
								gamesToFarmSolo.TrimExcess();
							} else {
								NowFarming = false;
								return;
							}
						}
					} else {
						if (FarmMultiple(GamesToFarm)) {
							farmedSomething = true;
							Logging.LogGenericInfo("Done farming: " + string.Join(", ", GamesToFarm.Keys), Bot.BotName);
						} else {
							NowFarming = false;
							return;
						}
					}
				}
			} else { // If we have unrestricted card drops, we use simple algorithm
				Logging.LogGenericInfo("Chosen farming algorithm: Simple", Bot.BotName);
				while (GamesToFarm.Count > 0) {
					uint appID = GetAnyGameToFarm(GamesToFarm);
					if (await FarmSolo(appID).ConfigureAwait(false)) {
						farmedSomething = true;
						Logging.LogGenericInfo("Done farming: " + appID, Bot.BotName);
					} else {
						NowFarming = false;
						return;
					}
				}
			}

			CurrentGamesFarming.Clear();
			CurrentGamesFarming.TrimExcess();
			NowFarming = false;
			Logging.LogGenericInfo("Farming finished!", Bot.BotName);
			await Bot.OnFarmingFinished(farmedSomething).ConfigureAwait(false);
		}

		internal async Task StopFarming() {
			await Semaphore.WaitAsync().ConfigureAwait(false);

			if (!NowFarming) {
				Semaphore.Release();
				return;
			}

			Logging.LogGenericInfo("Sending signal to stop farming", Bot.BotName);
			FarmResetEvent.Set();
			for (byte i = 0; i < 5 && NowFarming; i++) {
				Logging.LogGenericInfo("Waiting for reaction...", Bot.BotName);
				await Utilities.SleepAsync(1000).ConfigureAwait(false);
			}
			FarmResetEvent.Reset();
			Logging.LogGenericInfo("Farming stopped!", Bot.BotName);
			Semaphore.Release();
		}

		private async Task<bool> IsAnythingToFarm() {
			if (NowFarming) {
				return false;
			}

			if (await Bot.ArchiWebHandler.ReconnectIfNeeded().ConfigureAwait(false)) {
				return false;
			}

			Logging.LogGenericInfo("Checking badges...", Bot.BotName);

			// Find the number of badge pages
			Logging.LogGenericInfo("Checking first page...", Bot.BotName);
			HtmlDocument htmlDocument = await Bot.ArchiWebHandler.GetBadgePage(1).ConfigureAwait(false);
			if (htmlDocument == null) {
				Logging.LogGenericWarning("Could not get badges information, will try again later!", Bot.BotName);
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
			Logging.LogGenericInfo("Checking other pages...", Bot.BotName);

			List<Task> tasks = new List<Task>(maxPages - 1);
			for (byte page = 1; page <= maxPages; page++) {
				if (page == 1) {
					CheckPage(htmlDocument); // Because we fetched page number 1 already
				} else {
					byte currentPage = page; // We need a copy of variable being passed when in for loops
					tasks.Add(Task.Run(async () => await CheckPage(currentPage).ConfigureAwait(false)));
				}
			}
			await Task.WhenAll(tasks).ConfigureAwait(false);

			if (GamesToFarm.Count == 0) {
				return true;
			}

			// If we have restricted card drops, actually do check hours of all games that are left to farm
			if (Bot.BotConfig.CardDropsRestricted) {
				tasks = new List<Task>(GamesToFarm.Keys.Count);
				Logging.LogGenericInfo("Checking hours...", Bot.BotName);
				foreach (uint appID in GamesToFarm.Keys) {
					tasks.Add(Task.Run(async () => await CheckHours(appID).ConfigureAwait(false)));
				}
				await Task.WhenAll(tasks).ConfigureAwait(false);
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

				if (GlobalConfig.GlobalBlacklist.Contains(appID) || Program.GlobalConfig.Blacklist.Contains(appID)) {
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
				Logging.LogNullError("htmlDocument", Bot.BotName);
				return;
			}

			HtmlNode htmlNode = htmlDocument.DocumentNode.SelectSingleNode("//div[@class='badge_title_stats_playtime']");
			if (htmlNode == null) {
				Logging.LogNullError("htmlNode", Bot.BotName);
				return;
			}

			string hoursString = htmlNode.InnerText;
			if (string.IsNullOrEmpty(hoursString)) {
				Logging.LogNullError("hoursString", Bot.BotName);
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
			if (NowFarming || ManualMode || GamesToFarm.Count > 0 || !Bot.SteamClient.IsConnected) {
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
				Logging.LogGenericInfo("Still farming: " + appID, Bot.BotName);
				if (FarmResetEvent.WaitOne(1000 * 60 * StatusCheckSleep)) {
					success = false;
					break;
				}
				keepFarming = await ShouldFarm(appID).ConfigureAwait(false);
			}

			Bot.ResetGamesPlayed();
			Logging.LogGenericInfo("Stopped farming: " + appID, Bot.BotName);
			return success;
		}

		private bool Farm(float maxHour, ICollection<uint> appIDs) {
			if (maxHour >= 2) {
				return true;
			}

			Bot.ArchiHandler.PlayGames(appIDs);

			bool success = true;
			while (maxHour < 2) {
				Logging.LogGenericInfo("Still farming: " + string.Join(", ", appIDs), Bot.BotName);
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

			Bot.ResetGamesPlayed();
			Logging.LogGenericInfo("Stopped farming: " + string.Join(", ", appIDs), Bot.BotName);
			return success;
		}
	}
}
