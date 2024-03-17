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
using System.Text.Json.Serialization;
using ArchiSteamFarm.Steam.Data;

namespace ArchiSteamFarm.OfficialPlugins.ItemsMatcher.Data;

internal class AssetForMatching {
	[JsonInclude]
	[JsonPropertyName("a")]
	[JsonRequired]
	internal uint Amount { get; set; }

	[JsonInclude]
	[JsonPropertyName("c")]
	[JsonRequired]
	internal ulong ClassID { get; private init; }

	[JsonInclude]
	[JsonPropertyName("r")]
	[JsonRequired]
	internal EAssetRarity Rarity { get; private init; }

	[JsonInclude]
	[JsonPropertyName("e")]
	[JsonRequired]
	internal uint RealAppID { get; private init; }

	[JsonInclude]
	[JsonPropertyName("t")]
	[JsonRequired]
	internal bool Tradable { get; private init; }

	[JsonInclude]
	[JsonPropertyName("p")]
	[JsonRequired]
	internal EAssetType Type { get; private init; }

	[JsonConstructor]
	protected AssetForMatching() { }

	internal AssetForMatching(Asset asset) {
		ArgumentNullException.ThrowIfNull(asset);

		Amount = asset.Amount;

		ClassID = asset.ClassID;
		Tradable = asset.Tradable;

		RealAppID = asset.RealAppID;
		Type = asset.Type;
		Rarity = asset.Rarity;
	}
}
