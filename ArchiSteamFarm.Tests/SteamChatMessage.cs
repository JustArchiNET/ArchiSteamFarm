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
		public async Task NoNeedForAnySplitting() {
			const string message = "abcdef";

			List<string> output = await GetMessageParts(message).ToListAsync().ConfigureAwait(false);

			Assert.AreEqual(1, output.Count);
			Assert.AreEqual(message, output.First());
		}

		[TestMethod]
		public async Task ProperlySplitsLongSingleLine() {
			const ushort longLineLength = MaxMessageBytes - ReservedContinuationMessageBytes;

			string longLine = new('a', longLineLength);
			Assert.AreEqual(longLineLength, Encoding.UTF8.GetByteCount(longLine));

			string message = longLine + longLine + longLine + longLine;
			Assert.AreEqual(4 * longLineLength, Encoding.UTF8.GetByteCount(message));

			List<string> output = await GetMessageParts(message).ToListAsync().ConfigureAwait(false);

			Assert.AreEqual(4, output.Count);

			Assert.AreEqual(longLine + ContinuationCharacter, output[0]);
			Assert.AreEqual(ContinuationCharacter + longLine + ContinuationCharacter, output[1]);
			Assert.AreEqual(ContinuationCharacter + longLine + ContinuationCharacter, output[2]);
			Assert.AreEqual(ContinuationCharacter + longLine, output[3]);
		}
	}
}
