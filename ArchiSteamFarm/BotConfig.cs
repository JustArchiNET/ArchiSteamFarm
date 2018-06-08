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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Json;
using ArchiSteamFarm.Localization;
using Newtonsoft.Json;

namespace ArchiSteamFarm {
	[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
	internal sealed class BotConfig {
		private static readonly SemaphoreSlim WriteSemaphore = new SemaphoreSlim(1, 1);

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool AcceptGifts;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool AutoSteamSaleEvent;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly EBotBehaviour BotBehaviour = EBotBehaviour.None;

		[JsonProperty]
		internal readonly string CustomGamePlayedWhileFarming;

		[JsonProperty]
		internal readonly string CustomGamePlayedWhileIdle;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool DismissInventoryNotifications;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool Enabled;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly EFarmingOrder FarmingOrder = EFarmingOrder.Unordered;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool FarmOffline;

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

		[JsonProperty]
		internal string SteamLogin { get; set; }

		[JsonProperty(Required = Required.DisallowNull)]
		internal ulong SteamMasterClanID { get; private set; }

		[JsonProperty]
		internal string SteamParentalPIN { get; set; } = "0";

		[JsonProperty]
		internal string SteamPassword { get; set; }

		private bool ShouldSerializeSensitiveDetails = true;

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

			botConfig.ShouldSerializeSensitiveDetails = false;

			// Support encrypted passwords
			if ((botConfig.PasswordFormat != CryptoHelper.ECryptoMethod.PlainText) && !string.IsNullOrEmpty(botConfig.SteamPassword)) {
				// In worst case password will result in null, which will have to be corrected by user during runtime
				botConfig.SteamPassword = CryptoHelper.Decrypt(botConfig.PasswordFormat, botConfig.SteamPassword);
			}

			// User might not know what he's doing
			// Ensure that he can't screw core ASF variables
			if (botConfig.GamesPlayedWhileIdle.Count <= ArchiHandler.MaxGamesPlayedConcurrently) {
				return botConfig;
			}

			ASF.ArchiLogger.LogGenericWarning(string.Format(Strings.WarningTooManyGamesToPlay, ArchiHandler.MaxGamesPlayedConcurrently, nameof(botConfig.GamesPlayedWhileIdle)));

			HashSet<uint> validGames = botConfig.GamesPlayedWhileIdle.Take(ArchiHandler.MaxGamesPlayedConcurrently).ToHashSet();
			botConfig.GamesPlayedWhileIdle.IntersectWith(validGames);
			botConfig.GamesPlayedWhileIdle.TrimExcess();

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
			RejectInvalidGroupInvites = 4
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
			RedeemDateTimesDescending
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
			KeepMissingGames = 4
		}

		[Flags]
		internal enum ETradingPreferences : byte {
			None = 0,
			AcceptDonations = 1,
			SteamTradeMatcher = 2,
			MatchEverything = 4,
			DontAcceptBotTrades = 8
		}
	}
}