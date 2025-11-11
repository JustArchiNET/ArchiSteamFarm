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
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers;
using ArchiSteamFarm.Helpers.Json;
using ArchiSteamFarm.IPC.Integration;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Steam.Data;
using ArchiSteamFarm.Steam.Integration;
using JetBrains.Annotations;
using SteamKit2;

namespace ArchiSteamFarm.Steam.Storage;

[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
public sealed class BotConfig {
	[PublicAPI]
	public const bool DefaultAcceptGifts = false;

	[PublicAPI]
	public const EBotBehaviour DefaultBotBehaviour = EBotBehaviour.None;

	[PublicAPI]
	public const string? DefaultCustomGamePlayedWhileFarming = null;

	[PublicAPI]
	public const string? DefaultCustomGamePlayedWhileIdle = null;

	[PublicAPI]
	public const bool DefaultEnabled = false;

	[PublicAPI]
	public const EFarmingPreferences DefaultFarmingPreferences = EFarmingPreferences.None;

	[PublicAPI]
	public const EGamingDeviceType DefaultGamingDeviceType = EGamingDeviceType.StandardPC;

	[PublicAPI]
	public const byte DefaultHoursUntilCardDrops = 3;

	[PublicAPI]
	public const string? DefaultMachineName = null;

	[PublicAPI]
	public const EPersonaStateFlag DefaultOnlineFlags = 0;

	[PublicAPI]
	public const EPersonaState DefaultOnlineStatus = EPersonaState.Online;

	[PublicAPI]
	public const ArchiCryptoHelper.ECryptoMethod DefaultPasswordFormat = ArchiCryptoHelper.ECryptoMethod.PlainText;

	[PublicAPI]
	public const ERedeemingPreferences DefaultRedeemingPreferences = ERedeemingPreferences.None;

	[PublicAPI]
	public const ERemoteCommunication DefaultRemoteCommunication = ERemoteCommunication.All;

	[PublicAPI]
	public const byte DefaultSendTradePeriod = 0;

	[PublicAPI]
	public const string? DefaultSteamLogin = null;

	[PublicAPI]
	public const ulong DefaultSteamMasterClanID = 0;

	[PublicAPI]
	public const string? DefaultSteamParentalCode = null;

	[PublicAPI]
	public const string? DefaultSteamPassword = null;

	[PublicAPI]
	public const string? DefaultSteamTradeToken = null;

	[PublicAPI]
	public const byte DefaultTradeCheckPeriod = 60;

	[PublicAPI]
	public const ETradingPreferences DefaultTradingPreferences = ETradingPreferences.None;

	[PublicAPI]
	public const bool DefaultUseLoginKeys = true;

	[PublicAPI]
	public const EUIMode DefaultUserInterfaceMode = EUIMode.VGUI;

	[PublicAPI]
	public const string? DefaultWebProxyPassword = null;

	[PublicAPI]
	public const string? DefaultWebProxyText = null;

	[PublicAPI]
	public const string? DefaultWebProxyUsername = null;

	internal const byte SteamParentalCodeLength = 4;
	internal const byte SteamTradeTokenLength = 8;

	[PublicAPI]
	public static readonly ImmutableHashSet<EAssetType> DefaultCompleteTypesToSend = [];

	[PublicAPI]
	public static readonly ImmutableList<EFarmingOrder> DefaultFarmingOrders = [];

	[PublicAPI]
	public static readonly ImmutableList<uint> DefaultGamesPlayedWhileIdle = [];

	[PublicAPI]
	public static readonly ImmutableHashSet<EAssetType> DefaultLootableTypes = [EAssetType.BoosterPack, EAssetType.FoilTradingCard, EAssetType.TradingCard];

	[PublicAPI]
	public static readonly ImmutableHashSet<EAssetType> DefaultMatchableTypes = [EAssetType.TradingCard];

	[PublicAPI]
	public static readonly ImmutableDictionary<ulong, EAccess> DefaultSteamUserPermissions = ImmutableDictionary<ulong, EAccess>.Empty;

	[PublicAPI]
	public static readonly ImmutableHashSet<EAssetType> DefaultTransferableTypes = [EAssetType.BoosterPack, EAssetType.FoilTradingCard, EAssetType.TradingCard];

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

			WebProxy proxy = new(uri, true);

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
	public bool AcceptGifts { get; init; } = DefaultAcceptGifts;

	[JsonInclude]
	public EBotBehaviour BotBehaviour { get; init; } = DefaultBotBehaviour;

	[JsonDisallowNull]
	[JsonInclude]
	[SwaggerValidValues(ValidIntValues = [(int) EAssetType.FoilTradingCard, (int) EAssetType.TradingCard])]
	public ImmutableHashSet<EAssetType> CompleteTypesToSend { get; init; } = DefaultCompleteTypesToSend;

	[JsonInclude]
	public string? CustomGamePlayedWhileFarming { get; init; } = DefaultCustomGamePlayedWhileFarming;

	[JsonInclude]
	public string? CustomGamePlayedWhileIdle { get; init; } = DefaultCustomGamePlayedWhileIdle;

	[JsonInclude]
	public bool Enabled { get; init; } = DefaultEnabled;

	[JsonDisallowNull]
	[JsonInclude]
	public ImmutableList<EFarmingOrder> FarmingOrders { get; init; } = DefaultFarmingOrders;

	[JsonInclude]
	public EFarmingPreferences FarmingPreferences { get; init; } = DefaultFarmingPreferences;

	[JsonDisallowNull]
	[JsonInclude]
	[MaxLength(ArchiHandler.MaxGamesPlayedConcurrently)]
	[SwaggerItemsMinMax(MinimumUint = 1, MaximumUint = uint.MaxValue)]
	[UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL2026:RequiresUnreferencedCode", Justification = "This is optional, supportive attribute, we don't care if it gets trimmed or not")]
	public ImmutableList<uint> GamesPlayedWhileIdle { get; init; } = DefaultGamesPlayedWhileIdle;

	[JsonInclude]
	public EGamingDeviceType GamingDeviceType { get; init; } = DefaultGamingDeviceType;

	[JsonInclude]
	[Range(byte.MinValue, byte.MaxValue)]
	public byte HoursUntilCardDrops { get; init; } = DefaultHoursUntilCardDrops;

	[JsonDisallowNull]
	[JsonInclude]
	public ImmutableHashSet<EAssetType> LootableTypes { get; init; } = DefaultLootableTypes;

	[JsonInclude]
	public string? MachineName { get; init; } = DefaultMachineName;

	[JsonDisallowNull]
	[JsonInclude]
	public ImmutableHashSet<EAssetType> MatchableTypes { get; init; } = DefaultMatchableTypes;

	[JsonInclude]
	public EPersonaStateFlag OnlineFlags { get; init; } = DefaultOnlineFlags;

	[JsonInclude]
	public EPersonaState OnlineStatus { get; init; } = DefaultOnlineStatus;

	[JsonInclude]
	public ArchiCryptoHelper.ECryptoMethod PasswordFormat { get; internal set; } = DefaultPasswordFormat;

	[JsonInclude]
	public ERedeemingPreferences RedeemingPreferences { get; init; } = DefaultRedeemingPreferences;

	[JsonInclude]
	public ERemoteCommunication RemoteCommunication { get; init; } = DefaultRemoteCommunication;

	[JsonInclude]
	[Range(byte.MinValue, byte.MaxValue)]
	public byte SendTradePeriod { get; init; } = DefaultSendTradePeriod;

	[JsonInclude]
	public string? SteamLogin {
		get;

		internal set {
			IsSteamLoginSet = true;
			field = value;
		}
	} = DefaultSteamLogin;

	[JsonInclude]
	[SwaggerSteamIdentifier(AccountType = EAccountType.Clan)]
	[SwaggerValidValues(ValidIntValues = [0])]
	public ulong SteamMasterClanID { get; init; } = DefaultSteamMasterClanID;

	[JsonInclude]
	[MaxLength(SteamParentalCodeLength)]
	[MinLength(SteamParentalCodeLength)]
	[SwaggerValidValues(ValidStringValues = ["0"])]
	[UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL2026:RequiresUnreferencedCode", Justification = "This is optional, supportive attribute, we don't care if it gets trimmed or not")]
	public string? SteamParentalCode {
		get;

		internal set {
			IsSteamParentalCodeSet = true;
			field = value;
		}
	} = DefaultSteamParentalCode;

	[JsonInclude]
	[SwaggerSecurityCritical]
	public string? SteamPassword {
		get;

		internal set {
			IsSteamPasswordSet = true;
			field = value;
		}
	} = DefaultSteamPassword;

	[JsonInclude]
	[MaxLength(SteamTradeTokenLength)]
	[MinLength(SteamTradeTokenLength)]
	[UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL2026:RequiresUnreferencedCode", Justification = "This is optional, supportive attribute, we don't care if it gets trimmed or not")]
	public string? SteamTradeToken { get; init; } = DefaultSteamTradeToken;

	[JsonDisallowNull]
	[JsonInclude]
	public ImmutableDictionary<ulong, EAccess> SteamUserPermissions { get; init; } = DefaultSteamUserPermissions;

	[JsonInclude]
	[Range(byte.MinValue, byte.MaxValue)]
	public byte TradeCheckPeriod { get; init; } = DefaultTradeCheckPeriod;

	[JsonInclude]
	public ETradingPreferences TradingPreferences { get; init; } = DefaultTradingPreferences;

	[JsonDisallowNull]
	[JsonInclude]
	public ImmutableHashSet<EAssetType> TransferableTypes { get; init; } = DefaultTransferableTypes;

	[JsonInclude]
	public bool UseLoginKeys { get; init; } = DefaultUseLoginKeys;

	[JsonInclude]
	public EUIMode UserInterfaceMode { get; init; } = DefaultUserInterfaceMode;

	[JsonInclude]
	[JsonPropertyName(nameof(WebProxy))]
	public string? WebProxyText { get; init; } = DefaultWebProxyText;

	[JsonInclude]
	public string? WebProxyUsername { get; init; } = DefaultWebProxyUsername;

	[JsonExtensionData]
	[JsonInclude]
	internal Dictionary<string, JsonElement>? AdditionalProperties { get; set; }

	internal bool IsSteamLoginSet { get; set; }
	internal bool IsSteamParentalCodeSet { get; set; }
	internal bool IsSteamPasswordSet { get; set; }
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
	[JsonPropertyName($"{SharedInfo.UlongCompatibilityStringPrefix}{nameof(SteamMasterClanID)}")]
	private string SSteamMasterClanID {
		get => SteamMasterClanID.ToString(CultureInfo.InvariantCulture);

		init {
			ArgumentException.ThrowIfNullOrEmpty(value);

			// We intend to throw exception back to caller here
			SteamMasterClanID = ulong.Parse(value, CultureInfo.InvariantCulture);
		}
	}

	[JsonConstructor]
	public BotConfig() { }

	[UsedImplicitly]
	public bool ShouldSerializeAcceptGifts() => !Saving || (AcceptGifts != DefaultAcceptGifts);

	[UsedImplicitly]
	public bool ShouldSerializeBotBehaviour() => !Saving || (BotBehaviour != DefaultBotBehaviour);

	[UsedImplicitly]
	public bool ShouldSerializeCompleteTypesToSend() => !Saving || ((CompleteTypesToSend != DefaultCompleteTypesToSend) && !CompleteTypesToSend.SetEquals(DefaultCompleteTypesToSend));

	[UsedImplicitly]
	public bool ShouldSerializeCustomGamePlayedWhileFarming() => !Saving || (CustomGamePlayedWhileFarming != DefaultCustomGamePlayedWhileFarming);

	[UsedImplicitly]
	public bool ShouldSerializeCustomGamePlayedWhileIdle() => !Saving || (CustomGamePlayedWhileIdle != DefaultCustomGamePlayedWhileIdle);

	[UsedImplicitly]
	public bool ShouldSerializeEnabled() => !Saving || (Enabled != DefaultEnabled);

	[UsedImplicitly]
	public bool ShouldSerializeFarmingOrders() => !Saving || ((FarmingOrders != DefaultFarmingOrders) && !FarmingOrders.SequenceEqual(DefaultFarmingOrders));

	[UsedImplicitly]
	public bool ShouldSerializeFarmingPreferences() => !Saving || (FarmingPreferences != DefaultFarmingPreferences);

	[UsedImplicitly]
	public bool ShouldSerializeGamesPlayedWhileIdle() => !Saving || ((GamesPlayedWhileIdle != DefaultGamesPlayedWhileIdle) && !GamesPlayedWhileIdle.SequenceEqual(DefaultGamesPlayedWhileIdle));

	[UsedImplicitly]
	public bool ShouldSerializeGamingDeviceType() => !Saving || (GamingDeviceType != DefaultGamingDeviceType);

	[UsedImplicitly]
	public bool ShouldSerializeHoursUntilCardDrops() => !Saving || (HoursUntilCardDrops != DefaultHoursUntilCardDrops);

	[UsedImplicitly]
	public bool ShouldSerializeLootableTypes() => !Saving || ((LootableTypes != DefaultLootableTypes) && !LootableTypes.SetEquals(DefaultLootableTypes));

	[UsedImplicitly]
	public bool ShouldSerializeMachineName() => !Saving || (MachineName != DefaultMachineName);

	[UsedImplicitly]
	public bool ShouldSerializeMatchableTypes() => !Saving || ((MatchableTypes != DefaultMatchableTypes) && !MatchableTypes.SetEquals(DefaultMatchableTypes));

	[UsedImplicitly]
	public bool ShouldSerializeOnlineFlags() => !Saving || (OnlineFlags != DefaultOnlineFlags);

	[UsedImplicitly]
	public bool ShouldSerializeOnlineStatus() => !Saving || (OnlineStatus != DefaultOnlineStatus);

	[UsedImplicitly]
	public bool ShouldSerializePasswordFormat() => !Saving || (PasswordFormat != DefaultPasswordFormat);

	[UsedImplicitly]
	public bool ShouldSerializeRedeemingPreferences() => !Saving || (RedeemingPreferences != DefaultRedeemingPreferences);

	[UsedImplicitly]
	public bool ShouldSerializeRemoteCommunication() => !Saving || (RemoteCommunication != DefaultRemoteCommunication);

	[UsedImplicitly]
	public bool ShouldSerializeSendTradePeriod() => !Saving || (SendTradePeriod != DefaultSendTradePeriod);

	[UsedImplicitly]
	public bool ShouldSerializeSSteamMasterClanID() => !Saving;

	[UsedImplicitly]
	public bool ShouldSerializeSteamLogin() => Saving && IsSteamLoginSet && (SteamLogin != DefaultSteamLogin);

	[UsedImplicitly]
	public bool ShouldSerializeSteamMasterClanID() => !Saving || (SteamMasterClanID != DefaultSteamMasterClanID);

	[UsedImplicitly]
	public bool ShouldSerializeSteamParentalCode() => Saving && IsSteamParentalCodeSet && (SteamParentalCode != DefaultSteamParentalCode);

	[UsedImplicitly]
	public bool ShouldSerializeSteamPassword() => Saving && IsSteamPasswordSet && (SteamPassword != DefaultSteamPassword);

	[UsedImplicitly]
	public bool ShouldSerializeSteamTradeToken() => !Saving || (SteamTradeToken != DefaultSteamTradeToken);

	[UsedImplicitly]
	public bool ShouldSerializeSteamUserPermissions() => !Saving || ((SteamUserPermissions != DefaultSteamUserPermissions) && ((SteamUserPermissions.Count != DefaultSteamUserPermissions.Count) || SteamUserPermissions.Except(DefaultSteamUserPermissions).Any()));

	[UsedImplicitly]
	public bool ShouldSerializeTradeCheckPeriod() => !Saving || (TradeCheckPeriod != DefaultTradeCheckPeriod);

	[UsedImplicitly]
	public bool ShouldSerializeTradingPreferences() => !Saving || (TradingPreferences != DefaultTradingPreferences);

	[UsedImplicitly]
	public bool ShouldSerializeTransferableTypes() => !Saving || ((TransferableTypes != DefaultTransferableTypes) && !TransferableTypes.SetEquals(DefaultTransferableTypes));

	[UsedImplicitly]
	public bool ShouldSerializeUseLoginKeys() => !Saving || (UseLoginKeys != DefaultUseLoginKeys);

	[UsedImplicitly]
	public bool ShouldSerializeUserInterfaceMode() => !Saving || (UserInterfaceMode != DefaultUserInterfaceMode);

	[UsedImplicitly]
	public bool ShouldSerializeWebProxyPassword() => Saving && IsWebProxyPasswordSet && (WebProxyPassword != DefaultWebProxyPassword);

	[UsedImplicitly]
	public bool ShouldSerializeWebProxyText() => !Saving || (WebProxyText != DefaultWebProxyText);

	[UsedImplicitly]
	public bool ShouldSerializeWebProxyUsername() => !Saving || (WebProxyUsername != DefaultWebProxyUsername);

	[PublicAPI]
	public static async Task<bool> Write(string filePath, BotConfig botConfig) {
		ArgumentException.ThrowIfNullOrEmpty(filePath);
		ArgumentNullException.ThrowIfNull(botConfig);

		string json = botConfig.ToJsonText(true);

		return await SerializableFile.Write(filePath, json).ConfigureAwait(false);
	}

	internal (bool Valid, string? ErrorMessage) CheckValidation() {
		if (BotBehaviour > EBotBehaviour.All) {
			return (false, Strings.FormatErrorConfigPropertyInvalid(nameof(BotBehaviour), BotBehaviour));
		}

		HashSet<EAssetType>? completeTypesToSendValidTypes = null;

		foreach (EAssetType completableType in CompleteTypesToSend) {
			if (!Enum.IsDefined(completableType)) {
				return (false, Strings.FormatErrorConfigPropertyInvalid(nameof(CompleteTypesToSend), completableType));
			}

			if (completeTypesToSendValidTypes == null) {
				SwaggerValidValuesAttribute? completeTypesToSendValidValues = typeof(BotConfig).GetProperty(nameof(CompleteTypesToSend))?.GetCustomAttribute<SwaggerValidValuesAttribute>();

				if (completeTypesToSendValidValues?.ValidIntValues == null) {
					throw new InvalidOperationException(nameof(completeTypesToSendValidValues));
				}

				completeTypesToSendValidTypes = completeTypesToSendValidValues.ValidIntValues.Select(static value => (EAssetType) value).ToHashSet();
			}

			if (!completeTypesToSendValidTypes.Contains(completableType)) {
				return (false, Strings.FormatErrorConfigPropertyInvalid(nameof(CompleteTypesToSend), completableType));
			}
		}

		if (!string.IsNullOrEmpty(CustomGamePlayedWhileFarming)) {
			try {
				// Test CustomGamePlayedWhileFarming against supported format, otherwise we'll throw later when used
				string _ = string.Format(CultureInfo.CurrentCulture, CustomGamePlayedWhileFarming, null, null);
			} catch (FormatException e) {
				return (false, Strings.FormatErrorConfigPropertyInvalid(nameof(CustomGamePlayedWhileFarming), e.Message));
			}
		}

		foreach (EFarmingOrder farmingOrder in FarmingOrders.Where(static farmingOrder => !Enum.IsDefined(farmingOrder))) {
			return (false, Strings.FormatErrorConfigPropertyInvalid(nameof(FarmingOrders), farmingOrder));
		}

		if (GamesPlayedWhileIdle.Contains(0)) {
			return (false, Strings.FormatErrorConfigPropertyInvalid(nameof(GamesPlayedWhileIdle), 0));
		}

		if (GamesPlayedWhileIdle.Count > ArchiHandler.MaxGamesPlayedConcurrently) {
			return (false, Strings.FormatErrorConfigPropertyInvalid(nameof(GamesPlayedWhileIdle), $"{nameof(GamesPlayedWhileIdle.Count)} {GamesPlayedWhileIdle.Count} > {ArchiHandler.MaxGamesPlayedConcurrently}"));
		}

		if ((GamingDeviceType == EGamingDeviceType.Unknown) || !Enum.IsDefined(GamingDeviceType)) {
			return (false, Strings.FormatErrorConfigPropertyInvalid(nameof(GamingDeviceType), GamingDeviceType));
		}

		foreach (EAssetType lootableType in LootableTypes.Where(static lootableType => !Enum.IsDefined(lootableType))) {
			return (false, Strings.FormatErrorConfigPropertyInvalid(nameof(LootableTypes), lootableType));
		}

		if (!string.IsNullOrEmpty(MachineName)) {
			try {
				// Test MachineName against supported format, otherwise we'll throw later when used
				string _ = string.Format(CultureInfo.CurrentCulture, MachineName, null, null, null);
			} catch (FormatException e) {
				return (false, Strings.FormatErrorConfigPropertyInvalid(nameof(MachineName), e.Message));
			}
		}

		foreach (EAssetType matchableType in MatchableTypes.Where(static matchableType => !Enum.IsDefined(matchableType))) {
			return (false, Strings.FormatErrorConfigPropertyInvalid(nameof(MatchableTypes), matchableType));
		}

		if (OnlineFlags < 0) {
			return (false, Strings.FormatErrorConfigPropertyInvalid(nameof(OnlineFlags), OnlineFlags));
		}

		if (!Enum.IsDefined(OnlineStatus)) {
			return (false, Strings.FormatErrorConfigPropertyInvalid(nameof(OnlineStatus), OnlineStatus));
		}

		if (!Enum.IsDefined(PasswordFormat)) {
			return (false, Strings.FormatErrorConfigPropertyInvalid(nameof(PasswordFormat), PasswordFormat));
		}

		if (RedeemingPreferences > ERedeemingPreferences.All) {
			return (false, Strings.FormatErrorConfigPropertyInvalid(nameof(RedeemingPreferences), RedeemingPreferences));
		}

		if ((SteamMasterClanID != 0) && !new SteamID(SteamMasterClanID).IsClanAccount) {
			return (false, Strings.FormatErrorConfigPropertyInvalid(nameof(SteamMasterClanID), SteamMasterClanID));
		}

		if (!string.IsNullOrEmpty(SteamParentalCode) && ((SteamParentalCode.Length != SteamParentalCodeLength) || SteamParentalCode.Any(static character => character is < '0' or > '9'))) {
			return (false, Strings.FormatErrorConfigPropertyInvalid(nameof(SteamParentalCode), SteamParentalCode));
		}

		if (!string.IsNullOrEmpty(SteamTradeToken) && (SteamTradeToken.Length != SteamTradeTokenLength)) {
			return (false, Strings.FormatErrorConfigPropertyInvalid(nameof(SteamTradeToken), SteamTradeToken));
		}

		foreach ((ulong steamID, EAccess permission) in SteamUserPermissions) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
				return (false, Strings.FormatErrorConfigPropertyInvalid(nameof(SteamUserPermissions), steamID));
			}

			if (!Enum.IsDefined(permission)) {
				return (false, Strings.FormatErrorConfigPropertyInvalid(nameof(SteamUserPermissions), permission));
			}
		}

		if (TradingPreferences > ETradingPreferences.All) {
			return (false, Strings.FormatErrorConfigPropertyInvalid(nameof(TradingPreferences), TradingPreferences));
		}

		return (UserInterfaceMode < EUIMode.VGUI) || !Enum.IsDefined(UserInterfaceMode) ? (false, Strings.FormatErrorConfigPropertyInvalid(nameof(UserInterfaceMode), UserInterfaceMode)) : (true, null);
	}

	internal async Task<string?> GetDecryptedSteamPassword() {
		if (string.IsNullOrEmpty(SteamPassword)) {
			return null;
		}

		if (PasswordFormat == ArchiCryptoHelper.ECryptoMethod.PlainText) {
			// We can return SteamPassword only with PlainText, as despite no transformation other password formats still require decryption process
			return SteamPassword;
		}

		string? result = await ArchiCryptoHelper.Decrypt(PasswordFormat, SteamPassword).ConfigureAwait(false);

		if (string.IsNullOrEmpty(result)) {
			ASF.ArchiLogger.LogGenericError(Strings.FormatErrorIsInvalid(nameof(SteamPassword)));

			return null;
		}

		return result;
	}

	internal static async Task<(BotConfig? BotConfig, string? LatestJson)> Load(string filePath) {
		ArgumentException.ThrowIfNullOrEmpty(filePath);

		if (!File.Exists(filePath)) {
			return (null, null);
		}

		string json;
		BotConfig? botConfig;

		try {
			json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);

			if (string.IsNullOrEmpty(json)) {
				ASF.ArchiLogger.LogGenericError(Strings.FormatErrorIsEmpty(nameof(json)));

				return (null, null);
			}

			botConfig = json.ToJsonObject<BotConfig>();
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);

			return (null, null);
		}

		if (botConfig == null) {
			ASF.ArchiLogger.LogNullError(botConfig);

			return (null, null);
		}

		(bool valid, string? errorMessage) = botConfig.CheckValidation();

		if (!valid) {
			if (!string.IsNullOrEmpty(errorMessage)) {
				ASF.ArchiLogger.LogGenericError(errorMessage);
			}

			return (null, null);
		}

		switch (botConfig.PasswordFormat) {
			case ArchiCryptoHelper.ECryptoMethod.AES when ArchiCryptoHelper.HasDefaultCryptKey:
				ASF.ArchiLogger.LogGenericError(Strings.FormatWarningDefaultCryptKeyUsedForEncryption(botConfig.PasswordFormat, nameof(SteamPassword)));

				break;
			case ArchiCryptoHelper.ECryptoMethod.ProtectedDataForCurrentUser when ArchiCryptoHelper.HasDefaultCryptKey:
				ASF.ArchiLogger.LogGenericInfo(Strings.FormatWarningDefaultCryptKeyUsedForHashing(botConfig.PasswordFormat, nameof(SteamPassword)));

				break;
		}

		if (!Program.ConfigMigrate) {
			return (botConfig, null);
		}

		botConfig.Saving = true;
		string latestJson = botConfig.ToJsonText(true);
		botConfig.Saving = false;

		return (botConfig, json != latestJson ? latestJson : null);
	}

	[PublicAPI]
	public enum EAccess : byte {
		None,
		FamilySharing,
		Operator,
		Master
	}

	[Flags]
	[PublicAPI]
	public enum EBotBehaviour : byte {
		None = 0,
		RejectInvalidFriendInvites = 1,
		RejectInvalidTrades = 2,
		RejectInvalidGroupInvites = 4,
		DismissInventoryNotifications = 8,
		MarkReceivedMessagesAsRead = 16,
		MarkBotMessagesAsRead = 32,
		DisableIncomingTradesParsing = 64,
		All = RejectInvalidFriendInvites | RejectInvalidTrades | RejectInvalidGroupInvites | DismissInventoryNotifications | MarkReceivedMessagesAsRead | MarkBotMessagesAsRead | DisableIncomingTradesParsing
	}

	[PublicAPI]
	public enum EFarmingOrder : byte {
		Unordered,
		AppIDsAscending,
		AppIDsDescending,
		CardDropsAscending,
		CardDropsDescending,
		HoursAscending,
		HoursDescending,
		NamesAscending,
		NamesDescending,
		Random,
		BadgeLevelsAscending,
		BadgeLevelsDescending,
		RedeemDateTimesAscending,
		RedeemDateTimesDescending,
		MarketableAscending,
		MarketableDescending
	}

	[Flags]
	[PublicAPI]
	public enum EFarmingPreferences : ushort {
		None = 0,
		FarmingPausedByDefault = 1,
		ShutdownOnFarmingFinished = 2,
		SendOnFarmingFinished = 4,
		FarmPriorityQueueOnly = 8,
		SkipRefundableGames = 16,
		SkipUnplayedGames = 32,
		EnableRiskyCardsDiscovery = 64,
		AutoUnpackBoosterPacks = 256,
		All = FarmingPausedByDefault | ShutdownOnFarmingFinished | SendOnFarmingFinished | FarmPriorityQueueOnly | SkipRefundableGames | SkipUnplayedGames | EnableRiskyCardsDiscovery | AutoUnpackBoosterPacks
	}

	[Flags]
	[PublicAPI]
	public enum ERedeemingPreferences : byte {
		None = 0,
		Forwarding = 1,
		Distributing = 2,
		KeepMissingGames = 4,
		AssumeWalletKeyOnBadActivationCode = 8,
		All = Forwarding | Distributing | KeepMissingGames | AssumeWalletKeyOnBadActivationCode
	}

	[Flags]
	[PublicAPI]
	public enum ERemoteCommunication : byte {
		None = 0,
		SteamGroup = 1,
		PublicListing = 2,
		All = SteamGroup | PublicListing
	}

	[Flags]
	[PublicAPI]
	public enum ETradingPreferences : byte {
		None = 0,
		AcceptDonations = 1,
		SteamTradeMatcher = 2,
		MatchEverything = 4,
		DontAcceptBotTrades = 8,
		MatchActively = 16,
		All = AcceptDonations | SteamTradeMatcher | MatchEverything | DontAcceptBotTrades | MatchActively
	}
}
