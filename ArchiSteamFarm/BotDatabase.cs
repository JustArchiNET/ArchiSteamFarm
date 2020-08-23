//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2020 Łukasz "JustArchi" Domeradzki
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
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ArchiSteamFarm.Collections;
using ArchiSteamFarm.Helpers;
using ArchiSteamFarm.Localization;
using Newtonsoft.Json;
using SteamKit2;

namespace ArchiSteamFarm {
	internal sealed class BotDatabase : SerializableFile {
		internal uint GamesToRedeemInBackgroundCount {
			get {
				lock (GamesToRedeemInBackground) {
					return (uint) GamesToRedeemInBackground.Count;
				}
			}
		}

		internal bool HasGamesToRedeemInBackground => GamesToRedeemInBackgroundCount > 0;
		internal bool HasIdlingPriorityAppIDs => IdlingPriorityAppIDs.Count > 0;

		[JsonProperty(Required = Required.DisallowNull)]
		private readonly ConcurrentHashSet<ulong> BlacklistedFromTradesSteamIDs = new ConcurrentHashSet<ulong>();

		[JsonProperty(Required = Required.DisallowNull)]
		private readonly OrderedDictionary GamesToRedeemInBackground = new OrderedDictionary();

		[JsonProperty(Required = Required.DisallowNull)]
		private readonly ConcurrentHashSet<uint> IdlingBlacklistedAppIDs = new ConcurrentHashSet<uint>();

		[JsonProperty(Required = Required.DisallowNull)]
		private readonly ConcurrentHashSet<uint> IdlingPriorityAppIDs = new ConcurrentHashSet<uint>();

		internal string? LoginKey {
			get => BackingLoginKey;

			set {
				if (BackingLoginKey == value) {
					return;
				}

				BackingLoginKey = value;
				Utilities.InBackground(Save);
			}
		}

		internal MobileAuthenticator? MobileAuthenticator {
			get => BackingMobileAuthenticator;

			set {
				if (BackingMobileAuthenticator == value) {
					return;
				}

				BackingMobileAuthenticator = value;
				Utilities.InBackground(Save);
			}
		}

		[JsonProperty(PropertyName = "_" + nameof(LoginKey))]
		private string? BackingLoginKey;

		[JsonProperty(PropertyName = "_" + nameof(MobileAuthenticator))]
		private MobileAuthenticator? BackingMobileAuthenticator;

		private BotDatabase(string filePath) {
			if (string.IsNullOrEmpty(filePath)) {
				throw new ArgumentNullException(nameof(filePath));
			}

			FilePath = filePath;
		}

		[JsonConstructor]
		private BotDatabase() { }

		public override void Dispose() {
			BackingMobileAuthenticator?.Dispose();

			base.Dispose();
		}

		internal void AddBlacklistedFromTradesSteamIDs(IReadOnlyCollection<ulong> steamIDs) {
			if ((steamIDs == null) || (steamIDs.Count == 0)) {
				throw new ArgumentNullException(nameof(steamIDs));
			}

			if (BlacklistedFromTradesSteamIDs.AddRange(steamIDs)) {
				Utilities.InBackground(Save);
			}
		}

		internal void AddGamesToRedeemInBackground(IOrderedDictionary games) {
			if ((games == null) || (games.Count == 0)) {
				throw new ArgumentNullException(nameof(games));
			}

			bool save = false;

			lock (GamesToRedeemInBackground) {
				foreach (DictionaryEntry game in games.Cast<DictionaryEntry>().Where(game => !GamesToRedeemInBackground.Contains(game.Key))) {
					GamesToRedeemInBackground.Add(game.Key, game.Value);
					save = true;
				}
			}

			if (save) {
				Utilities.InBackground(Save);
			}
		}

		internal void AddIdlingBlacklistedAppIDs(IReadOnlyCollection<uint> appIDs) {
			if ((appIDs == null) || (appIDs.Count == 0)) {
				throw new ArgumentNullException(nameof(appIDs));
			}

			if (IdlingBlacklistedAppIDs.AddRange(appIDs)) {
				Utilities.InBackground(Save);
			}
		}

		internal void AddIdlingPriorityAppIDs(IReadOnlyCollection<uint> appIDs) {
			if ((appIDs == null) || (appIDs.Count == 0)) {
				throw new ArgumentNullException(nameof(appIDs));
			}

			if (IdlingPriorityAppIDs.AddRange(appIDs)) {
				Utilities.InBackground(Save);
			}
		}

		internal static async Task<BotDatabase?> CreateOrLoad(string filePath) {
			if (string.IsNullOrEmpty(filePath)) {
				throw new ArgumentNullException(nameof(filePath));
			}

			if (!File.Exists(filePath)) {
				return new BotDatabase(filePath);
			}

			BotDatabase botDatabase;

			try {
				string json = await RuntimeCompatibility.File.ReadAllTextAsync(filePath).ConfigureAwait(false);

				if (string.IsNullOrEmpty(json)) {
					ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorIsEmpty, nameof(json)));

					return null;
				}

				botDatabase = JsonConvert.DeserializeObject<BotDatabase>(json);
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);

				return null;
			}

			// ReSharper disable once ConditionIsAlwaysTrueOrFalse - wrong, "null" json serializes into null object
			if (botDatabase == null) {
				ASF.ArchiLogger.LogNullError(nameof(botDatabase));

				return null;
			}

			botDatabase.FilePath = filePath;

			return botDatabase;
		}

		internal IReadOnlyCollection<ulong> GetBlacklistedFromTradesSteamIDs() => BlacklistedFromTradesSteamIDs;

#pragma warning disable CS8605
		internal (string? Key, string? Name) GetGameToRedeemInBackground() {
			lock (GamesToRedeemInBackground) {
				foreach (DictionaryEntry game in GamesToRedeemInBackground) {
					return (game.Key as string, game.Value as string);
				}
			}

			return (null, null);
		}
#pragma warning restore CS8605

		internal IReadOnlyCollection<uint> GetIdlingBlacklistedAppIDs() => IdlingBlacklistedAppIDs;
		internal IReadOnlyCollection<uint> GetIdlingPriorityAppIDs() => IdlingPriorityAppIDs;

		internal bool IsBlacklistedFromIdling(uint appID) {
			if (appID == 0) {
				throw new ArgumentNullException(nameof(appID));
			}

			return IdlingBlacklistedAppIDs.Contains(appID);
		}

		internal bool IsBlacklistedFromTrades(ulong steamID) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
				throw new ArgumentNullException(nameof(steamID));
			}

			return BlacklistedFromTradesSteamIDs.Contains(steamID);
		}

		internal bool IsPriorityIdling(uint appID) {
			if (appID == 0) {
				throw new ArgumentNullException(nameof(appID));
			}

			return IdlingPriorityAppIDs.Contains(appID);
		}

		internal void RemoveBlacklistedFromTradesSteamIDs(IReadOnlyCollection<ulong> steamIDs) {
			if ((steamIDs == null) || (steamIDs.Count == 0)) {
				throw new ArgumentNullException(nameof(steamIDs));
			}

			if (BlacklistedFromTradesSteamIDs.RemoveRange(steamIDs)) {
				Utilities.InBackground(Save);
			}
		}

		internal void RemoveGameToRedeemInBackground(string key) {
			if (string.IsNullOrEmpty(key)) {
				throw new ArgumentNullException(nameof(key));
			}

			lock (GamesToRedeemInBackground) {
				if (!GamesToRedeemInBackground.Contains(key)) {
					return;
				}

				GamesToRedeemInBackground.Remove(key);
			}

			Utilities.InBackground(Save);
		}

		internal void RemoveIdlingBlacklistedAppIDs(IReadOnlyCollection<uint> appIDs) {
			if ((appIDs == null) || (appIDs.Count == 0)) {
				throw new ArgumentNullException(nameof(appIDs));
			}

			if (IdlingBlacklistedAppIDs.RemoveRange(appIDs)) {
				Utilities.InBackground(Save);
			}
		}

		internal void RemoveIdlingPriorityAppIDs(IReadOnlyCollection<uint> appIDs) {
			if ((appIDs == null) || (appIDs.Count == 0)) {
				throw new ArgumentNullException(nameof(appIDs));
			}

			if (IdlingPriorityAppIDs.RemoveRange(appIDs)) {
				Utilities.InBackground(Save);
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
