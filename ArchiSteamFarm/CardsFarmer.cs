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

		internal bool FarmMultiple(ConcurrentDictionary<uint, double> appIDs) {
			if (appIDs.Count == 0) {
				return true;
			}

			double maxHour = -1;

			foreach (double hour in appIDs.Values) {
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
				double hours;
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

			if (NowFarming) {
				Semaphore.Release();
				return;
			}

			if (await Bot.ArchiWebHandler.ReconnectIfNeeded().ConfigureAwait(false)) {
				Semaphore.Release();
				return;
			}

			Logging.LogGenericInfo(Bot.BotName, "Checking badges...");

			// Find the number of badge pages
			Logging.LogGenericInfo(Bot.BotName, "Checking page: 1/?");
			HtmlDocument htmlDocument = await Bot.ArchiWebHandler.GetBadgePage(1).ConfigureAwait(false);
			if (htmlDocument == null) {
				Logging.LogGenericWarning(Bot.BotName, "Could not get badges information, will try again later!");
				Semaphore.Release();
				return;
			}

			var maxPages = 1;
			HtmlNodeCollection htmlNodeCollection = htmlDocument.DocumentNode.SelectNodes("//a[@class='pagelink']");
			if (htmlNodeCollection != null && htmlNodeCollection.Count > 0) {
				HtmlNode htmlNode = htmlNodeCollection[htmlNodeCollection.Count - 1];
				if (!int.TryParse(htmlNode.InnerText, out maxPages)) {
					maxPages = 1; // Should never happen
				}
			}

			GamesToFarm.Clear();

			// Find APPIDs we need to farm
			for (var page = 1; page <= maxPages; page++) {
				if (page > 1) { // Because we fetched page number 1 already
					Logging.LogGenericInfo(Bot.BotName, "Checking page: " + page + "/" + maxPages);
					htmlDocument = await Bot.ArchiWebHandler.GetBadgePage(page).ConfigureAwait(false);
					if (htmlDocument == null) {
						break;
					}
				}

				htmlNodeCollection = htmlDocument.DocumentNode.SelectNodes("//a[@class='btn_green_white_innerfade btn_small_thin']");
				if (htmlNodeCollection == null) {
					continue;
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

			if (GamesToFarm.Count == 0) {
				Logging.LogGenericInfo(Bot.BotName, "No games to farm!");
				Semaphore.Release();
				return;
			}

			// If we have restricted card drops, actually do check hours of all games that are left to farm
			if (Bot.CardDropsRestricted) {
				foreach (uint appID in GamesToFarm.Keys) {
					Logging.LogGenericInfo(Bot.BotName, "Checking hours of appID: " + appID);
					htmlDocument = await Bot.ArchiWebHandler.GetGameCardsPage(appID).ConfigureAwait(false);
					if (htmlDocument == null) {
						continue;
					}

					HtmlNode htmlNode = htmlDocument.DocumentNode.SelectSingleNode("//div[@class='badge_title_stats_playtime']");
					if (htmlNode == null) {
						continue;
					}

					string hoursString = htmlNode.InnerText;
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

			NowFarming = true;
			Semaphore.Release(); // From this point we allow other calls to shut us down

			// Now the algorithm used for farming depends on whether account is restricted or not
			if (Bot.CardDropsRestricted) {
				// If we have restricted card drops, we use complex algorithm, which prioritizes farming solo titles >= 2 hours, then all at once, until any game hits mentioned 2 hours
				Logging.LogGenericInfo(Bot.BotName, "Chosen farming algorithm: Complex");
				while (GamesToFarm.Count > 0) {
					List<uint> gamesToFarmSolo = GetGamesToFarmSolo(GamesToFarm);
					if (gamesToFarmSolo.Count > 0) {
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
			} else {
				// If we have unrestricted card drops, we use simple algorithm and farm cards one-by-one
				Logging.LogGenericInfo(Bot.BotName, "Chosen farming algorithm: Simple");
				while (GamesToFarm.Count > 0) {
					uint appID = GetAnyGameToFarm(GamesToFarm);
					if (await FarmSolo(appID).ConfigureAwait(false)) {
						Logging.LogGenericInfo(Bot.BotName, "Done farming: " + appID);
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
			for (var i = 0; i < 5 && NowFarming; i++) {
				Logging.LogGenericInfo(Bot.BotName, "Waiting for reaction...");
				await Utilities.SleepAsync(1000).ConfigureAwait(false);
			}
			FarmResetEvent.Reset();
			Logging.LogGenericInfo(Bot.BotName, "Farming stopped!");
			Semaphore.Release();
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
				foreach (KeyValuePair<uint, double> gameToFarm in GamesToFarm) {
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
