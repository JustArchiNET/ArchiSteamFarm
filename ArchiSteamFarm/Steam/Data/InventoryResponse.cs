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
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers.Json;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Steam.Integration;
using JetBrains.Annotations;
using SteamKit2;

namespace ArchiSteamFarm.Steam.Data;

[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
internal sealed class InventoryResponse : OptionalResultResponse {
	[JsonDisallowNull]
	[JsonInclude]
	[JsonPropertyName("assets")]
	internal ImmutableList<Asset> Assets { get; private init; } = ImmutableList<Asset>.Empty;

	[JsonDisallowNull]
	[JsonInclude]
	[JsonPropertyName("descriptions")]
	internal ImmutableHashSet<Description> Descriptions { get; private init; } = ImmutableHashSet<Description>.Empty;

	internal EResult? ErrorCode { get; private init; }
	internal string? ErrorText { get; private init; }

	[JsonDisallowNull]
	[JsonInclude]
	[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
	[JsonPropertyName("last_assetid")]
	internal ulong LastAssetID { get; private init; }

	internal bool MoreItems { get; private init; }

	[JsonDisallowNull]
	[JsonInclude]
	[JsonPropertyName("total_inventory_count")]
	internal uint TotalInventoryCount { get; private init; }

	[JsonDisallowNull]
	[JsonInclude]
	[JsonPropertyName("error")]
	private string Error {
		get => ErrorText ?? "";

		init {
			if (string.IsNullOrEmpty(value)) {
				ASF.ArchiLogger.LogNullError(value);

				return;
			}

			ErrorCode = SteamUtilities.InterpretError(value);
			ErrorText = value;
		}
	}

	[JsonDisallowNull]
	[JsonInclude]
	[JsonPropertyName("more_items")]
	private byte MoreItemsNumber {
		get => MoreItems ? (byte) 1 : (byte) 0;
		init => MoreItems = value > 0;
	}

	[JsonConstructor]
	private InventoryResponse() { }

	internal sealed class Description {
		internal Asset.ERarity Rarity {
			get {
				foreach (Tag tag in Tags) {
					switch (tag.Identifier) {
						case "droprate":
							switch (tag.Value) {
								case "droprate_0":
									return Asset.ERarity.Common;
								case "droprate_1":
									return Asset.ERarity.Uncommon;
								case "droprate_2":
									return Asset.ERarity.Rare;
								default:
									ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(tag.Value), tag.Value));

									break;
							}

							break;
					}
				}

				return Asset.ERarity.Unknown;
			}
		}

		internal uint RealAppID {
			get {
				foreach (Tag tag in Tags) {
					switch (tag.Identifier) {
						case "Game":
							if (string.IsNullOrEmpty(tag.Value) || (tag.Value.Length <= 4) || !tag.Value.StartsWith("app_", StringComparison.Ordinal)) {
								ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(tag.Value), tag.Value));

								break;
							}

							string appIDText = tag.Value[4..];

							if (!uint.TryParse(appIDText, out uint appID) || (appID == 0)) {
								ASF.ArchiLogger.LogNullError(appID);

								break;
							}

							return appID;
					}
				}

				return 0;
			}
		}

		internal Asset.EType Type {
			get {
				Asset.EType type = Asset.EType.Unknown;

				foreach (Tag tag in Tags) {
					switch (tag.Identifier) {
						case "cardborder":
							switch (tag.Value) {
								case "cardborder_0":
									return Asset.EType.TradingCard;
								case "cardborder_1":
									return Asset.EType.FoilTradingCard;
								default:
									ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(tag.Value), tag.Value));

									return Asset.EType.Unknown;
							}
						case "item_class":
							switch (tag.Value) {
								case "item_class_2":
									if (type == Asset.EType.Unknown) {
										// This is a fallback in case we'd have no cardborder available to interpret
										type = Asset.EType.TradingCard;
									}

									continue;
								case "item_class_3":
									return Asset.EType.ProfileBackground;
								case "item_class_4":
									return Asset.EType.Emoticon;
								case "item_class_5":
									return Asset.EType.BoosterPack;
								case "item_class_6":
									return Asset.EType.Consumable;
								case "item_class_7":
									return Asset.EType.SteamGems;
								case "item_class_8":
									return Asset.EType.ProfileModifier;
								case "item_class_10":
									return Asset.EType.SaleItem;
								case "item_class_11":
									return Asset.EType.Sticker;
								case "item_class_12":
									return Asset.EType.ChatEffect;
								case "item_class_13":
									return Asset.EType.MiniProfileBackground;
								case "item_class_14":
									return Asset.EType.AvatarProfileFrame;
								case "item_class_15":
									return Asset.EType.AnimatedAvatar;
								case "item_class_16":
									return Asset.EType.KeyboardSkin;
								case "item_class_17":
									return Asset.EType.StartupVideo;
								default:
									ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(tag.Value), tag.Value));

									return Asset.EType.Unknown;
							}
					}
				}

				return type;
			}
		}

		[JsonExtensionData]
		[JsonInclude]
		internal Dictionary<string, JsonElement>? AdditionalProperties { get; set; }

		[JsonInclude]
		[JsonPropertyName("appid")]
		[JsonRequired]
		internal uint AppID { get; private init; }

		[JsonInclude]
		[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
		[JsonPropertyName("classid")]
		[JsonRequired]
		internal ulong ClassID { get; private init; }

		[JsonDisallowNull]
		[JsonInclude]
		[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
		[JsonPropertyName("instanceid")]
		internal ulong InstanceID { get; private init; }

		internal bool Marketable { get; private init; }

		[JsonDisallowNull]
		[JsonInclude]
		[JsonPropertyName("tags")]
		internal ImmutableHashSet<Tag> Tags { get; private init; } = ImmutableHashSet<Tag>.Empty;

		internal bool Tradable { get; private init; }

		[JsonInclude]
		[JsonPropertyName("marketable")]
		[JsonRequired]
		private byte MarketableNumber {
			get => Marketable ? (byte) 1 : (byte) 0;
			init => Marketable = value > 0;
		}

		[JsonInclude]
		[JsonPropertyName("tradable")]
		[JsonRequired]
		private byte TradableNumber {
			get => Tradable ? (byte) 1 : (byte) 0;
			init => Tradable = value > 0;
		}

		// Constructed from trades being received/sent
		internal Description(uint appID, ulong classID, ulong instanceID, bool marketable, IReadOnlyCollection<Tag>? tags = null) {
			ArgumentOutOfRangeException.ThrowIfZero(appID);
			ArgumentOutOfRangeException.ThrowIfZero(classID);

			AppID = appID;
			ClassID = classID;
			InstanceID = instanceID;
			Marketable = marketable;
			Tradable = true;

			if (tags?.Count > 0) {
				Tags = tags.ToImmutableHashSet();
			}
		}

		[JsonConstructor]
		private Description() { }

		[UsedImplicitly]
		public static bool ShouldSerializeAdditionalProperties() => false;
	}
}
