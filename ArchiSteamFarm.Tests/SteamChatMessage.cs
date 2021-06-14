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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static ArchiSteamFarm.Steam.Integration.SteamChatMessage;

namespace ArchiSteamFarm.Tests {
	[TestClass]
	public sealed class SteamChatMessage {
		[TestMethod]
		public async Task CanSplitEvenWithStupidlyLongPrefix() {
			string prefix = new('x', MaxMessagePrefixBytes);

			const string emoji = "😎";
			const string message = emoji + emoji + emoji + emoji;

			List<string> output = await GetMessageParts(message, prefix).ToListAsync().ConfigureAwait(false);

			Assert.AreEqual(4, output.Count);

			Assert.AreEqual(prefix + emoji + ContinuationCharacter, output[0]);
			Assert.AreEqual(prefix + ContinuationCharacter + emoji + ContinuationCharacter, output[1]);
			Assert.AreEqual(prefix + ContinuationCharacter + emoji + ContinuationCharacter, output[2]);
			Assert.AreEqual(prefix + ContinuationCharacter + emoji, output[3]);
		}

		[TestMethod]
		public void ContinuationCharacterSizeIsProperlyCalculated() => Assert.AreEqual(ContinuationCharacterBytes, Encoding.UTF8.GetByteCount(ContinuationCharacter.ToString()));

		[TestMethod]
		public async Task DoesntSplitInTheMiddleOfMultiByteChar() {
			const ushort longLineLength = MaxMessageBytes - ReservedContinuationMessageBytes;
			const string emoji = "😎";

			string longSequence = new('a', longLineLength - 1);
			string message = longSequence + emoji;

			List<string> output = await GetMessageParts(message).ToListAsync().ConfigureAwait(false);

			Assert.AreEqual(2, output.Count);

			Assert.AreEqual(longSequence + ContinuationCharacter, output[0]);
			Assert.AreEqual(ContinuationCharacter + emoji, output[1]);
		}

		[TestMethod]
		public async Task DoesntSplitJustBecauseOfLastEscapableCharacter() {
			const string message = "abcdef[";
			const string escapedMessage = @"abcdef\[";

			List<string> output = await GetMessageParts(message).ToListAsync().ConfigureAwait(false);

			Assert.AreEqual(1, output.Count);
			Assert.AreEqual(escapedMessage, output.First());
		}

		[TestMethod]
		public async Task DoesntSplitOnBackslashNotUsedForEscaping() {
			const ushort longLineLength = MaxMessageBytes - ReservedContinuationMessageBytes;

			string longLine = new('a', longLineLength - 2);
			string message = longLine + @"\";

			List<string> output = await GetMessageParts(message).ToListAsync().ConfigureAwait(false);

			Assert.AreEqual(1, output.Count);
			Assert.AreEqual(message + @"\", output.First());
		}

		[TestMethod]
		public async Task DoesntSplitOnEscapeCharacter() {
			const ushort longLineLength = MaxMessageBytes - ReservedContinuationMessageBytes;

			string longLine = new('a', longLineLength - 1);
			string message = longLine + "[";

			List<string> output = await GetMessageParts(message).ToListAsync().ConfigureAwait(false);

			Assert.AreEqual(2, output.Count);

			Assert.AreEqual(longLine + ContinuationCharacter, output[0]);
			Assert.AreEqual(ContinuationCharacter + @"\[", output[1]);
		}

		[TestMethod]
		public async Task NoNeedForAnySplittingWithNewlines() {
			string message = "abcdef" + Environment.NewLine + "ghijkl" + Environment.NewLine + "mnopqr";

			List<string> output = await GetMessageParts(message).ToListAsync().ConfigureAwait(false);

			Assert.AreEqual(1, output.Count);
			Assert.AreEqual(message, output.First());
		}

		[TestMethod]
		public async Task NoNeedForAnySplittingWithoutNewlines() {
			const string message = "abcdef";

			List<string> output = await GetMessageParts(message).ToListAsync().ConfigureAwait(false);

			Assert.AreEqual(1, output.Count);
			Assert.AreEqual(message, output.First());
		}

		[TestMethod]
		public async Task ProperlyEscapesCharacters() {
			const string message = @"[b]bold[/b] \n";
			const string escapedMessage = @"\[b]bold\[/b] \\n";

			List<string> output = await GetMessageParts(message).ToListAsync().ConfigureAwait(false);

			Assert.AreEqual(1, output.Count);
			Assert.AreEqual(escapedMessage, output.First());
		}

		[TestMethod]
		public async Task ProperlySplitsLongSingleLine() {
			const ushort longLineLength = MaxMessageBytes - ReservedContinuationMessageBytes;

			string longLine = new('a', longLineLength);
			string message = longLine + longLine + longLine + longLine;

			List<string> output = await GetMessageParts(message).ToListAsync().ConfigureAwait(false);

			Assert.AreEqual(4, output.Count);

			Assert.AreEqual(longLine + ContinuationCharacter, output[0]);
			Assert.AreEqual(ContinuationCharacter + longLine + ContinuationCharacter, output[1]);
			Assert.AreEqual(ContinuationCharacter + longLine + ContinuationCharacter, output[2]);
			Assert.AreEqual(ContinuationCharacter + longLine, output[3]);
		}

		[TestMethod]
		public async Task SplitsOnNewlinesWithoutContinuationCharacter() {
			StringBuilder newlinePartBuilder = new();

			for (ushort bytes = 0; bytes < MaxMessageBytes - ReservedContinuationMessageBytes - NewlineWeight;) {
				if (newlinePartBuilder.Length > 0) {
					bytes += NewlineWeight;
					newlinePartBuilder.Append(Environment.NewLine);
				}

				bytes++;
				newlinePartBuilder.Append('a');
			}

			string newlinePart = newlinePartBuilder.ToString();
			string message = newlinePart + Environment.NewLine + newlinePart + Environment.NewLine + newlinePart + Environment.NewLine + newlinePart;

			List<string> output = await GetMessageParts(message).ToListAsync().ConfigureAwait(false);

			Assert.AreEqual(4, output.Count);

			Assert.AreEqual(newlinePart, output[0]);
			Assert.AreEqual(newlinePart, output[1]);
			Assert.AreEqual(newlinePart, output[2]);
			Assert.AreEqual(newlinePart, output[3]);
		}

		[ExpectedException(typeof(ArgumentOutOfRangeException))]
		[TestMethod]
		public async Task ThrowsOnTooLongNewlinesPrefix() {
			string prefix = new('\n', (MaxMessagePrefixBytes / NewlineWeight) + 1);

			const string message = "asdf";

			await GetMessageParts(message, prefix).ToListAsync().ConfigureAwait(false);

			Assert.Fail();
		}

		[ExpectedException(typeof(ArgumentOutOfRangeException))]
		[TestMethod]
		public async Task ThrowsOnTooLongPrefix() {
			string prefix = new('x', MaxMessagePrefixBytes + 1);

			const string message = "asdf";

			await GetMessageParts(message, prefix).ToListAsync().ConfigureAwait(false);

			Assert.Fail();
		}
	}
}
