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
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using JetBrains.Annotations;

namespace ArchiSteamFarm.Web.Responses;

public sealed class HtmlDocumentResponse : BasicResponse, IDisposable {
	[PublicAPI]
	public IDocument? Content { get; }

	public HtmlDocumentResponse(BasicResponse basicResponse) : base(basicResponse) => ArgumentNullException.ThrowIfNull(basicResponse);

	private HtmlDocumentResponse(BasicResponse basicResponse, IDocument content) : this(basicResponse) {
		ArgumentNullException.ThrowIfNull(basicResponse);
		ArgumentNullException.ThrowIfNull(content);

		Content = content;
	}

	public void Dispose() => Content?.Dispose();

	[PublicAPI]
	public static async Task<HtmlDocumentResponse> Create(StreamResponse streamResponse, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(streamResponse);

		if (streamResponse.Content == null) {
			throw new InvalidOperationException(nameof(streamResponse.Content));
		}

		HtmlParser htmlParser = new();

		IHtmlDocument document = await htmlParser.ParseDocumentAsync(streamResponse.Content, cancellationToken).ConfigureAwait(false);

		return new HtmlDocumentResponse(streamResponse, document);
	}
}
