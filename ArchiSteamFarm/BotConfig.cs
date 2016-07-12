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
	internal sealed class BotConfig {
		[JsonProperty(Required = Required.DisallowNull)]
		internal bool Enabled { get; private set; } = false;

		[JsonProperty(Required = Required.DisallowNull)]
		internal bool StartOnLaunch { get; private set; } = true;

		[JsonProperty]
		internal string SteamLogin { get; set; }

		[JsonProperty]
		internal string SteamPassword { get; set; }

		[JsonProperty(Required = Required.DisallowNull)]
		[SuppressMessage("ReSharper", "ConvertToConstant.Local")]
		private readonly CryptoHelper.ECryptoMethod PasswordFormat = CryptoHelper.ECryptoMethod.PlainText;

		[JsonProperty]
		internal string SteamParentalPIN { get; set; } = "0";

		[JsonProperty]
		internal string SteamApiKey { get; private set; } = null;

		[JsonProperty(Required = Required.DisallowNull)]
		internal ulong SteamMasterID { get; private set; } = 0;

		[JsonProperty(Required = Required.DisallowNull)]
		internal ulong SteamMasterClanID { get; private set; } = 0;

		[JsonProperty(Required = Required.DisallowNull)]
		internal bool CardDropsRestricted { get; private set; } = false;

		[JsonProperty(Required = Required.DisallowNull)]
		internal bool DismissInventoryNotifications { get; private set; } = true;

		[JsonProperty(Required = Required.DisallowNull)]
		internal bool FarmOffline { get; private set; } = false;

		[JsonProperty(Required = Required.DisallowNull)]
		internal bool HandleOfflineMessages { get; private set; } = false;

		[JsonProperty(Required = Required.DisallowNull)]
		internal bool AcceptGifts { get; private set; } = false;

		[JsonProperty(Required = Required.DisallowNull)]
		internal bool IsBotAccount { get; private set; } = false;

		[JsonProperty(Required = Required.DisallowNull)]
		internal bool SteamTradeMatcher { get; private set; } = false;

		[JsonProperty(Required = Required.DisallowNull)]
		internal bool ForwardKeysToOtherBots { get; private set; } = false;

		[JsonProperty(Required = Required.DisallowNull)]
		internal bool DistributeKeys { get; private set; } = false;

		[JsonProperty(Required = Required.DisallowNull)]
		internal bool ShutdownOnFarmingFinished { get; private set; } = false;

		[JsonProperty(Required = Required.DisallowNull)]
		internal bool SendOnFarmingFinished { get; private set; } = false;

		[JsonProperty]
		internal string SteamTradeToken { get; private set; } = null;

		[JsonProperty(Required = Required.DisallowNull)]
		internal byte SendTradePeriod { get; private set; } = 0;

		[JsonProperty(Required = Required.DisallowNull)]
		internal byte AcceptConfirmationsPeriod { get; private set; } = 0;

		[JsonProperty]
		internal string CustomGamePlayedWhileFarming { get; private set; } = null;

		[JsonProperty]
		internal string CustomGamePlayedWhileIdle { get; private set; } = null;

		[JsonProperty(Required = Required.DisallowNull)]
		internal HashSet<uint> GamesPlayedWhileIdle { get; private set; } = new HashSet<uint>();


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
			botConfig.GamesPlayedWhileIdle = new HashSet<uint>(botConfig.GamesPlayedWhileIdle.Take(CardsFarmer.MaxGamesPlayedConcurrently));

			return botConfig;
		}

		// This constructor is used only by deserializer
		private BotConfig() { }
	}
}
