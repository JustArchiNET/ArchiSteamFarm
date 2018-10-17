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

using System;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace ArchiSteamFarm.IPC.Responses {
	public sealed class ASFResponse {
		/// <summary>
		///     ASF's build variant.
		/// </summary>
		[JsonProperty(Required = Required.Always)]
		[Required]
		public readonly string BuildVariant;

		/// <summary>
		///     Currently loaded ASF's global config.
		/// </summary>
		[JsonProperty(Required = Required.Always)]
		[Required]
		public readonly GlobalConfig GlobalConfig;

		/// <summary>
		///     Current amount of managed memory being used by the process, in kilobytes.
		/// </summary>
		[JsonProperty(Required = Required.Always)]
		[Required]
		public readonly uint MemoryUsage;

		/// <summary>
		///     Start date of the process.
		/// </summary>
		[JsonProperty(Required = Required.Always)]
		[Required]
		public readonly DateTime ProcessStartTime;

		/// <summary>
		///     ASF version of currently running binary.
		/// </summary>
		[JsonProperty(Required = Required.Always)]
		[Required]
		public readonly Version Version;

		internal ASFResponse(string buildVariant, GlobalConfig globalConfig, uint memoryUsage, DateTime processStartTime, Version version) {
			if (string.IsNullOrEmpty(buildVariant) || (globalConfig == null) || (memoryUsage == 0) || (processStartTime == DateTime.MinValue) || (version == null)) {
				throw new ArgumentNullException(nameof(buildVariant) + " || " + nameof(globalConfig) + " || " + nameof(memoryUsage) + " || " + nameof(processStartTime) + " || " + nameof(version));
			}

			BuildVariant = buildVariant;
			GlobalConfig = globalConfig;
			MemoryUsage = memoryUsage;
			ProcessStartTime = processStartTime;
			Version = version;
		}
	}
}
