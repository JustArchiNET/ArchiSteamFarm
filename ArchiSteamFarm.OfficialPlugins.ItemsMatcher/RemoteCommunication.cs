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
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.OfficialPlugins.ItemsMatcher.Data;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Cards;
using ArchiSteamFarm.Steam.Data;
using ArchiSteamFarm.Steam.Exchange;
using ArchiSteamFarm.Steam.Integration;
using ArchiSteamFarm.Steam.Security;
using ArchiSteamFarm.Steam.Storage;
using ArchiSteamFarm.Storage;
using ArchiSteamFarm.Web;
using ArchiSteamFarm.Web.Responses;
using Newtonsoft.Json.Linq;
using SteamKit2;
using SteamKit2.Internal;

namespace ArchiSteamFarm.OfficialPlugins.ItemsMatcher;

internal sealed class RemoteCommunication : IAsyncDisposable, IDisposable {
	private const string MatchActivelyTradeOfferIDsStorageKey = $"{nameof(ItemsMatcher)}-{nameof(MatchActively)}-TradeOfferIDs";
	private const byte MaxAnnouncementTTL = 60; // Maximum amount of minutes we can wait if the next announcement doesn't happen naturally
	private const byte MaxTradeOffersActive = 5; // The actual upper limit is 30, but we should use lower amount to allow some bots to react before we hit the maximum allowed
	private const byte MinAnnouncementTTL = 5; // Minimum amount of minutes we must wait before the next Announcement
	private const byte MinHeartBeatTTL = 10; // Minimum amount of minutes we must wait before sending next HeartBeat
	private const byte MinimumSteamGuardEnabledDays = 15; // As imposed by Steam limits
	private const byte MinPersonaStateTTL = 5; // Minimum amount of minutes we must wait before requesting persona state update

	private static readonly ImmutableHashSet<Asset.EType> AcceptedMatchableTypes = ImmutableHashSet.Create(
		Asset.EType.Emoticon,
		Asset.EType.FoilTradingCard,
		Asset.EType.ProfileBackground,
		Asset.EType.TradingCard
	);

	private readonly Bot Bot;
	private readonly Timer? HeartBeatTimer;

	// We access this collection only within a semaphore, therefore there is no need for concurrent access
	private readonly Dictionary<ulong, uint> LastAnnouncedItems = new();

	private readonly SemaphoreSlim MatchActivelySemaphore = new(1, 1);
	private readonly Timer? MatchActivelyTimer;
	private readonly SemaphoreSlim RequestsSemaphore = new(1, 1);
	private readonly WebBrowser WebBrowser;

	private string? LastAnnouncedTradeToken;
	private DateTime LastAnnouncement;
	private DateTime LastHeartBeat;
	private DateTime LastPersonaStateRequest;
	private bool ShouldSendAnnouncementEarlier;
	private bool ShouldSendHeartBeats;
	private bool SignedInWithSteam;

	internal RemoteCommunication(Bot bot) {
		ArgumentNullException.ThrowIfNull(bot);

		Bot = bot;
		WebBrowser = new WebBrowser(bot.ArchiLogger, ASF.GlobalConfig?.WebProxy, true);

		if (Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.SteamTradeMatcher) && Bot.BotConfig.RemoteCommunication.HasFlag(BotConfig.ERemoteCommunication.PublicListing)) {
			HeartBeatTimer = new Timer(
				HeartBeat,
				null,
				TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(ASF.LoadBalancingDelay * Bot.Bots?.Count ?? 0),
				TimeSpan.FromMinutes(1)
			);
		}

		if (Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.MatchActively)) {
			if ((ASF.GlobalConfig?.LicenseID != null) && (ASF.GlobalConfig.LicenseID != Guid.Empty)) {
				MatchActivelyTimer = new Timer(
					MatchActively,
					null,
					TimeSpan.FromHours(1) + TimeSpan.FromSeconds(ASF.LoadBalancingDelay * Bot.Bots?.Count ?? 0),
					TimeSpan.FromHours(6)
				);
			} else {
				bot.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningNoLicense, nameof(BotConfig.ETradingPreferences.MatchActively)));
			}
		}
	}

	public void Dispose() {
		// Those are objects that are always being created if constructor doesn't throw exception
		MatchActivelySemaphore.Dispose();
		RequestsSemaphore.Dispose();
		WebBrowser.Dispose();

		// Those are objects that might be null and the check should be in-place
		HeartBeatTimer?.Dispose();

		if (MatchActivelyTimer != null) {
			// ReSharper disable once SuspiciousLockOverSynchronizationPrimitive - this is not a mistake, we need extra synchronization, and we can re-use the semaphore object for that
			lock (MatchActivelySemaphore) {
				MatchActivelyTimer.Dispose();
			}
		}
	}

	public async ValueTask DisposeAsync() {
		// Dispose timers first so we won't launch new events
		if (HeartBeatTimer != null) {
			await HeartBeatTimer.DisposeAsync().ConfigureAwait(false);
		}

		if (MatchActivelyTimer != null) {
			// ReSharper disable once SuspiciousLockOverSynchronizationPrimitive - this is not a mistake, we need extra synchronization, and we can re-use the semaphore object for that
			lock (MatchActivelySemaphore) {
				MatchActivelyTimer.Dispose();
			}
		}

		// Ensure the semaphores are closed, then dispose the rest
		await MatchActivelySemaphore.WaitAsync().ConfigureAwait(false);
		await RequestsSemaphore.WaitAsync().ConfigureAwait(false);

		MatchActivelySemaphore.Dispose();
		RequestsSemaphore.Dispose();
		WebBrowser.Dispose();
	}

	internal void OnNewItemsNotification() => ShouldSendAnnouncementEarlier = true;

	internal async Task OnPersonaState(string? nickname = null, string? avatarHash = null) {
		if (!Bot.BotConfig.RemoteCommunication.HasFlag(BotConfig.ERemoteCommunication.PublicListing) || !Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.SteamTradeMatcher)) {
			return;
		}

		if ((DateTime.UtcNow < LastAnnouncement.AddMinutes(ShouldSendAnnouncementEarlier ? MinAnnouncementTTL : MaxAnnouncementTTL)) && ShouldSendHeartBeats) {
			return;
		}

		if (MatchActivelySemaphore.CurrentCount == 0) {
			// We shouldn't bother with announcements while we're matching, it can wait until we're done
			return;
		}

		await RequestsSemaphore.WaitAsync().ConfigureAwait(false);

		try {
			if ((DateTime.UtcNow < LastAnnouncement.AddMinutes(ShouldSendAnnouncementEarlier ? MinAnnouncementTTL : MaxAnnouncementTTL)) && ShouldSendHeartBeats) {
				return;
			}

			if (MatchActivelySemaphore.CurrentCount == 0) {
				// We shouldn't bother with announcements while we're matching, it can wait until we're done
				return;
			}

			// Don't announce if we don't meet conditions
			bool? eligible = await IsEligibleForListing().ConfigureAwait(false);

			if (!eligible.HasValue) {
				// This is actually network failure, so we'll stop sending heartbeats but not record it as valid check
				ShouldSendHeartBeats = false;

				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, $"{nameof(IsEligibleForListing)}: {eligible?.ToString() ?? "null"}"));

				return;
			}

			if (!eligible.Value) {
				// We're not eligible, record this as a valid check
				LastAnnouncement = DateTime.UtcNow;
				ShouldSendAnnouncementEarlier = ShouldSendHeartBeats = false;

				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, $"{nameof(IsEligibleForListing)}: {eligible.Value}"));

				return;
			}

			HashSet<Asset.EType> acceptedMatchableTypes = Bot.BotConfig.MatchableTypes.Where(AcceptedMatchableTypes.Contains).ToHashSet();

			if (acceptedMatchableTypes.Count == 0) {
				throw new InvalidOperationException(nameof(acceptedMatchableTypes));
			}

			string? tradeToken = await Bot.ArchiHandler.GetTradeToken().ConfigureAwait(false);

			if (string.IsNullOrEmpty(tradeToken)) {
				// This is actually a network failure, so we'll stop sending heartbeats but not record it as valid check
				ShouldSendHeartBeats = false;

				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, nameof(tradeToken)));

				return;
			}

			// We require to fetch whole inventory as a list here, as we need to know the order for calculating index and previousAssetID
			List<Asset> inventory;

			try {
				inventory = await Bot.ArchiWebHandler.GetInventoryAsync().ToListAsync().ConfigureAwait(false);
			} catch (HttpRequestException e) {
				// This is actually a network failure, so we'll stop sending heartbeats but not record it as valid check
				ShouldSendHeartBeats = false;

				Bot.ArchiLogger.LogGenericWarningException(e);

				return;
			} catch (Exception e) {
				// This is actually a network failure, so we'll stop sending heartbeats but not record it as valid check
				ShouldSendHeartBeats = false;

				Bot.ArchiLogger.LogGenericException(e);

				return;
			}

			if (inventory.Count == 0) {
				// We're not eligible, record this as a valid check
				LastAnnouncement = DateTime.UtcNow;
				ShouldSendAnnouncementEarlier = ShouldSendHeartBeats = false;

				return;
			}

			bool matchEverything = Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.MatchEverything);

			ulong previousAssetID = 0;

			List<AssetForListing> assetsForListing = new();

			Dictionary<(uint RealAppID, Asset.EType Type, Asset.ERarity Rarity), bool> tradableSets = new();

			foreach (Asset item in inventory) {
				if (acceptedMatchableTypes.Contains(item.Type)) {
					// Only tradable assets matter for MatchEverything bots
					if (!matchEverything || item.Tradable) {
						assetsForListing.Add(new AssetForListing(item, previousAssetID));
					}

					// But even for Fair bots, we should track and skip sets where we don't have any item to trade with
					if (!matchEverything) {
						(uint RealAppID, Asset.EType Type, Asset.ERarity Rarity) key = (item.RealAppID, item.Type, item.Rarity);

						if (tradableSets.TryGetValue(key, out bool tradable)) {
							if (!tradable && item.Tradable) {
								tradableSets[key] = true;
							}
						} else {
							tradableSets[key] = item.Tradable;
						}
					}
				}

				previousAssetID = item.AssetID;
			}

			if (assetsForListing.Count == 0) {
				// We're not eligible, record this as a valid check
				LastAnnouncement = DateTime.UtcNow;
				ShouldSendAnnouncementEarlier = ShouldSendHeartBeats = false;

				return;
			}

			// We can now skip sets where we don't have any item to trade with, MatchEverything bots are already filtered to tradable only
			if (!matchEverything) {
				assetsForListing.RemoveAll(item => tradableSets.TryGetValue((item.RealAppID, item.Type, item.Rarity), out bool tradable) && !tradable);

				if (assetsForListing.Count == 0) {
					// We're not eligible, record this as a valid check
					LastAnnouncement = DateTime.UtcNow;
					ShouldSendAnnouncementEarlier = ShouldSendHeartBeats = false;

					return;
				}
			}

			if (ShouldSendHeartBeats && (tradeToken == LastAnnouncedTradeToken) && (assetsForListing.Count == LastAnnouncedItems.Count) && assetsForListing.All(item => LastAnnouncedItems.TryGetValue(item.AssetID, out uint amount) && (item.Amount == amount))) {
				// There is nothing new to announce, this is fine, skip the request
				LastAnnouncement = DateTime.UtcNow;
				ShouldSendAnnouncementEarlier = false;

				return;
			}

			if (!SignedInWithSteam) {
				HttpStatusCode? signInWithSteam = await ArchiNet.SignInWithSteam(Bot, WebBrowser).ConfigureAwait(false);

				if (signInWithSteam == null) {
					// This is actually a network failure, so we'll stop sending heartbeats but not record it as valid check
					ShouldSendHeartBeats = false;

					return;
				}

				if (!signInWithSteam.Value.IsSuccessCode()) {
					// SignIn procedure failed and it wasn't a network error, hold off with future tries at least for a full day
					LastAnnouncement = DateTime.UtcNow.AddDays(1);
					ShouldSendHeartBeats = false;

					return;
				}

				SignedInWithSteam = true;
			}

			Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Localization.Strings.ListingAnnouncing, Bot.SteamID, nickname ?? Bot.SteamID.ToString(CultureInfo.InvariantCulture), assetsForListing.Count));

			// ReSharper disable once RedundantSuppressNullableWarningExpression - required for .NET Framework
			BasicResponse? response = await Backend.AnnounceForListing(Bot.SteamID, WebBrowser, assetsForListing, acceptedMatchableTypes, (uint) inventory.Count, matchEverything, tradeToken!, nickname, avatarHash).ConfigureAwait(false);

			if (response == null) {
				// This is actually a network failure, so we'll stop sending heartbeats but not record it as valid check
				ShouldSendHeartBeats = false;

				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorObjectIsNull, nameof(response)));

				return;
			}

			if (response.StatusCode.IsRedirectionCode()) {
				ShouldSendHeartBeats = false;

				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, response.StatusCode));

				if (response.FinalUri.Host != ArchiWebHandler.SteamCommunityURL.Host) {
					ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(response.FinalUri), response.FinalUri));

					return;
				}

				// We've expected the result, not the redirection to the sign in, we need to authenticate again
				SignedInWithSteam = false;

				return;
			}

			if (response.StatusCode.IsClientErrorCode()) {
				// ArchiNet told us that we've sent a bad request, so the process should restart from the beginning at later time
				ShouldSendHeartBeats = false;

				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, response.StatusCode));

				switch (response.StatusCode) {
					case HttpStatusCode.Forbidden:
						// ArchiNet told us to stop submitting data for now
						LastAnnouncement = DateTime.UtcNow.AddYears(1);

						return;
#if NETFRAMEWORK || NETSTANDARD
					case (HttpStatusCode) 429:
#else
					case HttpStatusCode.TooManyRequests:
#endif

						// ArchiNet told us to try again later
						LastAnnouncement = DateTime.UtcNow.AddDays(1);

						return;
					default:
						// There is something wrong with our payload or the server, we shouldn't retry for at least several hours
						LastAnnouncement = DateTime.UtcNow.AddHours(6);

						return;
				}
			}

			LastAnnouncement = LastHeartBeat = DateTime.UtcNow;
			ShouldSendAnnouncementEarlier = false;
			ShouldSendHeartBeats = true;

			LastAnnouncedTradeToken = tradeToken;
			LastAnnouncedItems.Clear();

			foreach (AssetForListing item in assetsForListing) {
				LastAnnouncedItems[item.AssetID] = item.Amount;
			}

			LastAnnouncedItems.TrimExcess();
		} finally {
			RequestsSemaphore.Release();
		}

		Bot.ArchiLogger.LogGenericInfo(Strings.Success);
	}

	internal void TriggerMatchActivelyEarlier() {
		if (MatchActivelyTimer == null) {
			throw new InvalidOperationException(nameof(MatchActivelyTimer));
		}

		// ReSharper disable once SuspiciousLockOverSynchronizationPrimitive - this is not a mistake, we need extra synchronization, and we can re-use the semaphore object for that
		lock (MatchActivelySemaphore) {
			MatchActivelyTimer.Change(TimeSpan.Zero, TimeSpan.FromHours(6));
		}
	}

	private async void HeartBeat(object? state = null) {
		if (!Bot.IsConnectedAndLoggedOn || (Bot.HeartBeatFailures > 0)) {
			return;
		}

		// Request persona update if needed
		if ((DateTime.UtcNow > LastPersonaStateRequest.AddMinutes(MinPersonaStateTTL)) && (DateTime.UtcNow > LastAnnouncement.AddMinutes(ShouldSendAnnouncementEarlier ? MinAnnouncementTTL : MaxAnnouncementTTL))) {
			LastPersonaStateRequest = DateTime.UtcNow;
			Bot.RequestPersonaStateUpdate();
		}

		if (!ShouldSendHeartBeats || (DateTime.UtcNow < LastHeartBeat.AddMinutes(MinHeartBeatTTL))) {
			return;
		}

		if (!await RequestsSemaphore.WaitAsync(0).ConfigureAwait(false)) {
			return;
		}

		try {
			BasicResponse? response = await Backend.HeartBeatForListing(Bot, WebBrowser).ConfigureAwait(false);

			if (response == null) {
				// This is actually a network failure, we should keep sending heartbeats for now
				return;
			}

			if (response.StatusCode.IsRedirectionCode()) {
				ShouldSendHeartBeats = false;

				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, response.StatusCode));

				if (response.FinalUri.Host != ArchiWebHandler.SteamCommunityURL.Host) {
					ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(response.FinalUri), response.FinalUri));

					return;
				}

				// We've expected the result, not the redirection to the sign in, we need to authenticate again
				SignedInWithSteam = false;

				return;
			}

			if (response.StatusCode.IsClientErrorCode()) {
				ShouldSendHeartBeats = false;

				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, response.StatusCode));

				return;
			}

			LastHeartBeat = DateTime.UtcNow;
		} finally {
			RequestsSemaphore.Release();
		}
	}

	private async Task<bool?> IsEligibleForListing() {
		// Bot must be eligible for matching
		bool? isEligibleForMatching = await IsEligibleForMatching().ConfigureAwait(false);

		if (isEligibleForMatching != true) {
			return isEligibleForMatching;
		}

		// Bot must have a public inventory
		bool? hasPublicInventory = await Bot.HasPublicInventory().ConfigureAwait(false);

		if (hasPublicInventory != true) {
			Bot.ArchiLogger.LogGenericTrace(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, $"{nameof(Bot.HasPublicInventory)}: {hasPublicInventory?.ToString() ?? "null"}"));

			return hasPublicInventory;
		}

		return true;
	}

	private async Task<bool?> IsEligibleForMatching() {
		// Bot can't be limited
		if (Bot.IsAccountLimited) {
			Bot.ArchiLogger.LogGenericTrace(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, $"{nameof(Bot.IsAccountLimited)}: {Bot.IsAccountLimited}"));

			return false;
		}

		// Bot can't be on lockdown
		if (Bot.IsAccountLocked) {
			Bot.ArchiLogger.LogGenericTrace(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, $"{nameof(Bot.IsAccountLocked)}: {Bot.IsAccountLocked}"));

			return false;
		}

		// Bot must have ASF 2FA
		if (!Bot.HasMobileAuthenticator) {
			Bot.ArchiLogger.LogGenericTrace(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, $"{nameof(Bot.HasMobileAuthenticator)}: {Bot.HasMobileAuthenticator}"));

			return false;
		}

		// Bot must have at least one accepted matchable type set
		if ((Bot.BotConfig.MatchableTypes.Count == 0) || Bot.BotConfig.MatchableTypes.All(static type => !AcceptedMatchableTypes.Contains(type))) {
			Bot.ArchiLogger.LogGenericTrace(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, $"{nameof(Bot.BotConfig.MatchableTypes)}: {string.Join(", ", Bot.BotConfig.MatchableTypes)}"));

			return false;
		}

		// Bot must pass some general trading requirements
		CCredentials_GetSteamGuardDetails_Response? steamGuardStatus = await Bot.ArchiHandler.GetSteamGuardStatus().ConfigureAwait(false);

		if (steamGuardStatus == null) {
			Bot.ArchiLogger.LogGenericTrace(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, $"{nameof(steamGuardStatus)}: null"));

			return null;
		}

		// Bot must have SteamGuard active for at least 15 days
		if (!steamGuardStatus.is_steamguard_enabled || ((steamGuardStatus.timestamp_steamguard_enabled > 0) && ((DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(steamGuardStatus.timestamp_steamguard_enabled)).TotalDays < MinimumSteamGuardEnabledDays))) {
			Bot.ArchiLogger.LogGenericTrace(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, $"{nameof(steamGuardStatus.is_steamguard_enabled)}/{nameof(steamGuardStatus.timestamp_steamguard_enabled)}: {steamGuardStatus.is_steamguard_enabled}/{steamGuardStatus.timestamp_steamguard_enabled}"));

			return false;
		}

		// Bot must have 2FA enabled for matching to work
		if (!steamGuardStatus.is_twofactor_enabled) {
			Bot.ArchiLogger.LogGenericTrace(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, $"{nameof(steamGuardStatus.is_twofactor_enabled)}: false"));

			return false;
		}

		// Bot must have valid API key (e.g. not being restricted account)
		bool? hasValidApiKey = await Bot.ArchiWebHandler.HasValidApiKey().ConfigureAwait(false);

		if (hasValidApiKey != true) {
			Bot.ArchiLogger.LogGenericTrace(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, $"{nameof(Bot.ArchiWebHandler.HasValidApiKey)}: {hasValidApiKey?.ToString() ?? "null"}"));

			return hasValidApiKey.HasValue ? false : null;
		}

		// Bot can't be trade banned
		(bool _, bool? Result) economyBan = await Bot.ArchiWebHandler.CachedEconomyBan.GetValue(ECacheFallback.SuccessPreviously).ConfigureAwait(false);

		if (economyBan.Result != false) {
			Bot.ArchiLogger.LogGenericTrace(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, $"{nameof(economyBan)}: {economyBan.Result?.ToString() ?? "null"}"));

			return economyBan.Result.HasValue ? false : null;
		}

		return true;
	}

	private async void MatchActively(object? state = null) {
		if (ASF.GlobalConfig == null) {
			throw new InvalidOperationException(nameof(ASF.GlobalConfig));
		}

		if (!ASF.GlobalConfig.LicenseID.HasValue || (ASF.GlobalConfig.LicenseID == Guid.Empty)) {
			throw new InvalidOperationException(nameof(ASF.GlobalConfig.LicenseID));
		}

		if (!Bot.IsConnectedAndLoggedOn || Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.MatchEverything) || !Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.MatchActively)) {
			Bot.ArchiLogger.LogGenericTrace(Strings.ErrorAborted);

			return;
		}

		bool? eligible = await IsEligibleForMatching().ConfigureAwait(false);

		if (eligible != true) {
			Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, $"{nameof(IsEligibleForMatching)}: {eligible?.ToString() ?? "null"}"));

			return;
		}

		HashSet<Asset.EType> acceptedMatchableTypes = Bot.BotConfig.MatchableTypes.Where(AcceptedMatchableTypes.Contains).ToHashSet();

		if (acceptedMatchableTypes.Count == 0) {
			Bot.ArchiLogger.LogNullError(acceptedMatchableTypes);

			return;
		}

		if (!await MatchActivelySemaphore.WaitAsync(0).ConfigureAwait(false)) {
			Bot.ArchiLogger.LogGenericTrace(Strings.ErrorAborted);

			return;
		}

		bool tradesSent;

		try {
			Bot.ArchiLogger.LogGenericInfo(Strings.Starting);

			Dictionary<ulong, Asset> ourInventory;

			try {
				ourInventory = await Bot.ArchiWebHandler.GetInventoryAsync().Where(item => acceptedMatchableTypes.Contains(item.Type) && !Bot.BotDatabase.MatchActivelyBlacklistAppIDs.Contains(item.RealAppID)).ToDictionaryAsync(static item => item.AssetID).ConfigureAwait(false);
			} catch (HttpRequestException e) {
				Bot.ArchiLogger.LogGenericWarningException(e);
				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, nameof(ourInventory)));

				return;
			} catch (Exception e) {
				Bot.ArchiLogger.LogGenericException(e);
				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, nameof(ourInventory)));

				return;
			}

			if (ourInventory.Count == 0) {
				Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(ourInventory)));

				return;
			}

			// Remove from our inventory items that can't be possibly matched due to no dupes to offer available
			HashSet<(uint RealAppID, Asset.EType Type, Asset.ERarity Rarity)> setsToKeep = Trading.GetInventorySets(ourInventory.Values).Where(static set => set.Value.Any(static amount => amount > 1)).Select(static set => set.Key).ToHashSet();

			HashSet<ulong> assetIDsToRemove = ourInventory.Where(item => !setsToKeep.Contains((item.Value.RealAppID, item.Value.Type, item.Value.Rarity))).Select(static item => item.Key).ToHashSet();

			foreach (ulong assetIDToRemove in assetIDsToRemove) {
				ourInventory.Remove(assetIDToRemove);
			}

			if (ourInventory.Count == 0) {
				Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(ourInventory)));

				return;
			}

			// ReSharper disable once RedundantSuppressNullableWarningExpression - required for .NET Framework
			(HttpStatusCode StatusCode, ImmutableHashSet<ListedUser> Users)? response = await Backend.GetListedUsersForMatching(ASF.GlobalConfig.LicenseID.Value, Bot, WebBrowser, ourInventory.Values, acceptedMatchableTypes).ConfigureAwait(false);

			if (response == null) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, nameof(response)));

				return;
			}

			if (!response.Value.StatusCode.IsSuccessCode()) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, response.Value.StatusCode));

				return;
			}

			if (response.Value.Users.IsEmpty) {
				Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(response.Value.Users)));

				return;
			}

#pragma warning disable CA2000 // False positive, we're actually wrapping it in the using clause below exactly for that purpose
			using (await Bot.Actions.GetTradingLock().ConfigureAwait(false)) {
#pragma warning restore CA2000 // False positive, we're actually wrapping it in the using clause below exactly for that purpose
				Bot.ArchiLogger.LogGenericInfo(Strings.Starting);

				tradesSent = await MatchActively(response.Value.Users, ourInventory, acceptedMatchableTypes).ConfigureAwait(false);
			}

			Bot.ArchiLogger.LogGenericInfo(Strings.Done);
		} finally {
			MatchActivelySemaphore.Release();
		}

		if (tradesSent && ShouldSendHeartBeats && (DateTime.UtcNow > LastAnnouncement.AddMinutes(ShouldSendAnnouncementEarlier ? MinAnnouncementTTL : MaxAnnouncementTTL))) {
			// If we're announced, it makes sense to update our state now, at least once
			Bot.RequestPersonaStateUpdate();
		}
	}

	private async Task<bool> MatchActively(IReadOnlyCollection<ListedUser> listedUsers, Dictionary<ulong, Asset> ourInventory, IReadOnlyCollection<Asset.EType> acceptedMatchableTypes) {
		if ((listedUsers == null) || (listedUsers.Count == 0)) {
			throw new ArgumentNullException(nameof(listedUsers));
		}

		if ((ourInventory == null) || (ourInventory.Count == 0)) {
			throw new ArgumentNullException(nameof(ourInventory));
		}

		if ((acceptedMatchableTypes == null) || (acceptedMatchableTypes.Count == 0)) {
			throw new ArgumentNullException(nameof(acceptedMatchableTypes));
		}

		(Dictionary<(uint RealAppID, Asset.EType Type, Asset.ERarity Rarity), Dictionary<ulong, uint>> ourFullState, Dictionary<(uint RealAppID, Asset.EType Type, Asset.ERarity Rarity), Dictionary<ulong, uint>> ourTradableState) = Trading.GetDividedInventoryState(ourInventory.Values);

		if (Trading.IsEmptyForMatching(ourFullState, ourTradableState)) {
			// User doesn't have any more dupes in the inventory
			Bot.ArchiLogger.LogGenericTrace(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, $"{nameof(ourFullState)} || {nameof(ourTradableState)}"));

			return false;
		}

		// Cancel previous trade offers sent and deprioritize SteamIDs that didn't answer us in this round
		HashSet<ulong>? matchActivelyTradeOfferIDs = null;

		JToken? matchActivelyTradeOfferIDsToken = Bot.BotDatabase.LoadFromJsonStorage(MatchActivelyTradeOfferIDsStorageKey);

		if (matchActivelyTradeOfferIDsToken != null) {
			try {
				matchActivelyTradeOfferIDs = matchActivelyTradeOfferIDsToken.ToObject<HashSet<ulong>>();
			} catch (Exception e) {
				Bot.ArchiLogger.LogGenericWarningException(e);
			}
		}

		matchActivelyTradeOfferIDs ??= new HashSet<ulong>();

		HashSet<ulong> deprioritizedSteamIDs = new();

		if (matchActivelyTradeOfferIDs.Count > 0) {
			// This is not a mandatory step, we allow it to fail
			HashSet<TradeOffer>? sentTradeOffers = await Bot.ArchiWebHandler.GetTradeOffers(true, false, true, false).ConfigureAwait(false);

			if (sentTradeOffers != null) {
				HashSet<ulong> activeTradeOfferIDs = new();

				foreach (TradeOffer tradeOffer in sentTradeOffers.Where(tradeOffer => (tradeOffer.State == ETradeOfferState.Active) && matchActivelyTradeOfferIDs.Contains(tradeOffer.TradeOfferID))) {
					deprioritizedSteamIDs.Add(tradeOffer.OtherSteamID64);

					if (!await Bot.ArchiWebHandler.CancelTradeOffer(tradeOffer.TradeOfferID).ConfigureAwait(false)) {
						activeTradeOfferIDs.Add(tradeOffer.TradeOfferID);
					}
				}

				if (!matchActivelyTradeOfferIDs.SetEquals(activeTradeOfferIDs)) {
					matchActivelyTradeOfferIDs = activeTradeOfferIDs;

					if (matchActivelyTradeOfferIDs.Count > 0) {
						Bot.BotDatabase.SaveToJsonStorage(MatchActivelyTradeOfferIDsStorageKey, JToken.FromObject(matchActivelyTradeOfferIDs));
					} else {
						Bot.BotDatabase.DeleteFromJsonStorage(MatchActivelyTradeOfferIDsStorageKey);
					}
				}
			}
		}

		HashSet<ulong> pendingMobileTradeOfferIDs = new();

		byte maxTradeHoldDuration = ASF.GlobalConfig?.MaxTradeHoldDuration ?? GlobalConfig.DefaultMaxTradeHoldDuration;

		byte failuresInRow = 0;
		uint matchedSets = 0;

		foreach (ListedUser listedUser in listedUsers.Where(listedUser => (listedUser.SteamID != Bot.SteamID) && acceptedMatchableTypes.Any(listedUser.MatchableTypes.Contains) && !Bot.IsBlacklistedFromTrades(listedUser.SteamID)).OrderBy(listedUser => deprioritizedSteamIDs.Contains(listedUser.SteamID)).ThenByDescending(static listedUser => listedUser.MatchEverything).ThenBy(static listedUser => listedUser.TotalInventoryCount)) {
			if (failuresInRow >= WebBrowser.MaxTries) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, $"{nameof(failuresInRow)} >= {WebBrowser.MaxTries}"));

				break;
			}

			if (!Bot.IsConnectedAndLoggedOn) {
				Bot.ArchiLogger.LogGenericWarning(Strings.BotNotConnected);

				break;
			}

			HashSet<(uint RealAppID, Asset.EType Type, Asset.ERarity Rarity)> wantedSets = ourTradableState.Keys.Where(set => listedUser.MatchableTypes.Contains(set.Type)).ToHashSet();

			if (wantedSets.Count == 0) {
				continue;
			}

			Bot.ArchiLogger.LogGenericTrace($"{listedUser.SteamID}...");

			byte? tradeHoldDuration = await Bot.ArchiWebHandler.GetCombinedTradeHoldDurationAgainstUser(listedUser.SteamID, listedUser.TradeToken).ConfigureAwait(false);

			switch (tradeHoldDuration) {
				case null:
					Bot.ArchiLogger.LogGenericTrace(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(tradeHoldDuration)));

					continue;
				case > 0 when (tradeHoldDuration.Value > maxTradeHoldDuration) || (tradeHoldDuration.Value > listedUser.MaxTradeHoldDuration):
					Bot.ArchiLogger.LogGenericTrace($"{tradeHoldDuration.Value} > {maxTradeHoldDuration} || {listedUser.MaxTradeHoldDuration}");

					continue;
			}

			HashSet<Asset> theirInventory = listedUser.Assets.Where(item => (!listedUser.MatchEverything || item.Tradable) && wantedSets.Contains((item.RealAppID, item.Type, item.Rarity)) && ((tradeHoldDuration.Value == 0) || !(item.Type is Asset.EType.FoilTradingCard or Asset.EType.TradingCard && CardsFarmer.SalesBlacklist.Contains(item.RealAppID)))).Select(static asset => asset.ToAsset()).ToHashSet();

			if (theirInventory.Count == 0) {
				continue;
			}

			HashSet<(uint RealAppID, Asset.EType Type, Asset.ERarity Rarity)> skippedSetsThisUser = new();

			Dictionary<(uint RealAppID, Asset.EType Type, Asset.ERarity Rarity), Dictionary<ulong, uint>> theirTradableState = Trading.GetTradableInventoryState(theirInventory);

			for (byte i = 0; i < Trading.MaxTradesPerAccount; i++) {
				byte itemsInTrade = 0;
				HashSet<(uint RealAppID, Asset.EType Type, Asset.ERarity Rarity)> skippedSetsThisTrade = new();

				Dictionary<ulong, uint> classIDsToGive = new();
				Dictionary<ulong, uint> classIDsToReceive = new();
				Dictionary<ulong, uint> fairClassIDsToGive = new();
				Dictionary<ulong, uint> fairClassIDsToReceive = new();

				foreach (((uint RealAppID, Asset.EType Type, Asset.ERarity Rarity) set, Dictionary<ulong, uint> ourFullItems) in ourFullState.Where(set => !skippedSetsThisUser.Contains(set.Key) && listedUser.MatchableTypes.Contains(set.Key.Type) && set.Value.Values.Any(static count => count > 1))) {
					if (!ourTradableState.TryGetValue(set, out Dictionary<ulong, uint>? ourTradableItems) || (ourTradableItems.Count == 0)) {
						// We may have no more tradable items from this set
						continue;
					}

					if (!theirTradableState.TryGetValue(set, out Dictionary<ulong, uint>? theirTradableItems) || (theirTradableItems.Count == 0)) {
						// They may have no more tradable items from this set
						continue;
					}

					if (Trading.IsEmptyForMatching(ourFullItems, ourTradableItems)) {
						// We may have no more matchable items from this set
						continue;
					}

					// Those 2 collections are on user-basis since we can't be sure that the trade passes through (and therefore we need to keep original state in case of a failure)
					Dictionary<ulong, uint> ourFullSet = new(ourFullItems);
					Dictionary<ulong, uint> ourTradableSet = new(ourTradableItems);

					bool match;

					do {
						match = false;

						foreach ((ulong ourItem, uint ourFullAmount) in ourFullSet.Where(static item => item.Value > 1).OrderByDescending(static item => item.Value)) {
							if (!ourTradableSet.TryGetValue(ourItem, out uint ourTradableAmount) || (ourTradableAmount == 0)) {
								continue;
							}

							foreach ((ulong theirItem, uint theirTradableAmount) in theirTradableItems.OrderBy(item => ourFullSet.TryGetValue(item.Key, out uint ourAmountOfTheirItem) ? ourAmountOfTheirItem : 0)) {
								if (ourFullSet.TryGetValue(theirItem, out uint ourAmountOfTheirItem) && (ourFullAmount <= ourAmountOfTheirItem + 1)) {
									continue;
								}

								if (!listedUser.MatchEverything) {
									// We have a potential match, let's check fairness for them
									fairClassIDsToGive.TryGetValue(ourItem, out uint fairGivenAmount);
									fairClassIDsToReceive.TryGetValue(theirItem, out uint fairReceivedAmount);
									fairClassIDsToGive[ourItem] = ++fairGivenAmount;
									fairClassIDsToReceive[theirItem] = ++fairReceivedAmount;

									// Filter their inventory for the sets we're trading or have traded with this user
									HashSet<Asset> fairFiltered = theirInventory.Where(item => ((item.RealAppID == set.RealAppID) && (item.Type == set.Type) && (item.Rarity == set.Rarity)) || skippedSetsThisTrade.Contains((item.RealAppID, item.Type, item.Rarity))).Select(static item => item.CreateShallowCopy()).ToHashSet();

									// Copy list to HashSet<Steam.Asset>
									HashSet<Asset> fairItemsToGive = Trading.GetTradableItemsFromInventory(ourInventory.Values.Where(item => ((item.RealAppID == set.RealAppID) && (item.Type == set.Type) && (item.Rarity == set.Rarity)) || skippedSetsThisTrade.Contains((item.RealAppID, item.Type, item.Rarity))).Select(static item => item.CreateShallowCopy()).ToHashSet(), fairClassIDsToGive.ToDictionary(static classID => classID.Key, static classID => classID.Value));
									HashSet<Asset> fairItemsToReceive = Trading.GetTradableItemsFromInventory(fairFiltered.Select(static item => item.CreateShallowCopy()).ToHashSet(), fairClassIDsToReceive.ToDictionary(static classID => classID.Key, static classID => classID.Value));

									// Actual check
									if (!Trading.IsTradeNeutralOrBetter(fairFiltered, fairItemsToReceive, fairItemsToGive)) {
										// Revert the changes
										if (fairGivenAmount > 1) {
											fairClassIDsToGive[ourItem] = fairGivenAmount - 1;
										} else {
											fairClassIDsToGive.Remove(ourItem);
										}

										if (fairReceivedAmount > 1) {
											fairClassIDsToReceive[theirItem] = fairReceivedAmount - 1;
										} else {
											fairClassIDsToReceive.Remove(theirItem);
										}

										continue;
									}
								}

								// Skip this set from the remaining of this round
								skippedSetsThisTrade.Add(set);

								// Update our state based on given items
								classIDsToGive[ourItem] = classIDsToGive.TryGetValue(ourItem, out uint ourGivenAmount) ? ourGivenAmount + 1 : 1;
								ourFullSet[ourItem] = ourFullAmount - 1; // We don't need to remove anything here because we can guarantee that ourItem.Value is at least 2

								// Update our state based on received items
								classIDsToReceive[theirItem] = classIDsToReceive.TryGetValue(theirItem, out uint ourReceivedAmount) ? ourReceivedAmount + 1 : 1;
								ourFullSet[theirItem] = ourAmountOfTheirItem + 1;

								if (ourTradableAmount > 1) {
									ourTradableSet[ourItem] = ourTradableAmount - 1;
								} else {
									ourTradableSet.Remove(ourItem);
								}

								// Update their state based on taken items
								if (theirTradableAmount > 1) {
									theirTradableItems[theirItem] = theirTradableAmount - 1;
								} else {
									theirTradableItems.Remove(theirItem);
								}

								itemsInTrade += 2;

								match = true;

								break;
							}

							if (match) {
								break;
							}
						}
					} while (match && (itemsInTrade < Trading.MaxItemsPerTrade - 1));

					if (itemsInTrade >= Trading.MaxItemsPerTrade - 1) {
						break;
					}
				}

				if (skippedSetsThisTrade.Count == 0) {
					Bot.ArchiLogger.LogGenericTrace(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(skippedSetsThisTrade)));

					break;
				}

				// Remove the items from inventories
				HashSet<Asset> itemsToGive = Trading.GetTradableItemsFromInventory(ourInventory.Values, classIDsToGive);
				HashSet<Asset> itemsToReceive = Trading.GetTradableItemsFromInventory(theirInventory, classIDsToReceive, true);

				if ((itemsToGive.Count != itemsToReceive.Count) || !Trading.IsFairExchange(itemsToGive, itemsToReceive)) {
					// Failsafe
					throw new InvalidOperationException($"{nameof(itemsToGive)} && {nameof(itemsToReceive)}");
				}

				Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Localization.Strings.MatchingFound, itemsToReceive.Count, listedUser.SteamID, listedUser.Nickname));

				Bot.ArchiLogger.LogGenericTrace($"{Bot.SteamID} <- {string.Join(", ", itemsToReceive.Select(static item => $"{item.RealAppID}/{item.Type}/{item.Rarity}/{item.ClassID} #{item.Amount}"))} | {string.Join(", ", itemsToGive.Select(static item => $"{item.RealAppID}/{item.Type}/{item.Rarity}/{item.ClassID} #{item.Amount}"))} -> {listedUser.SteamID}");

				(bool success, HashSet<ulong>? tradeOfferIDs, HashSet<ulong>? mobileTradeOfferIDs) = await Bot.ArchiWebHandler.SendTradeOffer(listedUser.SteamID, itemsToGive, itemsToReceive, listedUser.TradeToken, true).ConfigureAwait(false);

				if (tradeOfferIDs?.Count > 0) {
					matchActivelyTradeOfferIDs.UnionWith(tradeOfferIDs);

					Bot.BotDatabase.SaveToJsonStorage(MatchActivelyTradeOfferIDsStorageKey, JToken.FromObject(matchActivelyTradeOfferIDs));
				}

				if (mobileTradeOfferIDs?.Count > 0) {
					pendingMobileTradeOfferIDs.UnionWith(mobileTradeOfferIDs);

					if (pendingMobileTradeOfferIDs.Count >= MaxTradeOffersActive) {
						(bool twoFactorSuccess, IReadOnlyCollection<Confirmation>? handledConfirmations, _) = await Bot.Actions.HandleTwoFactorAuthenticationConfirmations(true, Confirmation.EType.Trade, pendingMobileTradeOfferIDs, true).ConfigureAwait(false);

						if (!twoFactorSuccess) {
							Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Localization.Strings.ActivelyMatchingSomeConfirmationsFailed, handledConfirmations?.Count ?? 0, pendingMobileTradeOfferIDs.Count));
						}

						pendingMobileTradeOfferIDs.Clear();
					}
				}

				if (!success) {
					// The user likely no longer has the items we need, this is fine, we can continue the matching with other ones
					failuresInRow++;

					Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Localization.Strings.TradeOfferFailed, listedUser.SteamID, listedUser.Nickname));

					break;
				}

				failuresInRow = 0;

				Bot.ArchiLogger.LogGenericInfo(Strings.Success);

				// Assume the trade offer has went through and was accepted, this will allow us to keep matching the same set with different users as if we've got what we wanted
				foreach (Asset itemToGive in itemsToGive) {
					if (!ourInventory.TryGetValue(itemToGive.AssetID, out Asset? item) || (itemToGive.Amount > item.Amount)) {
						throw new InvalidOperationException(nameof(item));
					}

					if (itemToGive.Amount == item.Amount) {
						ourInventory.Remove(itemToGive.AssetID);
					} else {
						item.Amount -= itemToGive.Amount;
					}

					if (!ourFullState.TryGetValue((itemToGive.RealAppID, itemToGive.Type, itemToGive.Rarity), out Dictionary<ulong, uint>? fullAmounts) || !fullAmounts.TryGetValue(itemToGive.ClassID, out uint fullAmount) || (itemToGive.Amount > fullAmount)) {
						// We're giving items we don't even have?
						throw new InvalidOperationException(nameof(fullAmounts));
					}

					if (itemToGive.Amount == fullAmount) {
						fullAmounts.Remove(itemToGive.ClassID);
					} else {
						fullAmounts[itemToGive.ClassID] = fullAmount - itemToGive.Amount;
					}

					if (!ourTradableState.TryGetValue((itemToGive.RealAppID, itemToGive.Type, itemToGive.Rarity), out Dictionary<ulong, uint>? tradableAmounts) || !tradableAmounts.TryGetValue(itemToGive.ClassID, out uint tradableAmount) || (itemToGive.Amount > tradableAmount)) {
						// We're giving items we don't even have?
						throw new InvalidOperationException(nameof(tradableAmounts));
					}

					if (itemToGive.Amount == tradableAmount) {
						tradableAmounts.Remove(itemToGive.ClassID);
					} else {
						tradableAmounts[itemToGive.ClassID] = tradableAmount - itemToGive.Amount;
					}
				}

				// However, since this is only an assumption, we must mark newly acquired items as untradable so we're sure that they're not considered for trading, only for matching
				foreach (Asset itemToReceive in itemsToReceive) {
					if (ourInventory.TryGetValue(itemToReceive.AssetID, out Asset? item)) {
						item.Tradable = false;
						item.Amount += itemToReceive.Amount;
					} else {
						itemToReceive.Tradable = false;
						ourInventory[itemToReceive.AssetID] = itemToReceive;
					}

					if (!ourFullState.TryGetValue((itemToReceive.RealAppID, itemToReceive.Type, itemToReceive.Rarity), out Dictionary<ulong, uint>? fullAmounts)) {
						// We're receiving items from a set we don't even have?
						throw new InvalidOperationException(nameof(fullAmounts));
					}

					if (!fullAmounts.TryGetValue(itemToReceive.ClassID, out uint fullAmount)) {
						fullAmount = 0;
					}

					fullAmounts[itemToReceive.ClassID] = itemToReceive.Amount + fullAmount;
				}

				skippedSetsThisUser.UnionWith(skippedSetsThisTrade);
			}

			if (skippedSetsThisUser.Count == 0) {
				continue;
			}

			matchedSets += (uint) skippedSetsThisUser.Count;

			if (Trading.IsEmptyForMatching(ourFullState, ourTradableState)) {
				// User doesn't have any more dupes in the inventory
				break;
			}
		}

		if (pendingMobileTradeOfferIDs.Count > 0) {
			(bool twoFactorSuccess, IReadOnlyCollection<Confirmation>? handledConfirmations, _) = Bot.IsConnectedAndLoggedOn ? await Bot.Actions.HandleTwoFactorAuthenticationConfirmations(true, Confirmation.EType.Trade, pendingMobileTradeOfferIDs, true).ConfigureAwait(false) : (false, null, null);

			if (!twoFactorSuccess) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Localization.Strings.ActivelyMatchingSomeConfirmationsFailed, handledConfirmations?.Count ?? 0, pendingMobileTradeOfferIDs.Count));
			}
		}

		Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Localization.Strings.ActivelyMatchingItemsRound, matchedSets));

		return matchedSets > 0;
	}
}
