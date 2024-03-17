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
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using ArchiSteamFarm.Core;
using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace ArchiSteamFarm.Web.GitHub.Data;

[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
public sealed class ReleaseResponse {
	internal string? ChangelogHTML {
		get {
			if (BackingChangelogHTML != null) {
				return BackingChangelogHTML;
			}

			if (Changelog == null) {
				ASF.ArchiLogger.LogNullError(Changelog);

				return null;
			}

			using StringWriter writer = new();

			HtmlRenderer renderer = new(writer);

			renderer.Render(Changelog);
			writer.Flush();

			return BackingChangelogHTML = writer.ToString();
		}
	}

	internal string? ChangelogPlainText {
		get {
			if (BackingChangelogPlainText != null) {
				return BackingChangelogPlainText;
			}

			if (Changelog == null) {
				ASF.ArchiLogger.LogNullError(Changelog);

				return null;
			}

			using StringWriter writer = new();

			HtmlRenderer renderer = new(writer) {
				EnableHtmlForBlock = false,
				EnableHtmlForInline = false,
				EnableHtmlEscape = false
			};

			renderer.Render(Changelog);
			writer.Flush();

			return BackingChangelogPlainText = writer.ToString();
		}
	}

	private MarkdownDocument? Changelog {
		get {
			if (BackingChangelog != null) {
				return BackingChangelog;
			}

			if (string.IsNullOrEmpty(MarkdownBody)) {
				ASF.ArchiLogger.LogNullError(MarkdownBody);

				return null;
			}

			return BackingChangelog = ExtractChangelogFromBody(MarkdownBody);
		}
	}

	[JsonInclude]
	[JsonPropertyName("assets")]
	[JsonRequired]
	public ImmutableHashSet<ReleaseAsset> Assets { get; private init; } = [];

	[JsonInclude]
	[JsonPropertyName("prerelease")]
	[JsonRequired]
	public bool IsPreRelease { get; private init; }

	[JsonInclude]
	[JsonPropertyName("body")]
	[JsonRequired]
	public string MarkdownBody { get; private init; } = "";

	[JsonInclude]
	[JsonPropertyName("published_at")]
	[JsonRequired]
	public DateTime PublishedAt { get; private init; }

	[JsonInclude]
	[JsonPropertyName("tag_name")]
	[JsonRequired]
	public string Tag { get; private init; } = "";

	private MarkdownDocument? BackingChangelog;
	private string? BackingChangelogHTML;
	private string? BackingChangelogPlainText;

	[JsonConstructor]
	private ReleaseResponse() { }

	private static MarkdownDocument ExtractChangelogFromBody(string markdownText) {
		ArgumentException.ThrowIfNullOrEmpty(markdownText);

		MarkdownDocument markdownDocument = Markdown.Parse(markdownText);
		MarkdownDocument result = [];

		foreach (Block block in markdownDocument.SkipWhile(static block => block is not HeadingBlock { Inline.FirstChild: LiteralInline literalInline } || (literalInline.Content.ToString()?.Equals("Changelog", StringComparison.OrdinalIgnoreCase) != true)).Skip(1).TakeWhile(static block => block is not ThematicBreakBlock).ToList()) {
			// All blocks that we're interested in must be removed from original markdownDocument firstly
			markdownDocument.Remove(block);
			result.Add(block);
		}

		return result;
	}
}
