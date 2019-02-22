//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2019 Łukasz "JustArchi" Domeradzki
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
using HtmlAgilityPack;
using JetBrains.Annotations;
using Newtonsoft.Json;
using SteamKit2;

namespace ArchiSteamFarm.Json {
	public static class Steam {
		// REF: https://developer.valvesoftware.com/wiki/Steam_Web_API/IEconService#CEcon_Asset
		public sealed class Asset {
			[PublicAPI]
			public const uint SteamAppID = 753;

			[PublicAPI]
			public const ulong SteamCommunityContextID = 6;

			[PublicAPI]
			public uint Amount { get; internal set; }

			[JsonProperty(PropertyName = "appid", Required = Required.DisallowNull)]
			public uint AppID { get; private set; }

			[PublicAPI]
			public ulong AssetID { get; private set; }

			[PublicAPI]
			public ulong ClassID { get; private set; }

			[PublicAPI]
			public ulong ContextID { get; private set; }

			[PublicAPI]
			public bool Marketable { get; internal set; }

			[PublicAPI]
			public ERarity Rarity { get; internal set; }

			[PublicAPI]
			public uint RealAppID { get; internal set; }

			[PublicAPI]
			public bool Tradable { get; internal set; }

			[PublicAPI]
			public EType Type { get; internal set; }

			[JsonProperty(PropertyName = "amount", Required = Required.Always)]
			[NotNull]
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

			[JsonProperty(PropertyName = "assetid", Required = Required.DisallowNull)]
			[NotNull]
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

			[JsonProperty(PropertyName = "classid", Required = Required.DisallowNull)]
			[NotNull]
			private string ClassIDText {
				get => ClassID.ToString();

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
			[NotNull]
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

			[JsonProperty(PropertyName = "id", Required = Required.DisallowNull)]
			[NotNull]
			private string IDText {
				get => AssetIDText;
				set => AssetIDText = value;
			}

			// Constructed from trades being received or plugins
			public Asset(uint appID, ulong contextID, ulong classID, uint amount, bool marketable = true, uint realAppID = 0, EType type = EType.Unknown, ERarity rarity = ERarity.Unknown) {
				if ((appID == 0) || (contextID == 0) || (classID == 0) || (amount == 0)) {
					throw new ArgumentNullException(nameof(appID) + " || " + nameof(contextID) + " || " + nameof(classID) + " || " + nameof(amount));
				}

				AppID = appID;
				ContextID = contextID;
				ClassID = classID;
				Amount = amount;
				Marketable = marketable;
				RealAppID = realAppID;
				Type = type;
				Rarity = rarity;
			}

			[JsonConstructor]
			private Asset() { }

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
				ProfileModifier
			}
		}

		[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
		public class BooleanResponse {
			[JsonProperty(PropertyName = "success", Required = Required.Always)]
			public readonly bool Success;

			[JsonConstructor]
			protected BooleanResponse() { }
		}

		[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
		public sealed class ConfirmationDetails : BooleanResponse {
			internal MobileAuthenticator.Confirmation Confirmation { get; set; }
			internal ulong TradeOfferID { get; private set; }
			internal EType Type { get; private set; }

			[JsonProperty(PropertyName = "html", Required = Required.DisallowNull)]
			private string HTML {
				set {
					if (string.IsNullOrEmpty(value)) {
						ASF.ArchiLogger.LogNullError(nameof(value));

						return;
					}

					HtmlDocument htmlDocument = WebBrowser.StringToHtmlDocument(value);

					if (htmlDocument == null) {
						ASF.ArchiLogger.LogNullError(nameof(htmlDocument));

						return;
					}

					if (htmlDocument.DocumentNode.SelectSingleNode("//div[@class='mobileconf_trade_area']") != null) {
						Type = EType.Trade;

						HtmlNode tradeOfferNode = htmlDocument.DocumentNode.SelectSingleNode("//div[@class='tradeoffer']");

						if (tradeOfferNode == null) {
							ASF.ArchiLogger.LogNullError(nameof(tradeOfferNode));

							return;
						}

						string idText = tradeOfferNode.GetAttributeValue("id", null);

						if (string.IsNullOrEmpty(idText)) {
							ASF.ArchiLogger.LogNullError(nameof(idText));

							return;
						}

						int index = idText.IndexOf('_');

						if (index < 0) {
							ASF.ArchiLogger.LogNullError(nameof(index));

							return;
						}

						index++;

						if (idText.Length <= index) {
							ASF.ArchiLogger.LogNullError(nameof(idText.Length));

							return;
						}

						idText = idText.Substring(index);

						if (!ulong.TryParse(idText, out ulong tradeOfferID) || (tradeOfferID == 0)) {
							ASF.ArchiLogger.LogNullError(nameof(tradeOfferID));

							return;
						}

						TradeOfferID = tradeOfferID;
					} else if (htmlDocument.DocumentNode.SelectSingleNode("//div[@class='mobileconf_listing_prices']") != null) {
						Type = EType.Market;
					} else {
						// Normally this should be reported, but under some specific circumstances we might actually receive this one
						Type = EType.Generic;
					}
				}
			}

			[JsonConstructor]
			private ConfirmationDetails() { }

			// REF: Internal documentation
			[PublicAPI]
			public enum EType : byte {
				Unknown,
				Generic,
				Trade,
				Market,

				// We're missing information about definition of number 4 type
				ChangePhoneNumber = 5
			}
		}

		[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
		public class EResultResponse {
			[JsonProperty(PropertyName = "success", Required = Required.Always)]
			public readonly EResult Result;

			[JsonConstructor]
			protected EResultResponse() { }
		}

		[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
		public class NumberResponse {
			[PublicAPI]
			public bool Success { get; private set; }

			[JsonProperty(PropertyName = "success", Required = Required.Always)]
			private byte SuccessNumber {
				set {
					switch (value) {
						case 0:
							Success = false;

							break;
						case 1:
							Success = true;

							break;
						default:
							ASF.ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(value), value));

							return;
					}
				}
			}

			[JsonConstructor]
			protected NumberResponse() { }
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
				if ((tradeOfferID == 0) || (otherSteamID3 == 0) || (state == ETradeOfferState.Unknown)) {
					throw new ArgumentNullException(nameof(tradeOfferID) + " || " + nameof(otherSteamID3) + " || " + nameof(state));
				}

				TradeOfferID = tradeOfferID;
				OtherSteamID64 = new SteamID(otherSteamID3, EUniverse.Public, EAccountType.Individual);
				State = state;
			}

			[PublicAPI]
			public bool IsValidSteamItemsRequest(IReadOnlyCollection<Asset.EType> acceptedTypes) {
				if ((acceptedTypes == null) || (acceptedTypes.Count == 0)) {
					ASF.ArchiLogger.LogNullError(nameof(acceptedTypes));

					return false;
				}

				return ItemsToGive.All(item => (item.AppID == Asset.SteamAppID) && (item.ContextID == Asset.SteamCommunityContextID) && acceptedTypes.Contains(item.Type));
			}

			[PublicAPI]
			public enum ETradeOfferState : byte {
				Unknown,
				Invalid,
				Active,
				Accepted,
				Countered,
				Expired,
				Canceled,
				Declined,
				InvalidItems,
				EmailPending,
				EmailCanceled,
				OnHold
			}
		}

		[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
		internal sealed class InventoryResponse : NumberResponse {
			[JsonProperty(PropertyName = "assets", Required = Required.DisallowNull)]
			internal readonly ImmutableHashSet<Asset> Assets;

			[JsonProperty(PropertyName = "descriptions", Required = Required.DisallowNull)]
			internal readonly ImmutableHashSet<Description> Descriptions;

			[JsonProperty(PropertyName = "error", Required = Required.DisallowNull)]
			internal readonly string Error;

			[JsonProperty(PropertyName = "total_inventory_count", Required = Required.DisallowNull)]
			internal readonly uint TotalInventoryCount;

			internal ulong LastAssetID { get; private set; }
			internal bool MoreItems { get; private set; }

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

			[JsonProperty(PropertyName = "more_items", Required = Required.DisallowNull)]
			private byte MoreItemsNumber {
				set => MoreItems = value > 0;
			}

			[JsonConstructor]
			private InventoryResponse() { }

			internal sealed class Description {
				[JsonProperty(PropertyName = "appid", Required = Required.Always)]
				internal readonly uint AppID;

				internal ulong ClassID { get; private set; }
				internal bool Marketable { get; private set; }
				internal Asset.ERarity Rarity { get; private set; }
				internal uint RealAppID { get; private set; }
				internal bool Tradable { get; private set; }
				internal Asset.EType Type { get; private set; }

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

				[JsonProperty(PropertyName = "marketable", Required = Required.Always)]
				private byte MarketableNumber {
					set => Marketable = value > 0;
				}

				[JsonProperty(PropertyName = "tags", Required = Required.DisallowNull)]
				private ImmutableHashSet<Tag> Tags {
					set {
						if ((value == null) || (value.Count == 0)) {
							ASF.ArchiLogger.LogNullError(nameof(value));

							return;
						}

						(Type, Rarity, RealAppID) = InterpretTags(value);
					}
				}

				[JsonProperty(PropertyName = "tradable", Required = Required.Always)]
				private byte TradableNumber {
					set => Tradable = value > 0;
				}

				[JsonConstructor]
				private Description() { }

				internal static (Asset.EType Type, Asset.ERarity Rarity, uint RealAppID) InterpretTags(IReadOnlyCollection<Tag> tags) {
					if ((tags == null) || (tags.Count == 0)) {
						ASF.ArchiLogger.LogNullError(nameof(tags));

						return (Asset.EType.Unknown, Asset.ERarity.Unknown, 0);
					}

					Asset.EType type = Asset.EType.Unknown;
					Asset.ERarity rarity = Asset.ERarity.Unknown;
					uint realAppID = 0;

					foreach (Tag tag in tags) {
						switch (tag.Identifier) {
							case "cardborder":

								switch (tag.Value) {
									case "cardborder_0":
										type = Asset.EType.TradingCard;

										break;
									case "cardborder_1":
										type = Asset.EType.FoilTradingCard;

										break;
									default:
										ASF.ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(tag.Value), tag.Value));

										break;
								}

								break;
							case "droprate":

								switch (tag.Value) {
									case "droprate_0":
										rarity = Asset.ERarity.Common;

										break;
									case "droprate_1":
										rarity = Asset.ERarity.Uncommon;

										break;
									case "droprate_2":
										rarity = Asset.ERarity.Rare;

										break;
									default:
										ASF.ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(tag.Value), tag.Value));

										break;
								}

								break;
							case "Game":

								if ((tag.Value.Length <= 4) || !tag.Value.StartsWith("app_", StringComparison.Ordinal)) {
									ASF.ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(tag.Value), tag.Value));

									break;
								}

								string appIDText = tag.Value.Substring(4);

								if (!uint.TryParse(appIDText, out uint appID) || (appID == 0)) {
									ASF.ArchiLogger.LogNullError(nameof(appID));

									break;
								}

								realAppID = appID;

								break;
							case "item_class":

								switch (tag.Value) {
									case "item_class_2":

										// This is a fallback in case we'd have no cardborder available to interpret
										if (type == Asset.EType.Unknown) {
											type = Asset.EType.TradingCard;
										}

										break;
									case "item_class_3":
										type = Asset.EType.ProfileBackground;

										break;
									case "item_class_4":
										type = Asset.EType.Emoticon;

										break;
									case "item_class_5":
										type = Asset.EType.BoosterPack;

										break;
									case "item_class_6":
										type = Asset.EType.Consumable;

										break;
									case "item_class_7":
										type = Asset.EType.SteamGems;

										break;
									case "item_class_8":
										type = Asset.EType.ProfileModifier;

										break;
									case "item_class_10":
										type = Asset.EType.SaleItem;

										break;
									default:
										ASF.ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(tag.Value), tag.Value));

										break;
								}

								break;
						}
					}

					return (type, rarity, realAppID);
				}

				internal sealed class Tag {
					[JsonProperty(PropertyName = "category", Required = Required.Always)]
					internal readonly string Identifier;

					[JsonProperty(PropertyName = "internal_name", Required = Required.Always)]
					internal readonly string Value;

					internal Tag([NotNull] string identifier, [NotNull] string value) {
						if (string.IsNullOrEmpty(identifier) || string.IsNullOrEmpty(value)) {
							throw new ArgumentNullException(nameof(identifier) + " || " + nameof(value));
						}

						Identifier = identifier;
						Value = value;
					}

					[JsonConstructor]
					private Tag() { }
				}
			}
		}

		[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
		internal sealed class NewDiscoveryQueueResponse {
			[JsonProperty(PropertyName = "queue", Required = Required.Always)]
			internal readonly ImmutableHashSet<uint> Queue;

			[JsonConstructor]
			private NewDiscoveryQueueResponse() { }
		}

		[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
		internal sealed class RedeemWalletResponse : EResultResponse {
			[JsonProperty(PropertyName = "wallet", Required = Required.DisallowNull)]
			internal readonly InternalKeyDetails KeyDetails;

			[JsonProperty(PropertyName = "detail", Required = Required.DisallowNull)]
			internal readonly EPurchaseResultDetail? PurchaseResultDetail;

			[JsonProperty(PropertyName = "currency", Required = Required.DisallowNull)]
			internal readonly ECurrencyCode? WalletCurrencyCode;

			[JsonConstructor]
			private RedeemWalletResponse() { }

			internal sealed class InternalKeyDetails {
				[JsonProperty(PropertyName = "currencycode", Required = Required.Always)]
				internal readonly ECurrencyCode CurrencyCode;

				[JsonConstructor]
				private InternalKeyDetails() { }
			}
		}

		[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
		internal sealed class TradeOfferAcceptResponse {
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

			[JsonConstructor]
			private TradeOfferSendResponse() { }
		}

		[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
		internal sealed class UserPrivacy {
			[JsonProperty(PropertyName = "eCommentPermission", Required = Required.Always)]
			internal readonly ECommentPermission CommentPermission;

			[JsonProperty(PropertyName = "PrivacySettings", Required = Required.Always)]
			internal readonly PrivacySettings Settings;

			// Constructed from privacy change request
			internal UserPrivacy([NotNull] PrivacySettings settings, ECommentPermission commentPermission) {
				Settings = settings ?? throw new ArgumentNullException(nameof(settings));
				CommentPermission = commentPermission;
			}

			[JsonConstructor]
			private UserPrivacy() { }

			internal sealed class PrivacySettings {
				[JsonProperty(PropertyName = "PrivacyFriendsList", Required = Required.Always)]
				internal readonly EPrivacySetting FriendsList;

				[JsonProperty(PropertyName = "PrivacyInventory", Required = Required.Always)]
				internal readonly EPrivacySetting Inventory;

				[JsonProperty(PropertyName = "PrivacyInventoryGifts", Required = Required.Always)]
				internal readonly EPrivacySetting InventoryGifts;

				[JsonProperty(PropertyName = "PrivacyOwnedGames", Required = Required.Always)]
				internal readonly EPrivacySetting OwnedGames;

				[JsonProperty(PropertyName = "PrivacyPlaytime", Required = Required.Always)]
				internal readonly EPrivacySetting Playtime;

				[JsonProperty(PropertyName = "PrivacyProfile", Required = Required.Always)]
				internal readonly EPrivacySetting Profile;

				// Constructed from privacy change request
				internal PrivacySettings(EPrivacySetting profile, EPrivacySetting ownedGames, EPrivacySetting playtime, EPrivacySetting friendsList, EPrivacySetting inventory, EPrivacySetting inventoryGifts) {
					if ((profile == EPrivacySetting.Unknown) || (ownedGames == EPrivacySetting.Unknown) || (playtime == EPrivacySetting.Unknown) || (friendsList == EPrivacySetting.Unknown) || (inventory == EPrivacySetting.Unknown) || (inventoryGifts == EPrivacySetting.Unknown)) {
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

				internal enum EPrivacySetting : byte {
					Unknown,
					Private,
					FriendsOnly,
					Public
				}
			}

			internal enum ECommentPermission : byte {
				FriendsOnly,
				Public,
				Private
			}
		}
	}
}
