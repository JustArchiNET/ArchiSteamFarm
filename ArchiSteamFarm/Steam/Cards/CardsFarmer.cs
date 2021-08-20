//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2021 Łukasz "JustArchi" Domeradzki
// Contact: JustArchi@JustArchi.net
// |
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// |
// http://www.apache.org/licenses/LICENSE-2.0
// |
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

#if NETFRAMEWORK
using JustArchiNET.Madness;
#endif
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Dom;
using ArchiSteamFarm.Collections;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Plugins;
using ArchiSteamFarm.Steam.Integration;
using ArchiSteamFarm.Steam.Storage;
using ArchiSteamFarm.Storage;
using JetBrains.Annotations;
using Newtonsoft.Json;
using SteamKit2;

namespace ArchiSteamFarm.Steam.Cards {
	public sealed class CardsFarmer : IAsyncDisposable {
		internal const byte DaysForRefund = 14; // In how many days since payment we're allowed to refund
		internal const byte HoursForRefund = 2; // Up to how many hours we're allowed to play for refund

		private const byte ExtraFarmingDelaySeconds = 10; // In seconds, how much time to add on top of FarmingDelay (helps fighting misc time differences of Steam network)
		private const byte HoursToIgnore = 1; // How many hours we ignore unreleased appIDs and don't bother checking them again

		[PublicAPI]
		public static readonly ImmutableHashSet<uint> SalesBlacklist = ImmutableHashSet.Create<uint>(267420, 303700, 335590, 368020, 425280, 480730, 566020, 639900, 762800, 876740, 991980, 1195670, 1343890, 1465680, 1658760);

		private static readonly ConcurrentDictionary<uint, DateTime> GloballyIgnoredAppIDs = new(); // Reserved for unreleased games

		// Games that were confirmed to show false status on general badges page
		private static readonly ImmutableHashSet<uint> UntrustedAppIDs = ImmutableHashSet.Create<uint>(440, 570, 730);

		[JsonProperty(PropertyName = nameof(CurrentGamesFarming))]
		[PublicAPI]
		public IReadOnlyCollection<Game> CurrentGamesFarmingReadOnly => CurrentGamesFarming;

		[JsonProperty(PropertyName = nameof(GamesToFarm))]
		[PublicAPI]
		public IReadOnlyCollection<Game> GamesToFarmReadOnly => GamesToFarm;

		[JsonProperty]
		[PublicAPI]
		public TimeSpan TimeRemaining =>
			new(
				Bot.BotConfig.HoursUntilCardDrops > 0 ? (ushort) Math.Ceiling(GamesToFarm.Count / (float) ArchiHandler.MaxGamesPlayedConcurrently) * Bot.BotConfig.HoursUntilCardDrops : 0,
				30 * GamesToFarm.Sum(game => game.CardsRemaining),
				0
			);

		private readonly Bot Bot;
		private readonly ConcurrentHashSet<Game> CurrentGamesFarming = new();
		private readonly SemaphoreSlim EventSemaphore = new(1, 1);
		private readonly SemaphoreSlim FarmingInitializationSemaphore = new(1, 1);
		private readonly SemaphoreSlim FarmingResetSemaphore = new(0, 1);
		private readonly ConcurrentList<Game> GamesToFarm = new();

#pragma warning disable CA2213 // False positive, .NET Framework can't understand DisposeAsync()
		private readonly Timer? IdleFarmingTimer;
#pragma warning restore CA2213 // False positive, .NET Framework can't understand DisposeAsync()

		private readonly ConcurrentDictionary<uint, DateTime> LocallyIgnoredAppIDs = new();

		private IEnumerable<ConcurrentDictionary<uint, DateTime>> SourcesOfIgnoredAppIDs {
			get {
				yield return GloballyIgnoredAppIDs;
				yield return LocallyIgnoredAppIDs;
			}
		}

		[JsonProperty]
		[PublicAPI]
		public bool Paused { get; private set; }

		internal bool NowFarming { get; private set; }

		private bool KeepFarming;
		private bool ParsingScheduled;
		private bool PermanentlyPaused;
		private bool ShouldResumeFarming = true;

		internal CardsFarmer(Bot bot) {
			Bot = bot ?? throw new ArgumentNullException(nameof(bot));

			byte idleFarmingPeriod = ASF.GlobalConfig?.IdleFarmingPeriod ?? GlobalConfig.DefaultIdleFarmingPeriod;

			if (idleFarmingPeriod > 0) {
				IdleFarmingTimer = new Timer(
					CheckGamesForFarming,
					null,
					TimeSpan.FromHours(idleFarmingPeriod) + TimeSpan.FromSeconds(ASF.LoadBalancingDelay * Bot.Bots?.Count ?? 0), // Delay
					TimeSpan.FromHours(idleFarmingPeriod) // Period
				);
			}
		}

		public async ValueTask DisposeAsync() {
			// Those are objects that are always being created if constructor doesn't throw exception
			EventSemaphore.Dispose();
			FarmingInitializationSemaphore.Dispose();
			FarmingResetSemaphore.Dispose();

			// Those are objects that might be null and the check should be in-place
			if (IdleFarmingTimer != null) {
				await IdleFarmingTimer.DisposeAsync().ConfigureAwait(false);
			}
		}

		internal void OnDisconnected() {
			if (!NowFarming) {
				return;
			}

			Utilities.InBackground(StopFarming);
		}

		internal async Task OnNewGameAdded() {
			// This update has a potential to modify local ignores, therefore we need to purge our cache
			LocallyIgnoredAppIDs.Clear();

			ShouldResumeFarming = true;

			// We aim to have a maximum of 2 tasks, one already parsing, and one waiting in the queue
			// This way we can call this function as many times as needed e.g. because of Steam events
			lock (EventSemaphore) {
				if (ParsingScheduled) {
					return;
				}

				ParsingScheduled = true;
			}

			await EventSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				lock (EventSemaphore) {
					ParsingScheduled = false;
				}

				// If we're not farming yet, obviously it's worth it to make a check
				if (!NowFarming) {
					await StartFarming().ConfigureAwait(false);

					return;
				}

				// We should restart the farming if the order or efficiency of the farming could be affected by the newly-activated product
				// The order is affected when user uses farming order that isn't independent of the game data (it could alter the order in deterministic way if the game was considered in current queue)
				// The efficiency is affected only in complex algorithm (entirely), as it depends on hours order that is not independent (as specified above)
				if ((Bot.BotConfig.HoursUntilCardDrops > 0) || ((Bot.BotConfig.FarmingOrders.Count > 0) && Bot.BotConfig.FarmingOrders.Any(farmingOrder => (farmingOrder != BotConfig.EFarmingOrder.Unordered) && (farmingOrder != BotConfig.EFarmingOrder.Random)))) {
					await StopFarming().ConfigureAwait(false);
					await StartFarming().ConfigureAwait(false);
				}
			} finally {
				EventSemaphore.Release();
			}
		}

		internal async Task OnNewItemsNotification() {
			if (NowFarming) {
				await FarmingInitializationSemaphore.WaitAsync().ConfigureAwait(false);

				try {
					if (NowFarming) {
						if (FarmingResetSemaphore.CurrentCount == 0) {
							FarmingResetSemaphore.Release();
						}

						return;
					}
				} finally {
					FarmingInitializationSemaphore.Release();
				}
			}

			// If we're not farming, and we got new items, it's likely to be a booster pack or likewise
			// In this case, perform a loot if user wants to do so
			if (Bot.BotConfig.SendOnFarmingFinished && (Bot.BotConfig.LootableTypes.Count > 0)) {
				await Bot.Actions.SendInventory(filterFunction: item => Bot.BotConfig.LootableTypes.Contains(item.Type)).ConfigureAwait(false);
			}
		}

		internal async Task Pause(bool permanent) {
			if (permanent) {
				PermanentlyPaused = true;
			}

			Paused = true;

			if (!NowFarming) {
				return;
			}

			await StopFarming().ConfigureAwait(false);
		}

		internal async Task<bool> Resume(bool userAction) {
			if (PermanentlyPaused) {
				if (!userAction) {
					Bot.ArchiLogger.LogGenericInfo(Strings.IgnoredPermanentPauseEnabled);

					return false;
				}

				PermanentlyPaused = false;
			}

			Paused = false;

			if (NowFarming) {
				return true;
			}

			if (!userAction && !ShouldResumeFarming) {
				return false;
			}

			await StartFarming().ConfigureAwait(false);

			return true;
		}

		internal void SetInitialState(bool paused) {
			PermanentlyPaused = Paused = paused;
			ShouldResumeFarming = true;
		}

		internal async Task StartFarming() {
			if (NowFarming || Paused || !Bot.IsPlayingPossible) {
				return;
			}

			if (!Bot.CanReceiveSteamCards || (Bot.BotConfig.FarmPriorityQueueOnly && (Bot.BotDatabase.IdlingPriorityAppIDs.Count == 0))) {
				Bot.ArchiLogger.LogGenericInfo(Strings.NothingToIdle);
				await Bot.OnFarmingFinished(false).ConfigureAwait(false);

				return;
			}

			await FarmingInitializationSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				if (NowFarming || Paused || !Bot.IsPlayingPossible) {
					return;
				}

				bool? isAnythingToFarm = await IsAnythingToFarm().ConfigureAwait(false);

				if (!isAnythingToFarm.HasValue) {
					return;
				}

				if (!isAnythingToFarm.Value) {
					Bot.ArchiLogger.LogGenericInfo(Strings.NothingToIdle);
					await Bot.OnFarmingFinished(false).ConfigureAwait(false);

					return;
				}

				if (GamesToFarm.Count == 0) {
					Bot.ArchiLogger.LogNullError(nameof(GamesToFarm));

					return;
				}

				// This is the last moment for final check if we can farm
				if (!Bot.IsPlayingPossible) {
					Bot.ArchiLogger.LogGenericInfo(Strings.PlayingNotAvailable);

					return;
				}

				if (Bot.PlayingWasBlocked) {
					Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.BotExtraIdlingCooldown, TimeSpan.FromSeconds(Bot.MinPlayingBlockedTTL).ToHumanReadable()));

					for (byte i = 0; (i < Bot.MinPlayingBlockedTTL) && Bot.IsPlayingPossible && Bot.PlayingWasBlocked; i++) {
						await Task.Delay(1000).ConfigureAwait(false);
					}

					if (!Bot.IsPlayingPossible) {
						Bot.ArchiLogger.LogGenericInfo(Strings.PlayingNotAvailable);

						return;
					}
				}

				KeepFarming = NowFarming = true;
				Utilities.InBackground(Farm, true);

				await PluginsCore.OnBotFarmingStarted(Bot).ConfigureAwait(false);
			} finally {
				FarmingInitializationSemaphore.Release();
			}
		}

		internal async Task StopFarming() {
			if (!NowFarming) {
				return;
			}

			await FarmingInitializationSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				if (!NowFarming) {
					return;
				}

				KeepFarming = false;

				for (byte i = 0; (i < byte.MaxValue) && NowFarming; i++) {
					if (FarmingResetSemaphore.CurrentCount == 0) {
						FarmingResetSemaphore.Release();
					}

					await Task.Delay(1000).ConfigureAwait(false);
				}

				if (NowFarming) {
					Bot.ArchiLogger.LogGenericError(Strings.WarningFailed);
					NowFarming = false;
				}

				Bot.ArchiLogger.LogGenericInfo(Strings.IdlingStopped);
				await Bot.OnFarmingStopped().ConfigureAwait(false);
			} finally {
				FarmingInitializationSemaphore.Release();
			}
		}

		private async Task CheckGame(uint appID, string name, float hours, byte badgeLevel) {
			if (appID == 0) {
				throw new ArgumentOutOfRangeException(nameof(appID));
			}

			if (string.IsNullOrEmpty(name)) {
				throw new ArgumentNullException(nameof(name));
			}

			if (hours < 0) {
				throw new ArgumentOutOfRangeException(nameof(hours));
			}

			ushort? cardsRemaining = await GetCardsRemaining(appID).ConfigureAwait(false);

			switch (cardsRemaining) {
				case null:
					Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningCouldNotCheckCardsStatus, appID, name));

					return;
				case 0:
					return;
				default:
					GamesToFarm.Add(new Game(appID, name, hours, cardsRemaining.Value, badgeLevel));

					break;
			}
		}

		private async void CheckGamesForFarming(object? state = null) {
			if (NowFarming || Paused || !Bot.IsConnectedAndLoggedOn) {
				return;
			}

			await StartFarming().ConfigureAwait(false);
		}

		private async Task CheckPage(IDocument htmlDocument, ISet<uint> parsedAppIDs) {
			if (htmlDocument == null) {
				throw new ArgumentNullException(nameof(htmlDocument));
			}

			if (parsedAppIDs == null) {
				throw new ArgumentNullException(nameof(parsedAppIDs));
			}

			IEnumerable<IElement> htmlNodes = htmlDocument.SelectNodes("//div[@class='badge_row_inner']");

			HashSet<Task>? backgroundTasks = null;

			foreach (IElement htmlNode in htmlNodes) {
				IElement? statsNode = htmlNode.SelectSingleElementNode(".//div[@class='badge_title_stats_content']");
				IElement? appIDNode = statsNode?.SelectSingleElementNode(".//div[@class='card_drop_info_dialog']");

				if (appIDNode == null) {
					// It's just a badge, nothing more
					continue;
				}

				string? appIDText = appIDNode.GetAttribute("id");

				if (string.IsNullOrEmpty(appIDText)) {
					Bot.ArchiLogger.LogNullError(nameof(appIDText));

					continue;
				}

				string[] appIDSplitted = appIDText.Split('_');

				if (appIDSplitted.Length < 5) {
					Bot.ArchiLogger.LogNullError(nameof(appIDSplitted));

					continue;
				}

				appIDText = appIDSplitted[4];

				if (!uint.TryParse(appIDText, out uint appID) || (appID == 0)) {
					Bot.ArchiLogger.LogNullError(nameof(appID));

					continue;
				}

				if (!parsedAppIDs.Add(appID)) {
					// Another task has already handled this appID
					continue;
				}

				if (SalesBlacklist.Contains(appID) || (ASF.GlobalConfig?.Blacklist.Contains(appID) == true) || Bot.IsBlacklistedFromIdling(appID) || (Bot.BotConfig.FarmPriorityQueueOnly && !Bot.IsPriorityIdling(appID))) {
					// We're configured to ignore this appID, so skip it
					continue;
				}

				bool ignored = false;

				foreach (ConcurrentDictionary<uint, DateTime> sourceOfIgnoredAppIDs in SourcesOfIgnoredAppIDs) {
					if (!sourceOfIgnoredAppIDs.TryGetValue(appID, out DateTime ignoredUntil)) {
						continue;
					}

					if (ignoredUntil > DateTime.UtcNow) {
						// This game is still ignored
						ignored = true;

						break;
					}

					// This game served its time as being ignored
					sourceOfIgnoredAppIDs.TryRemove(appID, out _);
				}

				if (ignored) {
					continue;
				}

				// Cards
				IElement? progressNode = statsNode?.SelectSingleElementNode(".//span[@class='progress_info_bold']");

				if (progressNode == null) {
					Bot.ArchiLogger.LogNullError(nameof(progressNode));

					continue;
				}

				string progressText = progressNode.TextContent;

				if (string.IsNullOrEmpty(progressText)) {
					Bot.ArchiLogger.LogNullError(nameof(progressText));

					continue;
				}

				ushort cardsRemaining = 0;
				Match progressMatch = Regex.Match(progressText, @"\d+");

				// This might fail if we have no card drops remaining, 0 is not printed in this case - that's fine
				if (progressMatch.Success) {
					if (!ushort.TryParse(progressMatch.Value, out cardsRemaining) || (cardsRemaining == 0)) {
						Bot.ArchiLogger.LogNullError(nameof(cardsRemaining));

						continue;
					}
				}

				if (cardsRemaining == 0) {
					// Normally we'd trust this information and simply skip the rest
					// However, Steam is so fucked up that we can't simply assume that it's correct
					// It's entirely possible that actual game page has different info, and badge page lied to us
					// We can't check every single game though, as this will literally kill people with cards from games they don't own
					// Luckily for us, it seems to happen only with some specific games
					if (!UntrustedAppIDs.Contains(appID)) {
						continue;
					}

					// To save us on extra work, check cards earned so far first
					IElement? cardsEarnedNode = statsNode?.SelectSingleElementNode(".//div[@class='card_drop_info_header']");

					if (cardsEarnedNode == null) {
						Bot.ArchiLogger.LogNullError(nameof(cardsEarnedNode));

						continue;
					}

					string cardsEarnedText = cardsEarnedNode.TextContent;

					if (string.IsNullOrEmpty(cardsEarnedText)) {
						Bot.ArchiLogger.LogNullError(nameof(cardsEarnedText));

						continue;
					}

					Match cardsEarnedMatch = Regex.Match(cardsEarnedText, @"\d+");

					if (!cardsEarnedMatch.Success) {
						Bot.ArchiLogger.LogNullError(nameof(cardsEarnedMatch));

						continue;
					}

					if (!ushort.TryParse(cardsEarnedMatch.Value, out ushort cardsEarned)) {
						Bot.ArchiLogger.LogNullError(nameof(cardsEarned));

						continue;
					}

					if (cardsEarned > 0) {
						// If we already earned some cards for this game, it's very likely that it's done
						// Let's hope that trusting cardsRemaining AND cardsEarned is enough
						// If I ever hear that it's not, I'll most likely need a doctor
						continue;
					}

					// If we have no cardsRemaining and no cardsEarned, it's either:
					// - A game we don't own physically, but we have cards from it in inventory
					// - F2P game that we didn't spend any money in, but we have cards from it in inventory
					// - Steam issue
					// As you can guess, we must follow the rest of the logic in case of Steam issue
				}

				// Hours
				IElement? timeNode = statsNode?.SelectSingleElementNode(".//div[@class='badge_title_stats_playtime']");

				if (timeNode == null) {
					Bot.ArchiLogger.LogNullError(nameof(timeNode));

					continue;
				}

				string hoursText = timeNode.TextContent;

				if (string.IsNullOrEmpty(hoursText)) {
					Bot.ArchiLogger.LogNullError(nameof(hoursText));

					continue;
				}

				float hours = 0.0F;
				Match hoursMatch = Regex.Match(hoursText, @"[0-9\.,]+");

				// This might fail if we have exactly 0.0 hours played, as it's not printed in that case - that's fine
				if (hoursMatch.Success) {
					if (!float.TryParse(hoursMatch.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out hours) || (hours <= 0.0F)) {
						Bot.ArchiLogger.LogNullError(nameof(hours));

						continue;
					}
				}

				// Names
				IElement? nameNode = statsNode?.SelectSingleElementNode("(.//div[@class='card_drop_info_body'])[last()]");

				if (nameNode == null) {
					Bot.ArchiLogger.LogNullError(nameof(nameNode));

					continue;
				}

				string name = nameNode.TextContent;

				if (string.IsNullOrEmpty(name)) {
					Bot.ArchiLogger.LogNullError(nameof(name));

					continue;
				}

				// We handle two cases here - normal one, and no card drops remaining
				int nameStartIndex = name.IndexOf(" by playing ", StringComparison.Ordinal);

				if (nameStartIndex <= 0) {
					nameStartIndex = name.IndexOf("You don't have any more drops remaining for ", StringComparison.Ordinal);

					if (nameStartIndex <= 0) {
						Bot.ArchiLogger.LogNullError(nameof(nameStartIndex));

						continue;
					}

					nameStartIndex += 32; // + 12 below
				}

				nameStartIndex += 12;

				int nameEndIndex = name.LastIndexOf('.');

				if (nameEndIndex <= nameStartIndex) {
					Bot.ArchiLogger.LogNullError(nameof(nameEndIndex));

					continue;
				}

				name = WebUtility.HtmlDecode(name[nameStartIndex..nameEndIndex]);

				if (string.IsNullOrEmpty(name)) {
					Bot.ArchiLogger.LogNullError(nameof(name));

					continue;
				}

				// Levels
				byte badgeLevel = 0;

				IElement? levelNode = htmlNode.SelectSingleElementNode(".//div[@class='badge_info_description']/div[2]");

				if (levelNode != null) {
					// There is no levelNode if we didn't craft that badge yet (level 0)
					string levelText = levelNode.TextContent;

					if (string.IsNullOrEmpty(levelText)) {
						Bot.ArchiLogger.LogNullError(nameof(levelText));

						continue;
					}

					int levelStartIndex = levelText.IndexOf("Level ", StringComparison.OrdinalIgnoreCase);

					if (levelStartIndex < 0) {
						Bot.ArchiLogger.LogNullError(nameof(levelStartIndex));

						continue;
					}

					levelStartIndex += 6;

					if (levelText.Length <= levelStartIndex) {
						Bot.ArchiLogger.LogNullError(nameof(levelStartIndex));

						continue;
					}

					int levelEndIndex = levelText.IndexOf(',', levelStartIndex);

					if (levelEndIndex <= levelStartIndex) {
						Bot.ArchiLogger.LogNullError(nameof(levelEndIndex));

						continue;
					}

					levelText = levelText[levelStartIndex..levelEndIndex];

					if (!byte.TryParse(levelText, out badgeLevel) || badgeLevel is 0 or > 5) {
						Bot.ArchiLogger.LogNullError(nameof(badgeLevel));

						continue;
					}
				}

				// Done with parsing, we have two possible cases here
				// Either we have decent info about appID, name, hours, cardsRemaining (cardsRemaining > 0) and level
				// OR we strongly believe that Steam lied to us, in this case we will need to check game individually (cardsRemaining == 0)
				if (cardsRemaining > 0) {
					GamesToFarm.Add(new Game(appID, name, hours, cardsRemaining, badgeLevel));
				} else {
					Task task = CheckGame(appID, name, hours, badgeLevel);

					switch (ASF.GlobalConfig?.OptimizationMode) {
						case GlobalConfig.EOptimizationMode.MinMemoryUsage:
							await task.ConfigureAwait(false);

							break;
						default:
							backgroundTasks ??= new HashSet<Task>();

							backgroundTasks.Add(task);

							break;
					}
				}
			}

			// If we have any background tasks, wait for them
			if (backgroundTasks?.Count > 0) {
				await Task.WhenAll(backgroundTasks).ConfigureAwait(false);
			}
		}

		private async Task CheckPage(byte page, ISet<uint> parsedAppIDs) {
			if (page == 0) {
				throw new ArgumentOutOfRangeException(nameof(page));
			}

			if (parsedAppIDs == null) {
				throw new ArgumentNullException(nameof(parsedAppIDs));
			}

			using IDocument? htmlDocument = await Bot.ArchiWebHandler.GetBadgePage(page).ConfigureAwait(false);

			if (htmlDocument == null) {
				return;
			}

			await CheckPage(htmlDocument, parsedAppIDs).ConfigureAwait(false);
		}

		private async Task Farm() {
			do {
				Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.GamesToIdle, GamesToFarm.Count, GamesToFarm.Sum(game => game.CardsRemaining), TimeRemaining.ToHumanReadable()));

				// Now the algorithm used for farming depends on whether account is restricted or not
				if (Bot.BotConfig.HoursUntilCardDrops > 0) {
					// If we have restricted card drops, we use complex algorithm
					Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.ChosenFarmingAlgorithm, "Complex"));

					while (GamesToFarm.Count > 0) {
						// Initially we're going to farm games that passed our HoursUntilCardDrops
						// This block is almost identical to Simple algorithm, we just copy appropriate items from GamesToFarm into innerGamesToFarm
						HashSet<Game> innerGamesToFarm = GamesToFarm.Where(game => game.HoursPlayed >= Bot.BotConfig.HoursUntilCardDrops).ToHashSet();

						while (innerGamesToFarm.Count > 0) {
							Game game = innerGamesToFarm.First();

							if (!await IsPlayableGame(game).ConfigureAwait(false)) {
								GamesToFarm.Remove(game);
								innerGamesToFarm.Remove(game);

								continue;
							}

							if (await FarmSolo(game).ConfigureAwait(false)) {
								innerGamesToFarm.Remove(game);

								continue;
							}

							NowFarming = false;

							return;
						}

						// At this point we have no games past HoursUntilCardDrops anymore, so we're going to farm all other ones
						// In order to maximize efficiency, we'll take games that are closest to our HoursPlayed first

						// We must call ToList() here as we can't remove items while enumerating
						foreach (Game game in GamesToFarm.OrderByDescending(game => game.HoursPlayed).ToList()) {
							if (!await IsPlayableGame(game).ConfigureAwait(false)) {
								GamesToFarm.Remove(game);

								continue;
							}

							innerGamesToFarm.Add(game);

							// There is no need to check all games at once, allow maximum of MaxGamesPlayedConcurrently in this batch
							if (innerGamesToFarm.Count >= ArchiHandler.MaxGamesPlayedConcurrently) {
								break;
							}
						}

						// If we have no playable games to farm, we're done
						if (innerGamesToFarm.Count == 0) {
							break;
						}

						// Otherwise, we farm our innerGamesToFarm batch until any game hits HoursUntilCardDrops
						if (await FarmMultiple(innerGamesToFarm).ConfigureAwait(false)) {
							Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.IdlingFinishedForGames, string.Join(", ", innerGamesToFarm.Select(game => game.AppID))));
						} else {
							NowFarming = false;

							return;
						}
					}
				} else {
					// If we have unrestricted card drops, we use simple algorithm
					Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.ChosenFarmingAlgorithm, "Simple"));

					while (GamesToFarm.Count > 0) {
						// In simple algorithm we're going to farm anything that is playable, regardless of hours
						Game game = GamesToFarm.First();

						if (!await IsPlayableGame(game).ConfigureAwait(false)) {
							GamesToFarm.Remove(game);

							continue;
						}

						if (await FarmSolo(game).ConfigureAwait(false)) {
							continue;
						}

						NowFarming = false;

						return;
					}
				}
			} while ((await IsAnythingToFarm().ConfigureAwait(false)).GetValueOrDefault());

			NowFarming = false;

			Bot.ArchiLogger.LogGenericInfo(Strings.IdlingFinished);
			await Bot.OnFarmingFinished(true).ConfigureAwait(false);
		}

		private async Task<bool> FarmCards(Game game) {
			if (game == null) {
				throw new ArgumentNullException(nameof(game));
			}

			if (game.AppID != game.PlayableAppID) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningIdlingGameMismatch, game.AppID, game.GameName, game.PlayableAppID));
			}

			await Bot.IdleGame(game).ConfigureAwait(false);

			bool success = true;
			DateTime endFarmingDate = DateTime.UtcNow.AddHours(ASF.GlobalConfig?.MaxFarmingTime ?? GlobalConfig.DefaultMaxFarmingTime);

			while ((DateTime.UtcNow < endFarmingDate) && (await ShouldFarm(game).ConfigureAwait(false)).GetValueOrDefault(true)) {
				Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.StillIdling, game.AppID, game.GameName));

				DateTime startFarmingPeriod = DateTime.UtcNow;

				if (await FarmingResetSemaphore.WaitAsync(((ASF.GlobalConfig?.FarmingDelay ?? GlobalConfig.DefaultFarmingDelay) * 60 * 1000) + (ExtraFarmingDelaySeconds * 1000)).ConfigureAwait(false)) {
					success = KeepFarming;
				}

				// Don't forget to update our GamesToFarm hours
				game.HoursPlayed += (float) DateTime.UtcNow.Subtract(startFarmingPeriod).TotalHours;

				if (!success) {
					break;
				}
			}

			Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.StoppedIdling, game.AppID, game.GameName));

			return success;
		}

		private async Task<bool> FarmHours(IReadOnlyCollection<Game> games) {
			if ((games == null) || (games.Count == 0)) {
				throw new ArgumentNullException(nameof(games));
			}

			float maxHour = games.Max(game => game.HoursPlayed);

			if (maxHour < 0) {
				Bot.ArchiLogger.LogNullError(nameof(maxHour));

				return false;
			}

			if (maxHour >= Bot.BotConfig.HoursUntilCardDrops) {
				Bot.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, nameof(maxHour)));

				return true;
			}

			await Bot.IdleGames(games).ConfigureAwait(false);

			bool success = true;

			while (maxHour < Bot.BotConfig.HoursUntilCardDrops) {
				Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.StillIdlingList, string.Join(", ", games.Select(game => game.AppID))));

				DateTime startFarmingPeriod = DateTime.UtcNow;

				if (await FarmingResetSemaphore.WaitAsync(((ASF.GlobalConfig?.FarmingDelay ?? GlobalConfig.DefaultFarmingDelay) * 60 * 1000) + (ExtraFarmingDelaySeconds * 1000)).ConfigureAwait(false)) {
					success = KeepFarming;
				}

				// Don't forget to update our GamesToFarm hours
				float timePlayed = (float) DateTime.UtcNow.Subtract(startFarmingPeriod).TotalHours;

				foreach (Game game in games) {
					game.HoursPlayed += timePlayed;
				}

				if (!success) {
					break;
				}

				maxHour += timePlayed;
			}

			Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.StoppedIdlingList, string.Join(", ", games.Select(game => game.AppID))));

			return success;
		}

		private async Task<bool> FarmMultiple(IReadOnlyCollection<Game> games) {
			if ((games == null) || (games.Count == 0)) {
				throw new ArgumentNullException(nameof(games));
			}

			CurrentGamesFarming.ReplaceWith(games);

			if (games.Count == 1) {
				Game game = games.First();
				Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.NowIdling, game.AppID, game.GameName));
			} else {
				Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.NowIdlingList, string.Join(", ", games.Select(game => game.AppID))));
			}

			bool result = await FarmHours(games).ConfigureAwait(false);
			CurrentGamesFarming.Clear();

			return result;
		}

		private async Task<bool> FarmSolo(Game game) {
			if (game == null) {
				throw new ArgumentNullException(nameof(game));
			}

			CurrentGamesFarming.Add(game);

			Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.NowIdling, game.AppID, game.GameName));

			bool result = await FarmCards(game).ConfigureAwait(false);
			CurrentGamesFarming.Clear();

			if (!result) {
				return false;
			}

			GamesToFarm.Remove(game);

			Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.IdlingFinishedForGame, game.AppID, game.GameName, TimeSpan.FromHours(game.HoursPlayed).ToHumanReadable()));

			return true;
		}

		private async Task<ushort?> GetCardsRemaining(uint appID) {
			if (appID == 0) {
				throw new ArgumentOutOfRangeException(nameof(appID));
			}

			using IDocument? htmlDocument = await Bot.ArchiWebHandler.GetGameCardsPage(appID).ConfigureAwait(false);

			IElement? progressNode = htmlDocument?.SelectSingleNode("//span[@class='progress_info_bold']");

			if (progressNode == null) {
				return null;
			}

			string progress = progressNode.TextContent;

			if (string.IsNullOrEmpty(progress)) {
				Bot.ArchiLogger.LogNullError(nameof(progress));

				return null;
			}

			Match match = Regex.Match(progress, @"\d+");

			if (!match.Success) {
				return 0;
			}

			if (!ushort.TryParse(match.Value, out ushort cardsRemaining) || (cardsRemaining == 0)) {
				Bot.ArchiLogger.LogNullError(nameof(cardsRemaining));

				return null;
			}

			return cardsRemaining;
		}

		private async Task<bool?> IsAnythingToFarm() {
			// Find the number of badge pages
			Bot.ArchiLogger.LogGenericInfo(Strings.CheckingFirstBadgePage);

			using IDocument? htmlDocument = await Bot.ArchiWebHandler.GetBadgePage(1).ConfigureAwait(false);

			if (htmlDocument == null) {
				Bot.ArchiLogger.LogGenericWarning(Strings.WarningCouldNotCheckBadges);

				return null;
			}

			byte maxPages = 1;

			IElement? htmlNode = htmlDocument.SelectSingleNode("(//a[@class='pagelink'])[last()]");

			if (htmlNode != null) {
				string lastPage = htmlNode.TextContent;

				if (string.IsNullOrEmpty(lastPage)) {
					Bot.ArchiLogger.LogNullError(nameof(lastPage));

					return null;
				}

				if (!byte.TryParse(lastPage, out maxPages) || (maxPages == 0)) {
					Bot.ArchiLogger.LogNullError(nameof(maxPages));

					return null;
				}
			}

			GamesToFarm.Clear();

			ConcurrentHashSet<uint> parsedAppIDs = new();

			Task mainTask = CheckPage(htmlDocument, parsedAppIDs);

			switch (ASF.GlobalConfig?.OptimizationMode) {
				case GlobalConfig.EOptimizationMode.MinMemoryUsage:
					await mainTask.ConfigureAwait(false);

					if (maxPages > 1) {
						Bot.ArchiLogger.LogGenericInfo(Strings.CheckingOtherBadgePages);

						for (byte page = 2; page <= maxPages; page++) {
							await CheckPage(page, parsedAppIDs).ConfigureAwait(false);
						}
					}

					break;
				default:
					HashSet<Task> tasks = new(maxPages) { mainTask };

					if (maxPages > 1) {
						Bot.ArchiLogger.LogGenericInfo(Strings.CheckingOtherBadgePages);

						for (byte page = 2; page <= maxPages; page++) {
							// ReSharper disable once InlineTemporaryVariable - we need a copy of variable being passed when in for loops, as loop will proceed before our task is launched
							byte currentPage = page;
							tasks.Add(CheckPage(currentPage, parsedAppIDs));
						}
					}

					await Task.WhenAll(tasks).ConfigureAwait(false);

					break;
			}

			if (GamesToFarm.Count == 0) {
				ShouldResumeFarming = false;

				return false;
			}

			ShouldResumeFarming = true;
			await SortGamesToFarm().ConfigureAwait(false);

			return true;
		}

		private async Task<bool> IsPlayableGame(Game game) {
			if (game == null) {
				throw new ArgumentNullException(nameof(game));
			}

			(uint playableAppID, DateTime ignoredUntil, bool ignoredGlobally) = await Bot.GetAppDataForIdling(game.AppID, game.HoursPlayed).ConfigureAwait(false);

			if (playableAppID == 0) {
				ConcurrentDictionary<uint, DateTime> ignoredAppIDs = ignoredGlobally ? GloballyIgnoredAppIDs : LocallyIgnoredAppIDs;

				ignoredAppIDs[game.AppID] = (ignoredUntil > DateTime.MinValue) && (ignoredUntil < DateTime.MaxValue) ? ignoredUntil : DateTime.UtcNow.AddHours(HoursToIgnore);
				Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.IdlingGameNotPossible, game.AppID, game.GameName));

				return false;
			}

			game.PlayableAppID = playableAppID;

			return true;
		}

		private async Task<bool?> ShouldFarm(Game game) {
			if (game == null) {
				throw new ArgumentNullException(nameof(game));
			}

			ushort? cardsRemaining = await GetCardsRemaining(game.AppID).ConfigureAwait(false);

			if (!cardsRemaining.HasValue) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningCouldNotCheckCardsStatus, game.AppID, game.GameName));

				return null;
			}

			game.CardsRemaining = cardsRemaining.Value;

			Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.IdlingStatusForGame, game.AppID, game.GameName, game.CardsRemaining));

			return game.CardsRemaining > 0;
		}

		private async Task SortGamesToFarm() {
			// Put priority idling appIDs on top
			IOrderedEnumerable<Game> orderedGamesToFarm = GamesToFarm.OrderByDescending(game => Bot.IsPriorityIdling(game.AppID));

			foreach (BotConfig.EFarmingOrder farmingOrder in Bot.BotConfig.FarmingOrders) {
				switch (farmingOrder) {
					case BotConfig.EFarmingOrder.Unordered:
						break;
					case BotConfig.EFarmingOrder.AppIDsAscending:
						orderedGamesToFarm = orderedGamesToFarm.ThenBy(game => game.AppID);

						break;
					case BotConfig.EFarmingOrder.AppIDsDescending:
						orderedGamesToFarm = orderedGamesToFarm.ThenByDescending(game => game.AppID);

						break;
					case BotConfig.EFarmingOrder.BadgeLevelsAscending:
						orderedGamesToFarm = orderedGamesToFarm.ThenBy(game => game.BadgeLevel);

						break;
					case BotConfig.EFarmingOrder.BadgeLevelsDescending:
						orderedGamesToFarm = orderedGamesToFarm.ThenByDescending(game => game.BadgeLevel);

						break;
					case BotConfig.EFarmingOrder.CardDropsAscending:
						orderedGamesToFarm = orderedGamesToFarm.ThenBy(game => game.CardsRemaining);

						break;
					case BotConfig.EFarmingOrder.CardDropsDescending:
						orderedGamesToFarm = orderedGamesToFarm.ThenByDescending(game => game.CardsRemaining);

						break;
					case BotConfig.EFarmingOrder.MarketableAscending:
					case BotConfig.EFarmingOrder.MarketableDescending:
						HashSet<uint>? marketableAppIDs = await Bot.GetMarketableAppIDs().ConfigureAwait(false);

						if (marketableAppIDs?.Count > 0) {
							ImmutableHashSet<uint> immutableMarketableAppIDs = marketableAppIDs.ToImmutableHashSet();

							switch (farmingOrder) {
								case BotConfig.EFarmingOrder.MarketableAscending:
									orderedGamesToFarm = orderedGamesToFarm.ThenBy(game => immutableMarketableAppIDs.Contains(game.AppID));

									break;
								case BotConfig.EFarmingOrder.MarketableDescending:
									orderedGamesToFarm = orderedGamesToFarm.ThenByDescending(game => immutableMarketableAppIDs.Contains(game.AppID));

									break;
								default:
									Bot.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(farmingOrder), farmingOrder));

									return;
							}
						}

						break;
					case BotConfig.EFarmingOrder.HoursAscending:
						orderedGamesToFarm = orderedGamesToFarm.ThenBy(game => game.HoursPlayed);

						break;
					case BotConfig.EFarmingOrder.HoursDescending:
						orderedGamesToFarm = orderedGamesToFarm.ThenByDescending(game => game.HoursPlayed);

						break;
					case BotConfig.EFarmingOrder.NamesAscending:
						orderedGamesToFarm = orderedGamesToFarm.ThenBy(game => game.GameName);

						break;
					case BotConfig.EFarmingOrder.NamesDescending:
						orderedGamesToFarm = orderedGamesToFarm.ThenByDescending(game => game.GameName);

						break;
					case BotConfig.EFarmingOrder.Random:
						orderedGamesToFarm = orderedGamesToFarm.ThenBy(_ => Utilities.RandomNext());

						break;
					case BotConfig.EFarmingOrder.RedeemDateTimesAscending:
					case BotConfig.EFarmingOrder.RedeemDateTimesDescending:
						Dictionary<uint, DateTime> redeemDates = new(GamesToFarm.Count);

						foreach (Game game in GamesToFarm) {
							DateTime redeemDate = DateTime.MinValue;
							HashSet<uint>? packageIDs = ASF.GlobalDatabase?.GetPackageIDs(game.AppID, Bot.OwnedPackageIDs.Keys);

							if (packageIDs != null) {
								foreach (uint packageID in packageIDs) {
									if (!Bot.OwnedPackageIDs.TryGetValue(packageID, out (EPaymentMethod PaymentMethod, DateTime TimeCreated) packageData)) {
										Bot.ArchiLogger.LogNullError(nameof(packageData));

										return;
									}

									if (packageData.TimeCreated > redeemDate) {
										redeemDate = packageData.TimeCreated;
									}
								}
							}

							redeemDates[game.AppID] = redeemDate;
						}

						ImmutableDictionary<uint, DateTime> immutableRedeemDates = redeemDates.ToImmutableDictionary();

						switch (farmingOrder) {
							case BotConfig.EFarmingOrder.RedeemDateTimesAscending:
								// ReSharper disable once AccessToModifiedClosure - you're wrong
								orderedGamesToFarm = orderedGamesToFarm.ThenBy(game => immutableRedeemDates[game.AppID]);

								break;
							case BotConfig.EFarmingOrder.RedeemDateTimesDescending:
								// ReSharper disable once AccessToModifiedClosure - you're wrong
								orderedGamesToFarm = orderedGamesToFarm.ThenByDescending(game => immutableRedeemDates[game.AppID]);

								break;
							default:
								Bot.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(farmingOrder), farmingOrder));

								return;
						}

						break;
					default:
						Bot.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(farmingOrder), farmingOrder));

						return;
				}
			}

			// We must call ToList() here as we can't do in-place replace
			List<Game> gamesToFarm = orderedGamesToFarm.ToList();
			GamesToFarm.ReplaceWith(gamesToFarm);
		}
	}
}
