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
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Localization;
using JetBrains.Annotations;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SteamKit2;

namespace ArchiSteamFarm {
	[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
	public sealed class GlobalConfig {
		[PublicAPI]
		public const bool DefaultAutoRestart = true;

		[PublicAPI]
		public const string? DefaultCommandPrefix = "!";

		[PublicAPI]
		public const byte DefaultConfirmationsLimiterDelay = 10;

		[PublicAPI]
		public const byte DefaultConnectionTimeout = 90;

		[PublicAPI]
		public const string? DefaultCurrentCulture = null;

		[PublicAPI]
		public const bool DefaultDebug = false;

		[PublicAPI]
		public const byte DefaultFarmingDelay = 15;

		[PublicAPI]
		public const byte DefaultGiftsLimiterDelay = 1;

		[PublicAPI]
		public const bool DefaultHeadless = false;

		[PublicAPI]
		public const byte DefaultIdleFarmingPeriod = 8;

		[PublicAPI]
		public const byte DefaultInventoryLimiterDelay = 3;

		[PublicAPI]
		public const bool DefaultIPC = false;

		[PublicAPI]
		public const string? DefaultIPCPassword = null;

		[PublicAPI]
		public const ArchiCryptoHelper.EHashingMethod DefaultIPCPasswordFormat = ArchiCryptoHelper.EHashingMethod.PlainText;

		[PublicAPI]
		public const byte DefaultLoginLimiterDelay = 10;

		[PublicAPI]
		public const byte DefaultMaxFarmingTime = 10;

		[PublicAPI]
		public const byte DefaultMaxTradeHoldDuration = 15;

		[PublicAPI]
		public const EOptimizationMode DefaultOptimizationMode = EOptimizationMode.MaxPerformance;

		[PublicAPI]
		public const bool DefaultStatistics = true;

		[PublicAPI]
		public const string? DefaultSteamMessagePrefix = "/me ";

		[PublicAPI]
		public const ulong DefaultSteamOwnerID = 0;

		[PublicAPI]
		public const ProtocolTypes DefaultSteamProtocols = ProtocolTypes.All;

		[PublicAPI]
		public const EUpdateChannel DefaultUpdateChannel = EUpdateChannel.Stable;

		[PublicAPI]
		public const byte DefaultUpdatePeriod = 24;

		[PublicAPI]
		public const ushort DefaultWebLimiterDelay = 300;

		[PublicAPI]
		public const string? DefaultWebProxyPassword = null;

		[PublicAPI]
		public const string? DefaultWebProxyText = null;

		[PublicAPI]
		public const string? DefaultWebProxyUsername = null;

		[PublicAPI]
		public static readonly ImmutableHashSet<uint> DefaultBlacklist = ImmutableHashSet<uint>.Empty;

		private static readonly SemaphoreSlim WriteSemaphore = new(1, 1);

		[JsonIgnore]
		[PublicAPI]
		public WebProxy? WebProxy {
			get {
				if (BackingWebProxy != null) {
					return BackingWebProxy;
				}

				if (string.IsNullOrEmpty(WebProxyText)) {
					return null;
				}

				Uri uri;

				try {
					uri = new Uri(WebProxyText!);
				} catch (UriFormatException e) {
					ASF.ArchiLogger.LogGenericException(e);

					return null;
				}

				WebProxy proxy = new() {
					Address = uri,
					BypassProxyOnLocal = true
				};

				if (!string.IsNullOrEmpty(WebProxyUsername) || !string.IsNullOrEmpty(WebProxyPassword)) {
					NetworkCredential credentials = new();

					if (!string.IsNullOrEmpty(WebProxyUsername)) {
						credentials.UserName = WebProxyUsername;
					}

					if (!string.IsNullOrEmpty(WebProxyPassword)) {
						credentials.Password = WebProxyPassword;
					}

					proxy.Credentials = credentials;
				}

				BackingWebProxy = proxy;

				return proxy;
			}
		}

		[JsonProperty]
		internal readonly string? IPCPassword = DefaultIPCPassword;

		[JsonProperty(Required = Required.DisallowNull)]
		public bool AutoRestart { get; private set; } = DefaultAutoRestart;

		[JsonProperty(Required = Required.DisallowNull)]
		public ImmutableHashSet<uint> Blacklist { get; private set; } = DefaultBlacklist;

		[JsonProperty]
		public string? CommandPrefix { get; private set; } = DefaultCommandPrefix;

		[JsonProperty(Required = Required.DisallowNull)]
		public byte ConfirmationsLimiterDelay { get; private set; } = DefaultConfirmationsLimiterDelay;

		[JsonProperty(Required = Required.DisallowNull)]
		public byte ConnectionTimeout { get; private set; } = DefaultConnectionTimeout;

		[JsonProperty]
		public string? CurrentCulture { get; private set; } = DefaultCurrentCulture;

		[JsonProperty(Required = Required.DisallowNull)]
		public bool Debug { get; private set; } = DefaultDebug;

		[JsonProperty(Required = Required.DisallowNull)]
		public byte FarmingDelay { get; private set; } = DefaultFarmingDelay;

		[JsonProperty(Required = Required.DisallowNull)]
		public byte GiftsLimiterDelay { get; private set; } = DefaultGiftsLimiterDelay;

		[JsonProperty(Required = Required.DisallowNull)]
		public bool Headless { get; private set; } = DefaultHeadless;

		[JsonProperty(Required = Required.DisallowNull)]
		public byte IdleFarmingPeriod { get; private set; } = DefaultIdleFarmingPeriod;

		[JsonProperty(Required = Required.DisallowNull)]
		public byte InventoryLimiterDelay { get; private set; } = DefaultInventoryLimiterDelay;

		[JsonProperty(Required = Required.DisallowNull)]
		public bool IPC { get; private set; } = DefaultIPC;

		[JsonProperty(Required = Required.DisallowNull)]
		public ArchiCryptoHelper.EHashingMethod IPCPasswordFormat { get; private set; } = DefaultIPCPasswordFormat;

		[JsonProperty(Required = Required.DisallowNull)]
		public byte LoginLimiterDelay { get; private set; } = DefaultLoginLimiterDelay;

		[JsonProperty(Required = Required.DisallowNull)]
		public byte MaxFarmingTime { get; private set; } = DefaultMaxFarmingTime;

		[JsonProperty(Required = Required.DisallowNull)]
		public byte MaxTradeHoldDuration { get; private set; } = DefaultMaxTradeHoldDuration;

		[JsonProperty(Required = Required.DisallowNull)]
		public EOptimizationMode OptimizationMode { get; private set; } = DefaultOptimizationMode;

		[JsonProperty(Required = Required.DisallowNull)]
		public bool Statistics { get; private set; } = DefaultStatistics;

		[JsonProperty]
		public string? SteamMessagePrefix { get; private set; } = DefaultSteamMessagePrefix;

		[JsonProperty(Required = Required.DisallowNull)]
		public ulong SteamOwnerID { get; private set; } = DefaultSteamOwnerID;

		[JsonProperty(Required = Required.DisallowNull)]
		public ProtocolTypes SteamProtocols { get; private set; } = DefaultSteamProtocols;

		[JsonProperty(Required = Required.DisallowNull)]
		public EUpdateChannel UpdateChannel { get; private set; } = DefaultUpdateChannel;

		[JsonProperty(Required = Required.DisallowNull)]
		public byte UpdatePeriod { get; private set; } = DefaultUpdatePeriod;

		[JsonProperty(Required = Required.DisallowNull)]
		public ushort WebLimiterDelay { get; private set; } = DefaultWebLimiterDelay;

		[JsonProperty(PropertyName = nameof(WebProxy))]
		public string? WebProxyText { get; private set; } = DefaultWebProxyText;

		[JsonProperty]
		public string? WebProxyUsername { get; private set; } = DefaultWebProxyUsername;

		[JsonExtensionData]
		internal Dictionary<string, JToken>? AdditionalProperties {
			get;
			[UsedImplicitly]
			set;
		}

		internal bool IsWebProxyPasswordSet { get; private set; }
		internal bool ShouldSerializeDefaultValues { private get; set; } = true;
		internal bool ShouldSerializeHelperProperties { private get; set; } = true;
		internal bool ShouldSerializeSensitiveDetails { private get; set; }

		[JsonProperty]
		internal string? WebProxyPassword {
			get => BackingWebProxyPassword;

			set {
				IsWebProxyPasswordSet = true;
				BackingWebProxyPassword = value;
			}
		}

		private WebProxy? BackingWebProxy;
		private string? BackingWebProxyPassword = DefaultWebProxyPassword;

		[JsonProperty(PropertyName = SharedInfo.UlongCompatibilityStringPrefix + nameof(SteamOwnerID), Required = Required.DisallowNull)]
		private string SSteamOwnerID {
			get => SteamOwnerID.ToString(CultureInfo.InvariantCulture);

			set {
				if (string.IsNullOrEmpty(value) || !ulong.TryParse(value, out ulong result)) {
					ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, nameof(SSteamOwnerID)));

					return;
				}

				SteamOwnerID = result;
			}
		}

		[JsonConstructor]
		internal GlobalConfig() { }

		internal (bool Valid, string? ErrorMessage) CheckValidation() {
			if (ConnectionTimeout == 0) {
				return (false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorConfigPropertyInvalid, nameof(ConnectionTimeout), ConnectionTimeout));
			}

			if (FarmingDelay == 0) {
				return (false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorConfigPropertyInvalid, nameof(FarmingDelay), FarmingDelay));
			}

			if (!Enum.IsDefined(typeof(ArchiCryptoHelper.EHashingMethod), IPCPasswordFormat)) {
				return (false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorConfigPropertyInvalid, nameof(IPCPasswordFormat), IPCPasswordFormat));
			}

			if (MaxFarmingTime == 0) {
				return (false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorConfigPropertyInvalid, nameof(MaxFarmingTime), MaxFarmingTime));
			}

			if (!Enum.IsDefined(typeof(EOptimizationMode), OptimizationMode)) {
				return (false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorConfigPropertyInvalid, nameof(OptimizationMode), OptimizationMode));
			}

			if (!string.IsNullOrEmpty(SteamMessagePrefix) && (SteamMessagePrefix!.Length > Bot.MaxMessagePrefixLength)) {
				return (false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorConfigPropertyInvalid, nameof(SteamMessagePrefix), SteamMessagePrefix));
			}

			if ((SteamOwnerID != 0) && !new SteamID(SteamOwnerID).IsIndividualAccount) {
				return (false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorConfigPropertyInvalid, nameof(SteamOwnerID), SteamOwnerID));
			}

			if (SteamProtocols is <= 0 or > ProtocolTypes.All) {
				return (false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorConfigPropertyInvalid, nameof(SteamProtocols), SteamProtocols));
			}

			return Enum.IsDefined(typeof(EUpdateChannel), UpdateChannel) ? (true, null) : (false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorConfigPropertyInvalid, nameof(UpdateChannel), UpdateChannel));
		}

		internal static async Task<GlobalConfig?> Load(string filePath) {
			if (string.IsNullOrEmpty(filePath)) {
				throw new ArgumentNullException(nameof(filePath));
			}

			if (!File.Exists(filePath)) {
				return null;
			}

			GlobalConfig? globalConfig;

			try {
				string json = await RuntimeCompatibility.File.ReadAllTextAsync(filePath).ConfigureAwait(false);

				if (string.IsNullOrEmpty(json)) {
					ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(json)));

					return null;
				}

				globalConfig = JsonConvert.DeserializeObject<GlobalConfig>(json);
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);

				return null;
			}

			if (globalConfig == null) {
				ASF.ArchiLogger.LogNullError(nameof(globalConfig));

				return null;
			}

			(bool valid, string? errorMessage) = globalConfig.CheckValidation();

			if (!valid) {
				if (!string.IsNullOrEmpty(errorMessage)) {
					ASF.ArchiLogger.LogGenericError(errorMessage!);
				}

				return null;
			}

			return globalConfig;
		}

		internal static async Task<bool> Write(string filePath, GlobalConfig globalConfig) {
			if (string.IsNullOrEmpty(filePath)) {
				throw new ArgumentNullException(nameof(filePath));
			}

			if (globalConfig == null) {
				throw new ArgumentNullException(nameof(globalConfig));
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

		public enum EOptimizationMode : byte {
			MaxPerformance,
			MinMemoryUsage
		}

		public enum EUpdateChannel : byte {
			None,
			Stable,

			[PublicAPI]
			Experimental
		}

		// ReSharper disable UnusedMember.Global
		public bool ShouldSerializeAutoRestart() => ShouldSerializeDefaultValues || (AutoRestart != DefaultAutoRestart);
		public bool ShouldSerializeBlacklist() => ShouldSerializeDefaultValues || ((Blacklist != DefaultBlacklist) && !Blacklist.SetEquals(DefaultBlacklist));
		public bool ShouldSerializeCommandPrefix() => ShouldSerializeDefaultValues || (CommandPrefix != DefaultCommandPrefix);
		public bool ShouldSerializeConfirmationsLimiterDelay() => ShouldSerializeDefaultValues || (ConfirmationsLimiterDelay != DefaultConfirmationsLimiterDelay);
		public bool ShouldSerializeConnectionTimeout() => ShouldSerializeDefaultValues || (ConnectionTimeout != DefaultConnectionTimeout);
		public bool ShouldSerializeCurrentCulture() => ShouldSerializeDefaultValues || (CurrentCulture != DefaultCurrentCulture);
		public bool ShouldSerializeDebug() => ShouldSerializeDefaultValues || (Debug != DefaultDebug);
		public bool ShouldSerializeFarmingDelay() => ShouldSerializeDefaultValues || (FarmingDelay != DefaultFarmingDelay);
		public bool ShouldSerializeGiftsLimiterDelay() => ShouldSerializeDefaultValues || (GiftsLimiterDelay != DefaultGiftsLimiterDelay);
		public bool ShouldSerializeHeadless() => ShouldSerializeDefaultValues || (Headless != DefaultHeadless);
		public bool ShouldSerializeIdleFarmingPeriod() => ShouldSerializeDefaultValues || (IdleFarmingPeriod != DefaultIdleFarmingPeriod);
		public bool ShouldSerializeInventoryLimiterDelay() => ShouldSerializeDefaultValues || (InventoryLimiterDelay != DefaultInventoryLimiterDelay);
		public bool ShouldSerializeIPC() => ShouldSerializeDefaultValues || (IPC != DefaultIPC);
		public bool ShouldSerializeIPCPassword() => ShouldSerializeSensitiveDetails && (ShouldSerializeDefaultValues || (IPCPassword != DefaultIPCPassword));
		public bool ShouldSerializeIPCPasswordFormat() => ShouldSerializeDefaultValues || (IPCPasswordFormat != DefaultIPCPasswordFormat);
		public bool ShouldSerializeLoginLimiterDelay() => ShouldSerializeDefaultValues || (LoginLimiterDelay != DefaultLoginLimiterDelay);
		public bool ShouldSerializeMaxFarmingTime() => ShouldSerializeDefaultValues || (MaxFarmingTime != DefaultMaxFarmingTime);
		public bool ShouldSerializeMaxTradeHoldDuration() => ShouldSerializeDefaultValues || (MaxTradeHoldDuration != DefaultMaxTradeHoldDuration);
		public bool ShouldSerializeOptimizationMode() => ShouldSerializeDefaultValues || (OptimizationMode != DefaultOptimizationMode);
		public bool ShouldSerializeSSteamOwnerID() => ShouldSerializeDefaultValues || (ShouldSerializeHelperProperties && (SteamOwnerID != DefaultSteamOwnerID));
		public bool ShouldSerializeStatistics() => ShouldSerializeDefaultValues || (Statistics != DefaultStatistics);
		public bool ShouldSerializeSteamMessagePrefix() => ShouldSerializeDefaultValues || (SteamMessagePrefix != DefaultSteamMessagePrefix);
		public bool ShouldSerializeSteamOwnerID() => ShouldSerializeDefaultValues || (SteamOwnerID != DefaultSteamOwnerID);
		public bool ShouldSerializeSteamProtocols() => ShouldSerializeDefaultValues || (SteamProtocols != DefaultSteamProtocols);
		public bool ShouldSerializeUpdateChannel() => ShouldSerializeDefaultValues || (UpdateChannel != DefaultUpdateChannel);
		public bool ShouldSerializeUpdatePeriod() => ShouldSerializeDefaultValues || (UpdatePeriod != DefaultUpdatePeriod);
		public bool ShouldSerializeWebLimiterDelay() => ShouldSerializeDefaultValues || (WebLimiterDelay != DefaultWebLimiterDelay);
		public bool ShouldSerializeWebProxyPassword() => ShouldSerializeSensitiveDetails && (ShouldSerializeDefaultValues || (WebProxyPassword != DefaultWebProxyPassword));
		public bool ShouldSerializeWebProxyText() => ShouldSerializeDefaultValues || (WebProxyText != DefaultWebProxyText);
		public bool ShouldSerializeWebProxyUsername() => ShouldSerializeDefaultValues || (WebProxyUsername != DefaultWebProxyUsername);

		// ReSharper restore UnusedMember.Global
	}
}
