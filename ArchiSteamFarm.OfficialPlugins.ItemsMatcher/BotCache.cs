//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
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
using System.Threading.Tasks;
using ArchiSteamFarm.Collections;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.OfficialPlugins.ItemsMatcher.Data;
using JetBrains.Annotations;
using Newtonsoft.Json;

namespace ArchiSteamFarm.OfficialPlugins.ItemsMatcher;

internal sealed class BotCache : SerializableFile {
	[JsonProperty(Required = Required.DisallowNull)]
	internal readonly ConcurrentList<AssetForListing> LastAnnouncedAssetsForListing = [];

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

	[JsonProperty]
	private string? BackingLastAnnouncedTradeToken;

	[JsonProperty]
	private string? BackingLastInventoryChecksumBeforeDeduplication;

	[JsonProperty]
	private DateTime? BackingLastRequestAt;

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

			botCache = JsonConvert.DeserializeObject<BotCache>(json);
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
