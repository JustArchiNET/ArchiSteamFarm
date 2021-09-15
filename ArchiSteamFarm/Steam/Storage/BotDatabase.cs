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
using File = JustArchiNET.Madness.FileMadness.File;
#else
using System.IO;
#endif
using System;
using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ArchiSteamFarm.Collections;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Steam.Security;
using JetBrains.Annotations;
using Newtonsoft.Json;

namespace ArchiSteamFarm.Steam.Storage {
	internal sealed class BotDatabase : SerializableFile {
		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly ConcurrentHashSet<ulong> BlacklistedFromTradesSteamIDs = new();

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly ConcurrentHashSet<uint> IdlingBlacklistedAppIDs = new();

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly ConcurrentHashSet<uint> IdlingPriorityAppIDs = new();

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly ConcurrentHashSet<uint> MatchActivelyBlacklistedAppIDs = new();

		internal uint GamesToRedeemInBackgroundCount {
			get {
				lock (GamesToRedeemInBackground) {
					return (uint) GamesToRedeemInBackground.Count;
				}
			}
		}

		internal bool HasGamesToRedeemInBackground => GamesToRedeemInBackgroundCount > 0;

		[JsonProperty(Required = Required.DisallowNull)]
		private readonly OrderedDictionary GamesToRedeemInBackground = new();

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
		private BotDatabase() {
			BlacklistedFromTradesSteamIDs.OnModified += OnObjectModified;
			IdlingBlacklistedAppIDs.OnModified += OnObjectModified;
			IdlingPriorityAppIDs.OnModified += OnObjectModified;
			MatchActivelyBlacklistedAppIDs.OnModified += OnObjectModified;
		}

		[UsedImplicitly]
		public bool ShouldSerializeBackingLoginKey() => !string.IsNullOrEmpty(BackingLoginKey);

		[UsedImplicitly]
		public bool ShouldSerializeBackingMobileAuthenticator() => BackingMobileAuthenticator != null;

		[UsedImplicitly]
		public bool ShouldSerializeBlacklistedFromTradesSteamIDs() => BlacklistedFromTradesSteamIDs.Count > 0;

		[UsedImplicitly]
		public bool ShouldSerializeGamesToRedeemInBackground() => HasGamesToRedeemInBackground;

		[UsedImplicitly]
		public bool ShouldSerializeIdlingBlacklistedAppIDs() => IdlingBlacklistedAppIDs.Count > 0;

		[UsedImplicitly]
		public bool ShouldSerializeIdlingPriorityAppIDs() => IdlingPriorityAppIDs.Count > 0;

		[UsedImplicitly]
		public bool ShouldSerializeMatchActivelyBlacklistedAppIDs() => MatchActivelyBlacklistedAppIDs.Count > 0;

		protected override void Dispose(bool disposing) {
			if (disposing) {
				// Events we registered
				BlacklistedFromTradesSteamIDs.OnModified -= OnObjectModified;
				IdlingBlacklistedAppIDs.OnModified -= OnObjectModified;
				IdlingPriorityAppIDs.OnModified -= OnObjectModified;
				MatchActivelyBlacklistedAppIDs.OnModified -= OnObjectModified;

				// Those are objects that might be null and the check should be in-place
				BackingMobileAuthenticator?.Dispose();
			}

			// Base dispose
			base.Dispose(disposing);
		}

		internal void AddGamesToRedeemInBackground(IOrderedDictionary games) {
			if ((games == null) || (games.Count == 0)) {
				throw new ArgumentNullException(nameof(games));
			}

			bool save = false;

			lock (GamesToRedeemInBackground) {
				foreach (DictionaryEntry game in games.OfType<DictionaryEntry>().Where(game => !GamesToRedeemInBackground.Contains(game.Key))) {
					GamesToRedeemInBackground.Add(game.Key, game.Value);
					save = true;
				}
			}

			if (save) {
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

			BotDatabase? botDatabase;

			try {
				string json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);

				if (string.IsNullOrEmpty(json)) {
					ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(json)));

					return null;
				}

				botDatabase = JsonConvert.DeserializeObject<BotDatabase>(json);
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

		internal (string? Key, string? Name) GetGameToRedeemInBackground() {
			lock (GamesToRedeemInBackground) {
				foreach (DictionaryEntry game in GamesToRedeemInBackground) {
					return (game.Key as string, game.Value as string);
				}
			}

			return (null, null);
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

		private async void OnObjectModified(object? sender, EventArgs e) {
			if (string.IsNullOrEmpty(FilePath)) {
				return;
			}

			await Save().ConfigureAwait(false);
		}
	}
}
