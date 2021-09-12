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
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Localization;
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
using Newtonsoft.Json;

namespace ArchiSteamFarm.Core {
	internal sealed class Statistics : IAsyncDisposable {
		private const ushort MaxItemsForFairBots = ArchiWebHandler.MaxItemsInSingleInventoryRequest * WebBrowser.MaxTries; // Determines which fair bots we'll deprioritize when matching due to excessive number of inventory requests they need to make, which are likely to fail in the process or cause excessive delays
		private const byte MaxMatchedBotsHard = 40; // Determines how many bots we can attempt to match in total, where match attempt is equal to analyzing bot's inventory
		private const byte MaxMatchingRounds = 10; // Determines maximum amount of matching rounds we're going to consider before leaving the rest of work for the next batch
		private const byte MinAnnouncementCheckTTL = 6; // Minimum amount of hours we must wait before checking eligibility for Announcement, should be lower than MinPersonaStateTTL
		private const byte MinHeartBeatTTL = 10; // Minimum amount of minutes we must wait before sending next HeartBeat
		private const byte MinItemsCount = 100; // Minimum amount of items to be eligible for public listing
		private const byte MinPersonaStateTTL = 8; // Minimum amount of hours we must wait before requesting persona state update
		private const string URL = "https://" + SharedInfo.StatisticsServer;

		private static readonly ImmutableHashSet<Asset.EType> AcceptedMatchableTypes = ImmutableHashSet.Create(
			Asset.EType.Emoticon,
			Asset.EType.FoilTradingCard,
			Asset.EType.ProfileBackground,
			Asset.EType.TradingCard
		);

		private readonly Bot Bot;
		private readonly SemaphoreSlim MatchActivelySemaphore = new(1, 1);

#pragma warning disable CA2213 // False positive, .NET Framework can't understand DisposeAsync()
		private readonly Timer MatchActivelyTimer;
#pragma warning restore CA2213 // False positive, .NET Framework can't understand DisposeAsync()

		private readonly SemaphoreSlim RequestsSemaphore = new(1, 1);

		private DateTime LastAnnouncementCheck;
		private DateTime LastHeartBeat;
		private DateTime LastPersonaStateRequest;
		private bool ShouldSendHeartBeats;

		internal Statistics(Bot bot) {
			Bot = bot ?? throw new ArgumentNullException(nameof(bot));

			MatchActivelyTimer = new Timer(
				MatchActively,
				null,
				TimeSpan.FromHours(1) + TimeSpan.FromSeconds(ASF.LoadBalancingDelay * Bot.Bots?.Count ?? 0), // Delay
				TimeSpan.FromHours(8) // Period
			);
		}

		public async ValueTask DisposeAsync() {
			MatchActivelySemaphore.Dispose();
			RequestsSemaphore.Dispose();

			await MatchActivelyTimer.DisposeAsync().ConfigureAwait(false);
		}

		internal async Task OnHeartBeat() {
			// Request persona update if needed
			if ((DateTime.UtcNow > LastPersonaStateRequest.AddHours(MinPersonaStateTTL)) && (DateTime.UtcNow > LastAnnouncementCheck.AddHours(MinAnnouncementCheckTTL))) {
				LastPersonaStateRequest = DateTime.UtcNow;
				Bot.RequestPersonaStateUpdate();
			}

			if (!ShouldSendHeartBeats || (DateTime.UtcNow < LastHeartBeat.AddMinutes(MinHeartBeatTTL))) {
				return;
			}

			await RequestsSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				if (!ShouldSendHeartBeats || (DateTime.UtcNow < LastHeartBeat.AddMinutes(MinHeartBeatTTL))) {
					return;
				}

				Uri request = new(URL + "/Api/HeartBeat");

				Dictionary<string, string> data = new(2, StringComparer.Ordinal) {
					{ "Guid", (ASF.GlobalDatabase?.Identifier ?? Guid.NewGuid()).ToString("N") },
					{ "SteamID", Bot.SteamID.ToString(CultureInfo.InvariantCulture) }
				};

				BasicResponse? response = await Bot.ArchiWebHandler.WebBrowser.UrlPost(request, data: data, requestOptions: WebBrowser.ERequestOptions.ReturnClientErrors).ConfigureAwait(false);

				if (response == null) {
					return;
				}

				if (response.StatusCode.IsClientErrorCode()) {
					LastHeartBeat = DateTime.MinValue;
					ShouldSendHeartBeats = false;

					return;
				}

				LastHeartBeat = DateTime.UtcNow;
			} finally {
				RequestsSemaphore.Release();
			}
		}

		internal async Task OnLoggedOn() {
			if (!await Bot.ArchiWebHandler.JoinGroup(SharedInfo.ASFGroupSteamID).ConfigureAwait(false)) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, nameof(ArchiWebHandler.JoinGroup)));
			}
		}

		internal async Task OnPersonaState(string? nickname = null, string? avatarHash = null) {
			if ((DateTime.UtcNow < LastAnnouncementCheck.AddHours(MinAnnouncementCheckTTL)) && (ShouldSendHeartBeats || (LastHeartBeat == DateTime.MinValue))) {
				return;
			}

			await RequestsSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				if ((DateTime.UtcNow < LastAnnouncementCheck.AddHours(MinAnnouncementCheckTTL)) && (ShouldSendHeartBeats || (LastHeartBeat == DateTime.MinValue))) {
					return;
				}

				// Don't announce if we don't meet conditions
				bool? eligible = await IsEligibleForListing().ConfigureAwait(false);

				if (!eligible.HasValue) {
					// This is actually network failure, so we'll stop sending heartbeats but not record it as valid check
					ShouldSendHeartBeats = false;

					return;
				}

				if (!eligible.Value) {
					LastAnnouncementCheck = DateTime.UtcNow;
					ShouldSendHeartBeats = false;

					return;
				}

				string? tradeToken = await Bot.ArchiHandler.GetTradeToken().ConfigureAwait(false);

				if (string.IsNullOrEmpty(tradeToken)) {
					// This is actually network failure, so we'll stop sending heartbeats but not record it as valid check
					ShouldSendHeartBeats = false;

					return;
				}

				HashSet<Asset.EType> acceptedMatchableTypes = Bot.BotConfig.MatchableTypes.Where(AcceptedMatchableTypes.Contains).ToHashSet();

				if (acceptedMatchableTypes.Count == 0) {
					Bot.ArchiLogger.LogNullError(nameof(acceptedMatchableTypes));
					LastAnnouncementCheck = DateTime.UtcNow;
					ShouldSendHeartBeats = false;

					return;
				}

				HashSet<Asset> inventory;

				try {
					inventory = await Bot.ArchiWebHandler.GetInventoryAsync().Where(item => item.Tradable && acceptedMatchableTypes.Contains(item.Type)).ToHashSetAsync().ConfigureAwait(false);
				} catch (HttpRequestException e) {
					Bot.ArchiLogger.LogGenericWarningException(e);

					// This is actually inventory failure, so we'll stop sending heartbeats but not record it as valid check
					ShouldSendHeartBeats = false;

					return;
				} catch (Exception e) {
					Bot.ArchiLogger.LogGenericException(e);

					// This is actually inventory failure, so we'll stop sending heartbeats but not record it as valid check
					ShouldSendHeartBeats = false;

					return;
				}

				LastAnnouncementCheck = DateTime.UtcNow;

				// This is actual inventory
				if (inventory.Count < MinItemsCount) {
					ShouldSendHeartBeats = false;

					return;
				}

				Uri request = new(URL + "/Api/Announce");

				Dictionary<string, string> data = new(9, StringComparer.Ordinal) {
					{ "AvatarHash", avatarHash ?? "" },
					{ "GamesCount", inventory.Select(item => item.RealAppID).Distinct().Count().ToString(CultureInfo.InvariantCulture) },
					{ "Guid", (ASF.GlobalDatabase?.Identifier ?? Guid.NewGuid()).ToString("N") },
					{ "ItemsCount", inventory.Count.ToString(CultureInfo.InvariantCulture) },
					{ "MatchableTypes", JsonConvert.SerializeObject(acceptedMatchableTypes) },
					{ "MatchEverything", Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.MatchEverything) ? "1" : "0" },
					{ "Nickname", nickname ?? "" },
					{ "SteamID", Bot.SteamID.ToString(CultureInfo.InvariantCulture) },

					// ReSharper disable once RedundantSuppressNullableWarningExpression - required for .NET Framework
					{ "TradeToken", tradeToken! }
				};

				BasicResponse? response = await Bot.ArchiWebHandler.WebBrowser.UrlPost(request, data: data, requestOptions: WebBrowser.ERequestOptions.ReturnClientErrors).ConfigureAwait(false);

				if (response == null) {
					return;
				}

				if (response.StatusCode.IsClientErrorCode()) {
					LastHeartBeat = DateTime.MinValue;
					ShouldSendHeartBeats = false;

					return;
				}

				LastHeartBeat = DateTime.UtcNow;
				ShouldSendHeartBeats = true;
			} finally {
				RequestsSemaphore.Release();
			}
		}

		private async Task<ImmutableHashSet<ListedUser>?> GetListedUsers() {
			Uri request = new(URL + "/Api/Bots");

			ObjectResponse<ImmutableHashSet<ListedUser>>? response = await Bot.ArchiWebHandler.WebBrowser.UrlGetToJsonObject<ImmutableHashSet<ListedUser>>(request).ConfigureAwait(false);

			return response?.Content;
		}

		private async Task<bool?> IsEligibleForListing() {
			bool? isEligibleForMatching = await IsEligibleForMatching().ConfigureAwait(false);

			if (isEligibleForMatching != true) {
				return isEligibleForMatching;
			}

			// Bot must have public inventory
			bool? hasPublicInventory = await Bot.HasPublicInventory().ConfigureAwait(false);

			if (hasPublicInventory != true) {
				Bot.ArchiLogger.LogGenericTrace(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, nameof(Bot.HasPublicInventory) + ": " + (hasPublicInventory?.ToString() ?? "null")));

				return hasPublicInventory;
			}

			return true;
		}

		private async Task<bool?> IsEligibleForMatching() {
			// Bot must have ASF 2FA
			if (!Bot.HasMobileAuthenticator) {
				Bot.ArchiLogger.LogGenericTrace(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, nameof(Bot.HasMobileAuthenticator) + ": " + Bot.HasMobileAuthenticator));

				return false;
			}

			// Bot must have STM enable in TradingPreferences
			if (!Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.SteamTradeMatcher)) {
				Bot.ArchiLogger.LogGenericTrace(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, nameof(Bot.BotConfig.TradingPreferences) + ": " + Bot.BotConfig.TradingPreferences));

				return false;
			}

			// Bot must have at least one accepted matchable type set
			if ((Bot.BotConfig.MatchableTypes.Count == 0) || Bot.BotConfig.MatchableTypes.All(type => !AcceptedMatchableTypes.Contains(type))) {
				Bot.ArchiLogger.LogGenericTrace(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, nameof(Bot.BotConfig.MatchableTypes) + ": " + string.Join(", ", Bot.BotConfig.MatchableTypes)));

				return false;
			}

			// Bot must have valid API key (e.g. not being restricted account)
			bool? hasValidApiKey = await Bot.ArchiWebHandler.HasValidApiKey().ConfigureAwait(false);

			if (hasValidApiKey != true) {
				Bot.ArchiLogger.LogGenericTrace(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, nameof(Bot.ArchiWebHandler.HasValidApiKey) + ": " + (hasValidApiKey?.ToString() ?? "null")));

				return hasValidApiKey;
			}

			return true;
		}

		private async void MatchActively(object? state = null) {
			if (!Bot.IsConnectedAndLoggedOn || Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.MatchEverything) || !Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.MatchActively) || (await IsEligibleForMatching().ConfigureAwait(false) != true)) {
				Bot.ArchiLogger.LogGenericTrace(Strings.ErrorAborted);

				return;
			}

			HashSet<Asset.EType> acceptedMatchableTypes = Bot.BotConfig.MatchableTypes.Where(AcceptedMatchableTypes.Contains).ToHashSet();

			if (acceptedMatchableTypes.Count == 0) {
				Bot.ArchiLogger.LogGenericTrace(Strings.ErrorAborted);

				return;
			}

			if (!await MatchActivelySemaphore.WaitAsync(0).ConfigureAwait(false)) {
				Bot.ArchiLogger.LogGenericTrace(Strings.ErrorAborted);

				return;
			}

			try {
				Bot.ArchiLogger.LogGenericTrace(Strings.Starting);

				Dictionary<ulong, (byte Tries, ISet<ulong>? GivenAssetIDs, ISet<ulong>? ReceivedAssetIDs)> triedSteamIDs = new();

				bool shouldContinueMatching = true;
				bool tradedSomething = false;

				for (byte i = 0; (i < MaxMatchingRounds) && shouldContinueMatching; i++) {
					if ((i > 0) && tradedSomething) {
						// After each round we wait at least 5 minutes for all bots to react
						await Task.Delay(5 * 60 * 1000).ConfigureAwait(false);
					}

					if (!Bot.IsConnectedAndLoggedOn || Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.MatchEverything) || !Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.MatchActively) || (await IsEligibleForMatching().ConfigureAwait(false) != true)) {
						Bot.ArchiLogger.LogGenericTrace(Strings.ErrorAborted);

						break;
					}

#pragma warning disable CA2000 // False positive, we're actually wrapping it in the using clause below exactly for that purpose
					using (await Bot.Actions.GetTradingLock().ConfigureAwait(false)) {
#pragma warning restore CA2000 // False positive, we're actually wrapping it in the using clause below exactly for that purpose
						Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.ActivelyMatchingItems, i));
						(shouldContinueMatching, tradedSomething) = await MatchActivelyRound(acceptedMatchableTypes, triedSteamIDs).ConfigureAwait(false);
						Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.DoneActivelyMatchingItems, i));
					}
				}

				Bot.ArchiLogger.LogGenericTrace(Strings.Done);
			} finally {
				MatchActivelySemaphore.Release();
			}
		}

		private async Task<(bool ShouldContinueMatching, bool TradedSomething)> MatchActivelyRound(IReadOnlyCollection<Asset.EType> acceptedMatchableTypes, IDictionary<ulong, (byte Tries, ISet<ulong>? GivenAssetIDs, ISet<ulong>? ReceivedAssetIDs)> triedSteamIDs) {
			if ((acceptedMatchableTypes == null) || (acceptedMatchableTypes.Count == 0)) {
				throw new ArgumentNullException(nameof(acceptedMatchableTypes));
			}

			if (triedSteamIDs == null) {
				throw new ArgumentNullException(nameof(triedSteamIDs));
			}

			HashSet<Asset> ourInventory;

			try {
				ourInventory = await Bot.ArchiWebHandler.GetInventoryAsync().Where(item => acceptedMatchableTypes.Contains(item.Type) && !Bot.BotDatabase.MatchActivelyBlacklistedAppIDs.Contains(item.RealAppID)).ToHashSetAsync().ConfigureAwait(false);
			} catch (HttpRequestException e) {
				Bot.ArchiLogger.LogGenericWarningException(e);

				return (false, false);
			} catch (Exception e) {
				Bot.ArchiLogger.LogGenericException(e);

				return (false, false);
			}

			if (ourInventory.Count == 0) {
				Bot.ArchiLogger.LogGenericTrace(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(ourInventory)));

				return (false, false);
			}

			(Dictionary<(uint RealAppID, Asset.EType Type, Asset.ERarity Rarity), Dictionary<ulong, uint>> ourFullState, Dictionary<(uint RealAppID, Asset.EType Type, Asset.ERarity Rarity), Dictionary<ulong, uint>> ourTradableState) = Trading.GetDividedInventoryState(ourInventory);

			if (Trading.IsEmptyForMatching(ourFullState, ourTradableState)) {
				// User doesn't have any more dupes in the inventory
				Bot.ArchiLogger.LogGenericTrace(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(ourFullState) + " || " + nameof(ourTradableState)));

				return (false, false);
			}

			ImmutableHashSet<ListedUser>? listedUsers = await GetListedUsers().ConfigureAwait(false);

			if ((listedUsers == null) || (listedUsers.Count == 0)) {
				Bot.ArchiLogger.LogGenericTrace(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(listedUsers)));

				return (false, false);
			}

			byte maxTradeHoldDuration = ASF.GlobalConfig?.MaxTradeHoldDuration ?? GlobalConfig.DefaultMaxTradeHoldDuration;
			byte totalMatches = 0;

			HashSet<(uint RealAppID, Asset.EType Type, Asset.ERarity Rarity)> skippedSetsThisRound = new();

			foreach (ListedUser listedUser in listedUsers.Where(listedUser => (listedUser.SteamID != Bot.SteamID) && acceptedMatchableTypes.Any(listedUser.MatchableTypes.Contains) && (!triedSteamIDs.TryGetValue(listedUser.SteamID, out (byte Tries, ISet<ulong>? GivenAssetIDs, ISet<ulong>? ReceivedAssetIDs) attempt) || (attempt.Tries < byte.MaxValue)) && !Bot.IsBlacklistedFromTrades(listedUser.SteamID)).OrderBy(listedUser => triedSteamIDs.TryGetValue(listedUser.SteamID, out (byte Tries, ISet<ulong>? GivenAssetIDs, ISet<ulong>? ReceivedAssetIDs) attempt) ? attempt.Tries : 0).ThenByDescending(listedUser => listedUser.MatchEverything).ThenByDescending(listedUser => listedUser.MatchEverything || (listedUser.ItemsCount < MaxItemsForFairBots)).ThenByDescending(listedUser => listedUser.Score)) {
				HashSet<(uint RealAppID, Asset.EType Type, Asset.ERarity Rarity)> wantedSets = ourTradableState.Keys.Where(set => !skippedSetsThisRound.Contains(set) && listedUser.MatchableTypes.Contains(set.Type)).ToHashSet();

				if (wantedSets.Count == 0) {
					continue;
				}

				if (++totalMatches > MaxMatchedBotsHard) {
					break;
				}

				Bot.ArchiLogger.LogGenericTrace(listedUser.SteamID + "...");

				byte? holdDuration = await Bot.ArchiWebHandler.GetTradeHoldDurationForUser(listedUser.SteamID, listedUser.TradeToken).ConfigureAwait(false);

				switch (holdDuration) {
					case null:
						Bot.ArchiLogger.LogGenericTrace(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(holdDuration)));

						continue;
					case > 0 when holdDuration.Value > maxTradeHoldDuration:
						Bot.ArchiLogger.LogGenericTrace(holdDuration.Value + " > " + maxTradeHoldDuration);

						continue;
				}

				HashSet<Asset> theirInventory;

				try {
					theirInventory = await Bot.ArchiWebHandler.GetInventoryAsync(listedUser.SteamID).Where(item => (!listedUser.MatchEverything || item.Tradable) && wantedSets.Contains((item.RealAppID, item.Type, item.Rarity)) && ((holdDuration.Value == 0) || !(item.Type is Asset.EType.FoilTradingCard or Asset.EType.TradingCard && CardsFarmer.SalesBlacklist.Contains(item.RealAppID)))).ToHashSetAsync().ConfigureAwait(false);
				} catch (HttpRequestException e) {
					Bot.ArchiLogger.LogGenericWarningException(e);

					continue;
				} catch (Exception e) {
					Bot.ArchiLogger.LogGenericException(e);

					continue;
				}

				if (theirInventory.Count == 0) {
					Bot.ArchiLogger.LogGenericTrace(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(theirInventory)));

					continue;
				}

				HashSet<(uint RealAppID, Asset.EType Type, Asset.ERarity Rarity)> skippedSetsThisUser = new();

				Dictionary<(uint RealAppID, Asset.EType Type, Asset.ERarity Rarity), Dictionary<ulong, uint>> theirTradableState = Trading.GetTradableInventoryState(theirInventory);
				Dictionary<(uint RealAppID, Asset.EType Type, Asset.ERarity Rarity), Dictionary<ulong, uint>> inventoryStateChanges = new();

				for (byte i = 0; i < Trading.MaxTradesPerAccount; i++) {
					byte itemsInTrade = 0;
					HashSet<(uint RealAppID, Asset.EType Type, Asset.ERarity Rarity)> skippedSetsThisTrade = new();

					Dictionary<ulong, uint> classIDsToGive = new();
					Dictionary<ulong, uint> classIDsToReceive = new();
					Dictionary<ulong, uint> fairClassIDsToGive = new();
					Dictionary<ulong, uint> fairClassIDsToReceive = new();

					foreach (((uint RealAppID, Asset.EType Type, Asset.ERarity Rarity) set, Dictionary<ulong, uint> ourFullItems) in ourFullState.Where(set => !skippedSetsThisUser.Contains(set.Key) && listedUser.MatchableTypes.Contains(set.Key.Type) && set.Value.Values.Any(count => count > 1))) {
						if (!ourTradableState.TryGetValue(set, out Dictionary<ulong, uint>? ourTradableItems) || (ourTradableItems.Count == 0)) {
							continue;
						}

						if (!theirTradableState.TryGetValue(set, out Dictionary<ulong, uint>? theirTradableItems) || (theirTradableItems.Count == 0)) {
							continue;
						}

						// Those 2 collections are on user-basis since we can't be sure that the trade passes through (and therefore we need to keep original state in case of failure)
						Dictionary<ulong, uint> ourFullSet = new(ourFullItems);
						Dictionary<ulong, uint> ourTradableSet = new(ourTradableItems);

						// We also have to take into account changes that happened in previous trades with this user, so this block will adapt to that
						if (inventoryStateChanges.TryGetValue(set, out Dictionary<ulong, uint>? pastChanges) && (pastChanges.Count > 0)) {
							foreach ((ulong classID, uint amount) in pastChanges) {
								if (!ourFullSet.TryGetValue(classID, out uint fullAmount) || (fullAmount == 0) || (fullAmount < amount)) {
									Bot.ArchiLogger.LogNullError(nameof(fullAmount));

									return (false, skippedSetsThisRound.Count > 0);
								}

								if (fullAmount > amount) {
									ourFullSet[classID] = fullAmount - amount;
								} else {
									ourFullSet.Remove(classID);
								}

								if (!ourTradableSet.TryGetValue(classID, out uint tradableAmount) || (tradableAmount == 0) || (tradableAmount < amount)) {
									Bot.ArchiLogger.LogNullError(nameof(tradableAmount));

									return (false, skippedSetsThisRound.Count > 0);
								}

								if (fullAmount > amount) {
									ourTradableSet[classID] = fullAmount - amount;
								} else {
									ourTradableSet.Remove(classID);
								}
							}

							if (Trading.IsEmptyForMatching(ourFullSet, ourTradableSet)) {
								continue;
							}
						}

						bool match;

						do {
							match = false;

							foreach ((ulong ourItem, uint ourFullAmount) in ourFullSet.Where(item => item.Value > 1).OrderByDescending(item => item.Value)) {
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
										HashSet<Asset> fairFiltered = theirInventory.Where(item => ((item.RealAppID == set.RealAppID) && (item.Type == set.Type) && (item.Rarity == set.Rarity)) || skippedSetsThisTrade.Contains((item.RealAppID, item.Type, item.Rarity))).Select(item => item.CreateShallowCopy()).ToHashSet();

										// Copy list to HashSet<Steam.Asset>
										HashSet<Asset> fairItemsToGive = Trading.GetTradableItemsFromInventory(ourInventory.Where(item => ((item.RealAppID == set.RealAppID) && (item.Type == set.Type) && (item.Rarity == set.Rarity)) || skippedSetsThisTrade.Contains((item.RealAppID, item.Type, item.Rarity))).Select(item => item.CreateShallowCopy()).ToHashSet(), fairClassIDsToGive.ToDictionary(classID => classID.Key, classID => classID.Value));
										HashSet<Asset> fairItemsToReceive = Trading.GetTradableItemsFromInventory(fairFiltered.Select(item => item.CreateShallowCopy()).ToHashSet(), fairClassIDsToReceive.ToDictionary(classID => classID.Key, classID => classID.Value));

										// Actual check:
										if (!Trading.IsTradeNeutralOrBetter(fairFiltered, fairItemsToReceive, fairItemsToGive)) {
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

									if (inventoryStateChanges.TryGetValue(set, out Dictionary<ulong, uint>? currentChanges)) {
										currentChanges[ourItem] = currentChanges.TryGetValue(ourItem, out uint amount) ? amount + 1 : 1;
									} else {
										inventoryStateChanges[set] = new Dictionary<ulong, uint> {
											{ ourItem, 1 }
										};
									}

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
					HashSet<Asset> itemsToGive = Trading.GetTradableItemsFromInventory(ourInventory, classIDsToGive);
					HashSet<Asset> itemsToReceive = Trading.GetTradableItemsFromInventory(theirInventory, classIDsToReceive);

					if ((itemsToGive.Count != itemsToReceive.Count) || !Trading.IsFairExchange(itemsToGive, itemsToReceive)) {
						// Failsafe
						Bot.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, Strings.ErrorAborted));

						return (false, skippedSetsThisRound.Count > 0);
					}

					if (triedSteamIDs.TryGetValue(listedUser.SteamID, out (byte Tries, ISet<ulong>? GivenAssetIDs, ISet<ulong>? ReceivedAssetIDs) previousAttempt)) {
						if ((previousAttempt.GivenAssetIDs == null) || (previousAttempt.ReceivedAssetIDs == null) || (itemsToGive.Select(item => item.AssetID).All(previousAttempt.GivenAssetIDs.Contains) && itemsToReceive.Select(item => item.AssetID).All(previousAttempt.ReceivedAssetIDs.Contains))) {
							// This user didn't respond in our previous round, avoid him for remaining ones
							triedSteamIDs[listedUser.SteamID] = (byte.MaxValue, previousAttempt.GivenAssetIDs, previousAttempt.ReceivedAssetIDs);

							break;
						}

						previousAttempt.GivenAssetIDs.UnionWith(itemsToGive.Select(item => item.AssetID));
						previousAttempt.ReceivedAssetIDs.UnionWith(itemsToReceive.Select(item => item.AssetID));
					} else {
						previousAttempt.GivenAssetIDs = new HashSet<ulong>(itemsToGive.Select(item => item.AssetID));
						previousAttempt.ReceivedAssetIDs = new HashSet<ulong>(itemsToReceive.Select(item => item.AssetID));
					}

					triedSteamIDs[listedUser.SteamID] = (++previousAttempt.Tries, previousAttempt.GivenAssetIDs, previousAttempt.ReceivedAssetIDs);

					Bot.ArchiLogger.LogGenericTrace(Bot.SteamID + " <- " + string.Join(", ", itemsToReceive.Select(item => item.RealAppID + "/" + item.Type + "-" + item.ClassID + " #" + item.Amount)) + " | " + string.Join(", ", itemsToGive.Select(item => item.RealAppID + "/" + item.Type + "-" + item.ClassID + " #" + item.Amount)) + " -> " + listedUser.SteamID);

					(bool success, HashSet<ulong>? mobileTradeOfferIDs) = await Bot.ArchiWebHandler.SendTradeOffer(listedUser.SteamID, itemsToGive, itemsToReceive, listedUser.TradeToken, true).ConfigureAwait(false);

					if ((mobileTradeOfferIDs?.Count > 0) && Bot.HasMobileAuthenticator) {
						(bool twoFactorSuccess, _, _) = await Bot.Actions.HandleTwoFactorAuthenticationConfirmations(true, Confirmation.EType.Trade, mobileTradeOfferIDs, true).ConfigureAwait(false);

						if (!twoFactorSuccess) {
							Bot.ArchiLogger.LogGenericTrace(Strings.WarningFailed);

							return (false, skippedSetsThisRound.Count > 0);
						}
					}

					if (!success) {
						Bot.ArchiLogger.LogGenericTrace(Strings.WarningFailed);

						break;
					}

					// Add itemsToGive to theirInventory to reflect their current state if we're over MaxItemsPerTrade
					theirInventory.UnionWith(itemsToGive);

					skippedSetsThisUser.UnionWith(skippedSetsThisTrade);
					Bot.ArchiLogger.LogGenericTrace(Strings.Success);
				}

				if (skippedSetsThisUser.Count == 0) {
					if (skippedSetsThisRound.Count == 0) {
						// If we didn't find any match on clean round, this user isn't going to have anything interesting for us anytime soon
						triedSteamIDs[listedUser.SteamID] = (byte.MaxValue, null, null);
					}

					continue;
				}

				skippedSetsThisRound.UnionWith(skippedSetsThisUser);

				foreach ((uint RealAppID, Asset.EType Type, Asset.ERarity Rarity) skippedSet in skippedSetsThisUser) {
					ourFullState.Remove(skippedSet);
					ourTradableState.Remove(skippedSet);
				}

				if (Trading.IsEmptyForMatching(ourFullState, ourTradableState)) {
					// User doesn't have any more dupes in the inventory
					break;
				}

				ourFullState.TrimExcess();
				ourTradableState.TrimExcess();
			}

			Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.ActivelyMatchingItemsRound, skippedSetsThisRound.Count));

			// Keep matching when we either traded something this round (so it makes sense for a refresh) or if we didn't try all available bots yet (so it makes sense to keep going)
			return ((totalMatches > 0) && ((skippedSetsThisRound.Count > 0) || triedSteamIDs.Values.All(data => data.Tries < 2)), skippedSetsThisRound.Count > 0);
		}

		[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
		private sealed class ListedUser {
#pragma warning disable CS0649 // False positive, it's a field set during json deserialization
			[JsonProperty(PropertyName = "items_count", Required = Required.Always)]
			internal readonly ushort ItemsCount;
#pragma warning restore CS0649 // False positive, it's a field set during json deserialization

			internal readonly HashSet<Asset.EType> MatchableTypes = new();

#pragma warning disable CS0649 // False positive, it's a field set during json deserialization
			[JsonProperty(PropertyName = "steam_id", Required = Required.Always)]
			internal readonly ulong SteamID;
#pragma warning restore CS0649 // False positive, it's a field set during json deserialization

			[JsonProperty(PropertyName = "trade_token", Required = Required.Always)]
			internal readonly string TradeToken = "";

			internal float Score => GamesCount / (float) ItemsCount;

#pragma warning disable CS0649 // False positive, it's a field set during json deserialization
			[JsonProperty(PropertyName = "games_count", Required = Required.Always)]
			private readonly ushort GamesCount;
#pragma warning restore CS0649 // False positive, it's a field set during json deserialization

			internal bool MatchEverything { get; private set; }

			[JsonProperty(PropertyName = "matchable_backgrounds", Required = Required.Always)]
			private byte MatchableBackgroundsNumber {
				set {
					switch (value) {
						case 0:
							MatchableTypes.Remove(Asset.EType.ProfileBackground);

							break;
						case 1:
							MatchableTypes.Add(Asset.EType.ProfileBackground);

							break;
						default:
							ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(value), value));

							return;
					}
				}
			}

			[JsonProperty(PropertyName = "matchable_cards", Required = Required.Always)]
			private byte MatchableCardsNumber {
				set {
					switch (value) {
						case 0:
							MatchableTypes.Remove(Asset.EType.TradingCard);

							break;
						case 1:
							MatchableTypes.Add(Asset.EType.TradingCard);

							break;
						default:
							ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(value), value));

							return;
					}
				}
			}

			[JsonProperty(PropertyName = "matchable_emoticons", Required = Required.Always)]
			private byte MatchableEmoticonsNumber {
				set {
					switch (value) {
						case 0:
							MatchableTypes.Remove(Asset.EType.Emoticon);

							break;
						case 1:
							MatchableTypes.Add(Asset.EType.Emoticon);

							break;
						default:
							ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(value), value));

							return;
					}
				}
			}

			[JsonProperty(PropertyName = "matchable_foil_cards", Required = Required.Always)]
			private byte MatchableFoilCardsNumber {
				set {
					switch (value) {
						case 0:
							MatchableTypes.Remove(Asset.EType.FoilTradingCard);

							break;
						case 1:
							MatchableTypes.Add(Asset.EType.FoilTradingCard);

							break;
						default:
							ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(value), value));

							return;
					}
				}
			}

			[JsonProperty(PropertyName = "match_everything", Required = Required.Always)]
			private byte MatchEverythingNumber {
				set {
					switch (value) {
						case 0:
							MatchEverything = false;

							break;
						case 1:
							MatchEverything = true;

							break;
						default:
							ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(value), value));

							return;
					}
				}
			}

			[JsonConstructor]
			private ListedUser() { }
		}
	}
}
