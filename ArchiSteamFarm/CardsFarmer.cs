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

		private readonly ManualResetEvent FarmResetEvent = new ManualResetEvent(false);
		private readonly SemaphoreSlim Semaphore = new SemaphoreSlim(1);

		private readonly Bot Bot;
		private readonly Timer Timer;

		internal readonly ConcurrentDictionary<uint, double> GamesToFarm = new ConcurrentDictionary<uint, double>();
		internal readonly List<uint> CurrentGamesFarming = new List<uint>();

		private volatile bool NowFarming = false;

		internal CardsFarmer(Bot bot) {
			Bot = bot;

			Timer = new Timer(
				async e => await CheckGamesForFarming().ConfigureAwait(false),
				null,
				TimeSpan.FromMinutes(15), // Delay
				TimeSpan.FromMinutes(15) // Period
			);
		}

		internal static List<uint> GetGamesToFarmSolo(ConcurrentDictionary<uint, double> gamesToFarm) {
			if (gamesToFarm == null) {
				return null;
			}

			List<uint> result = new List<uint>();
			foreach (KeyValuePair<uint, double> keyValue in gamesToFarm) {
				if (keyValue.Value >= 2) {
					result.Add(keyValue.Key);
				}
			}

			return result;
		}

		internal static uint GetAnyGameToFarm(ConcurrentDictionary<uint, double> gamesToFarm) {
			if (gamesToFarm == null) {
				return 0;
			}

			foreach (uint appID in gamesToFarm.Keys) {
				return appID;
			}

			return 0;
		}

		internal bool FarmMultiple() {
			if (GamesToFarm.Count == 0) {
				return true;
			}

			double maxHour = -1;

			foreach (double hour in GamesToFarm.Values) {
				if (hour > maxHour) {
					maxHour = hour;
				}
			}

			CurrentGamesFarming.Clear();
			foreach (uint appID in GamesToFarm.Keys) {
				CurrentGamesFarming.Add(appID);
			}

			Logging.LogGenericInfo(Bot.BotName, "Now farming: " + string.Join(", ", GamesToFarm.Keys));
			if (Farm(maxHour, GamesToFarm.Keys)) {
				return true;
			} else {
				CurrentGamesFarming.Clear();
				NowFarming = false;
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
				double hours;
				GamesToFarm.TryRemove(appID, out hours);
				return true;
			} else {
				CurrentGamesFarming.Clear();
				NowFarming = false;
				return false;
			}
		}

		internal async Task StartFarming() {
			await StopFarming().ConfigureAwait(false);

			await Semaphore.WaitAsync().ConfigureAwait(false);

			if (NowFarming) {
				Semaphore.Release();
				return;
			}

			// Check if farming is possible
			Logging.LogGenericInfo(Bot.BotName, "Checking possibility to farm...");
			NowFarming = true;
			Semaphore.Release();
			Bot.ArchiHandler.PlayGames(1337);

			// We'll now either receive OnLoggedOff() with LoggedInElsewhere, or nothing happens
			if (await Task.Run(() => FarmResetEvent.WaitOne(5000)).ConfigureAwait(false)) { // If LoggedInElsewhere happens in 5 seconds from now, abort farming
				NowFarming = false;
				return;
			}

			NowFarming = false;
			Logging.LogGenericInfo(Bot.BotName, "Farming is possible!");

			await Semaphore.WaitAsync().ConfigureAwait(false);

			if (await Bot.ArchiWebHandler.ReconnectIfNeeded().ConfigureAwait(false)) {
				Semaphore.Release();
				return;
			}

			Logging.LogGenericInfo(Bot.BotName, "Checking badges...");

			// Find the number of badge pages
			HtmlDocument badgesDocument = await Bot.ArchiWebHandler.GetBadgePage(1).ConfigureAwait(false);
			if (badgesDocument == null) {
				Logging.LogGenericWarning(Bot.BotName, "Could not get badges information, farming is stopped!");
				Semaphore.Release();
				return;
			}

			var maxPages = 1;
			HtmlNodeCollection badgesPagesNodeCollection = badgesDocument.DocumentNode.SelectNodes("//a[@class='pagelink']");
			if (badgesPagesNodeCollection != null) {
				maxPages = (badgesPagesNodeCollection.Count / 2) + 1; // Don't do this at home
			}

			// Find APPIDs we need to farm
			for (var page = 1; page <= maxPages; page++) {
				Logging.LogGenericInfo(Bot.BotName, "Checking page: " + page + "/" + maxPages);

				if (page > 1) { // Because we fetched page number 1 already
					badgesDocument = await Bot.ArchiWebHandler.GetBadgePage(page).ConfigureAwait(false);
					if (badgesDocument == null) {
						break;
					}
				}

				HtmlNodeCollection badgesPageNodes = badgesDocument.DocumentNode.SelectNodes("//a[@class='btn_green_white_innerfade btn_small_thin']");
				if (badgesPageNodes == null) {
					continue;
				}

				GamesToFarm.Clear();
				foreach (HtmlNode badgesPageNode in badgesPageNodes) {
					string steamLink = badgesPageNode.GetAttributeValue("href", null);
					if (steamLink == null) {
						continue;
					}

					uint appID = (uint) Utilities.OnlyNumbers(steamLink);
					if (appID == 0) {
						continue;
					}

					if (Bot.Blacklist.Contains(appID)) {
						continue;
					}

					// We assume that every game has at least 2 hours played, until we actually check them
					GamesToFarm.AddOrUpdate(appID, 2, (key, value) => 2);
				}
			}

			// If we have restricted card drops, actually do check all games that are left to farm
			if (Bot.CardDropsRestricted) {
				foreach (uint appID in GamesToFarm.Keys) {
					Logging.LogGenericInfo(Bot.BotName, "Checking hours of appID: " + appID);
					HtmlDocument appPage = await Bot.ArchiWebHandler.GetGameCardsPage(appID).ConfigureAwait(false);
					if (appPage == null) {
						continue;
					}

					HtmlNode appNode = appPage.DocumentNode.SelectSingleNode("//div[@class='badge_title_stats_playtime']");
					if (appNode == null) {
						continue;
					}

					string hoursString = appNode.InnerText;
					if (string.IsNullOrEmpty(hoursString)) {
						continue;
					}

					hoursString = Regex.Match(hoursString, @"[0-9\.,]+").Value;
					double hours;

					if (string.IsNullOrEmpty(hoursString)) {
						hours = 0;
					} else {
						hours = double.Parse(hoursString, CultureInfo.InvariantCulture);
					}

					GamesToFarm[appID] = hours;
				}
			}

			Logging.LogGenericInfo(Bot.BotName, "Farming in progress...");

			NowFarming = GamesToFarm.Count > 0;
			Semaphore.Release();

			// Now the algorithm used for farming depends on whether account is restricted or not
			if (Bot.CardDropsRestricted) {
				// If we have restricted card drops, we use complex algorithm, which prioritizes farming solo titles >= 2 hours, then all at once, until any game hits mentioned 2 hours
				Logging.LogGenericInfo(Bot.BotName, "Chosen farming algorithm: Complex");
				while (GamesToFarm.Count > 0) {
					List<uint> gamesToFarmSolo = GetGamesToFarmSolo(GamesToFarm);
					if (gamesToFarmSolo.Count > 0) {
						while (gamesToFarmSolo.Count > 0) {
							uint appID = gamesToFarmSolo[0];
							bool success = await FarmSolo(appID).ConfigureAwait(false);
							if (success) {
								Logging.LogGenericInfo(Bot.BotName, "Done farming: " + appID);
								gamesToFarmSolo.Remove(appID);
							} else {
								return;
							}
						}
					} else {
						bool success = FarmMultiple();
						if (success) {
							Logging.LogGenericInfo(Bot.BotName, "Done farming: " + string.Join(", ", GamesToFarm.Keys));
						} else {
							return;
						}
					}
				}
			} else {
				// If we have unrestricted card drops, we use simple algorithm and farm cards one-by-one
				Logging.LogGenericInfo(Bot.BotName, "Chosen farming algorithm: Simple");
				while (GamesToFarm.Count > 0) {
					uint appID = GetAnyGameToFarm(GamesToFarm);
					bool success = await FarmSolo(appID).ConfigureAwait(false);
					if (success) {
						Logging.LogGenericInfo(Bot.BotName, "Done farming: " + appID);
					} else {
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
			while (NowFarming) {
				Logging.LogGenericInfo(Bot.BotName, "Waiting for reaction...");
				await Utilities.SleepAsync(1000).ConfigureAwait(false);
			}
			FarmResetEvent.Reset();
			Logging.LogGenericInfo(Bot.BotName, "Farming stopped!");
			Semaphore.Release();
		}

		private async Task CheckGamesForFarming() {
			if (NowFarming || GamesToFarm.Count > 0) {
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

		private bool Farm(double maxHour, ICollection<uint> appIDs) {
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
				double timePlayed = StatusCheckSleep / 60.0;
				foreach (KeyValuePair<uint, double> keyValue in GamesToFarm) {
					if (!appIDs.Contains(keyValue.Key)) {
						continue;
					}

					GamesToFarm[keyValue.Key] = keyValue.Value + timePlayed;
				}

				maxHour += timePlayed;
			}

			Bot.ArchiHandler.PlayGames(0);
			Logging.LogGenericInfo(Bot.BotName, "Stopped farming: " + string.Join(", ", appIDs));
			return success;
		}
	}
}
