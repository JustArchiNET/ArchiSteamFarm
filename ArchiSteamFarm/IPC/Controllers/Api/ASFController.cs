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
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using ArchiSteamFarm.IPC.Requests;
using ArchiSteamFarm.IPC.Responses;
using ArchiSteamFarm.Localization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace ArchiSteamFarm.IPC.Controllers.Api {
	[Route("Api/ASF")]
	public sealed class ASFController : ArchiController {
		/// <summary>
		///     Fetches common info related to ASF as a whole.
		/// </summary>
		[HttpGet]
		[ProducesResponseType(typeof(GenericResponse<ASFResponse>), (int) HttpStatusCode.OK)]
		public ActionResult<GenericResponse<ASFResponse>> ASFGet() {
			if (ASF.GlobalConfig == null) {
				throw new ArgumentNullException(nameof(ASF.GlobalConfig));
			}

			uint memoryUsage = (uint) GC.GetTotalMemory(false) / 1024;

			ASFResponse result = new ASFResponse(SharedInfo.BuildInfo.Variant, ASF.GlobalConfig, memoryUsage, RuntimeCompatibility.ProcessStartTime, SharedInfo.Version);

			return Ok(new GenericResponse<ASFResponse>(result));
		}

		/// <summary>
		///     Updates ASF's global config.
		/// </summary>
		[Consumes("application/json")]
		[HttpPost]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.OK)]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.BadRequest)]
		public async Task<ActionResult<GenericResponse>> ASFPost([FromBody] ASFRequest request) {
			if (ASF.GlobalConfig == null) {
				throw new ArgumentNullException(nameof(ASF.GlobalConfig));
			}

			if (request?.GlobalConfig == null) {
				ASF.ArchiLogger.LogNullError(nameof(request) + " || " + nameof(request.GlobalConfig));

				return BadRequest(new GenericResponse(false, string.Format(Strings.ErrorIsEmpty, nameof(request))));
			}

			(bool valid, string? errorMessage) = request.GlobalConfig.CheckValidation();

			if (!valid) {
				return BadRequest(new GenericResponse(false, errorMessage));
			}

			request.GlobalConfig.ShouldSerializeDefaultValues = false;
			request.GlobalConfig.ShouldSerializeHelperProperties = false;
			request.GlobalConfig.ShouldSerializeSensitiveDetails = true;

			if (!request.GlobalConfig.IsWebProxyPasswordSet && ASF.GlobalConfig.IsWebProxyPasswordSet) {
				request.GlobalConfig.WebProxyPassword = ASF.GlobalConfig.WebProxyPassword;
			}

			if ((ASF.GlobalConfig.AdditionalProperties != null) && (ASF.GlobalConfig.AdditionalProperties.Count > 0)) {
				request.GlobalConfig.AdditionalProperties ??= new Dictionary<string, JToken>(ASF.GlobalConfig.AdditionalProperties.Count, ASF.GlobalConfig.AdditionalProperties.Comparer);

				foreach ((string key, JToken value) in ASF.GlobalConfig.AdditionalProperties.Where(property => !request.GlobalConfig.AdditionalProperties.ContainsKey(property.Key))) {
					request.GlobalConfig.AdditionalProperties.Add(key, value);
				}

				request.GlobalConfig.AdditionalProperties.TrimExcess();
			}

			string filePath = ASF.GetFilePath(ASF.EFileType.Config);

			if (string.IsNullOrEmpty(filePath)) {
				ASF.ArchiLogger.LogNullError(filePath);

				return BadRequest(new GenericResponse(false, string.Format(Strings.ErrorIsInvalid, nameof(filePath))));
			}

			bool result = await GlobalConfig.Write(filePath, request.GlobalConfig).ConfigureAwait(false);

			return Ok(new GenericResponse(result));
		}

		/// <summary>
		///     Makes ASF shutdown itself.
		/// </summary>
		[HttpPost("Exit")]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.OK)]
		public ActionResult<GenericResponse> ExitPost() {
			(bool success, string message) = Actions.Exit();

			return Ok(new GenericResponse(success, message));
		}

		/// <summary>
		///     Makes ASF restart itself.
		/// </summary>
		[HttpPost("Restart")]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.OK)]
		public ActionResult<GenericResponse> RestartPost() {
			(bool success, string message) = Actions.Restart();

			return Ok(new GenericResponse(success, message));
		}

		/// <summary>
		///     Makes ASF update itself.
		/// </summary>
		[HttpPost("Update")]
		[ProducesResponseType(typeof(GenericResponse<string>), (int) HttpStatusCode.OK)]
		public async Task<ActionResult<GenericResponse<string>>> UpdatePost() {
			(bool success, string? message, Version? version) = await Actions.Update().ConfigureAwait(false);

			if (string.IsNullOrEmpty(message)) {
				message = success ? Strings.Success : Strings.WarningFailed;
			}

			return Ok(new GenericResponse<string>(success, message!, version?.ToString()));
		}
	}
}
