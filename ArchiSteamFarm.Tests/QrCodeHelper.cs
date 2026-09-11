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
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ArchiSteamFarm.Tests;

#pragma warning disable CA1812 // False positive, the class is used during MSTest
[TestClass]
internal sealed class QrCodeHelper {
	[TestMethod]
	internal void GenerateAsciiContainsQrModules() {
		string result = Helpers.QrCodeHelper.GenerateAscii("https://s.team/q/l/test");

		Assert.IsFalse(string.IsNullOrEmpty(result));
		Assert.IsTrue(result.Contains('█', StringComparison.Ordinal) || result.Contains('▀', StringComparison.Ordinal) || result.Contains('▄', StringComparison.Ordinal));
	}

	[TestMethod]
	internal void SetUserInputAcceptsQrCodeLoginChoice() {
		Steam.Bot bot = Bot.GenerateBot();

		Assert.IsTrue(bot.SetUserInput(ASF.EUserInputType.QrCodeLogin, "Y"));
		Assert.IsTrue(bot.SetUserInput(ASF.EUserInputType.QrCodeLogin, "n"));
		Assert.IsFalse(bot.SetUserInput(ASF.EUserInputType.QrCodeLogin, "maybe"));
	}
}
#pragma warning restore CA1812
