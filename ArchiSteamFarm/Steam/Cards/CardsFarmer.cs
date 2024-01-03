//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2023 Łukasz "JustArchi" Domeradzki
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

using System;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Dom;
using AngleSharp.XPath;
using ArchiSteamFarm.Collections;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Plugins;
using ArchiSteamFarm.Steam.Data;
using ArchiSteamFarm.Steam.Integration;
using ArchiSteamFarm.Steam.Storage;
using ArchiSteamFarm.Storage;
using ArchiSteamFarm.Web;
using JetBrains.Annotations;
using Newtonsoft.Json;
using SteamKit2;

namespace ArchiSteamFarm.Steam.Cards;

public sealed class CardsFarmer : IAsyncDisposable, IDisposable {
	internal const byte DaysForRefund = 14; // In how many days since payment we're allowed to refund
	internal const byte HoursForRefund = 2; // Up to how many hours we're allowed to play for refund

	private const byte DaysToIgnoreRiskyAppIDs = 14; // How many days since determining that game is not candidate for idling, we assume that to still be the case, in risky approach
	private const byte ExtraFarmingDelaySeconds = 10; // In seconds, how much time to add on top of FarmingDelay (helps fighting misc time differences of Steam network)
	private const byte HoursToIgnore = 1; // How many hours we ignore unreleased appIDs and don't bother checking them again

	[PublicAPI]
	public static readonly FrozenSet<uint> SalesBlacklist = new HashSet<uint>(20) { 267420, 303700, 335590, 368020, 425280, 480730, 566020, 639900, 762800, 876740, 991980, 1195670, 1343890, 1465680, 1658760, 1797760, 2021850, 2243720, 2459330, 2640280 }.ToFrozenSet();

	private static readonly ConcurrentDictionary<uint, DateTime> GloballyIgnoredAppIDs = new(); // Reserved for unreleased games

	// Games that were confirmed to show false status on general badges page
	private static readonly FrozenSet<uint> UntrustedAppIDs = new HashSet<uint>(3) { 440, 570, 730 }.ToFrozenSet();

	[JsonProperty(nameof(CurrentGamesFarming))]
	[PublicAPI]
	public IReadOnlyCollection<Game> CurrentGamesFarmingReadOnly => CurrentGamesFarming;

	[JsonProperty(nameof(GamesToFarm))]
	[PublicAPI]
	public IReadOnlyCollection<Game> GamesToFarmReadOnly => GamesToFarm;

	[JsonProperty]
	[PublicAPI]
	public TimeSpan TimeRemaining {
		get {
			if (GamesToFarm.Count == 0) {
				return new TimeSpan(0);
			}

			byte hoursRequired = Bot.BotConfig.HoursUntilCardDrops;

			if (hoursRequired == 0) {
				// This is the simple case, one card drops each 30 minutes on average
				return TimeSpan.FromMinutes(GamesToFarm.Sum(static game => game.CardsRemaining) * 30);
			}

			// More advanced calculation, the above AND hours required for bumps
			uint cardsRemaining = 0;
			List<float> totalHoursClocked = [];

			foreach (Game gameToFarm in GamesToFarm) {
				cardsRemaining += gameToFarm.CardsRemaining;

				if (gameToFarm.HoursPlayed < hoursRequired) {
					totalHoursClocked.Add(gameToFarm.HoursPlayed);
				}
			}

			if (totalHoursClocked.Count == 0) {
				// Same as simple because we have no hours to bump
				return TimeSpan.FromMinutes(cardsRemaining * 30);
			}

			// Determine how many additional hours we'll waste on game bumps
			totalHoursClocked.Sort();

			double extraHours = 0;

			// Due to the fact that we have hours sorted, the lowest amount in each group is what we'll need for the entire group
			// This is still simplified as ASF will farm cards instead of hours ASAP, but it should give good enough approximation (if not the exact value)
			for (int i = 0; i < totalHoursClocked.Count; i += ArchiHandler.MaxGamesPlayedConcurrently) {
				float hoursClocked = totalHoursClocked[i];

				extraHours += hoursRequired - hoursClocked;
			}

			return TimeSpan.FromHours(extraHours) + TimeSpan.FromMinutes(cardsRemaining * 30);
		}
	}

	private readonly Bot Bot;
	private readonly ConcurrentHashSet<Game> CurrentGamesFarming = [];
	private readonly SemaphoreSlim EventSemaphore = new(1, 1);
	private readonly SemaphoreSlim FarmingInitializationSemaphore = new(1, 1);
	private readonly SemaphoreSlim FarmingResetSemaphore = new(0, 1);
	private readonly ConcurrentList<Game> GamesToFarm = [];
	private readonly Timer? IdleFarmingTimer;

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
	private bool ShouldResumeFarming;
	private bool ShouldSkipNewGamesIfPossible;

	internal CardsFarmer(Bot bot) {
		ArgumentNullException.ThrowIfNull(bot);

		Bot = bot;

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

	public void Dispose() {
		// Those are objects that are always being created if constructor doesn't throw exception
		EventSemaphore.Dispose();
		FarmingInitializationSemaphore.Dispose();
		FarmingResetSemaphore.Dispose();

		// Those are objects that might be null and the check should be in-place
		IdleFarmingTimer?.Dispose();
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

		// We aim to have a maximum of 2 tasks, one already parsing, and one waiting in the queue
		// This way we can call this function as many times as needed e.g. because of Steam events
		ShouldResumeFarming = true;

		// ReSharper disable once SuspiciousLockOverSynchronizationPrimitive - this is not a mistake, we need extra synchronization, and we can re-use the semaphore object for that
		lock (EventSemaphore) {
			if (ParsingScheduled) {
				return;
			}

			ParsingScheduled = true;
		}

		await EventSemaphore.WaitAsync().ConfigureAwait(false);

		try {
			// ReSharper disable once SuspiciousLockOverSynchronizationPrimitive - this is not a mistake, we need extra synchronization, and we can re-use the semaphore object for that
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
			if (!ShouldSkipNewGamesIfPossible && ((Bot.BotConfig.HoursUntilCardDrops > 0) || ((Bot.BotConfig.FarmingOrders.Count > 0) && Bot.BotConfig.FarmingOrders.Any(static farmingOrder => (farmingOrder != BotConfig.EFarmingOrder.Unordered) && (farmingOrder != BotConfig.EFarmingOrder.Random))))) {
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
		if (Bot.BotConfig is { SendOnFarmingFinished: true, LootableTypes.Count: > 0 }) {
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
		ShouldResumeFarming = ShouldSkipNewGamesIfPossible = false;
	}

	internal async Task StartFarming() {
		if (NowFarming || Paused || !Bot.IsPlayingPossible) {
			return;
		}

		if (!Bot.CanReceiveSteamCards || (Bot.BotConfig.FarmPriorityQueueOnly && (Bot.BotDatabase.FarmingPriorityQueueAppIDs.Count == 0))) {
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
				Bot.ArchiLogger.LogNullError(GamesToFarm);

				return;
			}

			// This is the last moment for final check if we can farm
			if (!Bot.IsPlayingPossible) {
				Bot.ArchiLogger.LogGenericInfo(Strings.PlayingNotAvailable);

				return;
			}

			if (Bot.PlayingWasBlocked) {
				byte minFarmingDelayAfterBlock = ASF.GlobalConfig?.MinFarmingDelayAfterBlock ?? GlobalConfig.DefaultMinFarmingDelayAfterBlock;

				if (minFarmingDelayAfterBlock > 0) {
					Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.BotExtraIdlingCooldown, TimeSpan.FromSeconds(minFarmingDelayAfterBlock).ToHumanReadable()));

					for (byte i = 0; (i < minFarmingDelayAfterBlock) && Bot is { IsConnectedAndLoggedOn: true, IsPlayingPossible: true, PlayingWasBlocked: true }; i++) {
						await Task.Delay(1000).ConfigureAwait(false);
					}

					if (!Bot.IsConnectedAndLoggedOn) {
						return;
					}

					if (!Bot.IsPlayingPossible) {
						Bot.ArchiLogger.LogGenericInfo(Strings.PlayingNotAvailable);

						return;
					}
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
		ArgumentOutOfRangeException.ThrowIfZero(appID);
		ArgumentException.ThrowIfNullOrEmpty(name);
		ArgumentOutOfRangeException.ThrowIfNegative(hours);

		Game? game = await GetGameCardsInfo(appID).ConfigureAwait(false);

		if (game == null) {
			Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningCouldNotCheckCardsStatus, appID, name));

			return;
		}

		if (game.CardsRemaining > 0) {
			Bot.BotDatabase.FarmingRiskyPrioritizedAppIDs.Add(appID);

			GamesToFarm.Add(new Game(appID, name, hours, game.CardsRemaining, badgeLevel));
		}
	}

	private async void CheckGamesForFarming(object? state = null) {
		if (NowFarming || Paused || !Bot.IsConnectedAndLoggedOn) {
			return;
		}

		await StartFarming().ConfigureAwait(false);
	}

	private async Task CheckPage(IDocument htmlDocument, ISet<uint> parsedAppIDs) {
		ArgumentNullException.ThrowIfNull(htmlDocument);
		ArgumentNullException.ThrowIfNull(parsedAppIDs);

		IEnumerable<IElement> htmlNodes = htmlDocument.SelectNodes<IElement>("//div[@class='badge_row_inner']");

		HashSet<Task>? backgroundTasks = null;

		foreach (IElement htmlNode in htmlNodes) {
			IElement? statsNode = htmlNode.SelectSingleNode<IElement>(".//div[@class='badge_title_stats_content']");
			IAttr? appIDNode = statsNode?.SelectSingleNode<IAttr>(".//div[@class='card_drop_info_dialog']/@id");

			if (appIDNode == null) {
				// It's just a badge, nothing more
				continue;
			}

			string appIDText = appIDNode.Value;

			if (string.IsNullOrEmpty(appIDText)) {
				Bot.ArchiLogger.LogNullError(appIDText);

				continue;
			}

			string[] appIDSplitted = appIDText.Split('_', 6);

			if (appIDSplitted.Length < 5) {
				Bot.ArchiLogger.LogNullError(appIDSplitted);

				continue;
			}

			appIDText = appIDSplitted[4];

			if (!uint.TryParse(appIDText, out uint appID) || (appID == 0)) {
				Bot.ArchiLogger.LogNullError(appID);

				continue;
			}

			if (!parsedAppIDs.Add(appID)) {
				// Another task has already handled this appID
				continue;
			}

			if (!ShouldIdle(appID)) {
				// No point in evaluating further if we can determine that on appID alone
				continue;
			}

			// Cards
			INode? progressNode = statsNode?.SelectSingleNode(".//span[@class='progress_info_bold']");

			if (progressNode == null) {
				Bot.ArchiLogger.LogNullError(progressNode);

				continue;
			}

			string progressText = progressNode.TextContent;

			if (string.IsNullOrEmpty(progressText)) {
				Bot.ArchiLogger.LogNullError(progressText);

				continue;
			}

			ushort cardsRemaining = 0;
			Match progressMatch = GeneratedRegexes.Digits().Match(progressText);

			// This might fail if we have no card drops remaining, 0 is not printed in this case - that's fine
			if (progressMatch.Success) {
				if (!ushort.TryParse(progressMatch.Value, out cardsRemaining) || (cardsRemaining == 0)) {
					Bot.ArchiLogger.LogNullError(cardsRemaining);

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
				INode? cardsEarnedNode = statsNode?.SelectSingleNode(".//div[@class='card_drop_info_header']");

				if (cardsEarnedNode == null) {
					Bot.ArchiLogger.LogNullError(cardsEarnedNode);

					continue;
				}

				string cardsEarnedText = cardsEarnedNode.TextContent;

				if (string.IsNullOrEmpty(cardsEarnedText)) {
					Bot.ArchiLogger.LogNullError(cardsEarnedText);

					continue;
				}

				Match cardsEarnedMatch = GeneratedRegexes.Digits().Match(cardsEarnedText);

				if (!cardsEarnedMatch.Success) {
					Bot.ArchiLogger.LogNullError(cardsEarnedMatch);

					continue;
				}

				if (!ushort.TryParse(cardsEarnedMatch.Value, out ushort cardsEarned)) {
					Bot.ArchiLogger.LogNullError(cardsEarned);

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
			INode? timeNode = statsNode?.SelectSingleNode(".//div[@class='badge_title_stats_playtime']");

			if (timeNode == null) {
				Bot.ArchiLogger.LogNullError(timeNode);

				continue;
			}

			string hoursText = timeNode.TextContent;

			if (string.IsNullOrEmpty(hoursText)) {
				Bot.ArchiLogger.LogNullError(hoursText);

				continue;
			}

			float hours = 0.0F;
			Match hoursMatch = GeneratedRegexes.Decimal().Match(hoursText);

			// This might fail if we have exactly 0.0 hours played, as it's not printed in that case - that's fine
			if (hoursMatch.Success) {
				if (!float.TryParse(hoursMatch.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out hours) || (hours <= 0.0F)) {
					Bot.ArchiLogger.LogNullError(hours);

					continue;
				}
			}

			// Names
			INode? nameNode = statsNode?.SelectSingleNode("(.//div[@class='card_drop_info_body'])[last()]");

			if (nameNode == null) {
				Bot.ArchiLogger.LogNullError(nameNode);

				continue;
			}

			string name = nameNode.TextContent;

			if (string.IsNullOrEmpty(name)) {
				Bot.ArchiLogger.LogNullError(name);

				continue;
			}

			// We handle two cases here - normal one, and no card drops remaining
			int nameStartIndex = name.IndexOf(" by playing ", StringComparison.Ordinal);

			if (nameStartIndex <= 0) {
				nameStartIndex = name.IndexOf("You don't have any more drops remaining for ", StringComparison.Ordinal);

				if (nameStartIndex <= 0) {
					Bot.ArchiLogger.LogNullError(nameStartIndex);

					continue;
				}

				nameStartIndex += 32; // + 12 below
			}

			nameStartIndex += 12;

			int nameEndIndex = name.LastIndexOf('.');

			if (nameEndIndex <= nameStartIndex) {
				Bot.ArchiLogger.LogNullError(nameEndIndex);

				continue;
			}

			name = Uri.UnescapeDataString(name[nameStartIndex..nameEndIndex]);

			if (string.IsNullOrEmpty(name)) {
				Bot.ArchiLogger.LogNullError(name);

				continue;
			}

			// Levels
			byte badgeLevel = 0;

			INode? levelNode = htmlNode.SelectSingleNode(".//div[@class='badge_info_description']/div[2]");

			if (levelNode != null) {
				// There is no levelNode if we didn't craft that badge yet (level 0)
				string levelText = levelNode.TextContent;

				if (string.IsNullOrEmpty(levelText)) {
					Bot.ArchiLogger.LogNullError(levelText);

					continue;
				}

				int levelStartIndex = levelText.IndexOf("Level ", StringComparison.OrdinalIgnoreCase);

				if (levelStartIndex < 0) {
					Bot.ArchiLogger.LogNullError(levelStartIndex);

					continue;
				}

				levelStartIndex += 6;

				if (levelText.Length <= levelStartIndex) {
					Bot.ArchiLogger.LogNullError(levelStartIndex);

					continue;
				}

				int levelEndIndex = levelText.IndexOf(',', levelStartIndex);

				if (levelEndIndex <= levelStartIndex) {
					Bot.ArchiLogger.LogNullError(levelEndIndex);

					continue;
				}

				levelText = levelText[levelStartIndex..levelEndIndex];

				if (!byte.TryParse(levelText, out badgeLevel) || badgeLevel is 0 or > 5) {
					Bot.ArchiLogger.LogNullError(badgeLevel);

					continue;
				}
			}

			// Done with parsing, we have two possible cases here
			// Either we have decent info about appID, name, hours, cardsRemaining (cardsRemaining > 0) and level
			// OR we strongly believe that Steam lied to us, in this case we will need to check game individually (cardsRemaining == 0)
			if (cardsRemaining > 0) {
				Bot.BotDatabase.FarmingRiskyPrioritizedAppIDs.Add(appID);

				GamesToFarm.Add(new Game(appID, name, hours, cardsRemaining, badgeLevel));
			} else {
				Task task = CheckGame(appID, name, hours, badgeLevel);

				switch (ASF.GlobalConfig?.OptimizationMode) {
					case GlobalConfig.EOptimizationMode.MinMemoryUsage:
						await task.ConfigureAwait(false);

						break;
					default:
						backgroundTasks ??= [];

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

	private async Task<bool> CheckPage(byte page, ISet<uint> parsedAppIDs) {
		ArgumentOutOfRangeException.ThrowIfZero(page);
		ArgumentNullException.ThrowIfNull(parsedAppIDs);

		using IDocument? htmlDocument = await Bot.ArchiWebHandler.GetBadgePage(page).ConfigureAwait(false);

		if (htmlDocument == null) {
			return false;
		}

		await CheckPage(htmlDocument, parsedAppIDs).ConfigureAwait(false);

		return true;
	}

	private async Task Farm() {
		do {
			Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.GamesToIdle, GamesToFarm.Count, GamesToFarm.Sum(static game => game.CardsRemaining), TimeRemaining.ToHumanReadable()));

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
					foreach (Game game in GamesToFarm.OrderByDescending(static game => game.HoursPlayed).ToList()) {
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
						Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.IdlingFinishedForGames, string.Join(", ", innerGamesToFarm.Select(static game => game.AppID))));
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
		ArgumentNullException.ThrowIfNull(game);

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

		float maxHour = games.Max(static game => game.HoursPlayed);

		if (maxHour < 0) {
			Bot.ArchiLogger.LogNullError(maxHour);

			return false;
		}

		if (maxHour >= Bot.BotConfig.HoursUntilCardDrops) {
			Bot.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, nameof(maxHour)));

			return true;
		}

		await Bot.IdleGames(games).ConfigureAwait(false);

		bool success = true;

		while (maxHour < Bot.BotConfig.HoursUntilCardDrops) {
			Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.StillIdlingList, string.Join(", ", games.Select(static game => game.AppID))));

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

		Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.StoppedIdlingList, string.Join(", ", games.Select(static game => game.AppID))));

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
			Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.NowIdlingList, string.Join(", ", games.Select(static game => game.AppID))));
		}

		bool result = await FarmHours(games).ConfigureAwait(false);
		CurrentGamesFarming.Clear();

		return result;
	}

	private async Task<bool> FarmSolo(Game game) {
		ArgumentNullException.ThrowIfNull(game);

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

	private async Task<Game?> GetGameCardsInfo(uint appID) {
		ArgumentOutOfRangeException.ThrowIfZero(appID);

		using IDocument? htmlDocument = await Bot.ArchiWebHandler.GetGameCardsPage(appID).ConfigureAwait(false);

		if (htmlDocument == null) {
			return null;
		}

		INode? nameNode = htmlDocument.SelectSingleNode("(//span[@class='profile_small_header_location'])[last()]");

		if (nameNode == null) {
			Bot.ArchiLogger.LogNullError(nameNode);

			return null;
		}

		string name = nameNode.TextContent;

		if (string.IsNullOrEmpty(name)) {
			Bot.ArchiLogger.LogNullError(name);

			return null;
		}

		INode? hoursNode = htmlDocument.SelectSingleNode("//div[@class='badge_title_stats_playtime']");

		if (hoursNode == null) {
			Bot.ArchiLogger.LogNullError(hoursNode);

			return null;
		}

		float hours = 0.0F;
		Match hoursMatch = GeneratedRegexes.Decimal().Match(hoursNode.TextContent);

		// This might fail if we have exactly 0.0 hours played, as it's not printed in that case - that's fine
		if (hoursMatch.Success) {
			if (!float.TryParse(hoursMatch.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out hours) || (hours <= 0.0F)) {
				Bot.ArchiLogger.LogNullError(hours);

				return null;
			}
		}

		INode? progressNode = htmlDocument.SelectSingleNode("//span[@class='progress_info_bold']");

		if (progressNode == null) {
			Bot.ArchiLogger.LogNullError(progressNode);

			return null;
		}

		string progress = progressNode.TextContent;

		if (string.IsNullOrEmpty(progress)) {
			Bot.ArchiLogger.LogNullError(progress);

			return null;
		}

		ushort cardsRemaining = 0;

		Match match = GeneratedRegexes.Digits().Match(progress);

		if (match.Success) {
			if (!ushort.TryParse(match.Value, out cardsRemaining) || (cardsRemaining == 0)) {
				Bot.ArchiLogger.LogNullError(cardsRemaining);

				return null;
			}
		}

		byte badgeLevel = 0;

		INode? levelNode = htmlDocument.SelectSingleNode("//div[@class='badge_info_description']/div[2]");

		// There is no levelNode if we didn't craft that badge yet (level 0)
		if (levelNode != null) {
			string levelText = levelNode.TextContent;

			if (string.IsNullOrEmpty(levelText)) {
				Bot.ArchiLogger.LogNullError(levelText);

				return null;
			}

			int levelStartIndex = levelText.IndexOf("Level ", StringComparison.OrdinalIgnoreCase);

			if (levelStartIndex < 0) {
				Bot.ArchiLogger.LogNullError(levelStartIndex);

				return null;
			}

			levelStartIndex += 6;

			if (levelText.Length <= levelStartIndex) {
				Bot.ArchiLogger.LogNullError(levelStartIndex);

				return null;
			}

			int levelEndIndex = levelText.IndexOf(',', levelStartIndex);

			if (levelEndIndex <= levelStartIndex) {
				Bot.ArchiLogger.LogNullError(levelEndIndex);

				return null;
			}

			levelText = levelText[levelStartIndex..levelEndIndex];

			if (!byte.TryParse(levelText, out badgeLevel) || badgeLevel is 0 or > 5) {
				Bot.ArchiLogger.LogNullError(badgeLevel);

				return null;
			}
		}

		return new Game(appID, name, hours, cardsRemaining, badgeLevel);
	}

	private async Task<bool?> IsAnythingToFarm() {
		// Find the number of badge pages
		Bot.ArchiLogger.LogGenericInfo(Strings.CheckingFirstBadgePage);

		using IDocument? htmlDocument = await Bot.ArchiWebHandler.GetBadgePage(1, Bot.BotConfig.EnableRiskyCardsDiscovery ? (byte) 2 : WebBrowser.MaxTries).ConfigureAwait(false);

		if (htmlDocument == null) {
			Bot.ArchiLogger.LogGenericWarning(Strings.WarningCouldNotCheckBadges);

			if (!Bot.BotConfig.EnableRiskyCardsDiscovery) {
				return null;
			}

			return await IsAnythingToFarmRisky().ConfigureAwait(false);
		}

		ShouldSkipNewGamesIfPossible = false;

		byte maxPages = 1;

		INode? htmlNode = htmlDocument.SelectSingleNode("(//a[@class='pagelink'])[last()]");

		if (htmlNode != null) {
			string lastPage = htmlNode.TextContent;

			if (string.IsNullOrEmpty(lastPage)) {
				Bot.ArchiLogger.LogNullError(lastPage);

				return null;
			}

			if (!byte.TryParse(lastPage, out maxPages) || (maxPages == 0)) {
				Bot.ArchiLogger.LogNullError(maxPages);

				return null;
			}
		}

		GamesToFarm.Clear();

		ConcurrentHashSet<uint> parsedAppIDs = [];

		Task mainTask = CheckPage(htmlDocument, parsedAppIDs);

		bool allTasksSucceeded = true;

		switch (ASF.GlobalConfig?.OptimizationMode) {
			case GlobalConfig.EOptimizationMode.MinMemoryUsage:
				await mainTask.ConfigureAwait(false);

				if (maxPages > 1) {
					Bot.ArchiLogger.LogGenericInfo(Strings.CheckingOtherBadgePages);

					for (byte page = 2; page <= maxPages; page++) {
						if (!await CheckPage(page, parsedAppIDs).ConfigureAwait(false)) {
							allTasksSucceeded = false;
						}
					}
				}

				break;
			default:
				if (maxPages > 1) {
					Bot.ArchiLogger.LogGenericInfo(Strings.CheckingOtherBadgePages);

					HashSet<Task<bool>> tasks = new(maxPages - 1);

					for (byte page = 2; page <= maxPages; page++) {
						// ReSharper disable once InlineTemporaryVariable - we need a copy of variable being passed when in for loops, as loop will proceed before our task is launched
						byte currentPage = page;
						tasks.Add(CheckPage(currentPage, parsedAppIDs));
					}

					bool[] taskResults = await Task.WhenAll(tasks).ConfigureAwait(false);

					if (taskResults.Any(static result => !result)) {
						allTasksSucceeded = false;
					}
				}

				await mainTask.ConfigureAwait(false);

				break;
		}

		if (allTasksSucceeded) {
			Bot.BotDatabase.FarmingRiskyPrioritizedAppIDs.IntersectWith(GamesToFarm.Select(static game => game.AppID));
		}

		if (GamesToFarm.Count == 0) {
			ShouldResumeFarming = false;

			// Allow changing to risky algorithm only if we failed at least some badge pages and we have the prop enabled
			if (allTasksSucceeded || !Bot.BotConfig.EnableRiskyCardsDiscovery) {
				return false;
			}

			return await IsAnythingToFarmRisky().ConfigureAwait(false);
		}

		ShouldResumeFarming = true;
		await SortGamesToFarm().ConfigureAwait(false);

		return true;
	}

	private async Task<bool?> IsAnythingToFarmRisky() {
		Task<ImmutableHashSet<BoosterCreatorEntry>?> boosterCreatorEntriesTask = Bot.ArchiWebHandler.GetBoosterCreatorEntries();

		HashSet<uint>? boosterElibility = await Bot.ArchiWebHandler.GetBoosterEligibility().ConfigureAwait(false);

		if (boosterElibility == null) {
			Bot.ArchiLogger.LogGenericWarning(Strings.WarningCouldNotCheckBadges);

			return null;
		}

		ImmutableHashSet<BoosterCreatorEntry>? boosterCreatorEntries = await boosterCreatorEntriesTask.ConfigureAwait(false);

		if (boosterCreatorEntries == null) {
			Bot.ArchiLogger.LogGenericWarning(Strings.WarningCouldNotCheckBadges);

			return null;
		}

		GamesToFarm.Clear();

		DateTime now = DateTime.UtcNow;

		byte failuresInRow = 0;

		// Normally we apply ordering after GamesToFarm are already found, but since this method is risky and greedy, we do as much as possible to allow user to optimize it
		// In particular, firstly we give priority to appIDs that we already found out before, either rule them out, or prioritize
		// Next, we apply farm priority queue right away, by both considering apps (if FarmPriorityQueueOnly) as well as giving priority to those that user specified
		// Lastly, we forcefully apply random order to those considered the same in value, as we can't really afford massive amount of misses in a row
		HashSet<uint> gamesToFarm = boosterCreatorEntries.Select(static entry => entry.AppID).Where(appID => !boosterElibility.Contains(appID) && (!Bot.BotDatabase.FarmingRiskyIgnoredAppIDs.TryGetValue(appID, out DateTime ignoredUntil) || (ignoredUntil < now)) && ShouldIdle(appID)).ToHashSet();

		foreach (uint appID in Bot.BotDatabase.FarmingRiskyIgnoredAppIDs.Keys.Where(appID => !gamesToFarm.Contains(appID))) {
			Bot.BotDatabase.FarmingRiskyIgnoredAppIDs.Remove(appID);
		}

		Bot.BotDatabase.FarmingRiskyPrioritizedAppIDs.IntersectWith(gamesToFarm);

#pragma warning disable CA5394 // This call isn't used in a security-sensitive manner
		IOrderedEnumerable<uint> gamesToFarmOrdered = gamesToFarm.OrderByDescending(Bot.BotDatabase.FarmingRiskyPrioritizedAppIDs.Contains).ThenByDescending(Bot.IsPriorityIdling).ThenBy(static _ => Random.Shared.Next());
#pragma warning restore CA5394 // This call isn't used in a security-sensitive manner

		DateTime ignoredUntil = now.AddDays(DaysToIgnoreRiskyAppIDs);

		foreach (uint appID in gamesToFarmOrdered) {
			Game? game = await GetGameCardsInfo(appID).ConfigureAwait(false);

			if (game == null) {
				if (++failuresInRow >= WebBrowser.MaxTries) {
					// We're not going to check further
					break;
				}

				continue;
			}

			failuresInRow = 0;

			if (game.CardsRemaining == 0) {
				Bot.BotDatabase.FarmingRiskyIgnoredAppIDs[appID] = ignoredUntil;
				Bot.BotDatabase.FarmingRiskyPrioritizedAppIDs.Remove(appID);

				continue;
			}

			Bot.BotDatabase.FarmingRiskyPrioritizedAppIDs.Add(appID);

			GamesToFarm.Add(game);

			if ((game.HoursPlayed >= Bot.BotConfig.HoursUntilCardDrops) || (GamesToFarm.Count >= ArchiHandler.MaxGamesPlayedConcurrently)) {
				// Avoid further parsing in this risky method, we have enough for now
				break;
			}
		}

		if (GamesToFarm.Count == 0) {
			ShouldResumeFarming = ShouldSkipNewGamesIfPossible = false;

			return false;
		}

		ShouldResumeFarming = ShouldSkipNewGamesIfPossible = true;
		await SortGamesToFarm().ConfigureAwait(false);

		return true;
	}

	private async Task<bool> IsPlayableGame(Game game) {
		ArgumentNullException.ThrowIfNull(game);

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
		ArgumentNullException.ThrowIfNull(game);

		Game? latestGameData = await GetGameCardsInfo(game.AppID).ConfigureAwait(false);

		if (latestGameData == null) {
			Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningCouldNotCheckCardsStatus, game.AppID, game.GameName));

			return null;
		}

		game.CardsRemaining = latestGameData.CardsRemaining;

		Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.IdlingStatusForGame, game.AppID, game.GameName, game.CardsRemaining));

		if (game.CardsRemaining == 0) {
			Bot.BotDatabase.FarmingRiskyIgnoredAppIDs[game.AppID] = DateTime.UtcNow.AddDays(DaysToIgnoreRiskyAppIDs);
			Bot.BotDatabase.FarmingRiskyPrioritizedAppIDs.Remove(game.AppID);

			return false;
		}

		return true;
	}

	private bool ShouldIdle(uint appID) {
		ArgumentOutOfRangeException.ThrowIfZero(appID);

		if (SalesBlacklist.Contains(appID) || (ASF.GlobalConfig?.Blacklist.Contains(appID) == true) || Bot.IsBlacklistedFromIdling(appID) || (Bot.BotConfig.FarmPriorityQueueOnly && !Bot.IsPriorityIdling(appID))) {
			// We're configured to ignore this appID, so skip it
			return false;
		}

		foreach (ConcurrentDictionary<uint, DateTime> sourceOfIgnoredAppIDs in SourcesOfIgnoredAppIDs) {
			if (!sourceOfIgnoredAppIDs.TryGetValue(appID, out DateTime ignoredUntil)) {
				continue;
			}

			if (ignoredUntil > DateTime.UtcNow) {
				// This game is still ignored
				return false;
			}

			// This game served its time as being ignored
			sourceOfIgnoredAppIDs.TryRemove(appID, out _);
		}

		return true;
	}

	private async Task SortGamesToFarm() {
		// Put priority idling appIDs on top
		IOrderedEnumerable<Game> orderedGamesToFarm = GamesToFarm.OrderByDescending(game => Bot.IsPriorityIdling(game.AppID));

		foreach (BotConfig.EFarmingOrder farmingOrder in Bot.BotConfig.FarmingOrders) {
			switch (farmingOrder) {
				case BotConfig.EFarmingOrder.Unordered:
					break;
				case BotConfig.EFarmingOrder.AppIDsAscending:
					orderedGamesToFarm = orderedGamesToFarm.ThenBy(static game => game.AppID);

					break;
				case BotConfig.EFarmingOrder.AppIDsDescending:
					orderedGamesToFarm = orderedGamesToFarm.ThenByDescending(static game => game.AppID);

					break;
				case BotConfig.EFarmingOrder.BadgeLevelsAscending:
					orderedGamesToFarm = orderedGamesToFarm.ThenBy(static game => game.BadgeLevel);

					break;
				case BotConfig.EFarmingOrder.BadgeLevelsDescending:
					orderedGamesToFarm = orderedGamesToFarm.ThenByDescending(static game => game.BadgeLevel);

					break;
				case BotConfig.EFarmingOrder.CardDropsAscending:
					orderedGamesToFarm = orderedGamesToFarm.ThenBy(static game => game.CardsRemaining);

					break;
				case BotConfig.EFarmingOrder.CardDropsDescending:
					orderedGamesToFarm = orderedGamesToFarm.ThenByDescending(static game => game.CardsRemaining);

					break;
				case BotConfig.EFarmingOrder.MarketableAscending:
				case BotConfig.EFarmingOrder.MarketableDescending:
					HashSet<uint>? marketableAppIDs = await Bot.GetMarketableAppIDs().ConfigureAwait(false);

					if (marketableAppIDs?.Count > 0) {
						HashSet<uint> marketableAppIDsCopy = marketableAppIDs;

						orderedGamesToFarm = farmingOrder switch {
							BotConfig.EFarmingOrder.MarketableAscending => orderedGamesToFarm.ThenBy(game => marketableAppIDsCopy.Contains(game.AppID)),
							BotConfig.EFarmingOrder.MarketableDescending => orderedGamesToFarm.ThenByDescending(game => marketableAppIDsCopy.Contains(game.AppID)),
							_ => throw new InvalidOperationException(nameof(farmingOrder))
						};
					}

					break;
				case BotConfig.EFarmingOrder.HoursAscending:
					orderedGamesToFarm = orderedGamesToFarm.ThenBy(static game => game.HoursPlayed);

					break;
				case BotConfig.EFarmingOrder.HoursDescending:
					orderedGamesToFarm = orderedGamesToFarm.ThenByDescending(static game => game.HoursPlayed);

					break;
				case BotConfig.EFarmingOrder.NamesAscending:
					orderedGamesToFarm = orderedGamesToFarm.ThenBy(static game => game.GameName);

					break;
				case BotConfig.EFarmingOrder.NamesDescending:
					orderedGamesToFarm = orderedGamesToFarm.ThenByDescending(static game => game.GameName);

					break;
				case BotConfig.EFarmingOrder.Random:
#pragma warning disable CA5394 // This call isn't used in a security-sensitive manner
					orderedGamesToFarm = orderedGamesToFarm.ThenBy(static _ => Random.Shared.Next());
#pragma warning restore CA5394 // This call isn't used in a security-sensitive manner

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
									Bot.ArchiLogger.LogNullError(packageData);

									return;
								}

								if (packageData.TimeCreated > redeemDate) {
									redeemDate = packageData.TimeCreated;
								}
							}
						}

						redeemDates[game.AppID] = redeemDate;
					}

					orderedGamesToFarm = farmingOrder switch {
						// ReSharper disable once AccessToModifiedClosure - you're wrong
						BotConfig.EFarmingOrder.RedeemDateTimesAscending => orderedGamesToFarm.ThenBy(game => redeemDates[game.AppID]),

						// ReSharper disable once AccessToModifiedClosure - you're wrong
						BotConfig.EFarmingOrder.RedeemDateTimesDescending => orderedGamesToFarm.ThenByDescending(game => redeemDates[game.AppID]),

						_ => throw new InvalidOperationException(nameof(farmingOrder))
					};

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
