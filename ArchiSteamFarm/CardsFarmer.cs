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
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ArchiSteamFarm {
	internal sealed class CardsFarmer {
		internal readonly ConcurrentDictionary<uint, float> GamesToFarm = new ConcurrentDictionary<uint, float>();
		internal readonly HashSet<uint> CurrentGamesFarming = new HashSet<uint>();

		private readonly ManualResetEvent FarmResetEvent = new ManualResetEvent(false);
		private readonly SemaphoreSlim Semaphore = new SemaphoreSlim(1);
		private readonly Bot Bot;
		private readonly Timer Timer;

		private volatile bool ManualMode, NowFarming;

		internal CardsFarmer(Bot bot) {
			if (bot == null) {
				return;
			}

			Bot = bot;

			if (Program.GlobalConfig.IdleFarmingPeriod > 0) {
				Timer = new Timer(
					async e => await CheckGamesForFarming().ConfigureAwait(false),
					null,
					TimeSpan.FromHours(Program.GlobalConfig.IdleFarmingPeriod), // Delay
					TimeSpan.FromHours(Program.GlobalConfig.IdleFarmingPeriod) // Period
				);
			}
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
				StartFarming().Forget();
			}

			return true;
		}

		internal async Task StartFarming() {
			if (NowFarming || ManualMode) {
				return;
			}

			await Semaphore.WaitAsync().ConfigureAwait(false);

			if (NowFarming || ManualMode) {
				Semaphore.Release(); // We have nothing to do, don't forget to release semaphore
				return;
			}

			if (!await IsAnythingToFarm().ConfigureAwait(false)) {
				Semaphore.Release(); // We have nothing to do, don't forget to release semaphore
				Logging.LogGenericInfo("We don't have anything to farm on this account!", Bot.BotName);
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
					HashSet<uint> gamesToFarmSolo = GetGamesToFarmSolo(GamesToFarm);
					if (gamesToFarmSolo.Count > 0) {
						while (gamesToFarmSolo.Count > 0) {
							uint appID = gamesToFarmSolo.First();
							if (await FarmSolo(appID).ConfigureAwait(false)) {
								farmedSomething = true;
								gamesToFarmSolo.Remove(appID);
								gamesToFarmSolo.TrimExcess();
							} else {
								NowFarming = false;
								return;
							}
						}
					} else {
						if (FarmMultiple()) {
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
					uint appID = GamesToFarm.Keys.FirstOrDefault();
					if (await FarmSolo(appID).ConfigureAwait(false)) {
						farmedSomething = true;
					} else {
						NowFarming = false;
						return;
					}
				}
			}

			CurrentGamesFarming.Clear();
			CurrentGamesFarming.TrimExcess();
			NowFarming = false;

			// We finished our queue for now, make sure that everything is indeed farmed before proceeding further
			// Some games could be added in the meantime
			if (await IsAnythingToFarm().ConfigureAwait(false)) {
				StartFarming().Forget();
				return;
			}

			Logging.LogGenericInfo("Farming finished!", Bot.BotName);
			await Bot.OnFarmingFinished(farmedSomething).ConfigureAwait(false);
		}

		internal async Task StopFarming() {
			if (!NowFarming) {
				return;
			}

			await Semaphore.WaitAsync().ConfigureAwait(false);

			if (!NowFarming) {
				Semaphore.Release();
				return;
			}

			Logging.LogGenericInfo("Sending signal to stop farming", Bot.BotName);
			FarmResetEvent.Set();

			Logging.LogGenericInfo("Waiting for reaction...", Bot.BotName);
			for (byte i = 0; i < Program.GlobalConfig.HttpTimeout && NowFarming; i++) {
				await Utilities.SleepAsync(1000).ConfigureAwait(false);
			}

			if (NowFarming) {
				Logging.LogGenericWarning("Timed out!", Bot.BotName);
			}

			FarmResetEvent.Reset();
			Logging.LogGenericInfo("Farming stopped!", Bot.BotName);
			Semaphore.Release();
		}

		internal async Task RestartFarming() {
			await StopFarming().ConfigureAwait(false);
			await StartFarming().ConfigureAwait(false);
		}

		private static HashSet<uint> GetGamesToFarmSolo(ConcurrentDictionary<uint, float> gamesToFarm) {
			if (gamesToFarm == null) {
				return null;
			}

			HashSet<uint> result = new HashSet<uint>();
			foreach (KeyValuePair<uint, float> keyValue in gamesToFarm) {
				if (keyValue.Value >= 2) {
					result.Add(keyValue.Key);
				}
			}

			return result;
		}

		private async Task<bool> IsAnythingToFarm() {
			if (NowFarming) {
				return true;
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
					tasks.Add(CheckPage(currentPage));
				}
			}
			await Task.WhenAll(tasks).ConfigureAwait(false);

			if (GamesToFarm.Count == 0) {
				return false;
			}

			return true;
		}

		private void CheckPage(HtmlDocument htmlDocument) {
			if (htmlDocument == null) {
				return;
			}

			HtmlNodeCollection htmlNodes = htmlDocument.DocumentNode.SelectNodes("//div[@class='badge_title_stats']");
			if (htmlNodes == null) {
				return;
			}

			foreach (HtmlNode htmlNode in htmlNodes) {
				HtmlNode farmingNode = htmlNode.SelectSingleNode(".//a[@class='btn_green_white_innerfade btn_small_thin']");
				if (farmingNode == null) {
					continue; // This game is not needed for farming
				}

				string steamLink = farmingNode.GetAttributeValue("href", null);
				if (string.IsNullOrEmpty(steamLink)) {
					Logging.LogNullError("steamLink", Bot.BotName);
					continue;
				}

				uint appID = (uint) Utilities.OnlyNumbers(steamLink);
				if (appID == 0) {
					Logging.LogNullError("appID", Bot.BotName);
					continue;
				}

				if (GlobalConfig.GlobalBlacklist.Contains(appID) || Program.GlobalConfig.Blacklist.Contains(appID)) {
					continue;
				}

				HtmlNode timeNode = htmlNode.SelectSingleNode(".//div[@class='badge_title_stats_playtime']");
				if (timeNode == null) {
					Logging.LogNullError("timeNode", Bot.BotName);
					continue;
				}

				string hoursString = timeNode.InnerText;
				if (string.IsNullOrEmpty(hoursString)) {
					Logging.LogNullError("hoursString", Bot.BotName);
					continue;
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

		private async Task CheckGamesForFarming() {
			if (NowFarming || ManualMode || !Bot.SteamClient.IsConnected) {
				return;
			}

			await StartFarming().ConfigureAwait(false);
		}

		private async Task<bool?> ShouldFarm(uint appID) {
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

		private bool FarmMultiple() {
			if (GamesToFarm.Count == 0) {
				return true;
			}

			float maxHour = 0;
			foreach (KeyValuePair<uint, float> game in GamesToFarm) {
				CurrentGamesFarming.Add(game.Key);
				if (game.Value > maxHour) {
					maxHour = game.Value;
				}
			}

			if (maxHour >= 2) {
				CurrentGamesFarming.Clear();
				CurrentGamesFarming.TrimExcess();
				return true;
			}

			Logging.LogGenericInfo("Now farming: " + string.Join(", ", CurrentGamesFarming), Bot.BotName);
			if (FarmHours(maxHour, CurrentGamesFarming)) {
				CurrentGamesFarming.Clear();
				CurrentGamesFarming.TrimExcess();
				return true;
			} else {
				CurrentGamesFarming.Clear();
				CurrentGamesFarming.TrimExcess();
				return false;
			}
		}

		private async Task<bool> FarmSolo(uint appID) {
			if (appID == 0) {
				return true;
			}

			CurrentGamesFarming.Add(appID);

			Logging.LogGenericInfo("Now farming: " + appID, Bot.BotName);
			if (await Farm(appID).ConfigureAwait(false)) {
				CurrentGamesFarming.Clear();
				CurrentGamesFarming.TrimExcess();
				float hours;
				if (GamesToFarm.TryRemove(appID, out hours)) {
					TimeSpan timeSpan = TimeSpan.FromHours(hours);
					Logging.LogGenericInfo("Done farming: " + appID + " after " + timeSpan.ToString(@"hh\:mm") + " hours of playtime!", Bot.BotName);
				}
				return true;
			} else {
				CurrentGamesFarming.Clear();
				CurrentGamesFarming.TrimExcess();
				return false;
			}
		}

		private async Task<bool> Farm(uint appID) {
			if (appID == 0) {
				return false;
			}

			Bot.ArchiHandler.PlayGames(appID);

			bool success = true;

			bool? keepFarming = await ShouldFarm(appID).ConfigureAwait(false);
			for (ushort farmingTime = 0; farmingTime <= 60 * Program.GlobalConfig.MaxFarmingTime && keepFarming.GetValueOrDefault(true); farmingTime += Program.GlobalConfig.FarmingDelay) {
				if (FarmResetEvent.WaitOne(60 * 1000 * Program.GlobalConfig.FarmingDelay)) {
					success = false;
					break;
				}

				// Don't forget to update our GamesToFarm hours
				float timePlayed = Program.GlobalConfig.FarmingDelay / 60.0F;
				GamesToFarm[appID] += timePlayed;

				keepFarming = await ShouldFarm(appID).ConfigureAwait(false);
				Logging.LogGenericInfo("Still farming: " + appID, Bot.BotName);
			}

			Bot.ResetGamesPlayed();
			Logging.LogGenericInfo("Stopped farming: " + appID, Bot.BotName);
			return success;
		}

		private bool FarmHours(float maxHour, HashSet<uint> appIDs) {
			if (maxHour < 0 || appIDs == null || appIDs.Count == 0) {
				return false;
			}

			if (maxHour >= 2) {
				return true;
			}

			Bot.ArchiHandler.PlayGames(appIDs);

			bool success = true;
			while (maxHour < 2) {
				if (FarmResetEvent.WaitOne(60 * 1000 * Program.GlobalConfig.FarmingDelay)) {
					success = false;
					break;
				}

				// Don't forget to update our GamesToFarm hours
				float timePlayed = Program.GlobalConfig.FarmingDelay / 60.0F;
				foreach (uint appID in appIDs) {
					GamesToFarm[appID] += timePlayed;
				}

				maxHour += timePlayed;
				Logging.LogGenericInfo("Still farming: " + string.Join(", ", appIDs), Bot.BotName);
			}

			Bot.ResetGamesPlayed();
			Logging.LogGenericInfo("Stopped farming: " + string.Join(", ", appIDs), Bot.BotName);
			return success;
		}
	}
}
