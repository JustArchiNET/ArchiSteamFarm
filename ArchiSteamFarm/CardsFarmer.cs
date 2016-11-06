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
			internal float HoursPlayed { get; set; }

			[JsonProperty]
			internal ushort CardsRemaining { get; set; }

			//internal string HeaderURL => "https://steamcdn-a.akamaihd.net/steam/apps/" + AppID + "/header.jpg";

			internal Game(uint appID, string gameName, float hoursPlayed, ushort cardsRemaining) {
				if ((appID == 0) || string.IsNullOrEmpty(gameName) || (hoursPlayed < 0) || (cardsRemaining == 0)) {
					throw new ArgumentOutOfRangeException(nameof(appID) + " || " + nameof(gameName) + " || " + nameof(hoursPlayed) + " || " + nameof(cardsRemaining));
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

				Game game = obj as Game;
				return (game != null) && Equals(game);
			}

			public override int GetHashCode() => (int) AppID;

			private bool Equals(Game other) => AppID == other.AppID;
		}

		internal const byte MaxGamesPlayedConcurrently = 32; // This is limit introduced by Steam Network

		[JsonProperty]
		internal readonly ConcurrentHashSet<Game> GamesToFarm = new ConcurrentHashSet<Game>();

		[JsonProperty]
		internal readonly ConcurrentHashSet<Game> CurrentGamesFarming = new ConcurrentHashSet<Game>();

		private readonly ManualResetEventSlim FarmResetEvent = new ManualResetEventSlim(false);
		private readonly SemaphoreSlim FarmingSemaphore = new SemaphoreSlim(1);
		private readonly Bot Bot;
		private readonly Timer IdleFarmingTimer;

		[JsonProperty]
		internal bool Paused { get; private set; }

		private bool KeepFarming, NowFarming, StickyPause;

		internal CardsFarmer(Bot bot) {
			if (bot == null) {
				throw new ArgumentNullException(nameof(bot));
			}

			Bot = bot;

			if (Program.GlobalConfig.IdleFarmingPeriod > 0) {
				IdleFarmingTimer = new Timer(
					e => CheckGamesForFarming(),
					null,
					TimeSpan.FromHours(Program.GlobalConfig.IdleFarmingPeriod) + TimeSpan.FromMinutes(0.5 * Bot.Bots.Count), // Delay
					TimeSpan.FromHours(Program.GlobalConfig.IdleFarmingPeriod) // Period
				);
			}
		}

		internal void SetInitialState(bool paused) => StickyPause = Paused = paused;

		internal async Task Pause(bool sticky) {
			if (sticky) {
				StickyPause = true;
			}

			Paused = true;
			if (NowFarming) {
				await StopFarming().ConfigureAwait(false);
			}
		}

		internal void Resume(bool userAction) {
			if (StickyPause) {
				if (!userAction) {
					Bot.ArchiLogger.LogGenericInfo("Not honoring this request, as sticky pause is enabled!");
					return;
				}

				StickyPause = false;
			}

			Paused = false;
			if (!NowFarming) {
				StartFarming().Forget();
			}
		}

		internal async Task StartFarming() {
			if (NowFarming || Paused || !Bot.IsFarmingPossible) {
				return;
			}

			await FarmingSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				if (NowFarming || Paused || !Bot.IsFarmingPossible) {
					return;
				}

				if (!await IsAnythingToFarm().ConfigureAwait(false)) {
					Bot.ArchiLogger.LogGenericInfo("We don't have anything to farm on this account!");
					await Bot.OnFarmingFinished(false).ConfigureAwait(false);
					return;
				}

				Bot.ArchiLogger.LogGenericInfo("We have a total of " + GamesToFarm.Count + " games (" + GamesToFarm.Sum(game => game.CardsRemaining) + " cards) to farm on this account...");

				// This is the last moment for final check if we can farm
				if (!Bot.IsFarmingPossible) {
					Bot.ArchiLogger.LogGenericInfo("But account is currently occupied, so farming is stopped!");
					return;
				}

				KeepFarming = NowFarming = true;
			} finally {
				FarmingSemaphore.Release();
			}

			do {
				// Now the algorithm used for farming depends on whether account is restricted or not
				if (Bot.BotConfig.CardDropsRestricted) { // If we have restricted card drops, we use complex algorithm
					Bot.ArchiLogger.LogGenericInfo("Chosen farming algorithm: Complex");
					while (GamesToFarm.Count > 0) {
						HashSet<Game> gamesToFarmSolo = GamesToFarm.Count > 1 ? new HashSet<Game>(GamesToFarm.Where(game => game.HoursPlayed >= 2)) : new HashSet<Game>(GamesToFarm);
						if (gamesToFarmSolo.Count > 0) {
							while (gamesToFarmSolo.Count > 0) {
								Game game = gamesToFarmSolo.First();
								if (await FarmSolo(game).ConfigureAwait(false)) {
									gamesToFarmSolo.Remove(game);
								} else {
									NowFarming = false;
									return;
								}
							}
						} else {
							if (FarmMultiple(GamesToFarm.OrderByDescending(game => game.HoursPlayed).Take(MaxGamesPlayedConcurrently))) {
								Bot.ArchiLogger.LogGenericInfo("Done farming: " + string.Join(", ", GamesToFarm.Select(game => game.AppID)));
							} else {
								NowFarming = false;
								return;
							}
						}
					}
				} else { // If we have unrestricted card drops, we use simple algorithm
					Bot.ArchiLogger.LogGenericInfo("Chosen farming algorithm: Simple");
					while (GamesToFarm.Count > 0) {
						Game game = GamesToFarm.First();
						if (await FarmSolo(game).ConfigureAwait(false)) {
							continue;
						}

						NowFarming = false;
						return;
					}
				}
			} while (await IsAnythingToFarm().ConfigureAwait(false));

			CurrentGamesFarming.ClearAndTrim();
			NowFarming = false;

			Bot.ArchiLogger.LogGenericInfo("Farming finished!");
			await Bot.OnFarmingFinished(true).ConfigureAwait(false);
		}

		internal async Task StopFarming() {
			if (!NowFarming) {
				return;
			}

			await FarmingSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				if (!NowFarming) {
					return;
				}

				Bot.ArchiLogger.LogGenericInfo("Sending signal to stop farming");
				KeepFarming = false;
				FarmResetEvent.Set();

				Bot.ArchiLogger.LogGenericInfo("Waiting for reaction...");
				for (byte i = 0; (i < 5) && NowFarming; i++) {
					await Task.Delay(1000).ConfigureAwait(false);
				}

				if (NowFarming) {
					Bot.ArchiLogger.LogGenericWarning("Timed out!");
				}

				Bot.ArchiLogger.LogGenericInfo("Farming stopped!");
				Bot.OnFarmingStopped();
			} finally {
				FarmingSemaphore.Release();
			}
		}

		internal void OnDisconnected() => StopFarming().Forget();

		internal async Task OnNewItemsNotification() {
			if (NowFarming) {
				FarmResetEvent.Set();
				return;
			}

			// If we're not farming, and we got new items, it's likely to be a booster pack or likewise
			// In this case, perform a loot if user wants to do so
			await Bot.LootIfNeeded().ConfigureAwait(false);
		}

		internal async Task OnNewGameAdded() {
			if (!NowFarming) {
				// If we're not farming yet, obviously it's worth it to make a check
				StartFarming().Forget();
				return;
			}

			if (Bot.BotConfig.CardDropsRestricted && (GamesToFarm.Count > 0) && (GamesToFarm.Min(game => game.HoursPlayed) < 2)) {
				// If we have Complex algorithm and some games to boost, it's also worth to make a check
				// That's because we would check for new games after our current round anyway
				await StopFarming().ConfigureAwait(false);
				StartFarming().Forget();
			}
		}

		private async Task<bool> IsAnythingToFarm() {
			Bot.ArchiLogger.LogGenericInfo("Checking badges...");

			// Find the number of badge pages
			Bot.ArchiLogger.LogGenericInfo("Checking first page...");
			HtmlDocument htmlDocument = await Bot.ArchiWebHandler.GetBadgePage(1).ConfigureAwait(false);
			if (htmlDocument == null) {
				Bot.ArchiLogger.LogGenericWarning("Could not get badges information, will try again later!");
				return false;
			}

			byte maxPages = 1;

			HtmlNode htmlNode = htmlDocument.DocumentNode.SelectSingleNode("(//a[@class='pagelink'])[last()]");
			if (htmlNode != null) {
				string lastPage = htmlNode.InnerText;
				if (string.IsNullOrEmpty(lastPage)) {
					Bot.ArchiLogger.LogNullError(nameof(lastPage));
					return false;
				}

				if (!byte.TryParse(lastPage, out maxPages) || (maxPages == 0)) {
					Bot.ArchiLogger.LogNullError(nameof(maxPages));
					return false;
				}
			}

			GamesToFarm.ClearAndTrim();
			CheckPage(htmlDocument);

			if (maxPages == 1) {
				SortGamesToFarm();
				return GamesToFarm.Count > 0;
			}

			Bot.ArchiLogger.LogGenericInfo("Checking other pages...");

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
					gamesToFarm = GamesToFarm.OrderBy(game => game.AppID);
					break;
				case BotConfig.EFarmingOrder.AppIDsDescending:
					gamesToFarm = GamesToFarm.OrderByDescending(game => game.AppID);
					break;
				case BotConfig.EFarmingOrder.CardDropsAscending:
					gamesToFarm = GamesToFarm.OrderBy(game => game.CardsRemaining);
					break;
				case BotConfig.EFarmingOrder.CardDropsDescending:
					gamesToFarm = GamesToFarm.OrderByDescending(game => game.CardsRemaining);
					break;
				case BotConfig.EFarmingOrder.HoursAscending:
					gamesToFarm = GamesToFarm.OrderBy(game => game.HoursPlayed);
					break;
				case BotConfig.EFarmingOrder.HoursDescending:
					gamesToFarm = GamesToFarm.OrderByDescending(game => game.HoursPlayed);
					break;
				case BotConfig.EFarmingOrder.NamesAscending:
					gamesToFarm = GamesToFarm.OrderBy(game => game.GameName);
					break;
				case BotConfig.EFarmingOrder.NamesDescending:
					gamesToFarm = GamesToFarm.OrderByDescending(game => game.GameName);
					break;
				default:
					Bot.ArchiLogger.LogGenericError("Unhandled case: " + Bot.BotConfig.FarmingOrder);
					return;
			}

			GamesToFarm.ReplaceWith(gamesToFarm.ToList()); // We must call ToList() here as we can't enumerate during replacing
		}

		private void CheckPage(HtmlDocument htmlDocument) {
			if (htmlDocument == null) {
				Bot.ArchiLogger.LogNullError(nameof(htmlDocument));
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
					Bot.ArchiLogger.LogNullError(nameof(steamLink));
					return;
				}

				int index = steamLink.LastIndexOf('/');
				if (index < 0) {
					Bot.ArchiLogger.LogNullError(nameof(index));
					return;
				}

				index++;
				if (steamLink.Length <= index) {
					Bot.ArchiLogger.LogNullError(nameof(steamLink.Length));
					return;
				}

				steamLink = steamLink.Substring(index);

				uint appID;
				if (!uint.TryParse(steamLink, out appID) || (appID == 0)) {
					Bot.ArchiLogger.LogNullError(nameof(appID));
					return;
				}

				if (GlobalConfig.GlobalBlacklist.Contains(appID) || Program.GlobalConfig.Blacklist.Contains(appID)) {
					continue;
				}

				// Hours
				HtmlNode timeNode = htmlNode.SelectSingleNode(".//div[@class='badge_title_stats_playtime']");
				if (timeNode == null) {
					Bot.ArchiLogger.LogNullError(nameof(timeNode));
					return;
				}

				string hoursString = timeNode.InnerText;
				if (string.IsNullOrEmpty(hoursString)) {
					Bot.ArchiLogger.LogNullError(nameof(hoursString));
					return;
				}

				float hours = 0;

				Match hoursMatch = Regex.Match(hoursString, @"[0-9\.,]+");
				if (hoursMatch.Success) { // Might fail if we have 0.0 hours played
					if (!float.TryParse(hoursMatch.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out hours)) {
						Bot.ArchiLogger.LogNullError(nameof(hours));
						return;
					}
				}

				// Cards
				string progress = progressNode.InnerText;
				if (string.IsNullOrEmpty(progress)) {
					Bot.ArchiLogger.LogNullError(nameof(progress));
					return;
				}

				Match progressMatch = Regex.Match(progress, @"\d+");
				if (!progressMatch.Success) {
					Bot.ArchiLogger.LogNullError(nameof(progressMatch));
					return;
				}

				ushort cardsRemaining;
				if (!ushort.TryParse(progressMatch.Value, out cardsRemaining) || (cardsRemaining == 0)) {
					Bot.ArchiLogger.LogNullError(nameof(cardsRemaining));
					return;
				}

				// Names
				HtmlNode nameNode = htmlNode.SelectSingleNode("(.//div[@class='card_drop_info_body'])[last()]");
				if (nameNode == null) {
					Bot.ArchiLogger.LogNullError(nameof(nameNode));
					return;
				}

				string name = nameNode.InnerText;
				if (string.IsNullOrEmpty(name)) {
					Bot.ArchiLogger.LogNullError(nameof(name));
					return;
				}

				int nameStartIndex = name.IndexOf(" by playing ", StringComparison.Ordinal);
				if (nameStartIndex <= 0) {
					Bot.ArchiLogger.LogNullError(nameof(nameStartIndex));
					return;
				}

				nameStartIndex += 12;

				int nameEndIndex = name.LastIndexOf('.');
				if (nameEndIndex <= nameStartIndex) {
					Bot.ArchiLogger.LogNullError(nameof(nameEndIndex));
					return;
				}

				name = name.Substring(nameStartIndex, nameEndIndex - nameStartIndex);

				// Final result
				GamesToFarm.Add(new Game(appID, name, hours, cardsRemaining));
			}
		}

		private async Task CheckPage(byte page) {
			if (page == 0) {
				Bot.ArchiLogger.LogNullError(nameof(page));
				return;
			}

			HtmlDocument htmlDocument = await Bot.ArchiWebHandler.GetBadgePage(page).ConfigureAwait(false);
			if (htmlDocument == null) {
				return;
			}

			CheckPage(htmlDocument);
		}

		private void CheckGamesForFarming() {
			if (NowFarming || Paused || !Bot.IsConnectedAndLoggedOn) {
				return;
			}

			StartFarming().Forget();
		}

		private async Task<bool?> ShouldFarm(Game game) {
			if (game == null) {
				Bot.ArchiLogger.LogNullError(nameof(game));
				return false;
			}

			HtmlDocument htmlDocument = await Bot.ArchiWebHandler.GetGameCardsPage(game.AppID).ConfigureAwait(false);
			if (htmlDocument == null) {
				return null;
			}

			HtmlNode htmlNode = htmlDocument.DocumentNode.SelectSingleNode("//span[@class='progress_info_bold']");
			if (htmlNode == null) {
				Bot.ArchiLogger.LogNullError(nameof(htmlNode));
				return null;
			}

			string progress = htmlNode.InnerText;
			if (string.IsNullOrEmpty(progress)) {
				Bot.ArchiLogger.LogNullError(nameof(progress));
				return null;
			}

			ushort cardsRemaining = 0;

			Match match = Regex.Match(progress, @"\d+");
			if (match.Success) {
				if (!ushort.TryParse(match.Value, out cardsRemaining)) {
					Bot.ArchiLogger.LogNullError(nameof(cardsRemaining));
					return null;
				}
			}

			game.CardsRemaining = cardsRemaining;

			Bot.ArchiLogger.LogGenericInfo("Status for " + game.AppID + " (" + game.GameName + "): " + cardsRemaining + " cards remaining");
			return cardsRemaining > 0;
		}

		private bool FarmMultiple(IEnumerable<Game> games) {
			if (games == null) {
				Bot.ArchiLogger.LogNullError(nameof(games));
				return false;
			}

			CurrentGamesFarming.ReplaceWith(games);

			Bot.ArchiLogger.LogGenericInfo("Now farming: " + string.Join(", ", CurrentGamesFarming.Select(game => game.AppID)));

			bool result = FarmHours(CurrentGamesFarming);
			CurrentGamesFarming.ClearAndTrim();
			return result;
		}

		private async Task<bool> FarmSolo(Game game) {
			if (game == null) {
				Bot.ArchiLogger.LogNullError(nameof(game));
				return true;
			}

			CurrentGamesFarming.Add(game);

			Bot.ArchiLogger.LogGenericInfo("Now farming: " + game.AppID + " (" + game.GameName + ")");

			bool result = await Farm(game).ConfigureAwait(false);
			CurrentGamesFarming.ClearAndTrim();

			if (!result) {
				return false;
			}

			GamesToFarm.Remove(game);

			TimeSpan timeSpan = TimeSpan.FromHours(game.HoursPlayed);
			Bot.ArchiLogger.LogGenericInfo("Done farming: " + game.AppID + " (" + game.GameName + ") after " + timeSpan.ToString(@"hh\:mm") + " hours of playtime!");
			return true;
		}

		private async Task<bool> Farm(Game game) {
			if (game == null) {
				Bot.ArchiLogger.LogNullError(nameof(game));
				return false;
			}

			Bot.ArchiHandler.PlayGame(game.AppID, Bot.BotConfig.CustomGamePlayedWhileFarming);
			DateTime endFarmingDate = DateTime.Now.AddHours(Program.GlobalConfig.MaxFarmingTime);

			bool success = true;
			bool? keepFarming = await ShouldFarm(game).ConfigureAwait(false);

			while (keepFarming.GetValueOrDefault(true) && (DateTime.Now < endFarmingDate)) {
				Bot.ArchiLogger.LogGenericInfo("Still farming: " + game.AppID + " (" + game.GameName + ")");

				DateTime startFarmingPeriod = DateTime.Now;
				if (FarmResetEvent.Wait(60 * 1000 * Program.GlobalConfig.FarmingDelay)) {
					FarmResetEvent.Reset();
					success = KeepFarming;
				}

				// Don't forget to update our GamesToFarm hours
				game.HoursPlayed += (float) DateTime.Now.Subtract(startFarmingPeriod).TotalHours;

				if (!success) {
					break;
				}

				keepFarming = await ShouldFarm(game).ConfigureAwait(false);
			}

			Bot.ArchiLogger.LogGenericInfo("Stopped farming: " + game.AppID + " (" + game.GameName + ")");
			return success;
		}

		private bool FarmHours(ConcurrentHashSet<Game> games) {
			if ((games == null) || (games.Count == 0)) {
				Bot.ArchiLogger.LogNullError(nameof(games));
				return false;
			}

			float maxHour = games.Max(game => game.HoursPlayed);
			if (maxHour < 0) {
				Bot.ArchiLogger.LogNullError(nameof(maxHour));
				return false;
			}

			if (maxHour >= 2) {
				Bot.ArchiLogger.LogGenericError("Received request for past-2h games!");
				return true;
			}

			Bot.ArchiHandler.PlayGames(games.Select(game => game.AppID), Bot.BotConfig.CustomGamePlayedWhileFarming);

			bool success = true;
			while (maxHour < 2) {
				Bot.ArchiLogger.LogGenericInfo("Still farming: " + string.Join(", ", games.Select(game => game.AppID)));

				DateTime startFarmingPeriod = DateTime.Now;
				if (FarmResetEvent.Wait(60 * 1000 * Program.GlobalConfig.FarmingDelay)) {
					FarmResetEvent.Reset();
					success = KeepFarming;
				}

				// Don't forget to update our GamesToFarm hours
				float timePlayed = (float) DateTime.Now.Subtract(startFarmingPeriod).TotalHours;
				foreach (Game game in games) {
					game.HoursPlayed += timePlayed;
				}

				if (!success) {
					break;
				}

				maxHour += timePlayed;
			}

			Bot.ArchiLogger.LogGenericInfo("Stopped farming: " + string.Join(", ", games.Select(game => game.AppID)));
			return success;
		}

		public void Dispose() {
			// Those are objects that are always being created if constructor doesn't throw exception
			CurrentGamesFarming.Dispose();
			GamesToFarm.Dispose();
			FarmingSemaphore.Dispose();
			FarmResetEvent.Dispose();

			// Those are objects that might be null and the check should be in-place
			IdleFarmingTimer?.Dispose();
		}
	}
}
