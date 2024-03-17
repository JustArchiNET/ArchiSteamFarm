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
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.IPC.Requests;
using ArchiSteamFarm.IPC.Responses;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Storage;
using Microsoft.AspNetCore.Mvc;

namespace ArchiSteamFarm.IPC.Controllers.Api;

[Route("Api/Command")]
public sealed class CommandController : ArchiController {
	/// <summary>
	///     Executes a command.
	/// </summary>
	/// <remarks>
	///     This API endpoint is supposed to be entirely replaced by ASF actions available under /Api/ASF/{action} and /Api/Bot/{bot}/{action}.
	///     You should use "given bot" commands when executing this endpoint, omitting targets of the command will cause the command to be executed on first defined bot
	/// </remarks>
	[Consumes("application/json")]
	[HttpPost]
	[ProducesResponseType<GenericResponse<string>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	public async Task<ActionResult<GenericResponse>> CommandPost([FromBody] CommandRequest request) {
		ArgumentNullException.ThrowIfNull(request);

		if (string.IsNullOrEmpty(request.Command)) {
			return BadRequest(new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(request.Command))));
		}

		Bot? targetBot = Bot.GetDefaultBot();

		if (targetBot == null) {
			return BadRequest(new GenericResponse(false, Strings.ErrorNoBotsDefined));
		}

		string command = request.Command;
		string? commandPrefix = ASF.GlobalConfig != null ? ASF.GlobalConfig.CommandPrefix : GlobalConfig.DefaultCommandPrefix;

		if (!string.IsNullOrEmpty(commandPrefix) && command.StartsWith(commandPrefix, StringComparison.Ordinal)) {
			if (command.Length == commandPrefix.Length) {
				// If the message starts with command prefix and is of the same length as command prefix, then it's just empty command trigger, useless
				return BadRequest(new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(command))));
			}

			command = command[commandPrefix.Length..];
		}

		string? response = await targetBot.Commands.Response(EAccess.Owner, command).ConfigureAwait(false);

		return Ok(new GenericResponse<string>(response));
	}
}
