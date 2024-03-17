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
using System.Net;
using System.Text.Json;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.IPC.Responses;
using Microsoft.AspNetCore.Mvc;

namespace ArchiSteamFarm.IPC.Controllers.Api;

[Route("Api/Storage/{key:required}")]
public sealed class StorageController : ArchiController {
	/// <summary>
	///     Deletes entry under specified key from ASF's persistent KeyValue JSON storage.
	/// </summary>
	[HttpDelete]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.OK)]
	public ActionResult<GenericResponse> StorageDelete(string key) {
		ArgumentException.ThrowIfNullOrEmpty(key);

		if (ASF.GlobalDatabase == null) {
			throw new InvalidOperationException(nameof(ASF.GlobalDatabase));
		}

		ASF.GlobalDatabase.DeleteFromJsonStorage(key);

		return Ok(new GenericResponse(true));
	}

	/// <summary>
	///     Loads entry under specified key from ASF's persistent KeyValue JSON storage.
	/// </summary>
	[HttpGet]
	[ProducesResponseType<GenericResponse<JsonElement?>>((int) HttpStatusCode.OK)]
	public ActionResult<GenericResponse> StorageGet(string key) {
		ArgumentException.ThrowIfNullOrEmpty(key);

		if (ASF.GlobalDatabase == null) {
			throw new InvalidOperationException(nameof(ASF.GlobalDatabase));
		}

		JsonElement value = ASF.GlobalDatabase.LoadFromJsonStorage(key);

		return Ok(new GenericResponse<JsonElement?>(true, value.ValueKind != JsonValueKind.Undefined ? value : null));
	}

	/// <summary>
	///     Saves entry under specified key in ASF's persistent KeyValue JSON storage.
	/// </summary>
	[Consumes("application/json")]
	[HttpPost]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.OK)]
	public ActionResult<GenericResponse> StoragePost(string key, [FromBody] JsonElement value) {
		ArgumentException.ThrowIfNullOrEmpty(key);

		if (value.ValueKind == JsonValueKind.Undefined) {
			throw new ArgumentOutOfRangeException(nameof(value));
		}

		if (ASF.GlobalDatabase == null) {
			throw new InvalidOperationException(nameof(ASF.GlobalDatabase));
		}

		if (value.ValueKind == JsonValueKind.Null) {
			ASF.GlobalDatabase.DeleteFromJsonStorage(key);
		} else {
			ASF.GlobalDatabase.SaveToJsonStorage(key, value);
		}

		return Ok(new GenericResponse(true));
	}
}
