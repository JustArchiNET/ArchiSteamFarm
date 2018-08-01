//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// 
// Copyright 2015-2018 Łukasz "JustArchi" Domeradzki
// Contact: JustArchi@JustArchi.net
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
// http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using ArchiSteamFarm.Localization;
using HtmlAgilityPack;
using Newtonsoft.Json;
using SteamKit2;

namespace ArchiSteamFarm.Json {
	internal static class Steam {
		// REF: https://developer.valvesoftware.com/wiki/Steam_Web_API/IEconService#CEcon_Asset
		internal sealed class Asset {
			internal const ushort SteamAppID = 753;
			internal const byte SteamCommunityContextID = 6;

			internal uint Amount { get; set; }

			[JsonProperty(PropertyName = "appid", Required = Required.DisallowNull)]
			internal uint AppID { get; private set; }

			internal ulong AssetID { get; private set; }
			internal ulong ClassID { get; private set; }
			internal ulong ContextID { get; private set; }
			internal uint RealAppID { get; set; }
			internal EType Type { get; set; }

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

			[JsonProperty(PropertyName = "classid", Required = Required.DisallowNull)]
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
			private string IDText {
				get => AssetIDText;
				set => AssetIDText = value;
			}

			// Constructed from trades being received
			internal Asset(uint appID, ulong contextID, ulong classID, uint amount, uint realAppID, EType type = EType.Unknown) {
				if ((appID == 0) || (contextID == 0) || (classID == 0) || (amount == 0) || (realAppID == 0)) {
					throw new ArgumentNullException(nameof(classID) + " || " + nameof(contextID) + " || " + nameof(classID) + " || " + nameof(amount) + " || " + nameof(realAppID));
				}

				AppID = appID;
				ContextID = contextID;
				ClassID = classID;
				Amount = amount;
				RealAppID = realAppID;
				Type = type;
			}

			// Deserialized from JSON
			private Asset() { }

			internal enum EType : byte {
				Unknown,
				BoosterPack,
				Emoticon,
				FoilTradingCard,
				ProfileBackground,
				TradingCard,
				SteamGems
			}
		}

		[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
		internal class BooleanResponse {
			[JsonProperty(PropertyName = "success", Required = Required.Always)]
			internal readonly bool Success;

			// Deserialized from JSON
			protected BooleanResponse() { }
		}

		[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
		internal sealed class ConfirmationDetails : BooleanResponse {
			internal ulong OtherSteamID64 {
				get {
					if (_OtherSteamID64 != 0) {
						return _OtherSteamID64;
					}

					if ((Type != EType.Trade) || (OtherSteamID3 == 0)) {
						return 0;
					}

					_OtherSteamID64 = new SteamID(OtherSteamID3, EUniverse.Public, EAccountType.Individual);
					return _OtherSteamID64;
				}
			}

			internal ulong TradeOfferID {
				get {
					if (_TradeOfferID != 0) {
						return _TradeOfferID;
					}

					if ((Type != EType.Trade) || (DocumentNode == null)) {
						return 0;
					}

					HtmlNode htmlNode = DocumentNode.SelectSingleNode("//div[@class='tradeoffer']");
					if (htmlNode == null) {
						ASF.ArchiLogger.LogNullError(nameof(htmlNode));
						return 0;
					}

					string idText = htmlNode.GetAttributeValue("id", null);
					if (string.IsNullOrEmpty(idText)) {
						ASF.ArchiLogger.LogNullError(nameof(idText));
						return 0;
					}

					int index = idText.IndexOf('_');
					if (index < 0) {
						ASF.ArchiLogger.LogNullError(nameof(index));
						return 0;
					}

					index++;
					if (idText.Length <= index) {
						ASF.ArchiLogger.LogNullError(nameof(idText.Length));
						return 0;
					}

					idText = idText.Substring(index);
					if (!ulong.TryParse(idText, out _TradeOfferID) || (_TradeOfferID == 0)) {
						ASF.ArchiLogger.LogNullError(nameof(_TradeOfferID));
						return 0;
					}

					return _TradeOfferID;
				}
			}

			internal EType Type {
				get {
					if (_Type != EType.Unknown) {
						return _Type;
					}

					if (DocumentNode == null) {
						return EType.Unknown;
					}

					if (DocumentNode.SelectSingleNode("//div[@class='mobileconf_listing_prices']") != null) {
						_Type = EType.Market;
						return _Type;
					}

					if (DocumentNode.SelectSingleNode("//div[@class='mobileconf_trade_area']") != null) {
						_Type = EType.Trade;
						return _Type;
					}

					// Normally this should be reported, but under some specific fuckups we might actually receive this one
					_Type = EType.Generic;
					return _Type;
				}
			}

#pragma warning disable 649
			[JsonProperty(PropertyName = "html", Required = Required.DisallowNull)]
			private readonly string HTML;
#pragma warning restore 649

			private HtmlNode DocumentNode {
				get {
					if (_DocumentNode != null) {
						return _DocumentNode;
					}

					if (string.IsNullOrEmpty(HTML)) {
						return null;
					}

					HtmlDocument htmlDocument = WebBrowser.StringToHtmlDocument(HTML);
					if (htmlDocument == null) {
						ASF.ArchiLogger.LogNullError(nameof(htmlDocument));
						return null;
					}

					_DocumentNode = htmlDocument.DocumentNode;
					return _DocumentNode;
				}
			}

			private uint OtherSteamID3 {
				get {
					if (_OtherSteamID3 != 0) {
						return _OtherSteamID3;
					}

					if ((Type != EType.Trade) || (DocumentNode == null)) {
						return 0;
					}

					HtmlNode htmlNode = DocumentNode.SelectSingleNode("//a/@data-miniprofile");
					if (htmlNode == null) {
						ASF.ArchiLogger.LogNullError(nameof(htmlNode));
						return 0;
					}

					string miniProfile = htmlNode.GetAttributeValue("data-miniprofile", null);
					if (string.IsNullOrEmpty(miniProfile)) {
						ASF.ArchiLogger.LogNullError(nameof(miniProfile));
						return 0;
					}

					if (!uint.TryParse(miniProfile, out _OtherSteamID3) || (_OtherSteamID3 == 0)) {
						ASF.ArchiLogger.LogNullError(nameof(_OtherSteamID3));
						return 0;
					}

					return _OtherSteamID3;
				}
			}

			internal MobileAuthenticator.Confirmation Confirmation {
				get => _Confirmation;

				set {
					if (value == null) {
						ASF.ArchiLogger.LogNullError(nameof(value));
						return;
					}

					_Confirmation = value;
				}
			}

			private MobileAuthenticator.Confirmation _Confirmation;
			private HtmlNode _DocumentNode;
			private uint _OtherSteamID3;
			private ulong _OtherSteamID64;
			private ulong _TradeOfferID;
			private EType _Type;

			// Deserialized from JSON
			private ConfirmationDetails() { }

			// REF: Internal documentation
			[SuppressMessage("ReSharper", "UnusedMember.Global")]
			internal enum EType : byte {
				Unknown,
				Generic,
				Trade,
				Market,

				// We're missing information about definition of number 4 type
				ChangePhoneNumber = 5
			}
		}

		[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
		internal class EResultResponse {
			[JsonProperty(PropertyName = "success", Required = Required.Always)]
			internal readonly EResult Result;

			// Deserialized from JSON
			protected EResultResponse() { }
		}

		[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
		internal sealed class InventoryResponse : NumberResponse {
			[JsonProperty(PropertyName = "assets", Required = Required.DisallowNull)]
			internal readonly HashSet<Asset> Assets;

			[JsonProperty(PropertyName = "descriptions", Required = Required.DisallowNull)]
			internal readonly HashSet<Description> Descriptions;

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

			// Deserialized from JSON
			private InventoryResponse() { }

			internal sealed class Description {
				[JsonProperty(PropertyName = "appid", Required = Required.Always)]
				internal readonly uint AppID;

				[JsonProperty(PropertyName = "market_hash_name", Required = Required.Always)]
				internal readonly string MarketHashName;

				[JsonProperty(PropertyName = "type", Required = Required.Always)]
				internal readonly string Type;

				internal ulong ClassID { get; private set; }
				internal bool Tradable { get; private set; }

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

				[JsonProperty(PropertyName = "tradable", Required = Required.Always)]
				private byte TradableNumber {
					set => Tradable = value > 0;
				}

				// Deserialized from JSON
				private Description() { }
			}
		}

		[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
		internal sealed class NewDiscoveryQueueResponse {
			[JsonProperty(PropertyName = "queue", Required = Required.Always)]
			internal readonly HashSet<uint> Queue;

			// Deserialized from JSON
			private NewDiscoveryQueueResponse() { }
		}

		[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
		internal class NumberResponse {
			internal bool Success { get; private set; }

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

			// Deserialized from JSON
			protected NumberResponse() { }
		}

		[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
		internal sealed class RedeemWalletResponse : EResultResponse {
			[JsonProperty(PropertyName = "detail", Required = Required.DisallowNull)]
			internal readonly EPurchaseResultDetail? PurchaseResultDetail;

			// Deserialized from JSON
			private RedeemWalletResponse() { }
		}

		// REF: https://developer.valvesoftware.com/wiki/Steam_Web_API/IEconService#CEcon_TradeOffer
		internal sealed class TradeOffer {
			internal readonly HashSet<Asset> ItemsToGive = new HashSet<Asset>();
			internal readonly HashSet<Asset> ItemsToReceive = new HashSet<Asset>();
			internal readonly ETradeOfferState State;
			internal readonly ulong TradeOfferID;

			internal ulong OtherSteamID64 {
				get {
					if (_OtherSteamID64 != 0) {
						return _OtherSteamID64;
					}

					if (OtherSteamID3 == 0) {
						ASF.ArchiLogger.LogNullError(nameof(OtherSteamID3));
						return 0;
					}

					_OtherSteamID64 = new SteamID(OtherSteamID3, EUniverse.Public, EAccountType.Individual);
					return _OtherSteamID64;
				}
			}

			private readonly uint OtherSteamID3;

			private ulong _OtherSteamID64;

			// Constructed from trades being received
			internal TradeOffer(ulong tradeOfferID, uint otherSteamID3, ETradeOfferState state) {
				if ((tradeOfferID == 0) || (otherSteamID3 == 0) || (state == ETradeOfferState.Unknown)) {
					throw new ArgumentNullException(nameof(tradeOfferID) + " || " + nameof(otherSteamID3) + " || " + nameof(state));
				}

				TradeOfferID = tradeOfferID;
				OtherSteamID3 = otherSteamID3;
				State = state;
			}

			internal bool IsFairTypesExchange() {
				Dictionary<uint, Dictionary<Asset.EType, uint>> itemsToGivePerGame = new Dictionary<uint, Dictionary<Asset.EType, uint>>();
				foreach (Asset item in ItemsToGive) {
					if (!itemsToGivePerGame.TryGetValue(item.RealAppID, out Dictionary<Asset.EType, uint> itemsPerType)) {
						itemsPerType = new Dictionary<Asset.EType, uint> { [item.Type] = item.Amount };
						itemsToGivePerGame[item.RealAppID] = itemsPerType;
					} else {
						itemsPerType[item.Type] = itemsPerType.TryGetValue(item.Type, out uint amount) ? amount + item.Amount : item.Amount;
					}
				}

				Dictionary<uint, Dictionary<Asset.EType, uint>> itemsToReceivePerGame = new Dictionary<uint, Dictionary<Asset.EType, uint>>();
				foreach (Asset item in ItemsToReceive) {
					if (!itemsToReceivePerGame.TryGetValue(item.RealAppID, out Dictionary<Asset.EType, uint> itemsPerType)) {
						itemsPerType = new Dictionary<Asset.EType, uint> { { item.Type, item.Amount } };

						itemsToReceivePerGame[item.RealAppID] = itemsPerType;
					} else {
						itemsPerType[item.Type] = itemsPerType.TryGetValue(item.Type, out uint amount) ? amount + item.Amount : item.Amount;
					}
				}

				// Ensure that amount of items to give is at least amount of items to receive (per game and per type)
				foreach (KeyValuePair<uint, Dictionary<Asset.EType, uint>> itemsPerGame in itemsToGivePerGame) {
					if (!itemsToReceivePerGame.TryGetValue(itemsPerGame.Key, out Dictionary<Asset.EType, uint> otherItemsPerType)) {
						return false;
					}

					foreach (KeyValuePair<Asset.EType, uint> itemsPerType in itemsPerGame.Value) {
						if (!otherItemsPerType.TryGetValue(itemsPerType.Key, out uint otherAmount)) {
							return false;
						}

						if (itemsPerType.Value > otherAmount) {
							return false;
						}
					}
				}

				return true;
			}

			internal bool IsValidSteamItemsRequest(IReadOnlyCollection<Asset.EType> acceptedTypes) {
				if ((acceptedTypes == null) || (acceptedTypes.Count == 0)) {
					ASF.ArchiLogger.LogNullError(nameof(acceptedTypes));
					return false;
				}

				return ItemsToGive.All(item => (item.AppID == Asset.SteamAppID) && (item.ContextID == Asset.SteamCommunityContextID) && acceptedTypes.Contains(item.Type));
			}

			[SuppressMessage("ReSharper", "UnusedMember.Global")]
			internal enum ETradeOfferState : byte {
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

		internal sealed class TradeOfferRequest {
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
		internal sealed class UserPrivacy {
			[JsonProperty(PropertyName = "eCommentPermission", Required = Required.Always)]
			internal readonly ECommentPermission CommentPermission;

			[JsonProperty(PropertyName = "PrivacySettings", Required = Required.Always)]
			internal readonly PrivacySettings Settings;

			// Constructed from privacy change request
			internal UserPrivacy(PrivacySettings settings, ECommentPermission commentPermission) {
				Settings = settings ?? throw new ArgumentNullException(nameof(settings));
				CommentPermission = commentPermission;
			}

			// Deserialized from JSON
			private UserPrivacy() { }

			internal sealed class PrivacySettings {
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
				internal PrivacySettings(EPrivacySetting profile, EPrivacySetting ownedGames, EPrivacySetting playtime, EPrivacySetting inventory, EPrivacySetting inventoryGifts) {
					if ((profile == EPrivacySetting.Unknown) || (ownedGames == EPrivacySetting.Unknown) || (playtime == EPrivacySetting.Unknown) || (inventory == EPrivacySetting.Unknown) || (inventoryGifts == EPrivacySetting.Unknown)) {
						throw new ArgumentNullException(nameof(profile) + " || " + nameof(ownedGames) + " || " + nameof(playtime) + " || " + nameof(inventory) + " || " + nameof(inventoryGifts));
					}

					Profile = profile;
					OwnedGames = ownedGames;
					Playtime = playtime;
					Inventory = inventory;
					InventoryGifts = inventoryGifts;
				}

				// Deserialized from JSON
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
