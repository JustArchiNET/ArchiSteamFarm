// ----------------------------------------------------------------------------------------------
//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// ----------------------------------------------------------------------------------------------
// |
// Copyright 2015-2024 Łukasz "JustArchi" Domeradzki
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
using System.Globalization;
using System.Net;
using System.Threading.Tasks;
using ArchiSteamFarm.IPC.Requests;
using ArchiSteamFarm.IPC.Responses;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Plugins;
using ArchiSteamFarm.Plugins.Interfaces;
using ArchiSteamFarm.Steam.Interaction;
using Microsoft.AspNetCore.Mvc;

namespace ArchiSteamFarm.IPC.Controllers.Api;

[Route("Api/Plugins")]
public sealed class PluginsController : ArchiController {
	[HttpGet]
	[ProducesResponseType<GenericResponse<IReadOnlyCollection<IPlugin>>>((int) HttpStatusCode.OK)]
	public ActionResult<GenericResponse<IReadOnlyCollection<IPlugin>>> PluginsGet() => Ok(new GenericResponse<IReadOnlyCollection<IPlugin>>(PluginsCore.ActivePlugins));

	/// <summary>
	///     Makes ASF update selected plugins.
	/// </summary>
	[HttpPost("Update")]
	[ProducesResponseType<GenericResponse<string>>((int) HttpStatusCode.OK)]
	public async Task<ActionResult<GenericResponse<string>>> UpdatePost([FromBody] PluginUpdateRequest request) {
		ArgumentNullException.ThrowIfNull(request);

		if (request.Channel.HasValue && !Enum.IsDefined(request.Channel.Value)) {
			return BadRequest(new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, nameof(request.Channel))));
		}

		(bool success, string? message) = await Actions.UpdatePlugins(request.Channel, request.Plugins, request.Forced).ConfigureAwait(false);

		if (string.IsNullOrEmpty(message)) {
			message = success ? Strings.Success : Strings.WarningFailed;
		}

		return Ok(new GenericResponse<string>(success, message));
	}
}
