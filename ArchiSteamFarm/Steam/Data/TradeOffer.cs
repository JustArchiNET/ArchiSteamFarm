//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2021 Łukasz "JustArchi" Domeradzki
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
using System.ComponentModel;
using System.Linq;
using JetBrains.Annotations;
using SteamKit2;

namespace ArchiSteamFarm.Steam.Data {
	// REF: https://developer.valvesoftware.com/wiki/Steam_Web_API/IEconService#CEcon_TradeOffer
	public sealed class TradeOffer {
		[PublicAPI]
		public IReadOnlyCollection<Asset> ItemsToGiveReadOnly => ItemsToGive;

		[PublicAPI]
		public IReadOnlyCollection<Asset> ItemsToReceiveReadOnly => ItemsToReceive;

		internal readonly HashSet<Asset> ItemsToGive = new();
		internal readonly HashSet<Asset> ItemsToReceive = new();

		[PublicAPI]
		public ulong OtherSteamID64 { get; private set; }

		[PublicAPI]
		public ETradeOfferState State { get; private set; }

		[PublicAPI]
		public ulong TradeOfferID { get; private set; }

		// Constructed from trades being received
		internal TradeOffer(ulong tradeOfferID, uint otherSteamID3, ETradeOfferState state) {
			if (tradeOfferID == 0) {
				throw new ArgumentOutOfRangeException(nameof(tradeOfferID));
			}

			if (otherSteamID3 == 0) {
				throw new ArgumentOutOfRangeException(nameof(otherSteamID3));
			}

			if (!Enum.IsDefined(typeof(ETradeOfferState), state)) {
				throw new InvalidEnumArgumentException(nameof(state), (int) state, typeof(ETradeOfferState));
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
}
