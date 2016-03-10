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
		internal sealed class Item {
			// REF: https://developer.valvesoftware.com/wiki/Steam_Web_API/IEconService#CEcon_Asset
			[JsonProperty(Required = Required.DisallowNull)]
			internal string appid { get; set; }

			[JsonProperty(Required = Required.DisallowNull)]
			internal string contextid { get; set; }

			[JsonProperty(Required = Required.DisallowNull)]
			internal string assetid { get; set; }

			[JsonProperty(Required = Required.DisallowNull)]
			internal string id {
				get { return assetid; }
				set { assetid = value; }
			}

			[JsonProperty(Required = Required.AllowNull)]
			internal string classid { get; set; }

			[JsonProperty(Required = Required.AllowNull)]
			internal string instanceid { get; set; }

			[JsonProperty(Required = Required.Always)]
			internal string amount { get; set; }
		}

		internal sealed class ItemList {
			[JsonProperty(Required = Required.Always)]
			internal List<Steam.Item> assets { get; } = new List<Steam.Item>();
		}

		internal sealed class TradeOffer {
			// REF: https://developer.valvesoftware.com/wiki/Steam_Web_API/IEconService#CEcon_TradeOffer
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

			[JsonProperty(Required = Required.Always)]
			internal string tradeofferid { get; set; }

			[JsonProperty(Required = Required.Always)]
			internal int accountid_other { get; set; }

			[JsonProperty(Required = Required.Always)]
			internal ETradeOfferState trade_offer_state { get; set; }

			[JsonProperty(Required = Required.Always)]
			internal List<Steam.Item> items_to_give { get; } = new List<Steam.Item>();

			[JsonProperty(Required = Required.Always)]
			internal List<Steam.Item> items_to_receive { get; } = new List<Steam.Item>();

			// Extra
			private ulong _OtherSteamID64 = 0;
			internal ulong OtherSteamID64 {
				get {
					if (_OtherSteamID64 == 0 && accountid_other != 0) {
						_OtherSteamID64 = new SteamID((uint) accountid_other, EUniverse.Public, EAccountType.Individual).ConvertToUInt64();
					}

					return _OtherSteamID64;
				}
			}
		}

		internal sealed class TradeOfferRequest {
			[JsonProperty(Required = Required.Always)]
			internal bool newversion { get; } = true;

			[JsonProperty(Required = Required.Always)]
			internal int version { get; } = 2;

			[JsonProperty(Required = Required.Always)]
			internal Steam.ItemList me { get; } = new Steam.ItemList();

			[JsonProperty(Required = Required.Always)]
			internal Steam.ItemList them { get; } = new Steam.ItemList();
		}
	}
}
