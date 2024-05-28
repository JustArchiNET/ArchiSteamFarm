// ----------------------------------------------------------------------------------------------
//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// ----------------------------------------------------------------------------------------------
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
using ArchiSteamFarm.Steam.Exchange;

namespace ArchiSteamFarm.OfficialPlugins.Monitoring;

internal sealed class TradeStatistics {
	private readonly object Lock = new();

	internal uint AcceptedOffers { get; private set; }
	internal uint BlacklistedOffers { get; private set; }
	internal uint ConfirmedOffers { get; private set; }
	internal uint IgnoredOffers { get; private set; }
	internal uint ItemsGiven { get; private set; }
	internal uint ItemsReceived { get; private set; }
	internal uint RejectedOffers { get; private set; }

	internal void Include(ParseTradeResult result) {
		ArgumentNullException.ThrowIfNull(result);

		lock (Lock) {
			switch (result.Result) {
				case ParseTradeResult.EResult.Accepted when result.Confirmed:
					ConfirmedOffers++;

					ItemsGiven += (uint) (result.ItemsToGive?.Count ?? 0);
					ItemsReceived += (uint) (result.ItemsToReceive?.Count ?? 0);

					goto case ParseTradeResult.EResult.Accepted;
				case ParseTradeResult.EResult.Accepted:
					AcceptedOffers++;

					break;
				case ParseTradeResult.EResult.Rejected:
					RejectedOffers++;

					break;
				case ParseTradeResult.EResult.Blacklisted:
					BlacklistedOffers++;

					break;
				case ParseTradeResult.EResult.Ignored:
					IgnoredOffers++;

					break;
			}
		}
	}
}
