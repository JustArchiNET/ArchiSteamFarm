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

using ArchiSteamFarm.IPC.Responses;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace ArchiSteamFarm.IPC.Controllers.Api {
	[ApiController]
	[Produces("application/json")]
	[SwaggerResponse(400, "The request has failed, check " + nameof(GenericResponse.Message) + " from response body for actual reason. Most of the time this is ASF, understanding the request, but refusing to execute it due to provided reason.", typeof(GenericResponse))]
	[SwaggerResponse(401, "ASF has " + nameof(GlobalConfig.IPCPassword) + " set, but you've failed to authenticate. See " + "https://github.com/" + SharedInfo.GithubRepo + "/wiki/IPC#authentication.")]
	[SwaggerResponse(403, "ASF has " + nameof(GlobalConfig.IPCPassword) + " set and you've failed to authenticate too many times, try again in an hour. See " + "https://github.com/" + SharedInfo.GithubRepo + "/wiki/IPC#authentication.")]
	public abstract class ArchiController : ControllerBase { }
}
