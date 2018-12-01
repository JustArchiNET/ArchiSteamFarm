//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// 
// Copyright 2015-2018 Łukasz "JustArchi" Domeradzki
// Contact: JustArchi@JustArchi.net
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
// http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Json;
using ArchiSteamFarm.Localization;
using Newtonsoft.Json;

namespace ArchiSteamFarm {
	internal sealed class Statistics : IDisposable {
		private const byte MaxMatchedBotsHard = 40;
		private const byte MaxMatchesBotsSoft = 20;
		private const byte MaxMatchingRounds = 10;
		private const byte MinAnnouncementCheckTTL = 6; // Minimum amount of hours we must wait before checking eligibility for Announcement, should be lower than MinPersonaStateTTL
		private const byte MinHeartBeatTTL = 10; // Minimum amount of minutes we must wait before sending next HeartBeat
		private const byte MinItemsCount = 100; // Minimum amount of items to be eligible for public listing
		private const byte MinPersonaStateTTL = 8; // Minimum amount of hours we must wait before requesting persona state update
		private const string URL = "https://" + SharedInfo.StatisticsServer;

		private static readonly HashSet<Steam.Asset.EType> AcceptedMatchableTypes = new HashSet<Steam.Asset.EType> {
			Steam.Asset.EType.Emoticon,
			Steam.Asset.EType.FoilTradingCard,
			Steam.Asset.EType.ProfileBackground,
			Steam.Asset.EType.TradingCard
		};

		private readonly Bot Bot;
		private readonly SemaphoreSlim MatchActivelySemaphore = new SemaphoreSlim(1, 1);
		private readonly Timer MatchActivelyTimer;
		private readonly SemaphoreSlim RequestsSemaphore = new SemaphoreSlim(1, 1);

		private DateTime LastAnnouncementCheck;
		private DateTime LastHeartBeat;
		private DateTime LastPersonaStateRequest;
		private bool ShouldSendHeartBeats;

		internal Statistics(Bot bot) {
			Bot = bot ?? throw new ArgumentNullException(nameof(bot));

			// TODO: This should start from 1 hour, not 1 minute
			MatchActivelyTimer = new Timer(
				async e => await MatchActively().ConfigureAwait(false),
				null,
				TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(Program.GlobalConfig.LoginLimiterDelay + Program.LoadBalancingDelay * Bot.Bots.Count), // Delay
				TimeSpan.FromDays(1) // Period
			);
		}

		public void Dispose() {
			MatchActivelySemaphore.Dispose();
			MatchActivelyTimer.Dispose();
			RequestsSemaphore.Dispose();
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

				const string request = URL + "/Api/HeartBeat";
				Dictionary<string, string> data = new Dictionary<string, string>(2) {
					{ "SteamID", Bot.SteamID.ToString() },
					{ "Guid", Program.GlobalDatabase.Guid.ToString("N") }
				};

				if (await Program.WebBrowser.UrlPost(request, data).ConfigureAwait(false) != null) {
					LastHeartBeat = DateTime.UtcNow;
				}
			} finally {
				RequestsSemaphore.Release();
			}
		}

		internal async Task OnLoggedOn() => await Bot.ArchiWebHandler.JoinGroup(SharedInfo.ASFGroupSteamID).ConfigureAwait(false);

		internal async Task OnPersonaState(string nickname = null, string avatarHash = null) {
			if (DateTime.UtcNow < LastAnnouncementCheck.AddHours(MinAnnouncementCheckTTL)) {
				return;
			}

			await RequestsSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				if (DateTime.UtcNow < LastAnnouncementCheck.AddHours(MinAnnouncementCheckTTL)) {
					return;
				}

				// Don't announce if we don't meet conditions
				string tradeToken;
				if (!await IsEligibleForMatching().ConfigureAwait(false) || string.IsNullOrEmpty(tradeToken = await Bot.ArchiHandler.GetTradeToken().ConfigureAwait(false))) {
					LastAnnouncementCheck = DateTime.UtcNow;
					ShouldSendHeartBeats = false;
					return;
				}

				HashSet<Steam.Asset.EType> acceptedMatchableTypes = Bot.BotConfig.MatchableTypes.Where(type => AcceptedMatchableTypes.Contains(type)).ToHashSet();
				if (acceptedMatchableTypes.Count == 0) {
					Bot.ArchiLogger.LogNullError(nameof(acceptedMatchableTypes));
					LastAnnouncementCheck = DateTime.UtcNow;
					ShouldSendHeartBeats = false;
					return;
				}

				HashSet<Steam.Asset> inventory = await Bot.ArchiWebHandler.GetInventory(Bot.SteamID, tradable: true, wantedTypes: acceptedMatchableTypes).ConfigureAwait(false);

				// This is actually inventory failure, so we'll stop sending heartbeats but not record it as valid check
				if (inventory == null) {
					ShouldSendHeartBeats = false;
					return;
				}

				// This is actual inventory
				if (inventory.Count < MinItemsCount) {
					LastAnnouncementCheck = DateTime.UtcNow;
					ShouldSendHeartBeats = false;
					return;
				}

				const string request = URL + "/Api/Announce";
				Dictionary<string, string> data = new Dictionary<string, string>(9) {
					{ "SteamID", Bot.SteamID.ToString() },
					{ "Guid", Program.GlobalDatabase.Guid.ToString("N") },
					{ "Nickname", nickname ?? "" },
					{ "AvatarHash", avatarHash ?? "" },
					{ "GamesCount", inventory.Select(item => item.RealAppID).Distinct().Count().ToString() },
					{ "ItemsCount", inventory.Count.ToString() },
					{ "MatchableTypes", JsonConvert.SerializeObject(acceptedMatchableTypes) },
					{ "MatchEverything", Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.MatchEverything) ? "1" : "0" },
					{ "TradeToken", tradeToken }
				};

				// Listing is free to deny our announce request, hence we don't retry
				if (await Program.WebBrowser.UrlPost(request, data, maxTries: 1).ConfigureAwait(false) != null) {
					LastAnnouncementCheck = DateTime.UtcNow;
					ShouldSendHeartBeats = true;
				}
			} finally {
				RequestsSemaphore.Release();
			}
		}

		private static async Task<HashSet<ListedUser>> GetListedUsers() {
			const string request = URL + "/Api/Bots";

			WebBrowser.ObjectResponse<HashSet<ListedUser>> objectResponse = await Program.WebBrowser.UrlGetToJsonObject<HashSet<ListedUser>>(request).ConfigureAwait(false);
			return objectResponse?.Content;
		}

		private async Task<bool> IsEligibleForMatching() {
			// Bot must have ASF 2FA
			if (!Bot.HasMobileAuthenticator) {
				return false;
			}

			// Bot must have STM enable in TradingPreferences
			if (!Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.SteamTradeMatcher)) {
				return false;
			}

			// Bot must have at least one accepted matchable type set
			if ((Bot.BotConfig.MatchableTypes.Count == 0) || Bot.BotConfig.MatchableTypes.All(type => !AcceptedMatchableTypes.Contains(type))) {
				return false;
			}

			// Bot must have public inventory
			if (!await Bot.ArchiWebHandler.HasPublicInventory().ConfigureAwait(false)) {
				return false;
			}

			// Bot must have valid API key (e.g. not being restricted account)
			return await Bot.ArchiWebHandler.HasValidApiKey().ConfigureAwait(false);
		}

		private async Task MatchActively() {
			// TODO: This function has a lot of debug leftovers for logic testing, once that period is over, get rid of them

			if (!await MatchActivelySemaphore.WaitAsync(0).ConfigureAwait(false)) {
				return;
			}

			try {
				bool match = true;

				Bot.ArchiLogger.LogGenericDebug("Matching started!");

				for (byte i = 0; (i < MaxMatchingRounds) && match; i++) {
					if (i > 0) {
						// After each round we wait at least 5 minutes for all bots to react
						Bot.ArchiLogger.LogGenericDebug("Cooldown...");
						await Task.Delay(5 * 60 * 1000).ConfigureAwait(false);
					}

					Bot.ArchiLogger.LogGenericDebug("Now matching, round #" + i);
					match = await MatchActivelyRound().ConfigureAwait(false);
					Bot.ArchiLogger.LogGenericDebug("Matching ended, round #" + i);
				}

				Bot.ArchiLogger.LogGenericDebug("Matching finished!");
			} finally {
				MatchActivelySemaphore.Release();
			}
		}

		private async Task<bool> MatchActivelyRound() {
			// TODO: This function has a lot of debug leftovers for logic testing, once that period is over, get rid of them
			if (!Bot.IsConnectedAndLoggedOn || Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.MatchEverything) || !Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.MatchActively) || !await IsEligibleForMatching().ConfigureAwait(false)) {
				Bot.ArchiLogger.LogGenericDebug("User not eligible for this function, returning");
				return false;
			}

			HashSet<Steam.Asset.EType> acceptedMatchableTypes = Bot.BotConfig.MatchableTypes.Where(type => AcceptedMatchableTypes.Contains(type)).ToHashSet();
			if (acceptedMatchableTypes.Count == 0) {
				Bot.ArchiLogger.LogGenericDebug("No acceptable matchable types, returning");
				return false;
			}

			HashSet<Steam.Asset> ourInventory = await Bot.ArchiWebHandler.GetInventory(Bot.SteamID, tradable: true, wantedTypes: acceptedMatchableTypes).ConfigureAwait(false);
			if ((ourInventory == null) || (ourInventory.Count == 0)) {
				Bot.ArchiLogger.LogGenericDebug("Empty inventory, returning");
				return false;
			}

			Dictionary<(uint AppID, Steam.Asset.EType Type), Dictionary<ulong, uint>> ourInventoryState = Trading.GetInventoryState(ourInventory);

			if (ourInventoryState.Values.All(set => set.Values.All(amount => amount <= 1))) {
				// User doesn't have any more dupes in the inventory
				Bot.ArchiLogger.LogGenericDebug("No dupes in inventory, returning");
				return false;
			}

			HashSet<ListedUser> listedUsers = await GetListedUsers().ConfigureAwait(false);
			if ((listedUsers == null) || (listedUsers.Count == 0)) {
				Bot.ArchiLogger.LogGenericDebug("No listed users, returning");
				return false;
			}

			byte emptyMatches = 0;
			HashSet<(uint AppID, Steam.Asset.EType Type)> skippedSets = new HashSet<(uint AppID, Steam.Asset.EType Type)>();

			foreach (ListedUser listedUser in listedUsers.Where(listedUser => listedUser.MatchEverything && !Bot.IsBlacklistedFromTrades(listedUser.SteamID)).OrderByDescending(listedUser => listedUser.Score).Take(MaxMatchedBotsHard)) {
				Bot.ArchiLogger.LogGenericDebug("Now matching " + listedUser.SteamID + "...");

				HashSet<Steam.Asset> theirInventory = await Bot.ArchiWebHandler.GetInventory(listedUser.SteamID, tradable: true, wantedTypes: acceptedMatchableTypes, skippedSets: skippedSets).ConfigureAwait(false);
				if ((theirInventory == null) || (theirInventory.Count == 0)) {
					Bot.ArchiLogger.LogGenericDebug("Inventory of " + listedUser.SteamID + " is empty, continuing...");
					continue;
				}

				Dictionary<(uint AppID, Steam.Asset.EType Type), Dictionary<ulong, uint>> theirInventoryState = Trading.GetInventoryState(theirInventory);

				Dictionary<ulong, uint> classIDsToGive = new Dictionary<ulong, uint>();
				Dictionary<ulong, uint> classIDsToReceive = new Dictionary<ulong, uint>();
				HashSet<(uint AppID, Steam.Asset.EType Type)> skippedSetsThisTrade = new HashSet<(uint AppID, Steam.Asset.EType Type)>();

				foreach (KeyValuePair<(uint AppID, Steam.Asset.EType Type), Dictionary<ulong, uint>> ourInventoryStateSet in ourInventoryState.Where(set => listedUser.MatchableTypes.Contains(set.Key.Type) && set.Value.Values.Any(count => count > 1))) {
					if (!theirInventoryState.TryGetValue(ourInventoryStateSet.Key, out Dictionary<ulong, uint> theirItems)) {
						continue;
					}

					bool match;

					do {
						match = false;

						foreach (KeyValuePair<ulong, uint> ourItem in ourInventoryStateSet.Value.Where(item => item.Value > 1).OrderByDescending(item => item.Value)) {
							foreach (KeyValuePair<ulong, uint> theirItem in theirItems.OrderBy(item => ourInventoryStateSet.Value.TryGetValue(item.Key, out uint ourAmount) ? ourAmount : 0)) {
								if (ourInventoryStateSet.Value.TryGetValue(theirItem.Key, out uint ourAmountOfTheirItem) && (ourItem.Value <= ourAmountOfTheirItem + 1)) {
									continue;
								}

								Bot.ArchiLogger.LogGenericDebug("Found a match: our " + ourItem.Key + " for theirs " + theirItem.Key);

								// Skip this set from the remaining of this round
								skippedSetsThisTrade.Add(ourInventoryStateSet.Key);

								// Update our state based on given items
								classIDsToGive[ourItem.Key] = classIDsToGive.TryGetValue(ourItem.Key, out uint givenAmount) ? givenAmount + 1 : 1;
								ourInventoryStateSet.Value[ourItem.Key] = ourItem.Value - 1;

								// Update our state based on received items
								classIDsToReceive[theirItem.Key] = classIDsToReceive.TryGetValue(theirItem.Key, out uint receivedAmount) ? receivedAmount + 1 : 1;
								ourInventoryStateSet.Value[theirItem.Key] = ourAmountOfTheirItem + 1;

								// Update their state based on taken items
								if (theirItems.TryGetValue(theirItem.Key, out uint theirAmount) && (theirAmount > 1)) {
									theirItems[theirItem.Key] = theirAmount - 1;
								} else {
									theirItems.Remove(theirItem.Key);
								}

								match = true;
								break;
							}

							if (match) {
								break;
							}
						}
					} while (match);
				}

				if ((classIDsToGive.Count == 0) && (classIDsToReceive.Count == 0)) {
					Bot.ArchiLogger.LogGenericDebug("No matches found, continuing...");

					if (++emptyMatches >= MaxMatchesBotsSoft) {
						break;
					}

					continue;
				}

				emptyMatches = 0;

				HashSet<Steam.Asset> itemsToGive = Trading.GetItemsFromInventory(ourInventory, classIDsToGive);
				HashSet<Steam.Asset> itemsToReceive = Trading.GetItemsFromInventory(theirInventory, classIDsToReceive);

				// TODO: Debug only offer, should be removed after tests
				Steam.TradeOffer debugOffer = new Steam.TradeOffer(1, 46697991, Steam.TradeOffer.ETradeOfferState.Active);

				foreach (Steam.Asset itemToGive in itemsToGive) {
					debugOffer.ItemsToGive.Add(itemToGive);
				}

				foreach (Steam.Asset itemToReceive in itemsToReceive) {
					debugOffer.ItemsToReceive.Add(itemToReceive);
				}

				if (!debugOffer.IsFairTypesExchange()) {
					Bot.ArchiLogger.LogGenericDebug("CRITICAL: This offer is NOT fair!!!");
					return false;
				}

				Bot.ArchiLogger.LogGenericDebug("Sending trade: our " + string.Join(", ", itemsToGive.Select(item => item.RealAppID + "/" + item.Type + " " + item.ClassID + " of " + item.Amount)));

				(bool success, HashSet<ulong> mobileTradeOfferIDs) = await Bot.ArchiWebHandler.SendTradeOffer(listedUser.SteamID, itemsToGive, itemsToReceive, listedUser.TradeToken, true).ConfigureAwait(false);

				if ((mobileTradeOfferIDs != null) && (mobileTradeOfferIDs.Count > 0) && Bot.HasMobileAuthenticator) {
					if (!await Bot.Actions.AcceptConfirmations(true, Steam.ConfirmationDetails.EType.Trade, listedUser.SteamID, mobileTradeOfferIDs, true).ConfigureAwait(false)) {
						return false;
					}
				}

				if (!success) {
					Bot.ArchiLogger.LogGenericDebug("Trade failed (?), continuing...");
					continue;
				}

				Bot.ArchiLogger.LogGenericDebug("Trade succeeded!");

				foreach ((uint AppID, Steam.Asset.EType Type) skippedSetThisTrade in skippedSetsThisTrade) {
					ourInventoryState.Remove(skippedSetThisTrade);
				}

				skippedSets.UnionWith(skippedSetsThisTrade);

				if (ourInventoryState.Values.All(set => set.Values.All(amount => amount <= 1))) {
					// User doesn't have any more dupes in the inventory
					Bot.ArchiLogger.LogGenericDebug("No dupes in inventory, breaking");
					break;
				}
			}

			Bot.ArchiLogger.LogGenericDebug("This round is over, we traded " + skippedSets.Count + " sets!");
			return skippedSets.Count > 0;
		}

		private sealed class ListedUser {
			internal readonly HashSet<Steam.Asset.EType> MatchableTypes = new HashSet<Steam.Asset.EType>();

#pragma warning disable 649
			[JsonProperty(PropertyName = "steam_id", Required = Required.Always)]
			internal readonly ulong SteamID;
#pragma warning restore 649

#pragma warning disable 649
			[JsonProperty(PropertyName = "trade_token", Required = Required.Always)]
			internal readonly string TradeToken;
#pragma warning restore 649

			internal float Score => GamesCount / (float) ItemsCount;

#pragma warning disable 649
			[JsonProperty(PropertyName = "games_count", Required = Required.Always)]
			private readonly ushort GamesCount;
#pragma warning restore 649

#pragma warning disable 649
			[JsonProperty(PropertyName = "items_count", Required = Required.Always)]
			private readonly ushort ItemsCount;
#pragma warning restore 649

			internal bool MatchEverything { get; private set; }

			[JsonProperty(PropertyName = "matchable_backgrounds", Required = Required.Always)]
			private byte MatchableBackgroundsNumber {
				set {
					switch (value) {
						case 0:
							MatchableTypes.Remove(Steam.Asset.EType.ProfileBackground);
							break;
						case 1:
							MatchableTypes.Add(Steam.Asset.EType.ProfileBackground);
							break;
						default:
							ASF.ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(value), value));
							return;
					}
				}
			}

			[JsonProperty(PropertyName = "matchable_cards", Required = Required.Always)]
			private byte MatchableCardsNumber {
				set {
					switch (value) {
						case 0:
							MatchableTypes.Remove(Steam.Asset.EType.TradingCard);
							break;
						case 1:
							MatchableTypes.Add(Steam.Asset.EType.TradingCard);
							break;
						default:
							ASF.ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(value), value));
							return;
					}
				}
			}

			[JsonProperty(PropertyName = "matchable_emoticons", Required = Required.Always)]
			private byte MatchableEmoticonsNumber {
				set {
					switch (value) {
						case 0:
							MatchableTypes.Remove(Steam.Asset.EType.Emoticon);
							break;
						case 1:
							MatchableTypes.Add(Steam.Asset.EType.Emoticon);
							break;
						default:
							ASF.ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(value), value));
							return;
					}
				}
			}

			[JsonProperty(PropertyName = "matchable_foil_cards", Required = Required.Always)]
			private byte MatchableFoilCardsNumber {
				set {
					switch (value) {
						case 0:
							MatchableTypes.Remove(Steam.Asset.EType.FoilTradingCard);
							break;
						case 1:
							MatchableTypes.Add(Steam.Asset.EType.FoilTradingCard);
							break;
						default:
							ASF.ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(value), value));
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
							ASF.ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(value), value));
							return;
					}
				}
			}
		}
	}
}
