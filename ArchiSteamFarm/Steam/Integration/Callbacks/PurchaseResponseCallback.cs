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
using System.IO;
using System.Net;
using ArchiSteamFarm.Core;
using JetBrains.Annotations;
using SteamKit2;
using SteamKit2.Internal;

namespace ArchiSteamFarm.Steam.Integration.Callbacks {
	public sealed class PurchaseResponseCallback : CallbackMsg {
		[PublicAPI]
		public Dictionary<uint, string>? Items { get; }

		public EPurchaseResultDetail PurchaseResultDetail { get; internal set; }

		[PublicAPI]
		public EResult Result { get; internal set; }

		internal PurchaseResponseCallback(EResult result, EPurchaseResultDetail purchaseResult) {
			if (!Enum.IsDefined(typeof(EResult), result)) {
				throw new InvalidEnumArgumentException(nameof(result), (int) result, typeof(EResult));
			}

			if (!Enum.IsDefined(typeof(EPurchaseResultDetail), purchaseResult)) {
				throw new InvalidEnumArgumentException(nameof(purchaseResult), (int) purchaseResult, typeof(EPurchaseResultDetail));
			}

			Result = result;
			PurchaseResultDetail = purchaseResult;
		}

		internal PurchaseResponseCallback(JobID jobID, CMsgClientPurchaseResponse msg) {
			if (jobID == null) {
				throw new ArgumentNullException(nameof(jobID));
			}

			if (msg == null) {
				throw new ArgumentNullException(nameof(msg));
			}

			JobID = jobID;
			PurchaseResultDetail = (EPurchaseResultDetail) msg.purchase_result_details;
			Result = (EResult) msg.eresult;

			if (msg.purchase_receipt_info == null) {
				ASF.ArchiLogger.LogNullError(nameof(msg.purchase_receipt_info));

				return;
			}

			KeyValue receiptInfo = new();

			using (MemoryStream ms = new(msg.purchase_receipt_info)) {
				if (!receiptInfo.TryReadAsBinary(ms)) {
					ASF.ArchiLogger.LogNullError(nameof(ms));

					return;
				}
			}

			List<KeyValue> lineItems = receiptInfo["lineitems"].Children;

			if (lineItems.Count == 0) {
				return;
			}

			Items = new Dictionary<uint, string>(lineItems.Count);

			foreach (KeyValue lineItem in lineItems) {
				uint packageID = lineItem["PackageID"].AsUnsignedInteger();

				if (packageID == 0) {
					// Coupons have PackageID of -1 (don't ask me why)
					// We'll use ItemAppID in this case
					packageID = lineItem["ItemAppID"].AsUnsignedInteger();

					if (packageID == 0) {
						ASF.ArchiLogger.LogNullError(nameof(packageID));

						return;
					}
				}

				string? gameName = lineItem["ItemDescription"].AsString();

				if (string.IsNullOrEmpty(gameName)) {
					ASF.ArchiLogger.LogNullError(nameof(gameName));

					return;
				}

				// Apparently steam expects client to decode sent HTML
				gameName = WebUtility.HtmlDecode(gameName);
				Items[packageID] = gameName;
			}
		}
	}
}
