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
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using ArchiSteamFarm.Core;
using JetBrains.Annotations;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ArchiSteamFarm.Steam.Data {
	// REF: https://developer.valvesoftware.com/wiki/Steam_Web_API/IEconService#CEcon_Asset
	public sealed class Asset {
		[PublicAPI]
		public const uint SteamAppID = 753;

		[PublicAPI]
		public const ulong SteamCommunityContextID = 6;

		[JsonIgnore]
		[PublicAPI]
		public IReadOnlyDictionary<string, JToken>? AdditionalPropertiesReadOnly => AdditionalProperties;

		[JsonIgnore]
		[PublicAPI]
		public uint Amount { get; internal set; }

		[JsonProperty(PropertyName = "appid", Required = Required.DisallowNull)]
		public uint AppID { get; private set; }

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

		[JsonExtensionData(WriteData = false)]
		internal Dictionary<string, JToken>? AdditionalProperties { private get; set; }

		[JsonProperty(PropertyName = "amount", Required = Required.Always)]
		private string AmountText {
			get => Amount.ToString(CultureInfo.InvariantCulture);

			set {
				if (string.IsNullOrEmpty(value)) {
					ASF.ArchiLogger.LogNullError(nameof(value));

					return;
				}

				if (!uint.TryParse(value, out uint amount) || (amount == 0)) {
					ASF.ArchiLogger.LogNullError(nameof(amount));

					return;
				}

				Amount = amount;
			}
		}

		[JsonProperty(PropertyName = "assetid", Required = Required.DisallowNull)]
		private string AssetIDText {
			get => AssetID.ToString(CultureInfo.InvariantCulture);

			set {
				if (string.IsNullOrEmpty(value)) {
					ASF.ArchiLogger.LogNullError(nameof(value));

					return;
				}

				if (!ulong.TryParse(value, out ulong assetID) || (assetID == 0)) {
					ASF.ArchiLogger.LogNullError(nameof(assetID));

					return;
				}

				AssetID = assetID;
			}
		}

		[JsonProperty(PropertyName = "classid", Required = Required.DisallowNull)]
		private string ClassIDText {
			set {
				if (string.IsNullOrEmpty(value)) {
					ASF.ArchiLogger.LogNullError(nameof(value));

					return;
				}

				if (!ulong.TryParse(value, out ulong classID) || (classID == 0)) {
					return;
				}

				ClassID = classID;
			}
		}

		[JsonProperty(PropertyName = "contextid", Required = Required.DisallowNull)]
		private string ContextIDText {
			get => ContextID.ToString(CultureInfo.InvariantCulture);

			set {
				if (string.IsNullOrEmpty(value)) {
					ASF.ArchiLogger.LogNullError(nameof(value));

					return;
				}

				if (!ulong.TryParse(value, out ulong contextID) || (contextID == 0)) {
					ASF.ArchiLogger.LogNullError(nameof(contextID));

					return;
				}

				ContextID = contextID;
			}
		}

		[JsonProperty(PropertyName = "id", Required = Required.DisallowNull)]
		private string IDText {
			set => AssetIDText = value;
		}

		[JsonProperty(PropertyName = "instanceid", Required = Required.DisallowNull)]
		private string InstanceIDText {
			set {
				if (string.IsNullOrEmpty(value)) {
					return;
				}

				if (!ulong.TryParse(value, out ulong instanceID)) {
					ASF.ArchiLogger.LogNullError(nameof(instanceID));

					return;
				}

				InstanceID = instanceID;
			}
		}

		// Constructed from trades being received or plugins
		public Asset(uint appID, ulong contextID, ulong classID, uint amount, ulong instanceID = 0, ulong assetID = 0, bool marketable = true, bool tradable = true, ImmutableHashSet<Tag>? tags = null, uint realAppID = 0, EType type = EType.Unknown, ERarity rarity = ERarity.Unknown) {
			if (appID == 0) {
				throw new ArgumentOutOfRangeException(nameof(appID));
			}

			if (contextID == 0) {
				throw new ArgumentOutOfRangeException(nameof(contextID));
			}

			if (classID == 0) {
				throw new ArgumentOutOfRangeException(nameof(classID));
			}

			if (amount == 0) {
				throw new ArgumentOutOfRangeException(nameof(amount));
			}

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
			AnimatedAvatar
		}
	}
}
