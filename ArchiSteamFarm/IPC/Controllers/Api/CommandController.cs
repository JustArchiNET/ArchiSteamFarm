//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// 
// Copyright 2015-2018 Łukasz "JustArchi" Domeradzki
// Contact: JustArchi@JustArchi.net
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
// http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Linq;
using System.Threading.Tasks;
using ArchiSteamFarm.IPC.Responses;
using ArchiSteamFarm.Localization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace ArchiSteamFarm.IPC.Controllers.Api {
	[ApiController]
	[Produces("application/json")]
	[Route("Api/Command")]
	[SwaggerResponse(400, "The request has failed, check " + nameof(GenericResponse.Message) + " from response body for actual reason. Most of the time this is ASF, understanding the request, but refusing to execute it due to provided reason.", typeof(GenericResponse))]
	[SwaggerResponse(401, "ASF has " + nameof(GlobalConfig.IPCPassword) + " set, but you've failed to authenticate. See " + "https://github.com/" + SharedInfo.GithubRepo + "/wiki/IPC#authentication.")]
	[SwaggerResponse(403, "ASF has " + nameof(GlobalConfig.IPCPassword) + " set and you've failed to authenticate too many times, try again in an hour. See " + "https://github.com/" + SharedInfo.GithubRepo + "/wiki/IPC#authentication.")]
	public sealed class CommandController : ControllerBase {
		/// <summary>
		/// Executes a command.
		/// </summary>
		/// <remarks>
		/// This API endpoint is supposed to be entirely replaced by ASF actions available under /Api/ASF/{action} and /Api/Bot/{bot}/{action}.
		/// You should use "given bot" commands when executing this endpoint, omitting targets of the command will cause the command to be executed on first defined bot
		/// </remarks>
		[HttpPost("{command:required}")]
		[SwaggerResponse(200, type: typeof(GenericResponse<string>))]
		public async Task<ActionResult<GenericResponse<string>>> CommandPost(string command) {
			if (string.IsNullOrEmpty(command)) {
				ASF.ArchiLogger.LogNullError(nameof(command));
				return BadRequest(new GenericResponse<string>(false, string.Format(Strings.ErrorIsEmpty, nameof(command))));
			}

			if (Program.GlobalConfig.SteamOwnerID == 0) {
				return BadRequest(new GenericResponse<string>(false, string.Format(Strings.ErrorIsInvalid, nameof(Program.GlobalConfig.SteamOwnerID))));
			}

			Bot targetBot = Bot.Bots.OrderBy(bot => bot.Key).Select(bot => bot.Value).FirstOrDefault();
			if (targetBot == null) {
				return BadRequest(new GenericResponse<string>(false, Strings.ErrorNoBotsDefined));
			}

			if (!string.IsNullOrEmpty(Program.GlobalConfig.CommandPrefix) && !command.StartsWith(Program.GlobalConfig.CommandPrefix, StringComparison.Ordinal)) {
				command = Program.GlobalConfig.CommandPrefix + command;
			}

			string response = await targetBot.Commands.Response(Program.GlobalConfig.SteamOwnerID, command).ConfigureAwait(false);
			return Ok(new GenericResponse<string>(response));
		}
	}
}
