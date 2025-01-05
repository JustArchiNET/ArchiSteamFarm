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
using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static ArchiSteamFarm.Helpers.ArchiCryptoHelper;

namespace ArchiSteamFarm.Tests;

#pragma warning disable CA1812 // False positive, the class is used during MSTest
[TestClass]
internal sealed class ArchiCryptoHelper {
	private const string TestPassword = "a2o41PuPdZNDLw9AT6dZt5pLVC23MN9O7NfKI4a0MWJgWWIAVGt3naYiIA0BhPel";

	[DataRow(ECryptoMethod.PlainText)]
	[DataRow(ECryptoMethod.AES)]
	[DataTestMethod]
	internal async Task CanEncryptDecrypt(ECryptoMethod cryptoMethod) {
		if (!Enum.IsDefined(cryptoMethod)) {
			throw new InvalidEnumArgumentException(nameof(cryptoMethod), (int) cryptoMethod, typeof(ECryptoMethod));
		}

		string? encrypted = Encrypt(cryptoMethod, TestPassword);

		Assert.IsNotNull(encrypted);

		string? decrypted = await Decrypt(cryptoMethod, encrypted).ConfigureAwait(false);

		Assert.IsNotNull(decrypted);
		Assert.AreEqual(TestPassword, decrypted);
	}

	[TestMethod]
	internal async Task CanEncryptDecryptProtectedDataForCurrentUser() {
		if (!OperatingSystem.IsWindows()) {
			// Not supported on other platforms than Windows
			return;
		}

		await CanEncryptDecrypt(ECryptoMethod.ProtectedDataForCurrentUser).ConfigureAwait(false);
	}
}
#pragma warning restore CA1812 // False positive, the class is used during MSTest
