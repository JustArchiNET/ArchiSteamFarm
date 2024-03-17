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
using System.Collections.Generic;
using System.Collections.Immutable;
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
	private static Uri URL => new("https://api.github.com");

	[PublicAPI]
	public static async Task<ReleaseResponse?> GetLatestRelease(string repoName, bool stable = true, CancellationToken cancellationToken = default) {
		ArgumentException.ThrowIfNullOrEmpty(repoName);

		if (stable) {
			Uri request = new(URL, $"/repos/{repoName}/releases/latest");

			return await GetReleaseFromURL(request, cancellationToken).ConfigureAwait(false);
		}

		ImmutableList<ReleaseResponse>? response = await GetReleases(repoName, 1, cancellationToken).ConfigureAwait(false);

		return response?.FirstOrDefault();
	}

	[PublicAPI]
	public static async Task<ReleaseResponse?> GetRelease(string repoName, string tag, CancellationToken cancellationToken = default) {
		ArgumentException.ThrowIfNullOrEmpty(repoName);
		ArgumentException.ThrowIfNullOrEmpty(tag);

		Uri request = new(URL, $"/repos/{repoName}/releases/tags/{tag}");

		return await GetReleaseFromURL(request, cancellationToken).ConfigureAwait(false);
	}

	[PublicAPI]
	public static async Task<ImmutableList<ReleaseResponse>?> GetReleases(string repoName, byte count = 10, CancellationToken cancellationToken = default) {
		ArgumentException.ThrowIfNullOrEmpty(repoName);
		ArgumentOutOfRangeException.ThrowIfZero(count);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 100);

		Uri request = new(URL, $"/repos/{repoName}/releases?per_page={count}");

		return await GetReleasesFromURL(request, cancellationToken).ConfigureAwait(false);
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
}
