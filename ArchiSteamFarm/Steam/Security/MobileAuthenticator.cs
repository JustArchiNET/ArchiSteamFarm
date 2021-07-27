//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2021 Łukasz "JustArchi" Domeradzki
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
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Dom;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Storage;
using Newtonsoft.Json;

namespace ArchiSteamFarm.Steam.Security {
	[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
	public sealed class MobileAuthenticator : IDisposable {
		internal const byte BackupCodeDigits = 7;
		internal const byte CodeDigits = 5;

		private const byte CodeInterval = 30;

		// For how many hours we can assume that SteamTimeDifference is correct
		private const byte SteamTimeTTL = 24;

		internal static readonly ImmutableSortedSet<char> CodeCharacters = ImmutableSortedSet.Create('2', '3', '4', '5', '6', '7', '8', '9', 'B', 'C', 'D', 'F', 'G', 'H', 'J', 'K', 'M', 'N', 'P', 'Q', 'R', 'T', 'V', 'W', 'X', 'Y');

		private static readonly SemaphoreSlim TimeSemaphore = new(1, 1);

		private static DateTime LastSteamTimeCheck;
		private static int? SteamTimeDifference;

		private readonly ArchiCacheable<string> CachedDeviceID;

		[JsonProperty(PropertyName = "identity_secret", Required = Required.Always)]
		private readonly string IdentitySecret = "";

		[JsonProperty(PropertyName = "shared_secret", Required = Required.Always)]
		private readonly string SharedSecret = "";

		private Bot? Bot;

		[JsonConstructor]
		private MobileAuthenticator() => CachedDeviceID = new ArchiCacheable<string>(ResolveDeviceID);

		public void Dispose() => CachedDeviceID.Dispose();

		internal async Task<string?> GenerateToken() {
			if (Bot == null) {
				throw new InvalidOperationException(nameof(Bot));
			}

			uint time = await GetSteamTime().ConfigureAwait(false);

			if (time == 0) {
				throw new InvalidOperationException(nameof(time));
			}

			return GenerateTokenForTime(time);
		}

		internal async Task<HashSet<Confirmation>?> GetConfirmations() {
			if (Bot == null) {
				throw new InvalidOperationException(nameof(Bot));
			}

			(bool success, string? deviceID) = await CachedDeviceID.GetValue().ConfigureAwait(false);

			if (!success || string.IsNullOrEmpty(deviceID)) {
				Bot.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, nameof(deviceID)));

				return null;
			}

			uint time = await GetSteamTime().ConfigureAwait(false);

			if (time == 0) {
				throw new InvalidOperationException(nameof(time));
			}

			string? confirmationHash = GenerateConfirmationHash(time, "conf");

			if (string.IsNullOrEmpty(confirmationHash)) {
				Bot.ArchiLogger.LogNullError(nameof(confirmationHash));

				return null;
			}

			await LimitConfirmationsRequestsAsync().ConfigureAwait(false);

			// ReSharper disable RedundantSuppressNullableWarningExpression - required for .NET Framework
			using IDocument? htmlDocument = await Bot.ArchiWebHandler.GetConfirmationsPage(deviceID!, confirmationHash!, time).ConfigureAwait(false);

			// ReSharper restore RedundantSuppressNullableWarningExpression - required for .NET Framework

			if (htmlDocument == null) {
				return null;
			}

			IEnumerable<IElement> confirmationNodes = htmlDocument.SelectNodes("//div[@class='mobileconf_list_entry']");

			HashSet<Confirmation> result = new();

			foreach (IElement confirmationNode in confirmationNodes) {
				string? idText = confirmationNode.GetAttribute("data-confid");

				if (string.IsNullOrEmpty(idText)) {
					Bot.ArchiLogger.LogNullError(nameof(idText));

					return null;
				}

				if (!ulong.TryParse(idText, out ulong id) || (id == 0)) {
					Bot.ArchiLogger.LogNullError(nameof(id));

					return null;
				}

				string? keyText = confirmationNode.GetAttribute("data-key");

				if (string.IsNullOrEmpty(keyText)) {
					Bot.ArchiLogger.LogNullError(nameof(keyText));

					return null;
				}

				if (!ulong.TryParse(keyText, out ulong key) || (key == 0)) {
					Bot.ArchiLogger.LogNullError(nameof(key));

					return null;
				}

				string? creatorText = confirmationNode.GetAttribute("data-creator");

				if (string.IsNullOrEmpty(creatorText)) {
					Bot.ArchiLogger.LogNullError(nameof(creatorText));

					return null;
				}

				if (!ulong.TryParse(creatorText, out ulong creator) || (creator == 0)) {
					Bot.ArchiLogger.LogNullError(nameof(creator));

					return null;
				}

				string? typeText = confirmationNode.GetAttribute("data-type");

				if (string.IsNullOrEmpty(typeText)) {
					Bot.ArchiLogger.LogNullError(nameof(typeText));

					return null;
				}

				if (!Enum.TryParse(typeText, out Confirmation.EType type) || (type == Confirmation.EType.Unknown)) {
					Bot.ArchiLogger.LogNullError(nameof(type));

					return null;
				}

				if (!Enum.IsDefined(typeof(Confirmation.EType), type)) {
					Bot.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(type), type));

					return null;
				}

				result.Add(new Confirmation(id, key, creator, type));
			}

			return result;
		}

		internal async Task<bool> HandleConfirmations(IReadOnlyCollection<Confirmation> confirmations, bool accept) {
			if ((confirmations == null) || (confirmations.Count == 0)) {
				throw new ArgumentNullException(nameof(confirmations));
			}

			if (Bot == null) {
				throw new InvalidOperationException(nameof(Bot));
			}

			(bool success, string? deviceID) = await CachedDeviceID.GetValue().ConfigureAwait(false);

			if (!success || string.IsNullOrEmpty(deviceID)) {
				Bot.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, nameof(deviceID)));

				return false;
			}

			uint time = await GetSteamTime().ConfigureAwait(false);

			if (time == 0) {
				throw new InvalidOperationException(nameof(time));
			}

			string? confirmationHash = GenerateConfirmationHash(time, "conf");

			if (string.IsNullOrEmpty(confirmationHash)) {
				Bot.ArchiLogger.LogNullError(nameof(confirmationHash));

				return false;
			}

			// ReSharper disable RedundantSuppressNullableWarningExpression - required for .NET Framework
			bool? result = await Bot.ArchiWebHandler.HandleConfirmations(deviceID!, confirmationHash!, time, confirmations, accept).ConfigureAwait(false);

			// ReSharper restore RedundantSuppressNullableWarningExpression - required for .NET Framework

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
				// ReSharper disable RedundantSuppressNullableWarningExpression - required for .NET Framework
				bool? confirmationResult = await Bot.ArchiWebHandler.HandleConfirmation(deviceID!, confirmationHash!, time, confirmation.ID, confirmation.Key, accept).ConfigureAwait(false);

				// ReSharper restore RedundantSuppressNullableWarningExpression - required for .NET Framework

				if (!confirmationResult.HasValue) {
					return false;
				}
			}

			return true;
		}

		internal void Init(Bot bot) => Bot = bot ?? throw new ArgumentNullException(nameof(bot));

		internal static async Task ResetSteamTimeDifference() {
			if ((SteamTimeDifference == null) && (LastSteamTimeCheck == DateTime.MinValue)) {
				return;
			}

			if (!await TimeSemaphore.WaitAsync(0).ConfigureAwait(false)) {
				// Resolve or reset is already in-progress
				return;
			}

			try {
				if ((SteamTimeDifference == null) && (LastSteamTimeCheck == DateTime.MinValue)) {
					return;
				}

				SteamTimeDifference = null;
				LastSteamTimeCheck = DateTime.MinValue;
			} finally {
				TimeSemaphore.Release();
			}
		}

		private string? GenerateConfirmationHash(uint time, string? tag = null) {
			if (time == 0) {
				throw new ArgumentOutOfRangeException(nameof(time));
			}

			if (Bot == null) {
				throw new InvalidOperationException(nameof(Bot));
			}

			if (string.IsNullOrEmpty(IdentitySecret)) {
				throw new InvalidOperationException(nameof(IdentitySecret));
			}

			byte[] identitySecret;

			try {
				identitySecret = Convert.FromBase64String(IdentitySecret);
			} catch (FormatException e) {
				Bot.ArchiLogger.LogGenericException(e);
				Bot.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, nameof(IdentitySecret)));

				return null;
			}

			byte bufferSize = 8;

			if (!string.IsNullOrEmpty(tag)) {
				// ReSharper disable once RedundantSuppressNullableWarningExpression - required for .NET Framework
				bufferSize += (byte) Math.Min(32, tag!.Length);
			}

			byte[] timeArray = BitConverter.GetBytes((ulong) time);

			if (BitConverter.IsLittleEndian) {
				Array.Reverse(timeArray);
			}

			byte[] buffer = new byte[bufferSize];

			Array.Copy(timeArray, buffer, 8);

			if (!string.IsNullOrEmpty(tag)) {
				// ReSharper disable once RedundantSuppressNullableWarningExpression - required for .NET Framework
				Array.Copy(Encoding.UTF8.GetBytes(tag!), 0, buffer, 8, bufferSize - 8);
			}

			byte[] hash;

#pragma warning disable CA5350 // This is actually a fair warning, but there is nothing we can do about Steam using weak cryptographic algorithms
			using (HMACSHA1 hmac = new(identitySecret)) {
				hash = hmac.ComputeHash(buffer);
			}
#pragma warning restore CA5350 // This is actually a fair warning, but there is nothing we can do about Steam using weak cryptographic algorithms

			return Convert.ToBase64String(hash);
		}

		private string? GenerateTokenForTime(uint time) {
			if (time == 0) {
				throw new ArgumentOutOfRangeException(nameof(time));
			}

			if (Bot == null) {
				throw new InvalidOperationException(nameof(Bot));
			}

			if (string.IsNullOrEmpty(SharedSecret)) {
				throw new InvalidOperationException(nameof(SharedSecret));
			}

			byte[] sharedSecret;

			try {
				sharedSecret = Convert.FromBase64String(SharedSecret);
			} catch (FormatException e) {
				Bot.ArchiLogger.LogGenericException(e);
				Bot.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, nameof(SharedSecret)));

				return null;
			}

			byte[] timeArray = BitConverter.GetBytes((ulong) (time / CodeInterval));

			if (BitConverter.IsLittleEndian) {
				Array.Reverse(timeArray);
			}

			byte[] hash;

#pragma warning disable CA5350 // This is actually a fair warning, but there is nothing we can do about Steam using weak cryptographic algorithms
			using (HMACSHA1 hmac = new(sharedSecret)) {
				hash = hmac.ComputeHash(timeArray);
			}
#pragma warning restore CA5350 // This is actually a fair warning, but there is nothing we can do about Steam using weak cryptographic algorithms

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
			char[] code = new char[CodeDigits];

			for (byte i = 0; i < CodeDigits; i++) {
				code[i] = CodeCharacters[(byte) (fullCode % CodeCharacters.Count)];
				fullCode /= (byte) CodeCharacters.Count;
			}

			return new string(code);
		}

		private async Task<uint> GetSteamTime() {
			if (Bot == null) {
				throw new InvalidOperationException(nameof(Bot));
			}

			int? steamTimeDifference = SteamTimeDifference;

			if (steamTimeDifference.HasValue && (DateTime.UtcNow.Subtract(LastSteamTimeCheck).TotalHours < SteamTimeTTL)) {
				return (uint) (Utilities.GetUnixTime() + steamTimeDifference.Value);
			}

			await TimeSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				steamTimeDifference = SteamTimeDifference;

				if (steamTimeDifference.HasValue && (DateTime.UtcNow.Subtract(LastSteamTimeCheck).TotalHours < SteamTimeTTL)) {
					return (uint) (Utilities.GetUnixTime() + steamTimeDifference.Value);
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
				throw new InvalidOperationException(nameof(ASF.ConfirmationsSemaphore));
			}

			byte confirmationsLimiterDelay = ASF.GlobalConfig?.ConfirmationsLimiterDelay ?? GlobalConfig.DefaultConfirmationsLimiterDelay;

			if (confirmationsLimiterDelay == 0) {
				return;
			}

			await ASF.ConfirmationsSemaphore.WaitAsync().ConfigureAwait(false);

			Utilities.InBackground(
				async () => {
					await Task.Delay(confirmationsLimiterDelay * 1000).ConfigureAwait(false);
					ASF.ConfirmationsSemaphore.Release();
				}
			);
		}

		private async Task<(bool Success, string? Result)> ResolveDeviceID() {
			if (Bot == null) {
				throw new ArgumentNullException(nameof(Bot));
			}

			string? deviceID = await Bot.ArchiHandler.GetTwoFactorDeviceIdentifier(Bot.SteamID).ConfigureAwait(false);

			if (string.IsNullOrEmpty(deviceID)) {
				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);

				return (false, null);
			}

			return (true, deviceID);
		}
	}
}
