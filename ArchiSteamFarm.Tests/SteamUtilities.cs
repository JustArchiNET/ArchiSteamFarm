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

using Microsoft.VisualStudio.TestTools.UnitTesting;
using static ArchiSteamFarm.Steam.Integration.SteamUtilities;

namespace ArchiSteamFarm.Tests;

#pragma warning disable CA1812 // False positive, the class is used during MSTest
[TestClass]
internal sealed class SteamUtilities {
	[DataRow("730", "APP", "APP", 730U)]
	[DataRow("730", "SUB", "SUB", 730U)]
	[DataRow("app/730", "SUB", "APP", 730U)]
	[DataRow("a/730", "SUB", "APP", 730U)]
	[DataRow("sub/123", "APP", "SUB", 123U)]
	[DataRow("s/123", "APP", "SUB", 123U)]
	[DataRow("https://store.steampowered.com/app/730", "SUB", "APP", 730U)]
	[DataRow("https://store.steampowered.com/sub/123", "APP", "SUB", 123U)]
	[DataRow("https://store.steampowered.com/app/730/SomeGameName/", "SUB", "APP", 730U)]
	[TestMethod]
	internal void TryParseGameIdentifierReturnsExpectedId(string input, string defaultType, string expectedType, uint expectedId) {
		bool result = TryParseGameIdentifier(input, defaultType, out string? type, out uint id);

		Assert.IsTrue(result);
		Assert.AreEqual(expectedType, type);
		Assert.AreEqual(expectedId, id);
	}

	[DataRow("0", "APP")]
	[DataRow("abc", "APP")]
	[DataRow("app/0", "SUB")]
	[DataRow("sub/abc", "APP")]
	[DataRow("unknown/123", "APP")]
	[DataRow("https://store.steampowered.com/bundle/123", "APP")]
	[DataRow("https://example.com/app/730", "APP")]
	[TestMethod]
	internal void TryParseGameIdentifierReturnsFalseForInvalidInput(string input, string defaultType) {
		bool result = TryParseGameIdentifier(input, defaultType, out string? type, out uint id);

		Assert.IsFalse(result);
		Assert.IsNull(type);
		Assert.AreEqual(0U, id);
	}

	[DataRow("regex/pattern", "APP", "REGEX", "pattern")]
	[DataRow("r/test.*", "APP", "REGEX", "test.*")]
	[DataRow("name/Half-Life", "APP", "NAME", "Half-Life")]
	[DataRow("n/Portal", "APP", "NAME", "Portal")]
	[TestMethod]
	internal void TryParseGameIdentifierStringReturnsExpectedValue(string input, string defaultType, string expectedType, string expectedValue) {
		bool result = TryParseGameIdentifier(input, defaultType, out string? type, out string? value);

		Assert.IsTrue(result);
		Assert.AreEqual(expectedType, type);
		Assert.AreEqual(expectedValue, value);
	}

	[DataRow("regex/pattern", "APP")]
	[DataRow("name/Half-Life", "APP")]
	[TestMethod]
	internal void TryParseGameIdentifierUintReturnsFalseForNonNumericTypes(string input, string defaultType) {
		bool result = TryParseGameIdentifier(input, defaultType, out string? type, out uint id);

		Assert.IsFalse(result);
		Assert.IsNull(type);
		Assert.AreEqual(0U, id);
	}
}
#pragma warning restore CA1812 // False positive, the class is used during MSTest
