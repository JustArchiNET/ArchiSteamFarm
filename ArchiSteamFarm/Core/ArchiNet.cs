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
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Dom;
using ArchiSteamFarm.Helpers;
using ArchiSteamFarm.IPC.Responses;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Integration;
using ArchiSteamFarm.Web;
using ArchiSteamFarm.Web.Responses;
using SteamKit2;

namespace ArchiSteamFarm.Core;

internal static class ArchiNet {
	internal static Uri URL => new("https://asf.JustArchi.net");

	private static readonly ArchiCacheable<IReadOnlyCollection<ulong>> CachedBadBots = new(ResolveCachedBadBots, TimeSpan.FromDays(1));

	internal static async Task<string?> FetchBuildChecksum(Version version, string variant, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(version);
		ArgumentException.ThrowIfNullOrEmpty(variant);

		if (ASF.WebBrowser == null) {
			throw new InvalidOperationException(nameof(ASF.WebBrowser));
		}

		Uri request = new(URL, $"/Api/Checksum/{version}/{variant}");

		ObjectResponse<GenericResponse<string>>? response;

		try {
			response = await ASF.WebBrowser.UrlGetToJsonObject<GenericResponse<string>>(request, cancellationToken: cancellationToken).ConfigureAwait(false);
		} catch (OperationCanceledException e) {
			ASF.ArchiLogger.LogGenericDebuggingException(e);

			return null;
		}

		if (response?.Content == null) {
			return null;
		}

		return response.Content.Result ?? "";
	}

	internal static async Task<bool?> IsBadBot(ulong steamID, CancellationToken cancellationToken = default) {
		if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		(_, IReadOnlyCollection<ulong>? badBots) = await CachedBadBots.GetValue(ECacheFallback.FailedNow, cancellationToken).ConfigureAwait(false);

		return badBots?.Contains(steamID);
	}

	internal static async Task<HttpStatusCode?> SignInWithSteam(Bot bot, WebBrowser webBrowser, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(bot);
		ArgumentNullException.ThrowIfNull(webBrowser);

		if (!bot.IsConnectedAndLoggedOn) {
			return null;
		}

		// We expect data or redirection to Steam OpenID
		Uri authenticateRequest = new(URL, $"/Api/Steam/Authenticate?steamID={bot.SteamID}");

		ObjectResponse<GenericResponse<ulong>>? authenticateResponse = await webBrowser.UrlGetToJsonObject<GenericResponse<ulong>>(authenticateRequest, requestOptions: WebBrowser.ERequestOptions.ReturnRedirections | WebBrowser.ERequestOptions.ReturnClientErrors | WebBrowser.ERequestOptions.AllowInvalidBodyOnErrors, cancellationToken: cancellationToken).ConfigureAwait(false);

		if (authenticateResponse == null) {
			return null;
		}

		if (authenticateResponse.StatusCode.IsClientErrorCode()) {
			return authenticateResponse.StatusCode;
		}

		if (authenticateResponse.StatusCode.IsSuccessCode()) {
			return authenticateResponse.Content?.Result == bot.SteamID ? HttpStatusCode.OK : HttpStatusCode.Unauthorized;
		}

		// We've got a redirection, initiate OpenID procedure by following it
		using HtmlDocumentResponse? challengeResponse = await bot.ArchiWebHandler.UrlGetToHtmlDocumentWithSession(authenticateResponse.FinalUri, cancellationToken: cancellationToken).ConfigureAwait(false);

		if (challengeResponse?.Content == null) {
			return null;
		}

		IAttr? paramsNode = challengeResponse.Content.SelectSingleNode<IAttr>("//input[@name='openidparams']/@value");

		if (paramsNode == null) {
			ASF.ArchiLogger.LogNullError(paramsNode);

			return null;
		}

		string paramsValue = paramsNode.Value;

		if (string.IsNullOrEmpty(paramsValue)) {
			ASF.ArchiLogger.LogNullError(paramsValue);

			return null;
		}

		IAttr? nonceNode = challengeResponse.Content.SelectSingleNode<IAttr>("//input[@name='nonce']/@value");

		if (nonceNode == null) {
			ASF.ArchiLogger.LogNullError(nonceNode);

			return null;
		}

		string nonceValue = nonceNode.Value;

		if (string.IsNullOrEmpty(nonceValue)) {
			ASF.ArchiLogger.LogNullError(nonceValue);

			return null;
		}

		Uri loginRequest = new(ArchiWebHandler.SteamCommunityURL, "/openid/login");

		using StringContent actionContent = new("steam_openid_login");
		using StringContent modeContent = new("checkid_setup");
		using StringContent paramsContent = new(paramsValue);
		using StringContent nonceContent = new(nonceValue);

		using MultipartFormDataContent data = new();

		data.Add(actionContent, "action");
		data.Add(modeContent, "openid.mode");
		data.Add(paramsContent, "openidparams");
		data.Add(nonceContent, "nonce");

		// Accept OpenID request presented and follow redirection back to the data we initially expected
		BasicResponse? loginResponse = await bot.ArchiWebHandler.WebBrowser.UrlPost(loginRequest, data: data, requestOptions: WebBrowser.ERequestOptions.ReturnRedirections, cancellationToken: cancellationToken).ConfigureAwait(false);

		if (loginResponse == null) {
			return null;
		}

		// We've got a final redirection, follow it and complete login procedure
		authenticateResponse = await webBrowser.UrlGetToJsonObject<GenericResponse<ulong>>(loginResponse.FinalUri, requestOptions: WebBrowser.ERequestOptions.ReturnClientErrors | WebBrowser.ERequestOptions.AllowInvalidBodyOnErrors, cancellationToken: cancellationToken).ConfigureAwait(false);

		if (authenticateResponse == null) {
			return null;
		}

		if (authenticateResponse.StatusCode.IsClientErrorCode()) {
			return authenticateResponse.StatusCode;
		}

		return authenticateResponse.Content?.Result == bot.SteamID ? HttpStatusCode.OK : HttpStatusCode.Unauthorized;
	}

	private static async Task<(bool Success, IReadOnlyCollection<ulong>? Result)> ResolveCachedBadBots(CancellationToken cancellationToken = default) {
		if (ASF.GlobalDatabase == null) {
			throw new InvalidOperationException(nameof(ASF.WebBrowser));
		}

		if (ASF.WebBrowser == null) {
			throw new InvalidOperationException(nameof(ASF.WebBrowser));
		}

		Uri request = new(URL, "/Api/BadBots");

		ObjectResponse<GenericResponse<ImmutableHashSet<ulong>>>? response;

		try {
			response = await ASF.WebBrowser.UrlGetToJsonObject<GenericResponse<ImmutableHashSet<ulong>>>(request, cancellationToken: cancellationToken).ConfigureAwait(false);
		} catch (OperationCanceledException e) {
			ASF.ArchiLogger.LogGenericDebuggingException(e);

			return (false, ASF.GlobalDatabase.CachedBadBots);
		}

		if (response?.Content?.Result == null) {
			return (false, ASF.GlobalDatabase.CachedBadBots);
		}

		ASF.GlobalDatabase.CachedBadBots.ReplaceIfNeededWith(response.Content.Result);

		return (true, response.Content.Result);
	}
}
