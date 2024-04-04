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
using ArchiSteamFarm.IPC.Integration;
using ArchiSteamFarm.IPC.Responses;
using ArchiSteamFarm.Localization;
using Microsoft.AspNetCore.Mvc;

namespace ArchiSteamFarm.IPC.Controllers.Api;

[Route("Api/IPC/Bans")]
public sealed class IPCBansController : ArchiController {
	/// <summary>
	///     Clears the list of all IP addresses currently blocked by ASFs IPC module
	/// </summary>
	[HttpDelete]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.OK)]
	public ActionResult<GenericResponse> Delete() {
		ApiAuthenticationMiddleware.ClearFailedAuthorizations();

		return Ok(new GenericResponse(true));
	}

	/// <summary>
	///     Removes an IP address from the list of addresses currently blocked by ASFs IPC module
	/// </summary>
	[HttpDelete("{ipAddress:required}")]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	public ActionResult<GenericResponse> DeleteSpecific(string ipAddress) {
		ArgumentException.ThrowIfNullOrEmpty(ipAddress);

		if (!IPAddress.TryParse(ipAddress, out IPAddress? remoteAddress)) {
			return BadRequest(new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, nameof(ipAddress))));
		}

		bool result = ApiAuthenticationMiddleware.UnbanIP(remoteAddress);

		if (!result) {
			return BadRequest(new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorIPNotBanned, ipAddress)));
		}

		return Ok(new GenericResponse(true));
	}

	/// <summary>
	///     Gets all IP addresses currently blocked by ASFs IPC module
	/// </summary>
	[HttpGet]
	[ProducesResponseType<GenericResponse<IReadOnlySet<string>>>((int) HttpStatusCode.OK)]
	public ActionResult<GenericResponse<IReadOnlySet<string>>> Get() => Ok(new GenericResponse<IReadOnlySet<string>>(ApiAuthenticationMiddleware.GetCurrentlyBannedIPs().Select(static ip => ip.ToString()).ToHashSet()));
}
