//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// 
//  Copyright 2015-2018 Łukasz "JustArchi" Domeradzki
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
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Localization;
using Newtonsoft.Json;
using SteamKit2;

namespace ArchiSteamFarm {
	[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
	internal sealed class GlobalConfig {
		internal const byte DefaultConnectionTimeout = 60;
		internal const ushort DefaultIPCPort = 1242;
		internal const byte DefaultLoginLimiterDelay = 10;

		internal static readonly HashSet<uint> SalesBlacklist = new HashSet<uint> { 267420, 303700, 335590, 368020, 425280, 480730, 566020, 639900, 762800, 876740 }; // Steam Summer/Winter sales

		private static readonly SemaphoreSlim WriteSemaphore = new SemaphoreSlim(1, 1);

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool AutoRestart = true;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly byte BackgroundGCPeriod;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly HashSet<uint> Blacklist = new HashSet<uint>();

		[JsonProperty]
		internal readonly string CommandPrefix = "!";

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly byte ConfirmationsLimiterDelay = 10;

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

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool IPC;

		[JsonProperty]
		internal readonly string IPCPassword;

		[JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace, Required = Required.DisallowNull)]
		internal readonly HashSet<string> IPCPrefixes = new HashSet<string> { "http://127.0.0.1:" + DefaultIPCPort + "/" };

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

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly ushort WebLimiterDelay = 200;

		[JsonProperty(Required = Required.DisallowNull)]
		internal ulong SteamOwnerID { get; private set; }

		[JsonProperty(Required = Required.DisallowNull)]
		internal ProtocolTypes SteamProtocols { get; private set; } = ProtocolTypes.All;

		internal WebProxy WebProxy { get; private set; }

		[JsonProperty]
		internal string WebProxyPassword {
			get => WebProxyCredentials?.Password;
			set {
				if (string.IsNullOrEmpty(value)) {
					if (WebProxyCredentials == null) {
						return;
					}

					WebProxyCredentials.Password = null;

					if (!string.IsNullOrEmpty(WebProxyCredentials.UserName)) {
						return;
					}

					WebProxyCredentials = null;
					if (WebProxy != null) {
						WebProxy.Credentials = null;
					}

					return;
				}

				if (WebProxyCredentials == null) {
					WebProxyCredentials = new NetworkCredential();
				}

				WebProxyCredentials.Password = value;

				if ((WebProxy != null) && (WebProxy.Credentials != WebProxyCredentials)) {
					WebProxy.Credentials = WebProxyCredentials;
				}
			}
		}

		[JsonProperty]
		internal string WebProxyUsername {
			get => WebProxyCredentials?.UserName;
			set {
				if (string.IsNullOrEmpty(value)) {
					if (WebProxyCredentials == null) {
						return;
					}

					WebProxyCredentials.UserName = null;

					if (!string.IsNullOrEmpty(WebProxyCredentials.Password)) {
						return;
					}

					WebProxyCredentials = null;
					if (WebProxy != null) {
						WebProxy.Credentials = null;
					}

					return;
				}

				if (WebProxyCredentials == null) {
					WebProxyCredentials = new NetworkCredential();
				}

				WebProxyCredentials.UserName = value;

				if ((WebProxy != null) && (WebProxy.Credentials != WebProxyCredentials)) {
					WebProxy.Credentials = WebProxyCredentials;
				}
			}
		}

		private bool ShouldSerializeSensitiveDetails = true;
		private NetworkCredential WebProxyCredentials;

		[JsonProperty(PropertyName = SharedInfo.UlongCompatibilityStringPrefix + nameof(SteamOwnerID), Required = Required.DisallowNull)]
		private string SSteamOwnerID {
			get => SteamOwnerID.ToString();
			set {
				if (string.IsNullOrEmpty(value) || !ulong.TryParse(value, out ulong result)) {
					ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorIsInvalid, nameof(SSteamOwnerID)));
					return;
				}

				SteamOwnerID = result;
			}
		}

		[JsonProperty(PropertyName = nameof(WebProxy))]
		private string WebProxyText {
			get => WebProxy?.Address.OriginalString;
			set {
				if (string.IsNullOrEmpty(value)) {
					if (WebProxy != null) {
						WebProxy = null;
					}

					return;
				}

				Uri uri;

				try {
					uri = new Uri(value);
				} catch (UriFormatException e) {
					ASF.ArchiLogger.LogGenericException(e);
					return;
				}

				if (WebProxy == null) {
					WebProxy = new WebProxy { BypassProxyOnLocal = true };
				}

				WebProxy.Address = uri;

				if ((WebProxyCredentials != null) && (WebProxy.Credentials != WebProxyCredentials)) {
					WebProxy.Credentials = WebProxyCredentials;
				}
			}
		}

		public bool ShouldSerializeWebProxyPassword() => ShouldSerializeSensitiveDetails;

		internal static async Task<GlobalConfig> Load(string filePath) {
			if (string.IsNullOrEmpty(filePath)) {
				ASF.ArchiLogger.LogNullError(nameof(filePath));
				return null;
			}

			if (!File.Exists(filePath)) {
				return null;
			}

			GlobalConfig globalConfig;

			try {
				globalConfig = JsonConvert.DeserializeObject<GlobalConfig>(await RuntimeCompatibility.File.ReadAllTextAsync(filePath).ConfigureAwait(false));
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);
				return null;
			}

			if (globalConfig == null) {
				ASF.ArchiLogger.LogNullError(nameof(globalConfig));
				return null;
			}

			globalConfig.ShouldSerializeSensitiveDetails = false;

			// User might not know what he's doing
			// Ensure that he can't screw core ASF variables
			if (globalConfig.ConnectionTimeout == 0) {
				ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorConfigPropertyInvalid, nameof(globalConfig.ConnectionTimeout), globalConfig.ConnectionTimeout));
				return null;
			}

			if (globalConfig.FarmingDelay == 0) {
				ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorConfigPropertyInvalid, nameof(globalConfig.FarmingDelay), globalConfig.FarmingDelay));
				return null;
			}

			if (globalConfig.MaxFarmingTime == 0) {
				ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorConfigPropertyInvalid, nameof(globalConfig.MaxFarmingTime), globalConfig.MaxFarmingTime));
				return null;
			}

			if ((globalConfig.SteamProtocols <= 0) || (globalConfig.SteamProtocols > ProtocolTypes.All)) {
				ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorConfigPropertyInvalid, nameof(globalConfig.SteamProtocols), globalConfig.SteamProtocols));
				return null;
			}

			return globalConfig;
		}

		internal static async Task<bool> Write(string filePath, GlobalConfig globalConfig) {
			if (string.IsNullOrEmpty(filePath) || (globalConfig == null)) {
				ASF.ArchiLogger.LogNullError(nameof(filePath) + " || " + nameof(globalConfig));
				return false;
			}

			string json = JsonConvert.SerializeObject(globalConfig, Formatting.Indented);
			string newFilePath = filePath + ".new";

			await WriteSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				await RuntimeCompatibility.File.WriteAllTextAsync(newFilePath, json).ConfigureAwait(false);

				if (File.Exists(filePath)) {
					File.Replace(newFilePath, filePath, null);
				} else {
					File.Move(newFilePath, filePath);
				}
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);
				return false;
			} finally {
				WriteSemaphore.Release();
			}

			return true;
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