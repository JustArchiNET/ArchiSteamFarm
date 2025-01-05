// ----------------------------------------------------------------------------------------------
//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// ----------------------------------------------------------------------------------------------
// |
// Copyright 2015-2025 Łukasz "JustArchi" Domeradzki
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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.IPC.Requests;
using ArchiSteamFarm.IPC.Responses;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Steam.Interaction;
using ArchiSteamFarm.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace ArchiSteamFarm.IPC.Controllers.Api;

[Route("Api/ASF")]
public sealed class ASFController : ArchiController {
	internal static Version? PendingVersionUpdate { get; set; }

	private readonly IHostApplicationLifetime ApplicationLifetime;

	public ASFController(IHostApplicationLifetime applicationLifetime) {
		ArgumentNullException.ThrowIfNull(applicationLifetime);

		ApplicationLifetime = applicationLifetime;
	}

	[EndpointSummary("Encrypts data with ASF encryption mechanisms using provided details.")]
	[HttpPost("Encrypt")]
	[ProducesResponseType<GenericResponse<string>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	public ActionResult<GenericResponse> ASFEncryptPost([FromBody] ASFEncryptRequest request) {
		ArgumentNullException.ThrowIfNull(request);

		if (string.IsNullOrEmpty(request.StringToEncrypt)) {
			return BadRequest(new GenericResponse(false, Strings.FormatErrorIsEmpty(nameof(request.StringToEncrypt))));
		}

		string? encryptedString = Actions.Encrypt(request.CryptoMethod, request.StringToEncrypt);

		return Ok(new GenericResponse<string>(encryptedString));
	}

	[EndpointSummary("Fetches common info related to ASF as a whole")]
	[HttpGet]
	[ProducesResponseType<GenericResponse<ASFResponse>>((int) HttpStatusCode.OK)]
	public ActionResult<GenericResponse<ASFResponse>> ASFGet() {
		if (ASF.GlobalConfig == null) {
			throw new InvalidOperationException(nameof(ASF.GlobalConfig));
		}

		uint memoryUsage = (uint) GC.GetTotalMemory(false) / 1024;

		ASFResponse result = new(BuildInfo.Variant, BuildInfo.CanUpdate, ASF.GlobalConfig, memoryUsage, OS.ProcessStartTime, SharedInfo.Version);

		return Ok(new GenericResponse<ASFResponse>(result));
	}

	[EndpointSummary("Hashes data with ASF hashing mechanisms using provided details")]
	[HttpPost("Hash")]
	[ProducesResponseType<GenericResponse<string>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	public ActionResult<GenericResponse> ASFHashPost([FromBody] ASFHashRequest request) {
		ArgumentNullException.ThrowIfNull(request);

		if (string.IsNullOrEmpty(request.StringToHash)) {
			return BadRequest(new GenericResponse(false, Strings.FormatErrorIsEmpty(nameof(request.StringToHash))));
		}

		string hash = Actions.Hash(request.HashingMethod, request.StringToHash);

		return Ok(new GenericResponse<string>(hash));
	}

	[EndpointSummary("Updates ASF's global config")]
	[HttpPost]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	public async Task<ActionResult<GenericResponse>> ASFPost([FromBody] ASFRequest request) {
		ArgumentNullException.ThrowIfNull(request);

		if (ASF.GlobalConfig == null) {
			throw new InvalidOperationException(nameof(ASF.GlobalConfig));
		}

		(bool valid, string? errorMessage) = request.GlobalConfig.CheckValidation();

		if (!valid) {
			return BadRequest(new GenericResponse(false, errorMessage));
		}

		request.GlobalConfig.Saving = true;

		if (!request.GlobalConfig.IsIPCPasswordSet && ASF.GlobalConfig.IsIPCPasswordSet) {
			request.GlobalConfig.IPCPassword = ASF.GlobalConfig.IPCPassword;
		}

		if (!request.GlobalConfig.IsLicenseIDSet && ASF.GlobalConfig.IsLicenseIDSet) {
			request.GlobalConfig.LicenseID = ASF.GlobalConfig.LicenseID;
		}

		if (!request.GlobalConfig.IsWebProxyPasswordSet && ASF.GlobalConfig.IsWebProxyPasswordSet) {
			request.GlobalConfig.WebProxyPassword = ASF.GlobalConfig.WebProxyPassword;
		}

		if (ASF.GlobalConfig.AdditionalProperties is { Count: > 0 }) {
			request.GlobalConfig.AdditionalProperties ??= new Dictionary<string, JsonElement>(ASF.GlobalConfig.AdditionalProperties.Count, ASF.GlobalConfig.AdditionalProperties.Comparer);

			foreach ((string key, JsonElement value) in ASF.GlobalConfig.AdditionalProperties.Where(property => !request.GlobalConfig.AdditionalProperties.ContainsKey(property.Key))) {
				request.GlobalConfig.AdditionalProperties.Add(key, value);
			}

			request.GlobalConfig.AdditionalProperties.TrimExcess();
		}

		string filePath = ASF.GetFilePath(ASF.EFileType.Config);

		if (string.IsNullOrEmpty(filePath)) {
			ASF.ArchiLogger.LogNullError(filePath);

			return BadRequest(new GenericResponse(false, Strings.FormatErrorIsInvalid(nameof(filePath))));
		}

		bool result = await GlobalConfig.Write(filePath, request.GlobalConfig).ConfigureAwait(false);

		return Ok(new GenericResponse(result));
	}

	[EndpointSummary("Makes ASF shutdown itself")]
	[HttpPost("Exit")]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.OK)]
	public ActionResult<GenericResponse> ExitPost() {
		(bool success, string message) = Actions.Exit();

		return Ok(new GenericResponse(success, message));
	}

	[EndpointSummary("Makes ASF restart itself")]
	[HttpPost("Restart")]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.OK)]
	public ActionResult<GenericResponse> RestartPost() {
		(bool success, string message) = Actions.Restart();

		return Ok(new GenericResponse(success, message));
	}

	[EndpointSummary("Makes ASF update itself")]
	[HttpPost("Update")]
	[ProducesResponseType<GenericResponse<string>>((int) HttpStatusCode.OK)]
	public async Task<ActionResult<GenericResponse<string>>> UpdatePost([FromBody] UpdateRequest request) {
		ArgumentNullException.ThrowIfNull(request);

		if (request.Channel.HasValue && !Enum.IsDefined(request.Channel.Value)) {
			return BadRequest(new GenericResponse(false, Strings.FormatErrorIsInvalid(nameof(request.Channel))));
		}

		// Update process can result in kestrel shutdown request, just before patching the files
		// In this case, we have very little opportunity to do anything, especially we will not have access to the return value of the action
		// That's because update action will synchronously stop the kestrel, and wait for it before proceeding with an update, and that'll wait for us finishing the request, never happening
		// Therefore, we'll allow this action to proceed while listening for application shutdown request, if it happens, we'll do our best by getting alternative signal that update is proceeding
		TaskCompletionSource<bool> applicationStopping = new();

		CancellationTokenRegistration applicationStoppingRegistration = ApplicationLifetime.ApplicationStopping.Register(() => applicationStopping.SetResult(true));

		await using (applicationStoppingRegistration.ConfigureAwait(false)) {
			Task<(bool Success, string? Message, Version? Version)> updateTask = Actions.Update(request.Channel, request.Forced);

			bool success;
			string? message = null;
			Version? version;

			if (await Task.WhenAny(updateTask, applicationStopping.Task).ConfigureAwait(false) == updateTask) {
				(success, message, version) = await updateTask.ConfigureAwait(false);
			} else {
				// It's almost guaranteed that this is the result of update process requesting kestrel shutdown
				// However, we're still going to check PendingVersionUpdate, which should be set by the update process as alternative way to inform us about pending update
				version = PendingVersionUpdate;
				success = version != null;
			}

			if (string.IsNullOrEmpty(message)) {
				message = success ? Strings.Success : Strings.WarningFailed;
			}

			return Ok(new GenericResponse<string>(success, message, version?.ToString()));
		}
	}
}
