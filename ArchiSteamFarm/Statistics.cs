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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SteamKit2;

namespace ArchiSteamFarm {
	internal sealed class Statistics : IDisposable {
		private const byte MinHeartBeatTTL = 5; // Minimum amount of minutes we must wait before sending next HeartBeat

		private readonly Bot Bot;
		private readonly SemaphoreSlim Semaphore = new SemaphoreSlim(1);

		private bool HasAutomatedTrading => Bot.HasMobileAuthenticator && Bot.HasValidApiKey;
		private bool SteamTradeMatcher => Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.SteamTradeMatcher);

		private string LastAvatarHash;
		private DateTime LastHeartBeat = DateTime.MinValue;
		private bool? LastMatchEverything;
		private string LastNickname;
		private bool ShouldSendHeartBeats;

		internal Statistics(Bot bot) {
			if (bot == null) {
				throw new ArgumentNullException(nameof(bot));
			}

			Bot = bot;
		}

		public void Dispose() => Semaphore.Dispose();

		internal async Task OnHeartBeat() {
			if (!ShouldSendHeartBeats || (DateTime.Now < LastHeartBeat.AddMinutes(MinHeartBeatTTL))) {
				return;
			}

			await Semaphore.WaitAsync().ConfigureAwait(false);

			try {
				if (!ShouldSendHeartBeats || (DateTime.Now < LastHeartBeat.AddMinutes(MinHeartBeatTTL))) {
					return;
				}

				const string request = SharedInfo.StatisticsServer + "/api/HeartBeat";
				Dictionary<string, string> data = new Dictionary<string, string>(2) {
					{ "SteamID", Bot.SteamID.ToString() },
					{ "Guid", Program.GlobalDatabase.Guid.ToString("N") }
				};

				// We don't need retry logic here
				if (await Program.WebBrowser.UrlPost(request, data).ConfigureAwait(false)) {
					LastHeartBeat = DateTime.Now;
				}
			} finally {
				Semaphore.Release();
			}
		}

		internal async Task OnLoggedOn() => await Bot.ArchiWebHandler.JoinGroup(SharedInfo.ASFGroupSteamID).ConfigureAwait(false);

		internal async Task OnPersonaState(SteamFriends.PersonaStateCallback callback) {
			if (callback == null) {
				ASF.ArchiLogger.LogNullError(nameof(callback));
				return;
			}

			// Don't announce if we don't meet conditions
			if (!HasAutomatedTrading || !SteamTradeMatcher) {
				ShouldSendHeartBeats = false;
				return;
			}

			string nickname = callback.Name ?? "";
			string avatarHash = "";

			if ((callback.AvatarHash != null) && (callback.AvatarHash.Length > 0) && callback.AvatarHash.Any(singleByte => singleByte != 0)) {
				avatarHash = BitConverter.ToString(callback.AvatarHash).Replace("-", "").ToLowerInvariant();
				if (avatarHash.Equals("0000000000000000000000000000000000000000")) {
					avatarHash = "";
				}
			}

			bool matchEverything = Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.MatchEverything);

			// Skip announcing if we already announced this bot with the same data
			if (!string.IsNullOrEmpty(LastNickname) && nickname.Equals(LastNickname) && !string.IsNullOrEmpty(LastAvatarHash) && avatarHash.Equals(LastAvatarHash) && LastMatchEverything.HasValue && (matchEverything == LastMatchEverything.Value)) {
				return;
			}

			await Semaphore.WaitAsync().ConfigureAwait(false);

			try {
				// Skip announcing if we already announced this bot with the same data
				if (!string.IsNullOrEmpty(LastNickname) && nickname.Equals(LastNickname) && !string.IsNullOrEmpty(LastAvatarHash) && avatarHash.Equals(LastAvatarHash) && LastMatchEverything.HasValue && (matchEverything == LastMatchEverything.Value)) {
					return;
				}

				// Even if following request fails, we want to send HeartBeats regardless
				ShouldSendHeartBeats = true;

				const string request = SharedInfo.StatisticsServer + "/api/Announce";
				Dictionary<string, string> data = new Dictionary<string, string>(5) {
					{ "SteamID", Bot.SteamID.ToString() },
					{ "Guid", Program.GlobalDatabase.Guid.ToString("N") },
					{ "Nickname", nickname },
					{ "AvatarHash", avatarHash },
					{ "MatchEverything", matchEverything ? "1" : "0" }
				};

				// We don't need retry logic here
				if (await Program.WebBrowser.UrlPost(request, data).ConfigureAwait(false)) {
					LastNickname = nickname;
					LastAvatarHash = avatarHash;
					LastMatchEverything = matchEverything;
				}
			} finally {
				Semaphore.Release();
			}
		}
	}
}