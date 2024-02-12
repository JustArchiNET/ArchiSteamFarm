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
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers.Json;
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

	[UsedImplicitly]
	public static bool ShouldSerializeAdditionalProperties => false;

	[JsonIgnore]
	[PublicAPI]
	public IReadOnlyDictionary<string, JsonElement>? AdditionalPropertiesReadOnly => AdditionalProperties;

	[JsonIgnore]
	[PublicAPI]
	public bool IsSteamPointsShopItem => !Tradable && (InstanceID == SteamPointsShopInstanceID);

	[JsonIgnore]
	[PublicAPI]
	public uint Amount { get; internal set; }

	[JsonDisallowNull]
	[JsonInclude]
	[JsonPropertyName("appid")]
	public uint AppID { get; private init; }

	[JsonIgnore]
	[PublicAPI]
	public ulong AssetID { get; private set; }

	[JsonIgnore]
	[PublicAPI]
	public ulong ClassID { get; private set; }

	[JsonIgnore]
	[PublicAPI]
	public ulong ContextID { get; private set; }

	[JsonIgnore]
	[PublicAPI]
	public ulong InstanceID { get; private set; }

	[JsonIgnore]
	[PublicAPI]
	public bool Marketable { get; internal set; }

	[JsonIgnore]
	[PublicAPI]
	public ERarity Rarity { get; internal set; }

	[JsonIgnore]
	[PublicAPI]
	public uint RealAppID { get; internal set; }

	[JsonIgnore]
	[PublicAPI]
	public ImmutableHashSet<Tag>? Tags { get; internal set; }

	[JsonIgnore]
	[PublicAPI]
	public bool Tradable { get; internal set; }

	[JsonIgnore]
	[PublicAPI]
	public EType Type { get; internal set; }

	[JsonExtensionData]
	internal Dictionary<string, JsonElement>? AdditionalProperties { private get; set; }

	[JsonInclude]
	[JsonPropertyName("amount")]
	[JsonRequired]
	private string AmountText {
		get => Amount.ToString(CultureInfo.InvariantCulture);

		init {
			if (string.IsNullOrEmpty(value)) {
				ASF.ArchiLogger.LogNullError(value);

				return;
			}

			if (!uint.TryParse(value, out uint amount) || (amount == 0)) {
				ASF.ArchiLogger.LogNullError(amount);

				return;
			}

			Amount = amount;
		}
	}

	[JsonDisallowNull]
	[JsonInclude]
	[JsonPropertyName("assetid")]
	private string AssetIDText {
		get => AssetID.ToString(CultureInfo.InvariantCulture);

		init {
			if (string.IsNullOrEmpty(value)) {
				ASF.ArchiLogger.LogNullError(value);

				return;
			}

			if (!ulong.TryParse(value, out ulong assetID) || (assetID == 0)) {
				ASF.ArchiLogger.LogNullError(assetID);

				return;
			}

			AssetID = assetID;
		}
	}

	[JsonDisallowNull]
	[JsonInclude]
	[JsonPropertyName("classid")]
	private string ClassIDText {
		init {
			if (string.IsNullOrEmpty(value)) {
				ASF.ArchiLogger.LogNullError(value);

				return;
			}

			if (!ulong.TryParse(value, out ulong classID) || (classID == 0)) {
				return;
			}

			ClassID = classID;
		}
	}

	[JsonDisallowNull]
	[JsonInclude]
	[JsonPropertyName("contextid")]
	private string ContextIDText {
		get => ContextID.ToString(CultureInfo.InvariantCulture);

		init {
			if (string.IsNullOrEmpty(value)) {
				ASF.ArchiLogger.LogNullError(value);

				return;
			}

			if (!ulong.TryParse(value, out ulong contextID) || (contextID == 0)) {
				ASF.ArchiLogger.LogNullError(contextID);

				return;
			}

			ContextID = contextID;
		}
	}

	[JsonDisallowNull]
	[JsonInclude]
	[JsonPropertyName("id")]
	private string IDText {
		init => AssetIDText = value;
	}

	[JsonDisallowNull]
	[JsonInclude]
	[JsonPropertyName("instanceid")]
	private string InstanceIDText {
		init {
			if (string.IsNullOrEmpty(value)) {
				return;
			}

			if (!ulong.TryParse(value, out ulong instanceID)) {
				ASF.ArchiLogger.LogNullError(instanceID);

				return;
			}

			InstanceID = instanceID;
		}
	}

	// Constructed from trades being received or plugins
	public Asset(uint appID, ulong contextID, ulong classID, uint amount, ulong instanceID = 0, ulong assetID = 0, bool marketable = true, bool tradable = true, ImmutableHashSet<Tag>? tags = null, uint realAppID = 0, EType type = EType.Unknown, ERarity rarity = ERarity.Unknown) {
		ArgumentOutOfRangeException.ThrowIfZero(appID);
		ArgumentOutOfRangeException.ThrowIfZero(contextID);
		ArgumentOutOfRangeException.ThrowIfZero(classID);
		ArgumentOutOfRangeException.ThrowIfZero(amount);

		AppID = appID;
		ContextID = contextID;
		ClassID = classID;
		Amount = amount;
		InstanceID = instanceID;
		AssetID = assetID;
		Marketable = marketable;
		Tradable = tradable;
		RealAppID = realAppID;
		Type = type;
		Rarity = rarity;

		if (tags?.Count > 0) {
			Tags = tags;
		}
	}

	[JsonConstructor]
	private Asset() { }

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
