/*
    _                _      _  ____   _                           _____
   / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
  / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
 / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
/_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|

 Copyright 2015-2017 Łukasz "JustArchi" Domeradzki
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
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Sockets;
using ArchiSteamFarm.Localization;
using Newtonsoft.Json;

namespace ArchiSteamFarm {
	[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
	[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
	[SuppressMessage("ReSharper", "ConvertToConstant.Global")]
	internal sealed class GlobalConfig {
		internal const byte DefaultConnectionTimeout = 60;
		internal const ushort DefaultWCFPort = 1242;

		// This is hardcoded blacklist which should not be possible to change
		internal static readonly HashSet<uint> GlobalBlacklist = new HashSet<uint> { 267420, 303700, 335590, 368020, 425280, 480730, 566020 };

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool AutoRestart = true;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool AutoUpdates = true;

		[SuppressMessage("ReSharper", "CollectionNeverUpdated.Global")]
		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly HashSet<uint> Blacklist = new HashSet<uint>();

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly byte ConnectionTimeout = DefaultConnectionTimeout;

		[JsonProperty]
		internal readonly string CurrentCulture = null;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool Debug = false;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly byte FarmingDelay = 15;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly byte GiftsLimiterDelay = 1;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool Headless = false;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly byte IdleFarmingPeriod = 3;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly byte InventoryLimiterDelay = 3;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly byte LoginLimiterDelay = 10;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly byte MaxFarmingTime = 10;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly byte MaxTradeHoldDuration = 15;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly EOptimizationMode OptimizationMode = EOptimizationMode.MaxPerformance;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool Statistics = true;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly ulong SteamOwnerID = 0;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly ProtocolType SteamProtocol = ProtocolType.Tcp;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly EUpdateChannel UpdateChannel = EUpdateChannel.Stable;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly EWCFBinding WCFBinding = EWCFBinding.NetTcp;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly ushort WCFPort = DefaultWCFPort;

		[JsonProperty]
		internal string WCFHost { get; set; } = "127.0.0.1";

		// This constructor is used only by deserializer
		private GlobalConfig() { }

		internal static GlobalConfig Load(string filePath) {
			if (string.IsNullOrEmpty(filePath)) {
				ASF.ArchiLogger.LogNullError(nameof(filePath));
				return null;
			}

			if (!File.Exists(filePath)) {
				return null;
			}

			GlobalConfig globalConfig;

			try {
				globalConfig = JsonConvert.DeserializeObject<GlobalConfig>(File.ReadAllText(filePath));
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);
				return null;
			}

			if (globalConfig == null) {
				ASF.ArchiLogger.LogNullError(nameof(globalConfig));
				return null;
			}

			// SK2 supports only TCP and UDP steam protocols
			// Ensure that user can't screw this up
			switch (globalConfig.SteamProtocol) {
				case ProtocolType.Tcp:
				case ProtocolType.Udp:
					break;
				default:
					ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorConfigPropertyInvalid, nameof(globalConfig.SteamProtocol), globalConfig.SteamProtocol));
					return null;
			}

			// User might not know what he's doing
			// Ensure that he can't screw core ASF variables
			if (globalConfig.MaxFarmingTime == 0) {
				ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorConfigPropertyInvalid, nameof(globalConfig.MaxFarmingTime), globalConfig.MaxFarmingTime));
				return null;
			}

			if (globalConfig.FarmingDelay == 0) {
				ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorConfigPropertyInvalid, nameof(globalConfig.FarmingDelay), globalConfig.FarmingDelay));
				return null;
			}

			if (globalConfig.ConnectionTimeout == 0) {
				ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorConfigPropertyInvalid, nameof(globalConfig.ConnectionTimeout), globalConfig.ConnectionTimeout));
				return null;
			}

			if (globalConfig.WCFPort != 0) {
				return globalConfig;
			}

			ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorConfigPropertyInvalid, nameof(globalConfig.WCFPort), globalConfig.WCFPort));
			return null;
		}

		internal enum EOptimizationMode : byte {
			MaxPerformance,
			MinMemoryUsage
		}

		[SuppressMessage("ReSharper", "UnusedMember.Global")]
		internal enum EUpdateChannel : byte {
			None,
			Stable,
			Experimental
		}

		internal enum EWCFBinding : byte {
			NetTcp,
			BasicHttp,
			WSHttp
		}
	}
}