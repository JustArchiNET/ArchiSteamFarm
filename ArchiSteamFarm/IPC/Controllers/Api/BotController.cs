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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ArchiSteamFarm.IPC.Requests;
using ArchiSteamFarm.IPC.Responses;
using ArchiSteamFarm.Localization;
using Microsoft.AspNetCore.Mvc;

namespace ArchiSteamFarm.IPC.Controllers.Api {
	[ApiController]
	[Route("Api/Bot")]
	public sealed class BotController : ControllerBase {
		[HttpDelete("{botNames:required}")]
		public async Task<ActionResult<GenericResponse>> Delete(string botNames) {
			if (string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(botNames));
				return BadRequest(new GenericResponse(false, string.Format(Strings.ErrorIsEmpty, nameof(botNames))));
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return BadRequest(new GenericResponse(false, string.Format(Strings.BotNotFound, botNames)));
			}

			IList<bool> results = await Utilities.InParallel(bots.Select(bot => bot.DeleteAllRelatedFiles())).ConfigureAwait(false);
			return Ok(new GenericResponse(results.All(result => result)));
		}

		[HttpGet("{botNames:required}")]
		public ActionResult<GenericResponse<HashSet<Bot>>> Get(string botNames) {
			if (string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(botNames));
				return BadRequest(new GenericResponse<HashSet<Bot>>(false, string.Format(Strings.ErrorIsEmpty, nameof(botNames))));
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);
			if (bots == null) {
				return BadRequest(new GenericResponse<HashSet<Bot>>(false, string.Format(Strings.ErrorIsInvalid, nameof(bots))));
			}

			return Ok(new GenericResponse<HashSet<Bot>>(bots));
		}

		[HttpPost("{botName:required}")]
		public async Task<ActionResult<GenericResponse>> Post(string botName, [FromBody] BotRequest request) {
			if (string.IsNullOrEmpty(botName) || (request == null)) {
				ASF.ArchiLogger.LogNullError(nameof(botName) + " || " + nameof(request));
				return BadRequest(new GenericResponse(false, string.Format(Strings.ErrorIsEmpty, nameof(botName) + " || " + nameof(request))));
			}

			(bool valid, string errorMessage) = request.BotConfig.CheckValidation();
			if (!valid) {
				return BadRequest(new GenericResponse(false, errorMessage));
			}

			if (request.KeepSensitiveDetails && Bot.Bots.TryGetValue(botName, out Bot bot)) {
				if (string.IsNullOrEmpty(request.BotConfig.SteamLogin) && !string.IsNullOrEmpty(bot.BotConfig.SteamLogin)) {
					request.BotConfig.SteamLogin = bot.BotConfig.SteamLogin;
				}

				if (string.IsNullOrEmpty(request.BotConfig.SteamParentalPIN) && !string.IsNullOrEmpty(bot.BotConfig.SteamParentalPIN)) {
					request.BotConfig.SteamParentalPIN = bot.BotConfig.SteamParentalPIN;
				}

				if (string.IsNullOrEmpty(request.BotConfig.OriginalSteamPassword) && !string.IsNullOrEmpty(bot.BotConfig.OriginalSteamPassword)) {
					request.BotConfig.OriginalSteamPassword = bot.BotConfig.OriginalSteamPassword;
				}
			}

			request.BotConfig.ShouldSerializeEverything = false;

			string filePath = Path.Combine(SharedInfo.ConfigDirectory, botName + SharedInfo.ConfigExtension);

			bool result = await BotConfig.Write(filePath, request.BotConfig).ConfigureAwait(false);
			return Ok(new GenericResponse(result));
		}

		[HttpPost("{botNames:required}/Stop")]
		public async Task<ActionResult<GenericResponse>> PostStop(string botNames) {
			if (string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(botNames));
				return BadRequest(new GenericResponse(false, string.Format(Strings.ErrorIsEmpty, nameof(botNames))));
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return BadRequest(new GenericResponse(false, string.Format(Strings.BotNotFound, botNames)));
			}

			IList<(bool Success, string Output)> results = await Utilities.InParallel(bots.Select(bot => Task.Run(bot.Actions.Stop))).ConfigureAwait(false);
			return Ok(new GenericResponse(results.All(result => result.Success), string.Join(Environment.NewLine, results.Select(result => result.Output))));
		}

		[HttpPost("{botNames:required}/Start")]
		public async Task<ActionResult<GenericResponse>> PostStart(string botNames) {
			if (string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(botNames));
				return BadRequest(new GenericResponse(false, string.Format(Strings.ErrorIsEmpty, nameof(botNames))));
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return BadRequest(new GenericResponse(false, string.Format(Strings.BotNotFound, botNames)));
			}

			IList<(bool Success, string Output)> results = await Utilities.InParallel(bots.Select(bot => Task.Run(bot.Actions.Start))).ConfigureAwait(false);
			return Ok(new GenericResponse(results.All(result => result.Success), string.Join(Environment.NewLine, results.Select(result => result.Output))));
		}
	}
}
