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
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Headers;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace ArchiSteamFarm.IPC.Integration {
	[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
	internal sealed class LocalizationMiddleware {
		internal static readonly ImmutableDictionary<string, string> CultureConversions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { "lol-US", SharedInfo.LolcatCultureName }, { "sr-CS", "sr-Latn" } }.ToImmutableDictionary();

		private readonly RequestDelegate Next;

		public LocalizationMiddleware(RequestDelegate next) => Next = next ?? throw new ArgumentNullException(nameof(next));

		[UsedImplicitly]
		public async Task InvokeAsync(HttpContext context) {
			if (context == null) {
				throw new ArgumentNullException(nameof(context));
			}

			RequestHeaders headers = context.Request.GetTypedHeaders();

			IList<StringWithQualityHeaderValue>? acceptLanguageHeader = headers.AcceptLanguage;

			if ((acceptLanguageHeader == null) || (acceptLanguageHeader.Count == 0)) {
				await Next(context).ConfigureAwait(false);

				return;
			}

			bool valuesChanged = false;

			for (int i = 0; i < acceptLanguageHeader.Count; i++) {
				StringSegment language = acceptLanguageHeader[i].Value;

				if (!language.HasValue || string.IsNullOrEmpty(language.Value)) {
					continue;
				}

				if (!CultureConversions.TryGetValue(language.Value, out string? replacement) || string.IsNullOrEmpty(replacement)) {
					continue;
				}

				acceptLanguageHeader[i] = StringWithQualityHeaderValue.Parse(replacement);
				valuesChanged = true;
			}

			if (valuesChanged) {
				// The getter returns a temporary collection; To make sure our changes are persisted, we need to assign it back
				headers.AcceptLanguage = acceptLanguageHeader;
			}

			await Next(context).ConfigureAwait(false);
		}
	}
}
