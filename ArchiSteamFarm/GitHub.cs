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
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Newtonsoft.Json;

namespace ArchiSteamFarm {
	internal static class GitHub {
		internal static async Task<ReleaseResponse?> GetLatestRelease(bool stable = true) {
			string releaseURL = SharedInfo.GithubReleaseURL + (stable ? "/latest" : "?per_page=1");

			if (stable) {
				return await GetReleaseFromURL(releaseURL).ConfigureAwait(false);
			}

			ImmutableList<ReleaseResponse>? response = await GetReleasesFromURL(releaseURL).ConfigureAwait(false);

			return response?.FirstOrDefault();
		}

		internal static async Task<ReleaseResponse?> GetRelease(string version) {
			if (string.IsNullOrEmpty(version)) {
				throw new ArgumentNullException(nameof(version));
			}

			return await GetReleaseFromURL(SharedInfo.GithubReleaseURL + "/tags/" + version).ConfigureAwait(false);
		}

		private static MarkdownDocument ExtractChangelogFromBody(string markdownText) {
			if (string.IsNullOrEmpty(markdownText)) {
				throw new ArgumentNullException(nameof(markdownText));
			}

			MarkdownDocument markdownDocument = Markdown.Parse(markdownText);
			MarkdownDocument result = new MarkdownDocument();

			foreach (Block block in markdownDocument.SkipWhile(block => !(block is HeadingBlock headingBlock) || (headingBlock.Inline.FirstChild == null) || !(headingBlock.Inline.FirstChild is LiteralInline literalInline) || (literalInline.Content.ToString() != "Changelog")).Skip(1).TakeWhile(block => !(block is ThematicBreakBlock)).ToList()) {
				// All blocks that we're interested in must be removed from original markdownDocument firstly
				markdownDocument.Remove(block);
				result.Add(block);
			}

			return result;
		}

		private static async Task<ReleaseResponse?> GetReleaseFromURL(string releaseURL) {
			if ((ASF.WebBrowser == null) || string.IsNullOrEmpty(releaseURL)) {
				throw new ArgumentNullException(nameof(ASF.WebBrowser) + " || " + nameof(releaseURL));
			}

			WebBrowser.ObjectResponse<ReleaseResponse>? objectResponse = await ASF.WebBrowser.UrlGetToJsonObject<ReleaseResponse>(releaseURL).ConfigureAwait(false);

			return objectResponse?.Content;
		}

		private static async Task<ImmutableList<ReleaseResponse>?> GetReleasesFromURL(string releaseURL) {
			if ((ASF.WebBrowser == null) || string.IsNullOrEmpty(releaseURL)) {
				throw new ArgumentNullException(nameof(ASF.WebBrowser) + " || " + nameof(releaseURL));
			}

			WebBrowser.ObjectResponse<ImmutableList<ReleaseResponse>>? objectResponse = await ASF.WebBrowser.UrlGetToJsonObject<ImmutableList<ReleaseResponse>>(releaseURL).ConfigureAwait(false);

			return objectResponse?.Content;
		}

		[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
		internal sealed class ReleaseResponse {
			[JsonProperty(PropertyName = "assets", Required = Required.Always)]
			internal readonly ImmutableHashSet<Asset>? Assets;

			[JsonProperty(PropertyName = "prerelease", Required = Required.Always)]
			internal readonly bool IsPreRelease;

			[JsonProperty(PropertyName = "published_at", Required = Required.Always)]
			internal readonly DateTime PublishedAt;

			[JsonProperty(PropertyName = "tag_name", Required = Required.Always)]
			internal readonly string? Tag;

			internal string? ChangelogHTML {
				get {
					if (BackingChangelogHTML != null) {
						return BackingChangelogHTML;
					}

					if (Changelog == null) {
						ASF.ArchiLogger.LogNullError(nameof(Changelog));

						return null;
					}

					using StringWriter writer = new StringWriter();

					HtmlRenderer renderer = new HtmlRenderer(writer);
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
						ASF.ArchiLogger.LogNullError(nameof(Changelog));

						return null;
					}

					using StringWriter writer = new StringWriter();

					HtmlRenderer renderer = new HtmlRenderer(writer) {
						EnableHtmlForBlock = false,
						EnableHtmlForInline = false
					};

					renderer.Render(Changelog);
					writer.Flush();

					return BackingChangelogPlainText = writer.ToString();
				}
			}

#pragma warning disable 649
			[JsonProperty(PropertyName = "body", Required = Required.Always)]
			private readonly string? MarkdownBody;
#pragma warning restore 649

			private MarkdownDocument? Changelog {
				get {
					if (BackingChangelog != null) {
						return BackingChangelog;
					}

					if (string.IsNullOrEmpty(MarkdownBody)) {
						ASF.ArchiLogger.LogNullError(nameof(MarkdownBody));

						return null;
					}

					return BackingChangelog = ExtractChangelogFromBody(MarkdownBody!);
				}
			}

			private MarkdownDocument? BackingChangelog;
			private string? BackingChangelogHTML;
			private string? BackingChangelogPlainText;

			[JsonConstructor]
			private ReleaseResponse() { }

			internal sealed class Asset {
				[JsonProperty(PropertyName = "browser_download_url", Required = Required.Always)]
				internal readonly string? DownloadURL;

				[JsonProperty(PropertyName = "name", Required = Required.Always)]
				internal readonly string? Name;

				[JsonProperty(PropertyName = "size", Required = Required.Always)]
				internal readonly uint Size;

				[JsonConstructor]
				private Asset() { }
			}
		}
	}
}
