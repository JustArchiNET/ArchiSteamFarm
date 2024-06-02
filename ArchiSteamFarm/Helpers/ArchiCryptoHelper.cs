// ----------------------------------------------------------------------------------------------
//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// ----------------------------------------------------------------------------------------------
// |
// Copyright 2015-2024 Łukasz "JustArchi" Domeradzki
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
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Localization;
using CryptSharp.Utility;
using SteamKit2;

namespace ArchiSteamFarm.Helpers;

public static class ArchiCryptoHelper {
	private const byte DefaultHashLength = 32;
	private const byte MinimumRecommendedCryptKeyBytes = 32;
	private const ushort SteamParentalPbkdf2Iterations = 10000;
	private const byte SteamParentalSCryptBlocksCount = 8;
	private const ushort SteamParentalSCryptIterations = 8192;

	internal static bool HasDefaultCryptKey { get; private set; } = true;

	private static IEnumerable<byte> SteamParentalCharacters => Enumerable.Range('0', 10).Select(static character => (byte) character);

	private static IEnumerable<byte[]> SteamParentalCodes {
		get {
			HashSet<byte> steamParentalCharacters = SteamParentalCharacters.ToHashSet();

			return from a in steamParentalCharacters from b in steamParentalCharacters from c in steamParentalCharacters from d in steamParentalCharacters select new[] { a, b, c, d };
		}
	}

	private static byte[] EncryptionKey = Encoding.UTF8.GetBytes(nameof(ArchiSteamFarm));

	internal static async Task<string?> Decrypt(ECryptoMethod cryptoMethod, string text) {
		if (!Enum.IsDefined(cryptoMethod)) {
			throw new InvalidEnumArgumentException(nameof(cryptoMethod), (int) cryptoMethod, typeof(ECryptoMethod));
		}

		ArgumentException.ThrowIfNullOrEmpty(text);

		return cryptoMethod switch {
			ECryptoMethod.AES => DecryptAES(text),
			ECryptoMethod.EnvironmentVariable => Environment.GetEnvironmentVariable(text)?.Trim(),
			ECryptoMethod.File => await ReadFromFile(text).ConfigureAwait(false),
			ECryptoMethod.PlainText => text,
			ECryptoMethod.ProtectedDataForCurrentUser => DecryptProtectedDataForCurrentUser(text),
			_ => throw new InvalidOperationException(nameof(cryptoMethod))
		};
	}

	internal static string? Encrypt(ECryptoMethod cryptoMethod, string text) {
		if (!Enum.IsDefined(cryptoMethod)) {
			throw new InvalidEnumArgumentException(nameof(cryptoMethod), (int) cryptoMethod, typeof(ECryptoMethod));
		}

		ArgumentException.ThrowIfNullOrEmpty(text);

		return cryptoMethod switch {
			ECryptoMethod.AES => EncryptAES(text),
			ECryptoMethod.EnvironmentVariable => text,
			ECryptoMethod.File => text,
			ECryptoMethod.PlainText => text,
			ECryptoMethod.ProtectedDataForCurrentUser => EncryptProtectedDataForCurrentUser(text),
			_ => throw new InvalidOperationException(nameof(cryptoMethod))
		};
	}

	internal static string Hash(EHashingMethod hashingMethod, string text) {
		if (!Enum.IsDefined(hashingMethod)) {
			throw new InvalidEnumArgumentException(nameof(hashingMethod), (int) hashingMethod, typeof(EHashingMethod));
		}

		ArgumentException.ThrowIfNullOrEmpty(text);

		if (hashingMethod == EHashingMethod.PlainText) {
			return text;
		}

		byte[] textBytes = Encoding.UTF8.GetBytes(text);
		byte[] hashBytes = Hash(textBytes, EncryptionKey, DefaultHashLength, hashingMethod);

		return Convert.ToBase64String(hashBytes);
	}

	internal static byte[] Hash(byte[] password, byte[] salt, byte hashLength, EHashingMethod hashingMethod) {
		if ((password == null) || (password.Length == 0)) {
			throw new ArgumentNullException(nameof(password));
		}

		if ((salt == null) || (salt.Length == 0)) {
			throw new ArgumentNullException(nameof(salt));
		}

		ArgumentOutOfRangeException.ThrowIfZero(hashLength);

		if (!Enum.IsDefined(hashingMethod)) {
			throw new InvalidEnumArgumentException(nameof(hashingMethod), (int) hashingMethod, typeof(EHashingMethod));
		}

		return hashingMethod switch {
			EHashingMethod.PlainText => password,
			EHashingMethod.SCrypt => SCrypt.ComputeDerivedKey(password, salt, SteamParentalSCryptIterations, SteamParentalSCryptBlocksCount, 1, null, hashLength),
			EHashingMethod.Pbkdf2 => Rfc2898DeriveBytes.Pbkdf2(password, salt, SteamParentalPbkdf2Iterations, HashAlgorithmName.SHA256, hashLength),
			_ => throw new InvalidOperationException(nameof(hashingMethod))
		};
	}

	internal static bool HasTransformation(this ECryptoMethod cryptoMethod) =>
		cryptoMethod switch {
			ECryptoMethod.AES => true,
			ECryptoMethod.ProtectedDataForCurrentUser => true,
			_ => false
		};

	internal static string? RecoverSteamParentalCode(byte[] passwordHash, byte[] salt, EHashingMethod hashingMethod) {
		if ((passwordHash == null) || (passwordHash.Length == 0)) {
			throw new ArgumentNullException(nameof(passwordHash));
		}

		if (passwordHash.Length > byte.MaxValue) {
			throw new ArgumentOutOfRangeException(nameof(passwordHash));
		}

		if ((salt == null) || (salt.Length == 0)) {
			throw new ArgumentNullException(nameof(salt));
		}

		if (!Enum.IsDefined(hashingMethod)) {
			throw new InvalidEnumArgumentException(nameof(hashingMethod), (int) hashingMethod, typeof(EHashingMethod));
		}

		byte[]? password = SteamParentalCodes.AsParallel().FirstOrDefault(passwordToTry => Hash(passwordToTry, salt, (byte) passwordHash.Length, hashingMethod).SequenceEqual(passwordHash));

		return password != null ? Encoding.UTF8.GetString(password) : null;
	}

	internal static void SetEncryptionKey(string key) {
		ArgumentException.ThrowIfNullOrEmpty(key);

		if (!HasDefaultCryptKey) {
			ASF.ArchiLogger.LogGenericError(Strings.ErrorAborted);

			return;
		}

		byte[] encryptionKey = Encoding.UTF8.GetBytes(key);

		if (encryptionKey.Length < MinimumRecommendedCryptKeyBytes) {
			ASF.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningTooShortCryptKey, MinimumRecommendedCryptKeyBytes));
		}

		HasDefaultCryptKey = encryptionKey.SequenceEqual(EncryptionKey);
		EncryptionKey = encryptionKey;
	}

	internal static bool VerifyHash(EHashingMethod hashingMethod, string text, string hash) {
		if (!Enum.IsDefined(hashingMethod)) {
			throw new InvalidEnumArgumentException(nameof(hashingMethod), (int) hashingMethod, typeof(EHashingMethod));
		}

		ArgumentException.ThrowIfNullOrEmpty(text);
		ArgumentException.ThrowIfNullOrEmpty(hash);

		// Text is always provided as plain text
		byte[] textBytes = Encoding.UTF8.GetBytes(text);
		textBytes = Hash(textBytes, EncryptionKey, DefaultHashLength, hashingMethod);

		// Hash is either plain text password (when EHashingMethod.PlainText), or base64-encoded hash
		byte[] hashBytes = hashingMethod == EHashingMethod.PlainText ? Encoding.UTF8.GetBytes(hash) : Convert.FromBase64String(hash);

		return CryptographicOperations.FixedTimeEquals(textBytes, hashBytes);
	}

	private static string? DecryptAES(string text) {
		ArgumentException.ThrowIfNullOrEmpty(text);

		try {
			byte[] key = SHA256.HashData(EncryptionKey);

			byte[] decryptedData = Convert.FromBase64String(text);
			decryptedData = CryptoHelper.SymmetricDecrypt(decryptedData, key);

			return Encoding.UTF8.GetString(decryptedData);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);

			return null;
		}
	}

	private static string? DecryptProtectedDataForCurrentUser(string text) {
		ArgumentException.ThrowIfNullOrEmpty(text);

		if (!OperatingSystem.IsWindows()) {
			return null;
		}

		try {
			byte[] decryptedData = ProtectedData.Unprotect(
				Convert.FromBase64String(text),
				EncryptionKey,
				DataProtectionScope.CurrentUser
			);

			return Encoding.UTF8.GetString(decryptedData);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);

			return null;
		}
	}

	private static string? EncryptAES(string text) {
		ArgumentException.ThrowIfNullOrEmpty(text);

		try {
			byte[] key = SHA256.HashData(EncryptionKey);

			byte[] encryptedData = Encoding.UTF8.GetBytes(text);
			encryptedData = CryptoHelper.SymmetricEncrypt(encryptedData, key);

			return Convert.ToBase64String(encryptedData);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);

			return null;
		}
	}

	private static string? EncryptProtectedDataForCurrentUser(string text) {
		ArgumentException.ThrowIfNullOrEmpty(text);

		if (!OperatingSystem.IsWindows()) {
			return null;
		}

		try {
			byte[] encryptedData = ProtectedData.Protect(
				Encoding.UTF8.GetBytes(text),
				EncryptionKey,
				DataProtectionScope.CurrentUser
			);

			return Convert.ToBase64String(encryptedData);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);

			return null;
		}
	}

	private static async Task<string?> ReadFromFile(string filePath) {
		ArgumentException.ThrowIfNullOrEmpty(filePath);

		if (!File.Exists(filePath)) {
			return null;
		}

		string text;

		try {
			text = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);

			return null;
		}

		return text.Trim();
	}

	public enum ECryptoMethod : byte {
		PlainText,
		AES,
		ProtectedDataForCurrentUser,
		EnvironmentVariable,
		File
	}

	public enum EHashingMethod : byte {
		PlainText,
		SCrypt,
		Pbkdf2
	}
}
