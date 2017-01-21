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
using System.Linq;
using ArchiSteamFarm.JSON;
using ArchiSteamFarm.Localization;
using Newtonsoft.Json;

namespace ArchiSteamFarm {
	[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
	[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
	[SuppressMessage("ReSharper", "ConvertToConstant.Local")]
	[SuppressMessage("ReSharper", "ConvertToConstant.Global")]
	internal sealed class BotConfig {
		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly byte AcceptConfirmationsPeriod = 0;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool AcceptGifts = false;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool CardDropsRestricted = true;

		[JsonProperty]
		internal readonly string CustomGamePlayedWhileFarming = null;

		[JsonProperty]
		internal readonly string CustomGamePlayedWhileIdle = null;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool DismissInventoryNotifications = true;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool Enabled = false;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly EFarmingOrder FarmingOrder = EFarmingOrder.Unordered;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool FarmOffline = false;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly HashSet<uint> GamesPlayedWhileIdle = new HashSet<uint>();

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool HandleOfflineMessages = false;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool IsBotAccount = false;

		[JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace, Required = Required.DisallowNull)]
		internal readonly HashSet<Steam.Item.EType> LootableTypes = new HashSet<Steam.Item.EType> {
			Steam.Item.EType.BoosterPack,
			Steam.Item.EType.FoilTradingCard,
			Steam.Item.EType.TradingCard
		};

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly CryptoHelper.ECryptoMethod PasswordFormat = CryptoHelper.ECryptoMethod.PlainText;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool Paused = false;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly ERedeemingPreferences RedeemingPreferences = ERedeemingPreferences.None;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool SendOnFarmingFinished = false;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly byte SendTradePeriod = 0;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool ShutdownOnFarmingFinished = false;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly ulong SteamMasterClanID = 0;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly ulong SteamMasterID = 0;

		[JsonProperty]
		internal readonly string SteamTradeToken = null;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly ETradingPreferences TradingPreferences = ETradingPreferences.AcceptDonations;

		[JsonProperty]
		internal string SteamLogin { get; set; }

		[JsonProperty]
		internal string SteamParentalPIN { get; set; } = "0";

		[JsonProperty]
		internal string SteamPassword { get; set; }

		// This constructor is used only by deserializer
		private BotConfig() { }

		internal static BotConfig Load(string filePath) {
			if (string.IsNullOrEmpty(filePath)) {
				Program.ArchiLogger.LogNullError(nameof(filePath));
				return null;
			}

			if (!File.Exists(filePath)) {
				return null;
			}

			BotConfig botConfig;

			try {
				botConfig = JsonConvert.DeserializeObject<BotConfig>(File.ReadAllText(filePath));
			} catch (Exception e) {
				Program.ArchiLogger.LogGenericException(e);
				return null;
			}

			if (botConfig == null) {
				Program.ArchiLogger.LogNullError(nameof(botConfig));
				return null;
			}

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

			Program.ArchiLogger.LogGenericWarning(string.Format(Strings.WarningTooManyGamesToPlay, ArchiHandler.MaxGamesPlayedConcurrently, nameof(botConfig.GamesPlayedWhileIdle)));

			HashSet<uint> validGames = new HashSet<uint>(botConfig.GamesPlayedWhileIdle.Take(ArchiHandler.MaxGamesPlayedConcurrently));
			botConfig.GamesPlayedWhileIdle.IntersectWith(validGames);
			botConfig.GamesPlayedWhileIdle.TrimExcess();

			return botConfig;
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
			NamesDescending
		}

		[Flags]
		internal enum ERedeemingPreferences : byte {
			[SuppressMessage("ReSharper", "UnusedMember.Global")]
			None = 0,
			Forwarding = 1,
			Distributing = 2
		}

		[Flags]
		internal enum ETradingPreferences : byte {
			[SuppressMessage("ReSharper", "UnusedMember.Global")]
			None = 0,
			AcceptDonations = 1,
			SteamTradeMatcher = 2,
			MatchEverything = 4
		}
	}
}