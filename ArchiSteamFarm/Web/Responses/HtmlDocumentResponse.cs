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
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using ArchiSteamFarm.Core;
using JetBrains.Annotations;

namespace ArchiSteamFarm.Web.Responses {
	public sealed class HtmlDocumentResponse : BasicResponse, IDisposable {
		[PublicAPI]
		public IDocument Content { get; }

		private HtmlDocumentResponse(BasicResponse basicResponse, IDocument content) : base(basicResponse) {
			if (basicResponse == null) {
				throw new ArgumentNullException(nameof(basicResponse));
			}

			Content = content ?? throw new ArgumentNullException(nameof(content));
		}

		public void Dispose() => Content.Dispose();

		[PublicAPI]
		public static async Task<HtmlDocumentResponse?> Create(StreamResponse streamResponse) {
			if (streamResponse == null) {
				throw new ArgumentNullException(nameof(streamResponse));
			}

			IBrowsingContext context = BrowsingContext.New();

			try {
				IDocument document = await context.OpenAsync(req => req.Content(streamResponse.Content, true)).ConfigureAwait(false);

				return new HtmlDocumentResponse(streamResponse, document);
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericWarningException(e);

				return null;
			}
		}
	}
}
