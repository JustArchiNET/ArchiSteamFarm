//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2020 Łukasz "JustArchi" Domeradzki
// Contact: JustArchi@JustArchi.net
// |
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// |
// http://www.apache.org/licenses/LICENSE-2.0
// |
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Dom;
using ArchiSteamFarm.Helpers;
using ArchiSteamFarm.Json;
using ArchiSteamFarm.Localization;
using JetBrains.Annotations;
using Newtonsoft.Json;

namespace ArchiSteamFarm {
	[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
	internal sealed class MobileAuthenticator : IDisposable {
		internal const byte CodeDigits = 5;

		private const byte CodeInterval = 30;
		private const byte SteamTimeTTL = 24; // For how many hours we can assume that SteamTimeDifference is correct

		private static readonly char[] CodeCharacters = { '2', '3', '4', '5', '6', '7', '8', '9', 'B', 'C', 'D', 'F', 'G', 'H', 'J', 'K', 'M', 'N', 'P', 'Q', 'R', 'T', 'V', 'W', 'X', 'Y' };
		private static readonly SemaphoreSlim TimeSemaphore = new SemaphoreSlim(1, 1);

		private static DateTime LastSteamTimeCheck;
		private static int? SteamTimeDifference;

		private readonly ArchiCacheable<string> CachedDeviceID;

#pragma warning disable 649
		[JsonProperty(PropertyName = "identity_secret", Required = Required.Always)]
		private readonly string IdentitySecret;
#pragma warning restore 649

#pragma warning disable 649
		[JsonProperty(PropertyName = "shared_secret", Required = Required.Always)]
		private readonly string SharedSecret;
#pragma warning restore 649

		private Bot Bot;

		[JsonConstructor]
		private MobileAuthenticator() => CachedDeviceID = new ArchiCacheable<string>(ResolveDeviceID);

		public void Dispose() => CachedDeviceID.Dispose();

		internal async Task<string> GenerateToken() {
			uint time = await GetSteamTime().ConfigureAwait(false);

			if (time == 0) {
				Bot.ArchiLogger.LogNullError(nameof(time));

				return null;
			}

			return GenerateTokenForTime(time);
		}

		[ItemCanBeNull]
		internal async Task<Steam.ConfirmationDetails> GetConfirmationDetails(Confirmation confirmation) {
			if (confirmation == null) {
				Bot.ArchiLogger.LogNullError(nameof(confirmation));

				return null;
			}

			(bool success, string deviceID) = await CachedDeviceID.GetValue().ConfigureAwait(false);

			if (!success || string.IsNullOrEmpty(deviceID)) {
				Bot.ArchiLogger.LogGenericError(string.Format(Strings.WarningFailedWithError, nameof(deviceID)));

				return null;
			}

			uint time = await GetSteamTime().ConfigureAwait(false);

			if (time == 0) {
				Bot.ArchiLogger.LogNullError(nameof(time));

				return null;
			}

			string confirmationHash = GenerateConfirmationHash(time, "conf");

			if (string.IsNullOrEmpty(confirmationHash)) {
				Bot.ArchiLogger.LogNullError(nameof(confirmationHash));

				return null;
			}

			Steam.ConfirmationDetails response = await Bot.ArchiWebHandler.GetConfirmationDetails(deviceID, confirmationHash, time, confirmation).ConfigureAwait(false);

			return response?.Success == true ? response : null;
		}

		[ItemCanBeNull]
		internal async Task<HashSet<Confirmation>> GetConfirmations(Steam.ConfirmationDetails.EType? acceptedType = null) {
			(bool success, string deviceID) = await CachedDeviceID.GetValue().ConfigureAwait(false);

			if (!success || string.IsNullOrEmpty(deviceID)) {
				Bot.ArchiLogger.LogGenericError(string.Format(Strings.WarningFailedWithError, nameof(deviceID)));

				return null;
			}

			uint time = await GetSteamTime().ConfigureAwait(false);

			if (time == 0) {
				Bot.ArchiLogger.LogNullError(nameof(time));

				return null;
			}

			string confirmationHash = GenerateConfirmationHash(time, "conf");

			if (string.IsNullOrEmpty(confirmationHash)) {
				Bot.ArchiLogger.LogNullError(nameof(confirmationHash));

				return null;
			}

			await LimitConfirmationsRequestsAsync().ConfigureAwait(false);

			using IDocument htmlDocument = await Bot.ArchiWebHandler.GetConfirmations(deviceID, confirmationHash, time).ConfigureAwait(false);

			if (htmlDocument == null) {
				return null;
			}

			HashSet<Confirmation> result = new HashSet<Confirmation>();

			List<IElement> confirmationNodes = htmlDocument.SelectNodes("//div[@class='mobileconf_list_entry']");

			if (confirmationNodes.Count == 0) {
				return result;
			}

			foreach (IElement confirmationNode in confirmationNodes) {
				string idText = confirmationNode.GetAttributeValue("data-confid");

				if (string.IsNullOrEmpty(idText)) {
					Bot.ArchiLogger.LogNullError(nameof(idText));

					return null;
				}

				if (!ulong.TryParse(idText, out ulong id) || (id == 0)) {
					Bot.ArchiLogger.LogNullError(nameof(id));

					return null;
				}

				string keyText = confirmationNode.GetAttributeValue("data-key");

				if (string.IsNullOrEmpty(keyText)) {
					Bot.ArchiLogger.LogNullError(nameof(keyText));

					return null;
				}

				if (!ulong.TryParse(keyText, out ulong key) || (key == 0)) {
					Bot.ArchiLogger.LogNullError(nameof(key));

					return null;
				}

				string typeText = confirmationNode.GetAttributeValue("data-type");

				if (string.IsNullOrEmpty(typeText)) {
					Bot.ArchiLogger.LogNullError(nameof(typeText));

					return null;
				}

				if (!Enum.TryParse(typeText, out Steam.ConfirmationDetails.EType type) || (type == Steam.ConfirmationDetails.EType.Unknown)) {
					Bot.ArchiLogger.LogNullError(nameof(type));

					return null;
				}

				if (!Enum.IsDefined(typeof(Steam.ConfirmationDetails.EType), type)) {
					Bot.ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(type), type));

					return null;
				}

				if (acceptedType.HasValue && (acceptedType.Value != type)) {
					continue;
				}

				result.Add(new Confirmation(id, key));
			}

			return result;
		}

		internal async Task<bool> HandleConfirmations(IReadOnlyCollection<Confirmation> confirmations, bool accept) {
			if ((confirmations == null) || (confirmations.Count == 0)) {
				Bot.ArchiLogger.LogNullError(nameof(confirmations));

				return false;
			}

			(bool success, string deviceID) = await CachedDeviceID.GetValue().ConfigureAwait(false);

			if (!success || string.IsNullOrEmpty(deviceID)) {
				Bot.ArchiLogger.LogGenericError(string.Format(Strings.WarningFailedWithError, nameof(deviceID)));

				return false;
			}

			uint time = await GetSteamTime().ConfigureAwait(false);

			if (time == 0) {
				Bot.ArchiLogger.LogNullError(nameof(time));

				return false;
			}

			string confirmationHash = GenerateConfirmationHash(time, "conf");

			if (string.IsNullOrEmpty(confirmationHash)) {
				Bot.ArchiLogger.LogNullError(nameof(confirmationHash));

				return false;
			}

			bool? result = await Bot.ArchiWebHandler.HandleConfirmations(deviceID, confirmationHash, time, confirmations, accept).ConfigureAwait(false);

			if (!result.HasValue) {
				// Request timed out
				return false;
			}

			if (result.Value) {
				// Request succeeded
				return true;
			}

			// Our multi request failed, this is almost always Steam issue that happens randomly
			// In this case, we'll accept all pending confirmations one-by-one, synchronously (as Steam can't handle them in parallel)
			// We totally ignore actual result returned by those calls, abort only if request timed out
			foreach (Confirmation confirmation in confirmations) {
				bool? confirmationResult = await Bot.ArchiWebHandler.HandleConfirmation(deviceID, confirmationHash, time, confirmation.ID, confirmation.Key, accept).ConfigureAwait(false);

				if (!confirmationResult.HasValue) {
					return false;
				}
			}

			return true;
		}

		internal void Init([JetBrains.Annotations.NotNull] Bot bot) => Bot = bot ?? throw new ArgumentNullException(nameof(bot));

		private string GenerateConfirmationHash(uint time, string tag = null) {
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

			using HMACSHA1 hmac = new HMACSHA1(identitySecret);

			byte[] hash = hmac.ComputeHash(buffer);

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
			int start = hash[^1] & 0x0f;

			// Extract those 4 bytes
			byte[] bytes = new byte[4];

			Array.Copy(hash, start, bytes, 0, 4);

			if (BitConverter.IsLittleEndian) {
				Array.Reverse(bytes);
			}

			uint fullCode = BitConverter.ToUInt32(bytes, 0) & 0x7fffffff;

			// Build the alphanumeric code
			StringBuilder code = new StringBuilder(CodeDigits, CodeDigits);

			for (byte i = 0; i < CodeDigits; i++) {
				code.Append(CodeCharacters[fullCode % CodeCharacters.Length]);
				fullCode /= (uint) CodeCharacters.Length;
			}

			return code.ToString();
		}

		private async Task<uint> GetSteamTime() {
			if (SteamTimeDifference.HasValue && (DateTime.UtcNow.Subtract(LastSteamTimeCheck).TotalHours < SteamTimeTTL)) {
				return (uint) (Utilities.GetUnixTime() + SteamTimeDifference.Value);
			}

			await TimeSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				if (SteamTimeDifference.HasValue && (DateTime.UtcNow.Subtract(LastSteamTimeCheck).TotalHours < SteamTimeTTL)) {
					return (uint) (Utilities.GetUnixTime() + SteamTimeDifference.Value);
				}

				uint serverTime = await Bot.ArchiWebHandler.GetServerTime().ConfigureAwait(false);

				if (serverTime == 0) {
					return Utilities.GetUnixTime();
				}

				SteamTimeDifference = (int) (serverTime - Utilities.GetUnixTime());
				LastSteamTimeCheck = DateTime.UtcNow;

				return (uint) (Utilities.GetUnixTime() + SteamTimeDifference.Value);
			} finally {
				TimeSemaphore.Release();
			}
		}

		private static async Task LimitConfirmationsRequestsAsync() {
			if (ASF.ConfirmationsSemaphore == null) {
				ASF.ArchiLogger.LogNullError(nameof(ASF.ConfirmationsSemaphore));

				return;
			}

			if (ASF.GlobalConfig.ConfirmationsLimiterDelay == 0) {
				return;
			}

			await ASF.ConfirmationsSemaphore.WaitAsync().ConfigureAwait(false);

			Utilities.InBackground(
				async () => {
					await Task.Delay(ASF.GlobalConfig.ConfirmationsLimiterDelay * 1000).ConfigureAwait(false);
					ASF.ConfirmationsSemaphore.Release();
				}
			);
		}

		private async Task<(bool Success, string Result)> ResolveDeviceID() {
			string deviceID = await Bot.ArchiHandler.GetTwoFactorDeviceIdentifier(Bot.SteamID).ConfigureAwait(false);

			if (string.IsNullOrEmpty(deviceID)) {
				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);

				return (false, null);
			}

			return (true, deviceID);
		}

		internal sealed class Confirmation {
			internal readonly ulong ID;
			internal readonly ulong Key;

			internal Confirmation(ulong id, ulong key) {
				if ((id == 0) || (key == 0)) {
					throw new ArgumentNullException(nameof(id) + " || " + nameof(key));
				}

				ID = id;
				Key = key;
			}
		}
	}
}
