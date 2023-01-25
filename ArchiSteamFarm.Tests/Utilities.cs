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
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static ArchiSteamFarm.Core.Utilities;

namespace ArchiSteamFarm.Tests;

[TestClass]
#pragma warning disable CA1724 // We don't care about the potential conflict, as ASF class name has a priority
public sealed class Utilities {
	[TestMethod]
	public void AdditionallyForbiddenWordsWeakenPassphrases() => Assert.IsTrue(TestPasswordStrength("10chars<!>asdf", new HashSet<string> { "chars<!>" }).IsWeak);

	[TestMethod]
	public void ContextSpecificWordsWeakenPassphrases() => Assert.IsTrue(TestPasswordStrength("archisteamfarmpassword").IsWeak);

	[TestMethod]
	public void EasyPasswordsHaveMeaningfulReason() {
		(bool isWeak, string? reason) = TestPasswordStrength("CorrectHorse");

		Assert.IsTrue(isWeak);
		Assert.IsTrue(reason?.Contains("Capitalization doesn't help very much", StringComparison.OrdinalIgnoreCase));
	}

	[TestMethod]
	public void LongPassphraseIsNotWeak() => Assert.IsFalse(TestPasswordStrength("10chars<!>asdf").IsWeak);

	[TestMethod]
	public void MemePasswordIsNotWeak() => Assert.IsFalse(TestPasswordStrength("correcthorsebatterystaple").IsWeak);

	[TestMethod]
	public void RepeatedPasswordsHaveMeaningfulReason() {
		(bool isWeak, string? reason) = TestPasswordStrength("abcabcabc");

		Assert.IsTrue(isWeak);
		Assert.IsTrue(reason?.Contains("Avoid repeated words and characters", StringComparison.OrdinalIgnoreCase));
	}

	[TestMethod]
	public void RepetitiveCharactersWeakenPassphrases() => Assert.IsTrue(TestPasswordStrength("testaaaatest").IsWeak);

	[TestMethod]
	public void SequentialCharactersWeakenPassphrases() => Assert.IsTrue(TestPasswordStrength("testabcdtest").IsWeak);

	[TestMethod]
	public void SequentialDescendingCharactersWeakenPassphrases() => Assert.IsTrue(TestPasswordStrength("testdcbatest").IsWeak);

	[TestMethod]
	public void ShortPassphraseIsWeak() => Assert.IsTrue(TestPasswordStrength("four").IsWeak);

	[TestMethod]
	public void StraightRowsPasswordsHaveMeaningfulReason() {
		(bool isWeak, string? reason) = TestPasswordStrength("`1234567890-=");

		Assert.IsTrue(isWeak);
		Assert.IsTrue(reason?.Contains("Straight rows of keys are easy to guess", StringComparison.OrdinalIgnoreCase));
	}
}
#pragma warning restore CA1724 // We don't care about the potential conflict, as ASF class name has a priority
