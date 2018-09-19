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

namespace ArchiSteamFarm.IPC.Controllers.Api {
	[ApiController]
	[Route("Api/Command")]
	public sealed class CommandController : ControllerBase {
		[HttpPost("{command:required}")]
		public async Task<ActionResult<GenericResponse<string>>> Post(string command) {
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
