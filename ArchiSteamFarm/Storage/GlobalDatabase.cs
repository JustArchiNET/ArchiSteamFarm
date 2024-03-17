// ----------------------------------------------------------------------------------------------
//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// ----------------------------------------------------------------------------------------------
// |
// Copyright 2015-2024 Łukasz "JustArchi" Domeradzki
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
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Collections;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers.Json;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.SteamKit2;
using JetBrains.Annotations;

namespace ArchiSteamFarm.Storage;

public sealed class GlobalDatabase : GenericDatabase {
	[JsonIgnore]
	[PublicAPI]
	public IReadOnlyDictionary<uint, ulong> PackageAccessTokensReadOnly => PackagesAccessTokens;

	[JsonIgnore]
	[PublicAPI]
	public IReadOnlyDictionary<uint, PackageData> PackagesDataReadOnly => PackagesData;

	private readonly SemaphoreSlim PackagesRefreshSemaphore = new(1, 1);

	[JsonInclude]
	[PublicAPI]
	public Guid Identifier { get; private init; } = Guid.NewGuid();

	[JsonDisallowNull]
	[JsonInclude]
	internal ConcurrentHashSet<ulong> CachedBadBots { get; private init; } = [];

	[JsonDisallowNull]
	[JsonInclude]
	internal ObservableConcurrentDictionary<uint, byte> CardCountsPerGame { get; private init; } = new();

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

	[JsonDisallowNull]
	[JsonInclude]
	internal InMemoryServerListProvider ServerListProvider { get; private init; } = new();

	[JsonInclude]
	[JsonPropertyName($"_{nameof(CellID)}")]
	private uint BackingCellID { get; set; }

	[JsonInclude]
	[JsonPropertyName($"_{nameof(LastChangeNumber)}")]
	private uint BackingLastChangeNumber { get; set; }

	[JsonDisallowNull]
	[JsonInclude]
	private ConcurrentDictionary<uint, ulong> PackagesAccessTokens { get; init; } = new();

	[JsonDisallowNull]
	[JsonInclude]
	private ConcurrentDictionary<uint, PackageData> PackagesData { get; init; } = new();

	private GlobalDatabase(string filePath) : this() {
		ArgumentException.ThrowIfNullOrEmpty(filePath);

		FilePath = filePath;
	}

	[JsonConstructor]
	private GlobalDatabase() {
		CachedBadBots.OnModified += OnObjectModified;
		CardCountsPerGame.OnModified += OnObjectModified;
		ServerListProvider.ServerListUpdated += OnObjectModified;
	}

	[PublicAPI]
	public void DeleteFromJsonStorage(string key) {
		ArgumentException.ThrowIfNullOrEmpty(key);

		DeleteFromJsonStorage(this, key);
	}

	[PublicAPI]
	public void SaveToJsonStorage<T>(string key, T value) where T : notnull {
		ArgumentException.ThrowIfNullOrEmpty(key);
		ArgumentNullException.ThrowIfNull(value);

		SaveToJsonStorage(this, key, value);
	}

	[PublicAPI]
	public void SaveToJsonStorage(string key, JsonElement value) {
		ArgumentException.ThrowIfNullOrEmpty(key);

		if (value.ValueKind == JsonValueKind.Undefined) {
			throw new ArgumentOutOfRangeException(nameof(value));
		}

		SaveToJsonStorage(this, key, value);
	}

	[UsedImplicitly]
	public bool ShouldSerializeBackingCellID() => BackingCellID != 0;

	[UsedImplicitly]
	public bool ShouldSerializeBackingLastChangeNumber() => BackingLastChangeNumber != 0;

	[UsedImplicitly]
	public bool ShouldSerializeCachedBadBots() => CachedBadBots.Count > 0;

	[UsedImplicitly]
	public bool ShouldSerializeCardCountsPerGame() => !CardCountsPerGame.IsEmpty;

	[UsedImplicitly]
	public bool ShouldSerializePackagesAccessTokens() => !PackagesAccessTokens.IsEmpty;

	[UsedImplicitly]
	public bool ShouldSerializePackagesData() => !PackagesData.IsEmpty;

	[UsedImplicitly]
	public bool ShouldSerializeServerListProvider() => ServerListProvider.ShouldSerializeServerRecords();

	protected override void Dispose(bool disposing) {
		if (disposing) {
			// Events we registered
			CachedBadBots.OnModified -= OnObjectModified;
			CardCountsPerGame.OnModified -= OnObjectModified;
			ServerListProvider.ServerListUpdated -= OnObjectModified;

			// Those are objects that are always being created if constructor doesn't throw exception
			PackagesRefreshSemaphore.Dispose();
		}

		// Base dispose
		base.Dispose(disposing);
	}

	protected override Task Save() => Save(this);

	internal static async Task<GlobalDatabase?> CreateOrLoad(string filePath) {
		ArgumentException.ThrowIfNullOrEmpty(filePath);

		if (!File.Exists(filePath)) {
			GlobalDatabase result = new(filePath);

			Utilities.InBackground(() => Save(result));

			return result;
		}

		GlobalDatabase? globalDatabase;

		try {
			string json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);

			if (string.IsNullOrEmpty(json)) {
				ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(json)));

				return null;
			}

			globalDatabase = json.ToJsonObject<GlobalDatabase>();
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);

			return null;
		}

		if (globalDatabase == null) {
			ASF.ArchiLogger.LogNullError(globalDatabase);

			return null;
		}

		globalDatabase.FilePath = filePath;

		return globalDatabase;
	}

	internal HashSet<uint> GetPackageIDs(uint appID, IEnumerable<uint> packageIDs, int limit = int.MaxValue) {
		ArgumentOutOfRangeException.ThrowIfZero(appID);
		ArgumentNullException.ThrowIfNull(packageIDs);

		HashSet<uint> result = [];

		foreach (uint packageID in packageIDs.Where(static packageID => packageID != 0)) {
			if (!PackagesData.TryGetValue(packageID, out PackageData? packageEntry) || (packageEntry.AppIDs?.Contains(appID) != true)) {
				continue;
			}

			result.Add(packageID);

			if (result.Count >= limit) {
				return result;
			}
		}

		return result;
	}

	internal async Task OnPICSChangesRestart(uint currentChangeNumber) {
		ArgumentOutOfRangeException.ThrowIfZero(currentChangeNumber);

		if (Bot.Bots == null) {
			throw new InvalidOperationException(nameof(Bot.Bots));
		}

		if (currentChangeNumber <= LastChangeNumber) {
			return;
		}

		LastChangeNumber = currentChangeNumber;

		Bot? refreshBot = Bot.Bots.Values.FirstOrDefault(static bot => bot.IsConnectedAndLoggedOn);

		if (refreshBot == null) {
			return;
		}

		if (PackagesData.IsEmpty) {
			return;
		}

		Dictionary<uint, uint> packageIDs = PackagesData.Keys.ToDictionary(static packageID => packageID, _ => currentChangeNumber);

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
		ArgumentNullException.ThrowIfNull(bot);

		if ((packages == null) || (packages.Count == 0)) {
			throw new ArgumentNullException(nameof(packages));
		}

		await PackagesRefreshSemaphore.WaitAsync().ConfigureAwait(false);

		try {
			DateTime now = DateTime.UtcNow;

			HashSet<uint> packageIDs = packages.Where(package => (package.Key != 0) && (!PackagesData.TryGetValue(package.Key, out PackageData? previousData) || (previousData.ChangeNumber < package.Value) || (previousData.ValidUntil < now))).Select(static package => package.Key).ToHashSet();

			if (packageIDs.Count == 0) {
				return;
			}

			Dictionary<uint, PackageData>? packagesData = await bot.GetPackagesData(packageIDs).ConfigureAwait(false);

			if (packagesData == null) {
				bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);

				return;
			}

			foreach ((uint packageID, PackageData packageData) in packagesData) {
				PackagesData[packageID] = packageData;
			}

			Utilities.InBackground(Save);
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
