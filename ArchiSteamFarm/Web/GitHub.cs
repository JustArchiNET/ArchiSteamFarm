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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using AngleSharp.Dom;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Web.Responses;
using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Newtonsoft.Json;

namespace ArchiSteamFarm.Web {
	internal static class GitHub {
		internal static async Task<ReleaseResponse?> GetLatestRelease(bool stable = true) {
			Uri request = new(SharedInfo.GithubReleaseURL + (stable ? "/latest" : "?per_page=1"));

			if (stable) {
				return await GetReleaseFromURL(request).ConfigureAwait(false);
			}

			ImmutableList<ReleaseResponse>? response = await GetReleasesFromURL(request).ConfigureAwait(false);

			return response?.FirstOrDefault();
		}

		internal static async Task<ReleaseResponse?> GetRelease(string version) {
			if (string.IsNullOrEmpty(version)) {
				throw new ArgumentNullException(nameof(version));
			}

			Uri request = new(SharedInfo.GithubReleaseURL + "/tags/" + version);

			return await GetReleaseFromURL(request).ConfigureAwait(false);
		}

		internal static async Task<Dictionary<string, DateTime>?> GetWikiHistory(string page) {
			if (string.IsNullOrEmpty(page)) {
				throw new ArgumentNullException(nameof(page));
			}

			if (ASF.WebBrowser == null) {
				throw new InvalidOperationException(nameof(ASF.WebBrowser));
			}

			Uri request = new(SharedInfo.ProjectURL + "/wiki/" + page + "/_history");

			using HtmlDocumentResponse? response = await ASF.WebBrowser.UrlGetToHtmlDocument(request, requestOptions: WebBrowser.ERequestOptions.ReturnClientErrors).ConfigureAwait(false);

			if (response == null) {
				return null;
			}

			if (response.StatusCode.IsClientErrorCode()) {
				return response.StatusCode switch {
					HttpStatusCode.NotFound => new Dictionary<string, DateTime>(0),
					_ => null
				};
			}

			IEnumerable<IElement> revisionNodes = response.Content.SelectNodes("//li[contains(@class, 'wiki-history-revision')]");

			Dictionary<string, DateTime> result = new();

			foreach (IElement revisionNode in revisionNodes) {
				IElement? versionNode = revisionNode.SelectSingleElementNode(".//input/@value");

				if (versionNode == null) {
					ASF.ArchiLogger.LogNullError(nameof(versionNode));

					return null;
				}

				string versionText = versionNode.GetAttribute("value");

				if (string.IsNullOrEmpty(versionText)) {
					ASF.ArchiLogger.LogNullError(nameof(versionText));

					return null;
				}

				IElement? dateTimeNode = revisionNode.SelectSingleElementNode(".//relative-time/@datetime");

				if (dateTimeNode == null) {
					ASF.ArchiLogger.LogNullError(nameof(dateTimeNode));

					return null;
				}

				string dateTimeText = dateTimeNode.GetAttribute("datetime");

				if (string.IsNullOrEmpty(dateTimeText)) {
					ASF.ArchiLogger.LogNullError(nameof(dateTimeText));

					return null;
				}

				if (!DateTime.TryParse(dateTimeText, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime dateTime)) {
					ASF.ArchiLogger.LogNullError(nameof(dateTime));

					return null;
				}

				result[versionText] = dateTime.ToUniversalTime();
			}

			return result;
		}

		internal static async Task<string?> GetWikiPage(string page, string? revision = null) {
			if (string.IsNullOrEmpty(page)) {
				throw new ArgumentNullException(nameof(page));
			}

			if (ASF.WebBrowser == null) {
				throw new InvalidOperationException(nameof(ASF.WebBrowser));
			}

			Uri request = new(SharedInfo.ProjectURL + "/wiki/" + page + (!string.IsNullOrEmpty(revision) ? "/" + revision : ""));

			using HtmlDocumentResponse? response = await ASF.WebBrowser.UrlGetToHtmlDocument(request).ConfigureAwait(false);

			if (response == null) {
				return null;
			}

			IElement? markdownBodyNode = response.Content.SelectSingleNode("//div[@class='markdown-body']");

			return markdownBodyNode?.InnerHtml.Trim() ?? "";
		}

		private static MarkdownDocument ExtractChangelogFromBody(string markdownText) {
			if (string.IsNullOrEmpty(markdownText)) {
				throw new ArgumentNullException(nameof(markdownText));
			}

			MarkdownDocument markdownDocument = Markdown.Parse(markdownText);
			MarkdownDocument result = new();

			foreach (Block block in markdownDocument.SkipWhile(block => block is not HeadingBlock { Inline: { FirstChild: LiteralInline literalInline } } || !literalInline.Content.ToString().Equals("Changelog", StringComparison.OrdinalIgnoreCase)).Skip(1).TakeWhile(block => block is not ThematicBreakBlock).ToList()) {
				// All blocks that we're interested in must be removed from original markdownDocument firstly
				markdownDocument.Remove(block);
				result.Add(block);
			}

			return result;
		}

		private static async Task<ReleaseResponse?> GetReleaseFromURL(Uri request) {
			if (request == null) {
				throw new ArgumentNullException(nameof(request));
			}

			if (ASF.WebBrowser == null) {
				throw new InvalidOperationException(nameof(ASF.WebBrowser));
			}

			ObjectResponse<ReleaseResponse>? response = await ASF.WebBrowser.UrlGetToJsonObject<ReleaseResponse>(request).ConfigureAwait(false);

			return response?.Content;
		}

		private static async Task<ImmutableList<ReleaseResponse>?> GetReleasesFromURL(Uri request) {
			if (request == null) {
				throw new ArgumentNullException(nameof(request));
			}

			if (ASF.WebBrowser == null) {
				throw new InvalidOperationException(nameof(ASF.WebBrowser));
			}

			ObjectResponse<ImmutableList<ReleaseResponse>>? response = await ASF.WebBrowser.UrlGetToJsonObject<ImmutableList<ReleaseResponse>>(request).ConfigureAwait(false);

			return response?.Content;
		}

		[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
		internal sealed class ReleaseResponse {
			[JsonProperty(PropertyName = "assets", Required = Required.Always)]
			internal readonly ImmutableHashSet<Asset> Assets = ImmutableHashSet<Asset>.Empty;

			[JsonProperty(PropertyName = "prerelease", Required = Required.Always)]
			internal readonly bool IsPreRelease;

			[JsonProperty(PropertyName = "published_at", Required = Required.Always)]
			internal readonly DateTime PublishedAt;

			[JsonProperty(PropertyName = "tag_name", Required = Required.Always)]
			internal readonly string Tag = "";

			internal string? ChangelogHTML {
				get {
					if (BackingChangelogHTML != null) {
						return BackingChangelogHTML;
					}

					if (Changelog == null) {
						ASF.ArchiLogger.LogNullError(nameof(Changelog));

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
						ASF.ArchiLogger.LogNullError(nameof(Changelog));

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

			[JsonProperty(PropertyName = "body", Required = Required.Always)]
			private readonly string? MarkdownBody = "";

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
				internal readonly Uri? DownloadURL;

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
