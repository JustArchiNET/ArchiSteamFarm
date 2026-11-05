// ----------------------------------------------------------------------------------------------
//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// ----------------------------------------------------------------------------------------------
// |
// Copyright 2015-2026 Łukasz "JustArchi" Domeradzki
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
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Dom;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Web.GitHub.Data;
using ArchiSteamFarm.Web.Responses;
using JetBrains.Annotations;

namespace ArchiSteamFarm.Web.GitHub;

public static class GitHubService {
	private static Uri ApiURL => new("https://api.github.com");
	private static Uri WebsiteURL => new("https://github.com");

	[PublicAPI]
	public static async Task<ReleaseResponse?> GetLatestRelease(string repoName, bool stable = true, CancellationToken cancellationToken = default) {
		ArgumentException.ThrowIfNullOrEmpty(repoName);

		return await GetLatestReleaseFromAPI(repoName, stable, cancellationToken).ConfigureAwait(false) ?? await GetLatestReleaseFromWebsite(repoName, stable, cancellationToken).ConfigureAwait(false);
	}

	[PublicAPI]
	public static async Task<ReleaseResponse?> GetRelease(string repoName, string tag, CancellationToken cancellationToken = default) {
		ArgumentException.ThrowIfNullOrEmpty(repoName);
		ArgumentException.ThrowIfNullOrEmpty(tag);

		return await GetReleaseFromAPI(repoName, tag, cancellationToken).ConfigureAwait(false) ?? await GetReleaseFromWebsite(repoName, tag, cancellationToken).ConfigureAwait(false);
	}

	[PublicAPI]
	public static async Task<ImmutableList<ReleaseResponse>?> GetReleases(string repoName, byte count = 10, CancellationToken cancellationToken = default) {
		ArgumentException.ThrowIfNullOrEmpty(repoName);
		ArgumentOutOfRangeException.ThrowIfZero(count);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 100);

		return await GetReleasesFromAPI(repoName, count, cancellationToken).ConfigureAwait(false) ?? await GetReleasesFromWebsite(repoName, count, cancellationToken).ConfigureAwait(false);
	}

	internal static async Task<Dictionary<string, DateTime>?> GetWikiHistory(string page, CancellationToken cancellationToken = default) {
		ArgumentException.ThrowIfNullOrEmpty(page);

		if (ASF.WebBrowser == null) {
			throw new InvalidOperationException(nameof(ASF.WebBrowser));
		}

		Uri request = new($"{SharedInfo.ProjectURL}/wiki/{page}/_history");

		using HtmlDocumentResponse? response = await ASF.WebBrowser.UrlGetToHtmlDocument(request, requestOptions: EWebRequestOptions.ReturnClientErrors | EWebRequestOptions.AllowInvalidBodyOnErrors, cancellationToken: cancellationToken).ConfigureAwait(false);

		if (response?.StatusCode.IsClientErrorCode() == true) {
			return response.StatusCode switch {
				HttpStatusCode.NotFound => new Dictionary<string, DateTime>(0, StringComparer.Ordinal),
				_ => null
			};
		}

		if (response?.Content == null) {
			return null;
		}

		IHtmlCollection<IElement> revisionNodes = response.Content.QuerySelectorAll("li[class*='wiki-history-revision']");

		Dictionary<string, DateTime> result = new(StringComparer.Ordinal);

		foreach (IElement revisionNode in revisionNodes) {
			IElement? versionNode = revisionNode.QuerySelector("input[value]");

			if (versionNode == null) {
				ASF.ArchiLogger.LogNullError(versionNode);

				return null;
			}

			string? versionText = versionNode.GetAttribute("value");

			if (string.IsNullOrEmpty(versionText)) {
				ASF.ArchiLogger.LogNullError(versionText);

				return null;
			}

			IElement? dateTimeNode = revisionNode.QuerySelector("relative-time[datetime]");

			if (dateTimeNode == null) {
				ASF.ArchiLogger.LogNullError(dateTimeNode);

				return null;
			}

			string? dateTimeText = dateTimeNode.GetAttribute("datetime");

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

		IElement? markdownBodyNode = response.Content.QuerySelector("div[class='markdown-body']");

		return markdownBodyNode?.InnerHtml.Trim() ?? "";
	}

	private static (string HTML, string PlainText) GetChangelogFromHTML(IParentNode changelogNode) {
		ArgumentNullException.ThrowIfNull(changelogNode);

		IElement? changelogHeading = changelogNode.QuerySelectorAll("h1, h2, h3, h4, h5, h6").FirstOrDefault(static heading => heading.TextContent.Trim().Equals("Changelog", StringComparison.OrdinalIgnoreCase));

		if (changelogHeading == null) {
			return ("", "");
		}

		List<IElement> changelogBlocks = [];

		for (IElement? element = changelogHeading.NextElementSibling; (element != null) && !element.LocalName.Equals("hr", StringComparison.OrdinalIgnoreCase); element = element.NextElementSibling) {
			changelogBlocks.Add(element);
		}

		return (
			string.Concat(changelogBlocks.Select(static block => block.OuterHtml)),
			string.Join(Environment.NewLine, changelogBlocks.Select(static block => block.TextContent.Trim()).Where(static text => !string.IsNullOrEmpty(text)))
		);
	}

	private static async Task<ReleaseResponse?> GetLatestReleaseFromAPI(string repoName, bool stable, CancellationToken cancellationToken = default) {
		ArgumentException.ThrowIfNullOrEmpty(repoName);

		if (stable) {
			Uri request = new(ApiURL, $"/repos/{repoName}/releases/latest");

			return await GetReleaseFromURL(request, cancellationToken).ConfigureAwait(false);
		}

		Uri releasesRequest = new(ApiURL, $"/repos/{repoName}/releases?per_page=1");

		ImmutableList<ReleaseResponse>? releases = await GetReleasesFromURL(releasesRequest, cancellationToken).ConfigureAwait(false);

		return releases?.FirstOrDefault();
	}

	private static async Task<ReleaseResponse?> GetLatestReleaseFromWebsite(string repoName, bool stable, CancellationToken cancellationToken = default) {
		ArgumentException.ThrowIfNullOrEmpty(repoName);

		if (!stable) {
			ImmutableList<ReleaseResponse>? releases = await GetReleasesFromHTML(repoName, 1, cancellationToken).ConfigureAwait(false);

			return releases?.FirstOrDefault();
		}

		Uri request = new(WebsiteURL, $"/{repoName}/releases/latest");

		return await GetReleaseFromHTML(request, repoName, null, cancellationToken).ConfigureAwait(false);
	}

	private static async Task<ImmutableHashSet<ReleaseAsset>?> GetReleaseAssetsFromHTML(Uri request, string repoName, string tag, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(request);

		if (ASF.WebBrowser == null) {
			throw new InvalidOperationException(nameof(ASF.WebBrowser));
		}

		using HtmlDocumentResponse? response = await ASF.WebBrowser.UrlGetToHtmlDocument(request, cancellationToken: cancellationToken).ConfigureAwait(false);

		if (response?.Content == null) {
			return null;
		}

		ImmutableHashSet<ReleaseAsset>.Builder result = ImmutableHashSet.CreateBuilder<ReleaseAsset>();

		foreach (IElement assetNode in response.Content.QuerySelectorAll("a[href]")) {
			if (!TryGetReleaseAssetURL(response.FinalUri, assetNode.GetAttribute("href"), repoName, tag, out Uri? downloadURL, out string? name)) {
				continue;
			}

			IElement? listItem = assetNode;

			while ((listItem != null) && !listItem.LocalName.Equals("li", StringComparison.OrdinalIgnoreCase)) {
				listItem = listItem.ParentElement;
			}

			string? sizeText = listItem?.QuerySelector("span[class~='text-right']")?.TextContent;
			uint size = TryParseAssetSize(sizeText, out uint parsedSize) ? parsedSize : 0;

			result.Add(new ReleaseAsset(downloadURL, name, size));
		}

		return result.ToImmutable();
	}

	private static async Task<ReleaseResponse?> GetReleaseFromAPI(string repoName, string tag, CancellationToken cancellationToken = default) {
		ArgumentException.ThrowIfNullOrEmpty(repoName);
		ArgumentException.ThrowIfNullOrEmpty(tag);

		Uri request = new(ApiURL, $"/repos/{repoName}/releases/tags/{tag}");

		return await GetReleaseFromURL(request, cancellationToken).ConfigureAwait(false);
	}

	private static async Task<ReleaseResponse?> GetReleaseFromHTML(Uri request, string repoName, string? tag, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(request);

		if (ASF.WebBrowser == null) {
			throw new InvalidOperationException(nameof(ASF.WebBrowser));
		}

		using HtmlDocumentResponse? response = await ASF.WebBrowser.UrlGetToHtmlDocument(request, cancellationToken: cancellationToken).ConfigureAwait(false);

		return response?.Content != null ? await GetReleaseFromHTML(response.Content, response.FinalUri, repoName, tag, cancellationToken).ConfigureAwait(false) : null;
	}

	private static async Task<ReleaseResponse?> GetReleaseFromHTML(IParentNode releaseNode, Uri request, string repoName, string? tag, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(releaseNode);
		ArgumentNullException.ThrowIfNull(request);
		ArgumentException.ThrowIfNullOrEmpty(repoName);

		tag ??= GetReleaseTag(releaseNode, request, repoName);

		if (string.IsNullOrEmpty(tag)) {
			ASF.ArchiLogger.LogNullError(tag);

			return null;
		}

		IElement? releaseTimeNode = releaseNode.QuerySelector("relative-time[datetime]");

		if (releaseTimeNode == null) {
			ASF.ArchiLogger.LogNullError(releaseTimeNode);

			return null;
		}

		string? publishedAtText = releaseTimeNode.GetAttribute("datetime");

		if (string.IsNullOrEmpty(publishedAtText) || !DateTime.TryParse(publishedAtText, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime publishedAt)) {
			ASF.ArchiLogger.LogNullError(publishedAtText);

			return null;
		}

		bool isPreRelease = releaseNode.QuerySelectorAll("span[class*='Label']").Any(static label => label.TextContent.Trim().Equals("Pre-release", StringComparison.OrdinalIgnoreCase));

		IElement? changelogNode = releaseNode.QuerySelector("div[data-test-selector='body-content']");

		if (changelogNode == null) {
			ASF.ArchiLogger.LogNullError(changelogNode);

			return null;
		}

		(string changelogHTML, string changelogPlainText) = GetChangelogFromHTML(changelogNode);

		IElement? assetsFragmentNode = releaseNode.QuerySelectorAll("include-fragment[src]").FirstOrDefault(node => TryGetReleaseAssetsURL(request, node.GetAttribute("src"), repoName, tag, out _));

		if ((assetsFragmentNode == null) || !TryGetReleaseAssetsURL(request, assetsFragmentNode.GetAttribute("src"), repoName, tag, out Uri? assetsURL)) {
			ASF.ArchiLogger.LogNullError(assetsFragmentNode);

			return null;
		}

		ImmutableHashSet<ReleaseAsset>? assets = await GetReleaseAssetsFromHTML(assetsURL, repoName, tag, cancellationToken).ConfigureAwait(false);

		return assets != null ? new ReleaseResponse(tag, isPreRelease, publishedAt.ToUniversalTime(), assets, changelogHTML, changelogPlainText) : null;
	}

	private static async Task<ReleaseResponse?> GetReleaseFromURL(Uri request, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(request);

		if (ASF.WebBrowser == null) {
			throw new InvalidOperationException(nameof(ASF.WebBrowser));
		}

		ObjectResponse<ReleaseResponse>? response = await ASF.WebBrowser.UrlGetToJsonObject<ReleaseResponse>(request, cancellationToken: cancellationToken).ConfigureAwait(false);

		return response?.Content;
	}

	private static async Task<ReleaseResponse?> GetReleaseFromWebsite(string repoName, string tag, CancellationToken cancellationToken = default) {
		ArgumentException.ThrowIfNullOrEmpty(repoName);
		ArgumentException.ThrowIfNullOrEmpty(tag);

		Uri request = new(WebsiteURL, $"/{repoName}/releases/tag/{Uri.EscapeDataString(tag)}");

		return await GetReleaseFromHTML(request, repoName, tag, cancellationToken).ConfigureAwait(false);
	}

	private static string? GetReleaseTag(IParentNode releaseNode, Uri request, string repoName) {
		ArgumentNullException.ThrowIfNull(releaseNode);
		ArgumentNullException.ThrowIfNull(request);
		ArgumentException.ThrowIfNullOrEmpty(repoName);

		if (TryGetReleaseTag(request, repoName, out string? tag) || releaseNode.QuerySelectorAll("a[href]").Any(link => TryGetReleaseTag(request, link.GetAttribute("href"), repoName, out tag))) {
			return tag;
		}

		return null;
	}

	private static async Task<ImmutableList<ReleaseResponse>?> GetReleasesFromAPI(string repoName, byte count, CancellationToken cancellationToken = default) {
		ArgumentException.ThrowIfNullOrEmpty(repoName);
		ArgumentOutOfRangeException.ThrowIfZero(count);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 100);

		Uri request = new(ApiURL, $"/repos/{repoName}/releases?per_page={count}");

		return await GetReleasesFromURL(request, cancellationToken).ConfigureAwait(false);
	}

	private static async Task<ImmutableList<ReleaseResponse>?> GetReleasesFromHTML(string repoName, byte count, CancellationToken cancellationToken = default) {
		ArgumentException.ThrowIfNullOrEmpty(repoName);
		ArgumentOutOfRangeException.ThrowIfZero(count);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 100);

		if (ASF.WebBrowser == null) {
			throw new InvalidOperationException(nameof(ASF.WebBrowser));
		}

		ImmutableList<ReleaseResponse>.Builder result = ImmutableList.CreateBuilder<ReleaseResponse>();

		for (int page = 1; result.Count < count; page++) {
			Uri request = new(WebsiteURL, $"/{repoName}/releases?page={page}");

			using HtmlDocumentResponse? response = await ASF.WebBrowser.UrlGetToHtmlDocument(request, cancellationToken: cancellationToken).ConfigureAwait(false);

			if (response?.Content == null) {
				return null;
			}

			IHtmlCollection<IElement> releaseNodes = response.Content.QuerySelectorAll("section[id^='release-']");

			if (releaseNodes.Length == 0) {
				return result.ToImmutable();
			}

			foreach (IElement releaseNode in releaseNodes) {
				ReleaseResponse? release = await GetReleaseFromHTML(releaseNode, response.FinalUri, repoName, null, cancellationToken).ConfigureAwait(false);

				if (release == null) {
					return null;
				}

				result.Add(release);

				if (result.Count >= count) {
					return result.ToImmutable();
				}
			}

			if (response.Content.QuerySelector("nav[aria-label='Release list pagination'] a[rel='next'][href]") == null) {
				return result.ToImmutable();
			}
		}

		return result.ToImmutable();
	}

	private static async Task<ImmutableList<ReleaseResponse>?> GetReleasesFromURL(Uri request, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(request);

		if (ASF.WebBrowser == null) {
			throw new InvalidOperationException(nameof(ASF.WebBrowser));
		}

		ObjectResponse<ImmutableList<ReleaseResponse>>? response = await ASF.WebBrowser.UrlGetToJsonObject<ImmutableList<ReleaseResponse>>(request, cancellationToken: cancellationToken).ConfigureAwait(false);

		return response?.Content;
	}

	private static async Task<ImmutableList<ReleaseResponse>?> GetReleasesFromWebsite(string repoName, byte count, CancellationToken cancellationToken = default) {
		ArgumentException.ThrowIfNullOrEmpty(repoName);
		ArgumentOutOfRangeException.ThrowIfZero(count);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 100);

		return await GetReleasesFromHTML(repoName, count, cancellationToken).ConfigureAwait(false);
	}

	private static bool TryGetReleaseAssetURL(Uri request, string? href, string repoName, string tag, [NotNullWhen(true)] out Uri? downloadURL, [NotNullWhen(true)] out string? name) {
		ArgumentNullException.ThrowIfNull(request);
		ArgumentException.ThrowIfNullOrEmpty(repoName);
		ArgumentException.ThrowIfNullOrEmpty(tag);

		downloadURL = null;
		name = null;

		if (string.IsNullOrEmpty(href) || !Uri.TryCreate(request, href, out Uri? uri) || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) || !uri.Host.Equals(WebsiteURL.Host, StringComparison.OrdinalIgnoreCase)) {
			return false;
		}

		string assetPrefix = $"/{repoName}/releases/download/{Uri.EscapeDataString(tag)}/";

		if (!uri.AbsolutePath.StartsWith(assetPrefix, StringComparison.OrdinalIgnoreCase)) {
			return false;
		}

		string assetName = Uri.UnescapeDataString(uri.AbsolutePath[assetPrefix.Length..]);

		if (string.IsNullOrEmpty(assetName) || assetName.Contains('/', StringComparison.Ordinal)) {
			return false;
		}

		downloadURL = uri;
		name = assetName;

		return true;
	}

	private static bool TryGetReleaseAssetsURL(Uri request, string? href, string repoName, string tag, [NotNullWhen(true)] out Uri? assetsURL) {
		ArgumentNullException.ThrowIfNull(request);
		ArgumentException.ThrowIfNullOrEmpty(repoName);
		ArgumentException.ThrowIfNullOrEmpty(tag);

		assetsURL = null;

		if (string.IsNullOrEmpty(href) || !Uri.TryCreate(request, href, out Uri? uri) || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) || !uri.Host.Equals(WebsiteURL.Host, StringComparison.OrdinalIgnoreCase)) {
			return false;
		}

		string assetsPrefix = $"/{repoName}/releases/expanded_assets/";

		if (!uri.AbsolutePath.StartsWith(assetsPrefix, StringComparison.OrdinalIgnoreCase) || !Uri.UnescapeDataString(uri.AbsolutePath[assetsPrefix.Length..]).Equals(tag, StringComparison.Ordinal)) {
			return false;
		}

		assetsURL = uri;

		return true;
	}

	private static bool TryGetReleaseTag(Uri request, string? href, string repoName, [NotNullWhen(true)] out string? tag) {
		ArgumentNullException.ThrowIfNull(request);
		ArgumentException.ThrowIfNullOrEmpty(repoName);

		tag = null;

		if (string.IsNullOrEmpty(href) || !Uri.TryCreate(request, href, out Uri? uri) || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) || !uri.Host.Equals(WebsiteURL.Host, StringComparison.OrdinalIgnoreCase)) {
			return false;
		}

		return TryGetReleaseTag(uri, repoName, out tag);
	}

	private static bool TryGetReleaseTag(Uri request, string repoName, [NotNullWhen(true)] out string? tag) {
		ArgumentNullException.ThrowIfNull(request);
		ArgumentException.ThrowIfNullOrEmpty(repoName);

		tag = null;

		string releasePrefix = $"/{repoName}/releases/tag/";

		if (!request.AbsolutePath.StartsWith(releasePrefix, StringComparison.OrdinalIgnoreCase)) {
			return false;
		}

		tag = Uri.UnescapeDataString(request.AbsolutePath[releasePrefix.Length..]);

		return !string.IsNullOrEmpty(tag);
	}

	private static bool TryParseAssetSize(string? sizeText, out uint size) {
		size = 0;

		if (string.IsNullOrWhiteSpace(sizeText)) {
			return false;
		}

		string[] sizeParts = sizeText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

		if ((sizeParts.Length != 2) || !decimal.TryParse(sizeParts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value) || (value <= 0)) {
			return false;
		}

		decimal multiplier = sizeParts[1].ToUpperInvariant() switch {
			"B" or "BYTES" => 1,
			"KB" or "KILOBYTES" => 1024,
			"MB" or "MEGABYTES" => 1024 * 1024,
			"GB" or "GIGABYTES" => 1024 * 1024 * 1024,
			_ => throw new InvalidOperationException(nameof(sizeParts))
		};

		decimal bytes = value * multiplier;

		size = bytes >= uint.MaxValue ? uint.MaxValue : (uint) bytes;

		return true;
	}
}
