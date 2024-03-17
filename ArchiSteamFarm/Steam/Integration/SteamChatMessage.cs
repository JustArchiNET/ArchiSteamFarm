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
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ArchiSteamFarm.Steam.Integration;

internal static class SteamChatMessage {
	internal const char ContinuationCharacter = '…'; // A character used for indicating that the next newline part is a continuation of the previous line
	internal const byte ContinuationCharacterBytes = 3; // The continuation character specified above uses 3 bytes in UTF-8
	internal const ushort MaxMessageBytesForLimitedAccounts = 1945; // This is a limitation enforced by Steam
	internal const ushort MaxMessageBytesForUnlimitedAccounts = 6340; // This is a limitation enforced by Steam
	internal const ushort MaxMessagePrefixBytes = MaxMessageBytesForLimitedAccounts - ReservedContinuationMessageBytes - ReservedEscapeMessageBytes; // Simplified calculation, nobody should be using prefixes even close to that anyway
	internal const byte NewlineWeight = 61; // This defines how much weight a newline character is adding to the output, limitation enforced by Steam
	internal const char ParagraphCharacter = '¶'; // A character used for indicating that this is not the last part of message (2 bytes, so it fits in ContinuationCharacterBytes)
	internal const byte ReservedContinuationMessageBytes = ContinuationCharacterBytes * 2; // Up to 2 optional continuation characters
	internal const byte ReservedEscapeMessageBytes = 5; // 2 characters total, escape one '\' of 1 byte and real one of up to 4 bytes

	internal static async IAsyncEnumerable<string> GetMessageParts(string message, string? steamMessagePrefix = null, bool isAccountLimited = false) {
		ArgumentException.ThrowIfNullOrEmpty(message);

		int prefixBytes = 0;
		int prefixLength = 0;

		if (!string.IsNullOrEmpty(steamMessagePrefix)) {
			// We must escape our message prefix if needed
			steamMessagePrefix = Escape(steamMessagePrefix);

			prefixBytes = GetMessagePrefixBytes(steamMessagePrefix);

			if (prefixBytes > MaxMessagePrefixBytes) {
				throw new ArgumentOutOfRangeException(nameof(steamMessagePrefix));
			}

			prefixLength = steamMessagePrefix.Length;
		}

		int maxMessageBytes = (isAccountLimited ? MaxMessageBytesForLimitedAccounts : MaxMessageBytesForUnlimitedAccounts) - ReservedContinuationMessageBytes;

		// We must escape our message prior to sending it
		message = Escape(message);

		int messagePartBytes = prefixBytes;
		StringBuilder messagePart = new(steamMessagePrefix);

		Decoder decoder = Encoding.UTF8.GetDecoder();
		ArrayPool<char> charPool = ArrayPool<char>.Shared;

		using StringReader stringReader = new(message);

		while (await stringReader.ReadLineAsync().ConfigureAwait(false) is { } line) {
			// Special case for empty newline
			if (line.Length == 0) {
				if (messagePart.Length > prefixLength) {
					messagePartBytes += NewlineWeight;
					messagePart.AppendLine();
				}

				// Check if we reached the limit for one message
				if (messagePartBytes + NewlineWeight + ReservedEscapeMessageBytes > maxMessageBytes) {
					if (stringReader.Peek() >= 0) {
						messagePart.Append(ParagraphCharacter);
					}

					yield return messagePart.ToString();

					messagePartBytes = prefixBytes;
					messagePart.Clear();
					messagePart.Append(steamMessagePrefix);
				}

				// Move on to the next line
				continue;
			}

			byte[] lineBytes = Encoding.UTF8.GetBytes(line);

			for (int lineBytesRead = 0; lineBytesRead < lineBytes.Length;) {
				if (messagePart.Length > prefixLength) {
					if (messagePartBytes + NewlineWeight + lineBytes.Length > maxMessageBytes) {
						messagePart.Append(ParagraphCharacter);

						yield return messagePart.ToString();

						messagePartBytes = prefixBytes;
						messagePart.Clear();
						messagePart.Append(steamMessagePrefix);
					} else {
						messagePartBytes += NewlineWeight;
						messagePart.AppendLine();
					}
				}

				int bytesToTake = Math.Min(maxMessageBytes - messagePartBytes, lineBytes.Length - lineBytesRead);

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
						messagePartBytes += ContinuationCharacterBytes;
						messagePart.Append(ContinuationCharacter);
					}

					lineBytesRead += bytesUsed;

					messagePartBytes += bytesUsed;
					messagePart.Append(lineChunk, 0, charsUsed);
				} finally {
					charPool.Return(lineChunk);
				}

				bool midLineSplitting = false;

				if (lineBytesRead < lineBytes.Length) {
					midLineSplitting = true;

					messagePartBytes += ContinuationCharacterBytes;
					messagePart.Append(ContinuationCharacter);
				}

				// Check if we still have room for one more line
				if (messagePartBytes + NewlineWeight + ReservedEscapeMessageBytes <= maxMessageBytes) {
					continue;
				}

				if (!midLineSplitting && (stringReader.Peek() >= 0)) {
					messagePart.Append(ParagraphCharacter);
				}

				yield return messagePart.ToString();

				messagePartBytes = prefixBytes;
				messagePart.Clear();
				messagePart.Append(steamMessagePrefix);
			}
		}

		if (messagePart.Length <= prefixLength) {
			yield break;
		}

		yield return messagePart.ToString();
	}

	internal static bool IsValidPrefix(string steamMessagePrefix) {
		ArgumentException.ThrowIfNullOrEmpty(steamMessagePrefix);

		return GetMessagePrefixBytes(Escape(steamMessagePrefix)) <= MaxMessagePrefixBytes;
	}

	internal static string Unescape(string message) {
		ArgumentException.ThrowIfNullOrEmpty(message);

		return message.Replace("\\[", "[", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
	}

	private static string Escape(string message) {
		ArgumentException.ThrowIfNullOrEmpty(message);

		return message.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("[", "\\[", StringComparison.Ordinal);
	}

	private static int GetMessagePrefixBytes(string escapedSteamMessagePrefix) {
		ArgumentException.ThrowIfNullOrEmpty(escapedSteamMessagePrefix);

		string[] prefixLines = escapedSteamMessagePrefix.Split(SharedInfo.NewLineIndicators, StringSplitOptions.None);

		return prefixLines.Where(static prefixLine => prefixLine.Length > 0).Sum(Encoding.UTF8.GetByteCount) + ((prefixLines.Length - 1) * NewlineWeight);
	}
}
