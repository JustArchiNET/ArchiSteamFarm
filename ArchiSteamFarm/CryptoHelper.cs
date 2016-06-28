/*
    _                _      _  ____   _                           _____
   / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
  / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
 / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
/_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|

 Copyright 2015-2016 Łukasz "JustArchi" Domeradzki
 Contact: JustArchi@JustArchi.net

 Licensed under the Apache License, Version 2.0 (the "License");
 you may not use this file except in compliance with the License.
 You may obtain a copy of the License at

 http://www.apache.org/licenses/LICENSE-2.0
					
 Unless required by applicable law or agreed to in writing, software
 distributed under the License is distributed on an "AS IS" BASIS,
 WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 See the License for the specific language governing permissions and
 limitations under the License.

*/

using System;
using System.Security.Cryptography;
using System.Text;

namespace ArchiSteamFarm {
	internal static class CryptoHelper {
		internal enum ECryptoMethod : byte {
			PlainText,
			Base64,
			AES
		}

		private static readonly byte[] EncryptionKey = Encoding.UTF8.GetBytes("ArchiSteamFarm");

		internal static string Encrypt(ECryptoMethod cryptoMethod, string decrypted) {
			if (string.IsNullOrEmpty(decrypted)) {
				Logging.LogNullError(nameof(decrypted));
				return null;
			}

			switch (cryptoMethod) {
				case ECryptoMethod.PlainText:
					return decrypted;
				case ECryptoMethod.Base64:
					return EncryptBase64(decrypted);
				case ECryptoMethod.AES:
					return EncryptAES(decrypted);
				default:
					return null;
			}
		}

		internal static string Decrypt(ECryptoMethod cryptoMethod, string encrypted) {
			if (string.IsNullOrEmpty(encrypted)) {
				Logging.LogNullError(nameof(encrypted));
				return null;
			}

			switch (cryptoMethod) {
				case ECryptoMethod.PlainText:
					return encrypted;
				case ECryptoMethod.Base64:
					return DecryptBase64(encrypted);
				case ECryptoMethod.AES:
					return DecryptAES(encrypted);
				default:
					return null;
			}
		}

		private static string EncryptBase64(string decrypted) {
			if (string.IsNullOrEmpty(decrypted)) {
				Logging.LogNullError(nameof(decrypted));
				return null;
			}

			try {
				byte[] data = Encoding.UTF8.GetBytes(decrypted);
				return Convert.ToBase64String(data);
			} catch (Exception e) {
				Logging.LogGenericException(e);
				return null;
			}
		}

		private static string DecryptBase64(string encrypted) {
			if (string.IsNullOrEmpty(encrypted)) {
				Logging.LogNullError(nameof(encrypted));
				return null;
			}

			try {
				byte[] data = Convert.FromBase64String(encrypted);
				return Encoding.UTF8.GetString(data);
			} catch (Exception e) {
				Logging.LogGenericException(e);
				return null;
			}
		}

		private static string EncryptAES(string decrypted) {
			if (string.IsNullOrEmpty(decrypted)) {
				Logging.LogNullError(nameof(decrypted));
				return null;
			}

			try {
				byte[] key;
				using (SHA256Managed sha256 = new SHA256Managed()) {
					key = sha256.ComputeHash(EncryptionKey);
				}

				byte[] data = Encoding.UTF8.GetBytes(decrypted);
				byte[] encrypted = SteamKit2.CryptoHelper.SymmetricEncrypt(data, key);
				return Convert.ToBase64String(encrypted);
			} catch (Exception e) {
				Logging.LogGenericException(e);
				return null;
			}
		}

		private static string DecryptAES(string encrypted) {
			if (string.IsNullOrEmpty(encrypted)) {
				Logging.LogNullError(nameof(encrypted));
				return null;
			}

			try {
				byte[] key;
				using (SHA256Managed sha256 = new SHA256Managed()) {
					key = sha256.ComputeHash(EncryptionKey);
				}

				byte[] data = Convert.FromBase64String(encrypted);
				byte[] decrypted = SteamKit2.CryptoHelper.SymmetricDecrypt(data, key);
				return Encoding.UTF8.GetString(decrypted);
			} catch (Exception e) {
				Logging.LogGenericException(e);
				return null;
			}
		}
	}
}
