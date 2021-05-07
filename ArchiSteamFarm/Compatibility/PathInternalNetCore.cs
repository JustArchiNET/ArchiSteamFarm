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
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ArchiSteamFarm.Compatibility {
	internal static class PathInternalNetCore {
		private const string ExtendedDevicePathPrefix = @"\\?\";
		private const string UncExtendedPathPrefix = @"\\?\UNC\";

		internal static StringComparison StringComparison => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

		/// <summary>
		///     Returns true if the two paths have the same root
		/// </summary>
		internal static bool AreRootsEqual(string first, string second, StringComparison comparisonType) {
			int firstRootLength = GetRootLength(first);
			int secondRootLength = GetRootLength(second);

			return (firstRootLength == secondRootLength)
				&& (string.Compare(
					first,
					0,
					second,
					0,
					firstRootLength,
					comparisonType
				) == 0);
		}

		/// <summary>
		///     Returns true if the path ends in a directory separator.
		/// </summary>
		internal static bool EndsInDirectorySeparator(string path) => (path.Length > 0) && IsDirectorySeparator(path[^1]);

		/// <summary>
		///     Get the common path length from the start of the string.
		/// </summary>
		internal static int GetCommonPathLength(string first, string second, bool ignoreCase) {
			int commonChars = EqualStartingCharacterCount(first, second, ignoreCase);

			// If nothing matches
			if (commonChars == 0) {
				return commonChars;
			}

			// Or we're a full string and equal length or match to a separator
			if ((commonChars == first.Length)
				&& ((commonChars == second.Length) || IsDirectorySeparator(second[commonChars]))) {
				return commonChars;
			}

			if ((commonChars == second.Length) && IsDirectorySeparator(first[commonChars])) {
				return commonChars;
			}

			// It's possible we matched somewhere in the middle of a segment e.g. C:\Foodie and C:\Foobar.
			while ((commonChars > 0) && !IsDirectorySeparator(first[commonChars - 1])) {
				commonChars--;
			}

			return commonChars;
		}

		/// <summary>
		///     True if the given character is a directory separator.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal static bool IsDirectorySeparator(char c) => (c == System.IO.Path.DirectorySeparatorChar) || (c == System.IO.Path.AltDirectorySeparatorChar);

		/// <summary>
		///     Gets the count of common characters from the left optionally ignoring case
		/// </summary>
		private static unsafe int EqualStartingCharacterCount(string first, string second, bool ignoreCase) {
			if (string.IsNullOrEmpty(first) || string.IsNullOrEmpty(second)) {
				return 0;
			}

			int commonChars = 0;

			fixed (char* f = first)
			fixed (char* s = second) {
				char* l = f;
				char* r = s;
				char* leftEnd = l + first.Length;
				char* rightEnd = r + second.Length;

				while ((l != leftEnd) && (r != rightEnd)
					&& ((*l == *r) || (ignoreCase &&
						(char.ToUpperInvariant(*l) == char.ToUpperInvariant(*r))))) {
					commonChars++;
					l++;
					r++;
				}
			}

			return commonChars;
		}

		/// <summary>
		///     Gets the length of the root of the path (drive, share, etc.).
		/// </summary>
		private static int GetRootLength(string path) {
			int i = 0;
			int volumeSeparatorLength = 2; // Length to the colon "C:"
			int uncRootLength = 2; // Length to the start of the server name "\\"

			bool extendedSyntax = path.StartsWith(ExtendedDevicePathPrefix, StringComparison.Ordinal);
			bool extendedUncSyntax = path.StartsWith(UncExtendedPathPrefix, StringComparison.Ordinal);

			if (extendedSyntax) {
				// Shift the position we look for the root from to account for the extended prefix
				if (extendedUncSyntax) {
					// "\\" -> "\\?\UNC\"
					uncRootLength = UncExtendedPathPrefix.Length;
				} else {
					// "C:" -> "\\?\C:"
					volumeSeparatorLength += ExtendedDevicePathPrefix.Length;
				}
			}

			if ((!extendedSyntax || extendedUncSyntax) && (path.Length > 0) && IsDirectorySeparator(path[0])) {
				// UNC or simple rooted path (e.g. "\foo", NOT "\\?\C:\foo")

				i = 1; //  Drive rooted (\foo) is one character

				if (extendedUncSyntax || ((path.Length > 1) && IsDirectorySeparator(path[1]))) {
					// UNC (\\?\UNC\ or \\), scan past the next two directory separators at most
					// (e.g. to \\?\UNC\Server\Share or \\Server\Share\)
					i = uncRootLength;
					int n = 2; // Maximum separators to skip

					while ((i < path.Length) && (!IsDirectorySeparator(path[i]) || (--n > 0))) {
						i++;
					}
				}
			} else if ((path.Length >= volumeSeparatorLength) &&
				(path[volumeSeparatorLength - 1] == System.IO.Path.VolumeSeparatorChar)) {
				// Path is at least longer than where we expect a colon, and has a colon (\\?\A:, A:)
				// If the colon is followed by a directory separator, move past it
				i = volumeSeparatorLength;

				if ((path.Length >= volumeSeparatorLength + 1) && IsDirectorySeparator(path[volumeSeparatorLength])) {
					i++;
				}
			}

			return i;
		}
	}
}
#endif
