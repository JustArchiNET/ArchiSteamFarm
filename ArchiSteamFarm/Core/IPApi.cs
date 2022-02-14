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
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Threading.Tasks;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Web.Responses;
using Newtonsoft.Json;

namespace ArchiSteamFarm.Core;

internal static class IPApi {
	private static Uri URL => new("http://ip-api.com");

	internal static async Task<string?> GetOriginCountry() {
		if (ASF.WebBrowser == null) {
			throw new InvalidOperationException(nameof(ASF.WebBrowser));
		}

		Uri request = new(URL, "/json?fields=country,status");

		ObjectResponse<ApiResponse>? response = await ASF.WebBrowser.UrlGetToJsonObject<ApiResponse>(request).ConfigureAwait(false);

		return response?.Content.Success == true ? response.Content.Country : null;
	}

	[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
	private sealed class ApiResponse {
#pragma warning disable CS0649 // False positive, the field is used during json deserialization
		[JsonProperty(PropertyName = "country", Required = Required.DisallowNull)]
		internal readonly string? Country;
#pragma warning restore CS0649 // False positive, the field is used during json deserialization

		internal bool Success { get; private set; }

		[JsonProperty(PropertyName = "status", Required = Required.Always)]
		private string Status {
			set {
				if (string.IsNullOrEmpty(value)) {
					ASF.ArchiLogger.LogNullError(nameof(value));

					return;
				}

				switch (value) {
					case "success":
						Success = true;

						break;
					case "fail":
						Success = false;

						break;
					default:
						ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(value), value));

						break;
				}
			}
		}

		[JsonConstructor]
		private ApiResponse() { }
	}
}
