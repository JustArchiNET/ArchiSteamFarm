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
using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
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

	internal string? AccessToken {
		get => BackingAccessToken;

		set {
			if (BackingAccessToken == value) {
				return;
			}

			BackingAccessToken = value;
			Utilities.InBackground(Save);
		}
	}

	[JsonDisallowNull]
	[JsonInclude]
	internal ConcurrentHashSet<uint> FarmingBlacklistAppIDs { get; private init; } = [];

	[JsonDisallowNull]
	[JsonInclude]
	internal ConcurrentHashSet<uint> FarmingPriorityQueueAppIDs { get; private init; } = [];

	[JsonDisallowNull]
	[JsonInclude]
	internal ObservableConcurrentDictionary<uint, DateTime> FarmingRiskyIgnoredAppIDs { get; private init; } = new();

	[JsonDisallowNull]
	[JsonInclude]
	internal ConcurrentHashSet<uint> FarmingRiskyPrioritizedAppIDs { get; private init; } = [];

	[JsonDisallowNull]
	[JsonInclude]
	internal ConcurrentHashSet<uint> MatchActivelyBlacklistAppIDs { get; private init; } = [];

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

	internal string? RefreshToken {
		get => BackingRefreshToken;

		set {
			if (BackingRefreshToken == value) {
				return;
			}

			BackingRefreshToken = value;
			Utilities.InBackground(Save);
		}
	}

	internal string? SteamGuardData {
		get => BackingSteamGuardData;

		set {
			if (BackingSteamGuardData == value) {
				return;
			}

			BackingSteamGuardData = value;
			Utilities.InBackground(Save);
		}
	}

	[JsonDisallowNull]
	[JsonInclude]
	internal ConcurrentHashSet<ulong> TradingBlacklistSteamIDs { get; private init; } = [];

	[JsonInclude]
	private string? BackingAccessToken { get; set; }

	[JsonInclude]
	[JsonPropertyName($"_{nameof(MobileAuthenticator)}")]
	private MobileAuthenticator? BackingMobileAuthenticator { get; set; }

	[JsonInclude]
	private string? BackingRefreshToken { get; set; }

	[JsonInclude]
	private string? BackingSteamGuardData { get; set; }

	[JsonDisallowNull]
	[JsonInclude]
	private OrderedDictionary GamesToRedeemInBackground { get; init; } = new();

	private BotDatabase(string filePath) : this() {
		ArgumentException.ThrowIfNullOrEmpty(filePath);

		FilePath = filePath;
	}

	[JsonConstructor]
	private BotDatabase() {
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
	public bool ShouldSerializeBackingAccessToken() => !string.IsNullOrEmpty(BackingAccessToken);

	[UsedImplicitly]
	public bool ShouldSerializeBackingMobileAuthenticator() => BackingMobileAuthenticator != null;

	[UsedImplicitly]
	public bool ShouldSerializeBackingRefreshToken() => !string.IsNullOrEmpty(BackingRefreshToken);

	[UsedImplicitly]
	public bool ShouldSerializeBackingSteamGuardData() => !string.IsNullOrEmpty(BackingSteamGuardData);

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
	public bool ShouldSerializeTradingBlacklistSteamIDs() => TradingBlacklistSteamIDs.Count > 0;

	protected override void Dispose(bool disposing) {
		if (disposing) {
			// Events we registered
			FarmingBlacklistAppIDs.OnModified -= OnObjectModified;
			FarmingPriorityQueueAppIDs.OnModified -= OnObjectModified;
			FarmingRiskyIgnoredAppIDs.OnModified -= OnObjectModified;
			FarmingRiskyPrioritizedAppIDs.OnModified -= OnObjectModified;
			MatchActivelyBlacklistAppIDs.OnModified -= OnObjectModified;
			TradingBlacklistSteamIDs.OnModified -= OnObjectModified;

			// Those are objects that might be null and the check should be in-place
			BackingMobileAuthenticator?.Dispose();
		}

		// Base dispose
		base.Dispose(disposing);
	}

	protected override Task Save() => Save(this);

	internal void AddGamesToRedeemInBackground(IOrderedDictionary games) {
		if ((games == null) || (games.Count == 0)) {
			throw new ArgumentNullException(nameof(games));
		}

		lock (GamesToRedeemInBackground) {
			foreach (DictionaryEntry game in games) {
				if (!IsValidGameToRedeemInBackground(game)) {
					throw new InvalidOperationException(nameof(game));
				}

				GamesToRedeemInBackground[game.Key] = game.Value;
			}
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
				ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(json)));

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
			foreach (DictionaryEntry game in GamesToRedeemInBackground) {
				return game.Value switch {
					string name => (game.Key as string, name),
					JsonElement { ValueKind: JsonValueKind.String } jsonElement => (game.Key as string, jsonElement.GetString()),
					_ => throw new InvalidOperationException(nameof(game.Value))
				};
			}
		}

		return (null, null);
	}

	internal static bool IsValidGameToRedeemInBackground(DictionaryEntry game) {
		string? key = game.Key as string;

		if (string.IsNullOrEmpty(key) || !Utilities.IsValidCdKey(key)) {
			return false;
		}

		switch (game.Value) {
			case string name when !string.IsNullOrEmpty(name):
			case JsonElement { ValueKind: JsonValueKind.String } jsonElement when !string.IsNullOrEmpty(jsonElement.GetString()):
				return true;
			default:
				return false;
		}
	}

	internal void PerformMaintenance() {
		DateTime now = DateTime.UtcNow;

		foreach (uint appID in FarmingRiskyIgnoredAppIDs.Where(entry => entry.Value < now).Select(static entry => entry.Key)) {
			FarmingRiskyIgnoredAppIDs.Remove(appID);
		}
	}

	internal void RemoveGameToRedeemInBackground(string key) {
		ArgumentException.ThrowIfNullOrEmpty(key);

		lock (GamesToRedeemInBackground) {
			if (!GamesToRedeemInBackground.Contains(key)) {
				return;
			}

			GamesToRedeemInBackground.Remove(key);
		}

		Utilities.InBackground(Save);
	}

	private (bool Valid, string? ErrorMessage) CheckValidation() => GamesToRedeemInBackground.Cast<DictionaryEntry>().Any(static game => !IsValidGameToRedeemInBackground(game)) ? (false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorConfigPropertyInvalid, nameof(GamesToRedeemInBackground), string.Join("", GamesToRedeemInBackground))) : (true, null);

	private async void OnObjectModified(object? sender, EventArgs e) {
		if (string.IsNullOrEmpty(FilePath)) {
			return;
		}

		await Save().ConfigureAwait(false);
	}
}
