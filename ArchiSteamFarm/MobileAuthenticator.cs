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

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.JSON;
using HtmlAgilityPack;
using Newtonsoft.Json;

namespace ArchiSteamFarm {
	[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
	[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
	internal sealed class MobileAuthenticator {
		internal sealed class Confirmation {
			internal readonly uint ID;
			internal readonly ulong Key;
			internal readonly Steam.ConfirmationDetails.EType Type;

			internal Confirmation(uint id, ulong key, Steam.ConfirmationDetails.EType type) {
				if ((id == 0) || (key == 0) || (type == Steam.ConfirmationDetails.EType.Unknown)) {
					throw new ArgumentNullException(nameof(id) + " || " + nameof(key) + " || " + nameof(type));
				}

				ID = id;
				Key = key;
				Type = type;
			}
		}

		private static readonly byte[] TokenCharacters = { 50, 51, 52, 53, 54, 55, 56, 57, 66, 67, 68, 70, 71, 72, 74, 75, 77, 78, 80, 81, 82, 84, 86, 87, 88, 89 };
		private static readonly SemaphoreSlim TimeSemaphore = new SemaphoreSlim(1);

		private static short SteamTimeDifference;

		internal bool HasDeviceID => !string.IsNullOrEmpty(DeviceID);

#pragma warning disable 649
		[JsonProperty(PropertyName = "shared_secret", Required = Required.DisallowNull)]
		private string SharedSecret;

		[JsonProperty(PropertyName = "identity_secret", Required = Required.DisallowNull)]
		private string IdentitySecret;
#pragma warning restore 649

		[JsonProperty(PropertyName = "device_id")]
		private string DeviceID;

		private Bot Bot;

		private MobileAuthenticator() {

		}

		internal void Init(Bot bot) {
			if (bot == null) {
				throw new ArgumentNullException(nameof(bot));
			}

			Bot = bot;
		}

		internal void CorrectDeviceID(string deviceID) {
			if (string.IsNullOrEmpty(deviceID)) {
				Logging.LogNullError(nameof(deviceID), Bot.BotName);
				return;
			}

			DeviceID = deviceID;
		}

		internal async Task<bool> HandleConfirmations(HashSet<Confirmation> confirmations, bool accept) {
			if ((confirmations == null) || (confirmations.Count == 0)) {
				Logging.LogNullError(nameof(confirmations), Bot.BotName);
				return false;
			}

			uint time = await GetSteamTime().ConfigureAwait(false);
			if (time == 0) {
				Logging.LogNullError(nameof(time), Bot.BotName);
				return false;
			}

			string confirmationHash = GenerateConfirmationKey(time, "conf");
			if (!string.IsNullOrEmpty(confirmationHash)) {
				return await Bot.ArchiWebHandler.HandleConfirmations(DeviceID, confirmationHash, time, confirmations, accept).ConfigureAwait(false);
			}

			Logging.LogNullError(nameof(confirmationHash), Bot.BotName);
			return false;
		}

		internal async Task<Steam.ConfirmationDetails> GetConfirmationDetails(Confirmation confirmation) {
			if (confirmation == null) {
				Logging.LogNullError(nameof(confirmation), Bot.BotName);
				return null;
			}

			uint time = await GetSteamTime().ConfigureAwait(false);
			if (time == 0) {
				Logging.LogNullError(nameof(time), Bot.BotName);
				return null;
			}

			string confirmationHash = GenerateConfirmationKey(time, "conf");
			if (string.IsNullOrEmpty(confirmationHash)) {
				Logging.LogNullError(nameof(confirmationHash), Bot.BotName);
				return null;
			}

			Steam.ConfirmationDetails response = await Bot.ArchiWebHandler.GetConfirmationDetails(DeviceID, confirmationHash, time, confirmation).ConfigureAwait(false);
			if ((response == null) || !response.Success) {
				return null;
			}

			return response;
		}

		internal async Task<string> GenerateToken() {
			uint time = await GetSteamTime().ConfigureAwait(false);
			if (time != 0) {
				return GenerateTokenForTime(time);
			}

			Logging.LogNullError(nameof(time), Bot.BotName);
			return null;
		}

		internal async Task<HashSet<Confirmation>> GetConfirmations() {
			uint time = await GetSteamTime().ConfigureAwait(false);
			if (time == 0) {
				Logging.LogNullError(nameof(time), Bot.BotName);
				return null;
			}

			string confirmationHash = GenerateConfirmationKey(time, "conf");
			if (string.IsNullOrEmpty(confirmationHash)) {
				Logging.LogNullError(nameof(confirmationHash), Bot.BotName);
				return null;
			}

			HtmlDocument htmlDocument = await Bot.ArchiWebHandler.GetConfirmations(DeviceID, confirmationHash, time).ConfigureAwait(false);

			HtmlNodeCollection confirmationNodes = htmlDocument?.DocumentNode.SelectNodes("//div[@class='mobileconf_list_entry']");
			if (confirmationNodes == null) {
				return null;
			}

			HashSet<Confirmation> result = new HashSet<Confirmation>();
			foreach (HtmlNode confirmationNode in confirmationNodes) {
				string idString = confirmationNode.GetAttributeValue("data-confid", null);
				if (string.IsNullOrEmpty(idString)) {
					Logging.LogNullError(nameof(idString), Bot.BotName);
					return null;
				}

				uint id;
				if (!uint.TryParse(idString, out id) || (id == 0)) {
					Logging.LogNullError(nameof(id), Bot.BotName);
					return null;
				}

				string keyString = confirmationNode.GetAttributeValue("data-key", null);
				if (string.IsNullOrEmpty(keyString)) {
					Logging.LogNullError(nameof(keyString), Bot.BotName);
					return null;
				}

				ulong key;
				if (!ulong.TryParse(keyString, out key) || (key == 0)) {
					Logging.LogNullError(nameof(key), Bot.BotName);
					return null;
				}

				HtmlNode descriptionNode = confirmationNode.SelectSingleNode(".//div[@class='mobileconf_list_entry_description']/div");
				if (descriptionNode == null) {
					Logging.LogNullError(nameof(descriptionNode), Bot.BotName);
					return null;
				}

				Steam.ConfirmationDetails.EType type;

				string description = descriptionNode.InnerText;
				if (description.Equals("Sell - Market Listing")) {
					type = Steam.ConfirmationDetails.EType.Market;
				} else if (description.StartsWith("Trade with ", StringComparison.Ordinal)) {
					type = Steam.ConfirmationDetails.EType.Trade;
				} else {
					type = Steam.ConfirmationDetails.EType.Other;
				}

				result.Add(new Confirmation(id, key, type));
			}

			return result;
		}

		internal async Task<uint> GetSteamTime() {
			if (SteamTimeDifference != 0) {
				return (uint) (Utilities.GetUnixTime() + SteamTimeDifference);
			}

			await TimeSemaphore.WaitAsync().ConfigureAwait(false);

			if (SteamTimeDifference == 0) {
				uint serverTime = Bot.ArchiWebHandler.GetServerTime();
				if (serverTime != 0) {
					SteamTimeDifference = (short) (serverTime - Utilities.GetUnixTime());
				}
			}

			TimeSemaphore.Release();
			return (uint) (Utilities.GetUnixTime() + SteamTimeDifference);
		}

		private string GenerateTokenForTime(long time) {
			if (time == 0) {
				Logging.LogNullError(nameof(time), Bot.BotName);
				return null;
			}

			byte[] sharedSecretArray = Convert.FromBase64String(SharedSecret);
			byte[] timeArray = new byte[8];

			time /= 30L;

			for (int i = 8; i > 0; i--) {
				timeArray[i - 1] = (byte) time;
				time >>= 8;
			}

			byte[] hashedData;
			using (HMACSHA1 hmacGenerator = new HMACSHA1(sharedSecretArray, true)) {
				hashedData = hmacGenerator.ComputeHash(timeArray);
			}

			byte b = (byte) (hashedData[19] & 0xF);
			int codePoint = ((hashedData[b] & 0x7F) << 24) | ((hashedData[b + 1] & 0xFF) << 16) | ((hashedData[b + 2] & 0xFF) << 8) | (hashedData[b + 3] & 0xFF);

			byte[] codeArray = new byte[5];
			for (int i = 0; i < 5; ++i) {
				codeArray[i] = TokenCharacters[codePoint % TokenCharacters.Length];
				codePoint /= TokenCharacters.Length;
			}

			return Encoding.UTF8.GetString(codeArray);
		}

		private string GenerateConfirmationKey(uint time, string tag = null) {
			if (time == 0) {
				Logging.LogNullError(nameof(time), Bot.BotName);
				return null;
			}

			byte[] b64Secret = Convert.FromBase64String(IdentitySecret);

			int bufferSize = 8;
			if (string.IsNullOrEmpty(tag) == false) {
				bufferSize += Math.Min(32, tag.Length);
			}

			byte[] buffer = new byte[bufferSize];

			byte[] timeArray = BitConverter.GetBytes((long) time);
			if (BitConverter.IsLittleEndian) {
				Array.Reverse(timeArray);
			}

			Array.Copy(timeArray, buffer, 8);
			if (string.IsNullOrEmpty(tag) == false) {
				Array.Copy(Encoding.UTF8.GetBytes(tag), 0, buffer, 8, bufferSize - 8);
			}

			byte[] hash;
			using (HMACSHA1 hmac = new HMACSHA1(b64Secret, true)) {
				hash = hmac.ComputeHash(buffer);
			}

			return Convert.ToBase64String(hash, Base64FormattingOptions.None);
		}
	}
}
