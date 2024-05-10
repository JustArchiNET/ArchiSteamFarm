// ----------------------------------------------------------------------------------------------
//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// ----------------------------------------------------------------------------------------------
// |
// Copyright 2015-2024 Łukasz "JustArchi" Domeradzki
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
	public const byte DefaultHoursUntilCardDrops = 3;

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
	public const ArchiHandler.EUserInterfaceMode DefaultUserInterfaceMode = ArchiHandler.EUserInterfaceMode.Default;

	internal const byte SteamParentalCodeLength = 4;
	internal const byte SteamTradeTokenLength = 8;

	[PublicAPI]
	public static readonly ImmutableHashSet<EAssetType> DefaultCompleteTypesToSend = [];

	[PublicAPI]
	public static readonly ImmutableList<EFarmingOrder> DefaultFarmingOrders = [];

	[PublicAPI]
	public static readonly ImmutableList<uint> DefaultGamesPlayedWhileIdle = [];

	[PublicAPI]
	public static readonly ImmutableHashSet<EAssetType> DefaultLootableTypes = ImmutableHashSet.Create(EAssetType.BoosterPack, EAssetType.FoilTradingCard, EAssetType.TradingCard);

	[PublicAPI]
	public static readonly ImmutableHashSet<EAssetType> DefaultMatchableTypes = ImmutableHashSet.Create(EAssetType.TradingCard);

	[PublicAPI]
	public static readonly ImmutableDictionary<ulong, EAccess> DefaultSteamUserPermissions = ImmutableDictionary<ulong, EAccess>.Empty;

	[PublicAPI]
	public static readonly ImmutableHashSet<EAssetType> DefaultTransferableTypes = ImmutableHashSet.Create(EAssetType.BoosterPack, EAssetType.FoilTradingCard, EAssetType.TradingCard);

	[JsonInclude]
	public bool AcceptGifts { get; private init; } = DefaultAcceptGifts;

	[JsonInclude]
	public EBotBehaviour BotBehaviour { get; private init; } = DefaultBotBehaviour;

	[JsonDisallowNull]
	[JsonInclude]
	[SwaggerValidValues(ValidIntValues = [(int) EAssetType.FoilTradingCard, (int) EAssetType.TradingCard])]
	public ImmutableHashSet<EAssetType> CompleteTypesToSend { get; private init; } = DefaultCompleteTypesToSend;

	[JsonInclude]
	public string? CustomGamePlayedWhileFarming { get; private init; } = DefaultCustomGamePlayedWhileFarming;

	[JsonInclude]
	public string? CustomGamePlayedWhileIdle { get; private init; } = DefaultCustomGamePlayedWhileIdle;

	[JsonInclude]
	public bool Enabled { get; private init; } = DefaultEnabled;

	[JsonDisallowNull]
	[JsonInclude]
	public ImmutableList<EFarmingOrder> FarmingOrders { get; private init; } = DefaultFarmingOrders;

	[JsonInclude]
	public EFarmingPreferences FarmingPreferences { get; private init; } = DefaultFarmingPreferences;

	[JsonDisallowNull]
	[JsonInclude]
	[MaxLength(ArchiHandler.MaxGamesPlayedConcurrently)]
	[SwaggerItemsMinMax(MinimumUint = 1, MaximumUint = uint.MaxValue)]
	[UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL2026:RequiresUnreferencedCode", Justification = "This is optional, supportive attribute, we don't care if it gets trimmed or not")]
	public ImmutableList<uint> GamesPlayedWhileIdle { get; private init; } = DefaultGamesPlayedWhileIdle;

	[JsonInclude]
	[Range(byte.MinValue, byte.MaxValue)]
	public byte HoursUntilCardDrops { get; private init; } = DefaultHoursUntilCardDrops;

	[JsonDisallowNull]
	[JsonInclude]
	public ImmutableHashSet<EAssetType> LootableTypes { get; private init; } = DefaultLootableTypes;

	[JsonDisallowNull]
	[JsonInclude]
	public ImmutableHashSet<EAssetType> MatchableTypes { get; private init; } = DefaultMatchableTypes;

	[JsonInclude]
	public EPersonaStateFlag OnlineFlags { get; private init; } = DefaultOnlineFlags;

	[JsonInclude]
	public EPersonaState OnlineStatus { get; private init; } = DefaultOnlineStatus;

	[JsonInclude]
	public ArchiCryptoHelper.ECryptoMethod PasswordFormat { get; internal set; } = DefaultPasswordFormat;

	[JsonInclude]
	public ERedeemingPreferences RedeemingPreferences { get; private init; } = DefaultRedeemingPreferences;

	[JsonInclude]
	public ERemoteCommunication RemoteCommunication { get; private init; } = DefaultRemoteCommunication;

	[JsonInclude]
	[Range(byte.MinValue, byte.MaxValue)]
	public byte SendTradePeriod { get; private init; } = DefaultSendTradePeriod;

	[JsonInclude]
	public string? SteamLogin {
		get => BackingSteamLogin;

		internal set {
			IsSteamLoginSet = true;
			BackingSteamLogin = value;
		}
	}

	[JsonInclude]
	[SwaggerSteamIdentifier(AccountType = EAccountType.Clan)]
	[SwaggerValidValues(ValidIntValues = [0])]
	public ulong SteamMasterClanID { get; private init; } = DefaultSteamMasterClanID;

	[JsonInclude]
	[MaxLength(SteamParentalCodeLength)]
	[MinLength(SteamParentalCodeLength)]
	[SwaggerValidValues(ValidStringValues = ["0"])]
	[UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL2026:RequiresUnreferencedCode", Justification = "This is optional, supportive attribute, we don't care if it gets trimmed or not")]
	public string? SteamParentalCode {
		get => BackingSteamParentalCode;

		internal set {
			IsSteamParentalCodeSet = true;
			BackingSteamParentalCode = value;
		}
	}

	[JsonInclude]
	[SwaggerSecurityCritical]
	public string? SteamPassword {
		get => BackingSteamPassword;

		internal set {
			IsSteamPasswordSet = true;
			BackingSteamPassword = value;
		}
	}

	[JsonInclude]
	[MaxLength(SteamTradeTokenLength)]
	[MinLength(SteamTradeTokenLength)]
	[UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL2026:RequiresUnreferencedCode", Justification = "This is optional, supportive attribute, we don't care if it gets trimmed or not")]
	public string? SteamTradeToken { get; private init; } = DefaultSteamTradeToken;

	[JsonDisallowNull]
	[JsonInclude]
	public ImmutableDictionary<ulong, EAccess> SteamUserPermissions { get; private init; } = DefaultSteamUserPermissions;

	[JsonInclude]
	[Range(byte.MinValue, byte.MaxValue)]
	public byte TradeCheckPeriod { get; private init; } = DefaultTradeCheckPeriod;

	[JsonInclude]
	public ETradingPreferences TradingPreferences { get; private init; } = DefaultTradingPreferences;

	[JsonDisallowNull]
	[JsonInclude]
	public ImmutableHashSet<EAssetType> TransferableTypes { get; private init; } = DefaultTransferableTypes;

	[JsonInclude]
	public bool UseLoginKeys { get; private init; } = DefaultUseLoginKeys;

	[JsonInclude]
	public ArchiHandler.EUserInterfaceMode UserInterfaceMode { get; private init; } = DefaultUserInterfaceMode;

	[JsonExtensionData]
	[JsonInclude]
	internal Dictionary<string, JsonElement>? AdditionalProperties { get; set; }

	internal bool IsSteamLoginSet { get; set; }
	internal bool IsSteamParentalCodeSet { get; set; }
	internal bool IsSteamPasswordSet { get; set; }
	internal bool Saving { get; set; }

	private string? BackingSteamLogin = DefaultSteamLogin;
	private string? BackingSteamParentalCode = DefaultSteamParentalCode;
	private string? BackingSteamPassword = DefaultSteamPassword;

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
	internal BotConfig() { }

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
	public bool ShouldSerializeHoursUntilCardDrops() => !Saving || (HoursUntilCardDrops != DefaultHoursUntilCardDrops);

	[UsedImplicitly]
	public bool ShouldSerializeLootableTypes() => !Saving || ((LootableTypes != DefaultLootableTypes) && !LootableTypes.SetEquals(DefaultLootableTypes));

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

	[PublicAPI]
	public static async Task<bool> Write(string filePath, BotConfig botConfig) {
		ArgumentException.ThrowIfNullOrEmpty(filePath);
		ArgumentNullException.ThrowIfNull(botConfig);

		string json = botConfig.ToJsonText(true);

		return await SerializableFile.Write(filePath, json).ConfigureAwait(false);
	}

	internal (bool Valid, string? ErrorMessage) CheckValidation() {
		if (BotBehaviour > EBotBehaviour.All) {
			return (false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorConfigPropertyInvalid, nameof(BotBehaviour), BotBehaviour));
		}

		if (!string.IsNullOrEmpty(CustomGamePlayedWhileFarming)) {
			try {
				// Test CustomGamePlayedWhileFarming against supported format, otherwise we'll throw later when used
				string _ = string.Format(CultureInfo.CurrentCulture, CustomGamePlayedWhileFarming, null, null);
			} catch (FormatException e) {
				return (false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorConfigPropertyInvalid, nameof(CustomGamePlayedWhileFarming), e.Message));
			}
		}

		foreach (EFarmingOrder farmingOrder in FarmingOrders.Where(static farmingOrder => !Enum.IsDefined(farmingOrder))) {
			return (false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorConfigPropertyInvalid, nameof(FarmingOrders), farmingOrder));
		}

		if (GamesPlayedWhileIdle.Contains(0)) {
			return (false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorConfigPropertyInvalid, nameof(GamesPlayedWhileIdle), 0));
		}

		if (GamesPlayedWhileIdle.Count > ArchiHandler.MaxGamesPlayedConcurrently) {
			return (false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorConfigPropertyInvalid, nameof(GamesPlayedWhileIdle), $"{nameof(GamesPlayedWhileIdle.Count)} {GamesPlayedWhileIdle.Count} > {ArchiHandler.MaxGamesPlayedConcurrently}"));
		}

		foreach (EAssetType lootableType in LootableTypes.Where(static lootableType => !Enum.IsDefined(lootableType))) {
			return (false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorConfigPropertyInvalid, nameof(LootableTypes), lootableType));
		}

		HashSet<EAssetType>? completeTypesToSendValidTypes = null;

		foreach (EAssetType completableType in CompleteTypesToSend) {
			if (!Enum.IsDefined(completableType)) {
				return (false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorConfigPropertyInvalid, nameof(CompleteTypesToSend), completableType));
			}

			if (completeTypesToSendValidTypes == null) {
				SwaggerValidValuesAttribute? completeTypesToSendValidValues = typeof(BotConfig).GetProperty(nameof(CompleteTypesToSend))?.GetCustomAttribute<SwaggerValidValuesAttribute>();

				if (completeTypesToSendValidValues?.ValidIntValues == null) {
					throw new InvalidOperationException(nameof(completeTypesToSendValidValues));
				}

				completeTypesToSendValidTypes = completeTypesToSendValidValues.ValidIntValues.Select(static value => (EAssetType) value).ToHashSet();
			}

			if (!completeTypesToSendValidTypes.Contains(completableType)) {
				return (false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorConfigPropertyInvalid, nameof(CompleteTypesToSend), completableType));
			}
		}

		foreach (EAssetType matchableType in MatchableTypes.Where(static matchableType => !Enum.IsDefined(matchableType))) {
			return (false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorConfigPropertyInvalid, nameof(MatchableTypes), matchableType));
		}

		if (OnlineFlags < 0) {
			return (false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorConfigPropertyInvalid, nameof(OnlineFlags), OnlineFlags));
		}

		if (!Enum.IsDefined(OnlineStatus)) {
			return (false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorConfigPropertyInvalid, nameof(OnlineStatus), OnlineStatus));
		}

		if (!Enum.IsDefined(PasswordFormat)) {
			return (false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorConfigPropertyInvalid, nameof(PasswordFormat), PasswordFormat));
		}

		if (RedeemingPreferences > ERedeemingPreferences.All) {
			return (false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorConfigPropertyInvalid, nameof(RedeemingPreferences), RedeemingPreferences));
		}

		if ((SteamMasterClanID != 0) && !new SteamID(SteamMasterClanID).IsClanAccount) {
			return (false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorConfigPropertyInvalid, nameof(SteamMasterClanID), SteamMasterClanID));
		}

		if (!string.IsNullOrEmpty(SteamParentalCode) && ((SteamParentalCode.Length != SteamParentalCodeLength) || SteamParentalCode.Any(static character => character is < '0' or > '9'))) {
			return (false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorConfigPropertyInvalid, nameof(SteamParentalCode), SteamParentalCode));
		}

		if (!string.IsNullOrEmpty(SteamTradeToken) && (SteamTradeToken.Length != SteamTradeTokenLength)) {
			return (false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorConfigPropertyInvalid, nameof(SteamTradeToken), SteamTradeToken));
		}

		foreach ((ulong steamID, EAccess permission) in SteamUserPermissions) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
				return (false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorConfigPropertyInvalid, nameof(SteamUserPermissions), steamID));
			}

			if (!Enum.IsDefined(permission)) {
				return (false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorConfigPropertyInvalid, nameof(SteamUserPermissions), permission));
			}
		}

		if (TradingPreferences > ETradingPreferences.All) {
			return (false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorConfigPropertyInvalid, nameof(TradingPreferences), TradingPreferences));
		}

		return !Enum.IsDefined(UserInterfaceMode) ? (false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorConfigPropertyInvalid, nameof(UserInterfaceMode), UserInterfaceMode)) : (true, null);
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
			ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, nameof(SteamPassword)));

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
				ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(json)));

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
				ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningDefaultCryptKeyUsedForEncryption, botConfig.PasswordFormat, nameof(SteamPassword)));

				break;
			case ArchiCryptoHelper.ECryptoMethod.ProtectedDataForCurrentUser when ArchiCryptoHelper.HasDefaultCryptKey:
				ASF.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.WarningDefaultCryptKeyUsedForHashing, botConfig.PasswordFormat, nameof(SteamPassword)));

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
		All = RejectInvalidFriendInvites | RejectInvalidTrades | RejectInvalidGroupInvites | DismissInventoryNotifications | MarkReceivedMessagesAsRead | MarkBotMessagesAsRead
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
	public enum EFarmingPreferences : byte {
		None = 0,
		FarmingPausedByDefault = 1,
		ShutdownOnFarmingFinished = 2,
		SendOnFarmingFinished = 4,
		FarmPriorityQueueOnly = 8,
		SkipRefundableGames = 16,
		SkipUnplayedGames = 32,
		EnableRiskyCardsDiscovery = 64,
		AutoSteamSaleEvent = 128,
		All = FarmingPausedByDefault | ShutdownOnFarmingFinished | SendOnFarmingFinished | FarmPriorityQueueOnly | SkipRefundableGames | SkipUnplayedGames | EnableRiskyCardsDiscovery | AutoSteamSaleEvent
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
