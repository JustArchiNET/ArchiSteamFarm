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
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Json;
using ArchiSteamFarm.Localization;
using Newtonsoft.Json;
using SteamKit2;

namespace ArchiSteamFarm {
	[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
	internal sealed class BotConfig {
		private static readonly SemaphoreSlim WriteSemaphore = new SemaphoreSlim(1, 1);

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool AcceptGifts;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool AutoSteamSaleEvent;

		[JsonProperty]
		internal readonly string CustomGamePlayedWhileFarming;

		[JsonProperty]
		internal readonly string CustomGamePlayedWhileIdle;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool Enabled;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly HashSet<EFarmingOrder> FarmingOrders = new HashSet<EFarmingOrder>();

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly HashSet<uint> GamesPlayedWhileIdle = new HashSet<uint>();

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool HandleOfflineMessages;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly byte HoursUntilCardDrops = 3;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool IdlePriorityQueueOnly;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool IdleRefundableGames = true;

		[JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace, Required = Required.DisallowNull)]
		internal readonly HashSet<Steam.Asset.EType> LootableTypes = new HashSet<Steam.Asset.EType> {
			Steam.Asset.EType.BoosterPack,
			Steam.Asset.EType.FoilTradingCard,
			Steam.Asset.EType.TradingCard
		};

		[JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace, Required = Required.DisallowNull)]
		internal readonly HashSet<Steam.Asset.EType> MatchableTypes = new HashSet<Steam.Asset.EType> { Steam.Asset.EType.TradingCard };

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly EPersonaState OnlineStatus = EPersonaState.Online;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly CryptoHelper.ECryptoMethod PasswordFormat = CryptoHelper.ECryptoMethod.PlainText;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool Paused;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly ERedeemingPreferences RedeemingPreferences = ERedeemingPreferences.None;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool SendOnFarmingFinished;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly byte SendTradePeriod;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool ShutdownOnFarmingFinished;

		[JsonProperty]
		internal readonly string SteamTradeToken;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly Dictionary<ulong, EPermission> SteamUserPermissions = new Dictionary<ulong, EPermission>();

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly ETradingPreferences TradingPreferences = ETradingPreferences.None;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool UseLoginKeys = true;

		[JsonProperty(Required = Required.DisallowNull)]
		internal EBotBehaviour BotBehaviour { get; private set; } = EBotBehaviour.None;

		[JsonProperty]
		internal string SteamLogin { get; set; }

		[JsonProperty(Required = Required.DisallowNull)]
		internal ulong SteamMasterClanID { get; private set; }

		[JsonProperty]
		internal string SteamParentalPIN { get; set; } = "0";

		[JsonProperty]
		internal string SteamPassword { get; set; }

		private bool ShouldSerializeSensitiveDetails = true;

		[JsonProperty(Required = Required.DisallowNull)]
		private bool DismissInventoryNotifications {
			set {
				ASF.ArchiLogger.LogGenericWarning(string.Format(Strings.WarningDeprecated, nameof(DismissInventoryNotifications), nameof(BotBehaviour)));

				if (value) {
					BotBehaviour |= EBotBehaviour.DismissInventoryNotifications;
				}
			}
		}

		[JsonProperty(Required = Required.DisallowNull)]
		private EFarmingOrder FarmingOrder {
			set {
				ASF.ArchiLogger.LogGenericWarning(string.Format(Strings.WarningDeprecated, nameof(FarmingOrder), nameof(FarmingOrders)));

				if (!Enum.IsDefined(typeof(EFarmingOrder), value)) {
					ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorConfigPropertyInvalid, nameof(FarmingOrder), value));
					return;
				}

				FarmingOrders.Add(value);
			}
		}

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

		public bool ShouldSerializeSteamLogin() => ShouldSerializeSensitiveDetails;
		public bool ShouldSerializeSteamParentalPIN() => ShouldSerializeSensitiveDetails;
		public bool ShouldSerializeSteamPassword() => ShouldSerializeSensitiveDetails;

		internal static async Task<BotConfig> Load(string filePath) {
			if (string.IsNullOrEmpty(filePath)) {
				ASF.ArchiLogger.LogNullError(nameof(filePath));
				return null;
			}

			if (!File.Exists(filePath)) {
				return null;
			}

			BotConfig botConfig;

			try {
				botConfig = JsonConvert.DeserializeObject<BotConfig>(await RuntimeCompatibility.File.ReadAllTextAsync(filePath).ConfigureAwait(false));
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);
				return null;
			}

			if (botConfig == null) {
				ASF.ArchiLogger.LogNullError(nameof(botConfig));
				return null;
			}

			if (botConfig.BotBehaviour > EBotBehaviour.All) {
				ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorConfigPropertyInvalid, nameof(botConfig.BotBehaviour), botConfig.BotBehaviour));
				return null;
			}

			foreach (EFarmingOrder farmingOrder in botConfig.FarmingOrders) {
				if (!Enum.IsDefined(typeof(EFarmingOrder), farmingOrder)) {
					ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorConfigPropertyInvalid, nameof(botConfig.FarmingOrders), farmingOrder));
					return null;
				}
			}

			if (botConfig.GamesPlayedWhileIdle.Count > ArchiHandler.MaxGamesPlayedConcurrently) {
				ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorConfigPropertyInvalid, nameof(botConfig.GamesPlayedWhileIdle), botConfig.GamesPlayedWhileIdle.Count + " > " + ArchiHandler.MaxGamesPlayedConcurrently));
				return null;
			}

			foreach (Steam.Asset.EType lootableType in botConfig.LootableTypes) {
				if (!Enum.IsDefined(typeof(Steam.Asset.EType), lootableType)) {
					ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorConfigPropertyInvalid, nameof(botConfig.LootableTypes), lootableType));
					return null;
				}
			}

			foreach (Steam.Asset.EType matchableType in botConfig.MatchableTypes) {
				if (!Enum.IsDefined(typeof(Steam.Asset.EType), matchableType)) {
					ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorConfigPropertyInvalid, nameof(botConfig.MatchableTypes), matchableType));
					return null;
				}
			}

			if ((botConfig.OnlineStatus < EPersonaState.Offline) || (botConfig.OnlineStatus >= EPersonaState.Max)) {
				ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorConfigPropertyInvalid, nameof(botConfig.OnlineStatus), botConfig.OnlineStatus));
				return null;
			}

			if (!Enum.IsDefined(typeof(CryptoHelper.ECryptoMethod), botConfig.PasswordFormat)) {
				ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorConfigPropertyInvalid, nameof(botConfig.PasswordFormat), botConfig.PasswordFormat));
				return null;
			}

			if (botConfig.RedeemingPreferences > ERedeemingPreferences.All) {
				ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorConfigPropertyInvalid, nameof(botConfig.RedeemingPreferences), botConfig.RedeemingPreferences));
				return null;
			}

			foreach (EPermission permission in botConfig.SteamUserPermissions.Values) {
				if (!Enum.IsDefined(typeof(EPermission), permission)) {
					ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorConfigPropertyInvalid, nameof(botConfig.SteamUserPermissions), permission));
					return null;
				}
			}

			if (botConfig.TradingPreferences > ETradingPreferences.All) {
				ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorConfigPropertyInvalid, nameof(botConfig.TradingPreferences), botConfig.TradingPreferences));
				return null;
			}

			// Support encrypted passwords
			if ((botConfig.PasswordFormat != CryptoHelper.ECryptoMethod.PlainText) && !string.IsNullOrEmpty(botConfig.SteamPassword)) {
				// In worst case password will result in null, which will have to be corrected by user during runtime
				botConfig.SteamPassword = CryptoHelper.Decrypt(botConfig.PasswordFormat, botConfig.SteamPassword);
			}

			botConfig.ShouldSerializeSensitiveDetails = false;
			return botConfig;
		}

		internal static async Task<bool> Write(string filePath, BotConfig botConfig) {
			if (string.IsNullOrEmpty(filePath) || (botConfig == null)) {
				ASF.ArchiLogger.LogNullError(nameof(filePath) + " || " + nameof(botConfig));
				return false;
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
		internal enum EBotBehaviour : byte {
			None = 0,
			RejectInvalidFriendInvites = 1,
			RejectInvalidTrades = 2,
			RejectInvalidGroupInvites = 4,
			DismissInventoryNotifications = 8,
			All = RejectInvalidFriendInvites | RejectInvalidTrades | RejectInvalidGroupInvites | DismissInventoryNotifications
		}

		internal enum EFarmingOrder : byte {
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

		internal enum EPermission : byte {
			None,
			FamilySharing,
			Operator,
			Master
		}

		[Flags]
		internal enum ERedeemingPreferences : byte {
			None = 0,
			Forwarding = 1,
			Distributing = 2,
			KeepMissingGames = 4,
			All = Forwarding | Distributing | KeepMissingGames
		}

		[Flags]
		internal enum ETradingPreferences : byte {
			None = 0,
			AcceptDonations = 1,
			SteamTradeMatcher = 2,
			MatchEverything = 4,
			DontAcceptBotTrades = 8,
			All = AcceptDonations | SteamTradeMatcher | MatchEverything | DontAcceptBotTrades
		}
	}
}