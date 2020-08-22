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
using System.Net;
using System.Threading.Tasks;
using ArchiSteamFarm.IPC.Controllers.Api;
using ArchiSteamFarm.IPC.Responses;
using Microsoft.AspNetCore.Mvc;

namespace ArchiSteamFarm.CustomPlugins.ExamplePlugin {
	// This is an example class which shows you how you can extend ASF's API with your own custom API routes and controllers
	// You're free to decide whether you want to integrate with existing ASF concepts (such as ArchiController/GenericResponse), or roll out your own
	// All API controllers will be discovered during our Kestrel initialization using attributes mapping, you're also getting usual ASF goodies such as swagger documentation out of the box
	[Route("/Api/Cat")]
	public sealed class CatController : ArchiController {
		/// <summary>
		///     Fetches URL of a random cat picture.
		/// </summary>
		[HttpGet]
		[ProducesResponseType(typeof(GenericResponse<string>), (int) HttpStatusCode.OK)]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.ServiceUnavailable)]
		public async Task<ActionResult<GenericResponse>> CatGet() {
			if (ASF.WebBrowser == null) {
				throw new ArgumentNullException(nameof(ASF.WebBrowser));
			}

			string? link = await CatAPI.GetRandomCatURL(ASF.WebBrowser).ConfigureAwait(false);

			return !string.IsNullOrEmpty(link) ? Ok(new GenericResponse<string>(link)) : StatusCode((int) HttpStatusCode.ServiceUnavailable, new GenericResponse(false));
		}
	}
}
