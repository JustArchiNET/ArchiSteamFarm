//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
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
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Dom;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Web.Responses;
using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace ArchiSteamFarm.Web;

internal static class GitHub {
	internal static async Task<ReleaseResponse?> GetLatestRelease(bool stable = true, CancellationToken cancellationToken = default) {
		Uri request = new($"{SharedInfo.GithubReleaseURL}{(stable ? "/latest" : "?per_page=1")}");

		if (stable) {
			return await GetReleaseFromURL(request, cancellationToken).ConfigureAwait(false);
		}

		ImmutableList<ReleaseResponse>? response = await GetReleasesFromURL(request, cancellationToken).ConfigureAwait(false);

		return response?.FirstOrDefault();
	}

	internal static async Task<ReleaseResponse?> GetRelease(string version, CancellationToken cancellationToken = default) {
		ArgumentException.ThrowIfNullOrEmpty(version);

		Uri request = new($"{SharedInfo.GithubReleaseURL}/tags/{version}");

		return await GetReleaseFromURL(request, cancellationToken).ConfigureAwait(false);
	}

	internal static async Task<Dictionary<string, DateTime>?> GetWikiHistory(string page, CancellationToken cancellationToken = default) {
		ArgumentException.ThrowIfNullOrEmpty(page);

		if (ASF.WebBrowser == null) {
			throw new InvalidOperationException(nameof(ASF.WebBrowser));
		}

		Uri request = new($"{SharedInfo.ProjectURL}/wiki/{page}/_history");

		using HtmlDocumentResponse? response = await ASF.WebBrowser.UrlGetToHtmlDocument(request, requestOptions: WebBrowser.ERequestOptions.ReturnClientErrors | WebBrowser.ERequestOptions.AllowInvalidBodyOnErrors, cancellationToken: cancellationToken).ConfigureAwait(false);

		if (response?.StatusCode.IsClientErrorCode() == true) {
			return response.StatusCode switch {
				HttpStatusCode.NotFound => new Dictionary<string, DateTime>(0),
				_ => null
			};
		}

		if (response?.Content == null) {
			return null;
		}

		IEnumerable<IElement> revisionNodes = response.Content.SelectNodes<IElement>("//li[contains(@class, 'wiki-history-revision')]");

		Dictionary<string, DateTime> result = new();

		foreach (IElement revisionNode in revisionNodes) {
			IAttr? versionNode = revisionNode.SelectSingleNode<IAttr>(".//input/@value");

			if (versionNode == null) {
				ASF.ArchiLogger.LogNullError(versionNode);

				return null;
			}

			string versionText = versionNode.Value;

			if (string.IsNullOrEmpty(versionText)) {
				ASF.ArchiLogger.LogNullError(versionText);

				return null;
			}

			IAttr? dateTimeNode = revisionNode.SelectSingleNode<IAttr>(".//relative-time/@datetime");

			if (dateTimeNode == null) {
				ASF.ArchiLogger.LogNullError(dateTimeNode);

				return null;
			}

			string dateTimeText = dateTimeNode.Value;

			if (string.IsNullOrEmpty(dateTimeText)) {
				ASF.ArchiLogger.LogNullError(dateTimeText);

				return null;
			}

			if (!DateTime.TryParse(dateTimeText, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime dateTime)) {
				ASF.ArchiLogger.LogNullError(dateTime);

				return null;
			}

			result[versionText] = dateTime.ToUniversalTime();
		}

		return result;
	}

	internal static async Task<string?> GetWikiPage(string page, string? revision = null, CancellationToken cancellationToken = default) {
		ArgumentException.ThrowIfNullOrEmpty(page);

		if (ASF.WebBrowser == null) {
			throw new InvalidOperationException(nameof(ASF.WebBrowser));
		}

		Uri request = new($"{SharedInfo.ProjectURL}/wiki/{page}{(!string.IsNullOrEmpty(revision) ? $"/{revision}" : "")}");

		using HtmlDocumentResponse? response = await ASF.WebBrowser.UrlGetToHtmlDocument(request, cancellationToken: cancellationToken).ConfigureAwait(false);

		if (response?.Content == null) {
			return null;
		}

		IElement? markdownBodyNode = response.Content.SelectSingleNode<IElement>("//div[@class='markdown-body']");

		return markdownBodyNode?.InnerHtml.Trim() ?? "";
	}

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

	private static async Task<ReleaseResponse?> GetReleaseFromURL(Uri request, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(request);

		if (ASF.WebBrowser == null) {
			throw new InvalidOperationException(nameof(ASF.WebBrowser));
		}

		ObjectResponse<ReleaseResponse>? response = await ASF.WebBrowser.UrlGetToJsonObject<ReleaseResponse>(request, cancellationToken: cancellationToken).ConfigureAwait(false);

		return response?.Content;
	}

	private static async Task<ImmutableList<ReleaseResponse>?> GetReleasesFromURL(Uri request, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(request);

		if (ASF.WebBrowser == null) {
			throw new InvalidOperationException(nameof(ASF.WebBrowser));
		}

		ObjectResponse<ImmutableList<ReleaseResponse>>? response = await ASF.WebBrowser.UrlGetToJsonObject<ImmutableList<ReleaseResponse>>(request, cancellationToken: cancellationToken).ConfigureAwait(false);

		return response?.Content;
	}

	[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
	internal sealed class ReleaseResponse {
		[JsonInclude]
		[JsonPropertyName("assets")]
		[JsonRequired]
		internal readonly ImmutableHashSet<Asset> Assets = ImmutableHashSet<Asset>.Empty;

		[JsonInclude]
		[JsonPropertyName("prerelease")]
		[JsonRequired]
		internal readonly bool IsPreRelease;

		[JsonInclude]
		[JsonPropertyName("published_at")]
		[JsonRequired]
		internal readonly DateTime PublishedAt;

		[JsonInclude]
		[JsonPropertyName("tag_name")]
		[JsonRequired]
		internal readonly string Tag = "";

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

		[JsonInclude]
		[JsonPropertyName("body")]
		[JsonRequired]
		private readonly string? MarkdownBody = "";

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

		private MarkdownDocument? BackingChangelog;
		private string? BackingChangelogHTML;
		private string? BackingChangelogPlainText;

		[JsonConstructor]
		private ReleaseResponse() { }

		internal sealed class Asset {
			[JsonInclude]
			[JsonPropertyName("browser_download_url")]
			[JsonRequired]
			internal readonly Uri? DownloadURL;

			[JsonInclude]
			[JsonPropertyName("name")]
			[JsonRequired]
			internal readonly string? Name;

			[JsonInclude]
			[JsonPropertyName("size")]
			[JsonRequired]
			internal readonly uint Size;

			[JsonConstructor]
			private Asset() { }
		}
	}
}
