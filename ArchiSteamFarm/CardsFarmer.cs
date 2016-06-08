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
		internal readonly ConcurrentHashSet<uint> CurrentGamesFarming = new ConcurrentHashSet<uint>();

		private readonly ManualResetEventSlim FarmResetEvent = new ManualResetEventSlim(false);
		private readonly SemaphoreSlim FarmingSemaphore = new SemaphoreSlim(1);
		private readonly Bot Bot;
		private readonly Timer Timer;

		internal bool ManualMode { get; private set; }

		private bool KeepFarming, NowFarming;

		internal CardsFarmer(Bot bot) {
			if (bot == null) {
				throw new ArgumentNullException(nameof(bot));
			}

			Bot = bot;

			if ((Timer == null) && (Program.GlobalConfig.IdleFarmingPeriod > 0)) {
				Timer = new Timer(
					async e => await CheckGamesForFarming().ConfigureAwait(false),
					null,
					TimeSpan.FromHours(Program.GlobalConfig.IdleFarmingPeriod), // Delay
					TimeSpan.FromHours(Program.GlobalConfig.IdleFarmingPeriod) // Period
				);
			}
		}

		internal async Task SwitchToManualMode(bool manualMode) {
			if (ManualMode == manualMode) {
				return;
			}

			ManualMode = manualMode;

			if (ManualMode) {
				Logging.LogGenericInfo("Now running in Manual Farming mode", Bot.BotName);
				await StopFarming().ConfigureAwait(false);
			} else {
				Logging.LogGenericInfo("Now running in Automatic Farming mode", Bot.BotName);
				StartFarming().Forget();
			}
		}

		internal async Task StartFarming() {
			if (NowFarming || ManualMode || Bot.PlayingBlocked) {
				return;
			}

			await FarmingSemaphore.WaitAsync().ConfigureAwait(false);

			if (NowFarming || ManualMode || Bot.PlayingBlocked) {
				FarmingSemaphore.Release(); // We have nothing to do, don't forget to release semaphore
				return;
			}

			if (!await IsAnythingToFarm().ConfigureAwait(false)) {
				FarmingSemaphore.Release(); // We have nothing to do, don't forget to release semaphore
				Logging.LogGenericInfo("We don't have anything to farm on this account!", Bot.BotName);
				await Bot.OnFarmingFinished(false).ConfigureAwait(false);
				return;
			}

			Logging.LogGenericInfo("We have a total of " + GamesToFarm.Count + " games to farm on this account...", Bot.BotName);

			// This is the last moment for final check if we can farm
			if (Bot.PlayingBlocked) {
				Logging.LogGenericInfo("But account is currently occupied, so farming is stopped!");
				FarmingSemaphore.Release(); // We have nothing to do, don't forget to release semaphore
				return;
			}

			KeepFarming = NowFarming = true;
			FarmingSemaphore.Release(); // From this point we allow other calls to shut us down

			do {
				// Now the algorithm used for farming depends on whether account is restricted or not
				if (Bot.BotConfig.CardDropsRestricted) { // If we have restricted card drops, we use complex algorithm
					Logging.LogGenericInfo("Chosen farming algorithm: Complex", Bot.BotName);
					while (GamesToFarm.Count > 0) {
						HashSet<uint> gamesToFarmSolo = GetGamesToFarmSolo(GamesToFarm);
						if (gamesToFarmSolo.Count > 0) {
							while (gamesToFarmSolo.Count > 0) {
								uint appID = gamesToFarmSolo.First();
								if (await FarmSolo(appID).ConfigureAwait(false)) {
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
							continue;
						}

						NowFarming = false;
						return;
					}
				}
			} while (await IsAnythingToFarm().ConfigureAwait(false));

			CurrentGamesFarming.ClearAndTrim();
			NowFarming = false;

			Logging.LogGenericInfo("Farming finished!", Bot.BotName);
			await Bot.OnFarmingFinished(true).ConfigureAwait(false);
		}

		internal async Task StopFarming() {
			if (!NowFarming) {
				return;
			}

			await FarmingSemaphore.WaitAsync().ConfigureAwait(false);

			if (!NowFarming) {
				FarmingSemaphore.Release();
				return;
			}

			Logging.LogGenericInfo("Sending signal to stop farming", Bot.BotName);
			KeepFarming = false;
			FarmResetEvent.Set();

			Logging.LogGenericInfo("Waiting for reaction...", Bot.BotName);
			for (byte i = 0; (i < 5) && NowFarming; i++) {
				await Utilities.SleepAsync(1000).ConfigureAwait(false);
			}

			if (NowFarming) {
				Logging.LogGenericWarning("Timed out!", Bot.BotName);
			}

			Logging.LogGenericInfo("Farming stopped!", Bot.BotName);
			Bot.OnFarmingStopped();
			FarmingSemaphore.Release();
		}

		internal void OnNewItemsNotification() {
			if (!NowFarming) {
				return;
			}

			FarmResetEvent.Set();
		}

		private static HashSet<uint> GetGamesToFarmSolo(ConcurrentDictionary<uint, float> gamesToFarm) {
			if (gamesToFarm == null) {
				Logging.LogNullError(nameof(gamesToFarm));
				return null;
			}

			HashSet<uint> result = new HashSet<uint>();
			foreach (KeyValuePair<uint, float> keyValue in gamesToFarm.Where(keyValue => keyValue.Value >= 2)) {
				result.Add(keyValue.Key);
			}

			return result;
		}

		private async Task<bool> IsAnythingToFarm() {
			Logging.LogGenericInfo("Checking badges...", Bot.BotName);

			// Find the number of badge pages
			Logging.LogGenericInfo("Checking first page...", Bot.BotName);
			HtmlDocument htmlDocument = await Bot.ArchiWebHandler.GetBadgePage(1).ConfigureAwait(false);
			if (htmlDocument == null) {
				Logging.LogGenericWarning("Could not get badges information, will try again later!", Bot.BotName);
				return false;
			}

			byte maxPages = 1;

			HtmlNode htmlNode = htmlDocument.DocumentNode.SelectSingleNode("(//a[@class='pagelink'])[last()]");
			if (htmlNode != null) {
				string lastPage = htmlNode.InnerText;
				if (string.IsNullOrEmpty(lastPage)) {
					Logging.LogNullError(nameof(lastPage), Bot.BotName);
					return false;
				}

				if (!byte.TryParse(lastPage, out maxPages) || (maxPages == 0)) {
					Logging.LogNullError(nameof(maxPages), Bot.BotName);
					return false;
				}
			}

			GamesToFarm.Clear();
			CheckPage(htmlDocument);

			if (maxPages == 1) {
				return GamesToFarm.Count > 0;
			}

			Logging.LogGenericInfo("Checking other pages...", Bot.BotName);

			List<Task> tasks = new List<Task>(maxPages - 1);
			for (byte page = 2; page <= maxPages; page++) {
				byte currentPage = page; // We need a copy of variable being passed when in for loops, as loop will proceed before task is launched
				tasks.Add(CheckPage(currentPage));
			}

			await Task.WhenAll(tasks).ConfigureAwait(false);
			return GamesToFarm.Count > 0;
		}

		private void CheckPage(HtmlDocument htmlDocument) {
			if (htmlDocument == null) {
				Logging.LogNullError(nameof(htmlDocument), Bot.BotName);
				return;
			}

			HtmlNodeCollection htmlNodes = htmlDocument.DocumentNode.SelectNodes("//div[@class='badge_title_stats']");
			if (htmlNodes == null) { // For example a page full of non-games badges
				return;
			}

			foreach (HtmlNode htmlNode in htmlNodes) {
				HtmlNode farmingNode = htmlNode.SelectSingleNode(".//a[@class='btn_green_white_innerfade btn_small_thin']");
				if (farmingNode == null) {
					continue; // This game is not needed for farming
				}

				string steamLink = farmingNode.GetAttributeValue("href", null);
				if (string.IsNullOrEmpty(steamLink)) {
					Logging.LogNullError(nameof(steamLink), Bot.BotName);
					continue;
				}

				int index = steamLink.LastIndexOf('/');
				if (index < 0) {
					Logging.LogNullError(nameof(index), Bot.BotName);
					continue;
				}

				index++;
				if (steamLink.Length <= index) {
					Logging.LogNullError(nameof(steamLink.Length), Bot.BotName);
					continue;
				}

				steamLink = steamLink.Substring(index);

				uint appID;
				if (!uint.TryParse(steamLink, out appID) || (appID == 0)) {
					Logging.LogNullError(nameof(appID), Bot.BotName);
					continue;
				}

				if (GlobalConfig.GlobalBlacklist.Contains(appID) || Program.GlobalConfig.Blacklist.Contains(appID)) {
					continue;
				}

				HtmlNode timeNode = htmlNode.SelectSingleNode(".//div[@class='badge_title_stats_playtime']");
				if (timeNode == null) {
					Logging.LogNullError(nameof(timeNode), Bot.BotName);
					continue;
				}

				string hoursString = timeNode.InnerText;
				if (string.IsNullOrEmpty(hoursString)) {
					Logging.LogNullError(nameof(hoursString), Bot.BotName);
					continue;
				}

				float hours = 0;

				Match match = Regex.Match(hoursString, @"[0-9\.,]+");
				if (match.Success) {
					if (!float.TryParse(match.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out hours)) {
						Logging.LogNullError(nameof(hours), Bot.BotName);
						continue;
					}
				}

				GamesToFarm[appID] = hours;
			}
		}

		private async Task CheckPage(byte page) {
			if (page == 0) {
				Logging.LogNullError(nameof(page), Bot.BotName);
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
				Logging.LogNullError(nameof(appID), Bot.BotName);
				return false;
			}

			HtmlDocument htmlDocument = await Bot.ArchiWebHandler.GetGameCardsPage(appID).ConfigureAwait(false);
			if (htmlDocument == null) {
				return null;
			}

			HtmlNode htmlNode = htmlDocument.DocumentNode.SelectSingleNode("//span[@class='progress_info_bold']");
			if (htmlNode != null) {
				return !htmlNode.InnerText.Contains("No card drops");
			}

			Logging.LogNullError(nameof(htmlNode), Bot.BotName);
			return null;
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
				CurrentGamesFarming.ClearAndTrim();
				return true;
			}

			Logging.LogGenericInfo("Now farming: " + string.Join(", ", CurrentGamesFarming), Bot.BotName);

			bool result = FarmHours(maxHour, CurrentGamesFarming);
			CurrentGamesFarming.ClearAndTrim();
			return result;
		}

		private async Task<bool> FarmSolo(uint appID) {
			if (appID == 0) {
				Logging.LogNullError(nameof(appID), Bot.BotName);
				return true;
			}

			CurrentGamesFarming.Add(appID);

			Logging.LogGenericInfo("Now farming: " + appID, Bot.BotName);

			bool result = await Farm(appID).ConfigureAwait(false);
			CurrentGamesFarming.ClearAndTrim();

			if (!result) {
				return false;
			}

			float hours;
			if (!GamesToFarm.TryRemove(appID, out hours)) {
				return false;
			}

			TimeSpan timeSpan = TimeSpan.FromHours(hours);
			Logging.LogGenericInfo("Done farming: " + appID + " after " + timeSpan.ToString(@"hh\:mm") + " hours of playtime!", Bot.BotName);
			return true;
		}

		private async Task<bool> Farm(uint appID) {
			if (appID == 0) {
				Logging.LogNullError(nameof(appID), Bot.BotName);
				return false;
			}

			Bot.ArchiHandler.PlayGames(appID);
			DateTime endFarmingDate = DateTime.Now.AddHours(Program.GlobalConfig.MaxFarmingTime);

			bool success = true;
			bool? keepFarming = await ShouldFarm(appID).ConfigureAwait(false);

			while (keepFarming.GetValueOrDefault(true) && (DateTime.Now < endFarmingDate)) {
				Logging.LogGenericInfo("Still farming: " + appID, Bot.BotName);

				DateTime startFarmingPeriod = DateTime.Now;
				if (FarmResetEvent.Wait(60 * 1000 * Program.GlobalConfig.FarmingDelay)) {
					FarmResetEvent.Reset();
					success = KeepFarming;
				}

				// Don't forget to update our GamesToFarm hours
				GamesToFarm[appID] += (float) DateTime.Now.Subtract(startFarmingPeriod).TotalHours;

				if (!success) {
					break;
				}

				keepFarming = await ShouldFarm(appID).ConfigureAwait(false);
			}

			Logging.LogGenericInfo("Stopped farming: " + appID, Bot.BotName);
			return success;
		}

		private bool FarmHours(float maxHour, ConcurrentHashSet<uint> appIDs) {
			if ((maxHour < 0) || (appIDs == null) || (appIDs.Count == 0)) {
				Logging.LogNullError(nameof(maxHour) + " || " + nameof(appIDs) + " || " + nameof(appIDs.Count), Bot.BotName);
				return false;
			}

			if (maxHour >= 2) {
				return true;
			}

			Bot.ArchiHandler.PlayGames(appIDs);

			bool success = true;
			while (maxHour < 2) {
				Logging.LogGenericInfo("Still farming: " + string.Join(", ", appIDs), Bot.BotName);

				DateTime startFarmingPeriod = DateTime.Now;
				if (FarmResetEvent.Wait(60 * 1000 * Program.GlobalConfig.FarmingDelay)) {
					FarmResetEvent.Reset();
					success = KeepFarming;
				}

				// Don't forget to update our GamesToFarm hours
				float timePlayed = (float) DateTime.Now.Subtract(startFarmingPeriod).TotalHours;
				foreach (uint appID in appIDs) {
					GamesToFarm[appID] += timePlayed;
				}

				if (!success) {
					break;
				}

				maxHour += timePlayed;
			}

			Logging.LogGenericInfo("Stopped farming: " + string.Join(", ", appIDs), Bot.BotName);
			return success;
		}
	}
}
