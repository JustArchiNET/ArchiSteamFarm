//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2020 Łukasz "JustArchi" Domeradzki
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
using System.Linq;
using ArchiSteamFarm.Localization;
using JetBrains.Annotations;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SteamKit2;

namespace ArchiSteamFarm.Json {
	public static class Steam {
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

			[JsonExtensionData]
			internal Dictionary<string, JToken>? AdditionalProperties { private get; set; }

#pragma warning disable IDE0051
			[JsonProperty(PropertyName = "amount", Required = Required.Always)]
			private string AmountText {
				get => Amount.ToString();

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
#pragma warning restore IDE0051

#pragma warning disable IDE0052
			[JsonProperty(PropertyName = "assetid", Required = Required.DisallowNull)]
			private string AssetIDText {
				get => AssetID.ToString();

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
#pragma warning restore IDE0052

#pragma warning disable IDE0051
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
#pragma warning restore IDE0051

#pragma warning disable IDE0051
			[JsonProperty(PropertyName = "contextid", Required = Required.DisallowNull)]
			private string ContextIDText {
				get => ContextID.ToString();

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
#pragma warning restore IDE0051

#pragma warning disable IDE0051
			[JsonProperty(PropertyName = "id", Required = Required.DisallowNull)]
			private string IDText {
				set => AssetIDText = value;
			}
#pragma warning restore IDE0051

#pragma warning disable IDE0051
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
#pragma warning restore IDE0051

			// Constructed from trades being received or plugins
			public Asset(uint appID, ulong contextID, ulong classID, uint amount, ulong instanceID = 0, ulong assetID = 0, bool marketable = true, bool tradable = true, ImmutableHashSet<Tag>? tags = null, uint realAppID = 0, EType type = EType.Unknown, ERarity rarity = ERarity.Unknown) {
				if ((appID == 0) || (contextID == 0) || (classID == 0) || (amount == 0)) {
					throw new ArgumentNullException(nameof(appID) + " || " + nameof(contextID) + " || " + nameof(classID) + " || " + nameof(amount));
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

				if ((tags != null) && (tags.Count > 0)) {
					Tags = tags;
				}
			}

			[JsonConstructor]
			private Asset() { }

			internal Asset CreateShallowCopy() => (Asset) MemberwiseClone();

			public sealed class Tag {
				[JsonProperty(PropertyName = "category", Required = Required.Always)]
				[PublicAPI]
				public readonly string? Identifier;

				[JsonProperty(PropertyName = "internal_name", Required = Required.Always)]
				[PublicAPI]
				public readonly string? Value;

				internal Tag(string identifier, string value) {
					if (string.IsNullOrEmpty(identifier) || string.IsNullOrEmpty(value)) {
						throw new ArgumentNullException(nameof(identifier) + " || " + nameof(value));
					}

					Identifier = identifier;
					Value = value;
				}

				[JsonConstructor]
				private Tag() { }
			}

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

		[PublicAPI]
		[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
		public class BooleanResponse {
			[JsonProperty(PropertyName = "success", Required = Required.Always)]
			public readonly bool Success;

			[JsonConstructor]
			protected BooleanResponse() { }
		}

		[PublicAPI]
		[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
		public class EResultResponse {
			[JsonProperty(PropertyName = "success", Required = Required.Always)]
			public readonly EResult Result;

			[JsonConstructor]
			protected EResultResponse() { }
		}

		// REF: https://developer.valvesoftware.com/wiki/Steam_Web_API/IEconService#CEcon_TradeOffer
		public sealed class TradeOffer {
			[PublicAPI]
			public readonly ulong OtherSteamID64;

			[PublicAPI]
			public readonly ETradeOfferState State;

			[PublicAPI]
			public readonly ulong TradeOfferID;

			[PublicAPI]
			public IReadOnlyCollection<Asset> ItemsToGiveReadOnly => ItemsToGive;

			[PublicAPI]
			public IReadOnlyCollection<Asset> ItemsToReceiveReadOnly => ItemsToReceive;

			internal readonly HashSet<Asset> ItemsToGive = new HashSet<Asset>();
			internal readonly HashSet<Asset> ItemsToReceive = new HashSet<Asset>();

			// Constructed from trades being received
			internal TradeOffer(ulong tradeOfferID, uint otherSteamID3, ETradeOfferState state) {
				if ((tradeOfferID == 0) || (otherSteamID3 == 0) || !Enum.IsDefined(typeof(ETradeOfferState), state)) {
					throw new ArgumentNullException(nameof(tradeOfferID) + " || " + nameof(otherSteamID3) + " || " + nameof(state));
				}

				TradeOfferID = tradeOfferID;
				OtherSteamID64 = new SteamID(otherSteamID3, EUniverse.Public, EAccountType.Individual);
				State = state;
			}

			[PublicAPI]
			public bool IsValidSteamItemsRequest(IReadOnlyCollection<Asset.EType> acceptedTypes) {
				if ((acceptedTypes == null) || (acceptedTypes.Count == 0)) {
					throw new ArgumentNullException(nameof(acceptedTypes));
				}

				return ItemsToGive.All(item => (item.AppID == Asset.SteamAppID) && (item.ContextID == Asset.SteamCommunityContextID) && acceptedTypes.Contains(item.Type));
			}
		}

		[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
		internal sealed class InventoryResponse : EResultResponse {
			[JsonProperty(PropertyName = "assets", Required = Required.DisallowNull)]
			internal readonly ImmutableHashSet<Asset>? Assets;

			[JsonProperty(PropertyName = "descriptions", Required = Required.DisallowNull)]
			internal readonly ImmutableHashSet<Description>? Descriptions;

			[JsonProperty(PropertyName = "error", Required = Required.DisallowNull)]
			internal readonly string? Error;

			[JsonProperty(PropertyName = "total_inventory_count", Required = Required.DisallowNull)]
			internal readonly uint TotalInventoryCount;

			internal ulong LastAssetID { get; private set; }
			internal bool MoreItems { get; private set; }

#pragma warning disable IDE0051
			[JsonProperty(PropertyName = "last_assetid", Required = Required.DisallowNull)]
			private string LastAssetIDText {
				set {
					if (string.IsNullOrEmpty(value)) {
						ASF.ArchiLogger.LogNullError(nameof(value));

						return;
					}

					if (!ulong.TryParse(value, out ulong lastAssetID) || (lastAssetID == 0)) {
						ASF.ArchiLogger.LogNullError(nameof(lastAssetID));

						return;
					}

					LastAssetID = lastAssetID;
				}
			}
#pragma warning restore IDE0051

#pragma warning disable IDE0051
			[JsonProperty(PropertyName = "more_items", Required = Required.DisallowNull)]
			private byte MoreItemsNumber {
				set => MoreItems = value > 0;
			}
#pragma warning restore IDE0051

			[JsonConstructor]
			private InventoryResponse() { }

			internal sealed class Description {
				internal Asset.ERarity Rarity {
					get {
						if (Tags == null) {
							return Asset.ERarity.Unknown;
						}

						foreach (Asset.Tag tag in Tags) {
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
											ASF.ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(tag.Value), tag.Value));

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
						if (Tags == null) {
							return 0;
						}

						foreach (Asset.Tag tag in Tags) {
							switch (tag.Identifier) {
								case "Game":
									if (string.IsNullOrEmpty(tag.Value) || (tag.Value!.Length <= 4) || !tag.Value.StartsWith("app_", StringComparison.Ordinal)) {
										ASF.ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(tag.Value), tag.Value));

										break;
									}

									string appIDText = tag.Value.Substring(4);

									if (!uint.TryParse(appIDText, out uint appID) || (appID == 0)) {
										ASF.ArchiLogger.LogNullError(nameof(appID));

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
						if (Tags == null) {
							return Asset.EType.Unknown;
						}

						Asset.EType type = Asset.EType.Unknown;

						foreach (Asset.Tag tag in Tags) {
							switch (tag.Identifier) {
								case "cardborder":
									switch (tag.Value) {
										case "cardborder_0":
											return Asset.EType.TradingCard;
										case "cardborder_1":
											return Asset.EType.FoilTradingCard;
										default:
											ASF.ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(tag.Value), tag.Value));

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
										default:
											ASF.ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(tag.Value), tag.Value));

											return Asset.EType.Unknown;
									}
							}
						}

						return type;
					}
				}

				[JsonExtensionData]
				internal Dictionary<string, JToken>? AdditionalProperties {
					get;
					[UsedImplicitly]
					set;
				}

				[JsonProperty(PropertyName = "appid", Required = Required.Always)]
				internal uint AppID { get; set; }

				internal ulong ClassID { get; set; }
				internal ulong InstanceID { get; set; }
				internal bool Marketable { get; set; }

				[JsonProperty(PropertyName = "tags", Required = Required.DisallowNull)]
				internal ImmutableHashSet<Asset.Tag>? Tags { get; set; }

				internal bool Tradable { get; set; }

#pragma warning disable IDE0051
				[JsonProperty(PropertyName = "classid", Required = Required.Always)]
				private string ClassIDText {
					set {
						if (string.IsNullOrEmpty(value)) {
							ASF.ArchiLogger.LogNullError(nameof(value));

							return;
						}

						if (!ulong.TryParse(value, out ulong classID) || (classID == 0)) {
							ASF.ArchiLogger.LogNullError(nameof(classID));

							return;
						}

						ClassID = classID;
					}
				}
#pragma warning restore IDE0051

#pragma warning disable IDE0051
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
#pragma warning restore IDE0051

#pragma warning disable IDE0051
				[JsonProperty(PropertyName = "marketable", Required = Required.Always)]
				private byte MarketableNumber {
					set => Marketable = value > 0;
				}
#pragma warning restore IDE0051

#pragma warning disable IDE0051
				[JsonProperty(PropertyName = "tradable", Required = Required.Always)]
				private byte TradableNumber {
					set => Tradable = value > 0;
				}
#pragma warning restore IDE0051

				[JsonConstructor]
				internal Description() { }
			}
		}

		[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
		internal sealed class NewDiscoveryQueueResponse {
			[JsonProperty(PropertyName = "queue", Required = Required.Always)]
			internal readonly ImmutableHashSet<uint>? Queue;

			[JsonConstructor]
			private NewDiscoveryQueueResponse() { }
		}

		[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
		internal sealed class RedeemWalletResponse : EResultResponse {
			[JsonProperty(PropertyName = "detail", Required = Required.DisallowNull)]
			internal readonly EPurchaseResultDetail? PurchaseResultDetail;

			[JsonConstructor]
			private RedeemWalletResponse() { }
		}

		[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
		internal sealed class TradeOfferAcceptResponse {
			[JsonProperty(PropertyName = "strError", Required = Required.DisallowNull)]
			internal readonly string? ErrorText;

			[JsonProperty(PropertyName = "needs_mobile_confirmation", Required = Required.DisallowNull)]
			internal readonly bool RequiresMobileConfirmation;

			[JsonConstructor]
			private TradeOfferAcceptResponse() { }
		}

		internal sealed class TradeOfferSendRequest {
			[JsonProperty(PropertyName = "me", Required = Required.Always)]
			internal readonly ItemList ItemsToGive = new ItemList();

			[JsonProperty(PropertyName = "them", Required = Required.Always)]
			internal readonly ItemList ItemsToReceive = new ItemList();

			internal sealed class ItemList {
				[JsonProperty(PropertyName = "assets", Required = Required.Always)]
				internal readonly HashSet<Asset> Assets = new HashSet<Asset>();
			}
		}

		[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
		internal sealed class TradeOfferSendResponse {
			[JsonProperty(PropertyName = "needs_mobile_confirmation", Required = Required.DisallowNull)]
			internal readonly bool RequiresMobileConfirmation;

			internal ulong TradeOfferID { get; private set; }

#pragma warning disable IDE0051
			[JsonProperty(PropertyName = "tradeofferid", Required = Required.Always)]
			private string TradeOfferIDText {
				set {
					if (string.IsNullOrEmpty(value)) {
						ASF.ArchiLogger.LogNullError(nameof(value));

						return;
					}

					if (!ulong.TryParse(value, out ulong tradeOfferID) || (tradeOfferID == 0)) {
						ASF.ArchiLogger.LogNullError(nameof(tradeOfferID));

						return;
					}

					TradeOfferID = tradeOfferID;
				}
			}
#pragma warning restore IDE0051

			[JsonConstructor]
			private TradeOfferSendResponse() { }
		}

		[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
		internal sealed class UserPrivacy {
			[JsonProperty(PropertyName = "eCommentPermission", Required = Required.Always)]
			internal readonly ECommentPermission CommentPermission;

			[JsonProperty(PropertyName = "PrivacySettings", Required = Required.Always)]
			internal readonly PrivacySettings? Settings;

			// Constructed from privacy change request
			internal UserPrivacy(PrivacySettings settings, ECommentPermission commentPermission) {
				Settings = settings ?? throw new ArgumentNullException(nameof(settings));
				CommentPermission = commentPermission;
			}

			[JsonConstructor]
			private UserPrivacy() { }

			internal sealed class PrivacySettings {
				[JsonProperty(PropertyName = "PrivacyFriendsList", Required = Required.Always)]
				internal readonly ArchiHandler.EPrivacySetting FriendsList;

				[JsonProperty(PropertyName = "PrivacyInventory", Required = Required.Always)]
				internal readonly ArchiHandler.EPrivacySetting Inventory;

				[JsonProperty(PropertyName = "PrivacyInventoryGifts", Required = Required.Always)]
				internal readonly ArchiHandler.EPrivacySetting InventoryGifts;

				[JsonProperty(PropertyName = "PrivacyOwnedGames", Required = Required.Always)]
				internal readonly ArchiHandler.EPrivacySetting OwnedGames;

				[JsonProperty(PropertyName = "PrivacyPlaytime", Required = Required.Always)]
				internal readonly ArchiHandler.EPrivacySetting Playtime;

				[JsonProperty(PropertyName = "PrivacyProfile", Required = Required.Always)]
				internal readonly ArchiHandler.EPrivacySetting Profile;

				// Constructed from privacy change request
				internal PrivacySettings(ArchiHandler.EPrivacySetting profile, ArchiHandler.EPrivacySetting ownedGames, ArchiHandler.EPrivacySetting playtime, ArchiHandler.EPrivacySetting friendsList, ArchiHandler.EPrivacySetting inventory, ArchiHandler.EPrivacySetting inventoryGifts) {
					if ((profile == ArchiHandler.EPrivacySetting.Unknown) || (ownedGames == ArchiHandler.EPrivacySetting.Unknown) || (playtime == ArchiHandler.EPrivacySetting.Unknown) || (friendsList == ArchiHandler.EPrivacySetting.Unknown) || (inventory == ArchiHandler.EPrivacySetting.Unknown) || (inventoryGifts == ArchiHandler.EPrivacySetting.Unknown)) {
						throw new ArgumentNullException(nameof(profile) + " || " + nameof(ownedGames) + " || " + nameof(playtime) + " || " + nameof(friendsList) + " || " + nameof(inventory) + " || " + nameof(inventoryGifts));
					}

					Profile = profile;
					OwnedGames = ownedGames;
					Playtime = playtime;
					FriendsList = friendsList;
					Inventory = inventory;
					InventoryGifts = inventoryGifts;
				}

				[JsonConstructor]
				private PrivacySettings() { }
			}

			internal enum ECommentPermission : byte {
				FriendsOnly,
				Public,
				Private
			}
		}
	}
}
