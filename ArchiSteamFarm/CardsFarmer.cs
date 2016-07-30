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
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace ArchiSteamFarm {
	internal sealed class CardsFarmer : IDisposable {
		internal sealed class Game {
			[JsonProperty]
			internal readonly uint AppID;

			[JsonProperty]
			internal readonly string GameName;

			[JsonProperty]
			internal float HoursPlayed;

			[JsonProperty]
			internal byte CardsRemaining;

			internal Game(uint appID, string gameName, float hoursPlayed, byte cardsRemaining) {
				if ((appID == 0) || string.IsNullOrEmpty(gameName) || (hoursPlayed < 0)) {
					throw new ArgumentOutOfRangeException(nameof(appID) + " || " + nameof(gameName) + " || " + nameof(hoursPlayed));
				}

				AppID = appID;
				GameName = gameName;
				HoursPlayed = hoursPlayed;
				CardsRemaining = cardsRemaining;
			}

			public override bool Equals(object obj) {
				if (ReferenceEquals(null, obj)) {
					return false;
				}

				if (ReferenceEquals(this, obj)) {
					return true;
				}

				return obj is Game && Equals((Game) obj);
			}

			public override int GetHashCode() => (int) AppID;

			private bool Equals(Game other) => AppID == other.AppID;
		}

		internal const byte MaxGamesPlayedConcurrently = 32; // This is limit introduced by Steam Network

		[JsonProperty]
		internal readonly ConcurrentHashSet<Game> GamesToFarm = new ConcurrentHashSet<Game>();

		[JsonProperty]
		internal readonly ConcurrentHashSet<uint> CurrentGamesFarming = new ConcurrentHashSet<uint>();

		private readonly ManualResetEventSlim FarmResetEvent = new ManualResetEventSlim(false);
		private readonly SemaphoreSlim FarmingSemaphore = new SemaphoreSlim(1);
		private readonly Bot Bot;
		private readonly Timer Timer;

		[JsonProperty]
		internal bool ManualMode { get; private set; }

		private bool KeepFarming, NowFarming;

		internal CardsFarmer(Bot bot) {
			if (bot == null) {
				throw new ArgumentNullException(nameof(bot));
			}

			Bot = bot;

			if ((Timer == null) && (Program.GlobalConfig.IdleFarmingPeriod > 0)) {
				Timer = new Timer(
					e => CheckGamesForFarming(),
					null,
					TimeSpan.FromHours(Program.GlobalConfig.IdleFarmingPeriod) + TimeSpan.FromMinutes(Bot.Bots.Count), // Delay
					TimeSpan.FromHours(Program.GlobalConfig.IdleFarmingPeriod) // Period
				);
			}
		}

		public void Dispose() {
			CurrentGamesFarming.Dispose();
			FarmResetEvent.Dispose();
			FarmingSemaphore.Dispose();

			Timer?.Dispose();
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
				Logging.LogGenericInfo("But account is currently occupied, so farming is stopped!", Bot.BotName);
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
								} else {
									NowFarming = false;
									return;
								}
							}
						} else {
							if (FarmMultiple()) {
								Logging.LogGenericInfo("Done farming: " + string.Join(", ", GamesToFarm.Select(g => g.AppID)),
									Bot.BotName);
							} else {
								NowFarming = false;
								return;
							}
						}
					}
				} else { // If we have unrestricted card drops, we use simple algorithm
					Logging.LogGenericInfo("Chosen farming algorithm: Simple", Bot.BotName);
					while (GamesToFarm.Count > 0) {
						uint appID = GamesToFarm.First().AppID;
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
				await Task.Delay(1000).ConfigureAwait(false);
			}

			if (NowFarming) {
				Logging.LogGenericWarning("Timed out!", Bot.BotName);
			}

			Logging.LogGenericInfo("Farming stopped!", Bot.BotName);
			Bot.OnFarmingStopped();
			FarmingSemaphore.Release();
		}

		internal void OnDisconnected() => StopFarming().Forget();

		internal void OnNewItemsNotification() {
			if (!NowFarming) {
				return;
			}

			FarmResetEvent.Set();
		}

		internal async Task OnNewGameAdded() {
			if (!NowFarming) {
				// If we're not farming yet, obviously it's worth it to make a check
				StartFarming().Forget();
				return;
			}

			if (Bot.BotConfig.CardDropsRestricted && (GamesToFarm.Count > 0) && (GamesToFarm.Min(g => g.HoursPlayed) < 2)) {
				// If we have Complex algorithm and some games to boost, it's also worth to make a check
				// That's because we would check for new games after our current round anyway
				await StopFarming().ConfigureAwait(false);
				StartFarming().Forget();
			}
		}

		private static HashSet<uint> GetGamesToFarmSolo(IEnumerable<Game> gamesToFarm) {
			if (gamesToFarm == null) {
				Logging.LogNullError(nameof(gamesToFarm));
				return null;
			}

			HashSet<uint> result = new HashSet<uint>();
			foreach (Game game in gamesToFarm.Where(g => g.HoursPlayed >= 2)) {
				result.Add(game.AppID);
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

			GamesToFarm.ClearAndTrim();
			CheckPage(htmlDocument);

			if (maxPages == 1) {
				SortGamesToFarm();
				return GamesToFarm.Count > 0;
			}

			Logging.LogGenericInfo("Checking other pages...", Bot.BotName);

			List<Task> tasks = new List<Task>(maxPages - 1);
			for (byte page = 2; page <= maxPages; page++) {
				byte currentPage = page; // We need a copy of variable being passed when in for loops, as loop will proceed before task is launched
				tasks.Add(CheckPage(currentPage));
			}

			await Task.WhenAll(tasks).ConfigureAwait(false);
			SortGamesToFarm();
			return GamesToFarm.Count > 0;
		}

		private void SortGamesToFarm() {
			IOrderedEnumerable<Game> gamesToFarm;
			switch (Bot.BotConfig.FarmingOrder) {
				case BotConfig.EFarmingOrder.Unordered:
					return;
				case BotConfig.EFarmingOrder.AppIDsAscending:
					gamesToFarm = GamesToFarm.OrderBy(g => g.AppID);
					break;
				case BotConfig.EFarmingOrder.AppIDsDescending:
					gamesToFarm = GamesToFarm.OrderByDescending(g => g.AppID);
					break;
				case BotConfig.EFarmingOrder.CardDropsAscending:
					gamesToFarm = GamesToFarm.OrderBy(g => g.CardsRemaining);
					break;
				case BotConfig.EFarmingOrder.CardDropsDescending:
					gamesToFarm = GamesToFarm.OrderByDescending(g => g.CardsRemaining);
					break;
				case BotConfig.EFarmingOrder.HoursAscending:
					gamesToFarm = GamesToFarm.OrderBy(g => g.HoursPlayed);
					break;
				case BotConfig.EFarmingOrder.HoursDescending:
					gamesToFarm = GamesToFarm.OrderByDescending(g => g.HoursPlayed);
					break;
				case BotConfig.EFarmingOrder.NamesAscending:
					gamesToFarm = GamesToFarm.OrderBy(g => g.GameName);
					break;
				case BotConfig.EFarmingOrder.NamesDescending:
					gamesToFarm = GamesToFarm.OrderByDescending(g => g.GameName);
					break;
				default:
					Logging.LogGenericError("Unhandled case: " + Bot.BotConfig.FarmingOrder, Bot.BotName);
					return;
			}

			GamesToFarm.ReplaceWith(gamesToFarm.ToList()); // We must call ToList() here as we can't enumerate during replacing
		}

		private void CheckPage(HtmlDocument htmlDocument) {
			if (htmlDocument == null) {
				Logging.LogNullError(nameof(htmlDocument), Bot.BotName);
				return;
			}

			HtmlNodeCollection htmlNodes = htmlDocument.DocumentNode.SelectNodes("//div[@class='badge_title_stats']");
			if (htmlNodes == null) { // No eligible badges
				return;
			}

			foreach (HtmlNode htmlNode in htmlNodes) {
				HtmlNode farmingNode = htmlNode.SelectSingleNode(".//a[@class='btn_green_white_innerfade btn_small_thin']");
				if (farmingNode == null) {
					continue; // This game is not needed for farming
				}

				HtmlNode progressNode = htmlNode.SelectSingleNode(".//span[@class='progress_info_bold']");
				if (progressNode == null) {
					continue; // e.g. Holiday Sale 2015
				}

				// AppIDs
				string steamLink = farmingNode.GetAttributeValue("href", null);
				if (string.IsNullOrEmpty(steamLink)) {
					Logging.LogNullError(nameof(steamLink), Bot.BotName);
					return;
				}

				int index = steamLink.LastIndexOf('/');
				if (index < 0) {
					Logging.LogNullError(nameof(index), Bot.BotName);
					return;
				}

				index++;
				if (steamLink.Length <= index) {
					Logging.LogNullError(nameof(steamLink.Length), Bot.BotName);
					return;
				}

				steamLink = steamLink.Substring(index);

				uint appID;
				if (!uint.TryParse(steamLink, out appID) || (appID == 0)) {
					Logging.LogNullError(nameof(appID), Bot.BotName);
					return;
				}

				if (GlobalConfig.GlobalBlacklist.Contains(appID) || Program.GlobalConfig.Blacklist.Contains(appID)) {
					continue;
				}

				// Hours
				HtmlNode timeNode = htmlNode.SelectSingleNode(".//div[@class='badge_title_stats_playtime']");
				if (timeNode == null) {
					Logging.LogNullError(nameof(timeNode), Bot.BotName);
					return;
				}

				string hoursString = timeNode.InnerText;
				if (string.IsNullOrEmpty(hoursString)) {
					Logging.LogNullError(nameof(hoursString), Bot.BotName);
					return;
				}

				float hours = 0;

				Match hoursMatch = Regex.Match(hoursString, @"[0-9\.,]+");
				if (hoursMatch.Success) { // Might fail if we have 0.0 hours played
					if (!float.TryParse(hoursMatch.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out hours)) {
						Logging.LogNullError(nameof(hours), Bot.BotName);
						return;
					}
				}

				// Cards
				string progress = progressNode.InnerText;
				if (string.IsNullOrEmpty(progress)) {
					Logging.LogNullError(nameof(progress), Bot.BotName);
					return;
				}

				Match progressMatch = Regex.Match(progress, @"\d+");
				if (!progressMatch.Success) {
					Logging.LogNullError(nameof(progressMatch), Bot.BotName);
					return;
				}

				byte cardsRemaining;
				if (!byte.TryParse(progressMatch.Value, out cardsRemaining) || (cardsRemaining == 0)) {
					Logging.LogNullError(nameof(cardsRemaining), Bot.BotName);
					return;
				}

				// Names
				HtmlNode nameNode = htmlNode.SelectSingleNode("(.//div[@class='card_drop_info_body'])[last()]");
				if (nameNode == null) {
					Logging.LogNullError(nameof(nameNode), Bot.BotName);
					return;
				}

				string name = nameNode.InnerText;
				if (string.IsNullOrEmpty(name)) {
					Logging.LogNullError(nameof(name), Bot.BotName);
					return;
				}

				int nameStartIndex = name.IndexOf(" by playing ", StringComparison.Ordinal);
				if (nameStartIndex <= 0) {
					Logging.LogNullError(nameof(nameStartIndex));
					return;
				}

				nameStartIndex += 12;

				int nameEndIndex = name.LastIndexOf('.');
				if (nameEndIndex <= nameStartIndex) {
					Logging.LogNullError(nameof(nameEndIndex));
					return;
				}

				name = name.Substring(nameStartIndex, nameEndIndex - nameStartIndex);

				// Final result
				GamesToFarm.Add(new Game(appID, name, hours, cardsRemaining));
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

		private void CheckGamesForFarming() {
			if (NowFarming || ManualMode || !Bot.SteamClient.IsConnected) {
				return;
			}

			StartFarming().Forget();
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
			if (htmlNode == null) {
				Logging.LogNullError(nameof(htmlNode), Bot.BotName);
				return null;
			}

			string progress = htmlNode.InnerText;
			if (string.IsNullOrEmpty(progress)) {
				Logging.LogNullError(nameof(progress), Bot.BotName);
				return null;
			}

			byte cardsRemaining = 0;

			Match match = Regex.Match(progress, @"\d+");
			if (match.Success) {
				if (!byte.TryParse(match.Value, out cardsRemaining)) {
					Logging.LogNullError(nameof(cardsRemaining), Bot.BotName);
					return null;
				}
			}

			Game game = GamesToFarm.First(g => g.AppID == appID);
			game.CardsRemaining = cardsRemaining;

			Logging.LogGenericInfo("Status for " + appID + ": " + cardsRemaining + " cards remaining", Bot.BotName);
			return cardsRemaining > 0;
		}

		private bool FarmMultiple() {
			if (GamesToFarm.Count == 0) {
				return true;
			}

			float maxHour = 0;
			foreach (Game game in GamesToFarm) {
				CurrentGamesFarming.Add(game.AppID);
				if (game.HoursPlayed > maxHour) {
					maxHour = game.HoursPlayed;
				}

				if (CurrentGamesFarming.Count >= MaxGamesPlayedConcurrently) {
					break;
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

			Game game = GamesToFarm.FirstOrDefault(g => g.AppID == appID);
			if (game == null) {
				return false;
			}

			GamesToFarm.Remove(game);

			TimeSpan timeSpan = TimeSpan.FromHours(game.HoursPlayed);
			Logging.LogGenericInfo("Done farming: " + appID + " after " + timeSpan.ToString(@"hh\:mm") + " hours of playtime!", Bot.BotName);
			return true;
		}

		private async Task<bool> Farm(uint appID) {
			if (appID == 0) {
				Logging.LogNullError(nameof(appID), Bot.BotName);
				return false;
			}

			Bot.ArchiHandler.PlayGame(appID, Bot.BotConfig.CustomGamePlayedWhileFarming);
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
				Game game = GamesToFarm.First(g => g.AppID == appID);
				game.HoursPlayed += (float) DateTime.Now.Subtract(startFarmingPeriod).TotalHours;

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

			Bot.ArchiHandler.PlayGames(appIDs, Bot.BotConfig.CustomGamePlayedWhileFarming);

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
				foreach (Game game in GamesToFarm.Where(game => appIDs.Contains(game.AppID))) {
					game.HoursPlayed += timePlayed;
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
