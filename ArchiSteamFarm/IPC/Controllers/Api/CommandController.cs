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
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.IPC.Requests;
using ArchiSteamFarm.IPC.Responses;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace ArchiSteamFarm.IPC.Controllers.Api;

[Route("Api/Command")]
public sealed class CommandController : ArchiController {
	private readonly IHostApplicationLifetime ApplicationLifetime;

	public CommandController(IHostApplicationLifetime applicationLifetime) {
		ArgumentNullException.ThrowIfNull(applicationLifetime);

		ApplicationLifetime = applicationLifetime;
	}

	[EndpointDescription($"This API endpoint is supposed to be entirely replaced by ASF actions available under /Api/ASF/{{action}} and /Api/Bot/{{bot}}/{{action}}. You should use \"given bot\" commands when executing this endpoint, omitting targets of the command will cause the command to be executed on {nameof(GlobalConfig.DefaultBot)}")]
	[EndpointSummary("Executes a command")]
	[HttpPost]
	[ProducesResponseType<GenericResponse<string>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	public async Task<ActionResult<GenericResponse>> CommandPost([FromBody] CommandRequest request) {
		ArgumentNullException.ThrowIfNull(request);

		if (string.IsNullOrEmpty(request.Command)) {
			return BadRequest(new GenericResponse(false, Strings.FormatErrorIsEmpty(nameof(request.Command))));
		}

		Bot? targetBot = Bot.GetDefaultBot();

		if (targetBot == null) {
			return BadRequest(new GenericResponse(false, Strings.ErrorNoBotsDefined));
		}

		string command = request.Command;
		string? commandPrefix = ASF.GlobalConfig != null ? ASF.GlobalConfig.CommandPrefix : GlobalConfig.DefaultCommandPrefix;

		if (!string.IsNullOrEmpty(commandPrefix) && command.StartsWith(commandPrefix, StringComparison.Ordinal)) {
			if (command.Length == commandPrefix.Length) {
				// If the message starts with command prefix and is of the same length as command prefix, then it's just empty command trigger, useless
				return BadRequest(new GenericResponse(false, Strings.FormatErrorIsEmpty(nameof(command))));
			}

			command = command[commandPrefix.Length..];
		}

		// Update process can result in kestrel shutdown request, just before patching the files
		// In this case, we have very little opportunity to do anything, especially we will not have access to the return value of the command
		// That's because update command will synchronously stop the kestrel, and wait for it before proceeding with an update, and that'll wait for us finishing the request, never happening
		// Therefore, we'll allow this command to proceed while listening for application shutdown request, if it happens, we'll do our best by getting alternative signal that update is proceeding
		TaskCompletionSource<bool> applicationStopping = new();

		CancellationTokenRegistration applicationStoppingRegistration = ApplicationLifetime.ApplicationStopping.Register(() => applicationStopping.SetResult(true));

		await using (applicationStoppingRegistration.ConfigureAwait(false)) {
			Task<string?> commandTask = targetBot.Commands.Response(EAccess.Owner, command);

			string? response;

			if (await Task.WhenAny(commandTask, applicationStopping.Task).ConfigureAwait(false) == commandTask) {
				response = await commandTask.ConfigureAwait(false);
			} else {
				// It's almost guaranteed that this is the result of update process requesting kestrel shutdown
				// However, we're still going to check PendingVersionUpdate, which should be set by the update process as alternative way to inform us about pending update
				response = ASFController.PendingVersionUpdate != null ? Strings.PatchingFiles : Strings.Exiting;
			}

			return Ok(new GenericResponse<string>(response));
		}
	}
}
