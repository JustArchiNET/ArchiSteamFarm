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

using ArchiSteamFarm.Localization;
using Newtonsoft.Json;

namespace ArchiSteamFarm.IPC.Responses {
	public sealed class GenericResponse<T> : GenericResponse where T : class {
		[JsonProperty]
		private readonly T Result;

		internal GenericResponse(T result) : base(result != null) => Result = result;
		internal GenericResponse(bool success, string message) : base(success, message) { }
		internal GenericResponse(bool success, T result) : base(success) => Result = result;
	}

	public class GenericResponse {
		[JsonProperty]
		private readonly string Message;

		[JsonProperty]
		private readonly bool Success;

		internal GenericResponse(bool success, string message = null) {
			Success = success;

			if (!string.IsNullOrEmpty(message)) {
				Message = message;
				return;
			}

			Message = success ? "OK" : Strings.WarningFailed;
		}
	}
}
