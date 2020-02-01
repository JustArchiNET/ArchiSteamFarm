//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2020 Łukasz "JustArchi" Domeradzki
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
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using ArchiSteamFarm.IPC.Requests;
using ArchiSteamFarm.IPC.Responses;
using ArchiSteamFarm.Localization;
using Microsoft.AspNetCore.Mvc;

namespace ArchiSteamFarm.IPC.Controllers.Api {
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
		[ProducesResponseType(typeof(GenericResponse<string>), (int) HttpStatusCode.OK)]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.BadRequest)]
		public async Task<ActionResult<GenericResponse>> CommandPost([FromBody] CommandRequest request) {
			if (request == null) {
				ASF.ArchiLogger.LogNullError(nameof(request));

				return BadRequest(new GenericResponse(false, string.Format(Strings.ErrorIsEmpty, nameof(request))));
			}

			if (string.IsNullOrEmpty(request.Command)) {
				return BadRequest(new GenericResponse(false, string.Format(Strings.ErrorIsEmpty, nameof(request.Command))));
			}

			if (ASF.GlobalConfig.SteamOwnerID == 0) {
				return BadRequest(new GenericResponse(false, string.Format(Strings.ErrorIsInvalid, nameof(ASF.GlobalConfig.SteamOwnerID))));
			}

			Bot targetBot = Bot.Bots.OrderBy(bot => bot.Key, Bot.BotsComparer).Select(bot => bot.Value).FirstOrDefault();

			if (targetBot == null) {
				return BadRequest(new GenericResponse(false, Strings.ErrorNoBotsDefined));
			}

			string command = request.Command;

			if (!string.IsNullOrEmpty(ASF.GlobalConfig.CommandPrefix) && command.StartsWith(ASF.GlobalConfig.CommandPrefix, StringComparison.Ordinal)) {
				command = command.Substring(ASF.GlobalConfig.CommandPrefix.Length);

				if (string.IsNullOrEmpty(command)) {
					return BadRequest(new GenericResponse(false, string.Format(Strings.ErrorIsEmpty, nameof(command))));
				}
			}

			string response = await targetBot.Commands.Response(ASF.GlobalConfig.SteamOwnerID, command).ConfigureAwait(false);

			return Ok(new GenericResponse<string>(response));
		}
	}
}
