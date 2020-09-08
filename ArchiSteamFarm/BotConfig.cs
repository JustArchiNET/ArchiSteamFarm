//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2020 Łukasz "JustArchi" Domeradzki
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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Json;
using ArchiSteamFarm.Localization;
using JetBrains.Annotations;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SteamKit2;

namespace ArchiSteamFarm {
	[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
	public sealed class BotConfig {
		[PublicAPI]
		public const bool DefaultAcceptGifts = false;

		[PublicAPI]
		public const bool DefaultAutoSteamSaleEvent = false;

		[PublicAPI]
		public const EBotBehaviour DefaultBotBehaviour = EBotBehaviour.None;

		[PublicAPI]
		public const string DefaultCustomGamePlayedWhileFarming = null;

		[PublicAPI]
		public const string DefaultCustomGamePlayedWhileIdle = null;

		[PublicAPI]
		public const bool DefaultEnabled = false;

		[PublicAPI]
		public const byte DefaultHoursUntilCardDrops = 3;

		[PublicAPI]
		public const bool DefaultIdlePriorityQueueOnly = false;

		[PublicAPI]
		public const bool DefaultIdleRefundableGames = true;

		[PublicAPI]
		public const EPersonaState DefaultOnlineStatus = EPersonaState.Online;

		[PublicAPI]
		public const ArchiCryptoHelper.ECryptoMethod DefaultPasswordFormat = ArchiCryptoHelper.ECryptoMethod.PlainText;

		[PublicAPI]
		public const bool DefaultPaused = false;

		[PublicAPI]
		public const ERedeemingPreferences DefaultRedeemingPreferences = ERedeemingPreferences.None;

		[PublicAPI]
		public const bool DefaultSendOnFarmingFinished = false;

		[PublicAPI]
		public const byte DefaultSendTradePeriod = 0;

		[PublicAPI]
		public const bool DefaultShutdownOnFarmingFinished = false;

		[PublicAPI]
		public const string DefaultSteamLogin = null;

		[PublicAPI]
		public const ulong DefaultSteamMasterClanID = 0;

		[PublicAPI]
		public const string DefaultSteamParentalCode = null;

		[PublicAPI]
		public const string DefaultSteamPassword = null;

		[PublicAPI]
		public const string DefaultSteamTradeToken = null;

		[PublicAPI]
		public const ETradingPreferences DefaultTradingPreferences = ETradingPreferences.None;

		[PublicAPI]
		public const bool DefaultUseLoginKeys = true;

		internal const byte SteamParentalCodeLength = 4;

		private const byte SteamTradeTokenLength = 8;

		[PublicAPI]
		public static readonly ImmutableList<EFarmingOrder> DefaultFarmingOrders = ImmutableList<EFarmingOrder>.Empty;

		[PublicAPI]
		public static readonly ImmutableHashSet<uint> DefaultGamesPlayedWhileIdle = ImmutableHashSet<uint>.Empty;

		[PublicAPI]
		public static readonly ImmutableHashSet<Steam.Asset.EType> DefaultLootableTypes = ImmutableHashSet.Create(Steam.Asset.EType.BoosterPack, Steam.Asset.EType.FoilTradingCard, Steam.Asset.EType.TradingCard);

		[PublicAPI]
		public static readonly ImmutableHashSet<Steam.Asset.EType> DefaultMatchableTypes = ImmutableHashSet.Create(Steam.Asset.EType.TradingCard);

		[PublicAPI]
		public static readonly ImmutableDictionary<ulong, EPermission> DefaultSteamUserPermissions = ImmutableDictionary<ulong, EPermission>.Empty;

		[PublicAPI]
		public static readonly ImmutableHashSet<Steam.Asset.EType> DefaultTransferableTypes = ImmutableHashSet.Create(Steam.Asset.EType.BoosterPack, Steam.Asset.EType.FoilTradingCard, Steam.Asset.EType.TradingCard);

		private static readonly SemaphoreSlim WriteSemaphore = new SemaphoreSlim(1, 1);

		[JsonProperty(Required = Required.DisallowNull)]
		public readonly bool AcceptGifts = DefaultAcceptGifts;

		[JsonProperty(Required = Required.DisallowNull)]
		public readonly bool AutoSteamSaleEvent = DefaultAutoSteamSaleEvent;

		[JsonProperty(Required = Required.DisallowNull)]
		public readonly EBotBehaviour BotBehaviour = DefaultBotBehaviour;

		[JsonProperty]
		public readonly string? CustomGamePlayedWhileFarming = DefaultCustomGamePlayedWhileFarming;

		[JsonProperty]
		public readonly string? CustomGamePlayedWhileIdle = DefaultCustomGamePlayedWhileIdle;

		[JsonProperty(Required = Required.DisallowNull)]
		public readonly bool Enabled = DefaultEnabled;

		[JsonProperty(Required = Required.DisallowNull)]
		public readonly ImmutableList<EFarmingOrder> FarmingOrders = DefaultFarmingOrders;

		[JsonProperty(Required = Required.DisallowNull)]
		public readonly ImmutableHashSet<uint> GamesPlayedWhileIdle = DefaultGamesPlayedWhileIdle;

		[JsonProperty(Required = Required.DisallowNull)]
		public readonly byte HoursUntilCardDrops = DefaultHoursUntilCardDrops;

		[JsonProperty(Required = Required.DisallowNull)]
		public readonly bool IdlePriorityQueueOnly = DefaultIdlePriorityQueueOnly;

		[JsonProperty(Required = Required.DisallowNull)]
		public readonly bool IdleRefundableGames = DefaultIdleRefundableGames;

		[JsonProperty(Required = Required.DisallowNull)]
		public readonly ImmutableHashSet<Steam.Asset.EType> LootableTypes = DefaultLootableTypes;

		[JsonProperty(Required = Required.DisallowNull)]
		public readonly ImmutableHashSet<Steam.Asset.EType> MatchableTypes = DefaultMatchableTypes;

		[JsonProperty(Required = Required.DisallowNull)]
		public readonly EPersonaState OnlineStatus = DefaultOnlineStatus;

		[JsonProperty(Required = Required.DisallowNull)]
		public readonly ArchiCryptoHelper.ECryptoMethod PasswordFormat = DefaultPasswordFormat;

		[JsonProperty(Required = Required.DisallowNull)]
		public readonly bool Paused = DefaultPaused;

		[JsonProperty(Required = Required.DisallowNull)]
		public readonly ERedeemingPreferences RedeemingPreferences = DefaultRedeemingPreferences;

		[JsonProperty(Required = Required.DisallowNull)]
		public readonly bool SendOnFarmingFinished = DefaultSendOnFarmingFinished;

		[JsonProperty(Required = Required.DisallowNull)]
		public readonly byte SendTradePeriod = DefaultSendTradePeriod;

		[JsonProperty(Required = Required.DisallowNull)]
		public readonly bool ShutdownOnFarmingFinished = DefaultShutdownOnFarmingFinished;

		[JsonProperty]
		public readonly string? SteamTradeToken = DefaultSteamTradeToken;

		[JsonProperty(Required = Required.DisallowNull)]
		public readonly ImmutableDictionary<ulong, EPermission> SteamUserPermissions = DefaultSteamUserPermissions;

		[JsonProperty(Required = Required.DisallowNull)]
		public readonly ETradingPreferences TradingPreferences = DefaultTradingPreferences;

		[JsonProperty(Required = Required.DisallowNull)]
		public readonly ImmutableHashSet<Steam.Asset.EType> TransferableTypes = DefaultTransferableTypes;

		[JsonProperty(Required = Required.DisallowNull)]
		public readonly bool UseLoginKeys = DefaultUseLoginKeys;

		[JsonProperty(Required = Required.DisallowNull)]
		public ulong SteamMasterClanID { get; private set; } = DefaultSteamMasterClanID;

		[JsonExtensionData]
		internal Dictionary<string, JToken>? AdditionalProperties {
			get;
			[UsedImplicitly]
			set;
		}

		internal string? DecryptedSteamPassword {
			get {
				if (string.IsNullOrEmpty(SteamPassword)) {
					return null;
				}

				if (PasswordFormat == ArchiCryptoHelper.ECryptoMethod.PlainText) {
					return SteamPassword;
				}

				string? result = ArchiCryptoHelper.Decrypt(PasswordFormat, SteamPassword!);

				if (string.IsNullOrEmpty(result)) {
					ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorIsInvalid, nameof(SteamPassword)));

					return null;
				}

				return result;
			}

			set {
				if (!string.IsNullOrEmpty(value) && (PasswordFormat != ArchiCryptoHelper.ECryptoMethod.PlainText)) {
					value = ArchiCryptoHelper.Encrypt(PasswordFormat, value!);
				}

				SteamPassword = value;
			}
		}

		internal bool IsSteamLoginSet { get; private set; }
		internal bool IsSteamParentalCodeSet { get; private set; }
		internal bool IsSteamPasswordSet { get; private set; }
		internal bool ShouldSerializeDefaultValues { private get; set; } = true;
		internal bool ShouldSerializeHelperProperties { private get; set; } = true;
		internal bool ShouldSerializeSensitiveDetails { private get; set; }

		[JsonProperty]
		internal string? SteamLogin {
			get => BackingSteamLogin;

			set {
				IsSteamLoginSet = true;
				BackingSteamLogin = value;
			}
		}

		[JsonProperty]
		internal string? SteamParentalCode {
			get => BackingSteamParentalCode;

			set {
				IsSteamParentalCodeSet = true;
				BackingSteamParentalCode = value;
			}
		}

		[JsonProperty]
		internal string? SteamPassword {
			get => BackingSteamPassword;

			set {
				IsSteamPasswordSet = true;
				BackingSteamPassword = value;
			}
		}

		private string? BackingSteamLogin = DefaultSteamLogin;
		private string? BackingSteamParentalCode = DefaultSteamParentalCode;
		private string? BackingSteamPassword = DefaultSteamPassword;

		[JsonProperty(PropertyName = SharedInfo.UlongCompatibilityStringPrefix + nameof(SteamMasterClanID), Required = Required.DisallowNull)]

		private string SSteamMasterClanID {
			get => SteamMasterClanID.ToString();

			set {
				if (string.IsNullOrEmpty(value) || !ulong.TryParse(value, out ulong result)) {
					ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorIsInvalid, nameof(SSteamMasterClanID)));

					return;
				}

				SteamMasterClanID = result;
			}
		}

		[JsonConstructor]
		private BotConfig() { }

		internal (bool Valid, string? ErrorMessage) CheckValidation() {
			if (BotBehaviour > EBotBehaviour.All) {
				return (false, string.Format(Strings.ErrorConfigPropertyInvalid, nameof(BotBehaviour), BotBehaviour));
			}

			foreach (EFarmingOrder farmingOrder in FarmingOrders.Where(farmingOrder => !Enum.IsDefined(typeof(EFarmingOrder), farmingOrder))) {
				return (false, string.Format(Strings.ErrorConfigPropertyInvalid, nameof(FarmingOrders), farmingOrder));
			}

			if (GamesPlayedWhileIdle.Count > ArchiHandler.MaxGamesPlayedConcurrently) {
				return (false, string.Format(Strings.ErrorConfigPropertyInvalid, nameof(GamesPlayedWhileIdle), GamesPlayedWhileIdle.Count + " > " + ArchiHandler.MaxGamesPlayedConcurrently));
			}

			foreach (Steam.Asset.EType lootableType in LootableTypes.Where(lootableType => !Enum.IsDefined(typeof(Steam.Asset.EType), lootableType))) {
				return (false, string.Format(Strings.ErrorConfigPropertyInvalid, nameof(LootableTypes), lootableType));
			}

			foreach (Steam.Asset.EType matchableType in MatchableTypes.Where(matchableType => !Enum.IsDefined(typeof(Steam.Asset.EType), matchableType))) {
				return (false, string.Format(Strings.ErrorConfigPropertyInvalid, nameof(MatchableTypes), matchableType));
			}

			if (!Enum.IsDefined(typeof(EPersonaState), OnlineStatus)) {
				return (false, string.Format(Strings.ErrorConfigPropertyInvalid, nameof(OnlineStatus), OnlineStatus));
			}

			if (!Enum.IsDefined(typeof(ArchiCryptoHelper.ECryptoMethod), PasswordFormat)) {
				return (false, string.Format(Strings.ErrorConfigPropertyInvalid, nameof(PasswordFormat), PasswordFormat));
			}

			if (RedeemingPreferences > ERedeemingPreferences.All) {
				return (false, string.Format(Strings.ErrorConfigPropertyInvalid, nameof(RedeemingPreferences), RedeemingPreferences));
			}

			if ((SteamMasterClanID != 0) && !new SteamID(SteamMasterClanID).IsClanAccount) {
				return (false, string.Format(Strings.ErrorConfigPropertyInvalid, nameof(SteamMasterClanID), SteamMasterClanID));
			}

			if (!string.IsNullOrEmpty(SteamParentalCode) && (SteamParentalCode != "0") && (SteamParentalCode!.Length != SteamParentalCodeLength)) {
				return (false, string.Format(Strings.ErrorConfigPropertyInvalid, nameof(SteamParentalCode), SteamParentalCode));
			}

			if (!string.IsNullOrEmpty(SteamTradeToken) && (SteamTradeToken!.Length != SteamTradeTokenLength)) {
				return (false, string.Format(Strings.ErrorConfigPropertyInvalid, nameof(SteamTradeToken), SteamTradeToken));
			}

			foreach ((ulong steamID, EPermission permission) in SteamUserPermissions) {
				if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
					return (false, string.Format(Strings.ErrorConfigPropertyInvalid, nameof(SteamUserPermissions), steamID));
				}

				if (!Enum.IsDefined(typeof(EPermission), permission)) {
					return (false, string.Format(Strings.ErrorConfigPropertyInvalid, nameof(SteamUserPermissions), permission));
				}
			}

			return TradingPreferences <= ETradingPreferences.All ? (true, (string?) null) : (false, string.Format(Strings.ErrorConfigPropertyInvalid, nameof(TradingPreferences), TradingPreferences));
		}

		internal static async Task<BotConfig?> Load(string filePath) {
			if (string.IsNullOrEmpty(filePath)) {
				throw new ArgumentNullException(nameof(filePath));
			}

			if (!File.Exists(filePath)) {
				return null;
			}

			BotConfig botConfig;

			try {
				string json = await RuntimeCompatibility.File.ReadAllTextAsync(filePath).ConfigureAwait(false);

				if (string.IsNullOrEmpty(json)) {
					ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorIsEmpty, nameof(json)));

					return null;
				}

				botConfig = JsonConvert.DeserializeObject<BotConfig>(json);
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);

				return null;
			}

			// ReSharper disable once ConditionIsAlwaysTrueOrFalse - wrong, "null" json serializes into null object
			if (botConfig == null) {
				ASF.ArchiLogger.LogNullError(nameof(botConfig));

				return null;
			}

			(bool valid, string? errorMessage) = botConfig.CheckValidation();

			if (!valid) {
				if (!string.IsNullOrEmpty(errorMessage)) {
					ASF.ArchiLogger.LogGenericError(errorMessage!);
				}

				return null;
			}

			return botConfig;
		}

		internal static async Task<bool> Write(string filePath, BotConfig botConfig) {
			if (string.IsNullOrEmpty(filePath) || (botConfig == null)) {
				throw new ArgumentNullException(nameof(filePath) + " || " + nameof(botConfig));
			}

			string json = JsonConvert.SerializeObject(botConfig, Formatting.Indented);
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

		[Flags]
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

		public enum EPermission : byte {
			None,
			FamilySharing,
			Operator,
			Master
		}

		[Flags]
		public enum ERedeemingPreferences : byte {
			None = 0,
			Forwarding = 1,
			Distributing = 2,
			KeepMissingGames = 4,
			AssumeWalletKeyOnBadActivationCode = 8,
			All = Forwarding | Distributing | KeepMissingGames | AssumeWalletKeyOnBadActivationCode
		}

		[Flags]
		public enum ETradingPreferences : byte {
			None = 0,
			AcceptDonations = 1,
			SteamTradeMatcher = 2,
			MatchEverything = 4,
			DontAcceptBotTrades = 8,
			MatchActively = 16,
			All = AcceptDonations | SteamTradeMatcher | MatchEverything | DontAcceptBotTrades | MatchActively
		}

		// ReSharper disable UnusedMember.Global
		public bool ShouldSerializeAcceptGifts() => ShouldSerializeDefaultValues || (AcceptGifts != DefaultAcceptGifts);
		public bool ShouldSerializeAutoSteamSaleEvent() => ShouldSerializeDefaultValues || (AutoSteamSaleEvent != DefaultAutoSteamSaleEvent);
		public bool ShouldSerializeBotBehaviour() => ShouldSerializeDefaultValues || (BotBehaviour != DefaultBotBehaviour);
		public bool ShouldSerializeCustomGamePlayedWhileFarming() => ShouldSerializeDefaultValues || (CustomGamePlayedWhileFarming != DefaultCustomGamePlayedWhileFarming);
		public bool ShouldSerializeCustomGamePlayedWhileIdle() => ShouldSerializeDefaultValues || (CustomGamePlayedWhileIdle != DefaultCustomGamePlayedWhileIdle);
		public bool ShouldSerializeEnabled() => ShouldSerializeDefaultValues || (Enabled != DefaultEnabled);
		public bool ShouldSerializeFarmingOrders() => ShouldSerializeDefaultValues || ((FarmingOrders != DefaultFarmingOrders) && !FarmingOrders.SequenceEqual(DefaultFarmingOrders));
		public bool ShouldSerializeGamesPlayedWhileIdle() => ShouldSerializeDefaultValues || ((GamesPlayedWhileIdle != DefaultGamesPlayedWhileIdle) && !GamesPlayedWhileIdle.SetEquals(DefaultGamesPlayedWhileIdle));
		public bool ShouldSerializeHoursUntilCardDrops() => ShouldSerializeDefaultValues || (HoursUntilCardDrops != DefaultHoursUntilCardDrops);
		public bool ShouldSerializeIdlePriorityQueueOnly() => ShouldSerializeDefaultValues || (IdlePriorityQueueOnly != DefaultIdlePriorityQueueOnly);
		public bool ShouldSerializeIdleRefundableGames() => ShouldSerializeDefaultValues || (IdleRefundableGames != DefaultIdleRefundableGames);
		public bool ShouldSerializeLootableTypes() => ShouldSerializeDefaultValues || ((LootableTypes != DefaultLootableTypes) && !LootableTypes.SetEquals(DefaultLootableTypes));
		public bool ShouldSerializeMatchableTypes() => ShouldSerializeDefaultValues || ((MatchableTypes != DefaultMatchableTypes) && !MatchableTypes.SetEquals(DefaultMatchableTypes));
		public bool ShouldSerializeOnlineStatus() => ShouldSerializeDefaultValues || (OnlineStatus != DefaultOnlineStatus);
		public bool ShouldSerializePasswordFormat() => ShouldSerializeDefaultValues || (PasswordFormat != DefaultPasswordFormat);
		public bool ShouldSerializePaused() => ShouldSerializeDefaultValues || (Paused != DefaultPaused);
		public bool ShouldSerializeRedeemingPreferences() => ShouldSerializeDefaultValues || (RedeemingPreferences != DefaultRedeemingPreferences);
		public bool ShouldSerializeSendOnFarmingFinished() => ShouldSerializeDefaultValues || (SendOnFarmingFinished != DefaultSendOnFarmingFinished);
		public bool ShouldSerializeSendTradePeriod() => ShouldSerializeDefaultValues || (SendTradePeriod != DefaultSendTradePeriod);
		public bool ShouldSerializeShutdownOnFarmingFinished() => ShouldSerializeDefaultValues || (ShutdownOnFarmingFinished != DefaultShutdownOnFarmingFinished);
		public bool ShouldSerializeSSteamMasterClanID() => ShouldSerializeDefaultValues || (ShouldSerializeHelperProperties && (SteamMasterClanID != DefaultSteamMasterClanID));
		public bool ShouldSerializeSteamLogin() => ShouldSerializeSensitiveDetails && (ShouldSerializeDefaultValues || (SteamLogin != DefaultSteamLogin));
		public bool ShouldSerializeSteamMasterClanID() => ShouldSerializeDefaultValues || (SteamMasterClanID != DefaultSteamMasterClanID);
		public bool ShouldSerializeSteamParentalCode() => ShouldSerializeSensitiveDetails && (ShouldSerializeDefaultValues || (SteamParentalCode != DefaultSteamParentalCode));
		public bool ShouldSerializeSteamPassword() => ShouldSerializeSensitiveDetails && (ShouldSerializeDefaultValues || (SteamPassword != DefaultSteamPassword));
		public bool ShouldSerializeSteamTradeToken() => ShouldSerializeDefaultValues || (SteamTradeToken != DefaultSteamTradeToken);
		public bool ShouldSerializeSteamUserPermissions() => ShouldSerializeDefaultValues || ((SteamUserPermissions != DefaultSteamUserPermissions) && ((SteamUserPermissions.Count != DefaultSteamUserPermissions.Count) || SteamUserPermissions.Except(DefaultSteamUserPermissions).Any()));
		public bool ShouldSerializeTradingPreferences() => ShouldSerializeDefaultValues || (TradingPreferences != DefaultTradingPreferences);
		public bool ShouldSerializeTransferableTypes() => ShouldSerializeDefaultValues || ((TransferableTypes != DefaultTransferableTypes) && !TransferableTypes.SetEquals(DefaultTransferableTypes));
		public bool ShouldSerializeUseLoginKeys() => ShouldSerializeDefaultValues || (UseLoginKeys != DefaultUseLoginKeys);

		// ReSharper restore UnusedMember.Global
	}
}
