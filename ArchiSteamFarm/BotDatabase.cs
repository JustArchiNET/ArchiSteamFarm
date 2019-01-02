//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// 
// Copyright 2015-2019 Łukasz "JustArchi" Domeradzki
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
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Collections;
using Newtonsoft.Json;

namespace ArchiSteamFarm {
	internal sealed class BotDatabase : IDisposable {
		internal bool HasGamesToRedeemInBackground {
			get {
				lock (GamesToRedeemInBackground) {
					return GamesToRedeemInBackground.Count > 0;
				}
			}
		}

		[JsonProperty(Required = Required.DisallowNull)]
		private readonly ConcurrentHashSet<ulong> BlacklistedFromTradesSteamIDs = new ConcurrentHashSet<ulong>();

		private readonly SemaphoreSlim FileSemaphore = new SemaphoreSlim(1, 1);

		[JsonProperty(Required = Required.DisallowNull)]
		private readonly OrderedDictionary GamesToRedeemInBackground = new OrderedDictionary();

		[JsonProperty(Required = Required.DisallowNull)]
		private readonly ConcurrentHashSet<uint> IdlingBlacklistedAppIDs = new ConcurrentHashSet<uint>();

		[JsonProperty(Required = Required.DisallowNull)]
		private readonly ConcurrentHashSet<uint> IdlingPriorityAppIDs = new ConcurrentHashSet<uint>();

		[JsonProperty(PropertyName = "_LoginKey")]
		internal string LoginKey { get; private set; }

		[JsonProperty(PropertyName = "_MobileAuthenticator")]
		internal MobileAuthenticator MobileAuthenticator { get; private set; }

		private string FilePath;
		private bool ReadOnly;

		// This constructor is used when creating new database
		private BotDatabase(string filePath) {
			if (string.IsNullOrEmpty(filePath)) {
				throw new ArgumentNullException(nameof(filePath));
			}

			FilePath = filePath;
		}

		// This constructor is used only by deserializer
		private BotDatabase() { }

		public void Dispose() => FileSemaphore.Dispose();

		internal async Task AddBlacklistedFromTradesSteamIDs(IReadOnlyCollection<ulong> steamIDs) {
			if ((steamIDs == null) || (steamIDs.Count == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(steamIDs));

				return;
			}

			if (BlacklistedFromTradesSteamIDs.AddRange(steamIDs)) {
				await Save().ConfigureAwait(false);
			}
		}

		internal async Task AddGamesToRedeemInBackground(IOrderedDictionary games) {
			if ((games == null) || (games.Count == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(games));

				return;
			}

			bool save = false;

			lock (GamesToRedeemInBackground) {
				foreach (DictionaryEntry game in games) {
					if (GamesToRedeemInBackground.Contains(game.Key)) {
						continue;
					}

					GamesToRedeemInBackground.Add(game.Key, game.Value);
					save = true;
				}
			}

			if (save) {
				await Save().ConfigureAwait(false);
			}
		}

		internal async Task AddIdlingBlacklistedAppIDs(IReadOnlyCollection<uint> appIDs) {
			if ((appIDs == null) || (appIDs.Count == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(appIDs));

				return;
			}

			if (IdlingBlacklistedAppIDs.AddRange(appIDs)) {
				await Save().ConfigureAwait(false);
			}
		}

		internal async Task AddIdlingPriorityAppIDs(IReadOnlyCollection<uint> appIDs) {
			if ((appIDs == null) || (appIDs.Count == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(appIDs));

				return;
			}

			if (IdlingPriorityAppIDs.AddRange(appIDs)) {
				await Save().ConfigureAwait(false);
			}
		}

		internal static async Task<BotDatabase> CreateOrLoad(string filePath) {
			if (string.IsNullOrEmpty(filePath)) {
				ASF.ArchiLogger.LogNullError(nameof(filePath));

				return null;
			}

			if (!File.Exists(filePath)) {
				return new BotDatabase(filePath);
			}

			BotDatabase botDatabase;

			try {
				botDatabase = JsonConvert.DeserializeObject<BotDatabase>(await RuntimeCompatibility.File.ReadAllTextAsync(filePath).ConfigureAwait(false));
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);

				return null;
			}

			if (botDatabase == null) {
				ASF.ArchiLogger.LogNullError(nameof(botDatabase));

				return null;
			}

			botDatabase.FilePath = filePath;

			return botDatabase;
		}

		internal IReadOnlyCollection<ulong> GetBlacklistedFromTradesSteamIDs() => BlacklistedFromTradesSteamIDs;

		internal (string Key, string Name) GetGameToRedeemInBackground() {
			lock (GamesToRedeemInBackground) {
				foreach (DictionaryEntry game in GamesToRedeemInBackground) {
					return (game.Key as string, game.Value as string);
				}
			}

			return (null, null);
		}

		internal IReadOnlyCollection<uint> GetIdlingBlacklistedAppIDs() => IdlingBlacklistedAppIDs;
		internal IReadOnlyCollection<uint> GetIdlingPriorityAppIDs() => IdlingPriorityAppIDs;

		internal bool IsBlacklistedFromIdling(uint appID) {
			if (appID == 0) {
				ASF.ArchiLogger.LogNullError(nameof(appID));

				return false;
			}

			return IdlingBlacklistedAppIDs.Contains(appID);
		}

		internal bool IsBlacklistedFromTrades(ulong steamID) {
			if (steamID == 0) {
				ASF.ArchiLogger.LogNullError(nameof(steamID));

				return false;
			}

			return BlacklistedFromTradesSteamIDs.Contains(steamID);
		}

		internal bool IsPriorityIdling(uint appID) {
			if (appID == 0) {
				ASF.ArchiLogger.LogNullError(nameof(appID));

				return false;
			}

			return IdlingPriorityAppIDs.Contains(appID);
		}

		internal async Task MakeReadOnly() {
			if (ReadOnly) {
				return;
			}

			await FileSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				if (ReadOnly) {
					return;
				}

				ReadOnly = true;
			} finally {
				FileSemaphore.Release();
			}
		}

		internal async Task RemoveBlacklistedFromTradesSteamIDs(IReadOnlyCollection<ulong> steamIDs) {
			if ((steamIDs == null) || (steamIDs.Count == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(steamIDs));

				return;
			}

			if (BlacklistedFromTradesSteamIDs.RemoveRange(steamIDs)) {
				await Save().ConfigureAwait(false);
			}
		}

		internal async Task RemoveGameToRedeemInBackground(string key) {
			if (string.IsNullOrEmpty(key)) {
				ASF.ArchiLogger.LogNullError(nameof(key));

				return;
			}

			lock (GamesToRedeemInBackground) {
				if (!GamesToRedeemInBackground.Contains(key)) {
					return;
				}

				GamesToRedeemInBackground.Remove(key);
			}

			await Save().ConfigureAwait(false);
		}

		internal async Task RemoveIdlingBlacklistedAppIDs(IReadOnlyCollection<uint> appIDs) {
			if ((appIDs == null) || (appIDs.Count == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(appIDs));

				return;
			}

			if (IdlingBlacklistedAppIDs.RemoveRange(appIDs)) {
				await Save().ConfigureAwait(false);
			}
		}

		internal async Task RemoveIdlingPriorityAppIDs(IReadOnlyCollection<uint> appIDs) {
			if ((appIDs == null) || (appIDs.Count == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(appIDs));

				return;
			}

			if (IdlingPriorityAppIDs.RemoveRange(appIDs)) {
				await Save().ConfigureAwait(false);
			}
		}

		internal async Task SetLoginKey(string value = null) {
			if (value == LoginKey) {
				return;
			}

			LoginKey = value;
			await Save().ConfigureAwait(false);
		}

		internal async Task SetMobileAuthenticator(MobileAuthenticator value = null) {
			if (value == MobileAuthenticator) {
				return;
			}

			MobileAuthenticator = value;
			await Save().ConfigureAwait(false);
		}

		private async Task Save() {
			if (ReadOnly) {
				return;
			}

			string json = JsonConvert.SerializeObject(this);

			if (string.IsNullOrEmpty(json)) {
				ASF.ArchiLogger.LogNullError(nameof(json));

				return;
			}

			string newFilePath = FilePath + ".new";

			await FileSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				if (ReadOnly) {
					return;
				}

				// We always want to write entire content to temporary file first, in order to never load corrupted data, also when target file doesn't exist
				await RuntimeCompatibility.File.WriteAllTextAsync(newFilePath, json).ConfigureAwait(false);

				if (File.Exists(FilePath)) {
					File.Replace(newFilePath, FilePath, null);
				} else {
					File.Move(newFilePath, FilePath);
				}
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);
			} finally {
				FileSemaphore.Release();
			}
		}

		// ReSharper disable UnusedMember.Global
		public bool ShouldSerializeBlacklistedFromTradesSteamIDs() => BlacklistedFromTradesSteamIDs.Count > 0;
		public bool ShouldSerializeGamesToRedeemInBackground() => HasGamesToRedeemInBackground;
		public bool ShouldSerializeIdlingBlacklistedAppIDs() => IdlingBlacklistedAppIDs.Count > 0;
		public bool ShouldSerializeIdlingPriorityAppIDs() => IdlingPriorityAppIDs.Count > 0;
		public bool ShouldSerializeLoginKey() => !string.IsNullOrEmpty(LoginKey);
		public bool ShouldSerializeMobileAuthenticator() => MobileAuthenticator != null;

		// ReSharper restore UnusedMember.Global
	}
}
