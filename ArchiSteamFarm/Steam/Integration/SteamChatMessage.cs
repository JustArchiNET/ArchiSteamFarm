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

#if NETFRAMEWORK
using ArchiSteamFarm.Compatibility;
#endif
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ArchiSteamFarm.Steam.Integration {
	internal static class SteamChatMessage {
		internal const ushort MaxMessagePrefixLength = MaxMessageWidth - ReservedMessageLength - 2; // 2 for a minimum of 2 characters (escape one and real one)

		private const byte MaxMessageHeight = 45; // This is a limitation enforced by Steam, together with MaxMessageWidth
		private const byte MaxMessageWidth = 80; // This is a limitation enforced by Steam, together with MaxMessageHeight
		private const byte ReservedMessageLength = 2; // 2 for 2x optional …

		internal static async IAsyncEnumerable<string> GetMessageParts(string message, string? steamMessagePrefix = null) {
			if (string.IsNullOrEmpty(message)) {
				throw new ArgumentNullException(nameof(message));
			}

			// We must escape our message prior to sending it
			message = Escape(message);

			StringBuilder messagePart = new();
			int lines = 0;

			using StringReader stringReader = new(message);

			string? line;

			while ((line = await stringReader.ReadLineAsync().ConfigureAwait(false)) != null) {
				int maxMessageWidth;

				for (int i = 0; i < line.Length; i += maxMessageWidth) {
					maxMessageWidth = MaxMessageWidth - ReservedMessageLength;

					if ((messagePart.Length == 0) && !string.IsNullOrEmpty(steamMessagePrefix)) {
						maxMessageWidth -= steamMessagePrefix!.Length;
						messagePart.Append(steamMessagePrefix);
					}

					string lineChunk = line[i..Math.Min(maxMessageWidth, line.Length - i)];

					// If our message is of max length and ends with a single '\' then we can't split it here, it escapes the next character
					if ((lineChunk.Length >= maxMessageWidth) && (lineChunk[^1] == '\\') && (lineChunk[^2] != '\\')) {
						// Instead, we'll cut this message one char short and include the rest in next iteration
						lineChunk = lineChunk.Remove(lineChunk.Length - 1);
						i--;
					}

					if (++lines > 1) {
						messagePart.AppendLine();
					}

					if (i > 0) {
						messagePart.Append('…');
					}

					messagePart.Append(lineChunk);

					if (maxMessageWidth < line.Length - i) {
						messagePart.Append('…');
					}

					if (lines < MaxMessageHeight) {
						continue;
					}

					yield return messagePart.ToString();

					messagePart.Clear();
					lines = 0;
				}
			}

			if (lines == 0) {
				yield break;
			}

			yield return messagePart.ToString();
		}

		internal static string Unescape(string message) {
			if (string.IsNullOrEmpty(message)) {
				throw new ArgumentNullException(nameof(message));
			}

			return message.Replace("\\[", "[", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
		}

		private static string Escape(string message) {
			if (string.IsNullOrEmpty(message)) {
				throw new ArgumentNullException(nameof(message));
			}

			return message.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("[", "\\[", StringComparison.Ordinal);
		}
	}
}
