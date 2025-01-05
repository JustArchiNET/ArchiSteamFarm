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
using System.Reflection;
using System.Text.Json.Nodes;
using ArchiSteamFarm.Helpers.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ArchiSteamFarm.Tests;

#pragma warning disable CA1812 // False positive, the class is used during MSTest
[TestClass]
internal sealed class MobileAuthenticator {
	[DataRow("qrg+wW8/u/TDt2i/+FQuPhuVrmY=", (ulong) 1337, "QFo72j9TnG+uRXe9EIJs4zyBPo0=")]
	[DataRow("qrg+wW8/u/TDt2i/+FQuPhuVrmY=", (ulong) 1337, "mYbCKs8ZvsVN2odCMxpvidrIu1c=", "conf")]
	[DataRow("qrg+wW8/u/TDt2i/+FQuPhuVrmY=", (ulong) 1723332288, "hiEx+JBqJqFJnSSL+dEthPHOmsc=")]
	[DataRow("qrg+wW8/u/TDt2i/+FQuPhuVrmY=", (ulong) 1723332288, "hpZUxyNgwBvtKPROvedjuvVPQiE=", "conf")]
	[DataTestMethod]
	internal void GenerateConfirmationHash(string identitySecret, ulong time, string expectedCode, string? tag = null) {
		ArgumentException.ThrowIfNullOrEmpty(identitySecret);
		ArgumentOutOfRangeException.ThrowIfZero(time);
		ArgumentException.ThrowIfNullOrEmpty(expectedCode);

		MethodInfo? method = typeof(Steam.Security.MobileAuthenticator).GetMethod(nameof(GenerateConfirmationHash), BindingFlags.Instance | BindingFlags.NonPublic, [typeof(ulong), typeof(string)]);

		if (method == null) {
			throw new InvalidOperationException(nameof(method));
		}

		using Steam.Security.MobileAuthenticator authenticator = GenerateMobileAuthenticator(identitySecret, identitySecret);

		string? result = method.Invoke(authenticator, [time, tag]) as string;

		Assert.IsNotNull(result);
		Assert.AreEqual(expectedCode, result);
	}

	[DataRow("KDHC3rsY8+CmiswnXJcE5e5dRfd=", (ulong) 1337, "47J4D")]
	[DataRow("KDHC3rsY8+CmiswnXJcE5e5dRfd=", (ulong) 1723332288, "JQ3HQ")]
	[DataTestMethod]
	internal void GenerateTokenForTime(string sharedSecret, ulong time, string expectedCode) {
		ArgumentException.ThrowIfNullOrEmpty(sharedSecret);
		ArgumentOutOfRangeException.ThrowIfZero(time);
		ArgumentException.ThrowIfNullOrEmpty(expectedCode);

		using Steam.Security.MobileAuthenticator authenticator = GenerateMobileAuthenticator(sharedSecret, sharedSecret);

		string? result = authenticator.GenerateTokenForTime(time);

		Assert.IsNotNull(result);
		Assert.AreEqual(expectedCode, result);
	}

	private static Steam.Security.MobileAuthenticator GenerateMobileAuthenticator(string identitySecret, string sharedSecret) {
		ArgumentException.ThrowIfNullOrEmpty(identitySecret);
		ArgumentException.ThrowIfNullOrEmpty(sharedSecret);

		JsonObject jsonObject = new() {
			["identity_secret"] = identitySecret,
			["shared_secret"] = sharedSecret
		};

		Steam.Security.MobileAuthenticator? result = jsonObject.ToJsonElement().ToJsonObject<Steam.Security.MobileAuthenticator>();

		if (result == null) {
			throw new InvalidOperationException(nameof(result));
		}

		Steam.Bot bot = Bot.GenerateBot();

		result.Init(bot);

		return result;
	}
}
#pragma warning restore CA1812 // False positive, the class is used during MSTest
