// _                _      _  ____   _                           _____
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
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers.Json;
using ArchiSteamFarm.Localization;
using JetBrains.Annotations;
using SteamKit2.Internal;

namespace ArchiSteamFarm.Steam.Data;

[PublicAPI]
public sealed class InventoryDescription {
	[JsonIgnore]
	public CEconItem_Description ProtobufBody { get; } = new();

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
	public uint AppID {
		get => (uint) ProtobufBody.appid;
		private init => ProtobufBody.appid = (int) value;
	}

	[JsonInclude]
	[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
	[JsonPropertyName("classid")]
	[JsonRequired]
	public ulong ClassID {
		get => ProtobufBody.classid;
		private init => ProtobufBody.classid = value;
	}

	[JsonInclude]
	[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
	[JsonPropertyName("instanceid")]
	public ulong InstanceID {
		get => ProtobufBody.instanceid;
		private init => ProtobufBody.instanceid = value;
	}

	[JsonInclude]
	[JsonPropertyName("currency")]
	[JsonConverter(typeof(BooleanNumberConverter))]
	public bool Currency {
		get => ProtobufBody.currency;
		private init => ProtobufBody.currency = value;
	}

	[JsonInclude]
	[JsonPropertyName("background_color")]
	public string BackgroundColor {
		get => ProtobufBody.background_color;
		private init => ProtobufBody.background_color = value;
	}

	[JsonInclude]
	[JsonPropertyName("icon_url")]
#pragma warning disable CA1056 - this is a JSON/Protobuf field, and even then it doesn't contain full URL
	public string IconURL {
#pragma warning restore CA1056 - this is a JSON/Protobuf field, and even then it doesn't contain full URL
		get => ProtobufBody.icon_url;
		private init => ProtobufBody.icon_url = value;
	}

	[JsonInclude]
	[JsonPropertyName("icon_url_large")]
#pragma warning disable CA1056 - this is a JSON/Protobuf field, and even then it doesn't contain full URL
	public string IconURLLarge {
#pragma warning restore CA1056 - this is a JSON/Protobuf field, and even then it doesn't contain full URL
		get => ProtobufBody.icon_url_large;
		private init => ProtobufBody.icon_url_large = value;
	}

	[JsonInclude]
	[JsonPropertyName("owner_actions")]
	public ImmutableHashSet<Action> OwnerActions {
		get => ProtobufBody.owner_actions.Select(static action => new Action(action.link, action.name)).ToImmutableHashSet();
		private init {
			ProtobufBody.owner_actions.Clear();

			foreach (Action action in value) {
				ProtobufBody.owner_actions.Add(
					new CEconItem_Action {
						link = action.Link,
						name = action.Name
					}
				);
			}
		}
	}

	[JsonInclude]
	[JsonPropertyName("name")]
	public string Name {
		get => ProtobufBody.name;
		private init => ProtobufBody.name = value;
	}

	[JsonInclude]
	[JsonPropertyName("type")]
	public string TypeText {
		get => ProtobufBody.type;
		private init => ProtobufBody.type = value;
	}

	[JsonInclude]
	[JsonPropertyName("market_name")]
	public string MarketName {
		get => ProtobufBody.market_name;
		private init => ProtobufBody.market_name = value;
	}

	[JsonInclude]
	[JsonPropertyName("market_hash_name")]
	public string MarketHashName {
		get => ProtobufBody.market_hash_name;
		private init => ProtobufBody.market_hash_name = value;
	}

	[JsonInclude]
	[JsonPropertyName("market_fee_app")]
	public uint MarketFeeApp {
		get => (uint) ProtobufBody.market_fee_app;
		private init => ProtobufBody.market_fee_app = (int) value;
	}

	[JsonInclude]
	[JsonPropertyName("commodity")]
	[JsonConverter(typeof(BooleanNumberConverter))]
	public bool Commodity {
		get => ProtobufBody.commodity;
		private init => ProtobufBody.commodity = value;
	}

	[JsonInclude]
	[JsonPropertyName("descriptions")]
	public ImmutableHashSet<Description> Descriptions {
		get => ProtobufBody.descriptions.Select(static description => new Description(description.type, description.value, description.color, description.label)).ToImmutableHashSet();
		private init {
			ProtobufBody.descriptions.Clear();

			foreach (Description description in value) {
				ProtobufBody.descriptions.Add(
					new CEconItem_DescriptionLine {
						color = description.Color,
						label = description.Label,
						type = description.Type,
						value = description.Value
					}
				);
			}
		}
	}

	[JsonInclude]
	[JsonPropertyName("marketable")]
	[JsonRequired]
	[JsonConverter(typeof(BooleanNumberConverter))]
	public bool Marketable {
		get => ProtobufBody.marketable;
		private init => ProtobufBody.marketable = value;
	}

	[JsonDisallowNull]
	[JsonInclude]
	[JsonPropertyName("tags")]
	internal ImmutableHashSet<Tag> Tags {
		get => ProtobufBody.tags.Select(static x => new Tag(x.category, x.internal_name, x.localized_category_name, x.localized_tag_name)).ToImmutableHashSet();
		private init {
			ProtobufBody.tags.Clear();

			foreach (Tag tag in value) {
				ProtobufBody.tags.Add(
					new CEconItem_Tag {
						appid = AppID,
						category = tag.Identifier,

						//color =
						internal_name = tag.Value,
						localized_category_name = tag.LocalizedIdentifier,
						localized_tag_name = tag.LocalizedValue
					}
				);
			}
		}
	}

	[JsonInclude]
	[JsonPropertyName("tradable")]
	[JsonRequired]
	[JsonConverter(typeof(BooleanNumberConverter))]
	internal bool Tradable {
		get => ProtobufBody.tradable;
		private init => ProtobufBody.tradable = value;
	}

	// Constructed from trades being received/sent
	internal InventoryDescription(uint appID, ulong classID, ulong instanceID, bool marketable, bool tradable, IReadOnlyCollection<Tag>? tags = null) {
		ArgumentOutOfRangeException.ThrowIfZero(appID);
		ArgumentOutOfRangeException.ThrowIfZero(classID);

		AppID = appID;
		ClassID = classID;
		InstanceID = instanceID;
		Marketable = marketable;
		Tradable = tradable;

		if (tags?.Count > 0) {
			Tags = tags.ToImmutableHashSet();
		}
	}

	[JsonConstructor]
	private InventoryDescription() { }

	internal InventoryDescription(CEconItem_Description description) => ProtobufBody = description;

	[UsedImplicitly]
	public static bool ShouldSerializeAdditionalProperties() => false;

	[JsonIgnore]
	internal static readonly ImmutableHashSet<string> NonAdditionalProperties = typeof(InventoryDescription)
		.GetProperties(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
		.Select(static prop => prop.GetCustomAttribute<JsonPropertyNameAttribute>())
		.Where(static attr => attr != null)
		.Select(static attr => attr!.Name)
		.Where(static name => !string.IsNullOrEmpty(name))
		.ToImmutableHashSet();
}

public class Description {
	[JsonInclude]
	[JsonPropertyName("type")]
	public string Type { get; private init; } = null!;

	[JsonInclude]
	[JsonPropertyName("value")]
	public string Value { get; private init; } = null!;

	[JsonInclude]
	[JsonPropertyName("color")]
	public string Color { get; private init; } = null!;

	[JsonInclude]
	[JsonPropertyName("label")]
	public string Label { get; private init; } = null!;

	internal Description(string type, string value, string color, string label) {
		Type = type;
		Value = value;
		Color = color;
		Label = label;
	}

	[JsonConstructor]
	private Description() { }
}

public class Action {
	[JsonInclude]
	[JsonPropertyName("link")]
	public string Link { get; private init; } = null!;

	[JsonInclude]
	[JsonPropertyName("name")]
	public string Name { get; private init; } = null!;

	internal Action(string link, string name) {
		Link = link;
		Name = name;
	}

	[JsonConstructor]
	private Action() { }
}
