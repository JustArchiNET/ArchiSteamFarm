//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// 
// Copyright 2015-2018 Łukasz "JustArchi" Domeradzki
// Contact: JustArchi@JustArchi.net
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
// http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Security.Cryptography;
using System.Text;
using ArchiSteamFarm.Localization;
using SteamKit2;

namespace ArchiSteamFarm {
	internal static class ArchiCryptoHelper {
		private static byte[] EncryptionKey = Encoding.UTF8.GetBytes(nameof(ArchiSteamFarm));

		internal static string Decrypt(ECryptoMethod cryptoMethod, string encrypted) {
			if (string.IsNullOrEmpty(encrypted)) {
				ASF.ArchiLogger.LogNullError(nameof(encrypted));
				return null;
			}

			switch (cryptoMethod) {
				case ECryptoMethod.PlainText:
					return encrypted;
				case ECryptoMethod.AES:
					return DecryptAES(encrypted);
				case ECryptoMethod.ProtectedDataForCurrentUser:
					return DecryptProtectedDataForCurrentUser(encrypted);
				default:
					ASF.ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(cryptoMethod), cryptoMethod));
					return null;
			}
		}

		internal static string Encrypt(ECryptoMethod cryptoMethod, string decrypted) {
			if (string.IsNullOrEmpty(decrypted)) {
				ASF.ArchiLogger.LogNullError(nameof(decrypted));
				return null;
			}

			switch (cryptoMethod) {
				case ECryptoMethod.PlainText:
					return decrypted;
				case ECryptoMethod.AES:
					return EncryptAES(decrypted);
				case ECryptoMethod.ProtectedDataForCurrentUser:
					return EncryptProtectedDataForCurrentUser(decrypted);
				default:
					ASF.ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(cryptoMethod), cryptoMethod));
					return null;
			}
		}

		internal static void SetEncryptionKey(string key) {
			if (string.IsNullOrEmpty(key)) {
				ASF.ArchiLogger.LogNullError(nameof(key));
				return;
			}

			EncryptionKey = Encoding.UTF8.GetBytes(key);
		}

		private static string DecryptAES(string encrypted) {
			if (string.IsNullOrEmpty(encrypted)) {
				ASF.ArchiLogger.LogNullError(nameof(encrypted));
				return null;
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

		private static string DecryptProtectedDataForCurrentUser(string encrypted) {
			if (string.IsNullOrEmpty(encrypted)) {
				ASF.ArchiLogger.LogNullError(nameof(encrypted));
				return null;
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

		private static string EncryptAES(string decrypted) {
			if (string.IsNullOrEmpty(decrypted)) {
				ASF.ArchiLogger.LogNullError(nameof(decrypted));
				return null;
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

		private static string EncryptProtectedDataForCurrentUser(string decrypted) {
			if (string.IsNullOrEmpty(decrypted)) {
				ASF.ArchiLogger.LogNullError(nameof(decrypted));
				return null;
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

		internal enum ECryptoMethod : byte {
			PlainText,
			AES,
			ProtectedDataForCurrentUser
		}
	}
}
