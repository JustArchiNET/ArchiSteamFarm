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
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace ConfigGenerator {
	[SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
	[SuppressMessage("ReSharper", "CollectionNeverQueried.Global")]
	[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
	[SuppressMessage("ReSharper", "UnusedMember.Global")]
	internal sealed class BotConfig : ASFConfig {
		internal enum ECryptoMethod : byte {
			PlainText,
			AES,
			ProtectedDataForCurrentUser
		}

		[JsonProperty(Required = Required.DisallowNull)]
		public bool Enabled { get; set; } = false;

		[JsonProperty(Required = Required.DisallowNull)]
		public bool StartOnLaunch { get; set; } = true;

		[JsonProperty]
		public string SteamLogin { get; set; } = null;

		[JsonProperty]
		[PasswordPropertyText(true)]
		public string SteamPassword { get; set; } = null;

		[JsonProperty(Required = Required.DisallowNull)]
		public ECryptoMethod PasswordFormat { get; set; } = ECryptoMethod.PlainText;

		[JsonProperty]
		public string SteamParentalPIN { get; set; } = "0";

		[JsonProperty]
		public string SteamApiKey { get; set; } = null;

		[JsonProperty(Required = Required.DisallowNull)]
		public ulong SteamMasterID { get; set; } = 0;

		[JsonProperty(Required = Required.DisallowNull)]
		public ulong SteamMasterClanID { get; set; } = 0;

		[JsonProperty(Required = Required.DisallowNull)]
		public bool CardDropsRestricted { get; set; } = false;

		[JsonProperty(Required = Required.DisallowNull)]
		public bool DismissInventoryNotifications { get; set; } = true;

		[JsonProperty(Required = Required.DisallowNull)]
		public bool FarmOffline { get; set; } = false;

		[JsonProperty(Required = Required.DisallowNull)]
		public bool HandleOfflineMessages { get; set; } = false;

		[JsonProperty(Required = Required.DisallowNull)]
		public bool AcceptGifts { get; set; } = false;

		[JsonProperty(Required = Required.DisallowNull)]
		public bool IsBotAccount { get; set; } = false;

		[JsonProperty(Required = Required.DisallowNull)]
		public bool SteamTradeMatcher { get; set; } = false;

		[JsonProperty(Required = Required.DisallowNull)]
		public bool ForwardKeysToOtherBots { get; set; } = false;

		[JsonProperty(Required = Required.DisallowNull)]
		public bool DistributeKeys { get; set; } = false;

		[JsonProperty(Required = Required.DisallowNull)]
		public bool ShutdownOnFarmingFinished { get; set; } = false;

		[JsonProperty(Required = Required.DisallowNull)]
		public bool SendOnFarmingFinished { get; set; } = false;

		[JsonProperty]
		public string SteamTradeToken { get; set; } = null;

		[JsonProperty(Required = Required.DisallowNull)]
		public byte SendTradePeriod { get; set; } = 0;

		[JsonProperty(Required = Required.DisallowNull)]
		public byte AcceptConfirmationsPeriod { get; set; } = 0;

		[JsonProperty]
		public string CustomGamePlayedWhileFarming { get; set; } = null;

		[JsonProperty]
		public string CustomGamePlayedWhileIdle { get; set; } = null;

		[JsonProperty(Required = Required.DisallowNull)]
		public List<uint> GamesPlayedWhileIdle { get; set; } = new List<uint>();

		internal static BotConfig Load(string filePath) {
			if (string.IsNullOrEmpty(filePath)) {
				Logging.LogNullError(nameof(filePath));
				return null;
			}

			if (!File.Exists(filePath)) {
				return new BotConfig(filePath);
			}

			BotConfig botConfig;

			try {
				botConfig = JsonConvert.DeserializeObject<BotConfig>(File.ReadAllText(filePath));
			} catch (Exception e) {
				Logging.LogGenericException(e);
				return new BotConfig(filePath);
			}

			if (botConfig == null) {
				return new BotConfig(filePath);
			}

			botConfig.FilePath = filePath;

			return botConfig;
		}

		[SuppressMessage("ReSharper", "UnusedMember.Local")]
		private BotConfig() { }

		private BotConfig(string filePath) : base(filePath) {
			if (string.IsNullOrEmpty(filePath)) {
				throw new ArgumentNullException(nameof(filePath));
			}

			Save();
		}
	}
}
