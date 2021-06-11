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
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ArchiSteamFarm.Steam.Integration {
	internal static class SteamChatMessage {
		internal const char ContinuationCharacter = '…';
		internal const ushort MaxMessageBytes = 2800; // This is a limitation enforced by Steam, together with MaxMessageLines
		internal const ushort MaxMessagePrefixBytes = MaxMessageBytes - ReservedContinuationMessageBytes - ReservedEscapeMessageBytes; // Simplified calculation
		internal const byte ReservedContinuationMessageBytes = 6; // 2x optional … (3 bytes each)

		private const byte MaxMessageLines = 60; // This is a limitation enforced by Steam, together with MaxMessageBytes
		private const byte ReservedEscapeMessageBytes = 5; // 2 characters total, escape one '\' of 1 byte and real one of up to 4 bytes

		internal static async IAsyncEnumerable<string> GetMessageParts(string message, string? steamMessagePrefix = null) {
			if (string.IsNullOrEmpty(message)) {
				throw new ArgumentNullException(nameof(message));
			}

			// We must escape our message prior to sending it
			message = Escape(message);

			int bytes = 0;
			int lines = 0;
			StringBuilder messagePart = new();

			Decoder decoder = Encoding.UTF8.GetDecoder();
			ArrayPool<char> charPool = ArrayPool<char>.Shared;

			using StringReader stringReader = new(message);

			string? line;

			while ((line = await stringReader.ReadLineAsync().ConfigureAwait(false)) != null) {
				byte[] lineBytes = Encoding.UTF8.GetBytes(line);

				for (int lineBytesRead = 0; lineBytesRead < lineBytes.Length;) {
					int maxMessageBytes = MaxMessageBytes - ReservedContinuationMessageBytes;

					if ((messagePart.Length == 0) && !string.IsNullOrEmpty(steamMessagePrefix)) {
						maxMessageBytes -= Encoding.UTF8.GetByteCount(steamMessagePrefix);
						messagePart.Append(steamMessagePrefix);
					}

					int bytesToTake = Math.Min(maxMessageBytes - bytes, lineBytes.Length - lineBytesRead);

					// Convert() method fails if we ask for less than 2 chars, even if we can guarantee we don't need any more
					char[] lineChunk = charPool.Rent(Math.Max(bytesToTake, 2));

					try {
						decoder.Convert(lineBytes, lineBytesRead, bytesToTake, lineChunk, 0, bytesToTake, false, out int bytesUsed, out int charsUsed, out _);

						switch (charsUsed) {
							case <= 0:
								throw new InvalidOperationException(nameof(charsUsed));
							case >= 2 when (lineChunk[charsUsed - 1] == '\\') && (lineChunk[charsUsed - 2] != '\\'):
								// If our message is of max length and ends with a single '\' then we can't split it here, it escapes the next character
								// Instead, we'll cut this message one char short and include the rest in the next iteration
								charsUsed--;

								break;
						}

						if (++lines > 1) {
							messagePart.AppendLine();
						}

						if (lineBytesRead > 0) {
							bytes++;
							messagePart.Append(ContinuationCharacter);
						}

						lineBytesRead += bytesUsed;
						bytes += bytesUsed;

						messagePart.Append(lineChunk, 0, charsUsed);
					} finally {
						charPool.Return(lineChunk);
					}

					if (lineBytesRead < lineBytes.Length) {
						bytes++;
						messagePart.Append(ContinuationCharacter);
					}

					if ((bytes < maxMessageBytes) && (lines < MaxMessageLines)) {
						continue;
					}

					yield return messagePart.ToString();

					bytes = 0;
					lines = 0;
					messagePart.Clear();
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
