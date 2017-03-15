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
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing.Design;
using System.IO;
using ConfigGenerator.JSON;
using Newtonsoft.Json;
using Wexman.Design;

namespace ConfigGenerator {
	[SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
	[SuppressMessage("ReSharper", "CollectionNeverQueried.Global")]
	[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
	[SuppressMessage("ReSharper", "UnusedMember.Global")]
	internal sealed class BotConfig : ASFConfig {
		[JsonProperty(Required = Required.DisallowNull)]
		public bool AcceptGifts { get; set; } = false;

		[LocalizedCategory("Performance")]
		[JsonProperty(Required = Required.DisallowNull)]
		public bool CardDropsRestricted { get; set; } = true;

		[JsonProperty]
		public string CustomGamePlayedWhileFarming { get; set; } = null;

		[JsonProperty]
		public string CustomGamePlayedWhileIdle { get; set; } = null;

		[JsonProperty(Required = Required.DisallowNull)]
		public bool DismissInventoryNotifications { get; set; } = false;

		[LocalizedCategory("Core")]
		[JsonProperty(Required = Required.DisallowNull)]
		public bool Enabled { get; set; } = false;

		[JsonProperty(Required = Required.DisallowNull)]
		public EFarmingOrder FarmingOrder { get; set; } = EFarmingOrder.Unordered;

		[JsonProperty(Required = Required.DisallowNull)]
		public bool FarmOffline { get; set; } = false;

		[JsonProperty(Required = Required.DisallowNull)]
		public List<uint> GamesPlayedWhileIdle { get; set; } = new List<uint>();

		[LocalizedCategory("Advanced")]
		[JsonProperty(Required = Required.DisallowNull)]
		public bool HandleOfflineMessages { get; set; } = false;

		[LocalizedCategory("Advanced")]
		[JsonProperty(Required = Required.DisallowNull)]
		public bool IsBotAccount { get; set; } = false;

		[LocalizedCategory("Advanced")]
		[JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace, Required = Required.DisallowNull)]
		public List<Steam.Item.EType> LootableTypes { get; set; } = new List<Steam.Item.EType> {
			Steam.Item.EType.BoosterPack,
			Steam.Item.EType.FoilTradingCard,
			Steam.Item.EType.TradingCard
		};

		[LocalizedCategory("Access")]
		[JsonProperty(Required = Required.DisallowNull)]
		public ECryptoMethod PasswordFormat { get; set; } = ECryptoMethod.PlainText;

		[LocalizedCategory("Advanced")]
		[JsonProperty(Required = Required.DisallowNull)]
		public bool Paused { get; set; } = false;

		[LocalizedCategory("Advanced")]
		[Editor(typeof(FlagEnumUiEditor), typeof(UITypeEditor))]
		[JsonProperty(Required = Required.DisallowNull)]
		public ERedeemingPreferences RedeemingPreferences { get; set; } = ERedeemingPreferences.None;

		[JsonProperty(Required = Required.DisallowNull)]
		public bool SendOnFarmingFinished { get; set; } = false;

		[JsonProperty(Required = Required.DisallowNull)]
		public byte SendTradePeriod { get; set; } = 0;

		[JsonProperty(Required = Required.DisallowNull)]
		public bool ShutdownOnFarmingFinished { get; set; } = false;

		[LocalizedCategory("Core")]
		[JsonProperty]
		public string SteamLogin { get; set; } = null;

		[LocalizedCategory("Access")]
		[JsonProperty(Required = Required.DisallowNull)]
		public ulong SteamMasterClanID { get; set; } = 0;

		[LocalizedCategory("Access")]
		[JsonProperty]
		public string SteamParentalPIN { get; set; } = "0";

		[LocalizedCategory("Core")]
		[JsonProperty]
		[PasswordPropertyText(true)]
		public string SteamPassword { get; set; } = null;

		[LocalizedCategory("Access")]
		[JsonProperty]
		public string SteamTradeToken { get; set; } = null;

		[LocalizedCategory("Access")]
		[Editor(typeof(GenericDictionaryEditor<ulong, EPermission>), typeof(UITypeEditor))]
		[JsonProperty(Required = Required.DisallowNull)]
		public Dictionary<ulong, EPermission> SteamUserPermissions { get; set; } = new Dictionary<ulong, EPermission>();

		[LocalizedCategory("Advanced")]
		[Editor(typeof(FlagEnumUiEditor), typeof(UITypeEditor))]
		[JsonProperty(Required = Required.DisallowNull)]
		public ETradingPreferences TradingPreferences { get; set; } = ETradingPreferences.None;

		[SuppressMessage("ReSharper", "UnusedMember.Local")]
		private BotConfig() { }

		private BotConfig(string filePath) : base(filePath) {
			if (string.IsNullOrEmpty(filePath)) {
				throw new ArgumentNullException(nameof(filePath));
			}

			Save();
		}

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

		internal enum ECryptoMethod : byte {
			PlainText,
			AES,
			ProtectedDataForCurrentUser
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