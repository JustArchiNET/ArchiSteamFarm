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
using ArchiSteamFarm.Collections;
using ArchiSteamFarm.Helpers;
using Newtonsoft.Json;
using SteamKit2;

namespace ArchiSteamFarm.OfficialPlugins.SteamTokenDumper {
	internal sealed class GlobalCache : SerializableFile {
		private static string SharedFilePath => Path.Combine(ArchiSteamFarm.SharedInfo.ConfigDirectory, nameof(SteamTokenDumper) + ".cache");

		[JsonProperty(Required = Required.DisallowNull)]
		private readonly ConcurrentDictionary<uint, uint> AppChangeNumbers = new ConcurrentDictionary<uint, uint>();

		[JsonProperty(Required = Required.DisallowNull)]
		private readonly ConcurrentDictionary<uint, ulong> AppTokens = new ConcurrentDictionary<uint, ulong>();

		[JsonProperty(Required = Required.DisallowNull)]
		private readonly ConcurrentDictionary<uint, string> DepotKeys = new ConcurrentDictionary<uint, string>();

		[JsonProperty(Required = Required.DisallowNull)]
		private readonly ConcurrentDictionary<uint, ulong> PackageTokens = new ConcurrentDictionary<uint, ulong>();

		[JsonProperty(Required = Required.DisallowNull)]
		private readonly ConcurrentHashSet<uint> SubmittedAppIDs = new ConcurrentHashSet<uint>();

		[JsonProperty(Required = Required.DisallowNull)]
		private readonly ConcurrentHashSet<uint> SubmittedDepotIDs = new ConcurrentHashSet<uint>();

		[JsonProperty(Required = Required.DisallowNull)]
		private readonly ConcurrentHashSet<uint> SubmittedPackageIDs = new ConcurrentHashSet<uint>();

		[JsonProperty(Required = Required.DisallowNull)]
		internal uint LastChangeNumber { get; private set; }

		internal GlobalCache() => FilePath = SharedFilePath;

		internal ulong GetAppToken(uint appID) => AppTokens[appID];

		internal Dictionary<uint, ulong> GetAppTokensForSubmission() => AppTokens.Where(appToken => !SubmittedAppIDs.Contains(appToken.Key)).ToDictionary(appToken => appToken.Key, appToken => appToken.Value);
		internal Dictionary<uint, string> GetDepotKeysForSubmission() => DepotKeys.Where(depotKey => !SubmittedDepotIDs.Contains(depotKey.Key)).ToDictionary(depotKey => depotKey.Key, depotKey => depotKey.Value);
		internal Dictionary<uint, ulong> GetPackageTokensForSubmission() => PackageTokens.Where(packageToken => !SubmittedPackageIDs.Contains(packageToken.Key)).ToDictionary(packageToken => packageToken.Key, packageToken => packageToken.Value);

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
			if ((currentChangeNumber == 0) || (appChanges == null)) {
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
				throw new ArgumentNullException(nameof(currentChangeNumber));
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
			if ((appTokens == null) || (publicAppIDs == null)) {
				throw new ArgumentNullException(nameof(appTokens) + " || " + nameof(publicAppIDs));
			}

			bool save = false;

			foreach ((uint appID, ulong appToken) in appTokens) {
				if (AppTokens.TryGetValue(appID, out ulong previousAppToken) && (previousAppToken == appToken)) {
					continue;
				}

				AppTokens[appID] = appToken;

				if (appToken == 0) {
					// Backend is not interested in zero access tokens
					SubmittedAppIDs.Add(appID);
				}

				save = true;
			}

			foreach (uint appID in publicAppIDs) {
				if (AppTokens.TryGetValue(appID, out ulong previousAppToken) && (previousAppToken == 0)) {
					continue;
				}

				AppTokens[appID] = 0;

				// Backend is not interested in zero access tokens
				SubmittedAppIDs.Add(appID);

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
				if ((depotKeyResult == null) || (depotKeyResult.Result != EResult.OK)) {
					continue;
				}

				string depotKey = BitConverter.ToString(depotKeyResult.DepotKey).Replace("-", "");

				if (DepotKeys.TryGetValue(depotKeyResult.DepotID, out string? previousDepotKey) && (previousDepotKey == depotKey)) {
					continue;
				}

				DepotKeys[depotKeyResult.DepotID] = depotKey;

				if (string.IsNullOrEmpty(depotKey)) {
					// Backend is not interested in zero depot keys
					SubmittedDepotIDs.Add(depotKeyResult.DepotID);
				}

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

				if (packageToken == 0) {
					// Backend is not interested in zero access tokens
					SubmittedPackageIDs.Add(packageID);
				}

				save = true;
			}

			if (save) {
				await Save().ConfigureAwait(false);
			}
		}

		internal async Task UpdateSubmittedData(IReadOnlyCollection<uint> appIDs, IReadOnlyCollection<uint> packageIDs, IReadOnlyCollection<uint> depotIDs) {
			if ((appIDs == null) || (packageIDs == null) || (depotIDs == null)) {
				throw new ArgumentNullException(nameof(appIDs) + " || " + nameof(packageIDs) + " || " + nameof(depotIDs));
			}

			bool save = false;

			foreach (uint _ in appIDs.Where(appID => SubmittedAppIDs.Add(appID))) {
				save = true;
			}

			foreach (uint _ in packageIDs.Where(packageID => SubmittedPackageIDs.Add(packageID))) {
				save = true;
			}

			foreach (uint _ in depotIDs.Where(depotID => SubmittedDepotIDs.Add(depotID))) {
				save = true;
			}

			if (save) {
				await Save().ConfigureAwait(false);
			}
		}
	}
}
