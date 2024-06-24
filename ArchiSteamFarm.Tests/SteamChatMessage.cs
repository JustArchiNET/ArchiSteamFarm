// ----------------------------------------------------------------------------------------------
//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// ----------------------------------------------------------------------------------------------
// |
// Copyright 2015-2024 Łukasz "JustArchi" Domeradzki
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
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static ArchiSteamFarm.Steam.Integration.SteamChatMessage;

namespace ArchiSteamFarm.Tests;

#pragma warning disable CA1812 // False positive, the class is used during MSTest
[TestClass]
internal sealed class SteamChatMessage {
	[TestMethod]
	internal async Task CanSplitEvenWithStupidlyLongPrefix() {
		string prefix = new('x', MaxMessagePrefixBytes);

		const string emoji = "😎";
		const string message = $"{emoji}{emoji}{emoji}{emoji}";

		List<string> output = await GetMessageParts(message, prefix, true).ToListAsync().ConfigureAwait(false);

		Assert.AreEqual(4, output.Count);

		Assert.AreEqual($"{prefix}{emoji}{ContinuationCharacter}", output[0]);
		Assert.AreEqual($"{prefix}{ContinuationCharacter}{emoji}{ContinuationCharacter}", output[1]);
		Assert.AreEqual($"{prefix}{ContinuationCharacter}{emoji}{ContinuationCharacter}", output[2]);
		Assert.AreEqual($"{prefix}{ContinuationCharacter}{emoji}", output[3]);
	}

	[TestMethod]
	internal void ContinuationCharacterSizeIsProperlyCalculated() => Assert.AreEqual(ContinuationCharacterBytes, Encoding.UTF8.GetByteCount(ContinuationCharacter.ToString()));

	[TestMethod]
	internal async Task DoesntSkipEmptyNewlines() {
		string message = $"asdf{Environment.NewLine}{Environment.NewLine}asdf";

		List<string> output = await GetMessageParts(message).ToListAsync().ConfigureAwait(false);

		Assert.AreEqual(1, output.Count);
		Assert.AreEqual(message, output.First());
	}

	[DataRow(false)]
	[DataRow(true)]
	[DataTestMethod]
	internal async Task DoesntSplitInTheMiddleOfMultiByteChar(bool isAccountLimited) {
		int maxMessageBytes = isAccountLimited ? MaxMessageBytesForLimitedAccounts : MaxMessageBytesForUnlimitedAccounts;
		int longLineLength = maxMessageBytes - ReservedContinuationMessageBytes;

		const string emoji = "😎";

		string longSequence = new('a', longLineLength - 1);
		string message = $"{longSequence}{emoji}";

		List<string> output = await GetMessageParts(message, isAccountLimited: isAccountLimited).ToListAsync().ConfigureAwait(false);

		Assert.AreEqual(2, output.Count);

		Assert.AreEqual($"{longSequence}{ContinuationCharacter}", output[0]);
		Assert.AreEqual($"{ContinuationCharacter}{emoji}", output[1]);
	}

	[TestMethod]
	internal async Task DoesntSplitJustBecauseOfLastEscapableCharacter() {
		const string message = "abcdef[";
		const string escapedMessage = @"abcdef\[";

		List<string> output = await GetMessageParts(message).ToListAsync().ConfigureAwait(false);

		Assert.AreEqual(1, output.Count);
		Assert.AreEqual(escapedMessage, output.First());
	}

	[DataRow(false)]
	[DataRow(true)]
	[DataTestMethod]
	internal async Task DoesntSplitOnBackslashNotUsedForEscaping(bool isAccountLimited) {
		int maxMessageBytes = isAccountLimited ? MaxMessageBytesForLimitedAccounts : MaxMessageBytesForUnlimitedAccounts;
		int longLineLength = maxMessageBytes - ReservedContinuationMessageBytes;

		string longLine = new('a', longLineLength - 2);
		string message = $@"{longLine}\";

		List<string> output = await GetMessageParts(message, isAccountLimited: isAccountLimited).ToListAsync().ConfigureAwait(false);

		Assert.AreEqual(1, output.Count);
		Assert.AreEqual($@"{message}\", output.First());
	}

	[DataRow(false)]
	[DataRow(true)]
	[DataTestMethod]
	internal async Task DoesntSplitOnEscapeCharacter(bool isAccountLimited) {
		int maxMessageBytes = isAccountLimited ? MaxMessageBytesForLimitedAccounts : MaxMessageBytesForUnlimitedAccounts;
		int longLineLength = maxMessageBytes - ReservedContinuationMessageBytes;

		string longLine = new('a', longLineLength - 1);
		string message = $"{longLine}[";

		List<string> output = await GetMessageParts(message, isAccountLimited: isAccountLimited).ToListAsync().ConfigureAwait(false);

		Assert.AreEqual(2, output.Count);

		Assert.AreEqual($"{longLine}{ContinuationCharacter}", output[0]);
		Assert.AreEqual($@"{ContinuationCharacter}\[", output[1]);
	}

	[TestMethod]
	internal async Task NoNeedForAnySplittingWithNewlines() {
		string message = $"abcdef{Environment.NewLine}ghijkl{Environment.NewLine}mnopqr";

		List<string> output = await GetMessageParts(message).ToListAsync().ConfigureAwait(false);

		Assert.AreEqual(1, output.Count);
		Assert.AreEqual(message, output.First());
	}

	[TestMethod]
	internal async Task NoNeedForAnySplittingWithoutNewlines() {
		const string message = "abcdef";

		List<string> output = await GetMessageParts(message).ToListAsync().ConfigureAwait(false);

		Assert.AreEqual(1, output.Count);
		Assert.AreEqual(message, output.First());
	}

	[TestMethod]
	internal void ParagraphCharacterSizeIsLessOrEqualToContinuationCharacterSize() => Assert.IsTrue(ContinuationCharacterBytes >= Encoding.UTF8.GetByteCount(ParagraphCharacter.ToString()));

	[TestMethod]
	internal async Task ProperlyEscapesCharacters() {
		const string message = @"[b]bold[/b] \n";
		const string escapedMessage = @"\[b]bold\[/b] \\n";

		List<string> output = await GetMessageParts(message).ToListAsync().ConfigureAwait(false);

		Assert.AreEqual(1, output.Count);
		Assert.AreEqual(escapedMessage, output.First());
	}

	[TestMethod]
	internal async Task ProperlyEscapesSteamMessagePrefix() {
		const string prefix = "/pre []";
		const string escapedPrefix = @"/pre \[]";

		const string message = "asdf";

		List<string> output = await GetMessageParts(message, prefix).ToListAsync().ConfigureAwait(false);

		Assert.AreEqual(1, output.Count);
		Assert.AreEqual($"{escapedPrefix}{message}", output.First());
	}

	[DataRow(false)]
	[DataRow(true)]
	[DataTestMethod]
	internal async Task ProperlySplitsLongSingleLine(bool isAccountLimited) {
		int maxMessageBytes = isAccountLimited ? MaxMessageBytesForLimitedAccounts : MaxMessageBytesForUnlimitedAccounts;
		int longLineLength = maxMessageBytes - ReservedContinuationMessageBytes;

		string longLine = new('a', longLineLength);
		string message = $"{longLine}{longLine}{longLine}{longLine}";

		List<string> output = await GetMessageParts(message, isAccountLimited: isAccountLimited).ToListAsync().ConfigureAwait(false);

		Assert.AreEqual(4, output.Count);

		Assert.AreEqual($"{longLine}{ContinuationCharacter}", output[0]);
		Assert.AreEqual($"{ContinuationCharacter}{longLine}{ContinuationCharacter}", output[1]);
		Assert.AreEqual($"{ContinuationCharacter}{longLine}{ContinuationCharacter}", output[2]);
		Assert.AreEqual($"{ContinuationCharacter}{longLine}", output[3]);
	}

	[TestMethod]
	internal void ReservedSizeForEscapingIsProperlyCalculated() => Assert.AreEqual(ReservedEscapeMessageBytes, Encoding.UTF8.GetByteCount(@"\") + 4); // Maximum amount of bytes per single UTF-8 character is 4, not 6 as from Encoding.UTF8.GetMaxByteCount(1)

	[TestMethod]
	internal async Task RyzhehvostInitialTestForSplitting() {
		const string prefix = "/me ";

		const string message = """
								<XLimited5> Уже имеет: app/1493800 | Aircraft Carrier Survival: Prolouge
								<XLimited5> Уже имеет: app/349520 | Armillo
								<XLimited5> Уже имеет: app/346330 | BrainBread 2
								<XLimited5> Уже имеет: app/1086690 | C-War 2
								<XLimited5> Уже имеет: app/730 | Counter-Strike: Global Offensive
								<XLimited5> Уже имеет: app/838380 | DEAD OR ALIVE 6
								<XLimited5> Уже имеет: app/582890 | Estranged: The Departure
								<XLimited5> Уже имеет: app/331470 | Everlasting Summer
								<XLimited5> Уже имеет: app/1078000 | Gamecraft
								<XLimited5> Уже имеет: app/266310 | GameGuru
								<XLimited5> Уже имеет: app/275390 | Guacamelee! Super Turbo Championship Edition
								<XLimited5> Уже имеет: app/627690 | Idle Champions of the Forgotten Realms
								<XLimited5> Уже имеет: app/1048540 | Kao the Kangaroo: Round 2
								<XLimited5> Уже имеет: app/370910 | Kathy Rain
								<XLimited5> Уже имеет: app/343710 | KHOLAT
								<XLimited5> Уже имеет: app/253900 | Knights and Merchants
								<XLimited5> Уже имеет: app/224260 | No More Room in Hell
								<XLimited5> Уже имеет: app/343360 | Particula
								<XLimited5> Уже имеет: app/237870 | Planet Explorers
								<XLimited5> Уже имеет: app/684680 | Polygoneer
								<XLimited5> Уже имеет: app/1089130 | Quake II RTX
								<XLimited5> Уже имеет: app/755790 | Ring of Elysium
								<XLimited5> Уже имеет: app/1258080 | Shop Titans
								<XLimited5> Уже имеет: app/759530 | Struckd - 3D Game Creator
								<XLimited5> Уже имеет: app/269710 | Tumblestone
								<XLimited5> Уже имеет: app/304930 | Unturned
								<XLimited5> Уже имеет: app/1019250 | WWII TCG - World War 2: The Card Game

								<ASF> 1/1 ботов уже имеют игру app/1493800 | Aircraft Carrier Survival: Prolouge.
								<ASF> 1/1 ботов уже имеют игру app/349520 | Armillo.
								<ASF> 1/1 ботов уже имеют игру app/346330 | BrainBread 2.
								<ASF> 1/1 ботов уже имеют игру app/1086690 | C-War 2.
								<ASF> 1/1 ботов уже имеют игру app/730 | Counter-Strike: Global Offensive.
								<ASF> 1/1 ботов уже имеют игру app/838380 | DEAD OR ALIVE 6.
								<ASF> 1/1 ботов уже имеют игру app/582890 | Estranged: The Departure.
								<ASF> 1/1 ботов уже имеют игру app/331470 | Everlasting Summer.
								<ASF> 1/1 ботов уже имеют игру app/1078000 | Gamecraft.
								<ASF> 1/1 ботов уже имеют игру app/266310 | GameGuru.
								<ASF> 1/1 ботов уже имеют игру app/275390 | Guacamelee! Super Turbo Championship Edition.
								<ASF> 1/1 ботов уже имеют игру app/627690 | Idle Champions of the Forgotten Realms.
								<ASF> 1/1 ботов уже имеют игру app/1048540 | Kao the Kangaroo: Round 2.
								<ASF> 1/1 ботов уже имеют игру app/370910 | Kathy Rain.
								<ASF> 1/1 ботов уже имеют игру app/343710 | KHOLAT.
								<ASF> 1/1 ботов уже имеют игру app/253900 | Knights and Merchants.
								<ASF> 1/1 ботов уже имеют игру app/224260 | No More Room in Hell.
								<ASF> 1/1 ботов уже имеют игру app/343360 | Particula.
								<ASF> 1/1 ботов уже имеют игру app/237870 | Planet Explorers.
								<ASF> 1/1 ботов уже имеют игру app/684680 | Polygoneer.
								<ASF> 1/1 ботов уже имеют игру app/1089130 | Quake II RTX.
								<ASF> 1/1 ботов уже имеют игру app/755790 | Ring of Elysium.
								<ASF> 1/1 ботов уже имеют игру app/1258080 | Shop Titans.
								<ASF> 1/1 ботов уже имеют игру app/759530 | Struckd - 3D Game Creator.
								<ASF> 1/1 ботов уже имеют игру app/269710 | Tumblestone.
								<ASF> 1/1 ботов уже имеют игру app/304930 | Unturned.
								""";

		List<string> output = await GetMessageParts(message, prefix).ToListAsync().ConfigureAwait(false);

		Assert.AreEqual(2, output.Count);

		foreach (string messagePart in output) {
			if ((messagePart.Length <= prefix.Length) || !messagePart.StartsWith(prefix, StringComparison.Ordinal)) {
				Assert.Fail();

				return;
			}

			string[] lines = messagePart.Split(SharedInfo.NewLineIndicators, StringSplitOptions.None);

			int bytes = lines.Where(static line => line.Length > 0).Sum(Encoding.UTF8.GetByteCount) + ((lines.Length - 1) * NewlineWeight);

			if (bytes > MaxMessageBytesForUnlimitedAccounts) {
				Assert.Fail();

				return;
			}
		}
	}

	[DataRow(false)]
	[DataRow(true)]
	[DataTestMethod]
	internal async Task SplitsOnNewlinesWithParagraphCharacter(bool isAccountLimited) {
		int maxMessageBytes = isAccountLimited ? MaxMessageBytesForLimitedAccounts : MaxMessageBytesForUnlimitedAccounts;

		StringBuilder newlinePartBuilder = new();

		for (ushort bytes = 0; bytes < maxMessageBytes - ReservedContinuationMessageBytes - NewlineWeight;) {
			if (newlinePartBuilder.Length > 0) {
				bytes += NewlineWeight;
				newlinePartBuilder.Append(Environment.NewLine);
			}

			bytes++;
			newlinePartBuilder.Append('a');
		}

		string newlinePart = newlinePartBuilder.ToString();
		string message = $"{newlinePart}{Environment.NewLine}{newlinePart}{Environment.NewLine}{newlinePart}{Environment.NewLine}{newlinePart}";

		List<string> output = await GetMessageParts(message, isAccountLimited: isAccountLimited).ToListAsync().ConfigureAwait(false);

		Assert.AreEqual(4, output.Count);

		Assert.AreEqual($"{newlinePart}{ParagraphCharacter}", output[0]);
		Assert.AreEqual($"{newlinePart}{ParagraphCharacter}", output[1]);
		Assert.AreEqual($"{newlinePart}{ParagraphCharacter}", output[2]);
		Assert.AreEqual(newlinePart, output[3]);
	}

	[TestMethod]
	internal async Task ThrowsOnTooLongNewlinesPrefix() {
		string prefix = new('\n', (MaxMessagePrefixBytes / NewlineWeight) + 1);

		const string message = "asdf";

		await Assert.ThrowsExceptionAsync<ArgumentOutOfRangeException>(async () => await GetMessageParts(message, prefix).ToListAsync().ConfigureAwait(false)).ConfigureAwait(false);
	}

	[TestMethod]
	internal async Task ThrowsOnTooLongPrefix() {
		string prefix = new('x', MaxMessagePrefixBytes + 1);

		const string message = "asdf";

		await Assert.ThrowsExceptionAsync<ArgumentOutOfRangeException>(async () => await GetMessageParts(message, prefix).ToListAsync().ConfigureAwait(false)).ConfigureAwait(false);
	}
}
#pragma warning restore CA1812 // False positive, the class is used during MSTest
