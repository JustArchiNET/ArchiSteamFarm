//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// 
// Copyright 2015-2018 Łukasz "JustArchi" Domeradzki
// Contact: JustArchi@JustArchi.net
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
// http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Humanizer;
using Humanizer.Localisation;

namespace ArchiSteamFarm {
	internal static class Utilities {
		// Normally we wouldn't need to use this singleton, but we want to ensure decent randomness across entire program's lifetime
		private static readonly Random Random = new Random();

		internal static string GetArgsAsText(string[] args, byte argsToSkip, string delimiter) {
			if ((args == null) || (args.Length <= argsToSkip) || string.IsNullOrEmpty(delimiter)) {
				ASF.ArchiLogger.LogNullError(nameof(args) + " || " + nameof(argsToSkip) + " || " + nameof(delimiter));
				return null;
			}

			return string.Join(delimiter, args.Skip(argsToSkip));
		}

		internal static string GetArgsAsText(string text, byte argsToSkip) {
			if (string.IsNullOrEmpty(text)) {
				ASF.ArchiLogger.LogNullError(nameof(text));
				return null;
			}

			string[] args = text.Split((char[]) null, argsToSkip + 1, StringSplitOptions.RemoveEmptyEntries);
			return args[args.Length - 1];
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
			return cookies.Count > 0 ? (from Cookie cookie in cookies where cookie.Name.Equals(name) select cookie.Value).FirstOrDefault() : null;
		}

		internal static uint GetUnixTime() => (uint) DateTimeOffset.UtcNow.ToUnixTimeSeconds();

		internal static void InBackground(Action action, bool longRunning = false) {
			if (action == null) {
				ASF.ArchiLogger.LogNullError(nameof(action));
				return;
			}

			TaskCreationOptions options = TaskCreationOptions.DenyChildAttach;

			if (longRunning) {
				options |= TaskCreationOptions.LongRunning | TaskCreationOptions.PreferFairness;
			}

			Task.Factory.StartNew(action, CancellationToken.None, options, TaskScheduler.Default);
		}

		internal static void InBackground<T>(Func<T> function, bool longRunning = false) {
			if (function == null) {
				ASF.ArchiLogger.LogNullError(nameof(function));
				return;
			}

			TaskCreationOptions options = TaskCreationOptions.DenyChildAttach;

			if (longRunning) {
				options |= TaskCreationOptions.LongRunning | TaskCreationOptions.PreferFairness;
			}

			Task.Factory.StartNew(function, CancellationToken.None, options, TaskScheduler.Default);
		}

		internal static async Task<IList<T>> InParallel<T>(IEnumerable<Task<T>> tasks) {
			if (tasks == null) {
				ASF.ArchiLogger.LogNullError(nameof(tasks));
				return null;
			}

			IList<T> results;

			switch (Program.GlobalConfig.OptimizationMode) {
				case GlobalConfig.EOptimizationMode.MinMemoryUsage:
					results = new List<T>();

					foreach (Task<T> task in tasks) {
						results.Add(await task.ConfigureAwait(false));
					}

					break;
				default:
					results = await Task.WhenAll(tasks).ConfigureAwait(false);
					break;
			}

			return results;
		}

		internal static async Task InParallel(IEnumerable<Task> tasks) {
			if (tasks == null) {
				ASF.ArchiLogger.LogNullError(nameof(tasks));
				return;
			}

			switch (Program.GlobalConfig.OptimizationMode) {
				case GlobalConfig.EOptimizationMode.MinMemoryUsage:
					foreach (Task task in tasks) {
						await task.ConfigureAwait(false);
					}

					break;
				default:
					await Task.WhenAll(tasks).ConfigureAwait(false);
					break;
			}
		}

		internal static bool IsValidCdKey(string key) {
			if (string.IsNullOrEmpty(key)) {
				ASF.ArchiLogger.LogNullError(nameof(key));
				return false;
			}

			return Regex.IsMatch(key, @"^[0-9A-Z]{4,7}-[0-9A-Z]{4,7}-[0-9A-Z]{4,7}(?:(?:-[0-9A-Z]{4,7})?(?:-[0-9A-Z]{4,7}))?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
		}

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

		internal static int RandomNext(int maxWithout) {
			if (maxWithout <= 0) {
				ASF.ArchiLogger.LogNullError(nameof(maxWithout));
				return -1;
			}

			if (maxWithout == 1) {
				return 0;
			}

			lock (Random) {
				return Random.Next(maxWithout);
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

		internal static IEnumerable<T> ToEnumerable<T>(this T item) {
			yield return item;
		}

		internal static string ToHumanReadable(this TimeSpan timeSpan) => timeSpan.Humanize(3, maxUnit: TimeUnit.Year, minUnit: TimeUnit.Second);
	}
}
