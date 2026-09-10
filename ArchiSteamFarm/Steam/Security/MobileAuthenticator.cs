// ----------------------------------------------------------------------------------------------
//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// ----------------------------------------------------------------------------------------------
// |
// Copyright 2015-2026 Łukasz "JustArchi" Domeradzki
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
using System.Buffers.Binary;
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
using SteamKit2;

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

		Span<byte> sharedSecret = stackalloc byte[32];

		if (!Convert.TryFromBase64String(SharedSecret, sharedSecret, out int bytesWritten) && !TryFromBase64StringLenient(SharedSecret, sharedSecret, out bytesWritten)) {
			Bot.ArchiLogger.LogGenericError(Strings.FormatErrorIsInvalid(nameof(SharedSecret)));

			return null;
		}

		sharedSecret = sharedSecret[..bytesWritten];

		Span<byte> timeArray = stackalloc byte[sizeof(long)];
		BinaryPrimitives.WriteInt64BigEndian(timeArray, (long) (time / CodeInterval));

		Span<byte> hash = stackalloc byte[HMACSHA1.HashSizeInBytes];

#pragma warning disable CA5350 // This is actually a fair warning, but there is nothing we can do about Steam using weak cryptographic algorithms
		HMACSHA1.HashData(sharedSecret, timeArray, hash);
#pragma warning restore CA5350 // This is actually a fair warning, but there is nothing we can do about Steam using weak cryptographic algorithms

		// The last 4 bits of the mac say where the code starts
		int start = hash[^1] & 0x0f;

		// Extract those 4 bytes
		Span<byte> bytes = hash[start..(start + 4)];

		// Build the alphanumeric code
		uint fullCode = BinaryPrimitives.ReadUInt32BigEndian(bytes) & 0x7fffffff;

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

		foreach (Confirmation confirmation in response.Confirmations.Where(static confirmation => (confirmation.ConfirmationType == EMobileConfirmationType.Invalid) || !Enum.IsDefined(confirmation.ConfirmationType))) {
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

		Span<byte> identitySecret = stackalloc byte[32];

		if (!Convert.TryFromBase64String(IdentitySecret, identitySecret, out int bytesWritten)) {
			Bot.ArchiLogger.LogGenericError(Strings.FormatErrorIsInvalid(nameof(IdentitySecret)));

				return null;
			}

		identitySecret = identitySecret[..bytesWritten];

		byte tagLength = string.IsNullOrEmpty(tag) ? (byte) 0 : (byte) Math.Min(32, tag.Length);

		Span<byte> buffer = stackalloc byte[sizeof(ulong) + tagLength];
		BinaryPrimitives.WriteUInt64BigEndian(buffer, time);

		if (tagLength > 0) {
			Span<byte> tagBytes = stackalloc byte[Encoding.UTF8.GetMaxByteCount(tagLength)];
			Encoding.UTF8.GetBytes(tag.AsSpan(0, tagLength), tagBytes);

			tagBytes[..tagLength].CopyTo(buffer[sizeof(ulong)..]);
		}

		Span<byte> hash = stackalloc byte[HMACSHA1.HashSizeInBytes];

#pragma warning disable CA5350 // This is actually a fair warning, but there is nothing we can do about Steam using weak cryptographic algorithms
		HMACSHA1.HashData(identitySecret, buffer, hash);
#pragma warning restore CA5350 // This is actually a fair warning, but there is nothing we can do about Steam using weak cryptographic algorithms

		return Convert.ToBase64String(hash);
	}

	private static bool TryFromBase64StringLenient(ReadOnlySpan<char> input, Span<byte> destination, out int bytesWritten) {
		// Some real-world secrets are encoded in a non-canonical way (e.g. with non-zero bits discarded by the padding of the last base64 group), which the standard, strict base64 decoder refuses to decode. This is a lenient fallback decoder that tolerates such input
		bytesWritten = 0;

		int length = input.Length;

		while ((length > 0) && (input[length - 1] == '=')) {
			length--;
		}

		if (length == 0) {
			return true;
		}

		// A single leftover base64 character can't represent a full byte, so it's invalid regardless of the canonical form
		if (length % 4 == 1) {
			return false;
		}

		int requiredBytes = (length * 6) / 8;

		if (destination.Length < requiredBytes) {
			return false;
		}

		int bitBuffer = 0;
		int bitCount = 0;
		int outputIndex = 0;

		foreach (char character in input[..length]) {
			int value = character switch {
				>= 'A' and <= 'Z' => character - 'A',
				>= 'a' and <= 'z' => (character - 'a') + 26,
				>= '0' and <= '9' => (character - '0') + 52,
				'+' => 62,
				'/' => 63,
				_ => -1
			};

			if (value < 0) {
				return false;
			}

			bitBuffer = (bitBuffer << 6) | value;
			bitCount += 6;

			if (bitCount >= 8) {
				bitCount -= 8;
				destination[outputIndex++] = (byte) ((bitBuffer >> bitCount) & 0xFF);
			}
		}

		bytesWritten = outputIndex;

		return true;
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
