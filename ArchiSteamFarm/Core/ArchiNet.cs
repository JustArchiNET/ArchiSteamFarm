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
using System.Threading.Tasks;
using ArchiSteamFarm.Web.Responses;
using Newtonsoft.Json;

namespace ArchiSteamFarm.Core;

internal static class ArchiNet {
	internal static Uri URL => new("https://asf-backend.JustArchi.net");

	internal static async Task<string?> FetchBuildChecksum(Version version, string variant) {
		ArgumentNullException.ThrowIfNull(version);

		if (string.IsNullOrEmpty(variant)) {
			throw new ArgumentNullException(nameof(variant));
		}

		if (ASF.WebBrowser == null) {
			throw new InvalidOperationException(nameof(ASF.WebBrowser));
		}

		Uri request = new(URL, $"/Api/Checksum/{version}/{variant}");

		ObjectResponse<ChecksumResponse>? response = await ASF.WebBrowser.UrlGetToJsonObject<ChecksumResponse>(request).ConfigureAwait(false);

		if (response?.Content == null) {
			return null;
		}

		return response.Content.Checksum ?? "";
	}

	[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
	private sealed class ChecksumResponse {
#pragma warning disable CS0649 // False positive, the field is used during json deserialization
		[JsonProperty("checksum", Required = Required.AllowNull)]
		internal readonly string? Checksum;
#pragma warning restore CS0649 // False positive, the field is used during json deserialization

		[JsonConstructor]
		private ChecksumResponse() { }
	}
}
