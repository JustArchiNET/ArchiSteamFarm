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
using JetBrains.Annotations;
using ProtoBuf;
using SteamKit2.Internal;

namespace ArchiSteamFarm.Steam.Data;

// REF: https://developer.valvesoftware.com/wiki/Steam_Web_API/IEconService#CEcon_Asset
[PublicAPI]
public sealed class Asset {
	public const uint SteamAppID = 753;
	public const ulong SteamCommunityContextID = 6;
	public const ulong SteamPointsShopInstanceID = 3865004543;

	[JsonIgnore]
	public CEcon_Asset Body { get; } = new();

	[JsonIgnore]
	public bool IsSteamPointsShopItem => !Tradable && (InstanceID == SteamPointsShopInstanceID);

	[JsonIgnore]
	public bool Marketable => Description?.Marketable ?? false;

	[JsonIgnore]
	public EAssetRarity Rarity => Description?.Rarity ?? EAssetRarity.Unknown;

	[JsonIgnore]
	public uint RealAppID => Description?.RealAppID ?? 0;

	[JsonIgnore]
	public bool Tradable => Description?.Tradable ?? false;

	[JsonIgnore]
	public EAssetType Type => Description?.Type ?? EAssetType.Unknown;

	[JsonInclude]
	[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
	[JsonPropertyName("amount")]
	[JsonRequired]
	public uint Amount {
		get => (uint) Body.amount;
		internal set => Body.amount = value;
	}

	[JsonInclude]
	[JsonPropertyName("appid")]
	public uint AppID {
		get => Body.appid;
		private init => Body.appid = value;
	}

	[JsonInclude]
	[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
	[JsonPropertyName("assetid")]
	public ulong AssetID {
		get => Body.assetid;
		private init => Body.assetid = value;
	}

	[JsonInclude]
	[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
	[JsonPropertyName("classid")]
	public ulong ClassID {
		get => Body.classid;
		private init => Body.classid = value;
	}

	[JsonInclude]
	[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
	[JsonPropertyName("contextid")]
	public ulong ContextID {
		get => Body.contextid;
		private init => Body.contextid = value;
	}

	[JsonIgnore]
	public InventoryDescription? Description { get; internal set; }

	[JsonInclude]
	[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
	[JsonPropertyName("instanceid")]
	public ulong InstanceID {
		get => Body.instanceid;
		private init => Body.instanceid = value;
	}

	[JsonInclude]
	[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
	[JsonPropertyName("id")]
	private ulong ID {
		get => AssetID;
		init => AssetID = value;
	}

	public Asset(CEcon_Asset asset, InventoryDescription? description = null) {
		ArgumentNullException.ThrowIfNull(asset);

		Body = asset;
		Description = description;
	}

	public Asset(uint appID, ulong contextID, ulong classID, uint amount, InventoryDescription? description = null, ulong assetID = 0, ulong instanceID = 0) {
		ArgumentOutOfRangeException.ThrowIfZero(appID);
		ArgumentOutOfRangeException.ThrowIfZero(contextID);
		ArgumentOutOfRangeException.ThrowIfZero(classID);
		ArgumentOutOfRangeException.ThrowIfZero(amount);

		AppID = appID;
		ContextID = contextID;
		ClassID = classID;
		Amount = amount;

		Description = description;
		AssetID = assetID;
		InstanceID = instanceID;
	}

	[JsonConstructor]
	private Asset() { }

	public Asset DeepClone() => new(Serializer.DeepClone(Body), Description?.DeepClone());
}
