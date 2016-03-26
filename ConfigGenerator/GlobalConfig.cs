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
using System.Net.Sockets;

namespace ConfigGenerator {
	internal sealed class GlobalConfig : ASFConfig {
		internal enum EUpdateChannel : byte {
			Unknown,
			Stable,
			Experimental
		}

		private const byte DefaultMaxFarmingTime = 10;
		private const byte DefaultFarmingDelay = 5;
		private const byte DefaultHttpTimeout = 60;
		private const ushort DefaultWCFPort = 1242;
		private const ProtocolType DefaultSteamProtocol = ProtocolType.Tcp;

		// This is hardcoded blacklist which should not be possible to change
		internal static readonly HashSet<uint> GlobalBlacklist = new HashSet<uint> { 267420, 303700, 335590, 368020, 425280 };

		[JsonProperty(Required = Required.DisallowNull)]
		public bool Debug { get; set; } = false;

		[JsonProperty(Required = Required.DisallowNull)]
		public bool AutoUpdates { get; set; } = true;

		[JsonProperty(Required = Required.DisallowNull)]
		public EUpdateChannel UpdateChannel { get; set; } = EUpdateChannel.Stable;

		[JsonProperty(Required = Required.DisallowNull)]
		public ProtocolType SteamProtocol { get; set; } = DefaultSteamProtocol;

		[JsonProperty(Required = Required.DisallowNull)]
		public ulong SteamOwnerID { get; set; } = 0;

		[JsonProperty(Required = Required.DisallowNull)]
		public byte MaxFarmingTime { get; set; } = DefaultMaxFarmingTime;

		[JsonProperty(Required = Required.DisallowNull)]
		public byte IdleFarmingPeriod { get; set; } = 3;

		[JsonProperty(Required = Required.DisallowNull)]
		public byte FarmingDelay { get; set; } = DefaultFarmingDelay;

		[JsonProperty(Required = Required.DisallowNull)]
		public byte AccountPlayingDelay { get; set; } = 5;

		[JsonProperty(Required = Required.DisallowNull)]
		public byte LoginLimiterDelay { get; set; } = 7;

		[JsonProperty(Required = Required.DisallowNull)]
		public byte InventoryLimiterDelay { get; set; } = 3;

		[JsonProperty(Required = Required.DisallowNull)]
		public bool ForceHttp { get; set; } = false;

		[JsonProperty(Required = Required.DisallowNull)]
		public byte HttpTimeout { get; set; } = DefaultHttpTimeout;

		[JsonProperty]
		public string WCFHostname { get; set; } = "localhost";

		[JsonProperty(Required = Required.DisallowNull)]
		public ushort WCFPort { get; set; } = DefaultWCFPort;

		[JsonProperty(Required = Required.DisallowNull)]
		public bool LogToFile { get; set; } = true;

		[JsonProperty(Required = Required.DisallowNull)]
		public bool Statistics { get; set; } = true;

		// TODO: Please remove me immediately after https://github.com/SteamRE/SteamKit/issues/254 gets fixed
		[JsonProperty(Required = Required.DisallowNull)]
		public bool HackIgnoreMachineID { get; set; } = false;

		[JsonProperty(Required = Required.DisallowNull)]
		public List<uint> Blacklist { get; set; } = new List<uint>();

		internal static GlobalConfig Load(string filePath) {
			if (string.IsNullOrEmpty(filePath)) {
				return null;
			}

			if (!File.Exists(filePath)) {
				return new GlobalConfig(filePath);
			}

			GlobalConfig globalConfig;
			try {
				globalConfig = JsonConvert.DeserializeObject<GlobalConfig>(File.ReadAllText(filePath));
			} catch (Exception e) {
				Logging.LogGenericException(e);
				return new GlobalConfig(filePath);
			}

			globalConfig.FilePath = filePath;

			// SK2 supports only TCP and UDP steam protocols
			// Ensure that user can't screw this up
			switch (globalConfig.SteamProtocol) {
				case ProtocolType.Tcp:
				case ProtocolType.Udp:
					break;
				default:
					Logging.LogGenericWarning("Configured SteamProtocol is invalid: " + globalConfig.SteamProtocol + ". Value of " + DefaultSteamProtocol + " will be used instead");
					globalConfig.SteamProtocol = DefaultSteamProtocol;
					break;
			}

			// User might not know what he's doing
			// Ensure that he can't screw core ASF variables
			if (globalConfig.MaxFarmingTime == 0) {
				Logging.LogGenericWarning("Configured MaxFarmingTime is invalid: " + globalConfig.MaxFarmingTime + ". Value of " + DefaultMaxFarmingTime + " will be used instead");
				globalConfig.MaxFarmingTime = DefaultMaxFarmingTime;
			}

			if (globalConfig.FarmingDelay == 0) {
				Logging.LogGenericWarning("Configured FarmingDelay is invalid: " + globalConfig.FarmingDelay + ". Value of " + DefaultFarmingDelay + " will be used instead");
				globalConfig.FarmingDelay = DefaultFarmingDelay;
			}

			if (globalConfig.HttpTimeout == 0) {
				Logging.LogGenericWarning("Configured HttpTimeout is invalid: " + globalConfig.HttpTimeout + ". Value of " + DefaultHttpTimeout + " will be used instead");
				globalConfig.HttpTimeout = DefaultHttpTimeout;
			}

			if (globalConfig.WCFPort == 0) {
				Logging.LogGenericWarning("Configured WCFPort is invalid: " + globalConfig.WCFPort + ". Value of " + DefaultWCFPort + " will be used instead");
				globalConfig.WCFPort = DefaultWCFPort;
			}

			return globalConfig;
		}

		// This constructor is used only by deserializer
		private GlobalConfig() { }

		private GlobalConfig(string filePath) : base(filePath) {
			FilePath = filePath;
			Blacklist.AddRange(GlobalBlacklist);
			Save();
		}
	}
}
