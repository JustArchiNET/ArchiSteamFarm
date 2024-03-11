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
using System.Collections.Immutable;
using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace ArchiSteamFarm.Steam.Data;

// REF: https://developer.valvesoftware.com/wiki/Steam_Web_API/IEconService#CEcon_Asset
public sealed class Asset {
	[PublicAPI]
	public const uint SteamAppID = 753;

	[PublicAPI]
	public const ulong SteamCommunityContextID = 6;

	[PublicAPI]
	public const ulong SteamPointsShopInstanceID = 3865004543;

	[JsonIgnore]
	[PublicAPI]
	public bool IsSteamPointsShopItem => !Tradable && (InstanceID == SteamPointsShopInstanceID);

	[JsonInclude]
	[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
	[JsonPropertyName("amount")]
	[JsonRequired]
	[PublicAPI]
	public uint Amount { get; internal set; }

	[JsonInclude]
	[JsonPropertyName("appid")]
	public uint AppID { get; private init; }

	[JsonInclude]
	[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
	[JsonPropertyName("assetid")]
	[PublicAPI]
	public ulong AssetID { get; private init; }

	[JsonInclude]
	[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
	[JsonPropertyName("classid")]
	[PublicAPI]
	public ulong ClassID { get; private init; }

	[JsonInclude]
	[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
	[JsonPropertyName("contextid")]
	[PublicAPI]
	public ulong ContextID { get; private init; }

	[JsonInclude]
	[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
	[JsonPropertyName("instanceid")]
	[PublicAPI]
	public ulong InstanceID { get; private init; }

	public InventoryDescription Description { get; internal set; }

	[JsonIgnore]
	[PublicAPI]
	public bool Marketable => Description.Marketable;

	[JsonIgnore]
	[PublicAPI]
	public ERarity Rarity => Description.Rarity;

	[JsonIgnore]
	[PublicAPI]
	public uint RealAppID => Description.RealAppID;

	[JsonIgnore]
	[PublicAPI]
	public ImmutableHashSet<Tag> Tags => Description.Tags;

	[JsonIgnore]
	[PublicAPI]
	public bool Tradable => Description.Tradable;

	[JsonIgnore]
	[PublicAPI]
	public EType Type => Description.Type;

	[JsonInclude]
	[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
	[JsonPropertyName("id")]
	private ulong ID {
		get => AssetID;
		init => AssetID = value;
	}

	// Constructed from trades being received or plugins
	public Asset(uint appID, ulong contextID, ulong classID, uint amount, InventoryDescription description, ulong instanceID = 0, ulong assetID = 0) {
		ArgumentOutOfRangeException.ThrowIfZero(appID);
		ArgumentOutOfRangeException.ThrowIfZero(contextID);
		ArgumentOutOfRangeException.ThrowIfZero(classID);
		ArgumentOutOfRangeException.ThrowIfZero(amount);

		AppID = appID;
		ContextID = contextID;
		ClassID = classID;
		Amount = amount;
		Description = description;
		InstanceID = instanceID;
		AssetID = assetID;
	}

	[JsonConstructor]
	private Asset() { }

	[UsedImplicitly]
	public static bool ShouldSerializeAdditionalProperties() => false;

	internal Asset CreateShallowCopy() => (Asset) MemberwiseClone();

	public enum ERarity : byte {
		Unknown,
		Common,
		Uncommon,
		Rare
	}

	public enum EType : byte {
		Unknown,
		BoosterPack,
		Emoticon,
		FoilTradingCard,
		ProfileBackground,
		TradingCard,
		SteamGems,
		SaleItem,
		Consumable,
		ProfileModifier,
		Sticker,
		ChatEffect,
		MiniProfileBackground,
		AvatarProfileFrame,
		AnimatedAvatar,
		KeyboardSkin,
		StartupVideo
	}
}
