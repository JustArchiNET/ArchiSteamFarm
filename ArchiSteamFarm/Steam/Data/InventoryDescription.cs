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
using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers.Json;
using ArchiSteamFarm.Localization;
using JetBrains.Annotations;
using ProtoBuf;
using SteamKit2.Internal;

namespace ArchiSteamFarm.Steam.Data;

[PublicAPI]
public sealed class InventoryDescription {
	[JsonIgnore]
	public CEconItem_Description Body { get; } = new();

	[JsonInclude]
	[JsonPropertyName("appid")]
	[JsonRequired]
	public uint AppID {
		get => (uint) Body.appid;
		private init => Body.appid = (int) value;
	}

	[JsonInclude]
	[JsonPropertyName("background_color")]
	public string BackgroundColor {
		get => Body.background_color;
		private init => Body.background_color = value;
	}

	[JsonInclude]
	[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
	[JsonPropertyName("classid")]
	[JsonRequired]
	public ulong ClassID {
		get => Body.classid;
		private init => Body.classid = value;
	}

	[JsonConverter(typeof(BooleanNumberConverter))]
	[JsonInclude]
	[JsonPropertyName("commodity")]
	public bool Commodity {
		get => Body.commodity;
		private init => Body.commodity = value;
	}

	[JsonConverter(typeof(BooleanNumberConverter))]
	[JsonInclude]
	[JsonPropertyName("currency")]
	public bool Currency {
		get => Body.currency;
		private init => Body.currency = value;
	}

	[JsonInclude]
	[JsonPropertyName("descriptions")]
	public ImmutableHashSet<ItemDescription> Descriptions {
		get => Body.descriptions.Select(static description => new ItemDescription(description.type, description.value, description.color, description.label)).ToImmutableHashSet();

		private init {
			Body.descriptions.Clear();

			Body.descriptions.AddRange(
				value.Select(
					static description => new CEconItem_DescriptionLine {
						color = description.Color,
						label = description.Label,
						type = description.Type,
						value = description.Value
					}
				)
			);
		}
	}

#pragma warning disable CA1056 // This property is not guaranteed to have parsable Uri
	[JsonInclude]
	[JsonPropertyName("icon_url")]
	public string IconURL {
		get => Body.icon_url;
		private init => Body.icon_url = value;
	}
#pragma warning restore CA1056 // This property is not guaranteed to have parsable Uri

#pragma warning disable CA1056 // This property is not guaranteed to have parsable Uri
	[JsonInclude]
	[JsonPropertyName("icon_url_large")]
	public string IconURLLarge {
		get => Body.icon_url_large;
		private init => Body.icon_url_large = value;
	}
#pragma warning restore CA1056 // This property is not guaranteed to have parsable Uri

	[JsonInclude]
	[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
	[JsonPropertyName("instanceid")]
	public ulong InstanceID {
		get => Body.instanceid;
		private init => Body.instanceid = value;
	}

	[JsonConverter(typeof(BooleanNumberConverter))]
	[JsonInclude]
	[JsonPropertyName("marketable")]
	[JsonRequired]
	public bool Marketable {
		get => Body.marketable;
		private init => Body.marketable = value;
	}

	[JsonInclude]
	[JsonPropertyName("market_fee_app")]
	public uint MarketFeeApp {
		get => (uint) Body.market_fee_app;
		private init => Body.market_fee_app = (int) value;
	}

	[JsonInclude]
	[JsonPropertyName("market_hash_name")]
	public string MarketHashName {
		get => Body.market_hash_name;
		private init => Body.market_hash_name = value;
	}

	[JsonInclude]
	[JsonPropertyName("market_name")]
	public string MarketName {
		get => Body.market_name;
		private init => Body.market_name = value;
	}

	[JsonInclude]
	[JsonPropertyName("name")]
	public string Name {
		get => Body.name;
		private init => Body.name = value;
	}

	[JsonInclude]
	[JsonPropertyName("owner_actions")]
	public ImmutableHashSet<ItemAction> OwnerActions {
		get => Body.owner_actions.Select(static action => new ItemAction(action.link, action.name)).ToImmutableHashSet();

		private init {
			Body.owner_actions.Clear();

			Body.owner_actions.AddRange(
				value.Select(
					static action => new CEconItem_Action {
						link = action.Link,
						name = action.Name
					}
				)
			);
		}
	}

	[JsonIgnore]
	public EAssetRarity Rarity {
		get {
			if (CachedRarity.HasValue) {
				return CachedRarity.Value;
			}

			foreach (CEconItem_Tag? tag in Body.tags) {
				switch (tag.category) {
					case "droprate":
						switch (tag.internal_name) {
							case "droprate_0":
								CachedRarity = EAssetRarity.Common;

								return CachedRarity.Value;
							case "droprate_1":
								CachedRarity = EAssetRarity.Uncommon;

								return CachedRarity.Value;
							case "droprate_2":
								CachedRarity = EAssetRarity.Rare;

								return CachedRarity.Value;
							default:
								ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(tag.internal_name), tag.internal_name));

								CachedRarity = EAssetRarity.Unknown;

								return CachedRarity.Value;
						}
				}
			}

			CachedRarity = EAssetRarity.Unknown;

			return CachedRarity.Value;
		}

		private init {
			if (!Enum.IsDefined(value)) {
				throw new InvalidEnumArgumentException(nameof(value), (int) value, typeof(EAssetRarity));
			}

			CachedRarity = value;

			if (value == EAssetRarity.Unknown) {
				return;
			}

			CEconItem_Tag? tag = Body.tags.FirstOrDefault(static tag => tag.category == "droprate");

			if (tag == null) {
				tag = new CEconItem_Tag { category = "droprate" };

				Body.tags.Add(tag);
			}

			tag.internal_name = value switch {
				EAssetRarity.Common => "droprate_0",
				EAssetRarity.Uncommon => "droprate_1",
				EAssetRarity.Rare => "droprate_2",
				_ => throw new InvalidOperationException(nameof(value))
			};
		}
	}

	[JsonIgnore]
	public uint RealAppID {
		get {
			if (CachedRealAppID.HasValue) {
				return CachedRealAppID.Value;
			}

			foreach (CEconItem_Tag? tag in Body.tags) {
				switch (tag.category) {
					case "Game":
						if (string.IsNullOrEmpty(tag.internal_name) || (tag.internal_name.Length <= 4) || !tag.internal_name.StartsWith("app_", StringComparison.Ordinal)) {
							ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(tag.internal_name), tag.internal_name));

							break;
						}

						string appIDText = tag.internal_name[4..];

						if (!uint.TryParse(appIDText, out uint appID) || (appID == 0)) {
							ASF.ArchiLogger.LogNullError(appID);

							break;
						}

						CachedRealAppID = appID;

						return CachedRealAppID.Value;
				}
			}

			CachedRealAppID = 0;

			return CachedRealAppID.Value;
		}

		private init {
			CachedRealAppID = value;

			if (value == 0) {
				return;
			}

			CEconItem_Tag? tag = Body.tags.FirstOrDefault(static tag => tag.category == "Game");

			if (tag == null) {
				tag = new CEconItem_Tag { category = "Game" };

				Body.tags.Add(tag);
			}

			tag.internal_name = $"app_{value}";
		}
	}

	[JsonDisallowNull]
	[JsonInclude]
	[JsonPropertyName("tags")]
	public ImmutableHashSet<Tag> Tags {
		get => Body.tags.Select(static tag => new Tag(tag.category, tag.internal_name, tag.localized_category_name, tag.localized_tag_name, tag.color)).ToImmutableHashSet();

		private init {
			Body.tags.Clear();

			Body.tags.AddRange(
				value.Select(
					tag => new CEconItem_Tag {
						appid = AppID,
						category = tag.Identifier,
						color = tag.Color,
						internal_name = tag.Value,
						localized_category_name = tag.LocalizedIdentifier,
						localized_tag_name = tag.LocalizedValue
					}
				)
			);
		}
	}

	[JsonConverter(typeof(BooleanNumberConverter))]
	[JsonInclude]
	[JsonPropertyName("tradable")]
	[JsonRequired]
	public bool Tradable {
		get => Body.tradable;
		private init => Body.tradable = value;
	}

	[JsonIgnore]
	public EAssetType Type {
		get {
			if (CachedType.HasValue) {
				return CachedType.Value;
			}

			EAssetType type = EAssetType.Unknown;

			foreach (CEconItem_Tag? tag in Body.tags) {
				switch (tag.category) {
					case "cardborder":
						switch (tag.internal_name) {
							case "cardborder_0":
								CachedType = EAssetType.TradingCard;

								return CachedType.Value;
							case "cardborder_1":
								CachedType = EAssetType.FoilTradingCard;

								return CachedType.Value;
							default:
								ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(tag.internal_name), tag.internal_name));

								CachedType = EAssetType.Unknown;

								return CachedType.Value;
						}
					case "item_class":
						switch (tag.internal_name) {
							case "item_class_2":
								if (type == EAssetType.Unknown) {
									// This is a fallback in case we'd have no cardborder available to interpret
									type = EAssetType.TradingCard;
								}

								continue;
							case "item_class_3":
								CachedType = EAssetType.ProfileBackground;

								return CachedType.Value;
							case "item_class_4":
								CachedType = EAssetType.Emoticon;

								return CachedType.Value;
							case "item_class_5":
								CachedType = EAssetType.BoosterPack;

								return CachedType.Value;
							case "item_class_6":
								CachedType = EAssetType.Consumable;

								return CachedType.Value;
							case "item_class_7":
								CachedType = EAssetType.SteamGems;

								return CachedType.Value;
							case "item_class_8":
								CachedType = EAssetType.ProfileModifier;

								return CachedType.Value;
							case "item_class_10":
								CachedType = EAssetType.SaleItem;

								return CachedType.Value;
							case "item_class_11":
								CachedType = EAssetType.Sticker;

								return CachedType.Value;
							case "item_class_12":
								CachedType = EAssetType.ChatEffect;

								return CachedType.Value;
							case "item_class_13":
								CachedType = EAssetType.MiniProfileBackground;

								return CachedType.Value;
							case "item_class_14":
								CachedType = EAssetType.AvatarProfileFrame;

								return CachedType.Value;
							case "item_class_15":
								CachedType = EAssetType.AnimatedAvatar;

								return CachedType.Value;
							case "item_class_16":
								CachedType = EAssetType.KeyboardSkin;

								return CachedType.Value;
							case "item_class_17":
								CachedType = EAssetType.StartupVideo;

								return CachedType.Value;
							default:
								ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(tag.internal_name), tag.internal_name));

								CachedType = EAssetType.Unknown;

								return CachedType.Value;
						}
				}
			}

			CachedType = type;

			return CachedType.Value;
		}

		private init {
			if (!Enum.IsDefined(value)) {
				throw new InvalidEnumArgumentException(nameof(value), (int) value, typeof(EAssetType));
			}

			CachedType = value;

			switch (value) {
				case EAssetType.Unknown:
					return;
				case EAssetType.TradingCard:
				case EAssetType.FoilTradingCard:
					CEconItem_Tag? cardTag = Body.tags.FirstOrDefault(static tag => tag.category == "cardborder");

					if (cardTag == null) {
						cardTag = new CEconItem_Tag { category = "cardborder" };

						Body.tags.Add(cardTag);
					}

					cardTag.internal_name = value switch {
						EAssetType.TradingCard => "cardborder_0",
						EAssetType.FoilTradingCard => "cardborder_1",
						_ => throw new InvalidOperationException(nameof(value))
					};

					// We're still going to add item_class tag below
					break;
			}

			CEconItem_Tag? tag = Body.tags.FirstOrDefault(static tag => tag.category == "item_class");

			if (tag == null) {
				tag = new CEconItem_Tag { category = "item_class" };

				Body.tags.Add(tag);
			}

			tag.internal_name = value switch {
				EAssetType.TradingCard => "item_class_2",
				EAssetType.FoilTradingCard => "item_class_2",
				EAssetType.ProfileBackground => "item_class_3",
				EAssetType.Emoticon => "item_class_4",
				EAssetType.BoosterPack => "item_class_5",
				EAssetType.Consumable => "item_class_6",
				EAssetType.SteamGems => "item_class_7",
				EAssetType.ProfileModifier => "item_class_8",
				EAssetType.SaleItem => "item_class_10",
				EAssetType.Sticker => "item_class_11",
				EAssetType.ChatEffect => "item_class_12",
				EAssetType.MiniProfileBackground => "item_class_13",
				EAssetType.AvatarProfileFrame => "item_class_14",
				EAssetType.AnimatedAvatar => "item_class_15",
				EAssetType.KeyboardSkin => "item_class_16",
				EAssetType.StartupVideo => "item_class_17",
				_ => throw new InvalidOperationException(nameof(value))
			};
		}
	}

	[JsonInclude]
	[JsonPropertyName("type")]
	public string TypeText {
		get => Body.type;
		private init => Body.type = value;
	}

	private EAssetRarity? CachedRarity;
	private uint? CachedRealAppID;
	private EAssetType? CachedType;

	public InventoryDescription(CEconItem_Description description) {
		ArgumentNullException.ThrowIfNull(description);

		Body = description;
	}

	public InventoryDescription(uint appID, ulong classID, ulong instanceID = 0, bool marketable = false, bool tradable = false, uint realAppID = 0, EAssetType type = EAssetType.Unknown, EAssetRarity rarity = EAssetRarity.Unknown) {
		ArgumentOutOfRangeException.ThrowIfZero(appID);
		ArgumentOutOfRangeException.ThrowIfZero(classID);

		if (!Enum.IsDefined(type)) {
			throw new InvalidEnumArgumentException(nameof(type), (int) type, typeof(EAssetType));
		}

		if (!Enum.IsDefined(rarity)) {
			throw new InvalidEnumArgumentException(nameof(rarity), (int) rarity, typeof(EAssetRarity));
		}

		AppID = appID;
		ClassID = classID;

		InstanceID = instanceID;
		Marketable = marketable;
		Tradable = tradable;
		RealAppID = realAppID;
		Type = type;
		Rarity = rarity;
	}

	[JsonConstructor]
	private InventoryDescription() { }

	public InventoryDescription DeepClone() => new(Serializer.DeepClone(Body));
}
