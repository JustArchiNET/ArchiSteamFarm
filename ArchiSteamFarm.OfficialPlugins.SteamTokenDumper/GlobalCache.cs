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

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers;
using ArchiSteamFarm.Localization;
using JetBrains.Annotations;
using Newtonsoft.Json;
using SteamKit2;

namespace ArchiSteamFarm.OfficialPlugins.SteamTokenDumper;

internal sealed class GlobalCache : SerializableFile {
	private static string SharedFilePath => Path.Combine(ArchiSteamFarm.SharedInfo.ConfigDirectory, $"{nameof(SteamTokenDumper)}.cache");

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

	[UsedImplicitly]
	public bool ShouldSerializeAppChangeNumbers() => !AppChangeNumbers.IsEmpty;

	[UsedImplicitly]
	public bool ShouldSerializeAppTokens() => !AppTokens.IsEmpty;

	[UsedImplicitly]
	public bool ShouldSerializeDepotKeys() => !DepotKeys.IsEmpty;

	[UsedImplicitly]
	public bool ShouldSerializeLastChangeNumber() => LastChangeNumber > 0;

	[UsedImplicitly]
	public bool ShouldSerializePackageTokens() => !PackageTokens.IsEmpty;

	[UsedImplicitly]
	public bool ShouldSerializeSubmittedApps() => !SubmittedApps.IsEmpty;

	[UsedImplicitly]
	public bool ShouldSerializeSubmittedDepots() => !SubmittedDepots.IsEmpty;

	[UsedImplicitly]
	public bool ShouldSerializeSubmittedPackages() => !SubmittedPackages.IsEmpty;

	internal ulong GetAppToken(uint appID) => AppTokens[appID];

	internal Dictionary<uint, ulong> GetAppTokensForSubmission() => AppTokens.Where(appToken => (SteamTokenDumperPlugin.Config?.SecretAppIDs.Contains(appToken.Key) == false) && (appToken.Value > 0) && (!SubmittedApps.TryGetValue(appToken.Key, out ulong token) || (appToken.Value != token))).ToDictionary(static appToken => appToken.Key, static appToken => appToken.Value);
	internal Dictionary<uint, string> GetDepotKeysForSubmission() => DepotKeys.Where(depotKey => (SteamTokenDumperPlugin.Config?.SecretDepotIDs.Contains(depotKey.Key) == false) && !string.IsNullOrEmpty(depotKey.Value) && (!SubmittedDepots.TryGetValue(depotKey.Key, out string? key) || (depotKey.Value != key))).ToDictionary(static depotKey => depotKey.Key, static depotKey => depotKey.Value);
	internal Dictionary<uint, ulong> GetPackageTokensForSubmission() => PackageTokens.Where(packageToken => (SteamTokenDumperPlugin.Config?.SecretPackageIDs.Contains(packageToken.Key) == false) && (packageToken.Value > 0) && (!SubmittedPackages.TryGetValue(packageToken.Key, out ulong token) || (packageToken.Value != token))).ToDictionary(static packageToken => packageToken.Key, static packageToken => packageToken.Value);

	internal static async Task<GlobalCache?> Load() {
		if (!File.Exists(SharedFilePath)) {
			return new GlobalCache();
		}

		ASF.ArchiLogger.LogGenericInfo(Localization.Strings.LoadingGlobalCache);

		GlobalCache? globalCache;

		try {
			string json = await File.ReadAllTextAsync(SharedFilePath).ConfigureAwait(false);

			if (string.IsNullOrEmpty(json)) {
				ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(json)));

				return null;
			}

			globalCache = JsonConvert.DeserializeObject<GlobalCache>(json);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);

			return null;
		}

		if (globalCache == null) {
			ASF.ArchiLogger.LogNullError(nameof(globalCache));

			return null;
		}

		ASF.ArchiLogger.LogGenericInfo(Localization.Strings.ValidatingGlobalCacheIntegrity);

		if (globalCache.DepotKeys.Values.Any(static depotKey => !IsValidDepotKey(depotKey))) {
			ASF.ArchiLogger.LogGenericError(Localization.Strings.GlobalCacheIntegrityValidationFailed);

			return null;
		}

		return globalCache;
	}

	internal void OnPICSChanges(uint currentChangeNumber, IReadOnlyCollection<KeyValuePair<uint, SteamApps.PICSChangesCallback.PICSChangeData>> appChanges) {
		if (currentChangeNumber == 0) {
			throw new ArgumentOutOfRangeException(nameof(currentChangeNumber));
		}

		ArgumentNullException.ThrowIfNull(appChanges);

		if (currentChangeNumber <= LastChangeNumber) {
			return;
		}

		LastChangeNumber = currentChangeNumber;

		foreach ((uint appID, SteamApps.PICSChangesCallback.PICSChangeData appData) in appChanges) {
			if (!AppChangeNumbers.TryGetValue(appID, out uint previousChangeNumber) || (appData.ChangeNumber <= previousChangeNumber)) {
				continue;
			}

			AppChangeNumbers.TryRemove(appID, out _);
		}

		Utilities.InBackground(Save);
	}

	internal void OnPICSChangesRestart(uint currentChangeNumber) {
		if (currentChangeNumber == 0) {
			throw new ArgumentOutOfRangeException(nameof(currentChangeNumber));
		}

		if (currentChangeNumber <= LastChangeNumber) {
			return;
		}

		LastChangeNumber = currentChangeNumber;
		AppChangeNumbers.Clear();

		Utilities.InBackground(Save);
	}

	internal bool ShouldRefreshAppInfo(uint appID) => !AppChangeNumbers.ContainsKey(appID);
	internal bool ShouldRefreshDepotKey(uint depotID) => !DepotKeys.ContainsKey(depotID);

	internal void UpdateAppChangeNumbers(IReadOnlyCollection<KeyValuePair<uint, uint>> appChangeNumbers) {
		ArgumentNullException.ThrowIfNull(appChangeNumbers);

		bool save = false;

		foreach ((uint appID, uint changeNumber) in appChangeNumbers) {
			if (AppChangeNumbers.TryGetValue(appID, out uint previousChangeNumber) && (previousChangeNumber == changeNumber)) {
				continue;
			}

			AppChangeNumbers[appID] = changeNumber;
			save = true;
		}

		if (save) {
			Utilities.InBackground(Save);
		}
	}

	internal void UpdateAppTokens(IReadOnlyCollection<KeyValuePair<uint, ulong>> appTokens, IReadOnlyCollection<uint> publicAppIDs) {
		ArgumentNullException.ThrowIfNull(appTokens);
		ArgumentNullException.ThrowIfNull(publicAppIDs);

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
			Utilities.InBackground(Save);
		}
	}

	internal void UpdateDepotKeys(ICollection<SteamApps.DepotKeyCallback> depotKeyResults) {
		ArgumentNullException.ThrowIfNull(depotKeyResults);

		bool save = false;

		foreach (SteamApps.DepotKeyCallback depotKeyResult in depotKeyResults) {
			if (depotKeyResult.Result != EResult.OK) {
				continue;
			}

			string depotKey = Convert.ToHexString(depotKeyResult.DepotKey);

			if (!IsValidDepotKey(depotKey)) {
				ASF.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, nameof(depotKey)));

				continue;
			}

			if (DepotKeys.TryGetValue(depotKeyResult.DepotID, out string? previousDepotKey) && (previousDepotKey == depotKey)) {
				continue;
			}

			DepotKeys[depotKeyResult.DepotID] = depotKey;
			save = true;
		}

		if (save) {
			Utilities.InBackground(Save);
		}
	}

	internal void UpdatePackageTokens(IReadOnlyCollection<KeyValuePair<uint, ulong>> packageTokens) {
		ArgumentNullException.ThrowIfNull(packageTokens);

		bool save = false;

		foreach ((uint packageID, ulong packageToken) in packageTokens) {
			if (PackageTokens.TryGetValue(packageID, out ulong previousPackageToken) && (previousPackageToken == packageToken)) {
				continue;
			}

			PackageTokens[packageID] = packageToken;
			save = true;
		}

		if (save) {
			Utilities.InBackground(Save);
		}
	}

	internal void UpdateSubmittedData(IReadOnlyDictionary<uint, ulong> apps, IReadOnlyDictionary<uint, ulong> packages, IReadOnlyDictionary<uint, string> depots) {
		ArgumentNullException.ThrowIfNull(apps);
		ArgumentNullException.ThrowIfNull(packages);
		ArgumentNullException.ThrowIfNull(depots);

		foreach ((uint appID, ulong token) in apps) {
			SubmittedApps[appID] = token;
		}

		foreach ((uint packageID, ulong token) in packages) {
			SubmittedPackages[packageID] = token;
		}

		foreach ((uint depotID, string key) in depots) {
			SubmittedDepots[depotID] = key;
		}

		Utilities.InBackground(Save);
	}

	private static bool IsValidDepotKey(string depotKey) {
		if (string.IsNullOrEmpty(depotKey)) {
			throw new ArgumentNullException(nameof(depotKey));
		}

		return (depotKey.Length == 64) && Utilities.IsValidHexadecimalText(depotKey);
	}
}
