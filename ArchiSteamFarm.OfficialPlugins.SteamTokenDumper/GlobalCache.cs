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
using System.Threading.Tasks;
using ArchiSteamFarm.Helpers;
using Newtonsoft.Json;
using SteamKit2;

namespace ArchiSteamFarm.OfficialPlugins.SteamTokenDumper {
	internal sealed class GlobalCache : SerializableFile {
		private static string SharedFilePath => Path.Combine(ArchiSteamFarm.SharedInfo.ConfigDirectory, nameof(SteamTokenDumper) + ".cache");

		[JsonProperty(Required = Required.DisallowNull)]
		private readonly ConcurrentDictionary<uint, uint> AppChangeNumbers = new();

		[JsonProperty(Required = Required.DisallowNull)]
		private readonly ConcurrentDictionary<uint, ulong> AppTokens = new();

		[JsonProperty(Required = Required.DisallowNull)]
		private readonly ConcurrentDictionary<uint, string> DepotKeys = new();

		[JsonProperty(Required = Required.DisallowNull)]
		private readonly ConcurrentDictionary<uint, ulong> PackageTokens = new();

		[JsonProperty(Required = Required.DisallowNull)]
		private readonly ConcurrentDictionary<uint, ulong> SubmittedApps = new();

		[JsonProperty(Required = Required.DisallowNull)]
		private readonly ConcurrentDictionary<uint, string> SubmittedDepots = new();

		[JsonProperty(Required = Required.DisallowNull)]
		private readonly ConcurrentDictionary<uint, ulong> SubmittedPackages = new();

		[JsonProperty(Required = Required.DisallowNull)]
		internal uint LastChangeNumber { get; private set; }

		internal GlobalCache() => FilePath = SharedFilePath;

		internal ulong GetAppToken(uint appID) => AppTokens[appID];

		internal Dictionary<uint, ulong> GetAppTokensForSubmission() => AppTokens.Where(appToken => (appToken.Value > 0) && (!SubmittedApps.TryGetValue(appToken.Key, out ulong token) || (appToken.Value != token))).ToDictionary(appToken => appToken.Key, appToken => appToken.Value);
		internal Dictionary<uint, string> GetDepotKeysForSubmission() => DepotKeys.Where(depotKey => !string.IsNullOrEmpty(depotKey.Value) && (!SubmittedDepots.TryGetValue(depotKey.Key, out string? key) || (depotKey.Value != key))).ToDictionary(depotKey => depotKey.Key, depotKey => depotKey.Value);
		internal Dictionary<uint, ulong> GetPackageTokensForSubmission() => PackageTokens.Where(packageToken => (packageToken.Value > 0) && (!SubmittedPackages.TryGetValue(packageToken.Key, out ulong token) || (packageToken.Value != token))).ToDictionary(packageToken => packageToken.Key, packageToken => packageToken.Value);

		internal static async Task<GlobalCache> Load() {
			if (!File.Exists(SharedFilePath)) {
				return new GlobalCache();
			}

			GlobalCache? globalCache = null;

			try {
				string json = await RuntimeCompatibility.File.ReadAllTextAsync(SharedFilePath).ConfigureAwait(false);

				if (!string.IsNullOrEmpty(json)) {
					globalCache = JsonConvert.DeserializeObject<GlobalCache>(json);
				}
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);
			}

			if (globalCache == null) {
				ASF.ArchiLogger.LogGenericError($"{nameof(GlobalCache)} could not be loaded, a fresh instance will be initialized.");

				globalCache = new GlobalCache();
			}

			return globalCache;
		}

		internal async Task OnPICSChanges(uint currentChangeNumber, IReadOnlyCollection<KeyValuePair<uint, SteamApps.PICSChangesCallback.PICSChangeData>> appChanges) {
			if (currentChangeNumber == 0) {
				throw new ArgumentOutOfRangeException(nameof(currentChangeNumber));
			}

			if (appChanges == null) {
				throw new ArgumentNullException(nameof(appChanges));
			}

			if (currentChangeNumber <= LastChangeNumber) {
				return;
			}

			ASF.ArchiLogger.LogGenericTrace($"{LastChangeNumber} => {currentChangeNumber}");

			LastChangeNumber = currentChangeNumber;

			foreach ((uint appID, SteamApps.PICSChangesCallback.PICSChangeData appData) in appChanges) {
				if (!AppChangeNumbers.TryGetValue(appID, out uint previousChangeNumber) || (appData.ChangeNumber <= previousChangeNumber)) {
					continue;
				}

				AppChangeNumbers.TryRemove(appID, out _);
				ASF.ArchiLogger.LogGenericTrace($"App needs refresh: {appID}");
			}

			await Save().ConfigureAwait(false);
		}

		internal async Task OnPICSChangesRestart(uint currentChangeNumber) {
			if (currentChangeNumber == 0) {
				throw new ArgumentOutOfRangeException(nameof(currentChangeNumber));
			}

			if (currentChangeNumber <= LastChangeNumber) {
				return;
			}

			ASF.ArchiLogger.LogGenericDebug($"RESET {LastChangeNumber} => {currentChangeNumber}");

			LastChangeNumber = currentChangeNumber;

			AppChangeNumbers.Clear();

			await Save().ConfigureAwait(false);
		}

		internal bool ShouldRefreshAppInfo(uint appID) => !AppChangeNumbers.ContainsKey(appID);
		internal bool ShouldRefreshDepotKey(uint depotID) => !DepotKeys.ContainsKey(depotID);

		internal async Task UpdateAppChangeNumbers(IReadOnlyCollection<KeyValuePair<uint, uint>> appChangeNumbers) {
			if (appChangeNumbers == null) {
				throw new ArgumentNullException(nameof(appChangeNumbers));
			}

			bool save = false;

			foreach ((uint appID, uint changeNumber) in appChangeNumbers) {
				if (AppChangeNumbers.TryGetValue(appID, out uint previousChangeNumber) && (previousChangeNumber == changeNumber)) {
					continue;
				}

				AppChangeNumbers[appID] = changeNumber;
				save = true;
			}

			if (save) {
				await Save().ConfigureAwait(false);
			}
		}

		internal async Task UpdateAppTokens(IReadOnlyCollection<KeyValuePair<uint, ulong>> appTokens, IReadOnlyCollection<uint> publicAppIDs) {
			if (appTokens == null) {
				throw new ArgumentNullException(nameof(appTokens));
			}

			if (publicAppIDs == null) {
				throw new ArgumentNullException(nameof(publicAppIDs));
			}

			bool save = false;

			foreach ((uint appID, ulong appToken) in appTokens) {
				if (AppTokens.TryGetValue(appID, out ulong previousAppToken) && (previousAppToken == appToken)) {
					continue;
				}

				AppTokens[appID] = appToken;
				save = true;
			}

			foreach (uint appID in publicAppIDs) {
				if (AppTokens.TryGetValue(appID, out ulong previousAppToken) && (previousAppToken == 0)) {
					continue;
				}

				AppTokens[appID] = 0;
				save = true;
			}

			if (save) {
				await Save().ConfigureAwait(false);
			}
		}

		internal async Task UpdateDepotKeys(ICollection<SteamApps.DepotKeyCallback> depotKeyResults) {
			if (depotKeyResults == null) {
				throw new ArgumentNullException(nameof(depotKeyResults));
			}

			bool save = false;

			foreach (SteamApps.DepotKeyCallback depotKeyResult in depotKeyResults) {
				if (depotKeyResult?.Result != EResult.OK) {
					continue;
				}

				string depotKey = BitConverter.ToString(depotKeyResult.DepotKey).Replace("-", "");

				if (DepotKeys.TryGetValue(depotKeyResult.DepotID, out string? previousDepotKey) && (previousDepotKey == depotKey)) {
					continue;
				}

				DepotKeys[depotKeyResult.DepotID] = depotKey;
				save = true;
			}

			if (save) {
				await Save().ConfigureAwait(false);
			}
		}

		internal async Task UpdatePackageTokens(IReadOnlyCollection<KeyValuePair<uint, ulong>> packageTokens) {
			if (packageTokens == null) {
				throw new ArgumentNullException(nameof(packageTokens));
			}

			bool save = false;

			foreach ((uint packageID, ulong packageToken) in packageTokens) {
				if (PackageTokens.TryGetValue(packageID, out ulong previousPackageToken) && (previousPackageToken == packageToken)) {
					continue;
				}

				PackageTokens[packageID] = packageToken;
				save = true;
			}

			if (save) {
				await Save().ConfigureAwait(false);
			}
		}

		internal async Task UpdateSubmittedData(IReadOnlyDictionary<uint, ulong> apps, IReadOnlyDictionary<uint, ulong> packages, IReadOnlyDictionary<uint, string> depots) {
			if (apps == null) {
				throw new ArgumentNullException(nameof(apps));
			}

			if (packages == null) {
				throw new ArgumentNullException(nameof(packages));
			}

			if (depots == null) {
				throw new ArgumentNullException(nameof(depots));
			}

			foreach ((uint appID, ulong token) in apps) {
				SubmittedApps[appID] = token;
			}

			foreach ((uint packageID, ulong token) in packages) {
				SubmittedPackages[packageID] = token;
			}

			foreach ((uint depotID, string key) in depots) {
				SubmittedDepots[depotID] = key;
			}

			await Save().ConfigureAwait(false);
		}
	}
}
