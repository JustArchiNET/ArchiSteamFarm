/*
    _                _      _  ____   _                           _____
   / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
  / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
 / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
/_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|

 Copyright 2015-2016 Łukasz "JustArchi" Domeradzki
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

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;

namespace ArchiSteamFarm {
	[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
	[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
	[SuppressMessage("ReSharper", "ConvertToConstant.Local")]
	[SuppressMessage("ReSharper", "ConvertToConstant.Global")]
	internal sealed class BotConfig {
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

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool Enabled = false;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool Paused = false;

		[JsonProperty]
		internal string SteamLogin { get; set; }

		[JsonProperty]
		internal string SteamPassword { get; set; }

		[JsonProperty]
		internal string SteamParentalPIN { get; set; } = "0";

		[JsonProperty]
		internal readonly string SteamApiKey = null;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly ulong SteamMasterID = 0;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly ulong SteamMasterClanID = 0;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool CardDropsRestricted = true;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool DismissInventoryNotifications = true;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly EFarmingOrder FarmingOrder = EFarmingOrder.Unordered;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool FarmOffline = false;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool HandleOfflineMessages = false;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool AcceptGifts = false;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool IsBotAccount = false;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool SteamTradeMatcher = false;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool ForwardKeysToOtherBots = false;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool DistributeKeys = false;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool ShutdownOnFarmingFinished = false;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly bool SendOnFarmingFinished = false;

		[JsonProperty]
		internal readonly string SteamTradeToken = null;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly byte SendTradePeriod = 0;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly byte AcceptConfirmationsPeriod = 0;

		[JsonProperty]
		internal readonly string CustomGamePlayedWhileFarming = null;

		[JsonProperty]
		internal readonly string CustomGamePlayedWhileIdle = null;

		[JsonProperty(Required = Required.DisallowNull)]
		internal readonly HashSet<uint> GamesPlayedWhileIdle = new HashSet<uint>();

		[JsonProperty(Required = Required.DisallowNull)]
		private readonly CryptoHelper.ECryptoMethod PasswordFormat = CryptoHelper.ECryptoMethod.PlainText;

		internal static BotConfig Load(string filePath) {
			if (string.IsNullOrEmpty(filePath)) {
				Logging.LogNullError(nameof(filePath));
				return null;
			}

			if (!File.Exists(filePath)) {
				return null;
			}

			BotConfig botConfig;

			try {
				botConfig = JsonConvert.DeserializeObject<BotConfig>(File.ReadAllText(filePath));
			} catch (Exception e) {
				Logging.LogGenericException(e);
				return null;
			}

			if (botConfig == null) {
				Logging.LogNullError(nameof(botConfig));
				return null;
			}

			// Support encrypted passwords
			if ((botConfig.PasswordFormat != CryptoHelper.ECryptoMethod.PlainText) && !string.IsNullOrEmpty(botConfig.SteamPassword)) {
				// In worst case password will result in null, which will have to be corrected by user during runtime
				botConfig.SteamPassword = CryptoHelper.Decrypt(botConfig.PasswordFormat, botConfig.SteamPassword);
			}

			// User might not know what he's doing
			// Ensure that he can't screw core ASF variables
			if (botConfig.GamesPlayedWhileIdle.Count <= CardsFarmer.MaxGamesPlayedConcurrently) {
				return botConfig;
			}

			Logging.LogGenericWarning("Playing more than " + CardsFarmer.MaxGamesPlayedConcurrently + " games concurrently is not possible, only first " + CardsFarmer.MaxGamesPlayedConcurrently + " entries from GamesPlayedWhileIdle will be used");

			HashSet<uint> validGames = new HashSet<uint>(botConfig.GamesPlayedWhileIdle.Take(CardsFarmer.MaxGamesPlayedConcurrently));
			botConfig.GamesPlayedWhileIdle.IntersectWith(validGames);
			botConfig.GamesPlayedWhileIdle.TrimExcess();

			return botConfig;
		}

		// This constructor is used only by deserializer
		private BotConfig() { }
	}
}
