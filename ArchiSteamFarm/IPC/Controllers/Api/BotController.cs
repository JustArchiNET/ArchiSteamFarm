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
using System.Collections.Specialized;
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
		public async Task<ActionResult<GenericResponse>> BotDelete(string botNames) {
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
		public ActionResult<GenericResponse<HashSet<Bot>>> BotGet(string botNames) {
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
		public async Task<ActionResult<GenericResponse>> BotPost(string botName, [FromBody] BotRequest request) {
			if (string.IsNullOrEmpty(botName) || (request == null)) {
				ASF.ArchiLogger.LogNullError(nameof(botName) + " || " + nameof(request));
				return BadRequest(new GenericResponse(false, string.Format(Strings.ErrorIsEmpty, nameof(botName) + " || " + nameof(request))));
			}

			(bool valid, string errorMessage) = request.BotConfig.CheckValidation();
			if (!valid) {
				return BadRequest(new GenericResponse(false, errorMessage));
			}

			if (Bot.Bots.TryGetValue(botName, out Bot bot)) {
				if (!request.BotConfig.IsSteamLoginSet && bot.BotConfig.IsSteamLoginSet) {
					request.BotConfig.SteamLogin = bot.BotConfig.SteamLogin;
				}

				if (!request.BotConfig.IsSteamPasswordSet && bot.BotConfig.IsSteamPasswordSet) {
					request.BotConfig.DecryptedSteamPassword = bot.BotConfig.DecryptedSteamPassword;
				}

				if (!request.BotConfig.IsSteamParentalCodeSet && bot.BotConfig.IsSteamParentalCodeSet) {
					request.BotConfig.SteamParentalCode = bot.BotConfig.SteamParentalCode;
				}
			}

			request.BotConfig.ShouldSerializeEverything = false;

			string filePath = Path.Combine(SharedInfo.ConfigDirectory, botName + SharedInfo.ConfigExtension);

			bool result = await BotConfig.Write(filePath, request.BotConfig).ConfigureAwait(false);
			return Ok(new GenericResponse(result));
		}

		[HttpDelete("{botNames:required}/GamesToRedeemInBackground")]
		public async Task<ActionResult<GenericResponse>> GamesToRedeemInBackgroundDelete(string botNames) {
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

		[HttpGet("{botNames:required}/GamesToRedeemInBackground")]
		public async Task<ActionResult<GenericResponse<Dictionary<string, GamesToRedeemInBackgroundResponse>>>> GamesToRedeemInBackgroundGet(string botNames) {
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

		[HttpPost("{botName:required}/GamesToRedeemInBackground")]
		public async Task<ActionResult<GenericResponse<OrderedDictionary>>> GamesToRedeemInBackgroundPost(string botName, [FromBody] GamesToRedeemInBackgroundRequest request) {
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

		[HttpPost("{botNames:required}/Pause")]
		public async Task<ActionResult<GenericResponse>> PausePost(string botNames, [FromBody] BotPauseRequest request) {
			if (string.IsNullOrEmpty(botNames) || (request == null)) {
				ASF.ArchiLogger.LogNullError(nameof(botNames) + " || " + nameof(request));
				return BadRequest(new GenericResponse(false, string.Format(Strings.ErrorIsEmpty, nameof(botNames) + " || " + nameof(request))));
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return BadRequest(new GenericResponse(false, string.Format(Strings.BotNotFound, botNames)));
			}

			IList<(bool Success, string Output)> results = await Utilities.InParallel(bots.Select(bot => bot.Actions.Pause(request.Permanent, request.ResumeInSeconds))).ConfigureAwait(false);
			return Ok(new GenericResponse(results.All(result => result.Success), string.Join(Environment.NewLine, results.Select(result => result.Output))));
		}

		[HttpPost("{botNames:required}/Resume")]
		public async Task<ActionResult<GenericResponse>> ResumePost(string botNames) {
			if (string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(botNames));
				return BadRequest(new GenericResponse(false, string.Format(Strings.ErrorIsEmpty, nameof(botNames))));
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return BadRequest(new GenericResponse(false, string.Format(Strings.BotNotFound, botNames)));
			}

			IList<(bool Success, string Output)> results = await Utilities.InParallel(bots.Select(bot => Task.Run(bot.Actions.Resume))).ConfigureAwait(false);
			return Ok(new GenericResponse(results.All(result => result.Success), string.Join(Environment.NewLine, results.Select(result => result.Output))));
		}

		[HttpPost("{botNames:required}/Start")]
		public async Task<ActionResult<GenericResponse>> StartPost(string botNames) {
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

		[HttpPost("{botNames:required}/Stop")]
		public async Task<ActionResult<GenericResponse>> StopPost(string botNames) {
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
	}
}
