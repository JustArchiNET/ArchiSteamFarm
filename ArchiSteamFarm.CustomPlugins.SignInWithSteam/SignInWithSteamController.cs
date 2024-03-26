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
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using AngleSharp.Dom;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.CustomPlugins.SignInWithSteam.Data;
using ArchiSteamFarm.IPC.Controllers.Api;
using ArchiSteamFarm.IPC.Responses;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Integration;
using ArchiSteamFarm.Web;
using ArchiSteamFarm.Web.Responses;
using Microsoft.AspNetCore.Mvc;

namespace ArchiSteamFarm.CustomPlugins.SignInWithSteam;

[Route("/Api/Bot/{botName:required}/SignInWithSteam")]
public sealed class SignInWithSteamController : ArchiController {
	[HttpPost]
	[ProducesResponseType<GenericResponse<SignInWithSteamResponse>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.ServiceUnavailable)]
	public async Task<ActionResult<GenericResponse>> Post(string botName, [FromBody] SignInWithSteamRequest request) {
		ArgumentException.ThrowIfNullOrEmpty(botName);
		ArgumentNullException.ThrowIfNull(request);

		Bot? bot = Bot.GetBot(botName);

		if (bot == null) {
			return BadRequest(new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.BotNotFound, botName)));
		}

		if (!bot.IsConnectedAndLoggedOn) {
			return StatusCode((int) HttpStatusCode.ServiceUnavailable, new GenericResponse(false, Strings.BotNotConnected));
		}

		// We've got a redirection, initiate OpenID procedure by following it
		using HtmlDocumentResponse? challengeResponse = await bot.ArchiWebHandler.UrlGetToHtmlDocumentWithSession(request.RedirectURL).ConfigureAwait(false);

		if (challengeResponse?.Content == null) {
			return StatusCode((int) HttpStatusCode.ServiceUnavailable, new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries)));
		}

		IAttr? paramsNode = challengeResponse.Content.SelectSingleNode<IAttr>("//input[@name='openidparams']/@value");

		if (paramsNode == null) {
			ASF.ArchiLogger.LogNullError(paramsNode);

			return StatusCode((int) HttpStatusCode.InternalServerError, new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorObjectIsNull, nameof(paramsNode))));
		}

		string paramsValue = paramsNode.Value;

		if (string.IsNullOrEmpty(paramsValue)) {
			ASF.ArchiLogger.LogNullError(paramsValue);

			return StatusCode((int) HttpStatusCode.InternalServerError, new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorObjectIsNull, nameof(paramsValue))));
		}

		IAttr? nonceNode = challengeResponse.Content.SelectSingleNode<IAttr>("//input[@name='nonce']/@value");

		if (nonceNode == null) {
			ASF.ArchiLogger.LogNullError(nonceNode);

			return StatusCode((int) HttpStatusCode.InternalServerError, new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorObjectIsNull, nameof(nonceNode))));
		}

		string nonceValue = nonceNode.Value;

		if (string.IsNullOrEmpty(nonceValue)) {
			ASF.ArchiLogger.LogNullError(nonceValue);

			return StatusCode((int) HttpStatusCode.InternalServerError, new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorObjectIsNull, nameof(nonceValue))));
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
		BasicResponse? loginResponse = await bot.ArchiWebHandler.WebBrowser.UrlPost(loginRequest, data: data, requestOptions: WebBrowser.ERequestOptions.ReturnRedirections).ConfigureAwait(false);

		return loginResponse != null ? Ok(new GenericResponse<SignInWithSteamResponse>(new SignInWithSteamResponse(loginResponse.FinalUri))) : StatusCode((int) HttpStatusCode.ServiceUnavailable, new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries)));
	}
}
