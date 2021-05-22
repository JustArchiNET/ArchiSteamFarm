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
using System.Collections.Immutable;
using System.ComponentModel;
using System.Linq;
using ArchiSteamFarm.Steam.Data;
using JetBrains.Annotations;

namespace ArchiSteamFarm.Steam.Exchange {
	public sealed class ParseTradeResult {
		[PublicAPI]
		public EResult Result { get; }

		[PublicAPI]
		public ulong TradeOfferID { get; }

		internal readonly ImmutableHashSet<Asset.EType>? ReceivedItemTypes;

		internal ParseTradeResult(ulong tradeOfferID, EResult result, IReadOnlyCollection<Asset>? itemsToReceive = null) {
			if (tradeOfferID == 0) {
				throw new ArgumentOutOfRangeException(nameof(tradeOfferID));
			}

			if ((result == EResult.Unknown) || !Enum.IsDefined(typeof(EResult), result)) {
				throw new InvalidEnumArgumentException(nameof(result), (int) result, typeof(EResult));
			}

			TradeOfferID = tradeOfferID;
			Result = result;

			if (itemsToReceive?.Count > 0) {
				ReceivedItemTypes = itemsToReceive.Select(item => item.Type).ToImmutableHashSet();
			}
		}

		public enum EResult : byte {
			Unknown,
			Accepted,
			Blacklisted,
			Ignored,
			Rejected,
			TryAgain
		}
	}
}
