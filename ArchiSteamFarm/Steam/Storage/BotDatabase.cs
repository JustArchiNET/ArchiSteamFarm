// ----------------------------------------------------------------------------------------------
//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// ----------------------------------------------------------------------------------------------
// |
// Copyright 2015-2025 Łukasz "JustArchi" Domeradzki
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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ArchiSteamFarm.Collections;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers.Json;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Steam.Security;
using ArchiSteamFarm.Storage;
using JetBrains.Annotations;

namespace ArchiSteamFarm.Steam.Storage;

public sealed class BotDatabase : GenericDatabase {
	internal uint GamesToRedeemInBackgroundCount {
		get {
			lock (GamesToRedeemInBackground) {
				return (uint) GamesToRedeemInBackground.Count;
			}
		}
	}

	internal bool HasGamesToRedeemInBackground => GamesToRedeemInBackgroundCount > 0;

	[JsonInclude]
	[JsonPropertyName("BackingTradeRestrictionsAcknowledged")]
	[PublicAPI]
	public bool TradeRestrictionsAcknowledged {
		get;

		internal set {
			if (field == value) {
				return;
			}

			field = value;
			Utilities.InBackground(Save);
		}
	}

	[JsonInclude]
	[JsonPropertyName("BackingAccessToken")]
	internal string? AccessToken {
		get;

		set {
			if (field == value) {
				return;
			}

			field = value;
			Utilities.InBackground(Save);
		}
	}

	[JsonInclude]
	internal string? CachedSteamParentalCode {
		get;

		set {
			if (field == value) {
				return;
			}

			field = value;
			Utilities.InBackground(Save);
		}
	}

	[JsonDisallowNull]
	[JsonInclude]
	[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
	internal ConcurrentHashSet<uint> ExtraStorePackages { get; private init; } = [];

	[JsonInclude]
	[JsonPropertyName("BackingExtraStorePackagesRefreshedAt")]
	internal DateTime ExtraStorePackagesRefreshedAt {
		get;

		set {
			if (field == value) {
				return;
			}

			field = value;
			Utilities.InBackground(Save);
		}
	}

	[JsonDisallowNull]
	[JsonInclude]
	[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
	internal ConcurrentHashSet<uint> FarmingBlacklistAppIDs { get; private init; } = [];

	[JsonDisallowNull]
	[JsonInclude]
	[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
	internal ConcurrentHashSet<uint> FarmingPriorityQueueAppIDs { get; private init; } = [];

	[JsonDisallowNull]
	[JsonInclude]
	[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
	internal ObservableConcurrentDictionary<uint, DateTime> FarmingRiskyIgnoredAppIDs { get; private init; } = new();

	[JsonDisallowNull]
	[JsonInclude]
	[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
	internal ConcurrentHashSet<uint> FarmingRiskyPrioritizedAppIDs { get; private init; } = [];

	[JsonDisallowNull]
	[JsonInclude]
	[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
	internal ConcurrentHashSet<uint> MatchActivelyBlacklistAppIDs { get; private init; } = [];

	[JsonInclude]
	[JsonPropertyName($"_{nameof(MobileAuthenticator)}")]
	internal MobileAuthenticator? MobileAuthenticator {
		get;

		set {
			if (field == value) {
				return;
			}

			field = value;
			Utilities.InBackground(Save);
		}
	}

	[JsonInclude]
	[JsonPropertyName("BackingRefreshToken")]
	internal string? RefreshToken {
		get;

		set {
			if (field == value) {
				return;
			}

			field = value;
			Utilities.InBackground(Save);
		}
	}

	[JsonInclude]
	[JsonPropertyName("BackingSteamGuardData")]
	internal string? SteamGuardData {
		get;

		set {
			if (field == value) {
				return;
			}

			field = value;
			Utilities.InBackground(Save);
		}
	}

	[JsonDisallowNull]
	[JsonInclude]
	[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
	internal ConcurrentHashSet<ulong> TradingBlacklistSteamIDs { get; private init; } = [];

	[JsonDisallowNull]
	[JsonInclude]
	[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
	private OrderedDictionary<string, string> GamesToRedeemInBackground { get; init; } = new(StringComparer.OrdinalIgnoreCase);

	private BotDatabase(string filePath) : this() {
		ArgumentException.ThrowIfNullOrEmpty(filePath);

		FilePath = filePath;
	}

	[JsonConstructor]
	private BotDatabase() {
		ExtraStorePackages.OnModified += OnObjectModified;
		FarmingBlacklistAppIDs.OnModified += OnObjectModified;
		FarmingPriorityQueueAppIDs.OnModified += OnObjectModified;
		FarmingRiskyIgnoredAppIDs.OnModified += OnObjectModified;
		FarmingRiskyPrioritizedAppIDs.OnModified += OnObjectModified;
		MatchActivelyBlacklistAppIDs.OnModified += OnObjectModified;
		TradingBlacklistSteamIDs.OnModified += OnObjectModified;
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
	public bool ShouldSerializeAccessToken() => !string.IsNullOrEmpty(AccessToken);

	[UsedImplicitly]
	public bool ShouldSerializeCachedSteamParentalCode() => !string.IsNullOrEmpty(CachedSteamParentalCode);

	[UsedImplicitly]
	public bool ShouldSerializeExtraStorePackages() => ExtraStorePackages.Count > 0;

	[UsedImplicitly]
	public bool ShouldSerializeExtraStorePackagesRefreshedAt() => ExtraStorePackagesRefreshedAt > DateTime.MinValue;

	[UsedImplicitly]
	public bool ShouldSerializeFarmingBlacklistAppIDs() => FarmingBlacklistAppIDs.Count > 0;

	[UsedImplicitly]
	public bool ShouldSerializeFarmingPriorityQueueAppIDs() => FarmingPriorityQueueAppIDs.Count > 0;

	[UsedImplicitly]
	public bool ShouldSerializeFarmingRiskyIgnoredAppIDs() => !FarmingRiskyIgnoredAppIDs.IsEmpty;

	[UsedImplicitly]
	public bool ShouldSerializeFarmingRiskyPrioritizedAppIDs() => FarmingRiskyPrioritizedAppIDs.Count > 0;

	[UsedImplicitly]
	public bool ShouldSerializeGamesToRedeemInBackground() => HasGamesToRedeemInBackground;

	[UsedImplicitly]
	public bool ShouldSerializeMatchActivelyBlacklistAppIDs() => MatchActivelyBlacklistAppIDs.Count > 0;

	[UsedImplicitly]
	public bool ShouldSerializeMobileAuthenticator() => MobileAuthenticator != null;

	[UsedImplicitly]
	public bool ShouldSerializeRefreshToken() => !string.IsNullOrEmpty(RefreshToken);

	[UsedImplicitly]
	public bool ShouldSerializeSteamGuardData() => !string.IsNullOrEmpty(SteamGuardData);

	[UsedImplicitly]
	public bool ShouldSerializeTradeRestrictionsAcknowledged() => TradeRestrictionsAcknowledged;

	[UsedImplicitly]
	public bool ShouldSerializeTradingBlacklistSteamIDs() => TradingBlacklistSteamIDs.Count > 0;

	protected override void Dispose(bool disposing) {
		if (disposing) {
			// Events we registered
			ExtraStorePackages.OnModified -= OnObjectModified;
			FarmingBlacklistAppIDs.OnModified -= OnObjectModified;
			FarmingPriorityQueueAppIDs.OnModified -= OnObjectModified;
			FarmingRiskyIgnoredAppIDs.OnModified -= OnObjectModified;
			FarmingRiskyPrioritizedAppIDs.OnModified -= OnObjectModified;
			MatchActivelyBlacklistAppIDs.OnModified -= OnObjectModified;
			TradingBlacklistSteamIDs.OnModified -= OnObjectModified;

			// Those are objects that might be null and the check should be in-place
			MobileAuthenticator?.Dispose();
		}

		// Base dispose
		base.Dispose(disposing);
	}

	protected override Task Save() => Save(this);

	internal void AddGamesToRedeemInBackground(IReadOnlyDictionary<string, string> games) {
		if ((games == null) || (games.Count == 0)) {
			throw new ArgumentNullException(nameof(games));
		}

		lock (GamesToRedeemInBackground) {
			foreach ((string key, string name) in games) {
				if (!IsValidGameToRedeemInBackground(key, name)) {
					throw new InvalidOperationException(nameof(IsValidGameToRedeemInBackground));
				}

				GamesToRedeemInBackground[key] = name;
			}
		}

		Utilities.InBackground(Save);
	}

	internal void ClearGamesToRedeemInBackground() {
		lock (GamesToRedeemInBackground) {
			if (GamesToRedeemInBackground.Count == 0) {
				return;
			}

			GamesToRedeemInBackground.Clear();
		}

		Utilities.InBackground(Save);
	}

	internal static async Task<BotDatabase?> CreateOrLoad(string filePath) {
		ArgumentException.ThrowIfNullOrEmpty(filePath);

		if (!File.Exists(filePath)) {
			return new BotDatabase(filePath);
		}

		BotDatabase? botDatabase;

		try {
			string json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);

			if (string.IsNullOrEmpty(json)) {
				ASF.ArchiLogger.LogGenericError(Strings.FormatErrorIsEmpty(nameof(json)));

				return null;
			}

			botDatabase = json.ToJsonObject<BotDatabase>();
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);

			return null;
		}

		if (botDatabase == null) {
			ASF.ArchiLogger.LogNullError(botDatabase);

			return null;
		}

		(bool valid, string? errorMessage) = botDatabase.CheckValidation();

		if (!valid) {
			if (!string.IsNullOrEmpty(errorMessage)) {
				ASF.ArchiLogger.LogGenericError(errorMessage);
			}

			return null;
		}

		botDatabase.FilePath = filePath;

		return botDatabase;
	}

	internal (string? Key, string? Name) GetGameToRedeemInBackground() {
		lock (GamesToRedeemInBackground) {
			foreach ((string key, string name) in GamesToRedeemInBackground) {
				return (key, name);
			}
		}

		return (null, null);
	}

	internal static bool IsValidGameToRedeemInBackground(string key, string name) => !string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(name) && Utilities.IsValidCdKey(key);

	internal void PerformMaintenance() {
		DateTime now = DateTime.UtcNow;

		foreach (uint appID in FarmingRiskyIgnoredAppIDs.Where(entry => entry.Value < now).Select(static entry => entry.Key)) {
			FarmingRiskyIgnoredAppIDs.Remove(appID);
		}
	}

	internal void RemoveGameToRedeemInBackground(string key) {
		ArgumentException.ThrowIfNullOrEmpty(key);

		lock (GamesToRedeemInBackground) {
			if (!GamesToRedeemInBackground.Remove(key)) {
				return;
			}
		}

		Utilities.InBackground(Save);
	}

	private (bool Valid, string? ErrorMessage) CheckValidation() => GamesToRedeemInBackground.Any(static entry => !IsValidGameToRedeemInBackground(entry.Key, entry.Value)) ? (false, Strings.FormatErrorConfigPropertyInvalid(nameof(GamesToRedeemInBackground), string.Join("", GamesToRedeemInBackground))) : (true, null);

	private async void OnObjectModified(object? sender, EventArgs e) {
		if (string.IsNullOrEmpty(FilePath)) {
			return;
		}

		await Save().ConfigureAwait(false);
	}
}
