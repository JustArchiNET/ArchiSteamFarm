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
using System.Globalization;
using System.IO;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ArchiSteamFarm.Collections;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers;
using ArchiSteamFarm.Helpers.Json;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.OfficialPlugins.ItemsMatcher.Data;
using JetBrains.Annotations;

namespace ArchiSteamFarm.OfficialPlugins.ItemsMatcher;

internal sealed class BotCache : SerializableFile {
	[JsonDisallowNull]
	[JsonInclude]
	internal ConcurrentList<AssetForListing> LastAnnouncedAssetsForListing { get; private init; } = [];

	internal string? LastAnnouncedTradeToken {
		get => BackingLastAnnouncedTradeToken;

		set {
			if (BackingLastAnnouncedTradeToken == value) {
				return;
			}

			BackingLastAnnouncedTradeToken = value;
			Utilities.InBackground(Save);
		}
	}

	internal string? LastInventoryChecksumBeforeDeduplication {
		get => BackingLastInventoryChecksumBeforeDeduplication;

		set {
			if (BackingLastInventoryChecksumBeforeDeduplication == value) {
				return;
			}

			BackingLastInventoryChecksumBeforeDeduplication = value;
			Utilities.InBackground(Save);
		}
	}

	internal DateTime? LastRequestAt {
		get => BackingLastRequestAt;

		set {
			if (BackingLastRequestAt == value) {
				return;
			}

			BackingLastRequestAt = value;
			Utilities.InBackground(Save);
		}
	}

	[JsonInclude]
	private string? BackingLastAnnouncedTradeToken { get; set; }

	[JsonInclude]
	private string? BackingLastInventoryChecksumBeforeDeduplication { get; set; }

	[JsonInclude]
	private DateTime? BackingLastRequestAt { get; set; }

	private BotCache(string filePath) : this() {
		ArgumentException.ThrowIfNullOrEmpty(filePath);

		FilePath = filePath;
	}

	[JsonConstructor]
	private BotCache() => LastAnnouncedAssetsForListing.OnModified += OnObjectModified;

	[UsedImplicitly]
	public bool ShouldSerializeBackingLastAnnouncedTradeToken() => !string.IsNullOrEmpty(BackingLastAnnouncedTradeToken);

	[UsedImplicitly]
	public bool ShouldSerializeBackingLastInventoryChecksumBeforeDeduplication() => !string.IsNullOrEmpty(BackingLastInventoryChecksumBeforeDeduplication);

	[UsedImplicitly]
	public bool ShouldSerializeBackingLastRequestAt() => BackingLastRequestAt.HasValue;

	[UsedImplicitly]
	public bool ShouldSerializeLastAnnouncedAssetsForListing() => LastAnnouncedAssetsForListing.Count > 0;

	protected override void Dispose(bool disposing) {
		if (disposing) {
			// Events we registered
			LastAnnouncedAssetsForListing.OnModified -= OnObjectModified;
		}

		// Base dispose
		base.Dispose(disposing);
	}

	protected override Task Save() => Save(this);

	internal static async Task<BotCache> CreateOrLoad(string filePath) {
		ArgumentException.ThrowIfNullOrEmpty(filePath);

		if (!File.Exists(filePath)) {
			return new BotCache(filePath);
		}

		BotCache? botCache;

		try {
			string json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);

			if (string.IsNullOrEmpty(json)) {
				ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(json)));

				return new BotCache(filePath);
			}

			botCache = json.ToJsonObject<BotCache>();
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);

			return new BotCache(filePath);
		}

		if (botCache == null) {
			ASF.ArchiLogger.LogNullError(botCache);

			return new BotCache(filePath);
		}

		botCache.FilePath = filePath;

		return botCache;
	}

	private async void OnObjectModified(object? sender, EventArgs e) {
		if (string.IsNullOrEmpty(FilePath)) {
			return;
		}

		await Save().ConfigureAwait(false);
	}
}
