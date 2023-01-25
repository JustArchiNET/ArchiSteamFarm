//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2023 Łukasz "JustArchi" Domeradzki
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

using System.Diagnostics.CodeAnalysis;
using ArchiSteamFarm.Core;
using Newtonsoft.Json;

namespace ArchiSteamFarm.Steam.Data;

[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
internal sealed class TradeOfferSendResponse {
	[JsonProperty("strError", Required = Required.DisallowNull)]
	internal readonly string ErrorText = "";

	[JsonProperty("needs_mobile_confirmation", Required = Required.DisallowNull)]
	internal readonly bool RequiresMobileConfirmation;

	internal ulong TradeOfferID { get; private set; }

	[JsonProperty("tradeofferid", Required = Required.DisallowNull)]
	private string TradeOfferIDText {
		set {
			if (string.IsNullOrEmpty(value)) {
				ASF.ArchiLogger.LogNullError(value);

				return;
			}

			if (!ulong.TryParse(value, out ulong tradeOfferID) || (tradeOfferID == 0)) {
				ASF.ArchiLogger.LogNullError(tradeOfferID);

				return;
			}

			TradeOfferID = tradeOfferID;
		}
	}

	[JsonConstructor]
	private TradeOfferSendResponse() { }
}
