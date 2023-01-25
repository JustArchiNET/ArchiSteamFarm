//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2023 Łukasz "JustArchi" Domeradzki
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

using System.ComponentModel.DataAnnotations;
using System.Net;
using Newtonsoft.Json;

namespace ArchiSteamFarm.IPC.Responses;

public sealed class StatusCodeResponse {
	/// <summary>
	///     Value indicating whether the status is permanent. If yes, retrying the request with exactly the same payload doesn't make sense due to a permanent problem (e.g. ASF misconfiguration).
	/// </summary>
	[JsonProperty(Required = Required.Always)]
	[Required]
	public bool Permanent { get; private set; }

	/// <summary>
	///     Status code transmitted in addition to the one in HTTP spec.
	/// </summary>
	[JsonProperty(Required = Required.Always)]
	[Required]
	public HttpStatusCode StatusCode { get; private set; }

	internal StatusCodeResponse(HttpStatusCode statusCode, bool permanent) {
		StatusCode = statusCode;
		Permanent = permanent;
	}
}
