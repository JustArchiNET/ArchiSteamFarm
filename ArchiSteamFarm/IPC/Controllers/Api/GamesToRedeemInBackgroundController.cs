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
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using ArchiSteamFarm.IPC.Requests;
using ArchiSteamFarm.IPC.Responses;
using ArchiSteamFarm.Localization;
using Microsoft.AspNetCore.Mvc;

namespace ArchiSteamFarm.IPC.Controllers.Api {
	[ApiController]
	[Route("Api/GamesToRedeemInBackground")]
	public sealed class GamesToRedeemInBackgroundController : ControllerBase {
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

			IEnumerable<Task<bool>> tasks = bots.Select(bot => Task.Run(bot.DeleteRedeemedKeysFiles));
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
		public async Task<ActionResult<GenericResponse<Dictionary<string, GamesToRedeemInBackgroundResponse>>>> Get(string botNames) {
			if (string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(botNames));
				return BadRequest(new GenericResponse<Dictionary<string, GamesToRedeemInBackgroundResponse>>(false, string.Format(Strings.ErrorIsEmpty, nameof(botNames))));
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return BadRequest(new GenericResponse<Dictionary<string, GamesToRedeemInBackgroundResponse>>(false, string.Format(Strings.BotNotFound, botNames)));
			}

			IEnumerable<(string BotName, Task<(Dictionary<string, string> UnusedKeys, Dictionary<string, string> UsedKeys)> Task)> tasks = bots.Select(bot => (bot.BotName, bot.GetUsedAndUnusedKeys()));
			ICollection<(string BotName, (Dictionary<string, string> UnusedKeys, Dictionary<string, string> UsedKeys))> results;

			switch (Program.GlobalConfig.OptimizationMode) {
				case GlobalConfig.EOptimizationMode.MinMemoryUsage:
					results = new List<(string BotName, (Dictionary<string, string> UnusedKeys, Dictionary<string, string> UsedKeys))>(bots.Count);
					foreach ((string botName, Task<(Dictionary<string, string> UnusedKeys, Dictionary<string, string> UsedKeys)> task) in tasks) {
						results.Add((botName, await task.ConfigureAwait(false)));
					}

					break;
				default:
					results = await Task.WhenAll(tasks.Select(async task => (task.BotName, await task.Task.ConfigureAwait(false)))).ConfigureAwait(false);
					break;
			}

			Dictionary<string, GamesToRedeemInBackgroundResponse> result = new Dictionary<string, GamesToRedeemInBackgroundResponse>();

			foreach ((string botName, (Dictionary<string, string> UnusedKeys, Dictionary<string, string> UsedKeys) taskResult) in results) {
				result[botName] = new GamesToRedeemInBackgroundResponse(taskResult.UnusedKeys, taskResult.UsedKeys);
			}

			return Ok(new GenericResponse<Dictionary<string, GamesToRedeemInBackgroundResponse>>(result));
		}

		[HttpPost("{botName:required}")]
		public async Task<ActionResult<GenericResponse<OrderedDictionary>>> Post(string botName, [FromBody] GamesToRedeemInBackgroundRequest request) {
			if (string.IsNullOrEmpty(botName) || (request == null)) {
				ASF.ArchiLogger.LogNullError(nameof(botName) + " || " + nameof(request));
				return BadRequest(new GenericResponse<OrderedDictionary>(false, string.Format(Strings.ErrorIsEmpty, nameof(botName) + " || " + nameof(request))));
			}

			if (request.GamesToRedeemInBackground.Count == 0) {
				return BadRequest(new GenericResponse<OrderedDictionary>(false, string.Format(Strings.ErrorIsEmpty, nameof(request.GamesToRedeemInBackground))));
			}

			if (!Bot.Bots.TryGetValue(botName, out Bot bot)) {
				return BadRequest(new GenericResponse<OrderedDictionary>(false, string.Format(Strings.BotNotFound, botName)));
			}

			await bot.ValidateAndAddGamesToRedeemInBackground(request.GamesToRedeemInBackground).ConfigureAwait(false);
			return Ok(new GenericResponse<OrderedDictionary>(request.GamesToRedeemInBackground));
		}
	}
}
