//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2023 Łukasz "JustArchi" Domeradzki
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
using System.Collections.Frozen;
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

	private static readonly FrozenSet<string> ForbiddenCryptKeyPhrases = new HashSet<string>(3, StringComparer.InvariantCultureIgnoreCase) { "crypt", "key", "cryptkey" }.ToFrozenSet(StringComparer.InvariantCultureIgnoreCase);

	private static IEnumerable<byte> SteamParentalCharacters => Enumerable.Range('0', 10).Select(static character => (byte) character);

	private static IEnumerable<byte[]> SteamParentalCodes {
		get {
			HashSet<byte> steamParentalCharacters = SteamParentalCharacters.ToHashSet();

			return from a in steamParentalCharacters from b in steamParentalCharacters from c in steamParentalCharacters from d in steamParentalCharacters select new[] { a, b, c, d };
		}
	}

	private static byte[] EncryptionKey = Encoding.UTF8.GetBytes(nameof(ArchiSteamFarm));

	internal static async Task<string?> Decrypt(ECryptoMethod cryptoMethod, string encryptedString) {
		if (!Enum.IsDefined(cryptoMethod)) {
			throw new InvalidEnumArgumentException(nameof(cryptoMethod), (int) cryptoMethod, typeof(ECryptoMethod));
		}

		ArgumentException.ThrowIfNullOrEmpty(encryptedString);

		return cryptoMethod switch {
			ECryptoMethod.AES => DecryptAES(encryptedString),
			ECryptoMethod.EnvironmentVariable => Environment.GetEnvironmentVariable(encryptedString)?.Trim(),
			ECryptoMethod.File => await ReadFromFile(encryptedString).ConfigureAwait(false),
			ECryptoMethod.PlainText => encryptedString,
			ECryptoMethod.ProtectedDataForCurrentUser => DecryptProtectedDataForCurrentUser(encryptedString),
			_ => throw new InvalidOperationException(nameof(cryptoMethod))
		};
	}

	internal static string? Encrypt(ECryptoMethod cryptoMethod, string decryptedString) {
		if (!Enum.IsDefined(cryptoMethod)) {
			throw new InvalidEnumArgumentException(nameof(cryptoMethod), (int) cryptoMethod, typeof(ECryptoMethod));
		}

		ArgumentException.ThrowIfNullOrEmpty(decryptedString);

		return cryptoMethod switch {
			ECryptoMethod.AES => EncryptAES(decryptedString),
			ECryptoMethod.EnvironmentVariable => decryptedString,
			ECryptoMethod.File => decryptedString,
			ECryptoMethod.PlainText => decryptedString,
			ECryptoMethod.ProtectedDataForCurrentUser => EncryptProtectedDataForCurrentUser(decryptedString),
			_ => throw new InvalidOperationException(nameof(cryptoMethod))
		};
	}

	internal static string Hash(EHashingMethod hashingMethod, string stringToHash) {
		if (!Enum.IsDefined(hashingMethod)) {
			throw new InvalidEnumArgumentException(nameof(hashingMethod), (int) hashingMethod, typeof(EHashingMethod));
		}

		ArgumentException.ThrowIfNullOrEmpty(stringToHash);

		if (hashingMethod == EHashingMethod.PlainText) {
			return stringToHash;
		}

		byte[] passwordBytes = Encoding.UTF8.GetBytes(stringToHash);
		byte[] hashBytes = Hash(passwordBytes, EncryptionKey, DefaultHashLength, hashingMethod);

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

		switch (hashingMethod) {
			case EHashingMethod.PlainText:
				return password;
			case EHashingMethod.SCrypt:
				return SCrypt.ComputeDerivedKey(password, salt, SteamParentalSCryptIterations, SteamParentalSCryptBlocksCount, 1, null, hashLength);
			case EHashingMethod.Pbkdf2:
				using (HMACSHA256 hashAlgorithm = new(password)) {
					return Pbkdf2.ComputeDerivedKey(hashAlgorithm, salt, SteamParentalPbkdf2Iterations, hashLength);
				}
			default:
				throw new InvalidOperationException(nameof(hashingMethod));
		}
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

		Utilities.InBackground(
			() => {
				(bool isWeak, string? reason) = Utilities.TestPasswordStrength(key, ForbiddenCryptKeyPhrases);

				if (isWeak) {
					ASF.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningWeakCryptKey, reason));
				}
			}
		);

		byte[] encryptionKey = Encoding.UTF8.GetBytes(key);

		if (encryptionKey.Length < MinimumRecommendedCryptKeyBytes) {
			ASF.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningTooShortCryptKey, MinimumRecommendedCryptKeyBytes));
		}

		HasDefaultCryptKey = encryptionKey.SequenceEqual(EncryptionKey);
		EncryptionKey = encryptionKey;
	}

	private static string? DecryptAES(string encryptedString) {
		ArgumentException.ThrowIfNullOrEmpty(encryptedString);

		try {
			byte[] key = SHA256.HashData(EncryptionKey);

			byte[] decryptedData = Convert.FromBase64String(encryptedString);
			decryptedData = CryptoHelper.SymmetricDecrypt(decryptedData, key);

			return Encoding.UTF8.GetString(decryptedData);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);

			return null;
		}
	}

	private static string? DecryptProtectedDataForCurrentUser(string encryptedString) {
		ArgumentException.ThrowIfNullOrEmpty(encryptedString);

		if (!OperatingSystem.IsWindows()) {
			return null;
		}

		try {
			byte[] decryptedData = ProtectedData.Unprotect(
				Convert.FromBase64String(encryptedString),
				EncryptionKey,
				DataProtectionScope.CurrentUser
			);

			return Encoding.UTF8.GetString(decryptedData);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);

			return null;
		}
	}

	private static string? EncryptAES(string decryptedString) {
		ArgumentException.ThrowIfNullOrEmpty(decryptedString);

		try {
			byte[] key = SHA256.HashData(EncryptionKey);

			byte[] encryptedData = Encoding.UTF8.GetBytes(decryptedString);
			encryptedData = CryptoHelper.SymmetricEncrypt(encryptedData, key);

			return Convert.ToBase64String(encryptedData);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);

			return null;
		}
	}

	private static string? EncryptProtectedDataForCurrentUser(string decryptedString) {
		ArgumentException.ThrowIfNullOrEmpty(decryptedString);

		if (!OperatingSystem.IsWindows()) {
			return null;
		}

		try {
			byte[] encryptedData = ProtectedData.Protect(
				Encoding.UTF8.GetBytes(decryptedString),
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
