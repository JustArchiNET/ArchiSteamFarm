// ----------------------------------------------------------------------------------------------
//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// ----------------------------------------------------------------------------------------------
// |
// Copyright 2015-2024 Łukasz "JustArchi" Domeradzki
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
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.IPC.Responses;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.OfficialPlugins.ItemsMatcher.Data;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Cards;
using ArchiSteamFarm.Steam.Data;
using ArchiSteamFarm.Steam.Exchange;
using ArchiSteamFarm.Steam.Integration;
using ArchiSteamFarm.Steam.Storage;
using ArchiSteamFarm.Storage;
using ArchiSteamFarm.Web;
using ArchiSteamFarm.Web.Responses;
using SteamKit2;
using SteamKit2.Internal;

namespace ArchiSteamFarm.OfficialPlugins.ItemsMatcher;

internal sealed class RemoteCommunication : IAsyncDisposable, IDisposable {
	private const string MatchActivelyTradeOfferIDsStorageKey = $"{nameof(ItemsMatcher)}-{nameof(MatchActively)}-TradeOfferIDs";
	private const byte MaxAnnouncementTTL = 60; // Maximum amount of minutes we can wait if the next announcement doesn't happen naturally
	private const byte MaxInactivityDays = 14; // How long the server is willing to keep information about us for
	private const uint MaxItemsCount = 500000; // Server is unwilling to accept more items than this
	private const byte MaxTradeOffersActive = 5; // The actual upper limit is 30, but we should use lower amount to allow some bots to react before we hit the maximum allowed
	private const byte MinAnnouncementTTL = 5; // Minimum amount of minutes we must wait before the next Announcement
	private const byte MinHeartBeatTTL = 10; // Minimum amount of minutes we must wait before sending next HeartBeat
	private const byte MinimumPasswordResetCooldownDays = 5; // As imposed by Steam limits
	private const byte MinimumSteamGuardEnabledDays = 15; // As imposed by Steam limits
	private const byte MinPersonaStateTTL = 5; // Minimum amount of minutes we must wait before requesting persona state update

	private static readonly FrozenSet<EAssetType> AcceptedMatchableTypes = new HashSet<EAssetType>(4) {
		EAssetType.Emoticon,
		EAssetType.FoilTradingCard,
		EAssetType.ProfileBackground,
		EAssetType.TradingCard
	}.ToFrozenSet();

	private readonly Bot Bot;
	private readonly Timer? HeartBeatTimer;

	private readonly SemaphoreSlim MatchActivelySemaphore = new(1, 1);
	private readonly Timer? MatchActivelyTimer;
	private readonly SemaphoreSlim RequestsSemaphore = new(1, 1);
	private readonly WebBrowser WebBrowser;

	private string BotCacheFilePath => Path.Combine(SharedInfo.ConfigDirectory, $"{Bot.BotName}.{nameof(ItemsMatcher)}.cache");

	private BotCache? BotCache;
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
				OnHeartBeatTimer,
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
		// Dispose timers first so we won't launch new events
		HeartBeatTimer?.Dispose();

		if (MatchActivelyTimer != null) {
			// ReSharper disable once SuspiciousLockOverSynchronizationPrimitive - this is not a mistake, we need extra synchronization, and we can re-use the semaphore object for that
			lock (MatchActivelySemaphore) {
				MatchActivelyTimer.Dispose();
			}
		}

		// Ensure the semaphores are closed, then dispose the rest
		try {
			MatchActivelySemaphore.Wait();
		} catch (ObjectDisposedException) {
			// Ignored, this is fine
		}

		try {
			RequestsSemaphore.Wait();
		} catch (ObjectDisposedException) {
			// Ignored, this is fine
		}

		BotCache?.Dispose();

		MatchActivelySemaphore.Dispose();
		RequestsSemaphore.Dispose();
		WebBrowser.Dispose();
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
		try {
			await MatchActivelySemaphore.WaitAsync().ConfigureAwait(false);
		} catch (ObjectDisposedException) {
			// Ignored, this is fine
		}

		try {
			await RequestsSemaphore.WaitAsync().ConfigureAwait(false);
		} catch (ObjectDisposedException) {
			// Ignored, this is fine
		}

		BotCache?.Dispose();

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

			HashSet<EAssetType> acceptedMatchableTypes = Bot.BotConfig.MatchableTypes.Where(AcceptedMatchableTypes.Contains).ToHashSet();

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
				inventory = await Bot.ArchiHandler.GetMyInventoryAsync().ToListAsync().ConfigureAwait(false);
			} catch (TimeoutException e) {
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

			uint index = 0;
			ulong previousAssetID = 0;

			List<AssetForListing> assetsForListing = [];

			Dictionary<(uint RealAppID, EAssetType Type, EAssetRarity Rarity), bool> tradableSets = new();

			foreach (Asset item in inventory) {
				if (item is { AssetID: > 0, Amount: > 0, ClassID: > 0, RealAppID: > 0, Type: > EAssetType.Unknown, Rarity: > EAssetRarity.Unknown, IsSteamPointsShopItem: false } && acceptedMatchableTypes.Contains(item.Type)) {
					// Only tradable assets matter for MatchEverything bots
					if (!matchEverything || item.Tradable) {
						assetsForListing.Add(new AssetForListing(item, index, previousAssetID));
					}

					// But even for Fair bots, we should track and skip sets where we don't have any item to trade with
					if (!matchEverything) {
						(uint RealAppID, EAssetType Type, EAssetRarity Rarity) key = (item.RealAppID, item.Type, item.Rarity);

						if (tradableSets.TryGetValue(key, out bool tradable)) {
							if (!tradable && item.Tradable) {
								tradableSets[key] = true;
							}
						} else {
							tradableSets[key] = item.Tradable;
						}
					}
				}

				index++;
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

			BotCache ??= await BotCache.CreateOrLoad(BotCacheFilePath).ConfigureAwait(false);

			string inventoryChecksumBeforeDeduplication = Backend.GenerateChecksumFor(assetsForListing);

			if (BotCache.LastRequestAt.HasValue && (DateTime.UtcNow.Subtract(BotCache.LastRequestAt.Value).TotalDays < MaxInactivityDays) && (tradeToken == BotCache.LastAnnouncedTradeToken) && !string.IsNullOrEmpty(BotCache.LastInventoryChecksumBeforeDeduplication)) {
				if (inventoryChecksumBeforeDeduplication == BotCache.LastInventoryChecksumBeforeDeduplication) {
					// We've determined our state to be the same, we can skip announce entirely and start sending heartbeats exclusively
					bool triggerImmediately = !ShouldSendHeartBeats;

					LastAnnouncement = DateTime.UtcNow;
					ShouldSendAnnouncementEarlier = false;
					ShouldSendHeartBeats = true;

					if (triggerImmediately) {
						Utilities.InBackground(() => OnHeartBeatTimer());
					}

					return;
				}
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

			if (!matchEverything) {
				// We should deduplicate our sets before sending them to the server, for doing that we'll use ASFB set parts data
				HashSet<uint> realAppIDs = [];
				Dictionary<(uint RealAppID, EAssetType Type, EAssetRarity Rarity), Dictionary<ulong, uint>> state = new();

				foreach (AssetForListing asset in assetsForListing) {
					realAppIDs.Add(asset.RealAppID);

					(uint RealAppID, EAssetType Type, EAssetRarity Rarity) key = (asset.RealAppID, asset.Type, asset.Rarity);

					if (state.TryGetValue(key, out Dictionary<ulong, uint>? set)) {
						set[asset.ClassID] = set.GetValueOrDefault(asset.ClassID) + asset.Amount;
					} else {
						state[key] = new Dictionary<ulong, uint> { { asset.ClassID, asset.Amount } };
					}
				}

				ObjectResponse<GenericResponse<ImmutableHashSet<SetPart>>>? setPartsResponse = await Backend.GetSetParts(WebBrowser, Bot.SteamID, acceptedMatchableTypes, realAppIDs).ConfigureAwait(false);

				if (setPartsResponse == null) {
					// This is actually a network failure, so we'll stop sending heartbeats but not record it as valid check
					ShouldSendHeartBeats = false;
					Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorObjectIsNull, nameof(setPartsResponse)));

					return;
				}

				if (setPartsResponse.StatusCode.IsRedirectionCode()) {
					ShouldSendHeartBeats = false;
					Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, setPartsResponse.StatusCode));

					if (setPartsResponse.FinalUri.Host != ArchiWebHandler.SteamCommunityURL.Host) {
						ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(setPartsResponse.FinalUri), setPartsResponse.FinalUri));

						return;
					}

					// We've expected the result, not the redirection to the sign in, we need to authenticate again
					SignedInWithSteam = false;

					return;
				}

				if (!setPartsResponse.StatusCode.IsSuccessCode()) {
					// ArchiNet told us that we've sent a bad request, so the process should restart from the beginning at later time
					ShouldSendHeartBeats = false;
					Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, setPartsResponse.StatusCode));

					switch (setPartsResponse.StatusCode) {
						case HttpStatusCode.Forbidden:
							// ArchiNet told us to stop submitting data for now
							LastAnnouncement = DateTime.UtcNow.AddYears(1);

							return;
						case HttpStatusCode.TooManyRequests:
							// ArchiNet told us to try again later
							LastAnnouncement = DateTime.UtcNow.AddDays(1);

							return;
						default:
							// There is something wrong with our payload or the server, we shouldn't retry for at least several hours
							LastAnnouncement = DateTime.UtcNow.AddHours(6);

							return;
					}
				}

				if (setPartsResponse.Content?.Result == null) {
					// This should never happen if we got the correct response
					Bot.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(setPartsResponse), setPartsResponse.Content?.Result));

					return;
				}

				Dictionary<(uint RealAppID, EAssetType Type, EAssetRarity Rarity), HashSet<ulong>> databaseSets = setPartsResponse.Content.Result.GroupBy(static setPart => (setPart.RealAppID, setPart.Type, setPart.Rarity)).ToDictionary(static group => group.Key, static group => group.Select(static setPart => setPart.ClassID).ToHashSet());

				Dictionary<ulong, uint> setCopy = [];

				foreach (((uint RealAppID, EAssetType Type, EAssetRarity Rarity) key, Dictionary<ulong, uint> set) in state) {
					if (!databaseSets.TryGetValue(key, out HashSet<ulong>? databaseSet)) {
						// We have no clue about this set, we can't do any optimization
						continue;
					}

					if ((databaseSet.Count != set.Count) || !databaseSet.SetEquals(set.Keys)) {
						// User either has more or less classIDs than we know about, we can't optimize this
						continue;
					}

					// User has all classIDs we know about, we can deduplicate his items based on lowest count
					setCopy.Clear();

					uint minimumAmount = uint.MaxValue;

					foreach ((ulong classID, uint amount) in set) {
						if (amount < minimumAmount) {
							minimumAmount = amount;
						}

						setCopy[classID] = amount;
					}

					foreach ((ulong classID, uint amount) in setCopy) {
						if (minimumAmount >= amount) {
							set.Remove(classID);

							continue;
						}

						set[classID] = amount - minimumAmount;
					}
				}

				HashSet<AssetForListing> assetsForListingFiltered = [];

				foreach (AssetForListing asset in assetsForListing.Where(asset => state.TryGetValue((asset.RealAppID, asset.Type, asset.Rarity), out Dictionary<ulong, uint>? setState) && setState.TryGetValue(asset.ClassID, out uint targetAmount) && (targetAmount > 0)).OrderByDescending(static asset => asset.Tradable).ThenByDescending(static asset => asset.Index)) {
					(uint RealAppID, EAssetType Type, EAssetRarity Rarity) key = (asset.RealAppID, asset.Type, asset.Rarity);

					if (!state.TryGetValue(key, out Dictionary<ulong, uint>? setState) || !setState.TryGetValue(asset.ClassID, out uint targetAmount) || (targetAmount == 0)) {
						// We're not interested in this combination
						continue;
					}

					if (asset.Amount >= targetAmount) {
						asset.Amount = targetAmount;

						if (setState.Remove(asset.ClassID) && (setState.Count == 0)) {
							state.Remove(key);
						}
					} else {
						setState[asset.ClassID] = targetAmount - asset.Amount;
					}

					assetsForListingFiltered.Add(asset);
				}

				assetsForListing = assetsForListingFiltered.OrderBy(static asset => asset.Index).ToList();

				if (assetsForListing.Count == 0) {
					// We're not eligible, record this as a valid check
					LastAnnouncement = DateTime.UtcNow;
					ShouldSendAnnouncementEarlier = ShouldSendHeartBeats = false;

					// There is a possibility that our inventory has changed even if our announced assets did not, record that
					BotCache.LastInventoryChecksumBeforeDeduplication = inventoryChecksumBeforeDeduplication;

					return;
				}
			}

			if (assetsForListing.Count > MaxItemsCount) {
				// We're not eligible, record this as a valid check
				LastAnnouncement = DateTime.UtcNow;
				ShouldSendAnnouncementEarlier = ShouldSendHeartBeats = false;

				// There is a possibility that our inventory has changed even if our announced assets did not, record that
				BotCache.LastInventoryChecksumBeforeDeduplication = inventoryChecksumBeforeDeduplication;

				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, $"{nameof(assetsForListing)} > {MaxItemsCount}"));

				return;
			}

			string checksum = Backend.GenerateChecksumFor(assetsForListing);
			string? previousChecksum = BotCache.LastAnnouncedAssetsForListing.Count > 0 ? Backend.GenerateChecksumFor(BotCache.LastAnnouncedAssetsForListing) : null;

			if (BotCache.LastRequestAt.HasValue && (DateTime.UtcNow.Subtract(BotCache.LastRequestAt.Value).TotalDays < MaxInactivityDays) && (tradeToken == BotCache.LastAnnouncedTradeToken) && (checksum == previousChecksum)) {
				// We've determined our state to be the same, we can skip announce entirely and start sending heartbeats exclusively
				bool triggerImmediately = !ShouldSendHeartBeats;

				LastAnnouncement = DateTime.UtcNow;
				ShouldSendAnnouncementEarlier = false;
				ShouldSendHeartBeats = true;

				if (triggerImmediately) {
					Utilities.InBackground(() => OnHeartBeatTimer());
				}

				// There is a possibility that our inventory has changed even if our announced assets did not, record that
				BotCache.LastInventoryChecksumBeforeDeduplication = inventoryChecksumBeforeDeduplication;

				return;
			}

			if (BotCache.LastAnnouncedAssetsForListing.Count > 0) {
				Dictionary<ulong, AssetForListing> previousInventoryState = BotCache.LastAnnouncedAssetsForListing.ToDictionary(static asset => asset.AssetID);

				HashSet<AssetForListing> inventoryAddedChanged = assetsForListing.Where(asset => !previousInventoryState.Remove(asset.AssetID, out AssetForListing? previousAsset) || (asset.BackendHashCode != previousAsset.BackendHashCode)).ToHashSet();

				Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Localization.Strings.ListingAnnouncing, Bot.SteamID, nickname ?? Bot.SteamID.ToString(CultureInfo.InvariantCulture), assetsForListing.Count));

				ObjectResponse<GenericResponse<BackgroundTaskResponse>>? diffResponse = null;
				Guid diffRequestID = Guid.Empty;

				for (byte i = 0; i < WebBrowser.MaxTries; i++) {
					if (diffRequestID != Guid.Empty) {
						diffResponse = await Backend.PollResult(WebBrowser, Bot.SteamID, diffRequestID).ConfigureAwait(false);
					} else {
						diffResponse = await Backend.AnnounceDiffForListing(WebBrowser, Bot.SteamID, inventoryAddedChanged, checksum, acceptedMatchableTypes, (uint) inventory.Count, matchEverything, tradeToken, previousInventoryState.Values, previousChecksum, nickname, avatarHash).ConfigureAwait(false);
					}

					if (diffResponse == null) {
						// This is actually a network failure, so we'll stop sending heartbeats but not record it as valid check
						ShouldSendHeartBeats = false;
						Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorObjectIsNull, nameof(diffResponse)));

						return;
					}

					if (diffResponse.StatusCode.IsRedirectionCode()) {
						ShouldSendHeartBeats = false;
						Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, diffResponse.StatusCode));

						if (diffResponse.FinalUri.Host != ArchiWebHandler.SteamCommunityURL.Host) {
							ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(diffResponse.FinalUri), diffResponse.FinalUri));

							return;
						}

						// We've expected the result, not the redirection to the sign in, we need to authenticate again
						SignedInWithSteam = false;

						return;
					}

					if (!diffResponse.StatusCode.IsSuccessCode()) {
						// ArchiNet told us that we've sent a bad request, so the process should restart from the beginning at later time
						ShouldSendHeartBeats = false;
						Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, diffResponse.StatusCode));

						switch (diffResponse.StatusCode) {
							case HttpStatusCode.Conflict:
								// ArchiNet told us to do full announcement instead, the only non-OK response we accept
								break;
							case HttpStatusCode.Forbidden:
								// ArchiNet told us to stop submitting data for now
								LastAnnouncement = DateTime.UtcNow.AddYears(1);

								return;
							case HttpStatusCode.TooManyRequests:
								// ArchiNet told us to try again later
								LastAnnouncement = DateTime.UtcNow.AddDays(1);

								return;
							default:
								// There is something wrong with our payload or the server, we shouldn't retry for at least several hours
								LastAnnouncement = DateTime.UtcNow.AddHours(6);

								return;
						}

						break;
					}

					// Great, do we need to wait?
					if (diffResponse.Content?.Result == null) {
						// This should never happen if we got the correct response
						Bot.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(diffResponse), diffResponse.Content?.Result));

						return;
					}

					if (diffResponse.Content.Result.Finished) {
						break;
					}

					diffRequestID = diffResponse.Content.Result.RequestID;
					diffResponse = null;
				}

				if (diffResponse == null) {
					// We've waited long enough, something is definitely wrong with us or the backend
					Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, nameof(diffResponse)));

					return;
				}

				if (diffResponse.StatusCode.IsSuccessCode() && diffResponse.Content is { Success: true, Result.Finished: true }) {
					// Our diff announce has succeeded, we have nothing to do further
					Bot.ArchiLogger.LogGenericInfo(Strings.Success);

					LastAnnouncement = LastHeartBeat = DateTime.UtcNow;
					ShouldSendAnnouncementEarlier = false;
					ShouldSendHeartBeats = true;

					BotCache.LastAnnouncedAssetsForListing.ReplaceWith(assetsForListing);
					BotCache.LastAnnouncedTradeToken = tradeToken;
					BotCache.LastInventoryChecksumBeforeDeduplication = inventoryChecksumBeforeDeduplication;
					BotCache.LastRequestAt = LastHeartBeat;

					return;
				}
			}

			Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Localization.Strings.ListingAnnouncing, Bot.SteamID, nickname ?? Bot.SteamID.ToString(CultureInfo.InvariantCulture), assetsForListing.Count));

			ObjectResponse<GenericResponse<BackgroundTaskResponse>>? announceResponse = null;
			Guid announceRequestID = Guid.Empty;

			for (byte i = 0; i < WebBrowser.MaxTries; i++) {
				if (announceRequestID != Guid.Empty) {
					announceResponse = await Backend.PollResult(WebBrowser, Bot.SteamID, announceRequestID).ConfigureAwait(false);
				} else {
					announceResponse = await Backend.AnnounceForListing(WebBrowser, Bot.SteamID, assetsForListing, checksum, acceptedMatchableTypes, (uint) inventory.Count, matchEverything, tradeToken, nickname, avatarHash).ConfigureAwait(false);
				}

				if (announceResponse == null) {
					// This is actually a network failure, so we'll stop sending heartbeats but not record it as valid check
					ShouldSendHeartBeats = false;
					Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorObjectIsNull, nameof(announceResponse)));

					return;
				}

				if (announceResponse.StatusCode.IsRedirectionCode()) {
					ShouldSendHeartBeats = false;
					Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, announceResponse.StatusCode));

					if (announceResponse.FinalUri.Host != ArchiWebHandler.SteamCommunityURL.Host) {
						ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(announceResponse.FinalUri), announceResponse.FinalUri));

						return;
					}

					// We've expected the result, not the redirection to the sign in, we need to authenticate again
					SignedInWithSteam = false;

					return;
				}

				if (!announceResponse.StatusCode.IsSuccessCode()) {
					// ArchiNet told us that we've sent a bad request, so the process should restart from the beginning at later time
					ShouldSendHeartBeats = false;
					Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, announceResponse.StatusCode));

					switch (announceResponse.StatusCode) {
						case HttpStatusCode.Conflict:
							// ArchiNet told us to that we've applied wrong deduplication logic, we can try again in a second
							LastAnnouncement = DateTime.UtcNow.AddMinutes(5);

							return;
						case HttpStatusCode.Forbidden:
							// ArchiNet told us to stop submitting data for now
							LastAnnouncement = DateTime.UtcNow.AddYears(1);

							return;
						case HttpStatusCode.TooManyRequests:
							// ArchiNet told us to try again later
							LastAnnouncement = DateTime.UtcNow.AddDays(1);

							return;
						default:
							// There is something wrong with our payload or the server, we shouldn't retry for at least several hours
							LastAnnouncement = DateTime.UtcNow.AddHours(6);

							return;
					}
				}

				// Great, do we need to wait?
				if (announceResponse.Content?.Result == null) {
					// This should never happen if we got the correct response
					Bot.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(announceResponse), announceResponse.Content?.Result));

					return;
				}

				if (announceResponse.Content.Result.Finished) {
					break;
				}

				announceRequestID = announceResponse.Content.Result.RequestID;
				announceResponse = null;
			}

			if (announceResponse == null) {
				// We've waited long enough, something is definitely wrong with us or the backend
				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, nameof(announceResponse)));

				return;
			}

			if (announceResponse.StatusCode.IsSuccessCode() && announceResponse.Content is { Success: true, Result.Finished: true }) {
				// Our diff announce has succeeded, we have nothing to do further
				Bot.ArchiLogger.LogGenericInfo(Strings.Success);

				LastAnnouncement = LastHeartBeat = DateTime.UtcNow;
				ShouldSendAnnouncementEarlier = false;
				ShouldSendHeartBeats = true;

				BotCache.LastAnnouncedAssetsForListing.ReplaceWith(assetsForListing);
				BotCache.LastAnnouncedTradeToken = tradeToken;
				BotCache.LastInventoryChecksumBeforeDeduplication = inventoryChecksumBeforeDeduplication;
				BotCache.LastRequestAt = LastHeartBeat;

				return;
			}

			// Everything we've tried has failed
			Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
		} finally {
			RequestsSemaphore.Release();
		}
	}

	internal void TriggerMatchActivelyEarlier() {
		if (MatchActivelyTimer == null) {
			Utilities.InBackground(() => MatchActively());
		} else {
			// ReSharper disable once SuspiciousLockOverSynchronizationPrimitive - this is not a mistake, we need extra synchronization, and we can re-use the semaphore object for that
			lock (MatchActivelySemaphore) {
				MatchActivelyTimer.Change(TimeSpan.Zero, TimeSpan.FromHours(6));
			}
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

		CCredentials_LastCredentialChangeTime_Response? credentialChangeTimeDetails = await Bot.ArchiHandler.GetCredentialChangeTimeDetails().ConfigureAwait(false);

		if (credentialChangeTimeDetails == null) {
			Bot.ArchiLogger.LogGenericTrace(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, $"{nameof(credentialChangeTimeDetails)}: null"));

			return null;
		}

		// Bot didn't change password in last 5 days
		if ((credentialChangeTimeDetails.timestamp_last_password_reset > 0) && ((DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(credentialChangeTimeDetails.timestamp_last_password_reset)).TotalDays < MinimumPasswordResetCooldownDays)) {
			Bot.ArchiLogger.LogGenericTrace(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, $"{nameof(credentialChangeTimeDetails.timestamp_last_password_reset)}: {credentialChangeTimeDetails.timestamp_last_password_reset}"));

			return false;
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

		if (!Bot.IsConnectedAndLoggedOn || Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.MatchEverything)) {
			Bot.ArchiLogger.LogGenericTrace(Strings.ErrorAborted);

			return;
		}

		bool? eligible = await IsEligibleForMatching().ConfigureAwait(false);

		if (eligible != true) {
			Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, $"{nameof(IsEligibleForMatching)}: {eligible?.ToString() ?? "null"}"));

			return;
		}

		HashSet<EAssetType> acceptedMatchableTypes = Bot.BotConfig.MatchableTypes.Where(AcceptedMatchableTypes.Contains).ToHashSet();

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

			HttpStatusCode? licenseStatus = await Backend.GetLicenseStatus(ASF.GlobalConfig.LicenseID.Value, WebBrowser).ConfigureAwait(false);

			if (licenseStatus == null) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, nameof(licenseStatus)));

				return;
			}

			if (!licenseStatus.Value.IsSuccessCode()) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, licenseStatus.Value));

				return;
			}

			HashSet<Asset> assetsForMatching;

			try {
				assetsForMatching = await Bot.ArchiHandler.GetMyInventoryAsync().Where(item => item is { AssetID: > 0, Amount: > 0, ClassID: > 0, RealAppID: > 0, Type: > EAssetType.Unknown, Rarity: > EAssetRarity.Unknown, IsSteamPointsShopItem: false } && acceptedMatchableTypes.Contains(item.Type) && !Bot.BotDatabase.MatchActivelyBlacklistAppIDs.Contains(item.RealAppID)).ToHashSetAsync().ConfigureAwait(false);
			} catch (TimeoutException e) {
				Bot.ArchiLogger.LogGenericWarningException(e);
				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, nameof(assetsForMatching)));

				return;
			} catch (Exception e) {
				Bot.ArchiLogger.LogGenericException(e);
				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, nameof(assetsForMatching)));

				return;
			}

			if (assetsForMatching.Count == 0) {
				Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(assetsForMatching)));

				return;
			}

			// Remove from our inventory items that can't be possibly matched due to no dupes to offer available
			HashSet<(uint RealAppID, EAssetType Type, EAssetRarity Rarity)> setsToKeep = Trading.GetInventorySets(assetsForMatching).Where(static set => set.Value.Any(static amount => amount > 1)).Select(static set => set.Key).ToHashSet();

			if (assetsForMatching.RemoveWhere(item => !setsToKeep.Contains((item.RealAppID, item.Type, item.Rarity))) > 0) {
				if (assetsForMatching.Count == 0) {
					Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(assetsForMatching)));

					return;
				}
			}

			// We should deduplicate our sets before sending them to the server, for doing that we'll use ASFB set parts data
			HashSet<uint> realAppIDs = [];
			Dictionary<(uint RealAppID, EAssetType Type, EAssetRarity Rarity), Dictionary<ulong, uint>> setsState = new();

			foreach (Asset asset in assetsForMatching) {
				realAppIDs.Add(asset.RealAppID);

				(uint RealAppID, EAssetType Type, EAssetRarity Rarity) key = (asset.RealAppID, asset.Type, asset.Rarity);

				if (setsState.TryGetValue(key, out Dictionary<ulong, uint>? set)) {
					set[asset.ClassID] = set.GetValueOrDefault(asset.ClassID) + asset.Amount;
				} else {
					setsState[key] = new Dictionary<ulong, uint> { { asset.ClassID, asset.Amount } };
				}
			}

			if (!SignedInWithSteam) {
				HttpStatusCode? signInWithSteam = await ArchiNet.SignInWithSteam(Bot, WebBrowser).ConfigureAwait(false);

				if ((signInWithSteam == null) || !signInWithSteam.Value.IsSuccessCode()) {
					// This is actually a network failure
					return;
				}

				SignedInWithSteam = true;
			}

			ObjectResponse<GenericResponse<ImmutableHashSet<SetPart>>>? setPartsResponse = await Backend.GetSetParts(WebBrowser, Bot.SteamID, acceptedMatchableTypes, realAppIDs).ConfigureAwait(false);

			if (setPartsResponse == null) {
				// This is actually a network failure
				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorObjectIsNull, nameof(setPartsResponse)));

				return;
			}

			if (setPartsResponse.StatusCode.IsRedirectionCode()) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, setPartsResponse.StatusCode));

				if (setPartsResponse.FinalUri.Host != ArchiWebHandler.SteamCommunityURL.Host) {
					ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(setPartsResponse.FinalUri), setPartsResponse.FinalUri));

					return;
				}

				// We've expected the result, not the redirection to the sign in, we need to authenticate again
				SignedInWithSteam = false;

				return;
			}

			if (!setPartsResponse.StatusCode.IsSuccessCode()) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, setPartsResponse.StatusCode));

				return;
			}

			if (setPartsResponse.Content?.Result == null) {
				// This should never happen if we got the correct response
				Bot.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(setPartsResponse), setPartsResponse.Content?.Result));

				return;
			}

			Dictionary<(uint RealAppID, EAssetType Type, EAssetRarity Rarity), HashSet<ulong>> databaseSets = setPartsResponse.Content.Result.GroupBy(static setPart => (setPart.RealAppID, setPart.Type, setPart.Rarity)).ToDictionary(static group => group.Key, static group => group.Select(static setPart => setPart.ClassID).ToHashSet());

			Dictionary<ulong, uint> setCopy = [];

			foreach (((uint RealAppID, EAssetType Type, EAssetRarity Rarity) key, Dictionary<ulong, uint> set) in setsState) {
				uint minimumAmount = uint.MaxValue;
				uint maximumAmount = uint.MinValue;

				foreach (uint amount in set.Values) {
					if (amount < minimumAmount) {
						minimumAmount = amount;
					}

					if (amount > maximumAmount) {
						maximumAmount = amount;
					}
				}

				if (maximumAmount < 2) {
					// We don't have anything to swap with, remove all entries from this set
					set.Clear();

					continue;
				}

				if (!databaseSets.TryGetValue(key, out HashSet<ulong>? databaseSet)) {
					// We have no clue about this set, we can't do any optimization
					continue;
				}

				if ((databaseSet.Count != set.Count) || !databaseSet.SetEquals(set.Keys)) {
					// User either has more or less classIDs than we know about, we can't optimize this
					continue;
				}

				if (maximumAmount - minimumAmount < 2) {
					// We don't have anything to swap with, remove all entries from this set
					set.Clear();

					continue;
				}

				// User has all classIDs we know about, we can deduplicate his items based on lowest count
				setCopy.Clear();

				foreach ((ulong classID, uint amount) in set) {
					setCopy[classID] = amount;
				}

				foreach ((ulong classID, uint amount) in setCopy) {
					if (minimumAmount >= amount) {
						set.Remove(classID);

						continue;
					}

					set[classID] = amount - minimumAmount;
				}
			}

			HashSet<Asset> assetsForMatchingFiltered = [];

			foreach (Asset asset in assetsForMatching.Where(asset => setsState.TryGetValue((asset.RealAppID, asset.Type, asset.Rarity), out Dictionary<ulong, uint>? setState) && setState.TryGetValue(asset.ClassID, out uint targetAmount) && (targetAmount > 0)).OrderByDescending(static asset => asset.Tradable)) {
				(uint RealAppID, EAssetType Type, EAssetRarity Rarity) key = (asset.RealAppID, asset.Type, asset.Rarity);

				if (!setsState.TryGetValue(key, out Dictionary<ulong, uint>? setState) || !setState.TryGetValue(asset.ClassID, out uint targetAmount) || (targetAmount == 0)) {
					// We're not interested in this combination
					continue;
				}

				if (asset.Amount >= targetAmount) {
					asset.Amount = targetAmount;

					if (setState.Remove(asset.ClassID) && (setState.Count == 0)) {
						setsState.Remove(key);
					}
				} else {
					setState[asset.ClassID] = targetAmount - asset.Amount;
				}

				assetsForMatchingFiltered.Add(asset);
			}

			assetsForMatching = assetsForMatchingFiltered;

			if (assetsForMatching.Count == 0) {
				Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(assetsForMatching)));

				return;
			}

			if (assetsForMatching.Count > MaxItemsCount) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, $"{nameof(assetsForMatching)} > {MaxItemsCount}"));

				return;
			}

			(HttpStatusCode StatusCode, ImmutableHashSet<ListedUser> Users)? response = await Backend.GetListedUsersForMatching(ASF.GlobalConfig.LicenseID.Value, Bot, WebBrowser, assetsForMatching, acceptedMatchableTypes).ConfigureAwait(false);

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

			using (await Bot.Actions.GetTradingLock().ConfigureAwait(false)) {
				tradesSent = await MatchActively(response.Value.Users, assetsForMatching, acceptedMatchableTypes).ConfigureAwait(false);
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

	private async Task<bool> MatchActively(ImmutableHashSet<ListedUser> listedUsers, HashSet<Asset> ourAssets, HashSet<EAssetType> acceptedMatchableTypes) {
		if ((listedUsers == null) || (listedUsers.Count == 0)) {
			throw new ArgumentNullException(nameof(listedUsers));
		}

		if ((ourAssets == null) || (ourAssets.Count == 0)) {
			throw new ArgumentNullException(nameof(ourAssets));
		}

		if ((acceptedMatchableTypes == null) || (acceptedMatchableTypes.Count == 0)) {
			throw new ArgumentNullException(nameof(acceptedMatchableTypes));
		}

		(Dictionary<(uint RealAppID, EAssetType Type, EAssetRarity Rarity), Dictionary<ulong, uint>> ourFullState, Dictionary<(uint RealAppID, EAssetType Type, EAssetRarity Rarity), Dictionary<ulong, uint>> ourTradableState) = MatchingUtilities.GetDividedInventoryState(ourAssets);

		if (MatchingUtilities.IsEmptyForMatching(ourFullState, ourTradableState)) {
			// User doesn't have any more dupes in the inventory
			Bot.ArchiLogger.LogGenericTrace(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, $"{nameof(ourFullState)} || {nameof(ourTradableState)}"));

			return false;
		}

		// Cancel previous trade offers sent and deprioritize SteamIDs that didn't answer us in this round
		HashSet<ulong>? matchActivelyTradeOfferIDs = null;

		JsonElement matchActivelyTradeOfferIDsToken = Bot.BotDatabase.LoadFromJsonStorage(MatchActivelyTradeOfferIDsStorageKey);

		if (matchActivelyTradeOfferIDsToken.ValueKind == JsonValueKind.Array) {
			try {
				matchActivelyTradeOfferIDs = new HashSet<ulong>(matchActivelyTradeOfferIDsToken.GetArrayLength());

				foreach (JsonElement tradeIDElement in matchActivelyTradeOfferIDsToken.EnumerateArray()) {
					if (!tradeIDElement.TryGetUInt64(out ulong tradeID)) {
						continue;
					}

					matchActivelyTradeOfferIDs.Add(tradeID);
				}
			} catch (Exception e) {
				Bot.ArchiLogger.LogGenericWarningException(e);
			}
		}

		matchActivelyTradeOfferIDs ??= [];

		HashSet<ulong> deprioritizedSteamIDs = [];

		if (matchActivelyTradeOfferIDs.Count > 0) {
			// This is not a mandatory step, we allow it to fail
			HashSet<TradeOffer>? sentTradeOffers = await Bot.ArchiWebHandler.GetTradeOffers(true, false, true, false).ConfigureAwait(false);

			if (sentTradeOffers != null) {
				HashSet<ulong> activeTradeOfferIDs = [];

				foreach (TradeOffer tradeOffer in sentTradeOffers.Where(tradeOffer => (tradeOffer.State == ETradeOfferState.Active) && matchActivelyTradeOfferIDs.Contains(tradeOffer.TradeOfferID))) {
					deprioritizedSteamIDs.Add(tradeOffer.OtherSteamID64);

					if (!await Bot.ArchiWebHandler.CancelTradeOffer(tradeOffer.TradeOfferID).ConfigureAwait(false)) {
						activeTradeOfferIDs.Add(tradeOffer.TradeOfferID);
					}
				}

				if (!matchActivelyTradeOfferIDs.SetEquals(activeTradeOfferIDs)) {
					matchActivelyTradeOfferIDs = activeTradeOfferIDs;

					if (matchActivelyTradeOfferIDs.Count > 0) {
						Bot.BotDatabase.SaveToJsonStorage(MatchActivelyTradeOfferIDsStorageKey, matchActivelyTradeOfferIDs);
					} else {
						Bot.BotDatabase.DeleteFromJsonStorage(MatchActivelyTradeOfferIDsStorageKey);
					}
				}
			}
		}

		Dictionary<ulong, Asset> ourInventory = ourAssets.ToDictionary(static asset => asset.AssetID);

		HashSet<ulong> pendingMobileTradeOfferIDs = [];

		byte maxTradeHoldDuration = ASF.GlobalConfig?.MaxTradeHoldDuration ?? GlobalConfig.DefaultMaxTradeHoldDuration;

		byte failuresInRow = 0;
		uint matchedSets = 0;

		HashSet<(uint RealAppID, EAssetType Type, EAssetRarity Rarity)> skippedSetsThisUser = [];
		HashSet<(uint RealAppID, EAssetType Type, EAssetRarity Rarity)> skippedSetsThisTrade = [];

		Dictionary<ulong, uint> classIDsToGive = new();
		Dictionary<ulong, uint> classIDsToReceive = new();
		Dictionary<ulong, uint> fairClassIDsToGive = new();
		Dictionary<ulong, uint> fairClassIDsToReceive = new();

		foreach (ListedUser listedUser in listedUsers.Where(listedUser => (listedUser.SteamID != Bot.SteamID) && acceptedMatchableTypes.Any(listedUser.MatchableTypes.Contains) && !Bot.IsBlacklistedFromTrades(listedUser.SteamID)).OrderByDescending(listedUser => !deprioritizedSteamIDs.Contains(listedUser.SteamID)).ThenByDescending(static listedUser => listedUser.TotalGamesCount > 1).ThenByDescending(static listedUser => listedUser.MatchEverything).ThenBy(static listedUser => listedUser.TotalInventoryCount)) {
			if (failuresInRow >= WebBrowser.MaxTries) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, $"{nameof(failuresInRow)} >= {WebBrowser.MaxTries}"));

				break;
			}

			if (!Bot.IsConnectedAndLoggedOn) {
				Bot.ArchiLogger.LogGenericWarning(Strings.BotNotConnected);

				break;
			}

			HashSet<(uint RealAppID, EAssetType Type, EAssetRarity Rarity)> wantedSets = ourTradableState.Keys.Where(set => listedUser.MatchableTypes.Contains(set.Type)).ToHashSet();

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

			HashSet<Asset> theirInventory = listedUser.Assets.Where(item => (!listedUser.MatchEverything || item.Tradable) && wantedSets.Contains((item.RealAppID, item.Type, item.Rarity)) && ((tradeHoldDuration.Value == 0) || !(item.Type is EAssetType.FoilTradingCard or EAssetType.TradingCard && CardsFarmer.SalesBlacklist.Contains(item.RealAppID)))).Select(static asset => asset.ToAsset()).ToHashSet();

			if (theirInventory.Count == 0) {
				continue;
			}

			skippedSetsThisUser.Clear();

			Dictionary<(uint RealAppID, EAssetType Type, EAssetRarity Rarity), Dictionary<ulong, uint>> theirTradableState = MatchingUtilities.GetTradableInventoryState(theirInventory);

			for (byte i = 0; i < Trading.MaxTradesPerAccount; i++) {
				byte itemsInTrade = 0;

				skippedSetsThisTrade.Clear();

				classIDsToGive.Clear();
				classIDsToReceive.Clear();
				fairClassIDsToGive.Clear();
				fairClassIDsToReceive.Clear();

				foreach (((uint RealAppID, EAssetType Type, EAssetRarity Rarity) set, Dictionary<ulong, uint> ourFullItems) in ourFullState.Where(set => !skippedSetsThisUser.Contains(set.Key) && listedUser.MatchableTypes.Contains(set.Key.Type) && set.Value.Values.Any(static count => count > 1))) {
					if (!ourTradableState.TryGetValue(set, out Dictionary<ulong, uint>? ourTradableItems) || (ourTradableItems.Count == 0)) {
						// We may have no more tradable items from this set
						continue;
					}

					if (!theirTradableState.TryGetValue(set, out Dictionary<ulong, uint>? theirTradableItems) || (theirTradableItems.Count == 0)) {
						// They may have no more tradable items from this set
						continue;
					}

					if (MatchingUtilities.IsEmptyForMatching(ourFullItems, ourTradableItems)) {
						// We may have no more matchable items from this set
						continue;
					}

					// Those 2 collections are on user-basis since we can't be sure that the trade passes through (and therefore we need to keep original state in case of a failure)
					Dictionary<ulong, uint> ourFullSet = ourFullItems.ToDictionary();
					Dictionary<ulong, uint> ourTradableSet = ourTradableItems.ToDictionary();

					bool match;

					do {
						match = false;

						foreach ((ulong ourItem, uint ourFullAmount) in ourFullSet.Where(static item => item.Value > 1).OrderByDescending(static item => item.Value)) {
							if (!ourTradableSet.TryGetValue(ourItem, out uint ourTradableAmount) || (ourTradableAmount == 0)) {
								continue;
							}

							foreach ((ulong theirItem, uint theirTradableAmount) in theirTradableItems.OrderBy(item => ourFullSet.GetValueOrDefault(item.Key))) {
								if (ourFullSet.TryGetValue(theirItem, out uint ourAmountOfTheirItem) && (ourFullAmount <= ourAmountOfTheirItem + 1)) {
									continue;
								}

								if (!listedUser.MatchEverything) {
									// We have a potential match, let's check fairness for them
									uint fairGivenAmount = fairClassIDsToGive.GetValueOrDefault(ourItem);
									uint fairReceivedAmount = fairClassIDsToReceive.GetValueOrDefault(theirItem);

									fairClassIDsToGive[ourItem] = ++fairGivenAmount;
									fairClassIDsToReceive[theirItem] = ++fairReceivedAmount;

									// Filter their inventory for the sets we're trading or have traded with this user
									HashSet<Asset> fairFiltered = theirInventory.Where(item => ((item.RealAppID == set.RealAppID) && (item.Type == set.Type) && (item.Rarity == set.Rarity)) || skippedSetsThisTrade.Contains((item.RealAppID, item.Type, item.Rarity))).ToHashSet();

									// Get tradable items from our and their inventory
									HashSet<Asset> fairItemsToGive = MatchingUtilities.GetTradableItemsFromInventory(ourInventory.Values.Where(item => ((item.RealAppID == set.RealAppID) && (item.Type == set.Type) && (item.Rarity == set.Rarity)) || skippedSetsThisTrade.Contains((item.RealAppID, item.Type, item.Rarity))).ToHashSet(), fairClassIDsToGive);
									HashSet<Asset> fairItemsToReceive = MatchingUtilities.GetTradableItemsFromInventory(fairFiltered, fairClassIDsToReceive);

									// Actual check, since we do this against remote user, we flip places for items
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
								classIDsToGive[ourItem] = classIDsToGive.GetValueOrDefault(ourItem) + 1;
								ourFullSet[ourItem] = ourFullAmount - 1; // We don't need to remove anything here because we can guarantee that ourItem.Value is at least 2

								// Update our state based on received items
								classIDsToReceive[theirItem] = classIDsToReceive.GetValueOrDefault(theirItem) + 1;
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
				HashSet<Asset> itemsToGive = MatchingUtilities.GetTradableItemsFromInventory(ourInventory.Values, classIDsToGive);
				HashSet<Asset> itemsToReceive = MatchingUtilities.GetTradableItemsFromInventory(theirInventory, classIDsToReceive, true);

				if ((itemsToGive.Count != itemsToReceive.Count) || !Trading.IsFairExchange(itemsToGive, itemsToReceive)) {
					// Failsafe
					throw new InvalidOperationException($"{nameof(itemsToGive)} && {nameof(itemsToReceive)}");
				}

				Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Localization.Strings.MatchingFound, itemsToReceive.Count, listedUser.SteamID, listedUser.Nickname));

				Bot.ArchiLogger.LogGenericTrace($"{Bot.SteamID} <- {string.Join(", ", itemsToReceive.Select(static item => $"{item.RealAppID}/{item.Type}/{item.Rarity}/{item.ClassID} #{item.Amount}"))} | {string.Join(", ", itemsToGive.Select(static item => $"{item.RealAppID}/{item.Type}/{item.Rarity}/{item.ClassID} #{item.Amount}"))} -> {listedUser.SteamID}");

				(bool success, HashSet<ulong>? tradeOfferIDs, HashSet<ulong>? mobileTradeOfferIDs) = await Bot.ArchiWebHandler.SendTradeOffer(listedUser.SteamID, itemsToGive, itemsToReceive, listedUser.TradeToken, true).ConfigureAwait(false);

				if (tradeOfferIDs?.Count > 0) {
					matchActivelyTradeOfferIDs.UnionWith(tradeOfferIDs);

					Bot.BotDatabase.SaveToJsonStorage(MatchActivelyTradeOfferIDsStorageKey, matchActivelyTradeOfferIDs);
				}

				if (mobileTradeOfferIDs?.Count > 0) {
					pendingMobileTradeOfferIDs.UnionWith(mobileTradeOfferIDs);

					if (pendingMobileTradeOfferIDs.Count >= MaxTradeOffersActive) {
						(bool twoFactorSuccess, IReadOnlyCollection<Confirmation>? handledConfirmations, _) = await Bot.Actions.HandleTwoFactorAuthenticationConfirmations(true, Confirmation.EConfirmationType.Trade, pendingMobileTradeOfferIDs, true).ConfigureAwait(false);

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
						item.Description ??= new InventoryDescription(itemToReceive.AppID, itemToReceive.ClassID, itemToReceive.InstanceID, realAppID: itemToReceive.RealAppID, type: itemToReceive.Type, rarity: itemToReceive.Rarity);

						item.Description.Body.tradable = false;
						item.Amount += itemToReceive.Amount;
					} else {
						itemToReceive.Description ??= new InventoryDescription(itemToReceive.AppID, itemToReceive.ClassID, itemToReceive.InstanceID, realAppID: itemToReceive.RealAppID, type: itemToReceive.Type, rarity: itemToReceive.Rarity);

						itemToReceive.Description.Body.tradable = false;
						ourInventory[itemToReceive.AssetID] = itemToReceive;
					}

					if (!ourFullState.TryGetValue((itemToReceive.RealAppID, itemToReceive.Type, itemToReceive.Rarity), out Dictionary<ulong, uint>? fullAmounts)) {
						// We're receiving items from a set we don't even have?
						throw new InvalidOperationException(nameof(fullAmounts));
					}

					fullAmounts[itemToReceive.ClassID] = fullAmounts.GetValueOrDefault(itemToReceive.ClassID) + itemToReceive.Amount;
				}

				skippedSetsThisUser.UnionWith(skippedSetsThisTrade);
			}

			if (skippedSetsThisUser.Count == 0) {
				continue;
			}

			matchedSets += (uint) skippedSetsThisUser.Count;

			if (MatchingUtilities.IsEmptyForMatching(ourFullState, ourTradableState)) {
				// User doesn't have any more dupes in the inventory
				break;
			}
		}

		if (pendingMobileTradeOfferIDs.Count > 0) {
			(bool twoFactorSuccess, IReadOnlyCollection<Confirmation>? handledConfirmations, _) = Bot.IsConnectedAndLoggedOn ? await Bot.Actions.HandleTwoFactorAuthenticationConfirmations(true, Confirmation.EConfirmationType.Trade, pendingMobileTradeOfferIDs, true).ConfigureAwait(false) : (false, null, null);

			if (!twoFactorSuccess) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Localization.Strings.ActivelyMatchingSomeConfirmationsFailed, handledConfirmations?.Count ?? 0, pendingMobileTradeOfferIDs.Count));
			}
		}

		Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Localization.Strings.ActivelyMatchingItemsRound, matchedSets));

		return matchedSets > 0;
	}

	private async void OnHeartBeatTimer(object? state = null) {
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

			BotCache ??= await BotCache.CreateOrLoad(BotCacheFilePath).ConfigureAwait(false);

			if (!response.StatusCode.IsSuccessCode()) {
				ShouldSendHeartBeats = false;

				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, response.StatusCode));

				switch (response.StatusCode) {
					case HttpStatusCode.Conflict:
						// ArchiNet told us to that we need to announce again
						LastAnnouncement = DateTime.MinValue;

						BotCache.LastAnnouncedAssetsForListing.Clear();
						BotCache.LastInventoryChecksumBeforeDeduplication = BotCache.LastAnnouncedTradeToken = null;
						BotCache.LastRequestAt = null;

						return;
					case HttpStatusCode.Forbidden:
						// ArchiNet told us to stop submitting data for now
						LastAnnouncement = DateTime.UtcNow.AddYears(1);

						return;
					case HttpStatusCode.TooManyRequests:
						// ArchiNet told us to try again later
						LastAnnouncement = DateTime.UtcNow.AddDays(1);

						return;
					default:
						// There is something wrong with our payload or the server, we shouldn't retry for at least several hours
						LastAnnouncement = DateTime.UtcNow.AddHours(6);

						return;
				}
			}

			BotCache.LastRequestAt = LastHeartBeat = DateTime.UtcNow;
		} finally {
			RequestsSemaphore.Release();
		}
	}
}
