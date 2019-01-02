//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// 
// Copyright 2015-2019 Łukasz "JustArchi" Domeradzki
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
using ArchiSteamFarm.IPC.Responses;
using ArchiSteamFarm.Localization;
using Microsoft.AspNetCore.Mvc;

namespace ArchiSteamFarm.IPC.Controllers.Api {
	[Route("Api/Structure")]
	public sealed class StructureController : ArchiController {
		/// <summary>
		///     Fetches structure of given type.
		/// </summary>
		/// <remarks>
		///     Structure is defined as a representation of given object in its default state.
		/// </remarks>
		[HttpGet("{structure:required}")]
		[ProducesResponseType(typeof(GenericResponse<object>), 200)]
		public ActionResult<GenericResponse<object>> StructureGet(string structure) {
			if (string.IsNullOrEmpty(structure)) {
				ASF.ArchiLogger.LogNullError(nameof(structure));

				return BadRequest(new GenericResponse<object>(false, string.Format(Strings.ErrorIsEmpty, nameof(structure))));
			}

			Type targetType = WebUtilities.ParseType(structure);

			if (targetType == null) {
				return BadRequest(new GenericResponse<object>(false, string.Format(Strings.ErrorIsInvalid, structure)));
			}

			object obj;

			try {
				obj = Activator.CreateInstance(targetType, true);
			} catch (Exception e) {
				return BadRequest(new GenericResponse<object>(false, string.Format(Strings.ErrorParsingObject, nameof(targetType)) + Environment.NewLine + e));
			}

			return Ok(new GenericResponse<object>(obj));
		}
	}
}
