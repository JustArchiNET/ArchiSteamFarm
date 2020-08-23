//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2020 Łukasz "JustArchi" Domeradzki
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
using Newtonsoft.Json;

namespace ArchiSteamFarm.CustomPlugins.ExamplePlugin {
	// This is example class that shows how you can call third-party services within your plugin
	// You've always wanted from your ASF to post cats, right? Now is your chance!
	// P.S. The code is almost 1:1 copy from the one I use in ArchiBot, you can thank me later
	internal static class CatAPI {
		private const string URL = "https://aws.random.cat";

		internal static async Task<string?> GetRandomCatURL(WebBrowser webBrowser) {
			if (webBrowser == null) {
				throw new ArgumentNullException(nameof(webBrowser));
			}

			const string request = URL + "/meow";

			WebBrowser.ObjectResponse<MeowResponse>? response = await webBrowser.UrlGetToJsonObject<MeowResponse>(request).ConfigureAwait(false);

			if (response?.Content == null) {
				return null;
			}

			if (string.IsNullOrEmpty(response.Content.Link)) {
				throw new ArgumentNullException(nameof(response.Content.Link));
			}

			return Uri.EscapeUriString(response.Content!.Link!);
		}

		[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
		private sealed class MeowResponse {
#pragma warning disable 649
			[JsonProperty(PropertyName = "file", Required = Required.Always)]
			internal readonly string? Link;
#pragma warning restore 649

			[JsonConstructor]
			private MeowResponse() { }
		}
	}
}
