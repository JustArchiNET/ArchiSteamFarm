// ----------------------------------------------------------------------------------------------
//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// ----------------------------------------------------------------------------------------------
// |
// Copyright 2015-2025 Łukasz "JustArchi" Domeradzki
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
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Steam.Data;
using ArchiSteamFarm.Storage;

namespace ArchiSteamFarm.Steam.Security;

[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
public sealed class MobileAuthenticator : IDisposable {
	internal const byte BackupCodeDigits = 7;
	internal const byte CodeDigits = 5;

	private const byte CodeInterval = 30;

	// For how many minutes we can assume that SteamTimeDifference is correct
	private const byte SteamTimeTTL = 15;

	internal static readonly ImmutableSortedSet<char> CodeCharacters = ['2', '3', '4', '5', '6', '7', '8', '9', 'B', 'C', 'D', 'F', 'G', 'H', 'J', 'K', 'M', 'N', 'P', 'Q', 'R', 'T', 'V', 'W', 'X', 'Y'];

	private static readonly SemaphoreSlim TimeSemaphore = new(1, 1);

	private static DateTime LastSteamTimeCheck;
	private static int? SteamTimeDifference;

	private readonly ArchiCacheable<string> CachedDeviceID;

	private Bot? Bot;

	[JsonInclude]
	[JsonPropertyName("identity_secret")]
	[JsonRequired]
	private string IdentitySecret { get; init; } = "";

	[JsonInclude]
	[JsonPropertyName("shared_secret")]
	[JsonRequired]
	private string SharedSecret { get; init; } = "";

	[JsonConstructor]
	private MobileAuthenticator() => CachedDeviceID = new ArchiCacheable<string>(ResolveDeviceID);

	public void Dispose() => CachedDeviceID.Dispose();

	internal async Task<string?> GenerateToken() {
		if (Bot == null) {
			throw new InvalidOperationException(nameof(Bot));
		}

		ulong time = await GetSteamTime().ConfigureAwait(false);

		if (time == 0) {
			throw new InvalidOperationException(nameof(time));
		}

		return GenerateTokenForTime(time);
	}

	internal string? GenerateTokenForTime(ulong time) {
		ArgumentOutOfRangeException.ThrowIfZero(time);

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
			Bot.ArchiLogger.LogGenericError(Strings.FormatErrorIsInvalid(nameof(SharedSecret)));

			return null;
		}

		byte[] timeArray = BitConverter.GetBytes(time / CodeInterval);

		if (BitConverter.IsLittleEndian) {
			Array.Reverse(timeArray);
		}

#pragma warning disable CA5350 // This is actually a fair warning, but there is nothing we can do about Steam using weak cryptographic algorithms
		byte[] hash = HMACSHA1.HashData(sharedSecret, timeArray);
#pragma warning restore CA5350 // This is actually a fair warning, but there is nothing we can do about Steam using weak cryptographic algorithms

		// The last 4 bits of the mac say where the code starts
		int start = hash[^1] & 0x0f;

		uint fullCode;

		// Extract those 4 bytes
		byte[] bytes = ArrayPool<byte>.Shared.Rent(4);

		try {
			Array.Copy(hash, start, bytes, 0, 4);

			Span<byte> span;

			if (BitConverter.IsLittleEndian) {
				Array.Reverse(bytes);

				span = bytes.AsSpan()[^4..];
			} else {
				span = bytes.AsSpan()[..4];
			}

			// Build the alphanumeric code
			fullCode = BitConverter.ToUInt32(span) & 0x7fffffff;
		} finally {
			ArrayPool<byte>.Shared.Return(bytes);
		}

		return string.Create(
			CodeDigits, fullCode, static (buffer, state) => {
				for (byte i = 0; i < CodeDigits; i++) {
					buffer[i] = CodeCharacters[(byte) (state % CodeCharacters.Count)];
					state /= (byte) CodeCharacters.Count;
				}
			}
		);
	}

	internal async Task<ImmutableHashSet<Confirmation>?> GetConfirmations() {
		if (Bot == null) {
			throw new InvalidOperationException(nameof(Bot));
		}

		(_, string? deviceID) = await CachedDeviceID.GetValue(ECacheFallback.SuccessPreviously).ConfigureAwait(false);

		if (string.IsNullOrEmpty(deviceID)) {
			Bot.ArchiLogger.LogGenericError(Strings.FormatWarningFailedWithError(nameof(deviceID)));

			return null;
		}

		await LimitConfirmationsRequestsAsync().ConfigureAwait(false);

		ulong time = await GetSteamTime().ConfigureAwait(false);

		if (time == 0) {
			throw new InvalidOperationException(nameof(time));
		}

		string? confirmationHash = GenerateConfirmationHash(time, "conf");

		if (string.IsNullOrEmpty(confirmationHash)) {
			Bot.ArchiLogger.LogNullError(confirmationHash);

			return null;
		}

		ConfirmationsResponse? response = await Bot.ArchiWebHandler.GetConfirmations(deviceID, confirmationHash, time).ConfigureAwait(false);

		if (response?.Success != true) {
			return null;
		}

		foreach (Confirmation confirmation in response.Confirmations.Where(static confirmation => (confirmation.ConfirmationType == Confirmation.EConfirmationType.Unknown) || !Enum.IsDefined(confirmation.ConfirmationType))) {
			Bot.ArchiLogger.LogGenericError(Strings.FormatWarningUnknownValuePleaseReport(nameof(confirmation.ConfirmationType), $"{confirmation.ConfirmationType} ({confirmation.ConfirmationTypeName ?? "null"})"));
		}

		return response.Confirmations;
	}

	internal async Task<ulong> GetSteamTime() {
		if (Bot == null) {
			throw new InvalidOperationException(nameof(Bot));
		}

		int? steamTimeDifference = SteamTimeDifference;

		if (steamTimeDifference.HasValue && (DateTime.UtcNow.Subtract(LastSteamTimeCheck).TotalMinutes < SteamTimeTTL)) {
			return Utilities.MathAdd(Utilities.GetUnixTime(), steamTimeDifference.Value);
		}

		await TimeSemaphore.WaitAsync().ConfigureAwait(false);

		try {
			steamTimeDifference = SteamTimeDifference;

			if (steamTimeDifference.HasValue && (DateTime.UtcNow.Subtract(LastSteamTimeCheck).TotalMinutes < SteamTimeTTL)) {
				return Utilities.MathAdd(Utilities.GetUnixTime(), steamTimeDifference.Value);
			}

			ulong serverTime = await Bot.ArchiHandler.GetServerTime().ConfigureAwait(false);

			if (serverTime == 0) {
				return Utilities.GetUnixTime();
			}

			// We assume that the difference between times will be within int range, therefore we accept underflow here (for subtraction), and since we cast that result to int afterwards, we also accept overflow for the cast itself
			steamTimeDifference = unchecked((int) (serverTime - Utilities.GetUnixTime()));

			SteamTimeDifference = steamTimeDifference;
			LastSteamTimeCheck = DateTime.UtcNow;
		} finally {
			TimeSemaphore.Release();
		}

		return Utilities.MathAdd(Utilities.GetUnixTime(), steamTimeDifference.Value);
	}

	internal async Task<bool> HandleConfirmations(IReadOnlyCollection<Confirmation> confirmations, bool accept) {
		if ((confirmations == null) || (confirmations.Count == 0)) {
			throw new ArgumentNullException(nameof(confirmations));
		}

		if (Bot == null) {
			throw new InvalidOperationException(nameof(Bot));
		}

		(_, string? deviceID) = await CachedDeviceID.GetValue(ECacheFallback.SuccessPreviously).ConfigureAwait(false);

		if (string.IsNullOrEmpty(deviceID)) {
			Bot.ArchiLogger.LogGenericError(Strings.FormatWarningFailedWithError(nameof(deviceID)));

			return false;
		}

		ulong time = await GetSteamTime().ConfigureAwait(false);

		if (time == 0) {
			throw new InvalidOperationException(nameof(time));
		}

		string? confirmationHash = GenerateConfirmationHash(time, "conf");

		if (string.IsNullOrEmpty(confirmationHash)) {
			Bot.ArchiLogger.LogNullError(confirmationHash);

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
			bool? confirmationResult = await Bot.ArchiWebHandler.HandleConfirmation(deviceID, confirmationHash, time, confirmation.ID, confirmation.Nonce, accept).ConfigureAwait(false);

			if (!confirmationResult.HasValue) {
				return false;
			}
		}

		return true;
	}

	internal void Init(Bot bot) {
		ArgumentNullException.ThrowIfNull(bot);

		Bot = bot;
	}

	internal void OnInitModules() => Utilities.InBackground(() => CachedDeviceID.Reset());

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

	private string? GenerateConfirmationHash(ulong time, string? tag = null) {
		ArgumentOutOfRangeException.ThrowIfZero(time);

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
			Bot.ArchiLogger.LogGenericError(Strings.FormatErrorIsInvalid(nameof(IdentitySecret)));

			return null;
		}

		byte bufferSize = 8;

		if (!string.IsNullOrEmpty(tag)) {
			bufferSize += (byte) Math.Min(32, tag.Length);
		}

		byte[] timeArray = BitConverter.GetBytes(time);

		if (BitConverter.IsLittleEndian) {
			Array.Reverse(timeArray);
		}

		byte[] hash;

		byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);

		try {
			Array.Copy(timeArray, buffer, timeArray.Length);

			if (!string.IsNullOrEmpty(tag)) {
				Array.Copy(Encoding.UTF8.GetBytes(tag), 0, buffer, timeArray.Length, bufferSize - timeArray.Length);
			}

#pragma warning disable CA5350 // This is actually a fair warning, but there is nothing we can do about Steam using weak cryptographic algorithms
			hash = HMACSHA1.HashData(identitySecret, buffer.AsSpan()[..bufferSize]);
#pragma warning restore CA5350 // This is actually a fair warning, but there is nothing we can do about Steam using weak cryptographic algorithms
		} finally {
			ArrayPool<byte>.Shared.Return(buffer);
		}

		return Convert.ToBase64String(hash);
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

		Utilities.InBackground(async () => {
				await Task.Delay(confirmationsLimiterDelay * 1000).ConfigureAwait(false);
				ASF.ConfirmationsSemaphore.Release();
			}
		);
	}

	private async Task<(bool Success, string? Result)> ResolveDeviceID(CancellationToken cancellationToken = default) {
		if (Bot == null) {
			throw new InvalidOperationException(nameof(Bot));
		}

		string? deviceID = await Bot.ArchiHandler.GetTwoFactorDeviceIdentifier(Bot.SteamID).ConfigureAwait(false);

		if (string.IsNullOrEmpty(deviceID)) {
			Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);

			return (false, null);
		}

		return (true, deviceID);
	}
}
