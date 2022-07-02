//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2022 Łukasz "JustArchi" Domeradzki
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
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.IPC.Responses;
using ArchiSteamFarm.Localization;
using Microsoft.AspNetCore.Mvc;

namespace ArchiSteamFarm.IPC.Controllers.Api;

[Route("Api/Log")]
public sealed class LogController : ArchiController {
	/// <summary>
	///     Fetches ASF log file, this works on assumption that the log file is in fact generated, as user could disable it through custom configuration.
	/// </summary>
	/// <param name="count">Maximum amount of lines from the log file returned. The respone naturally might have less amount than specified, if you've read whole file already.</param>
	/// <param name="lastAt">Ending index, used for pagination. Omit it for the first request, then initialize to TotalLines returned, and on every following request subtract count that you've used in the previous request from it until you hit 0 or less, which means you've read whole file already.</param>
	[HttpGet]
	[ProducesResponseType(typeof(GenericResponse<GenericResponse<LogResponse>>), (int) HttpStatusCode.OK)]
	[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.BadRequest)]
	[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.ServiceUnavailable)]
	public async Task<ActionResult<GenericResponse>> Get(int count = 100, int lastAt = 0) {
		if (count <= 0) {
			return BadRequest(new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, nameof(count))));
		}

		if (lastAt < 0) {
			return BadRequest(new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, nameof(lastAt))));
		}

		if (!System.IO.File.Exists(SharedInfo.LogFile)) {
			return BadRequest(new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(SharedInfo.LogFile))));
		}

		string[] lines;

		try {
			lines = await System.IO.File.ReadAllLinesAsync(SharedInfo.LogFile).ConfigureAwait(false);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);

			return StatusCode((int) HttpStatusCode.ServiceUnavailable, new GenericResponse(false, e.Message));
		}

		if (lines.Length == 0) {
			return BadRequest(new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(SharedInfo.LogFile))));
		}

		if (lastAt > lines.Length) {
			return BadRequest(new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, nameof(lastAt))));
		}

		if (lastAt == 0) {
			lastAt = lines.Length;
		}

		int startFrom = Math.Max(lastAt - count, 0);

		return Ok(new GenericResponse<LogResponse>(new LogResponse(lines.Length, lines[startFrom..lastAt])));
	}
}
