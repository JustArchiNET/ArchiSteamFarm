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

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Newtonsoft.Json;
using SteamKit2;

namespace ArchiSteamFarm.JSON {
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

			// ReSharper disable once UnusedMember.Local
			[JsonProperty(PropertyName = "appid", Required = Required.DisallowNull)]
			private string AppIDString {
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

			// ReSharper disable once UnusedMember.Local
			[JsonProperty(PropertyName = "contextid", Required = Required.DisallowNull)]
			private string ContextIDString {
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
			private string AssetIDString {
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

			// ReSharper disable once UnusedMember.Local
			[JsonProperty(PropertyName = "id", Required = Required.DisallowNull)]
			private string ID {
				get { return AssetIDString; }
				set { AssetIDString = value; }
			}

			internal ulong ClassID { get; set; }

			// ReSharper disable once UnusedMember.Local
			[JsonProperty(PropertyName = "classid", Required = Required.DisallowNull)]
			private string ClassIDString {
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

			// ReSharper disable once UnusedMember.Local
			[JsonProperty(PropertyName = "instanceid", Required = Required.DisallowNull)]
			private string InstanceIDString {
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

			// ReSharper disable once UnusedMember.Local
			[JsonProperty(PropertyName = "amount", Required = Required.Always)]
			private string AmountString {
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

			internal ulong TradeOfferID { get; set; }

			// ReSharper disable once UnusedMember.Local
			[JsonProperty(PropertyName = "tradeofferid", Required = Required.Always)]
			private string TradeOfferIDString {
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
			internal uint OtherSteamID3 { private get; set; }

			[JsonProperty(PropertyName = "trade_offer_state", Required = Required.Always)]
			internal ETradeOfferState State { get; set; }

			[JsonProperty(PropertyName = "items_to_give", Required = Required.Always)]
			internal HashSet<Item> ItemsToGive { get; } = new HashSet<Item>();

			[JsonProperty(PropertyName = "items_to_receive", Required = Required.Always)]
			internal HashSet<Item> ItemsToReceive { get; } = new HashSet<Item>();

			// Extra
			internal ulong OtherSteamID64 => OtherSteamID3 == 0 ? 0 : new SteamID(OtherSteamID3, EUniverse.Public, EAccountType.Individual);

			internal bool IsSteamCardsOnlyTradeForUs() => ItemsToGive.All(item => (item.AppID == Item.SteamAppID) && (item.ContextID == Item.SteamContextID) && ((item.Type == Item.EType.FoilTradingCard) || (item.Type == Item.EType.TradingCard)));

			internal bool IsPotentiallyDupesTradeForUs() {
				Dictionary<uint, Dictionary<Item.EType, uint>> itemsToGivePerGame = new Dictionary<uint, Dictionary<Item.EType, uint>>();
				foreach (Item item in ItemsToGive) {
					Dictionary<Item.EType, uint> itemsPerType;
					if (!itemsToGivePerGame.TryGetValue(item.RealAppID, out itemsPerType)) {
						itemsPerType = new Dictionary<Item.EType, uint> { [item.Type] = item.Amount };
						itemsToGivePerGame[item.RealAppID] = itemsPerType;
					} else {
						uint amount;
						if (itemsPerType.TryGetValue(item.Type, out amount)) {
							itemsPerType[item.Type] = amount + item.Amount;
						} else {
							itemsPerType[item.Type] = item.Amount;
						}
					}
				}

				Dictionary<uint, Dictionary<Item.EType, uint>> itemsToReceivePerGame = new Dictionary<uint, Dictionary<Item.EType, uint>>();
				foreach (Item item in ItemsToReceive) {
					Dictionary<Item.EType, uint> itemsPerType;
					if (!itemsToReceivePerGame.TryGetValue(item.RealAppID, out itemsPerType)) {
						itemsPerType = new Dictionary<Item.EType, uint> { [item.Type] = item.Amount };
						itemsToReceivePerGame[item.RealAppID] = itemsPerType;
					} else {
						uint amount;
						if (itemsPerType.TryGetValue(item.Type, out amount)) {
							itemsPerType[item.Type] = amount + item.Amount;
						} else {
							itemsPerType[item.Type] = item.Amount;
						}
					}
				}

				// Ensure that amount of items to give is at least amount of items to receive (per game and per type)
				foreach (KeyValuePair<uint, Dictionary<Item.EType, uint>> itemsPerGame in itemsToGivePerGame) {
					Dictionary<Item.EType, uint> otherItemsPerType;
					if (!itemsToReceivePerGame.TryGetValue(itemsPerGame.Key, out otherItemsPerType)) {
						return false;
					}

					foreach (KeyValuePair<Item.EType, uint> itemsPerType in itemsPerGame.Value) {
						uint otherAmount;
						if (!otherItemsPerType.TryGetValue(itemsPerType.Key, out otherAmount)) {
							return false;
						}

						if (itemsPerType.Value > otherAmount) {
							return false;
						}
					}
				}

				return true;
			}
		}

		[SuppressMessage("ReSharper", "UnusedMember.Global")]
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
