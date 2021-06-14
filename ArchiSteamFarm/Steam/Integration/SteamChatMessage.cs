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
using System.Linq;
using System.Text;

namespace ArchiSteamFarm.Steam.Integration {
	internal static class SteamChatMessage {
		internal const char ContinuationCharacter = '…'; // A character used for indicating that the next newline part is a continuation of the previous line
		internal const ushort MaxMessageBytes = 6449; // This is a limitation enforced by Steam
		internal const ushort MaxMessagePrefixBytes = MaxMessageBytes - ReservedContinuationMessageBytes - ReservedEscapeMessageBytes; // Simplified calculation, nobody should be using prefixes even close to that anyway
		internal const byte NewlineWeight = 61; // This defines how much weight a newline character is adding to the output, limitation enforced by Steam
		internal const byte ReservedContinuationMessageBytes = 6; // 2x optional … (3 bytes each)

		private const byte ReservedEscapeMessageBytes = 5; // 2 characters total, escape one '\' of 1 byte and real one of up to 4 bytes

		internal static async IAsyncEnumerable<string> GetMessageParts(string message, string? steamMessagePrefix = null) {
			if (string.IsNullOrEmpty(message)) {
				throw new ArgumentNullException(nameof(message));
			}

			int prefixBytes = 0;

			if (!string.IsNullOrEmpty(steamMessagePrefix)) {
				string[] prefixLines = steamMessagePrefix!.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

				prefixBytes = prefixLines.Where(prefixLine => prefixLine.Length > 0).Sum(Encoding.UTF8.GetByteCount) + ((prefixLines.Length - 1) * NewlineWeight);

				if (prefixBytes > MaxMessagePrefixBytes) {
					throw new ArgumentOutOfRangeException(nameof(steamMessagePrefix));
				}
			}

			// We must escape our message prior to sending it
			message = Escape(message);

			int bytesRead = 0;
			StringBuilder messagePart = new();

			Decoder decoder = Encoding.UTF8.GetDecoder();
			ArrayPool<char> charPool = ArrayPool<char>.Shared;

			using StringReader stringReader = new(message);

			string? line;

			while ((line = await stringReader.ReadLineAsync().ConfigureAwait(false)) != null) {
				byte[] lineBytes = Encoding.UTF8.GetBytes(line);

				for (int lineBytesRead = 0; lineBytesRead < lineBytes.Length;) {
					int maxMessageBytes = MaxMessageBytes - ReservedContinuationMessageBytes;

					if (messagePart.Length == 0) {
						if (prefixBytes > 0) {
							maxMessageBytes -= prefixBytes;
							messagePart.Append(steamMessagePrefix);
						}
					} else {
						bytesRead += NewlineWeight;
						messagePart.AppendLine();
					}

					int bytesToTake = Math.Min(maxMessageBytes - bytesRead, lineBytes.Length - lineBytesRead);

					// We can never have more characters than bytes used, so this covers the worst case of 1-byte characters exclusively
					char[] lineChunk = charPool.Rent(bytesToTake);

					try {
						// We have to reset the decoder prior to using it, as we must discard any amount of bytes read from previous incomplete character
						decoder.Reset();

						int charsUsed = decoder.GetChars(lineBytes, lineBytesRead, bytesToTake, lineChunk, 0, false);

						switch (charsUsed) {
							case <= 0:
								throw new InvalidOperationException(nameof(charsUsed));
							case >= 2 when (lineChunk[charsUsed - 1] == '\\') && (lineChunk[charsUsed - 2] != '\\'):
								// If our message is of max length and ends with a single '\' then we can't split it here, because it escapes the next character
								// Instead, we'll cut this message one char short and include the rest in the next iteration
								charsUsed--;

								break;
						}

						int bytesUsed = Encoding.UTF8.GetByteCount(lineChunk, 0, charsUsed);

						if (lineBytesRead > 0) {
							bytesRead++;
							messagePart.Append(ContinuationCharacter);
						}

						lineBytesRead += bytesUsed;
						bytesRead += bytesUsed;

						messagePart.Append(lineChunk, 0, charsUsed);
					} finally {
						charPool.Return(lineChunk);
					}

					if (lineBytesRead < lineBytes.Length) {
						bytesRead++;
						messagePart.Append(ContinuationCharacter);
					}

					// Check if we still have room for one more line
					if (bytesRead + NewlineWeight + ReservedEscapeMessageBytes <= maxMessageBytes) {
						continue;
					}

					yield return messagePart.ToString();

					bytesRead = 0;
					messagePart.Clear();
				}
			}

			if (messagePart.Length == 0) {
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
