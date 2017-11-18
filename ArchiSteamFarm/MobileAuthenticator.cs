//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// 
//  Copyright 2015-2017 Łukasz "JustArchi" Domeradzki
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
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.JSON;
using ArchiSteamFarm.Localization;
using HtmlAgilityPack;
using Newtonsoft.Json;

namespace ArchiSteamFarm {
	internal sealed class MobileAuthenticator : IDisposable {
		private const byte CodeDigits = 5;
		private const byte CodeInterval = 30;

		private static readonly char[] CodeCharacters = { '2', '3', '4', '5', '6', '7', '8', '9', 'B', 'C', 'D', 'F', 'G', 'H', 'J', 'K', 'M', 'N', 'P', 'Q', 'R', 'T', 'V', 'W', 'X', 'Y' };
		private static readonly SemaphoreSlim TimeSemaphore = new SemaphoreSlim(1, 1);

		private static int? SteamTimeDifference;

		// "ERROR" is being used by SteamDesktopAuthenticator
		internal bool HasCorrectDeviceID => !string.IsNullOrEmpty(DeviceID) && !DeviceID.Equals("ERROR");

		private readonly SemaphoreSlim ConfirmationsSemaphore = new SemaphoreSlim(1, 1);

#pragma warning disable 649
		[JsonProperty(PropertyName = "identity_secret", Required = Required.Always)]
		private readonly string IdentitySecret;
#pragma warning restore 649

#pragma warning disable 649
		[JsonProperty(PropertyName = "shared_secret", Required = Required.Always)]
		private readonly string SharedSecret;
#pragma warning restore 649

		private Bot Bot;

		[JsonProperty(PropertyName = "device_id")]
		private string DeviceID;

		private MobileAuthenticator() { }

		public void Dispose() => ConfirmationsSemaphore.Dispose();

		internal bool CorrectDeviceID(string deviceID) {
			if (string.IsNullOrEmpty(deviceID)) {
				Bot.ArchiLogger.LogNullError(nameof(deviceID));
				return false;
			}

			if (!string.IsNullOrEmpty(DeviceID) && DeviceID.Equals(deviceID)) {
				return false;
			}

			DeviceID = deviceID;
			return true;
		}

		internal async Task<string> GenerateToken() {
			uint time = await GetSteamTime().ConfigureAwait(false);
			if (time != 0) {
				return GenerateTokenForTime(time);
			}

			Bot.ArchiLogger.LogNullError(nameof(time));
			return null;
		}

		internal async Task<Steam.ConfirmationDetails> GetConfirmationDetails(Confirmation confirmation) {
			if (confirmation == null) {
				Bot.ArchiLogger.LogNullError(nameof(confirmation));
				return null;
			}

			if (!HasCorrectDeviceID) {
				Bot.ArchiLogger.LogGenericError(Strings.ErrorMobileAuthenticatorInvalidDeviceID);
				return null;
			}

			uint time = await GetSteamTime().ConfigureAwait(false);
			if (time == 0) {
				Bot.ArchiLogger.LogNullError(nameof(time));
				return null;
			}

			string confirmationHash = GenerateConfirmationKey(time, "conf");
			if (string.IsNullOrEmpty(confirmationHash)) {
				Bot.ArchiLogger.LogNullError(nameof(confirmationHash));
				return null;
			}

			Steam.ConfirmationDetails response = await Bot.ArchiWebHandler.GetConfirmationDetails(DeviceID, confirmationHash, time, confirmation).ConfigureAwait(false);
			return response?.Success == true ? response : null;
		}

		internal async Task<HashSet<Confirmation>> GetConfirmations() {
			if (!HasCorrectDeviceID) {
				Bot.ArchiLogger.LogGenericError(Strings.ErrorMobileAuthenticatorInvalidDeviceID);
				return null;
			}

			uint time = await GetSteamTime().ConfigureAwait(false);
			if (time == 0) {
				Bot.ArchiLogger.LogNullError(nameof(time));
				return null;
			}

			string confirmationHash = GenerateConfirmationKey(time, "conf");
			if (string.IsNullOrEmpty(confirmationHash)) {
				Bot.ArchiLogger.LogNullError(nameof(confirmationHash));
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
					Bot.ArchiLogger.LogNullError(nameof(idString));
					return null;
				}

				if (!uint.TryParse(idString, out uint id) || (id == 0)) {
					Bot.ArchiLogger.LogNullError(nameof(id));
					return null;
				}

				string keyString = confirmationNode.GetAttributeValue("data-key", null);
				if (string.IsNullOrEmpty(keyString)) {
					Bot.ArchiLogger.LogNullError(nameof(keyString));
					return null;
				}

				if (!ulong.TryParse(keyString, out ulong key) || (key == 0)) {
					Bot.ArchiLogger.LogNullError(nameof(key));
					return null;
				}

				HtmlNode descriptionNode = confirmationNode.SelectSingleNode(".//div[@class='mobileconf_list_entry_description']/div");
				if (descriptionNode == null) {
					Bot.ArchiLogger.LogNullError(nameof(descriptionNode));
					return null;
				}

				Steam.ConfirmationDetails.EType type;

				string description = descriptionNode.InnerText;
				if (description.StartsWith("Sell - ", StringComparison.Ordinal)) {
					type = Steam.ConfirmationDetails.EType.Market;
				} else if (description.StartsWith("Trade with ", StringComparison.Ordinal) || description.Equals("Error loading trade details")) {
					type = Steam.ConfirmationDetails.EType.Trade;
				} else {
					Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(description), description));
					type = Steam.ConfirmationDetails.EType.Other;
				}

				result.Add(new Confirmation(id, key, type));
			}

			return result;
		}

		internal async Task<bool> HandleConfirmations(IReadOnlyCollection<Confirmation> confirmations, bool accept) {
			if ((confirmations == null) || (confirmations.Count == 0)) {
				Bot.ArchiLogger.LogNullError(nameof(confirmations));
				return false;
			}

			if (!HasCorrectDeviceID) {
				Bot.ArchiLogger.LogGenericError(Strings.ErrorMobileAuthenticatorInvalidDeviceID);
				return false;
			}

			await ConfirmationsSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				uint time = await GetSteamTime().ConfigureAwait(false);
				if (time == 0) {
					Bot.ArchiLogger.LogNullError(nameof(time));
					return false;
				}

				string confirmationHash = GenerateConfirmationKey(time, "conf");
				if (string.IsNullOrEmpty(confirmationHash)) {
					Bot.ArchiLogger.LogNullError(nameof(confirmationHash));
					return false;
				}

				bool? result = await Bot.ArchiWebHandler.HandleConfirmations(DeviceID, confirmationHash, time, confirmations, accept).ConfigureAwait(false);
				if (!result.HasValue) {
					// Request timed out
					return false;
				}

				if (result.Value) {
					// Request succeeded
					return true;
				}

				// Our multi request failed, this is almost always Steam fuckup that happens randomly
				// In this case, we'll accept all pending confirmations one-by-one, synchronously (as Steam can't handle them in parallel)
				// We totally ignore actual result returned by those calls, abort only if request timed out

				foreach (Confirmation confirmation in confirmations) {
					bool? confirmationResult = await Bot.ArchiWebHandler.HandleConfirmation(DeviceID, confirmationHash, time, confirmation.ID, confirmation.Key, accept).ConfigureAwait(false);
					if (!confirmationResult.HasValue) {
						return false;
					}
				}

				return true;
			} finally {
				ConfirmationsSemaphore.Release();
			}
		}

		internal void Init(Bot bot) => Bot = bot ?? throw new ArgumentNullException(nameof(bot));

		private string GenerateConfirmationKey(uint time, string tag = null) {
			if (time == 0) {
				Bot.ArchiLogger.LogNullError(nameof(time));
				return null;
			}

			byte[] identitySecret;

			try {
				identitySecret = Convert.FromBase64String(IdentitySecret);
			} catch (FormatException e) {
				Bot.ArchiLogger.LogGenericException(e);
				Bot.ArchiLogger.LogGenericError(string.Format(Strings.ErrorIsInvalid, nameof(IdentitySecret)));
				return null;
			}

			byte bufferSize = 8;
			if (!string.IsNullOrEmpty(tag)) {
				bufferSize += (byte) Math.Min(32, tag.Length);
			}

			byte[] timeArray = BitConverter.GetBytes((long) time);
			if (BitConverter.IsLittleEndian) {
				Array.Reverse(timeArray);
			}

			byte[] buffer = new byte[bufferSize];

			Array.Copy(timeArray, buffer, 8);
			if (!string.IsNullOrEmpty(tag)) {
				Array.Copy(Encoding.UTF8.GetBytes(tag), 0, buffer, 8, bufferSize - 8);
			}

			byte[] hash;
			using (HMACSHA1 hmac = new HMACSHA1(identitySecret)) {
				hash = hmac.ComputeHash(buffer);
			}

			return Convert.ToBase64String(hash);
		}

		private string GenerateTokenForTime(uint time) {
			if (time == 0) {
				Bot.ArchiLogger.LogNullError(nameof(time));
				return null;
			}

			byte[] sharedSecret;

			try {
				sharedSecret = Convert.FromBase64String(SharedSecret);
			} catch (FormatException e) {
				Bot.ArchiLogger.LogGenericException(e);
				Bot.ArchiLogger.LogGenericError(string.Format(Strings.ErrorIsInvalid, nameof(SharedSecret)));
				return null;
			}

			byte[] timeArray = BitConverter.GetBytes((long) time / CodeInterval);
			if (BitConverter.IsLittleEndian) {
				Array.Reverse(timeArray);
			}

			byte[] hash;
			using (HMACSHA1 hmac = new HMACSHA1(sharedSecret)) {
				hash = hmac.ComputeHash(timeArray);
			}

			// The last 4 bits of the mac say where the code starts
			int start = hash[hash.Length - 1] & 0x0f;

			// Extract those 4 bytes
			byte[] bytes = new byte[4];

			Array.Copy(hash, start, bytes, 0, 4);

			if (BitConverter.IsLittleEndian) {
				Array.Reverse(bytes);
			}

			uint fullCode = BitConverter.ToUInt32(bytes, 0) & 0x7fffffff;

			// Build the alphanumeric code
			StringBuilder code = new StringBuilder();

			for (byte i = 0; i < CodeDigits; i++) {
				code.Append(CodeCharacters[fullCode % CodeCharacters.Length]);
				fullCode /= (uint) CodeCharacters.Length;
			}

			return code.ToString();
		}

		private async Task<uint> GetSteamTime() {
			await TimeSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				if (SteamTimeDifference.HasValue) {
					return (uint) (Utilities.GetUnixTime() + SteamTimeDifference.Value);
				}

				uint serverTime = await Bot.ArchiWebHandler.GetServerTime().ConfigureAwait(false);
				if (serverTime == 0) {
					return Utilities.GetUnixTime();
				}

				SteamTimeDifference = (int) (serverTime - Utilities.GetUnixTime());
				return (uint) (Utilities.GetUnixTime() + SteamTimeDifference.Value);
			} finally {
				TimeSemaphore.Release();
			}
		}

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
	}
}