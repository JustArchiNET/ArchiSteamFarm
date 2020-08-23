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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Helpers;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.SteamKit2;
using JetBrains.Annotations;
using Newtonsoft.Json;

namespace ArchiSteamFarm {
	public sealed class GlobalDatabase : SerializableFile {
		[JsonProperty(Required = Required.DisallowNull)]
		[PublicAPI]
		public readonly Guid Guid = Guid.NewGuid();

		[JsonIgnore]
		[PublicAPI]
		public IReadOnlyDictionary<uint, ulong> PackageAccessTokensReadOnly => PackagesAccessTokens;

		[JsonIgnore]
		[PublicAPI]
		public IReadOnlyDictionary<uint, (uint ChangeNumber, HashSet<uint>? AppIDs)> PackagesDataReadOnly => PackagesData;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly InMemoryServerListProvider ServerListProvider = new InMemoryServerListProvider();

		[JsonProperty(Required = Required.DisallowNull)]
		private readonly ConcurrentDictionary<uint, ulong> PackagesAccessTokens = new ConcurrentDictionary<uint, ulong>();

		[JsonProperty(Required = Required.DisallowNull)]
		private readonly ConcurrentDictionary<uint, (uint ChangeNumber, HashSet<uint>? AppIDs)> PackagesData = new ConcurrentDictionary<uint, (uint ChangeNumber, HashSet<uint>? AppIDs)>();

		private readonly SemaphoreSlim PackagesRefreshSemaphore = new SemaphoreSlim(1, 1);

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

		[JsonProperty(PropertyName = "_" + nameof(CellID), Required = Required.DisallowNull)]
		private uint BackingCellID;

		private GlobalDatabase(string filePath) : this() {
			if (string.IsNullOrEmpty(filePath)) {
				throw new ArgumentNullException(nameof(filePath));
			}

			FilePath = filePath;
		}

		[JsonConstructor]
		private GlobalDatabase() => ServerListProvider.ServerListUpdated += OnServerListUpdated;

		public override void Dispose() {
			// Events we registered
			ServerListProvider.ServerListUpdated -= OnServerListUpdated;

			// Those are objects that are always being created if constructor doesn't throw exception
			PackagesRefreshSemaphore.Dispose();

			// Base dispose
			base.Dispose();
		}

		internal static async Task<GlobalDatabase?> CreateOrLoad(string filePath) {
			if (string.IsNullOrEmpty(filePath)) {
				throw new ArgumentNullException(nameof(filePath));
			}

			if (!File.Exists(filePath)) {
				return new GlobalDatabase(filePath);
			}

			GlobalDatabase globalDatabase;

			try {
				string json = await RuntimeCompatibility.File.ReadAllTextAsync(filePath).ConfigureAwait(false);

				if (string.IsNullOrEmpty(json)) {
					ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorIsEmpty, nameof(json)));

					return null;
				}

				globalDatabase = JsonConvert.DeserializeObject<GlobalDatabase>(json);
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);

				return null;
			}

			// ReSharper disable once ConditionIsAlwaysTrueOrFalse - wrong, "null" json serializes into null object
			if (globalDatabase == null) {
				ASF.ArchiLogger.LogNullError(nameof(globalDatabase));

				return null;
			}

			globalDatabase.FilePath = filePath;

			return globalDatabase;
		}

		internal HashSet<uint> GetPackageIDs(uint appID, IEnumerable<uint> packageIDs) {
			if ((appID == 0) || (packageIDs == null)) {
				throw new ArgumentNullException(nameof(appID) + " || " + nameof(packageIDs));
			}

			HashSet<uint> result = new HashSet<uint>();

			foreach (uint packageID in packageIDs.Where(packageID => packageID != 0)) {
				if (!PackagesData.TryGetValue(packageID, out (uint ChangeNumber, HashSet<uint>? AppIDs) packagesData) || (packagesData.AppIDs?.Contains(appID) != true)) {
					continue;
				}

				result.Add(packageID);
			}

			return result;
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
			if ((bot == null) || (packages == null) || (packages.Count == 0)) {
				throw new ArgumentNullException(nameof(bot) + " || " + nameof(packages));
			}

			await PackagesRefreshSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				HashSet<uint> packageIDs = packages.Where(package => (package.Key != 0) && (!PackagesData.TryGetValue(package.Key, out (uint ChangeNumber, HashSet<uint>? AppIDs) packageData) || (packageData.ChangeNumber < package.Value))).Select(package => package.Key).ToHashSet();

				if (packageIDs.Count == 0) {
					return;
				}

				Dictionary<uint, (uint ChangeNumber, HashSet<uint>? AppIDs)>? packagesData = await bot.GetPackagesData(packageIDs).ConfigureAwait(false);

				if (packagesData == null) {
					bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);

					return;
				}

				bool save = false;

				foreach ((uint packageID, (uint ChangeNumber, HashSet<uint>? AppIDs) packageData) in packagesData) {
					if (PackagesData.TryGetValue(packageID, out (uint ChangeNumber, HashSet<uint>? AppIDs) previousData) && (packageData.ChangeNumber < previousData.ChangeNumber)) {
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

		private async void OnServerListUpdated(object? sender, EventArgs e) => await Save().ConfigureAwait(false);

		// ReSharper disable UnusedMember.Global
		public bool ShouldSerializeCellID() => CellID != 0;
		public bool ShouldSerializePackagesData() => PackagesData.Count > 0;
		public bool ShouldSerializeServerListProvider() => ServerListProvider.ShouldSerializeServerRecords();

		// ReSharper restore UnusedMember.Global
	}
}
