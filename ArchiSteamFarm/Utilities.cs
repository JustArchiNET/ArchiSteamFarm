//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// 
//  Copyright 2015-2017 Łukasz "JustArchi" Domeradzki
//  Contact: JustArchi@JustArchi.net
// 
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
// 
//  http://www.apache.org/licenses/LICENSE-2.0
//      
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Humanizer;
using Humanizer.Localisation;

namespace ArchiSteamFarm {
	internal static class Utilities {
		private static readonly Random Random = new Random();

		[SuppressMessage("ReSharper", "UnusedParameter.Global")]
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal static void Forget(this object obj) { }

		internal static string GetArgsString(string[] args, byte argsToSkip = 1, string delimiter = " ") {
			if ((args == null) || (args.Length < argsToSkip) || string.IsNullOrEmpty(delimiter)) {
				ASF.ArchiLogger.LogNullError(nameof(args) + " || " + nameof(delimiter));
				return null;
			}

			string result = string.Join(delimiter, GetArgs(args, argsToSkip));
			return result;
		}

		internal static string GetCookieValue(this CookieContainer cookieContainer, string url, string name) {
			if ((cookieContainer == null) || string.IsNullOrEmpty(url) || string.IsNullOrEmpty(name)) {
				ASF.ArchiLogger.LogNullError(nameof(cookieContainer) + " || " + nameof(url) + " || " + nameof(name));
				return null;
			}

			Uri uri;

			try {
				uri = new Uri(url);
			} catch (UriFormatException e) {
				ASF.ArchiLogger.LogGenericException(e);
				return null;
			}

			CookieCollection cookies = cookieContainer.GetCookies(uri);
			return cookies.Count != 0 ? (from Cookie cookie in cookies where cookie.Name.Equals(name) select cookie.Value).FirstOrDefault() : null;
		}

		internal static uint GetUnixTime() => (uint) DateTimeOffset.UtcNow.ToUnixTimeSeconds();

		internal static bool IsValidHexadecimalString(string text) {
			if (string.IsNullOrEmpty(text)) {
				ASF.ArchiLogger.LogNullError(nameof(text));
				return false;
			}

			const byte split = 16;
			for (byte i = 0; i < text.Length; i += split) {
				string textPart = string.Join("", text.Skip(i).Take(split));

				if (!ulong.TryParse(textPart, NumberStyles.HexNumber, null, out _)) {
					return false;
				}
			}

			return true;
		}

		internal static int RandomNext() {
			lock (Random) {
				return Random.Next();
			}
		}

		internal static string ReadLineMasked(char mask = '*') {
			StringBuilder result = new StringBuilder();

			ConsoleKeyInfo keyInfo;
			while ((keyInfo = Console.ReadKey(true)).Key != ConsoleKey.Enter) {
				if (!char.IsControl(keyInfo.KeyChar)) {
					result.Append(keyInfo.KeyChar);
					Console.Write(mask);
				} else if ((keyInfo.Key == ConsoleKey.Backspace) && (result.Length > 0)) {
					result.Remove(result.Length - 1, 1);

					if (Console.CursorLeft == 0) {
						Console.SetCursorPosition(Console.BufferWidth - 1, Console.CursorTop - 1);
						Console.Write(' ');
						Console.SetCursorPosition(Console.BufferWidth - 1, Console.CursorTop - 1);
					} else {
						// There are two \b characters here
						Console.Write(@" ");
					}
				}
			}

			Console.WriteLine();
			return result.ToString();
		}

		internal static void StartBackgroundAction(Action action, bool longRunning = true) {
			if (action == null) {
				ASF.ArchiLogger.LogNullError(nameof(action));
				return;
			}

			TaskCreationOptions options = TaskCreationOptions.DenyChildAttach;

			if (longRunning) {
				options |= TaskCreationOptions.LongRunning;
			}

			Task.Factory.StartNew(action, options).Forget();
		}

		internal static void StartBackgroundFunction(Func<Task> function, bool longRunning = true) {
			if (function == null) {
				ASF.ArchiLogger.LogNullError(nameof(function));
				return;
			}

			TaskCreationOptions options = TaskCreationOptions.DenyChildAttach;

			if (longRunning) {
				options |= TaskCreationOptions.LongRunning;
			}

			Task.Factory.StartNew(function, options).Forget();
		}

		internal static IEnumerable<T> ToEnumerable<T>(this T item) {
			yield return item;
		}

		internal static string ToHumanReadable(this TimeSpan timeSpan) => timeSpan.Humanize(3, maxUnit: TimeUnit.Year);

		private static string[] GetArgs(string[] args, byte argsToSkip = 1) {
			if ((args == null) || (args.Length < argsToSkip)) {
				ASF.ArchiLogger.LogNullError(nameof(args));
				return null;
			}

			byte argsToCopy = (byte) (args.Length - argsToSkip);
			string[] result = new string[argsToCopy];

			if (argsToCopy > 0) {
				Array.Copy(args, argsToSkip, result, 0, argsToCopy);
			}

			return result;
		}
	}
}