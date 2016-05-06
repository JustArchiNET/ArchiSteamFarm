/*
    _                _      _  ____   _                           _____
   / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
  / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
 / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
/_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|

 Copyright 2015-2016 Łukasz "JustArchi" Domeradzki
 Contact: JustArchi@JustArchi.net

 Licensed under the Apache License, Version 2.0 (the "License");
 you may not use this file except in compliance with the License.
 You may obtain a copy of the License at

 http://www.apache.org/licenses/LICENSE-2.0
					
 Unless required by applicable law or agreed to in writing, software
 distributed under the License is distributed on an "AS IS" BASIS,
 WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 See the License for the specific language governing permissions and
 limitations under the License.

*/

using Newtonsoft.Json;
using SteamKit2;
using System.Collections.Generic;

namespace ArchiSteamFarm {
	internal static class Steam {
		internal sealed class Item { // REF: https://developer.valvesoftware.com/wiki/Steam_Web_API/IEconService#CEcon_Asset
			internal const ushort SteamAppID = 753;
			internal const byte SteamContextID = 6;

			internal enum EType : byte {
				Unknown,

				BoosterPack,
				Coupon,
				Gift,
				SteamGems,

				Emoticon,
				FoilTradingCard,
				ProfileBackground,
				TradingCard
			}

			internal uint AppID { get; set; }

			[JsonProperty(PropertyName = "appid", Required = Required.DisallowNull)]
			internal string AppIDString {
				get {
					return AppID.ToString();
				}
				set {
					if (string.IsNullOrEmpty(value)) {
						return;
					}

					uint result;
					if (!uint.TryParse(value, out result)) {
						return;
					}

					AppID = result;
				}
			}

			internal ulong ContextID { get; set; }

			[JsonProperty(PropertyName = "contextid", Required = Required.DisallowNull)]
			internal string ContextIDString {
				get {
					return ContextID.ToString();
				}
				set {
					if (string.IsNullOrEmpty(value)) {
						return;
					}

					ulong result;
					if (!ulong.TryParse(value, out result)) {
						return;
					}

					ContextID = result;
				}
			}

			internal ulong AssetID { get; set; }

			[JsonProperty(PropertyName = "assetid", Required = Required.DisallowNull)]
			internal string AssetIDString {
				get {
					return AssetID.ToString();
				}
				set {
					if (string.IsNullOrEmpty(value)) {
						return;
					}

					ulong result;
					if (!ulong.TryParse(value, out result)) {
						return;
					}

					AssetID = result;
				}
			}

			[JsonProperty(PropertyName = "id", Required = Required.DisallowNull)]
			internal string ID {
				get { return AssetIDString; }
				set { AssetIDString = value; }
			}

			internal ulong ClassID { get; set; }

			[JsonProperty(PropertyName = "classid", Required = Required.DisallowNull)]
			internal string ClassIDString {
				get {
					return ClassID.ToString();
				}
				set {
					if (string.IsNullOrEmpty(value)) {
						return;
					}

					ulong result;
					if (!ulong.TryParse(value, out result)) {
						return;
					}

					ClassID = result;
				}
			}

			internal ulong InstanceID { get; set; }

			[JsonProperty(PropertyName = "instanceid", Required = Required.DisallowNull)]
			internal string InstanceIDString {
				get {
					return InstanceID.ToString();
				}
				set {
					if (string.IsNullOrEmpty(value)) {
						return;
					}

					ulong result;
					if (!ulong.TryParse(value, out result)) {
						return;
					}

					InstanceID = result;
				}
			}

			internal uint Amount { get; set; }

			[JsonProperty(PropertyName = "amount", Required = Required.Always)]
			internal string AmountString {
				get {
					return Amount.ToString();
				}
				set {
					if (string.IsNullOrEmpty(value)) {
						return;
					}

					uint result;
					if (!uint.TryParse(value, out result)) {
						return;
					}

					Amount = result;
				}
			}

			internal uint RealAppID { get; set; }
			internal EType Type { get; set; }
		}

		internal sealed class TradeOffer { // REF: https://developer.valvesoftware.com/wiki/Steam_Web_API/IEconService#CEcon_TradeOffer
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

			internal ulong TradeOfferID { get; set; }

			[JsonProperty(PropertyName = "tradeofferid", Required = Required.Always)]
			internal string TradeOfferIDString {
				get {
					return TradeOfferID.ToString();
				}
				set {
					if (string.IsNullOrEmpty(value)) {
						return;
					}

					ulong result;
					if (!ulong.TryParse(value, out result)) {
						return;
					}

					TradeOfferID = result;
				}
			}

			[JsonProperty(PropertyName = "accountid_other", Required = Required.Always)]
			internal uint OtherSteamID3 { get; set; }

			[JsonProperty(PropertyName = "trade_offer_state", Required = Required.Always)]
			internal ETradeOfferState State { get; set; }

			[JsonProperty(PropertyName = "items_to_give", Required = Required.Always)]
			internal HashSet<Item> ItemsToGive { get; } = new HashSet<Item>();

			[JsonProperty(PropertyName = "items_to_receive", Required = Required.Always)]
			internal HashSet<Item> ItemsToReceive { get; } = new HashSet<Item>();

			// Extra
			internal ulong OtherSteamID64 {
				get {
					if (OtherSteamID3 == 0) {
						return 0;
					}

					return new SteamID(OtherSteamID3, EUniverse.Public, EAccountType.Individual);
				}
				set {
					if (value == 0) {
						return;
					}

					OtherSteamID3 = new SteamID(value).AccountID;
				}
			}

			internal bool IsSteamCardsOnlyTradeForUs() {
				foreach (Item item in ItemsToGive) {
					if (item.AppID != Item.SteamAppID || item.ContextID != Item.SteamContextID || (item.Type != Item.EType.FoilTradingCard && item.Type != Item.EType.TradingCard)) {
						return false;
					}
				}

				return true;
			}

			internal bool IsPotentiallyDupesTradeForUs() {
				Dictionary<uint, Dictionary<Item.EType, uint>> ItemsToGivePerGame = new Dictionary<uint, Dictionary<Item.EType, uint>>();
				foreach (Item item in ItemsToGive) {
					Dictionary<Item.EType, uint> ItemsPerType;
					if (!ItemsToGivePerGame.TryGetValue(item.RealAppID, out ItemsPerType)) {
						ItemsPerType = new Dictionary<Item.EType, uint>();
						ItemsPerType[item.Type] = item.Amount;
						ItemsToGivePerGame[item.RealAppID] = ItemsPerType;
					} else {
						uint amount;
						if (ItemsPerType.TryGetValue(item.Type, out amount)) {
							ItemsPerType[item.Type] = amount + item.Amount;
						} else {
							ItemsPerType[item.Type] = item.Amount;
						}
					}
				}

				Dictionary<uint, Dictionary<Item.EType, uint>> ItemsToReceivePerGame = new Dictionary<uint, Dictionary<Item.EType, uint>>();
				foreach (Item item in ItemsToReceive) {
					Dictionary<Item.EType, uint> ItemsPerType;
					if (!ItemsToReceivePerGame.TryGetValue(item.RealAppID, out ItemsPerType)) {
						ItemsPerType = new Dictionary<Item.EType, uint>();
						ItemsPerType[item.Type] = item.Amount;
						ItemsToReceivePerGame[item.RealAppID] = ItemsPerType;
					} else {
						uint amount;
						if (ItemsPerType.TryGetValue(item.Type, out amount)) {
							ItemsPerType[item.Type] = amount + item.Amount;
						} else {
							ItemsPerType[item.Type] = item.Amount;
						}
					}
				}

				// Ensure that amount per type and per game matches
				foreach (KeyValuePair<uint, Dictionary<Item.EType, uint>> ItemsPerGame in ItemsToGivePerGame) {
					Dictionary<Item.EType, uint> otherItemsPerType;
					if (!ItemsToReceivePerGame.TryGetValue(ItemsPerGame.Key, out otherItemsPerType)) {
						return false;
					}

					foreach (KeyValuePair<Item.EType, uint> ItemsPerType in ItemsPerGame.Value) {
						uint otherAmount;
						if (!otherItemsPerType.TryGetValue(ItemsPerType.Key, out otherAmount)) {
							return false;
						}

						if (ItemsPerType.Value != otherAmount) {
							return false;
						}
					}
				}

				return true;
			}
		}

		internal sealed class TradeOfferRequest {
			internal sealed class ItemList {
				[JsonProperty(PropertyName = "assets", Required = Required.Always)]
				internal HashSet<Item> Assets { get; } = new HashSet<Item>();
			}

			[JsonProperty(PropertyName = "newversion", Required = Required.Always)]
			internal bool NewVersion { get; } = true;

			[JsonProperty(PropertyName = "version", Required = Required.Always)]
			internal byte Version { get; } = 2;

			[JsonProperty(PropertyName = "me", Required = Required.Always)]
			internal ItemList ItemsToGive { get; } = new ItemList();

			[JsonProperty(PropertyName = "them", Required = Required.Always)]
			internal ItemList ItemsToReceive { get; } = new ItemList();
		}
	}
}
