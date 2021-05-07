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
using System.ComponentModel.DataAnnotations;
using ArchiSteamFarm.Storage;
using Newtonsoft.Json;

namespace ArchiSteamFarm.IPC.Responses {
	public sealed class ASFResponse {
		/// <summary>
		///     ASF's build variant.
		/// </summary>
		[JsonProperty(Required = Required.Always)]
		[Required]
		public string BuildVariant { get; private set; }

		/// <summary>
		///     A value specifying whether this variant of ASF is capable of auto-update.
		/// </summary>
		[JsonProperty(Required = Required.Always)]
		[Required]
		public bool CanUpdate { get; private set; }

		/// <summary>
		///     Currently loaded ASF's global config.
		/// </summary>
		[JsonProperty(Required = Required.Always)]
		[Required]
		public GlobalConfig GlobalConfig { get; private set; }

		/// <summary>
		///     Current amount of managed memory being used by the process, in kilobytes.
		/// </summary>
		[JsonProperty(Required = Required.Always)]
		[Required]
		public uint MemoryUsage { get; private set; }

		/// <summary>
		///     Start date of the process.
		/// </summary>
		[JsonProperty(Required = Required.Always)]
		[Required]
		public DateTime ProcessStartTime { get; private set; }

		/// <summary>
		///     ASF version of currently running binary.
		/// </summary>
		[JsonProperty(Required = Required.Always)]
		[Required]
		public Version Version { get; private set; }

		internal ASFResponse(string buildVariant, bool canUpdate, GlobalConfig globalConfig, uint memoryUsage, DateTime processStartTime, Version version) {
			BuildVariant = !string.IsNullOrEmpty(buildVariant) ? buildVariant : throw new ArgumentNullException(nameof(buildVariant));
			CanUpdate = canUpdate;
			GlobalConfig = globalConfig ?? throw new ArgumentNullException(nameof(globalConfig));
			MemoryUsage = memoryUsage > 0 ? memoryUsage : throw new ArgumentOutOfRangeException(nameof(memoryUsage));
			ProcessStartTime = processStartTime > DateTime.MinValue ? processStartTime : throw new ArgumentOutOfRangeException(nameof(processStartTime));
			Version = version ?? throw new ArgumentNullException(nameof(version));
		}
	}
}
