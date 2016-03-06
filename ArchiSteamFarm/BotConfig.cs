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
using System.IO;
using System.Xml;

namespace ArchiSteamFarm {
	internal sealed class BotConfig {
		[JsonProperty(Required = Required.DisallowNull)]
		internal bool Enabled { get; private set; } = false;

		[JsonProperty(Required = Required.DisallowNull)]
		internal bool StartOnLaunch { get; private set; } = true;

		[JsonProperty]
		internal string SteamLogin { get; set; } = null;

		[JsonProperty]
		internal string SteamPassword { get; set; } = null;

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
		internal bool ForwardKeysToOtherBots { get; private set; } = false;

		[JsonProperty(Required = Required.DisallowNull)]
		internal bool DistributeKeys { get; private set; } = false;

		[JsonProperty(Required = Required.DisallowNull)]
		internal bool UseAsfAsMobileAuthenticator { get; private set; } = false;

		[JsonProperty(Required = Required.DisallowNull)]
		internal bool ShutdownOnFarmingFinished { get; private set; } = false;

		[JsonProperty(Required = Required.DisallowNull)]
		internal bool SendOnFarmingFinished { get; private set; } = false;

		[JsonProperty]
		internal string SteamTradeToken { get; private set; } = null;

		[JsonProperty(Required = Required.DisallowNull)]
		internal byte SendTradePeriod { get; private set; } = 0;

		[JsonProperty(Required = Required.DisallowNull)]
		internal HashSet<uint> GamesPlayedWhileIdle { get; private set; } = new HashSet<uint>() { 0 };

		[JsonProperty(Required = Required.DisallowNull)]
		internal bool Statistics { get; private set; } = true;


		internal static BotConfig Load(string path) {
			if (!File.Exists(path)) {
				return null;
			}

			BotConfig botConfig;
			try {
				botConfig = JsonConvert.DeserializeObject<BotConfig>(File.ReadAllText(path));
			} catch (Exception e) {
				Logging.LogGenericException(e);
				return null;
			}

			return botConfig;
		}

		// TODO: This should be removed soon
		internal static BotConfig LoadOldFormat(string path) {
			if (!File.Exists(path)) {
				return null;
			}

			BotConfig botConfig = new BotConfig();

			try {
				using (XmlReader reader = XmlReader.Create(path)) {
					while (reader.Read()) {
						if (reader.NodeType != XmlNodeType.Element) {
							continue;
						}

						string key = reader.Name;
						if (string.IsNullOrEmpty(key)) {
							continue;
						}

						string value = reader.GetAttribute("value");
						if (string.IsNullOrEmpty(value)) {
							continue;
						}

						switch (key) {
							case "Enabled":
								botConfig.Enabled = bool.Parse(value);
								break;
							case "SteamLogin":
								botConfig.SteamLogin = value;
								break;
							case "SteamPassword":
								botConfig.SteamPassword = value;
								break;
							case "SteamApiKey":
								botConfig.SteamApiKey = value;
								break;
							case "SteamTradeToken":
								botConfig.SteamTradeToken = value;
								break;
							case "SteamParentalPIN":
								botConfig.SteamParentalPIN = value;
								break;
							case "SteamMasterID":
								botConfig.SteamMasterID = ulong.Parse(value);
								break;
							case "SteamMasterClanID":
								botConfig.SteamMasterClanID = ulong.Parse(value);
								break;
							case "StartOnLaunch":
								botConfig.StartOnLaunch = bool.Parse(value);
								break;
							case "UseAsfAsMobileAuthenticator":
								botConfig.UseAsfAsMobileAuthenticator = bool.Parse(value);
								break;
							case "CardDropsRestricted":
								botConfig.CardDropsRestricted = bool.Parse(value);
								break;
							case "FarmOffline":
								botConfig.FarmOffline = bool.Parse(value);
								break;
							case "HandleOfflineMessages":
								botConfig.HandleOfflineMessages = bool.Parse(value);
								break;
							case "ForwardKeysToOtherBots":
								botConfig.ForwardKeysToOtherBots = bool.Parse(value);
								break;
							case "DistributeKeys":
								botConfig.DistributeKeys = bool.Parse(value);
								break;
							case "ShutdownOnFarmingFinished":
								botConfig.ShutdownOnFarmingFinished = bool.Parse(value);
								break;
							case "SendOnFarmingFinished":
								botConfig.SendOnFarmingFinished = bool.Parse(value);
								break;
							case "SendTradePeriod":
								botConfig.SendTradePeriod = byte.Parse(value);
								break;
							case "GamesPlayedWhileIdle":
								botConfig.GamesPlayedWhileIdle.Clear();
								foreach (string appID in value.Split(',')) {
									botConfig.GamesPlayedWhileIdle.Add(uint.Parse(appID));
								}
								break;
							case "Statistics":
								botConfig.Statistics = bool.Parse(value);
								break;
							case "Blacklist":
							case "SteamNickname":
								break;
							default:
								Logging.LogGenericWarning("Unrecognized config value: " + key + "=" + value);
								break;
						}
					}
				}
			} catch (Exception e) {
				Logging.LogGenericException(e);
				Logging.LogGenericError("Your config for this bot instance is invalid, it won't run!");
				return null;
			}

			// Fixups for new format
			if (botConfig.SteamLogin != null && botConfig.SteamLogin.Equals("null")) {
				botConfig.SteamLogin = null;
			}

			if (botConfig.SteamPassword != null && botConfig.SteamPassword.Equals("null")) {
				botConfig.SteamPassword = null;
			}

			if (botConfig.SteamApiKey != null && botConfig.SteamApiKey.Equals("null")) {
				botConfig.SteamApiKey = null;
			}

			if (botConfig.SteamParentalPIN != null && botConfig.SteamParentalPIN.Equals("null")) {
				botConfig.SteamParentalPIN = null;
			}

			if (botConfig.SteamTradeToken != null && botConfig.SteamTradeToken.Equals("null")) {
				botConfig.SteamTradeToken = null;
			}

			return botConfig;
		}

		// This constructor is used only by deserializer
		private BotConfig() { }

		// TODO: This should be removed soon
		internal bool Convert(string path) {
			try {
				File.WriteAllText(path, JsonConvert.SerializeObject(this, Newtonsoft.Json.Formatting.Indented));
			} catch (Exception e) {
				Logging.LogGenericException(e);
				return false;
			}

			Logging.LogGenericWarning("Your config was converted to new ASF V2.0 format");
			return true;
		}
	}
}
