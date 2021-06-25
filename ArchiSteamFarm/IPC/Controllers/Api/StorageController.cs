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

using System;
using System.Net;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.IPC.Responses;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace ArchiSteamFarm.IPC.Controllers.Api {
	[Route("Api/Storage/{key:required}")]
	public sealed class StorageController : ArchiController {
		/// <summary>
		///     Deletes entry under specified key from ASF's persistent KeyValue JSON storage.
		/// </summary>
		[HttpDelete]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.OK)]
		public ActionResult<GenericResponse> StorageDelete(string key) {
			if (string.IsNullOrEmpty(key)) {
				throw new ArgumentNullException(nameof(key));
			}

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
		[ProducesResponseType(typeof(GenericResponse<JToken>), (int) HttpStatusCode.OK)]
		public ActionResult<GenericResponse> StorageGet(string key) {
			if (string.IsNullOrEmpty(key)) {
				throw new ArgumentNullException(nameof(key));
			}

			if (ASF.GlobalDatabase == null) {
				throw new InvalidOperationException(nameof(ASF.GlobalDatabase));
			}

			JToken? value = ASF.GlobalDatabase.LoadFromJsonStorage(key);

			return Ok(new GenericResponse<JToken>(true, value));
		}

		/// <summary>
		///     Saves entry under specified key in ASF's persistent KeyValue JSON storage.
		/// </summary>
		[Consumes("application/json")]
		[HttpPost]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.OK)]
		public ActionResult<GenericResponse> StoragePost(string key, [FromBody] JToken value) {
			if (string.IsNullOrEmpty(key)) {
				throw new ArgumentNullException(nameof(key));
			}

			if (value == null) {
				throw new ArgumentNullException(nameof(value));
			}

			if (ASF.GlobalDatabase == null) {
				throw new InvalidOperationException(nameof(ASF.GlobalDatabase));
			}

			if (value.Type == JTokenType.Null) {
				ASF.GlobalDatabase.DeleteFromJsonStorage(key);
			} else {
				ASF.GlobalDatabase.SaveToJsonStorage(key, value);
			}

			return Ok(new GenericResponse(true));
		}
	}
}
