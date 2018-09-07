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

			IEnumerable<Task<bool>> tasks = bots.Select(bot => bot.DeleteAllRelatedFiles());
			ICollection<bool> results;

			switch (Program.GlobalConfig.OptimizationMode) {
				case GlobalConfig.EOptimizationMode.MinMemoryUsage:
					results = new List<bool>(bots.Count);
					foreach (Task<bool> task in tasks) {
						results.Add(await task.ConfigureAwait(false));
					}

					break;
				default:
					results = await Task.WhenAll(tasks).ConfigureAwait(false);
					break;
			}

			if (results.Any(result => !result)) {
				return BadRequest(new GenericResponse(false, Strings.WarningFailed));
			}

			return Ok(new GenericResponse(true));
		}

		[HttpGet("{botNames:required}")]
		public ActionResult<GenericResponse<HashSet<Bot>>> Get(string botNames) {
			if (string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(botNames));
				return BadRequest(new GenericResponse(false, string.Format(Strings.ErrorIsEmpty, nameof(botNames))));
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return BadRequest(new GenericResponse(false, string.Format(Strings.BotNotFound, botNames)));
			}

			return Ok(new GenericResponse<HashSet<Bot>>(bots));
		}

		[HttpPost("{botName:required}")]
		public async Task<ActionResult<GenericResponse>> Post(string botName, [FromBody] BotRequest request) {
			if (string.IsNullOrEmpty(botName) || (request == null)) {
				return BadRequest(new GenericResponse(false, string.Format(Strings.ErrorIsEmpty, nameof(botName) + " || " + nameof(request))));
			}

			if (request.KeepSensitiveDetails && Bot.Bots.TryGetValue(botName, out Bot bot)) {
				if (string.IsNullOrEmpty(request.BotConfig.SteamLogin) && !string.IsNullOrEmpty(bot.BotConfig.SteamLogin)) {
					request.BotConfig.SteamLogin = bot.BotConfig.SteamLogin;
				}

				if (string.IsNullOrEmpty(request.BotConfig.SteamParentalPIN) && !string.IsNullOrEmpty(bot.BotConfig.SteamParentalPIN)) {
					request.BotConfig.SteamParentalPIN = bot.BotConfig.SteamParentalPIN;
				}

				if (string.IsNullOrEmpty(request.BotConfig.SteamPassword) && !string.IsNullOrEmpty(bot.BotConfig.SteamPassword)) {
					request.BotConfig.SteamPassword = bot.BotConfig.SteamPassword;
				}
			}

			request.BotConfig.ShouldSerializeEverything = false;

			string filePath = Path.Combine(SharedInfo.ConfigDirectory, botName + SharedInfo.ConfigExtension);

			if (!await BotConfig.Write(filePath, request.BotConfig).ConfigureAwait(false)) {
				return BadRequest(new GenericResponse(false, Strings.WarningFailed));
			}

			return Ok(new GenericResponse(true));
		}
	}
}
