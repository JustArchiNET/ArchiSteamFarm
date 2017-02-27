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
using ConfigGenerator.Localization;
using Newtonsoft.Json;

namespace ConfigGenerator {
	[SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
	[SuppressMessage("ReSharper", "CollectionNeverQueried.Global")]
	[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
	[SuppressMessage("ReSharper", "UnusedMember.Global")]
	internal sealed class GlobalConfig : ASFConfig {
		private const byte DefaultConnectionTimeout = 60;
		private const byte DefaultFarmingDelay = 15;
		private const byte DefaultMaxFarmingTime = 10;
		private const ProtocolType DefaultSteamProtocol = ProtocolType.Tcp;
		private const ushort DefaultWCFPort = 1242;

		[LocalizedCategory("Updates")]
		[JsonProperty(Required = Required.DisallowNull)]
		public bool AutoRestart { get; set; } = true;

		[LocalizedCategory("Updates")]
		[JsonProperty(Required = Required.DisallowNull)]
		public bool AutoUpdates { get; set; } = true;

		[JsonProperty(Required = Required.DisallowNull)]
		public List<uint> Blacklist { get; set; } = new List<uint>();

		[LocalizedCategory("Debugging")]
		[JsonProperty(Required = Required.DisallowNull)]
		public byte ConnectionTimeout { get; set; } = DefaultConnectionTimeout;

		[JsonProperty]
		public string CurrentCulture { get; set; } = null;

		[LocalizedCategory("Debugging")]
		[JsonProperty(Required = Required.DisallowNull)]
		public bool Debug { get; set; } = false;

		[LocalizedCategory("Performance")]
		[JsonProperty(Required = Required.DisallowNull)]
		public byte FarmingDelay { get; set; } = DefaultFarmingDelay;

		[LocalizedCategory("Performance")]
		[JsonProperty(Required = Required.DisallowNull)]
		public byte GiftsLimiterDelay { get; set; } = 1;

		[LocalizedCategory("Advanced")]
		[JsonProperty(Required = Required.DisallowNull)]
		public bool Headless { get; set; } = false;

		[LocalizedCategory("Performance")]
		[JsonProperty(Required = Required.DisallowNull)]
		public byte IdleFarmingPeriod { get; set; } = 3;

		[LocalizedCategory("Performance")]
		[JsonProperty(Required = Required.DisallowNull)]
		public byte InventoryLimiterDelay { get; set; } = 3;

		[LocalizedCategory("Performance")]
		[JsonProperty(Required = Required.DisallowNull)]
		public byte LoginLimiterDelay { get; set; } = 10;

		[LocalizedCategory("Performance")]
		[JsonProperty(Required = Required.DisallowNull)]
		public byte MaxFarmingTime { get; set; } = DefaultMaxFarmingTime;

		[JsonProperty(Required = Required.DisallowNull)]
		public byte MaxTradeHoldDuration { get; set; } = 15;

		[LocalizedCategory("Performance")]
		[JsonProperty(Required = Required.DisallowNull)]
		public EOptimizationMode OptimizationMode { get; set; } = EOptimizationMode.MaxPerformance;

		[JsonProperty(Required = Required.DisallowNull)]
		public bool Statistics { get; set; } = true;

		[LocalizedCategory("Access")]
		[JsonProperty(Required = Required.DisallowNull)]
		public ulong SteamOwnerID { get; set; } = 0;

		[LocalizedCategory("Advanced")]
		[JsonProperty(Required = Required.DisallowNull)]
		public ProtocolType SteamProtocol { get; set; } = DefaultSteamProtocol;

		[LocalizedCategory("Updates")]
		[JsonProperty(Required = Required.DisallowNull)]
		public EUpdateChannel UpdateChannel { get; set; } = EUpdateChannel.Stable;

		[LocalizedCategory("Access")]
		[JsonProperty(Required = Required.DisallowNull)]
		public EWCFBinding WCFBinding { get; set; } = EWCFBinding.NetTcp;

		[LocalizedCategory("Access")]
		[JsonProperty]
		public string WCFHost { get; set; } = "127.0.0.1";

		[LocalizedCategory("Access")]
		[JsonProperty(Required = Required.DisallowNull)]
		public ushort WCFPort { get; set; } = DefaultWCFPort;

		[SuppressMessage("ReSharper", "UnusedMember.Local")]
		private GlobalConfig() { }

		private GlobalConfig(string filePath) : base(filePath) {
			if (string.IsNullOrEmpty(filePath)) {
				throw new ArgumentNullException(nameof(filePath));
			}

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
			globalConfig.ValidateAndFix();
			return globalConfig;
		}

		internal override void ValidateAndFix() {
			base.ValidateAndFix();

			if (ConnectionTimeout == 0) {
				Logging.LogGenericWarning(string.Format(CGStrings.ErrorConfigPropertyInvalid, nameof(ConnectionTimeout), ConnectionTimeout));
				ConnectionTimeout = DefaultConnectionTimeout;
				Save();
				Logging.LogGenericWarning(string.Format(CGStrings.WarningConfigPropertyModified, nameof(ConnectionTimeout), ConnectionTimeout));
			}

			if (FarmingDelay == 0) {
				Logging.LogGenericWarning(string.Format(CGStrings.ErrorConfigPropertyInvalid, nameof(FarmingDelay), FarmingDelay));
				FarmingDelay = DefaultFarmingDelay;
				Save();
				Logging.LogGenericWarning(string.Format(CGStrings.WarningConfigPropertyModified, nameof(FarmingDelay), FarmingDelay));
			}

			if (MaxFarmingTime == 0) {
				Logging.LogGenericWarning(string.Format(CGStrings.ErrorConfigPropertyInvalid, nameof(MaxFarmingTime), MaxFarmingTime));
				MaxFarmingTime = DefaultMaxFarmingTime;
				Save();
				Logging.LogGenericWarning(string.Format(CGStrings.WarningConfigPropertyModified, nameof(MaxFarmingTime), MaxFarmingTime));
			}

			switch (SteamProtocol) {
				case ProtocolType.Tcp:
				case ProtocolType.Udp:
					break;
				default:
					Logging.LogGenericWarning(string.Format(CGStrings.ErrorConfigPropertyInvalid, nameof(SteamProtocol), SteamProtocol));
					SteamProtocol = DefaultSteamProtocol;
					Save();
					Logging.LogGenericWarning(string.Format(CGStrings.WarningConfigPropertyModified, nameof(SteamProtocol), SteamProtocol));
					break;
			}

			if (WCFPort != 0) {
				return;
			}

			Logging.LogGenericWarning(string.Format(CGStrings.ErrorConfigPropertyInvalid, nameof(WCFPort), WCFPort));
			WCFPort = DefaultWCFPort;
			Save();
			Logging.LogGenericWarning(string.Format(CGStrings.WarningConfigPropertyModified, nameof(WCFPort), WCFPort));
		}

		internal enum EOptimizationMode : byte {
			MaxPerformance,
			MinMemoryUsage
		}

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