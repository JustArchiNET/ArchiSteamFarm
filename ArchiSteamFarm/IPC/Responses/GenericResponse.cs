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

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ArchiSteamFarm.Localization;

namespace ArchiSteamFarm.IPC.Responses;

public sealed class GenericResponse<T> : GenericResponse {
	[Description("The actual result of the request, if available. The type of the result depends on the API endpoint that you've called")]
	[JsonInclude]
	public T? Result { get; private init; }

	public GenericResponse(T? result) : base(result is not null) => Result = result;
	public GenericResponse(bool success, string? message) : base(success, message) { }
	public GenericResponse(bool success, T? result) : base(success) => Result = result;
	public GenericResponse(bool success, string? message, T? result) : base(success, message) => Result = result;

	[JsonConstructor]
	private GenericResponse() { }
}

public class GenericResponse {
	[Description("A message that describes what happened with the request, if available. This property will provide exact reason for majority of expected failures")]
	[JsonInclude]
	public string? Message { get; private init; }

	[Description("Boolean type that specifies if the request has succeeded")]
	[JsonInclude]
	[JsonRequired]
	[Required]
	public bool Success { get; private init; }

	public GenericResponse(bool success, string? message = null) {
		Success = success;
		Message = !string.IsNullOrEmpty(message) ? message : success ? "OK" : Strings.WarningFailed;
	}

	[JsonConstructor]
	protected GenericResponse() { }
}
