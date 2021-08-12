//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2021 Łukasz "JustArchi" Domeradzki
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

#if NETFRAMEWORK
using JustArchiNET.Madness;
#endif
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.IPC.Requests;
using ArchiSteamFarm.IPC.Responses;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Integration.Callbacks;
using ArchiSteamFarm.Steam.Security;
using ArchiSteamFarm.Steam.Storage;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace ArchiSteamFarm.IPC.Controllers.Api {
	[Route("Api/Bot")]
	public sealed class BotController : ArchiController {
		/// <summary>
		///     Deletes all files related to given bots.
		/// </summary>
		[HttpDelete("{botNames:required}")]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.OK)]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.BadRequest)]
		public async Task<ActionResult<GenericResponse>> BotDelete(string botNames) {
			if (string.IsNullOrEmpty(botNames)) {
				throw new ArgumentNullException(nameof(botNames));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return BadRequest(new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.BotNotFound, botNames)));
			}

			IList<bool> results = await Utilities.InParallel(bots.Select(bot => bot.DeleteAllRelatedFiles())).ConfigureAwait(false);

			return Ok(new GenericResponse(results.All(result => result)));
		}

		/// <summary>
		///     Fetches common info related to given bots.
		/// </summary>
		[HttpGet("{botNames:required}")]
		[ProducesResponseType(typeof(GenericResponse<IReadOnlyDictionary<string, Bot>>), (int) HttpStatusCode.OK)]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.BadRequest)]
		public ActionResult<GenericResponse> BotGet(string botNames) {
			if (string.IsNullOrEmpty(botNames)) {
				throw new ArgumentNullException(nameof(botNames));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if (bots == null) {
				return BadRequest(new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, nameof(bots))));
			}

			return Ok(new GenericResponse<IReadOnlyDictionary<string, Bot>>(bots.Where(bot => !string.IsNullOrEmpty(bot.BotName)).ToDictionary(bot => bot.BotName, bot => bot, Bot.BotsComparer)));
		}

		/// <summary>
		///     Updates bot config of given bot.
		/// </summary>
		[Consumes("application/json")]
		[HttpPost("{botNames:required}")]
		[ProducesResponseType(typeof(GenericResponse<IReadOnlyDictionary<string, bool>>), (int) HttpStatusCode.OK)]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.BadRequest)]
		public async Task<ActionResult<GenericResponse>> BotPost(string botNames, [FromBody] BotRequest request) {
			if (string.IsNullOrEmpty(botNames)) {
				throw new ArgumentNullException(nameof(botNames));
			}

			if (request == null) {
				throw new ArgumentNullException(nameof(request));
			}

			if (Bot.Bots == null) {
				throw new InvalidOperationException(nameof(Bot.Bots));
			}

			(bool valid, string? errorMessage) = request.BotConfig.CheckValidation();

			if (!valid) {
				return BadRequest(new GenericResponse(false, errorMessage));
			}

			request.BotConfig.Saving = true;

			HashSet<string> bots = botNames.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToHashSet(Bot.BotsComparer);

			if (bots.Any(botName => !ASF.IsValidBotName(botName))) {
				return BadRequest(new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, nameof(botNames))));
			}

			Dictionary<string, bool> result = new(bots.Count, Bot.BotsComparer);

			foreach (string botName in bots) {
				if (Bot.Bots.TryGetValue(botName, out Bot? bot)) {
					if (!request.BotConfig.IsSteamLoginSet && bot.BotConfig.IsSteamLoginSet) {
						request.BotConfig.SteamLogin = bot.BotConfig.SteamLogin;
					}

					if (!request.BotConfig.IsSteamPasswordSet && bot.BotConfig.IsSteamPasswordSet) {
						request.BotConfig.DecryptedSteamPassword = bot.BotConfig.DecryptedSteamPassword;
					}

					if (!request.BotConfig.IsSteamParentalCodeSet && bot.BotConfig.IsSteamParentalCodeSet) {
						request.BotConfig.SteamParentalCode = bot.BotConfig.SteamParentalCode;
					}

					if (bot.BotConfig.AdditionalProperties?.Count > 0) {
						request.BotConfig.AdditionalProperties ??= new Dictionary<string, JToken>(bot.BotConfig.AdditionalProperties.Count, bot.BotConfig.AdditionalProperties.Comparer);

						foreach ((string key, JToken value) in bot.BotConfig.AdditionalProperties.Where(property => !request.BotConfig.AdditionalProperties.ContainsKey(property.Key))) {
							request.BotConfig.AdditionalProperties.Add(key, value);
						}

						request.BotConfig.AdditionalProperties.TrimExcess();
					}
				}

				string filePath = Bot.GetFilePath(botName, Bot.EFileType.Config);

				if (string.IsNullOrEmpty(filePath)) {
					ASF.ArchiLogger.LogNullError(filePath);

					return BadRequest(new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, nameof(filePath))));
				}

				result[botName] = await BotConfig.Write(filePath, request.BotConfig).ConfigureAwait(false);
			}

			return Ok(new GenericResponse<IReadOnlyDictionary<string, bool>>(result.Values.All(value => value), result));
		}

		/// <summary>
		///     Removes BGR output files of given bots.
		/// </summary>
		[HttpDelete("{botNames:required}/GamesToRedeemInBackground")]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.OK)]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.BadRequest)]
		public async Task<ActionResult<GenericResponse>> GamesToRedeemInBackgroundDelete(string botNames) {
			if (string.IsNullOrEmpty(botNames)) {
				throw new ArgumentNullException(nameof(botNames));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return BadRequest(new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.BotNotFound, botNames)));
			}

			IList<bool> results = await Utilities.InParallel(bots.Select(bot => Task.Run(bot.DeleteRedeemedKeysFiles))).ConfigureAwait(false);

			return Ok(results.All(result => result) ? new GenericResponse(true) : new GenericResponse(false, Strings.WarningFailed));
		}

		/// <summary>
		///     Fetches BGR output files of given bots.
		/// </summary>
		[HttpGet("{botNames:required}/GamesToRedeemInBackground")]
		[ProducesResponseType(typeof(GenericResponse<IReadOnlyDictionary<string, GamesToRedeemInBackgroundResponse>>), (int) HttpStatusCode.OK)]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.BadRequest)]
		public async Task<ActionResult<GenericResponse>> GamesToRedeemInBackgroundGet(string botNames) {
			if (string.IsNullOrEmpty(botNames)) {
				throw new ArgumentNullException(nameof(botNames));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return BadRequest(new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.BotNotFound, botNames)));
			}

			IList<(Dictionary<string, string>? UnusedKeys, Dictionary<string, string>? UsedKeys)> results = await Utilities.InParallel(bots.Select(bot => bot.GetUsedAndUnusedKeys())).ConfigureAwait(false);

			Dictionary<string, GamesToRedeemInBackgroundResponse> result = new(bots.Count, Bot.BotsComparer);

			foreach (Bot bot in bots) {
				(Dictionary<string, string>? unusedKeys, Dictionary<string, string>? usedKeys) = results[result.Count];
				result[bot.BotName] = new GamesToRedeemInBackgroundResponse(unusedKeys, usedKeys);
			}

			return Ok(new GenericResponse<IReadOnlyDictionary<string, GamesToRedeemInBackgroundResponse>>(result));
		}

		/// <summary>
		///     Adds keys to redeem using BGR to given bot.
		/// </summary>
		[Consumes("application/json")]
		[HttpPost("{botNames:required}/GamesToRedeemInBackground")]
		[ProducesResponseType(typeof(GenericResponse<IReadOnlyDictionary<string, IOrderedDictionary>>), (int) HttpStatusCode.OK)]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.BadRequest)]
		public async Task<ActionResult<GenericResponse>> GamesToRedeemInBackgroundPost(string botNames, [FromBody] BotGamesToRedeemInBackgroundRequest request) {
			if (string.IsNullOrEmpty(botNames)) {
				throw new ArgumentNullException(nameof(botNames));
			}

			if (request == null) {
				throw new ArgumentNullException(nameof(request));
			}

			if (request.GamesToRedeemInBackground.Count == 0) {
				return BadRequest(new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(request.GamesToRedeemInBackground))));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return BadRequest(new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.BotNotFound, botNames)));
			}

			IOrderedDictionary validGamesToRedeemInBackground = Bot.ValidateGamesToRedeemInBackground(request.GamesToRedeemInBackground);

			if (validGamesToRedeemInBackground.Count == 0) {
				return BadRequest(new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(validGamesToRedeemInBackground))));
			}

			await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.AddGamesToRedeemInBackground(validGamesToRedeemInBackground)))).ConfigureAwait(false);

			Dictionary<string, IOrderedDictionary> result = new(bots.Count, Bot.BotsComparer);

			foreach (Bot bot in bots) {
				result[bot.BotName] = validGamesToRedeemInBackground;
			}

			return Ok(new GenericResponse<IReadOnlyDictionary<string, IOrderedDictionary>>(result));
		}

		/// <summary>
		///     Provides input value to given bot for next usage.
		/// </summary>
		[Consumes("application/json")]
		[HttpPost("{botNames:required}/Input")]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.OK)]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.BadRequest)]
		public async Task<ActionResult<GenericResponse>> InputPost(string botNames, [FromBody] BotInputRequest request) {
			if (string.IsNullOrEmpty(botNames)) {
				throw new ArgumentNullException(nameof(botNames));
			}

			if (request == null) {
				throw new ArgumentNullException(nameof(request));
			}

			if ((request.Type == ASF.EUserInputType.None) || !Enum.IsDefined(typeof(ASF.EUserInputType), request.Type) || string.IsNullOrEmpty(request.Value)) {
				return BadRequest(new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, nameof(request.Type) + " || " + nameof(request.Value))));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return BadRequest(new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.BotNotFound, botNames)));
			}

			IList<bool> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.SetUserInput(request.Type, request.Value)))).ConfigureAwait(false);

			return Ok(results.All(result => result) ? new GenericResponse(true) : new GenericResponse(false, Strings.WarningFailed));
		}

		/// <summary>
		///     Pauses given bots.
		/// </summary>
		[Consumes("application/json")]
		[HttpPost("{botNames:required}/Pause")]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.OK)]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.BadRequest)]
		public async Task<ActionResult<GenericResponse>> PausePost(string botNames, [FromBody] BotPauseRequest request) {
			if (string.IsNullOrEmpty(botNames)) {
				throw new ArgumentNullException(nameof(botNames));
			}

			if (request == null) {
				throw new ArgumentNullException(nameof(request));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return BadRequest(new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.BotNotFound, botNames)));
			}

			IList<(bool Success, string Message)> results = await Utilities.InParallel(bots.Select(bot => bot.Actions.Pause(request.Permanent, request.ResumeInSeconds))).ConfigureAwait(false);

			return Ok(new GenericResponse(results.All(result => result.Success), string.Join(Environment.NewLine, results.Select(result => result.Message))));
		}

		/// <summary>
		///     Redeems cd-keys on given bot.
		/// </summary>
		/// <remarks>
		///     Response contains a map that maps each provided cd-key to its redeem result.
		///     Redeem result can be a null value, this means that ASF didn't even attempt to send a request (e.g. because of bot not being connected to Steam network).
		/// </remarks>
		[Consumes("application/json")]
		[HttpPost("{botNames:required}/Redeem")]
		[ProducesResponseType(typeof(GenericResponse<IReadOnlyDictionary<string, IReadOnlyDictionary<string, PurchaseResponseCallback>>>), (int) HttpStatusCode.OK)]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.BadRequest)]
		public async Task<ActionResult<GenericResponse>> RedeemPost(string botNames, [FromBody] BotRedeemRequest request) {
			if (string.IsNullOrEmpty(botNames)) {
				throw new ArgumentNullException(nameof(botNames));
			}

			if (request == null) {
				throw new ArgumentNullException(nameof(request));
			}

			if (request.KeysToRedeem.Count == 0) {
				return BadRequest(new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(request.KeysToRedeem))));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return BadRequest(new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.BotNotFound, botNames)));
			}

			IList<PurchaseResponseCallback?> results = await Utilities.InParallel(bots.Select(bot => request.KeysToRedeem.Select(key => bot.Actions.RedeemKey(key))).SelectMany(task => task)).ConfigureAwait(false);

			Dictionary<string, IReadOnlyDictionary<string, PurchaseResponseCallback?>> result = new(bots.Count, Bot.BotsComparer);

			int count = 0;

			foreach (Bot bot in bots) {
				Dictionary<string, PurchaseResponseCallback?> responses = new(request.KeysToRedeem.Count, StringComparer.Ordinal);
				result[bot.BotName] = responses;

				foreach (string key in request.KeysToRedeem) {
					responses[key] = results[count++];
				}
			}

			return Ok(new GenericResponse<IReadOnlyDictionary<string, IReadOnlyDictionary<string, PurchaseResponseCallback?>>>(result.Values.SelectMany(responses => responses.Values).All(value => value != null), result));
		}

		/// <summary>
		///     Renames given bot along with all its related files.
		/// </summary>
		[Consumes("application/json")]
		[HttpPost("{botName:required}/Rename")]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.OK)]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.BadRequest)]
		public async Task<ActionResult<GenericResponse>> RenamePost(string botName, [FromBody] BotRenameRequest request) {
			if (string.IsNullOrEmpty(botName)) {
				throw new ArgumentNullException(nameof(botName));
			}

			if (request == null) {
				throw new ArgumentNullException(nameof(request));
			}

			if (Bot.Bots == null) {
				throw new InvalidOperationException(nameof(Bot.Bots));
			}

			if (string.IsNullOrEmpty(request.NewName) || !ASF.IsValidBotName(request.NewName) || Bot.Bots.ContainsKey(request.NewName)) {
				return BadRequest(new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, nameof(request.NewName))));
			}

			if (!Bot.Bots.TryGetValue(botName, out Bot? bot)) {
				return BadRequest(new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.BotNotFound, botName)));
			}

			bool result = await bot.Rename(request.NewName).ConfigureAwait(false);

			return Ok(new GenericResponse(result));
		}

		/// <summary>
		///     Resumes given bots.
		/// </summary>
		[HttpPost("{botNames:required}/Resume")]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.OK)]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.BadRequest)]
		public async Task<ActionResult<GenericResponse>> ResumePost(string botNames) {
			if (string.IsNullOrEmpty(botNames)) {
				throw new ArgumentNullException(nameof(botNames));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return BadRequest(new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.BotNotFound, botNames)));
			}

			IList<(bool Success, string Message)> results = await Utilities.InParallel(bots.Select(bot => Task.Run(bot.Actions.Resume))).ConfigureAwait(false);

			return Ok(new GenericResponse(results.All(result => result.Success), string.Join(Environment.NewLine, results.Select(result => result.Message))));
		}

		/// <summary>
		///     Starts given bots.
		/// </summary>
		[HttpPost("{botNames:required}/Start")]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.OK)]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.BadRequest)]
		public async Task<ActionResult<GenericResponse>> StartPost(string botNames) {
			if (string.IsNullOrEmpty(botNames)) {
				throw new ArgumentNullException(nameof(botNames));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return BadRequest(new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.BotNotFound, botNames)));
			}

			IList<(bool Success, string Message)> results = await Utilities.InParallel(bots.Select(bot => Task.Run(bot.Actions.Start))).ConfigureAwait(false);

			return Ok(new GenericResponse(results.All(result => result.Success), string.Join(Environment.NewLine, results.Select(result => result.Message))));
		}

		/// <summary>
		///     Stops given bots.
		/// </summary>
		[HttpPost("{botNames:required}/Stop")]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.OK)]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.BadRequest)]
		public async Task<ActionResult<GenericResponse>> StopPost(string botNames) {
			if (string.IsNullOrEmpty(botNames)) {
				throw new ArgumentNullException(nameof(botNames));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return BadRequest(new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.BotNotFound, botNames)));
			}

			IList<(bool Success, string Message)> results = await Utilities.InParallel(bots.Select(bot => Task.Run(bot.Actions.Stop))).ConfigureAwait(false);

			return Ok(new GenericResponse(results.All(result => result.Success), string.Join(Environment.NewLine, results.Select(result => result.Message))));
		}

		/// <summary>
		///     Handles 2FA confirmations of given bots, requires ASF 2FA module to be active on them.
		/// </summary>
		[HttpPost("{botNames:required}/TwoFactorAuthentication/Confirmations")]
		[ProducesResponseType(typeof(GenericResponse<IReadOnlyDictionary<string, GenericResponse<IReadOnlyCollection<Confirmation>>>>), (int) HttpStatusCode.OK)]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.BadRequest)]
		public async Task<ActionResult<GenericResponse>> TwoFactorAuthenticationConfirmationsPost(string botNames, [FromBody] TwoFactorAuthenticationConfirmationsRequest request) {
			if (string.IsNullOrEmpty(botNames)) {
				throw new ArgumentNullException(nameof(botNames));
			}

			if (request == null) {
				throw new ArgumentNullException(nameof(request));
			}

			if (request.AcceptedType.HasValue && ((request.AcceptedType.Value == Confirmation.EType.Unknown) || !Enum.IsDefined(typeof(Confirmation.EType), request.AcceptedType.Value))) {
				return BadRequest(new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, nameof(request.AcceptedType))));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return BadRequest(new GenericResponse<IReadOnlyDictionary<string, GenericResponse<IReadOnlyCollection<Confirmation>>>>(false, string.Format(CultureInfo.CurrentCulture, Strings.BotNotFound, botNames)));
			}

			IList<(bool Success, IReadOnlyCollection<Confirmation>? HandledConfirmations, string Message)> results = await Utilities.InParallel(bots.Select(bot => bot.Actions.HandleTwoFactorAuthenticationConfirmations(request.Accept, request.AcceptedType, request.AcceptedCreatorIDs.Count > 0 ? request.AcceptedCreatorIDs : null, request.WaitIfNeeded))).ConfigureAwait(false);

			Dictionary<string, GenericResponse<IReadOnlyCollection<Confirmation>>> result = new(bots.Count, Bot.BotsComparer);

			foreach (Bot bot in bots) {
				(bool success, IReadOnlyCollection<Confirmation>? handledConfirmations, string message) = results[result.Count];
				result[bot.BotName] = new GenericResponse<IReadOnlyCollection<Confirmation>>(success, message, handledConfirmations);
			}

			return Ok(new GenericResponse<IReadOnlyDictionary<string, GenericResponse<IReadOnlyCollection<Confirmation>>>>(result));
		}

		/// <summary>
		///     Fetches 2FA tokens of given bots, requires ASF 2FA module to be active on them.
		/// </summary>
		[HttpGet("{botNames:required}/TwoFactorAuthentication/Token")]
		[ProducesResponseType(typeof(GenericResponse<IReadOnlyDictionary<string, GenericResponse<string>>>), (int) HttpStatusCode.OK)]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.BadRequest)]
		public async Task<ActionResult<GenericResponse>> TwoFactorAuthenticationTokenGet(string botNames) {
			if (string.IsNullOrEmpty(botNames)) {
				throw new ArgumentNullException(nameof(botNames));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return BadRequest(new GenericResponse<IReadOnlyDictionary<string, GenericResponse<string>>>(false, string.Format(CultureInfo.CurrentCulture, Strings.BotNotFound, botNames)));
			}

			IList<(bool Success, string? Token, string Message)> results = await Utilities.InParallel(bots.Select(bot => bot.Actions.GenerateTwoFactorAuthenticationToken())).ConfigureAwait(false);

			Dictionary<string, GenericResponse<string>> result = new(bots.Count, Bot.BotsComparer);

			foreach (Bot bot in bots) {
				(bool success, string? token, string message) = results[result.Count];
				result[bot.BotName] = new GenericResponse<string>(success, message, token);
			}

			return Ok(new GenericResponse<IReadOnlyDictionary<string, GenericResponse<string>>>(result));
		}
	}
}
