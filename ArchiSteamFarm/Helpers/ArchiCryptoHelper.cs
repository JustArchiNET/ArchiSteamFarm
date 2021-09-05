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

#if NETFRAMEWORK
using OperatingSystem = JustArchiNET.Madness.OperatingSystemMadness.OperatingSystem;
#endif
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ArchiSteamFarm.Core;
using CryptSharp.Utility;
using SteamKit2;

namespace ArchiSteamFarm.Helpers {
	public static class ArchiCryptoHelper {
		private const byte DefaultHashLength = 32;
		private const ushort SteamParentalPbkdf2Iterations = 10000;
		private const byte SteamParentalSCryptBlocksCount = 8;
		private const ushort SteamParentalSCryptIterations = 8192;

		private static IEnumerable<byte> SteamParentalCharacters => Enumerable.Range('0', 10).Select(character => (byte) character);

		private static IEnumerable<byte[]> SteamParentalCodes {
			get {
				HashSet<byte> steamParentalCharacters = SteamParentalCharacters.ToHashSet();

				return from a in steamParentalCharacters from b in steamParentalCharacters from c in steamParentalCharacters from d in steamParentalCharacters select new[] { a, b, c, d };
			}
		}

		private static byte[] EncryptionKey = Encoding.UTF8.GetBytes(nameof(ArchiSteamFarm));

		internal static string? Decrypt(ECryptoMethod cryptoMethod, string encryptedString) {
			if (!Enum.IsDefined(typeof(ECryptoMethod), cryptoMethod)) {
				throw new InvalidEnumArgumentException(nameof(cryptoMethod), (int) cryptoMethod, typeof(ECryptoMethod));
			}

			if (string.IsNullOrEmpty(encryptedString)) {
				throw new ArgumentNullException(nameof(encryptedString));
			}

			return cryptoMethod switch {
				ECryptoMethod.PlainText => encryptedString,
				ECryptoMethod.AES => DecryptAES(encryptedString),
				ECryptoMethod.ProtectedDataForCurrentUser => DecryptProtectedDataForCurrentUser(encryptedString),
				_ => throw new ArgumentOutOfRangeException(nameof(cryptoMethod))
			};
		}

		internal static string? Encrypt(ECryptoMethod cryptoMethod, string decryptedString) {
			if (!Enum.IsDefined(typeof(ECryptoMethod), cryptoMethod)) {
				throw new InvalidEnumArgumentException(nameof(cryptoMethod), (int) cryptoMethod, typeof(ECryptoMethod));
			}

			if (string.IsNullOrEmpty(decryptedString)) {
				throw new ArgumentNullException(nameof(decryptedString));
			}

			return cryptoMethod switch {
				ECryptoMethod.PlainText => decryptedString,
				ECryptoMethod.AES => EncryptAES(decryptedString),
				ECryptoMethod.ProtectedDataForCurrentUser => EncryptProtectedDataForCurrentUser(decryptedString),
				_ => throw new ArgumentOutOfRangeException(nameof(cryptoMethod))
			};
		}

		internal static string Hash(EHashingMethod hashingMethod, string stringToHash) {
			if (!Enum.IsDefined(typeof(EHashingMethod), hashingMethod)) {
				throw new InvalidEnumArgumentException(nameof(hashingMethod), (int) hashingMethod, typeof(EHashingMethod));
			}

			if (string.IsNullOrEmpty(stringToHash)) {
				throw new ArgumentNullException(nameof(stringToHash));
			}

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

			if (hashLength == 0) {
				throw new ArgumentOutOfRangeException(nameof(hashLength));
			}

			if (!Enum.IsDefined(typeof(EHashingMethod), hashingMethod)) {
				throw new InvalidEnumArgumentException(nameof(hashingMethod), (int) hashingMethod, typeof(EHashingMethod));
			}

			switch (hashingMethod) {
				case EHashingMethod.PlainText:
					return password;
				case EHashingMethod.SCrypt:
					return SCrypt.ComputeDerivedKey(password, salt, SteamParentalSCryptIterations, SteamParentalSCryptBlocksCount, 1, null, hashLength);
				case EHashingMethod.Pbkdf2:
					using (HMACSHA256 hmacAlgorithm = new(password)) {
						return Pbkdf2.ComputeDerivedKey(hmacAlgorithm, salt, SteamParentalPbkdf2Iterations, hashLength);
					}
				default:
					throw new ArgumentOutOfRangeException(nameof(hashingMethod));
			}
		}

		internal static string? RecoverSteamParentalCode(byte[] passwordHash, byte[] salt, EHashingMethod hashingMethod) {
			if ((passwordHash == null) || (passwordHash.Length == 0)) {
				throw new ArgumentNullException(nameof(passwordHash));
			}

			if ((salt == null) || (salt.Length == 0)) {
				throw new ArgumentNullException(nameof(salt));
			}

			if (!Enum.IsDefined(typeof(EHashingMethod), hashingMethod)) {
				throw new InvalidEnumArgumentException(nameof(hashingMethod), (int) hashingMethod, typeof(EHashingMethod));
			}

			byte[]? password = SteamParentalCodes.AsParallel().FirstOrDefault(passwordToTry => Hash(passwordToTry, salt, (byte) passwordHash.Length, hashingMethod).SequenceEqual(passwordHash));

			return password != null ? Encoding.UTF8.GetString(password) : null;
		}

		internal static void SetEncryptionKey(string key) {
			if (string.IsNullOrEmpty(key)) {
				throw new ArgumentNullException(nameof(key));
			}

			EncryptionKey = Encoding.UTF8.GetBytes(key);
		}

		private static string? DecryptAES(string encryptedString) {
			if (string.IsNullOrEmpty(encryptedString)) {
				throw new ArgumentNullException(nameof(encryptedString));
			}

			try {
				byte[] key;

				using (SHA256 sha256 = SHA256.Create()) {
					key = sha256.ComputeHash(EncryptionKey);
				}

				byte[] decryptedData = Convert.FromBase64String(encryptedString);
				decryptedData = CryptoHelper.SymmetricDecrypt(decryptedData, key);

				return Encoding.UTF8.GetString(decryptedData);
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);

				return null;
			}
		}

		private static string? DecryptProtectedDataForCurrentUser(string encryptedString) {
			if (string.IsNullOrEmpty(encryptedString)) {
				throw new ArgumentNullException(nameof(encryptedString));
			}

#if TARGET_GENERIC || TARGET_WINDOWS
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
#else
			return null;
#endif
		}

		private static string? EncryptAES(string decryptedString) {
			if (string.IsNullOrEmpty(decryptedString)) {
				throw new ArgumentNullException(nameof(decryptedString));
			}

			try {
				byte[] key;

				using (SHA256 sha256 = SHA256.Create()) {
					key = sha256.ComputeHash(EncryptionKey);
				}

				byte[] encryptedData = Encoding.UTF8.GetBytes(decryptedString);
				encryptedData = CryptoHelper.SymmetricEncrypt(encryptedData, key);

				return Convert.ToBase64String(encryptedData);
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);

				return null;
			}
		}

		private static string? EncryptProtectedDataForCurrentUser(string decryptedString) {
			if (string.IsNullOrEmpty(decryptedString)) {
				throw new ArgumentNullException(nameof(decryptedString));
			}

#if TARGET_GENERIC || TARGET_WINDOWS
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
#else
			return null;
#endif
		}

		public enum ECryptoMethod : byte {
			PlainText,
			AES,
			ProtectedDataForCurrentUser
		}

		public enum EHashingMethod : byte {
			PlainText,
			SCrypt,
			Pbkdf2
		}
	}
}
