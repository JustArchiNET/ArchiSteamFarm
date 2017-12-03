//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// 
//  Copyright 2015-2017 Łukasz "JustArchi" Domeradzki
//  Contact: JustArchi@JustArchi.net
// 
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
// 
//  http://www.apache.org/licenses/LICENSE-2.0
//      
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using ArchiSteamFarm.Localization;
using Newtonsoft.Json;
using SteamKit2;

namespace ArchiSteamFarm {
	[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
	internal sealed class GlobalConfig {
		internal const byte DefaultConnectionTimeout = 60;
		internal const ushort DefaultIPCPort = 1242;
		internal const byte DefaultLoginLimiterDelay = 10;
		internal const string UlongStringPrefix = "s_";

		internal static readonly HashSet<uint> GamesBlacklist = new HashSet<uint> { 402590 }; // Games with broken/unobtainable card drops
		internal static readonly HashSet<uint> SalesBlacklist = new HashSet<uint> { 267420, 303700, 335590, 368020, 425280, 480730, 566020, 639900 }; // Steam Summer/Winter sales

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool AutoRestart = true;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly byte BackgroundGCPeriod;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly HashSet<uint> Blacklist = new HashSet<uint>();

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly byte ConnectionTimeout = DefaultConnectionTimeout;

		[JsonProperty]
		internal readonly string CurrentCulture;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool Debug;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly byte FarmingDelay = 15;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly byte GiftsLimiterDelay = 1;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool Headless;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly byte IdleFarmingPeriod = 8;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly byte InventoryLimiterDelay = 3;

		[JsonProperty]
		internal readonly string IPCPassword;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly ushort IPCPort = DefaultIPCPort;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly byte LoginLimiterDelay = DefaultLoginLimiterDelay;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly byte MaxFarmingTime = 10;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly byte MaxTradeHoldDuration = 15;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly EOptimizationMode OptimizationMode = EOptimizationMode.MaxPerformance;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool Statistics = true;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly EUpdateChannel UpdateChannel = EUpdateChannel.Stable;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly byte UpdatePeriod = 24;

		[JsonProperty]
		internal string IPCHost { get; set; } = "127.0.0.1";

		[JsonProperty(PropertyName = UlongStringPrefix + nameof(SteamOwnerID), Required = Required.DisallowNull)]
		internal string SSteamOwnerID {
			set {
				if (string.IsNullOrEmpty(value) || !ulong.TryParse(value, out ulong result)) {
					ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorIsInvalid, nameof(SSteamOwnerID)));
					return;
				}

				SteamOwnerID = result;
			}
		}

		[JsonProperty(Required = Required.DisallowNull)]
		internal ulong SteamOwnerID { get; private set; }

		[JsonProperty(Required = Required.DisallowNull)]
		internal ProtocolTypes SteamProtocols { get; private set; } = ProtocolTypes.Tcp | ProtocolTypes.Udp;

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

			if (globalConfig.IPCPort == 0) {
				ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorConfigPropertyInvalid, nameof(globalConfig.IPCPort), globalConfig.IPCPort));
				return null;
			}

			if (globalConfig.SteamProtocols.HasFlag(ProtocolTypes.WebSocket) && !OS.SupportsWebSockets()) {
				globalConfig.SteamProtocols &= ~ProtocolTypes.WebSocket;
				if (globalConfig.SteamProtocols == 0) {
					globalConfig.SteamProtocols = ProtocolTypes.Tcp;
				}
			}

			GlobalConfig result = globalConfig;
			return result;
		}

		internal enum EOptimizationMode : byte {
			MaxPerformance,
			MinMemoryUsage
		}

		internal enum EUpdateChannel : byte {
			None,
			Stable,

			[SuppressMessage("ReSharper", "UnusedMember.Global")]
			Experimental
		}
	}
}