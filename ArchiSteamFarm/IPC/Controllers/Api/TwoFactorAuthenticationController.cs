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
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.IPC.Requests;
using ArchiSteamFarm.IPC.Responses;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Data;
using ArchiSteamFarm.Steam.Security;
using Microsoft.AspNetCore.Mvc;

namespace ArchiSteamFarm.IPC.Controllers.Api;

[Route("Api/Bot/{botNames:required}/TwoFactorAuthentication")]
public sealed class TwoFactorAuthenticationController : ArchiController {
	/// <summary>
	///     Fetches pending 2FA confirmations of given bots, requires ASF 2FA module to be active on them.
	/// </summary>
	[HttpGet("Confirmations")]
	[ProducesResponseType<GenericResponse<IReadOnlyDictionary<string, GenericResponse<IReadOnlyCollection<Confirmation>>>>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	public async Task<ActionResult<GenericResponse>> ConfirmationsGet(string botNames) {
		ArgumentException.ThrowIfNullOrEmpty(botNames);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return BadRequest(new GenericResponse<IReadOnlyDictionary<string, GenericResponse<IReadOnlyCollection<Confirmation>>>>(false, string.Format(CultureInfo.CurrentCulture, Strings.BotNotFound, botNames)));
		}

		IList<(bool Success, IReadOnlyCollection<Confirmation>? Confirmations, string Message)> results = await Utilities.InParallel(bots.Select(static bot => bot.Actions.GetConfirmations())).ConfigureAwait(false);

		Dictionary<string, GenericResponse<IReadOnlyCollection<Confirmation>>> result = new(bots.Count, Bot.BotsComparer);

		foreach (Bot bot in bots) {
			(bool success, IReadOnlyCollection<Confirmation>? confirmations, string message) = results[result.Count];
			result[bot.BotName] = new GenericResponse<IReadOnlyCollection<Confirmation>>(success, message, confirmations);
		}

		return Ok(new GenericResponse<IReadOnlyDictionary<string, GenericResponse<IReadOnlyCollection<Confirmation>>>>(result));
	}

	/// <summary>
	///     Handles 2FA confirmations of given bots, requires ASF 2FA module to be active on them.
	/// </summary>
	[Consumes("application/json")]
	[HttpPost("Confirmations")]
	[ProducesResponseType<GenericResponse<IReadOnlyDictionary<string, GenericResponse<IReadOnlyCollection<Confirmation>>>>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	public async Task<ActionResult<GenericResponse>> ConfirmationsPost(string botNames, [FromBody] TwoFactorAuthenticationConfirmationsRequest request) {
		ArgumentException.ThrowIfNullOrEmpty(botNames);
		ArgumentNullException.ThrowIfNull(request);

		if (request.AcceptedType.HasValue && ((request.AcceptedType.Value == Confirmation.EConfirmationType.Unknown) || !Enum.IsDefined(request.AcceptedType.Value))) {
			return BadRequest(new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, nameof(request.AcceptedType))));
		}

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return BadRequest(new GenericResponse<IReadOnlyDictionary<string, GenericResponse<IReadOnlyCollection<Confirmation>>>>(false, string.Format(CultureInfo.CurrentCulture, Strings.BotNotFound, botNames)));
		}

		IList<(bool Success, IReadOnlyCollection<Confirmation>? HandledConfirmations, string Message)> results = await Utilities.InParallel(bots.Select(bot => bot.Actions.HandleTwoFactorAuthenticationConfirmations(request.Accept, request.AcceptedType, request.AcceptedCreatorIDs.Count > 0 ? request.AcceptedCreatorIDs : null, request.WaitIfNeeded))).ConfigureAwait(false);

		Dictionary<string, GenericResponse<IReadOnlyCollection<Confirmation>>> result = new(bots.Count, Bot.BotsComparer);

		foreach (Bot bot in bots) {
			(bool success, IReadOnlyCollection<Confirmation>? handledConfirmations, string message) = results[result.Count];
			result[bot.BotName] = new GenericResponse<IReadOnlyCollection<Confirmation>>(success, message, handledConfirmations);
		}

		return Ok(new GenericResponse<IReadOnlyDictionary<string, GenericResponse<IReadOnlyCollection<Confirmation>>>>(result));
	}

	/// <summary>
	///     Deletes the MobileAuthenticator of given bots if an ASF 2FA module is active on them.
	/// </summary>
	[HttpDelete]
	[ProducesResponseType<GenericResponse<IReadOnlyDictionary<string, GenericResponse<string>>>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	public async Task<ActionResult<GenericResponse>> Delete(string botNames) {
		ArgumentException.ThrowIfNullOrEmpty(botNames);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return BadRequest(new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.BotNotFound, botNames)));
		}

		IList<(bool Success, string? Message)> results = await Utilities.InParallel(bots.Select(static bot => Task.Run(bot.RemoveAuthenticator))).ConfigureAwait(false);

		Dictionary<string, GenericResponse<string>> result = new(bots.Count, Bot.BotsComparer);

		foreach (Bot bot in bots) {
			(bool success, string? message) = results[result.Count];
			result[bot.BotName] = new GenericResponse<string>(success, message);
		}

		return Ok(new GenericResponse<IReadOnlyDictionary<string, GenericResponse<string>>>(result));
	}

	/// <summary>
	///     Imports a MobileAuthenticator into the ASF 2FA module of a given bot.
	/// </summary>
	[Consumes("application/json")]
	[HttpPost]
	[ProducesResponseType<GenericResponse<IReadOnlyDictionary<string, GenericResponse>>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	public async Task<ActionResult<GenericResponse>> Post(string botNames, [FromBody] MobileAuthenticator authenticator) {
		ArgumentException.ThrowIfNullOrEmpty(botNames);
		ArgumentNullException.ThrowIfNull(authenticator);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return BadRequest(new GenericResponse<IReadOnlyDictionary<string, GenericResponse<string>>>(false, string.Format(CultureInfo.CurrentCulture, Strings.BotNotFound, botNames)));
		}

		IList<bool> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.TryImportAuthenticator(authenticator)))).ConfigureAwait(false);

		Dictionary<string, GenericResponse> result = new(bots.Count, Bot.BotsComparer);

		foreach (Bot bot in bots) {
			bool success = results[result.Count];
			result[bot.BotName] = new GenericResponse(success);
		}

		return Ok(new GenericResponse<IReadOnlyDictionary<string, GenericResponse>>(result));
	}

	/// <summary>
	///     Fetches 2FA tokens of given bots, requires ASF 2FA module to be active on them.
	/// </summary>
	[HttpGet("Token")]
	[ProducesResponseType<GenericResponse<IReadOnlyDictionary<string, GenericResponse<string>>>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	public async Task<ActionResult<GenericResponse>> TokenGet(string botNames) {
		ArgumentException.ThrowIfNullOrEmpty(botNames);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return BadRequest(new GenericResponse<IReadOnlyDictionary<string, GenericResponse<string>>>(false, string.Format(CultureInfo.CurrentCulture, Strings.BotNotFound, botNames)));
		}

		IList<(bool Success, string? Token, string Message)> results = await Utilities.InParallel(bots.Select(static bot => bot.Actions.GenerateTwoFactorAuthenticationToken())).ConfigureAwait(false);

		Dictionary<string, GenericResponse<string>> result = new(bots.Count, Bot.BotsComparer);

		foreach (Bot bot in bots) {
			(bool success, string? token, string message) = results[result.Count];
			result[bot.BotName] = new GenericResponse<string>(success, message, token);
		}

		return Ok(new GenericResponse<IReadOnlyDictionary<string, GenericResponse<string>>>(result));
	}
}
