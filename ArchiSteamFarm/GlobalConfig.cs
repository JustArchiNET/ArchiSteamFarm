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
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Sockets;

namespace ArchiSteamFarm {
	[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
	[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
	[SuppressMessage("ReSharper", "ConvertToConstant.Global")]
	internal sealed class GlobalConfig {
		[SuppressMessage("ReSharper", "UnusedMember.Global")]
		internal enum EUpdateChannel : byte {
			Unknown,
			Stable,
			Experimental
		}

		internal const byte DefaultHttpTimeout = 60;

		private const byte DefaultMaxFarmingTime = 10;
		private const byte DefaultFarmingDelay = 15;
		private const ushort DefaultWCFPort = 1242;
		private const ProtocolType DefaultSteamProtocol = ProtocolType.Tcp;

		// This is hardcoded blacklist which should not be possible to change
		internal static readonly HashSet<uint> GlobalBlacklist = new HashSet<uint> { 267420, 303700, 335590, 368020, 425280, 480730 };

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool Debug = false;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool Headless = false;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool AutoUpdates = true;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool AutoRestart = true;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly EUpdateChannel UpdateChannel = EUpdateChannel.Stable;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly ProtocolType SteamProtocol = DefaultSteamProtocol;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly ulong SteamOwnerID = 0;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly byte MaxFarmingTime = DefaultMaxFarmingTime;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly byte IdleFarmingPeriod = 3;

		[JsonProperty(Required = Required.DisallowNull)]
		internal byte FarmingDelay { get; private set; } = DefaultFarmingDelay;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly byte LoginLimiterDelay = 10;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly byte InventoryLimiterDelay = 3;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly byte GiftsLimiterDelay = 1;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly byte MaxTradeHoldDuration = 15;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool ForceHttp = false;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly byte HttpTimeout = DefaultHttpTimeout;

		[JsonProperty]
		internal string WCFHostname { get; set; } = "localhost";

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly ushort WCFPort = DefaultWCFPort;

        [JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool Statistics = true;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly HashSet<uint> Blacklist = new HashSet<uint>(GlobalBlacklist);

		internal static GlobalConfig Load(string filePath) {
			if (string.IsNullOrEmpty(filePath)) {
				Logging.LogNullError(nameof(filePath));
				return null;
			}

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

			if (globalConfig == null) {
				Logging.LogNullError(nameof(globalConfig));
				return null;
			}

			// SK2 supports only TCP and UDP steam protocols
			// Ensure that user can't screw this up
			switch (globalConfig.SteamProtocol) {
				case ProtocolType.Tcp:
				case ProtocolType.Udp:
					break;
				default:
					Logging.LogGenericWarning("Configured SteamProtocol is invalid: " + globalConfig.SteamProtocol);
					return null;
			}

			// User might not know what he's doing
			// Ensure that he can't screw core ASF variables
			if (globalConfig.MaxFarmingTime == 0) {
				Logging.LogGenericWarning("Configured MaxFarmingTime is invalid: " + globalConfig.MaxFarmingTime);
				return null;
			}

			if (globalConfig.FarmingDelay == 0) {
				Logging.LogGenericWarning("Configured FarmingDelay is invalid: " + globalConfig.FarmingDelay);
				return null;
			}

			if ((globalConfig.FarmingDelay > 5) && Runtime.RequiresWorkaroundForMonoBug41701()) {
				Logging.LogGenericWarning("Your Mono runtime is affected by bug 41701, FarmingDelay of " + globalConfig.FarmingDelay + " is not possible - value of 5 will be used instead");
				globalConfig.FarmingDelay = 5;
			}

			if (globalConfig.HttpTimeout == 0) {
				Logging.LogGenericWarning("Configured HttpTimeout is invalid: " + globalConfig.HttpTimeout);
				return null;
			}

			if (globalConfig.WCFPort != 0) {
				return globalConfig;
			}

			Logging.LogGenericWarning("Configured WCFPort is invalid: " + globalConfig.WCFPort);
			return null;
		}

		// This constructor is used only by deserializer
		private GlobalConfig() { }
	}
}
