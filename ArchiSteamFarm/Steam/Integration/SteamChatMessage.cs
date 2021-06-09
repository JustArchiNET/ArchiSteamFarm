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
		internal const ushort MaxMessagePrefixBytes = MaxMessageBytes - ReservedContinuationMessageBytes - ReservedEscapeMessageBytes; // Simplified calculation

		private const ushort MaxMessageBytes = 2800; // This is a limitation enforced by Steam, together with MaxMessageLines
		private const byte MaxMessageLines = 60; // This is a limitation enforced by Steam, together with MaxMessageBytes
		private const byte ReservedContinuationMessageBytes = 6; // 2x optional … (3 bytes each)
		private const byte ReservedEscapeMessageBytes = 5; // 2 characters total, escape one '\' of 1 byte and real one of up to 4 bytes

		internal static async IAsyncEnumerable<string> GetMessageParts(string message, string? steamMessagePrefix = null) {
			if (string.IsNullOrEmpty(message)) {
				throw new ArgumentNullException(nameof(message));
			}

			// We must escape our message prior to sending it
			message = Escape(message);

			int lines = 0;
			StringBuilder messagePart = new();

			Decoder decoder = Encoding.UTF8.GetDecoder();
			ArrayPool<char> charPool = ArrayPool<char>.Shared;

			using StringReader stringReader = new(message);

			string? line;

			while ((line = await stringReader.ReadLineAsync().ConfigureAwait(false)) != null) {
				byte[] lineBytes = Encoding.UTF8.GetBytes(line);

				for (int bytesRead = 0; bytesRead < lineBytes.Length;) {
					int maxMessageBytes = MaxMessageBytes - ReservedContinuationMessageBytes;

					if ((messagePart.Length == 0) && !string.IsNullOrEmpty(steamMessagePrefix)) {
						maxMessageBytes -= Encoding.UTF8.GetByteCount(steamMessagePrefix);
						messagePart.Append(steamMessagePrefix);
					}

					// We'll extract up to maxMessageBytes bytes, so also a maximum of maxMessageBytes 1-byte characters
					char[] lineChunk = charPool.Rent(Math.Min(maxMessageBytes, lineBytes.Length - bytesRead));

					try {
						int charCount = decoder.GetChars(lineBytes, bytesRead, Math.Min(maxMessageBytes, lineBytes.Length - bytesRead), lineChunk, 0);

						switch (charCount) {
							case <= 0:
								throw new InvalidOperationException(nameof(charCount));
							case >= 2 when (lineChunk[charCount - 1] == '\\') && (lineChunk[charCount - 2] != '\\'):
								// If our message is of max length and ends with a single '\' then we can't split it here, it escapes the next character
								// Instead, we'll cut this message one char short and include the rest in the next iteration
								charCount--;

								break;
						}

						if (++lines > 1) {
							messagePart.AppendLine();
						}

						if (bytesRead > 0) {
							messagePart.Append('…');
						}

						bytesRead += Encoding.UTF8.GetByteCount(lineChunk, 0, charCount);

						messagePart.Append(lineChunk, 0, charCount);
					} finally {
						charPool.Return(lineChunk);
					}

					if (bytesRead < lineBytes.Length) {
						messagePart.Append('…');
					}

					if (lines < MaxMessageLines) {
						continue;
					}

					yield return messagePart.ToString();

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
