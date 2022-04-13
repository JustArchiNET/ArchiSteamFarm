//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2022 Łukasz "JustArchi" Domeradzki
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
using System.Net;
using ArchiSteamFarm.Core;
using SteamKit2;

namespace ArchiSteamFarm.Steam.Integration;

internal static class SteamUtilities {
	internal static Dictionary<uint, string>? ParseItems(this SteamApps.PurchaseResponseCallback callback) {
		ArgumentNullException.ThrowIfNull(callback);

		List<KeyValue> lineItems = callback.PurchaseReceiptInfo["lineitems"].Children;

		if (lineItems.Count == 0) {
			return null;
		}

		Dictionary<uint, string> result = new(lineItems.Count);

		foreach (KeyValue lineItem in lineItems) {
			uint packageID = lineItem["PackageID"].AsUnsignedInteger();

			if (packageID == 0) {
				// Coupons have PackageID of -1 (don't ask me why)
				// We'll use ItemAppID in this case
				packageID = lineItem["ItemAppID"].AsUnsignedInteger();

				if (packageID == 0) {
					ASF.ArchiLogger.LogNullError(packageID);

					return null;
				}
			}

			string? gameName = lineItem["ItemDescription"].AsString();

			if (string.IsNullOrEmpty(gameName)) {
				ASF.ArchiLogger.LogNullError(gameName);

				return null;
			}

			// Apparently steam expects client to decode sent HTML
			gameName = WebUtility.HtmlDecode(gameName);
			result[packageID] = gameName;
		}

		return result;
	}
}
