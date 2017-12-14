//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// 
//  Copyright 2015-2017 Łukasz "JustArchi" Domeradzki
//  Contact: JustArchi@JustArchi.net
// 
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
// 
//  http://www.apache.org/licenses/LICENSE-2.0
//      
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using HtmlAgilityPack;
using Newtonsoft.Json;
using SteamKit2;

namespace ArchiSteamFarm.JSON {
	internal static class Steam {
		// REF: https://developer.valvesoftware.com/wiki/Steam_Web_API/IEconService#CEcon_Asset
		internal sealed class Asset {
			internal const ushort SteamAppID = 753;
			internal const byte SteamCommunityContextID = 6;

			internal uint Amount { get; private set; }

			[JsonProperty(PropertyName = "appid", Required = Required.DisallowNull)]
			internal uint AppID { get; private set; }

			internal ulong AssetID { get; private set; }
			internal ulong ClassID { get; private set; }
			internal ulong ContextID { get; private set; }
			internal uint RealAppID { get; set; }
			internal EType Type { get; set; }

			[JsonProperty(PropertyName = "amount", Required = Required.Always)]
			private string AmountString {
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
			private string AssetIDString {
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
			private string ClassIDString {
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
			private string ContextIDString {
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
			private string ID {
				get => AssetIDString;
				set => AssetIDString = value;
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
		internal sealed class ConfirmationDetails {
			[JsonProperty(PropertyName = "success", Required = Required.Always)]
			internal readonly bool Success;

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

					if ((Type != EType.Trade) || (HtmlDocument == null)) {
						return 0;
					}

					HtmlNode htmlNode = HtmlDocument.DocumentNode.SelectSingleNode("//div[@class='tradeoffer']");
					if (htmlNode == null) {
						ASF.ArchiLogger.LogNullError(nameof(htmlNode));
						return 0;
					}

					string id = htmlNode.GetAttributeValue("id", null);
					if (string.IsNullOrEmpty(id)) {
						ASF.ArchiLogger.LogNullError(nameof(id));
						return 0;
					}

					int index = id.IndexOf('_');
					if (index < 0) {
						ASF.ArchiLogger.LogNullError(nameof(index));
						return 0;
					}

					index++;
					if (id.Length <= index) {
						ASF.ArchiLogger.LogNullError(nameof(id.Length));
						return 0;
					}

					id = id.Substring(index);
					if (ulong.TryParse(id, out _TradeOfferID) && (_TradeOfferID != 0)) {
						return _TradeOfferID;
					}

					ASF.ArchiLogger.LogNullError(nameof(_TradeOfferID));
					return 0;
				}
			}

#pragma warning disable 649
			[JsonProperty(PropertyName = "html", Required = Required.DisallowNull)]
			private readonly string HTML;
#pragma warning restore 649

			private HtmlDocument HtmlDocument {
				get {
					if (_HtmlDocument != null) {
						return _HtmlDocument;
					}

					if (string.IsNullOrEmpty(HTML)) {
						return null;
					}

					_HtmlDocument = WebBrowser.StringToHtmlDocument(HTML);
					return _HtmlDocument;
				}
			}

			private uint OtherSteamID3 {
				get {
					if (_OtherSteamID3 != 0) {
						return _OtherSteamID3;
					}

					if ((Type != EType.Trade) || (HtmlDocument == null)) {
						return 0;
					}

					HtmlNode htmlNode = HtmlDocument.DocumentNode.SelectSingleNode("//a/@data-miniprofile");
					if (htmlNode == null) {
						ASF.ArchiLogger.LogNullError(nameof(htmlNode));
						return 0;
					}

					string miniProfile = htmlNode.GetAttributeValue("data-miniprofile", null);
					if (string.IsNullOrEmpty(miniProfile)) {
						ASF.ArchiLogger.LogNullError(nameof(miniProfile));
						return 0;
					}

					if (uint.TryParse(miniProfile, out _OtherSteamID3) && (_OtherSteamID3 != 0)) {
						return _OtherSteamID3;
					}

					ASF.ArchiLogger.LogNullError(nameof(_OtherSteamID3));
					return 0;
				}
			}

			private EType Type {
				get {
					if (_Type != EType.Unknown) {
						return _Type;
					}

					if (HtmlDocument == null) {
						return EType.Unknown;
					}

					HtmlNode testNode = HtmlDocument.DocumentNode.SelectSingleNode("//div[@class='mobileconf_listing_prices']");
					if (testNode != null) {
						_Type = EType.Market;
						return _Type;
					}

					testNode = HtmlDocument.DocumentNode.SelectSingleNode("//div[@class='mobileconf_trade_area']");
					if (testNode != null) {
						_Type = EType.Trade;
						return _Type;
					}

					_Type = EType.Other;
					return _Type;
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
			private HtmlDocument _HtmlDocument;
			private uint _OtherSteamID3;
			private ulong _OtherSteamID64;
			private ulong _TradeOfferID;
			private EType _Type;

			// Deserialized from JSON
			private ConfirmationDetails() { }

			internal enum EType : byte {
				Unknown,
				Trade,
				Market,
				Other
			}
		}

		[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
		internal sealed class ConfirmationResponse {
			[JsonProperty(PropertyName = "success", Required = Required.Always)]
			internal readonly bool Success;

			// Deserialized from JSON
			private ConfirmationResponse() { }
		}

		[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
		internal sealed class GenericResponse {
			[JsonProperty(PropertyName = "success", Required = Required.Always)]
			internal readonly EResult Result;

			// Deserialized from JSON
			private GenericResponse() { }
		}

		[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
		internal sealed class InventoryResponse {
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
			internal bool Success { get; private set; }

			[JsonProperty(PropertyName = "last_assetid", Required = Required.DisallowNull)]
			private string LastAssetIDString {
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

			[JsonProperty(PropertyName = "success", Required = Required.Always)]
			private byte SuccessNumber {
				set => Success = value > 0;
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
				private string ClassIDString {
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
		internal sealed class RedeemWalletResponse {
			[JsonProperty(PropertyName = "detail", Required = Required.DisallowNull)]
			internal readonly EPurchaseResultDetail? PurchaseResultDetail;

			[JsonProperty(PropertyName = "success", Required = Required.Always)]
			internal readonly EResult Result;

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
						if (itemsPerType.TryGetValue(item.Type, out uint amount)) {
							itemsPerType[item.Type] = amount + item.Amount;
						} else {
							itemsPerType[item.Type] = item.Amount;
						}
					}
				}

				Dictionary<uint, Dictionary<Asset.EType, uint>> itemsToReceivePerGame = new Dictionary<uint, Dictionary<Asset.EType, uint>>();
				foreach (Asset item in ItemsToReceive) {
					if (!itemsToReceivePerGame.TryGetValue(item.RealAppID, out Dictionary<Asset.EType, uint> itemsPerType)) {
						itemsPerType = new Dictionary<Asset.EType, uint> { { item.Type, item.Amount } };

						itemsToReceivePerGame[item.RealAppID] = itemsPerType;
					} else {
						if (itemsPerType.TryGetValue(item.Type, out uint amount)) {
							itemsPerType[item.Type] = amount + item.Amount;
						} else {
							itemsPerType[item.Type] = item.Amount;
						}
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

				bool result = ItemsToGive.All(item => (item.AppID == Asset.SteamAppID) && (item.ContextID == Asset.SteamCommunityContextID) && acceptedTypes.Contains(item.Type));
				return result;
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
	}
}