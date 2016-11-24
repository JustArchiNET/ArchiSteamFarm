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

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Sockets;
using Newtonsoft.Json;

namespace ConfigGenerator {
	[SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
	[SuppressMessage("ReSharper", "CollectionNeverQueried.Global")]
	[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
	[SuppressMessage("ReSharper", "UnusedMember.Global")]
	internal sealed class GlobalConfig : ASFConfig {
		private const byte DefaultFarmingDelay = 15;
		private const byte DefaultHttpTimeout = 60;
		private const byte DefaultMaxFarmingTime = 10;
		private const ProtocolType DefaultSteamProtocol = ProtocolType.Tcp;
		private const ushort DefaultWCFPort = 1242;

		// This is hardcoded blacklist which should not be possible to change
		private static readonly HashSet<uint> GlobalBlacklist = new HashSet<uint> { 267420, 303700, 335590, 368020, 425280, 480730 };

		[Category("\tUpdates")]
		[JsonProperty(Required = Required.DisallowNull)]
		public bool AutoRestart { get; set; } = true;

		[Category("\tUpdates")]
		[JsonProperty(Required = Required.DisallowNull)]
		public bool AutoUpdates { get; set; } = true;

		[JsonProperty(Required = Required.DisallowNull)]
		public List<uint> Blacklist { get; set; } = new List<uint>();

		[Category("\tDebugging")]
		[JsonProperty(Required = Required.DisallowNull)]
		public bool Debug { get; set; } = false;

		[Category("\tPerformance")]
		[JsonProperty(Required = Required.DisallowNull)]
		public byte FarmingDelay { get; set; } = DefaultFarmingDelay;

		[Category("\tDebugging")]
		[JsonProperty(Required = Required.DisallowNull)]
		public bool ForceHttp { get; set; } = false;

		[Category("\tPerformance")]
		[JsonProperty(Required = Required.DisallowNull)]
		public byte GiftsLimiterDelay { get; set; } = 1;

		[Category("\tAdvanced")]
		[JsonProperty(Required = Required.DisallowNull)]
		public bool Headless { get; set; } = false;

		[Category("\tDebugging")]
		[JsonProperty(Required = Required.DisallowNull)]
		public byte HttpTimeout { get; set; } = DefaultHttpTimeout;

		[Category("\tPerformance")]
		[JsonProperty(Required = Required.DisallowNull)]
		public byte IdleFarmingPeriod { get; set; } = 3;

		[Category("\tPerformance")]
		[JsonProperty(Required = Required.DisallowNull)]
		public byte InventoryLimiterDelay { get; set; } = 3;

		[Category("\tPerformance")]
		[JsonProperty(Required = Required.DisallowNull)]
		public byte LoginLimiterDelay { get; set; } = 10;

		[Category("\tPerformance")]
		[JsonProperty(Required = Required.DisallowNull)]
		public byte MaxFarmingTime { get; set; } = DefaultMaxFarmingTime;

		[JsonProperty(Required = Required.DisallowNull)]
		public byte MaxTradeHoldDuration { get; set; } = 15;

		[JsonProperty(Required = Required.DisallowNull)]
		public bool Statistics { get; set; } = true;

		[Category("\tAccess")]
		[JsonProperty(Required = Required.DisallowNull)]
		public ulong SteamOwnerID { get; set; } = 0;

		[Category("\tAdvanced")]
		[JsonProperty(Required = Required.DisallowNull)]
		public ProtocolType SteamProtocol { get; set; } = DefaultSteamProtocol;

		[Category("\tUpdates")]
		[JsonProperty(Required = Required.DisallowNull)]
		public EUpdateChannel UpdateChannel { get; set; } = EUpdateChannel.Stable;

		[Category("\tAccess")]
		[JsonProperty]
		public string WCFHostname { get; set; } = "localhost";

		[Category("\tAccess")]
		[JsonProperty(Required = Required.DisallowNull)]
		public ushort WCFPort { get; set; } = DefaultWCFPort;

		[SuppressMessage("ReSharper", "UnusedMember.Local")]
		private GlobalConfig() { }

		private GlobalConfig(string filePath) : base(filePath) {
			if (string.IsNullOrEmpty(filePath)) {
				throw new ArgumentNullException(nameof(filePath));
			}

			Blacklist.AddRange(GlobalBlacklist);
			Save();
		}

		internal static GlobalConfig Load(string filePath) {
			if (string.IsNullOrEmpty(filePath)) {
				Logging.LogNullError(nameof(filePath));
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

			if (globalConfig == null) {
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

			if (globalConfig.WCFPort != 0) {
				return globalConfig;
			}

			Logging.LogGenericWarning("Configured WCFPort is invalid: " + globalConfig.WCFPort + ". Value of " + DefaultWCFPort + " will be used instead");
			globalConfig.WCFPort = DefaultWCFPort;

			return globalConfig;
		}

		internal enum EUpdateChannel : byte {
			None,
			Stable,
			Experimental
		}
	}
}