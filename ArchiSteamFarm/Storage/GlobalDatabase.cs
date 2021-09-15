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
using File = JustArchiNET.Madness.FileMadness.File;
#else
using System.IO;
#endif
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.SteamKit2;
using JetBrains.Annotations;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ArchiSteamFarm.Storage {
	public sealed class GlobalDatabase : SerializableFile {
		[JsonIgnore]
		[PublicAPI]
		public IReadOnlyDictionary<uint, ulong> PackageAccessTokensReadOnly => PackagesAccessTokens;

		[JsonIgnore]
		[PublicAPI]
		public IReadOnlyDictionary<uint, (uint ChangeNumber, ImmutableHashSet<uint>? AppIDs)> PackagesDataReadOnly => PackagesData;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly InMemoryServerListProvider ServerListProvider = new();

		[JsonProperty(Required = Required.DisallowNull)]
		private readonly ConcurrentDictionary<string, JToken> KeyValueJsonStorage = new();

		[JsonProperty(Required = Required.DisallowNull)]
		private readonly ConcurrentDictionary<uint, ulong> PackagesAccessTokens = new();

		[JsonProperty(Required = Required.DisallowNull)]
		private readonly ConcurrentDictionary<uint, (uint ChangeNumber, ImmutableHashSet<uint>? AppIDs)> PackagesData = new();

		private readonly SemaphoreSlim PackagesRefreshSemaphore = new(1, 1);

		[JsonProperty(Required = Required.DisallowNull)]
		[PublicAPI]
		public Guid Identifier { get; private set; } = Guid.NewGuid();

		internal uint CellID {
			get => BackingCellID;

			set {
				if (BackingCellID == value) {
					return;
				}

				BackingCellID = value;
				Utilities.InBackground(Save);
			}
		}

		internal uint LastChangeNumber {
			get => BackingLastChangeNumber;

			set {
				if (BackingLastChangeNumber == value) {
					return;
				}

				BackingLastChangeNumber = value;
				Utilities.InBackground(Save);
			}
		}

		[JsonProperty(PropertyName = "_" + nameof(CellID), Required = Required.DisallowNull)]
		private uint BackingCellID;

		[JsonProperty(PropertyName = "_" + nameof(LastChangeNumber), Required = Required.DisallowNull)]
		private uint BackingLastChangeNumber;

		private GlobalDatabase(string filePath) : this() {
			if (string.IsNullOrEmpty(filePath)) {
				throw new ArgumentNullException(nameof(filePath));
			}

			FilePath = filePath;
		}

		[JsonConstructor]
		private GlobalDatabase() => ServerListProvider.ServerListUpdated += OnObjectModified;

		[PublicAPI]
		public void DeleteFromJsonStorage(string key) {
			if (string.IsNullOrEmpty(key)) {
				throw new ArgumentNullException(nameof(key));
			}

			if (!KeyValueJsonStorage.TryRemove(key, out _)) {
				return;
			}

			Utilities.InBackground(Save);
		}

		[PublicAPI]
		public JToken? LoadFromJsonStorage(string key) {
			if (string.IsNullOrEmpty(key)) {
				throw new ArgumentNullException(nameof(key));
			}

			return KeyValueJsonStorage.TryGetValue(key, out JToken? value) ? value : null;
		}

		[PublicAPI]
		public void SaveToJsonStorage(string key, JToken value) {
			if (string.IsNullOrEmpty(key)) {
				throw new ArgumentNullException(nameof(key));
			}

			if (value == null) {
				throw new ArgumentNullException(nameof(value));
			}

			if (value.Type == JTokenType.Null) {
				DeleteFromJsonStorage(key);

				return;
			}

			if (KeyValueJsonStorage.TryGetValue(key, out JToken? currentValue) && JToken.DeepEquals(currentValue, value)) {
				return;
			}

			KeyValueJsonStorage[key] = value;
			Utilities.InBackground(Save);
		}

		[UsedImplicitly]
		public bool ShouldSerializeBackingCellID() => BackingCellID != 0;

		[UsedImplicitly]
		public bool ShouldSerializeBackingLastChangeNumber() => LastChangeNumber != 0;

		[UsedImplicitly]
		public bool ShouldSerializeKeyValueJsonStorage() => !KeyValueJsonStorage.IsEmpty;

		[UsedImplicitly]
		public bool ShouldSerializePackagesAccessTokens() => !PackagesAccessTokens.IsEmpty;

		[UsedImplicitly]
		public bool ShouldSerializePackagesData() => !PackagesData.IsEmpty;

		[UsedImplicitly]
		public bool ShouldSerializeServerListProvider() => ServerListProvider.ShouldSerializeServerRecords();

		protected override void Dispose(bool disposing) {
			if (disposing) {
				// Events we registered
				ServerListProvider.ServerListUpdated -= OnObjectModified;

				// Those are objects that are always being created if constructor doesn't throw exception
				PackagesRefreshSemaphore.Dispose();
			}

			// Base dispose
			base.Dispose(disposing);
		}

		internal static async Task<GlobalDatabase?> CreateOrLoad(string filePath) {
			if (string.IsNullOrEmpty(filePath)) {
				throw new ArgumentNullException(nameof(filePath));
			}

			if (!File.Exists(filePath)) {
				GlobalDatabase result = new(filePath);

				Utilities.InBackground(result.Save);

				return result;
			}

			GlobalDatabase? globalDatabase;

			try {
				string json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);

				if (string.IsNullOrEmpty(json)) {
					ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(json)));

					return null;
				}

				globalDatabase = JsonConvert.DeserializeObject<GlobalDatabase>(json);
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);

				return null;
			}

			if (globalDatabase == null) {
				ASF.ArchiLogger.LogNullError(nameof(globalDatabase));

				return null;
			}

			globalDatabase.FilePath = filePath;

			return globalDatabase;
		}

		internal HashSet<uint> GetPackageIDs(uint appID, IEnumerable<uint> packageIDs) {
			if (appID == 0) {
				throw new ArgumentOutOfRangeException(nameof(appID));
			}

			if (packageIDs == null) {
				throw new ArgumentNullException(nameof(packageIDs));
			}

			HashSet<uint> result = new();

			foreach (uint packageID in packageIDs.Where(packageID => packageID != 0)) {
				if (!PackagesData.TryGetValue(packageID, out (uint ChangeNumber, ImmutableHashSet<uint>? AppIDs) packagesData) || (packagesData.AppIDs?.Contains(appID) != true)) {
					continue;
				}

				result.Add(packageID);
			}

			return result;
		}

		internal async Task OnPICSChangesRestart(uint currentChangeNumber) {
			if (currentChangeNumber == 0) {
				throw new ArgumentOutOfRangeException(nameof(currentChangeNumber));
			}

			if (Bot.Bots == null) {
				throw new InvalidOperationException(nameof(Bot.Bots));
			}

			if (currentChangeNumber <= LastChangeNumber) {
				return;
			}

			LastChangeNumber = currentChangeNumber;

			Bot? refreshBot = Bot.Bots.Values.FirstOrDefault(bot => bot.IsConnectedAndLoggedOn);

			if (refreshBot == null) {
				return;
			}

			if (PackagesData.IsEmpty) {
				return;
			}

			Dictionary<uint, uint> packageIDs = PackagesData.Keys.ToDictionary(packageID => packageID, _ => currentChangeNumber);

			await RefreshPackages(refreshBot, packageIDs).ConfigureAwait(false);
		}

		internal void RefreshPackageAccessTokens(IReadOnlyDictionary<uint, ulong> packageAccessTokens) {
			if ((packageAccessTokens == null) || (packageAccessTokens.Count == 0)) {
				throw new ArgumentNullException(nameof(packageAccessTokens));
			}

			bool save = false;

			foreach ((uint packageID, ulong currentAccessToken) in packageAccessTokens) {
				if (!PackagesAccessTokens.TryGetValue(packageID, out ulong previousAccessToken) || (previousAccessToken != currentAccessToken)) {
					PackagesAccessTokens[packageID] = currentAccessToken;
					save = true;
				}
			}

			if (save) {
				Utilities.InBackground(Save);
			}
		}

		internal async Task RefreshPackages(Bot bot, IReadOnlyDictionary<uint, uint> packages) {
			if (bot == null) {
				throw new ArgumentNullException(nameof(bot));
			}

			if ((packages == null) || (packages.Count == 0)) {
				throw new ArgumentNullException(nameof(packages));
			}

			await PackagesRefreshSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				HashSet<uint> packageIDs = packages.Where(package => (package.Key != 0) && (!PackagesData.TryGetValue(package.Key, out (uint ChangeNumber, ImmutableHashSet<uint>? AppIDs) previousData) || (previousData.ChangeNumber < package.Value))).Select(package => package.Key).ToHashSet();

				if (packageIDs.Count == 0) {
					return;
				}

				Dictionary<uint, (uint ChangeNumber, ImmutableHashSet<uint>? AppIDs)>? packagesData = await bot.GetPackagesData(packageIDs).ConfigureAwait(false);

				if (packagesData == null) {
					bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);

					return;
				}

				bool save = false;

				foreach ((uint packageID, (uint ChangeNumber, ImmutableHashSet<uint>? AppIDs) packageData) in packagesData) {
					if (PackagesData.TryGetValue(packageID, out (uint ChangeNumber, ImmutableHashSet<uint>? AppIDs) previousData) && (packageData.ChangeNumber <= previousData.ChangeNumber)) {
						continue;
					}

					PackagesData[packageID] = packageData;
					save = true;
				}

				if (save) {
					Utilities.InBackground(Save);
				}
			} finally {
				PackagesRefreshSemaphore.Release();
			}
		}

		private async void OnObjectModified(object? sender, EventArgs e) {
			if (string.IsNullOrEmpty(FilePath)) {
				return;
			}

			await Save().ConfigureAwait(false);
		}
	}
}
