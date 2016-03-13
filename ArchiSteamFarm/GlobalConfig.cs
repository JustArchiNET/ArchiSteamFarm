/*
    _                _      _  ____   _                           _____
   / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
  / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
 / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
/_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|

 Copyright 2015-2016 Łukasz "JustArchi" Domeradzki
 Contact: JustArchi@JustArchi.net

 Licensed under the Apache License, Version 2.0 (the "License");
 you may not use this file except in compliance with the License.
 You may obtain a copy of the License at

 http://www.apache.org/licenses/LICENSE-2.0
					
 Unless required by applicable law or agreed to in writing, software
 distributed under the License is distributed on an "AS IS" BASIS,
 WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 See the License for the specific language governing permissions and
 limitations under the License.

*/

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace ArchiSteamFarm {
	internal sealed class GlobalConfig {
		internal enum EUpdateChannel : byte {
			Unknown,
			Stable,
			Experimental
		}

		// This is hardcoded blacklist which should not be possible to change
		internal static readonly HashSet<uint> GlobalBlacklist = new HashSet<uint> { 267420, 303700, 335590, 368020, 425280 };

		[JsonProperty(Required = Required.DisallowNull)]
		internal bool Debug { get; private set; } = false;

		[JsonProperty(Required = Required.DisallowNull)]
		internal bool AutoUpdates { get; private set; } = true;

		[JsonProperty(Required = Required.DisallowNull)]
		internal EUpdateChannel UpdateChannel { get; private set; } = EUpdateChannel.Stable;

		[JsonProperty(Required = Required.DisallowNull)]
		internal byte MaxFarmingTime { get; private set; } = 10;

		[JsonProperty(Required = Required.DisallowNull)]
		internal byte IdleFarmingPeriod { get; private set; } = 3;

		[JsonProperty(Required = Required.DisallowNull)]
		internal byte FarmingDelay { get; private set; } = 5;

		[JsonProperty(Required = Required.DisallowNull)]
		internal byte AccountPlayingDelay { get; private set; } = 5;

		[JsonProperty(Required = Required.DisallowNull)]
		internal byte LoginLimiterDelay { get; private set; } = 7;

		[JsonProperty(Required = Required.DisallowNull)]
		internal byte InventoryLimiterDelay { get; private set; } = 3;

		[JsonProperty(Required = Required.DisallowNull)]
		internal byte HttpTimeout { get; private set; } = 60;

		[JsonProperty(Required = Required.DisallowNull)]
		internal string WCFHostname { get; private set; } = "localhost";

		[JsonProperty(Required = Required.DisallowNull)]
		internal ushort WCFPort { get; private set; } = 1242;

		[JsonProperty(Required = Required.DisallowNull)]
		internal bool Statistics { get; private set; } = true;

		[JsonProperty(Required = Required.DisallowNull)]
		internal HashSet<uint> Blacklist { get; private set; } = new HashSet<uint>(GlobalBlacklist);

		internal static GlobalConfig Load() {
			string filePath = Path.Combine(Program.ConfigDirectory, Program.GlobalConfigFile);
			if (!File.Exists(filePath)) {
				return null;
			}

			GlobalConfig globalConfig;
			try {
				globalConfig = JsonConvert.DeserializeObject<GlobalConfig>(File.ReadAllText(filePath));
			} catch (Exception e) {
				Logging.LogGenericException(e);
				return null;
			}

			return globalConfig;
		}

		// This constructor is used only by deserializer
		private GlobalConfig() { }
	}
}
