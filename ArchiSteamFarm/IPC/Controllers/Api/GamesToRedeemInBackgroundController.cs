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

			IList<bool> results = await Utilities.InParallel(bots.Select(bot => Task.Run(bot.DeleteRedeemedKeysFiles))).ConfigureAwait(false);
			return Ok(results.All(result => result) ? new GenericResponse(true) : new GenericResponse(false, Strings.WarningFailed));
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

			IList<(Dictionary<string, string> UnusedKeys, Dictionary<string, string> UsedKeys)> results = await Utilities.InParallel(bots.Select(bot => bot.GetUsedAndUnusedKeys())).ConfigureAwait(false);

			Dictionary<string, GamesToRedeemInBackgroundResponse> result = new Dictionary<string, GamesToRedeemInBackgroundResponse>();

			foreach (Bot bot in bots) {
				(Dictionary<string, string> unusedKeys, Dictionary<string, string> usedKeys) = results[result.Count];
				result[bot.BotName] = new GamesToRedeemInBackgroundResponse(unusedKeys, usedKeys);
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

			bool result = await bot.ValidateAndAddGamesToRedeemInBackground(request.GamesToRedeemInBackground).ConfigureAwait(false);
			return Ok(new GenericResponse<OrderedDictionary>(result, request.GamesToRedeemInBackground));
		}
	}
}
