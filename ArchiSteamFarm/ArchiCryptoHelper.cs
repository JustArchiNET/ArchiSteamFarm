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
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CryptSharp.Utility;
using SteamKit2;

namespace ArchiSteamFarm {
	public static class ArchiCryptoHelper {
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

		internal static string? Decrypt(ECryptoMethod cryptoMethod, string encrypted) {
			if (!Enum.IsDefined(typeof(ECryptoMethod), cryptoMethod) || string.IsNullOrEmpty(encrypted)) {
				throw new ArgumentNullException(nameof(cryptoMethod) + " || " + nameof(encrypted));
			}

			return cryptoMethod switch {
				ECryptoMethod.PlainText => encrypted,
				ECryptoMethod.AES => DecryptAES(encrypted),
				ECryptoMethod.ProtectedDataForCurrentUser => DecryptProtectedDataForCurrentUser(encrypted),
				_ => throw new ArgumentOutOfRangeException(nameof(cryptoMethod))
			};
		}

		internal static string? Encrypt(ECryptoMethod cryptoMethod, string decrypted) {
			if (!Enum.IsDefined(typeof(ECryptoMethod), cryptoMethod) || string.IsNullOrEmpty(decrypted)) {
				throw new ArgumentNullException(nameof(cryptoMethod) + " || " + nameof(decrypted));
			}

			return cryptoMethod switch {
				ECryptoMethod.PlainText => decrypted,
				ECryptoMethod.AES => EncryptAES(decrypted),
				ECryptoMethod.ProtectedDataForCurrentUser => EncryptProtectedDataForCurrentUser(decrypted),
				_ => throw new ArgumentOutOfRangeException(nameof(cryptoMethod))
			};
		}

		internal static IEnumerable<byte>? GenerateSteamParentalHash(byte[] password, byte[] salt, byte hashLength, ESteamParentalAlgorithm steamParentalAlgorithm) {
			if ((password == null) || (salt == null) || (hashLength == 0) || !Enum.IsDefined(typeof(ESteamParentalAlgorithm), steamParentalAlgorithm)) {
				throw new ArgumentNullException(nameof(password) + " || " + nameof(salt) + " || " + nameof(hashLength) + " || " + nameof(steamParentalAlgorithm));
			}

			switch (steamParentalAlgorithm) {
				case ESteamParentalAlgorithm.Pbkdf2:
					using (HMACSHA256 hmacAlgorithm = new HMACSHA256(password)) {
						return Pbkdf2.ComputeDerivedKey(hmacAlgorithm, salt, SteamParentalPbkdf2Iterations, hashLength);
					}
				case ESteamParentalAlgorithm.SCrypt:
					return SCrypt.ComputeDerivedKey(password, salt, SteamParentalSCryptIterations, SteamParentalSCryptBlocksCount, 1, null, hashLength);
				default:
					throw new ArgumentOutOfRangeException(nameof(steamParentalAlgorithm));
			}
		}

		internal static string? RecoverSteamParentalCode(byte[] passwordHash, byte[] salt, ESteamParentalAlgorithm steamParentalAlgorithm) {
			if ((passwordHash == null) || (salt == null) || !Enum.IsDefined(typeof(ESteamParentalAlgorithm), steamParentalAlgorithm)) {
				throw new ArgumentNullException(nameof(passwordHash) + " || " + nameof(salt) + " || " + nameof(steamParentalAlgorithm));
			}

			byte[]? password = SteamParentalCodes.AsParallel().FirstOrDefault(passwordToTry => GenerateSteamParentalHash(passwordToTry, salt, (byte) passwordHash.Length, steamParentalAlgorithm)?.SequenceEqual(passwordHash) == true);

			return password != null ? Encoding.UTF8.GetString(password) : null;
		}

		internal static void SetEncryptionKey(string key) {
			if (string.IsNullOrEmpty(key)) {
				throw new ArgumentNullException(nameof(key));
			}

			EncryptionKey = Encoding.UTF8.GetBytes(key);
		}

		private static string? DecryptAES(string encrypted) {
			if (string.IsNullOrEmpty(encrypted)) {
				throw new ArgumentNullException(nameof(encrypted));
			}

			try {
				byte[] key;

				using (SHA256 sha256 = SHA256.Create()) {
					key = sha256.ComputeHash(EncryptionKey);
				}

				byte[] decryptedData = Convert.FromBase64String(encrypted);
				decryptedData = CryptoHelper.SymmetricDecrypt(decryptedData, key);

				return Encoding.UTF8.GetString(decryptedData);
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);

				return null;
			}
		}

		private static string? DecryptProtectedDataForCurrentUser(string encrypted) {
			if (string.IsNullOrEmpty(encrypted)) {
				throw new ArgumentNullException(nameof(encrypted));
			}

			try {
				byte[] decryptedData = ProtectedData.Unprotect(
					Convert.FromBase64String(encrypted),
					EncryptionKey, // This is used as salt only and it's fine that it's known
					DataProtectionScope.CurrentUser
				);

				return Encoding.UTF8.GetString(decryptedData);
			} catch (PlatformNotSupportedException e) {
				ASF.ArchiLogger.LogGenericWarningException(e);

				return null;
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);

				return null;
			}
		}

		private static string? EncryptAES(string decrypted) {
			if (string.IsNullOrEmpty(decrypted)) {
				throw new ArgumentNullException(nameof(decrypted));
			}

			try {
				byte[] key;

				using (SHA256 sha256 = SHA256.Create()) {
					key = sha256.ComputeHash(EncryptionKey);
				}

				byte[] encryptedData = Encoding.UTF8.GetBytes(decrypted);
				encryptedData = CryptoHelper.SymmetricEncrypt(encryptedData, key);

				return Convert.ToBase64String(encryptedData);
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);

				return null;
			}
		}

		private static string? EncryptProtectedDataForCurrentUser(string decrypted) {
			if (string.IsNullOrEmpty(decrypted)) {
				throw new ArgumentNullException(nameof(decrypted));
			}

			try {
				byte[] encryptedData = ProtectedData.Protect(
					Encoding.UTF8.GetBytes(decrypted),
					EncryptionKey, // This is used as salt only and it's fine that it's known
					DataProtectionScope.CurrentUser
				);

				return Convert.ToBase64String(encryptedData);
			} catch (PlatformNotSupportedException e) {
				ASF.ArchiLogger.LogGenericWarningException(e);

				return null;
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);

				return null;
			}
		}

		public enum ECryptoMethod : byte {
			PlainText,
			AES,
			ProtectedDataForCurrentUser
		}

		internal enum ESteamParentalAlgorithm : byte {
			SCrypt,
			Pbkdf2
		}
	}
}
