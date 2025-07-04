// ----------------------------------------------------------------------------------------------
//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// ----------------------------------------------------------------------------------------------
// |
// Copyright 2015-2025 Łukasz "JustArchi" Domeradzki
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
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers;
using ArchiSteamFarm.Helpers.Json;
using ArchiSteamFarm.IPC.Integration;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Steam.Integration;
using JetBrains.Annotations;
using SteamKit2;

namespace ArchiSteamFarm.Storage;

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
	public const string? DefaultDefaultBot = null;

	[PublicAPI]
	public const byte DefaultFarmingDelay = 15;

	[PublicAPI]
	public const bool DefaultFilterBadBots = true;

	[PublicAPI]
	public const byte DefaultGiftsLimiterDelay = 1;

	[PublicAPI]
	public const bool DefaultHeadless = false;

	[PublicAPI]
	public const byte DefaultIdleFarmingPeriod = 8;

	[PublicAPI]
	public const byte DefaultInventoryLimiterDelay = 4;

	[PublicAPI]
	public const bool DefaultIPC = true;

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
	public const byte DefaultMinFarmingDelayAfterBlock = 60;

	[PublicAPI]
	public const EOptimizationMode DefaultOptimizationMode = EOptimizationMode.MaxPerformance;

	[PublicAPI]
	public const EPluginsUpdateMode DefaultPluginsUpdateMode = EPluginsUpdateMode.Whitelist;

	[PublicAPI]
	public const bool DefaultShutdownIfPossible = false;

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
	public static readonly ImmutableHashSet<uint> DefaultBlacklist = [];

	[PublicAPI]
	public static readonly Guid? DefaultLicenseID;

	[PublicAPI]
	public static readonly ImmutableHashSet<string> DefaultPluginsUpdateList = [];

	[JsonIgnore]
	[PublicAPI]
	public WebProxy? WebProxy {
		get {
			if (field != null) {
				return field;
			}

			if (string.IsNullOrEmpty(WebProxyText)) {
				return null;
			}

			Uri uri;

			try {
				uri = new Uri(WebProxyText);
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

			return field = proxy;
		}
	}

	[JsonInclude]
	public bool AutoRestart { get; init; } = DefaultAutoRestart;

	[JsonDisallowNull]
	[JsonInclude]
	[SwaggerItemsMinMax(MinimumUint = 1, MaximumUint = uint.MaxValue)]
	public ImmutableHashSet<uint> Blacklist { get; init; } = DefaultBlacklist;

	[JsonInclude]
	public string? CommandPrefix { get; init; } = DefaultCommandPrefix;

	[JsonInclude]
	[Range(byte.MinValue, byte.MaxValue)]
	public byte ConfirmationsLimiterDelay { get; init; } = DefaultConfirmationsLimiterDelay;

	[JsonInclude]
	[Range(1, byte.MaxValue)]
	public byte ConnectionTimeout { get; init; } = DefaultConnectionTimeout;

	[JsonInclude]
	public string? CurrentCulture { get; init; } = DefaultCurrentCulture;

	[JsonInclude]
	public bool Debug { get; init; } = DefaultDebug;

	[JsonInclude]
	public string? DefaultBot { get; init; } = DefaultDefaultBot;

	[JsonInclude]
	[Range(1, byte.MaxValue)]
	public byte FarmingDelay { get; init; } = DefaultFarmingDelay;

	[JsonInclude]
	public bool FilterBadBots { get; init; } = DefaultFilterBadBots;

	[JsonInclude]
	[Range(byte.MinValue, byte.MaxValue)]
	public byte GiftsLimiterDelay { get; init; } = DefaultGiftsLimiterDelay;

	[JsonInclude]
	public bool Headless { get; init; } = DefaultHeadless;

	[JsonInclude]
	[Range(byte.MinValue, byte.MaxValue)]
	public byte IdleFarmingPeriod { get; init; } = DefaultIdleFarmingPeriod;

	[JsonInclude]
	[Range(byte.MinValue, byte.MaxValue)]
	public byte InventoryLimiterDelay { get; init; } = DefaultInventoryLimiterDelay;

	[JsonInclude]
	public bool IPC { get; init; } = DefaultIPC;

	[JsonInclude]
	[SwaggerSecurityCritical]
	public string? IPCPassword {
		get;

		internal set {
			IsIPCPasswordSet = true;
			field = value;
		}
	} = DefaultIPCPassword;

	[JsonInclude]
	public ArchiCryptoHelper.EHashingMethod IPCPasswordFormat { get; init; } = DefaultIPCPasswordFormat;

	[JsonConverter(typeof(GuidJsonConverter))]
	[JsonInclude]
	[SwaggerSecurityCritical]
	public Guid? LicenseID {
		get;

		internal set {
			IsLicenseIDSet = true;
			field = value;
		}
	} = DefaultLicenseID;

	[JsonInclude]
	[Range(byte.MinValue, byte.MaxValue)]
	public byte LoginLimiterDelay { get; init; } = DefaultLoginLimiterDelay;

	[JsonInclude]
	[Range(1, byte.MaxValue)]
	public byte MaxFarmingTime { get; init; } = DefaultMaxFarmingTime;

	[JsonInclude]
	[Range(byte.MinValue, byte.MaxValue)]
	public byte MaxTradeHoldDuration { get; init; } = DefaultMaxTradeHoldDuration;

	[JsonInclude]
	[Range(byte.MinValue, byte.MaxValue)]
	public byte MinFarmingDelayAfterBlock { get; init; } = DefaultMinFarmingDelayAfterBlock;

	[JsonInclude]
	public EOptimizationMode OptimizationMode { get; init; } = DefaultOptimizationMode;

	[JsonDisallowNull]
	[JsonInclude]
	public ImmutableHashSet<string> PluginsUpdateList { get; init; } = DefaultPluginsUpdateList;

	[JsonInclude]
	public EPluginsUpdateMode PluginsUpdateMode { get; init; } = DefaultPluginsUpdateMode;

	[JsonInclude]
	public bool ShutdownIfPossible { get; init; } = DefaultShutdownIfPossible;

	[JsonInclude]
	[MaxLength(SteamChatMessage.MaxMessagePrefixBytes / SteamChatMessage.ReservedEscapeMessageBytes)]
	[UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL2026:RequiresUnreferencedCode", Justification = "This is optional, supportive attribute, we don't care if it gets trimmed or not")]
	public string? SteamMessagePrefix { get; init; } = DefaultSteamMessagePrefix;

	[JsonInclude]
	[SwaggerSteamIdentifier]
	[SwaggerValidValues(ValidIntValues = [0])]
	public ulong SteamOwnerID { get; init; } = DefaultSteamOwnerID;

	[JsonInclude]
	public ProtocolTypes SteamProtocols { get; init; } = DefaultSteamProtocols;

	[JsonInclude]
	public EUpdateChannel UpdateChannel { get; init; } = DefaultUpdateChannel;

	[JsonInclude]
	[Range(byte.MinValue, byte.MaxValue)]
	public byte UpdatePeriod { get; init; } = DefaultUpdatePeriod;

	[JsonInclude]
	[Range(ushort.MinValue, ushort.MaxValue)]
	public ushort WebLimiterDelay { get; init; } = DefaultWebLimiterDelay;

	[JsonInclude]
	[JsonPropertyName(nameof(WebProxy))]
	public string? WebProxyText { get; init; } = DefaultWebProxyText;

	[JsonInclude]
	public string? WebProxyUsername { get; init; } = DefaultWebProxyUsername;

	[JsonExtensionData]
	[JsonInclude]
	internal Dictionary<string, JsonElement>? AdditionalProperties { get; set; }

	internal bool IsIPCPasswordSet { get; private set; }
	internal bool IsLicenseIDSet { get; private set; }
	internal bool IsWebProxyPasswordSet { get; private set; }

	internal bool Saving { get; set; }

	[JsonInclude]
	[SwaggerSecurityCritical]
	internal string? WebProxyPassword {
		get;

		set {
			IsWebProxyPasswordSet = true;
			field = value;
		}
	} = DefaultWebProxyPassword;

	[JsonDisallowNull]
	[JsonInclude]
	[JsonPropertyName($"{SharedInfo.UlongCompatibilityStringPrefix}{nameof(SteamOwnerID)}")]
	private string SSteamOwnerID {
		get => SteamOwnerID.ToString(CultureInfo.InvariantCulture);

		init {
			ArgumentException.ThrowIfNullOrEmpty(value);

			// We intend to throw exception back to caller here
			SteamOwnerID = ulong.Parse(value, CultureInfo.InvariantCulture);
		}
	}

	[JsonConstructor]
	public GlobalConfig() { }

	[UsedImplicitly]
	public bool ShouldSerializeAutoRestart() => !Saving || (AutoRestart != DefaultAutoRestart);

	[UsedImplicitly]
	public bool ShouldSerializeBlacklist() => !Saving || ((Blacklist != DefaultBlacklist) && !Blacklist.SetEquals(DefaultBlacklist));

	[UsedImplicitly]
	public bool ShouldSerializeCommandPrefix() => !Saving || (CommandPrefix != DefaultCommandPrefix);

	[UsedImplicitly]
	public bool ShouldSerializeConfirmationsLimiterDelay() => !Saving || (ConfirmationsLimiterDelay != DefaultConfirmationsLimiterDelay);

	[UsedImplicitly]
	public bool ShouldSerializeConnectionTimeout() => !Saving || (ConnectionTimeout != DefaultConnectionTimeout);

	[UsedImplicitly]
	public bool ShouldSerializeCurrentCulture() => !Saving || (CurrentCulture != DefaultCurrentCulture);

	[UsedImplicitly]
	public bool ShouldSerializeDebug() => !Saving || (Debug != DefaultDebug);

	[UsedImplicitly]
	public bool ShouldSerializeDefaultBot() => !Saving || (DefaultBot != DefaultDefaultBot);

	[UsedImplicitly]
	public bool ShouldSerializeFarmingDelay() => !Saving || (FarmingDelay != DefaultFarmingDelay);

	[UsedImplicitly]
	public bool ShouldSerializeFilterBadBots() => !Saving || (FilterBadBots != DefaultFilterBadBots);

	[UsedImplicitly]
	public bool ShouldSerializeGiftsLimiterDelay() => !Saving || (GiftsLimiterDelay != DefaultGiftsLimiterDelay);

	[UsedImplicitly]
	public bool ShouldSerializeHeadless() => !Saving || (Headless != DefaultHeadless);

	[UsedImplicitly]
	public bool ShouldSerializeIdleFarmingPeriod() => !Saving || (IdleFarmingPeriod != DefaultIdleFarmingPeriod);

	[UsedImplicitly]
	public bool ShouldSerializeInventoryLimiterDelay() => !Saving || (InventoryLimiterDelay != DefaultInventoryLimiterDelay);

	[UsedImplicitly]
	public bool ShouldSerializeIPC() => !Saving || (IPC != DefaultIPC);

	[UsedImplicitly]
	public bool ShouldSerializeIPCPassword() => Saving && IsIPCPasswordSet && (IPCPassword != DefaultIPCPassword);

	[UsedImplicitly]
	public bool ShouldSerializeIPCPasswordFormat() => !Saving || (IPCPasswordFormat != DefaultIPCPasswordFormat);

	[UsedImplicitly]
	public bool ShouldSerializeLicenseID() => Saving && IsLicenseIDSet && (LicenseID != DefaultLicenseID) && (LicenseID != Guid.Empty);

	[UsedImplicitly]
	public bool ShouldSerializeLoginLimiterDelay() => !Saving || (LoginLimiterDelay != DefaultLoginLimiterDelay);

	[UsedImplicitly]
	public bool ShouldSerializeMaxFarmingTime() => !Saving || (MaxFarmingTime != DefaultMaxFarmingTime);

	[UsedImplicitly]
	public bool ShouldSerializeMaxTradeHoldDuration() => !Saving || (MaxTradeHoldDuration != DefaultMaxTradeHoldDuration);

	[UsedImplicitly]
	public bool ShouldSerializeMinFarmingDelayAfterBlock() => !Saving || (MinFarmingDelayAfterBlock != DefaultMinFarmingDelayAfterBlock);

	[UsedImplicitly]
	public bool ShouldSerializeOptimizationMode() => !Saving || (OptimizationMode != DefaultOptimizationMode);

	[UsedImplicitly]
	public bool ShouldSerializePluginsUpdateList() => !Saving || ((PluginsUpdateList != DefaultPluginsUpdateList) && !PluginsUpdateList.SetEquals(DefaultPluginsUpdateList));

	[UsedImplicitly]
	public bool ShouldSerializePluginsUpdateMode() => !Saving || (PluginsUpdateMode != DefaultPluginsUpdateMode);

	[UsedImplicitly]
	public bool ShouldSerializeShutdownIfPossible() => !Saving || (ShutdownIfPossible != DefaultShutdownIfPossible);

	[UsedImplicitly]
	public bool ShouldSerializeSSteamOwnerID() => !Saving;

	[UsedImplicitly]
	public bool ShouldSerializeSteamMessagePrefix() => !Saving || (SteamMessagePrefix != DefaultSteamMessagePrefix);

	[UsedImplicitly]
	public bool ShouldSerializeSteamOwnerID() => !Saving || (SteamOwnerID != DefaultSteamOwnerID);

	[UsedImplicitly]
	public bool ShouldSerializeSteamProtocols() => !Saving || (SteamProtocols != DefaultSteamProtocols);

	[UsedImplicitly]
	public bool ShouldSerializeUpdateChannel() => !Saving || (UpdateChannel != DefaultUpdateChannel);

	[UsedImplicitly]
	public bool ShouldSerializeUpdatePeriod() => !Saving || (UpdatePeriod != DefaultUpdatePeriod);

	[UsedImplicitly]
	public bool ShouldSerializeWebLimiterDelay() => !Saving || (WebLimiterDelay != DefaultWebLimiterDelay);

	[UsedImplicitly]
	public bool ShouldSerializeWebProxyPassword() => Saving && IsWebProxyPasswordSet && (WebProxyPassword != DefaultWebProxyPassword);

	[UsedImplicitly]
	public bool ShouldSerializeWebProxyText() => !Saving || (WebProxyText != DefaultWebProxyText);

	[UsedImplicitly]
	public bool ShouldSerializeWebProxyUsername() => !Saving || (WebProxyUsername != DefaultWebProxyUsername);

	internal (bool Valid, string? ErrorMessage) CheckValidation() {
		if (Blacklist.Contains(0)) {
			return (false, Strings.FormatErrorConfigPropertyInvalid(nameof(Blacklist), 0));
		}

		if (ConnectionTimeout == 0) {
			return (false, Strings.FormatErrorConfigPropertyInvalid(nameof(ConnectionTimeout), ConnectionTimeout));
		}

		if (FarmingDelay == 0) {
			return (false, Strings.FormatErrorConfigPropertyInvalid(nameof(FarmingDelay), FarmingDelay));
		}

		if (!Enum.IsDefined(IPCPasswordFormat)) {
			return (false, Strings.FormatErrorConfigPropertyInvalid(nameof(IPCPasswordFormat), IPCPasswordFormat));
		}

		switch (IPCPasswordFormat) {
			case ArchiCryptoHelper.EHashingMethod.Pbkdf2 when !string.IsNullOrEmpty(IPCPassword):
			case ArchiCryptoHelper.EHashingMethod.SCrypt when !string.IsNullOrEmpty(IPCPassword):
				try {
					// Ensure IPCPassword is in the appropriate format, base64-encoded string in this case
					_ = Convert.FromBase64String(IPCPassword);
				} catch (FormatException e) {
					ASF.ArchiLogger.LogGenericWarningException(e);

					return (false, Strings.FormatErrorConfigPropertyInvalid(nameof(IPCPassword), IPCPassword));
				}

				break;
		}

		if (MaxFarmingTime == 0) {
			return (false, Strings.FormatErrorConfigPropertyInvalid(nameof(MaxFarmingTime), MaxFarmingTime));
		}

		if (!Enum.IsDefined(OptimizationMode)) {
			return (false, Strings.FormatErrorConfigPropertyInvalid(nameof(OptimizationMode), OptimizationMode));
		}

		if (!Enum.IsDefined(PluginsUpdateMode)) {
			return (false, Strings.FormatErrorConfigPropertyInvalid(nameof(PluginsUpdateMode), PluginsUpdateMode));
		}

		if (!string.IsNullOrEmpty(SteamMessagePrefix) && !SteamChatMessage.IsValidPrefix(SteamMessagePrefix)) {
			return (false, Strings.FormatErrorConfigPropertyInvalid(nameof(SteamMessagePrefix), SteamMessagePrefix));
		}

		if ((SteamOwnerID != 0) && !new SteamID(SteamOwnerID).IsIndividualAccount) {
			return (false, Strings.FormatErrorConfigPropertyInvalid(nameof(SteamOwnerID), SteamOwnerID));
		}

		if (SteamProtocols is <= 0 or > ProtocolTypes.All) {
			return (false, Strings.FormatErrorConfigPropertyInvalid(nameof(SteamProtocols), SteamProtocols));
		}

		return Enum.IsDefined(UpdateChannel) ? (true, null) : (false, Strings.FormatErrorConfigPropertyInvalid(nameof(UpdateChannel), UpdateChannel));
	}

	internal static async Task<(GlobalConfig? GlobalConfig, string? LatestJson)> Load(string filePath) {
		ArgumentException.ThrowIfNullOrEmpty(filePath);

		if (!File.Exists(filePath)) {
			return (null, null);
		}

		string json;
		GlobalConfig? globalConfig;

		try {
			json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);

			if (string.IsNullOrEmpty(json)) {
				ASF.ArchiLogger.LogGenericError(Strings.FormatErrorIsEmpty(nameof(json)));

				return (null, null);
			}

			globalConfig = json.ToJsonObject<GlobalConfig>();
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);

			return (null, null);
		}

		if (globalConfig == null) {
			ASF.ArchiLogger.LogNullError(globalConfig);

			return (null, null);
		}

		(bool valid, string? errorMessage) = globalConfig.CheckValidation();

		if (!valid) {
			if (!string.IsNullOrEmpty(errorMessage)) {
				ASF.ArchiLogger.LogGenericError(errorMessage);
			}

			return (null, null);
		}

		if (globalConfig.IPC) {
			switch (globalConfig.IPCPasswordFormat) {
				case ArchiCryptoHelper.EHashingMethod.Pbkdf2 when ArchiCryptoHelper.HasDefaultCryptKey:
				case ArchiCryptoHelper.EHashingMethod.SCrypt when ArchiCryptoHelper.HasDefaultCryptKey:
					ASF.ArchiLogger.LogGenericWarning(Strings.FormatWarningDefaultCryptKeyUsedForHashing(globalConfig.IPCPasswordFormat, nameof(IPCPassword)));

					break;
			}
		}

		if (!Program.ConfigMigrate) {
			return (globalConfig, null);
		}

		globalConfig.Saving = true;
		string latestJson = globalConfig.ToJsonText(true);
		globalConfig.Saving = false;

		return (globalConfig, json != latestJson ? latestJson : null);
	}

	internal static async Task<bool> Write(string filePath, GlobalConfig globalConfig) {
		ArgumentException.ThrowIfNullOrEmpty(filePath);
		ArgumentNullException.ThrowIfNull(globalConfig);

		string json = globalConfig.ToJsonText(true);

		return await SerializableFile.Write(filePath, json).ConfigureAwait(false);
	}

	[PublicAPI]
	public enum EOptimizationMode : byte {
		MaxPerformance,
		MinMemoryUsage
	}

	[PublicAPI]
	public enum EPluginsUpdateMode : byte {
		Whitelist,
		Blacklist
	}

	[PublicAPI]
	public enum EUpdateChannel : byte {
		None,
		Stable,
		PreRelease
	}
}
