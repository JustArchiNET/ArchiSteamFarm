/*
    _                _      _  ____   _                           _____
   / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
  / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
 / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
/_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|

 Copyright 2015-2017 Łukasz "JustArchi" Domeradzki
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
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.JSON;
using SteamKit2;

namespace ArchiSteamFarm {
	internal sealed class Statistics : IDisposable {
		private const byte MinHeartBeatTTL = 10; // Minimum amount of minutes we must wait before sending next HeartBeat

		private static readonly SemaphoreSlim InitializationSemaphore = new SemaphoreSlim(1);

		private static string _URL;

		private readonly Bot Bot;
		private readonly SemaphoreSlim Semaphore = new SemaphoreSlim(1);

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
			if (!ShouldSendHeartBeats || (DateTime.UtcNow < LastHeartBeat.AddMinutes(MinHeartBeatTTL))) {
				return;
			}

			await Semaphore.WaitAsync().ConfigureAwait(false);

			try {
				if (!ShouldSendHeartBeats || (DateTime.UtcNow < LastHeartBeat.AddMinutes(MinHeartBeatTTL))) {
					return;
				}

				string request = await GetURL().ConfigureAwait(false) + "/api/HeartBeat";
				Dictionary<string, string> data = new Dictionary<string, string>(2) {
					{ "SteamID", Bot.SteamID.ToString() },
					{ "Guid", Program.GlobalDatabase.Guid.ToString("N") }
				};

				// We don't need retry logic here
				if (await Program.WebBrowser.UrlPost(request, data).ConfigureAwait(false)) {
					LastHeartBeat = DateTime.UtcNow;
				}
			} finally {
				Semaphore.Release();
			}
		}

		internal async Task OnLoggedOn() => await Bot.ArchiWebHandler.JoinGroup(SharedInfo.ASFGroupSteamID).ConfigureAwait(false);

		[SuppressMessage("ReSharper", "FunctionComplexityOverflow")]
		internal async Task OnPersonaState(SteamFriends.PersonaStateCallback callback) {
			if (callback == null) {
				ASF.ArchiLogger.LogNullError(nameof(callback));
				return;
			}

			// Don't announce if we don't meet conditions
			if (!Bot.HasMobileAuthenticator || !Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.SteamTradeMatcher) || !await Bot.ArchiWebHandler.HasValidApiKey().ConfigureAwait(false) || !await Bot.ArchiWebHandler.HasPublicInventory().ConfigureAwait(false)) {
				ShouldSendHeartBeats = false;
				return;
			}

			string nickname = callback.Name ?? "";

			string avatarHash = "";
			if ((callback.AvatarHash != null) && (callback.AvatarHash.Length > 0) && callback.AvatarHash.Any(singleByte => singleByte != 0)) {
				avatarHash = BitConverter.ToString(callback.AvatarHash).Replace("-", "").ToLowerInvariant();
				if (avatarHash.All(singleChar => singleChar == '0')) {
					avatarHash = "";
				}
			}

			bool matchEverything = Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.MatchEverything);

			// Skip announcing if we already announced this bot with the same data
			if (ShouldSendHeartBeats && (LastNickname != null) && nickname.Equals(LastNickname) && (LastAvatarHash != null) && avatarHash.Equals(LastAvatarHash) && LastMatchEverything.HasValue && (matchEverything == LastMatchEverything.Value)) {
				return;
			}

			await Semaphore.WaitAsync().ConfigureAwait(false);

			try {
				// Skip announcing if we already announced this bot with the same data
				if (ShouldSendHeartBeats && (LastNickname != null) && nickname.Equals(LastNickname) && (LastAvatarHash != null) && avatarHash.Equals(LastAvatarHash) && LastMatchEverything.HasValue && (matchEverything == LastMatchEverything.Value)) {
					return;
				}

				await Trading.LimitInventoryRequestsAsync().ConfigureAwait(false);
				HashSet<Steam.Item> inventory = await Bot.ArchiWebHandler.GetMySteamInventory(true, new HashSet<Steam.Item.EType> { Steam.Item.EType.TradingCard }).ConfigureAwait(false);

				if ((inventory == null) || (inventory.Count == 0)) {
					// Don't announce, we have empty inventory
					ShouldSendHeartBeats = false;
					return;
				}

				// Even if following request fails, we want to send HeartBeats regardless
				ShouldSendHeartBeats = true;

				string request = await GetURL().ConfigureAwait(false) + "/api/Announce";
				Dictionary<string, string> data = new Dictionary<string, string>(6) {
					{ "SteamID", Bot.SteamID.ToString() },
					{ "Guid", Program.GlobalDatabase.Guid.ToString("N") },
					{ "Nickname", nickname },
					{ "AvatarHash", avatarHash },
					{ "MatchEverything", matchEverything ? "1" : "0" },
					{ "CardsCount", inventory.Count.ToString() }
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

		private static async Task<string> GetURL() {
			if (!string.IsNullOrEmpty(_URL)) {
				return _URL;
			}

			// Our statistics server is using TLS 1.2 encryption method, which is not supported e.g. by older versions of Mono
			// That's not a problem, as we support HTTP too, but of course we prefer more secure HTTPS if possible
			// Because of that, this function is responsible for finding which URL should be used for accessing the server

			// If our runtime doesn't require TLS 1.2 tests, skip the rest entirely, just use HTTPS
			const string httpsURL = "https://" + SharedInfo.StatisticsServer;
			if (!Runtime.RequiresTls12Testing) {
				_URL = httpsURL;
				return _URL;
			}

			await InitializationSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				if (!string.IsNullOrEmpty(_URL)) {
					return _URL;
				}

				// If we connect using HTTPS, use HTTPS as default version
				if (await Program.WebBrowser.UrlPost(httpsURL + "/api/ConnectionTest").ConfigureAwait(false)) {
					_URL = httpsURL;
					return _URL;
				}

				// If we connect using HTTP, use HTTP as default version instead
				const string httpURL = "http://" + SharedInfo.StatisticsServer;
				if (await Program.WebBrowser.UrlPost(httpURL + "/api/ConnectionTest").ConfigureAwait(false)) {
					_URL = httpURL;
					return _URL;
				}
			} finally {
				InitializationSemaphore.Release();
			}

			// If we didn't manage to establish connection through any of the above, return HTTPS, but don't record it
			// We might need to re-run this function in the future
			return httpsURL;
		}
	}
}