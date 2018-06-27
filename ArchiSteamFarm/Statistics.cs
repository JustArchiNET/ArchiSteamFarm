//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// 
//  Copyright 2015-2018 Łukasz "JustArchi" Domeradzki
//  Contact: JustArchi@JustArchi.net
// 
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
// 
//  http://www.apache.org/licenses/LICENSE-2.0
//      
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Json;
using Newtonsoft.Json;

namespace ArchiSteamFarm {
	internal sealed class Statistics : IDisposable {
		private const byte MinAnnouncementCheckTTL = 6; // Minimum amount of hours we must wait before checking eligibility for Announcement, should be lower than MinPersonaStateTTL
		private const byte MinHeartBeatTTL = 10; // Minimum amount of minutes we must wait before sending next HeartBeat
		private const byte MinItemsCount = 100; // Minimum amount of items to be eligible for public listing
		private const byte MinPersonaStateTTL = 8; // Minimum amount of hours we must wait before requesting persona state update
		private const string URL = "https://" + SharedInfo.StatisticsServer;

		private static readonly HashSet<Steam.Asset.EType> AcceptedMatchableTypes = new HashSet<Steam.Asset.EType> { Steam.Asset.EType.Emoticon, Steam.Asset.EType.FoilTradingCard, Steam.Asset.EType.ProfileBackground, Steam.Asset.EType.TradingCard };

		private readonly Bot Bot;
		private readonly SemaphoreSlim RequestsSemaphore = new SemaphoreSlim(1, 1);

		private DateTime LastAnnouncementCheck;
		private DateTime LastHeartBeat;
		private DateTime LastPersonaStateRequest;
		private bool ShouldSendHeartBeats;

		internal Statistics(Bot bot) => Bot = bot ?? throw new ArgumentNullException(nameof(bot));

		public void Dispose() => RequestsSemaphore.Dispose();

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
					{ "SteamID", Bot.CachedSteamID.ToString() },
					{ "Guid", Program.GlobalDatabase.Guid.ToString("N") }
				};

				// We don't need retry logic here
				if (await Program.WebBrowser.UrlPost(request, data, maxTries: 1).ConfigureAwait(false) != null) {
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
				if (!await ShouldAnnounce().ConfigureAwait(false) || string.IsNullOrEmpty(tradeToken = await Bot.ArchiWebHandler.GetTradeToken().ConfigureAwait(false))) {
					LastAnnouncementCheck = DateTime.UtcNow;
					ShouldSendHeartBeats = false;
					return;
				}

				HashSet<Steam.Asset.EType> acceptedMatchableTypes = Bot.BotConfig.MatchableTypes.Where(type => AcceptedMatchableTypes.Contains(type)).ToHashSet();
				if (acceptedMatchableTypes.Count == 0) {
					LastAnnouncementCheck = DateTime.UtcNow;
					ShouldSendHeartBeats = false;
					return;
				}

				HashSet<Steam.Asset> inventory = await Bot.ArchiWebHandler.GetInventory(Bot.CachedSteamID, tradable: true, wantedTypes: acceptedMatchableTypes).ConfigureAwait(false);

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
					{ "SteamID", Bot.CachedSteamID.ToString() },
					{ "Guid", Program.GlobalDatabase.Guid.ToString("N") },
					{ "Nickname", nickname ?? "" },
					{ "AvatarHash", avatarHash ?? "" },
					{ "GamesCount", inventory.Select(item => item.RealAppID).Distinct().Count().ToString() },
					{ "ItemsCount", inventory.Count.ToString() },
					{ "MatchableTypes", JsonConvert.SerializeObject(acceptedMatchableTypes) },
					{ "MatchEverything", Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.MatchEverything) ? "1" : "0" },
					{ "TradeToken", tradeToken }
				};

				// We don't need retry logic here
				if (await Program.WebBrowser.UrlPost(request, data, maxTries: 1).ConfigureAwait(false) != null) {
					LastAnnouncementCheck = DateTime.UtcNow;
					ShouldSendHeartBeats = true;
				}
			} finally {
				RequestsSemaphore.Release();
			}
		}

		private async Task<bool> ShouldAnnounce() {
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
	}
}