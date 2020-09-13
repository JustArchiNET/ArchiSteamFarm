//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2020 Łukasz "JustArchi" Domeradzki
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
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Dom;
using AngleSharp.XPath;
using Humanizer;
using Humanizer.Localisation;
using JetBrains.Annotations;
using SteamKit2;

namespace ArchiSteamFarm {
	public static class Utilities {
		private const byte TimeoutForLongRunningTasksInSeconds = 60;

		// Normally we wouldn't need to use this singleton, but we want to ensure decent randomness across entire program's lifetime
		private static readonly Random Random = new Random();

		[PublicAPI]
		public static string GetArgsAsText(string[] args, byte argsToSkip, string delimiter) {
			if ((args == null) || (args.Length <= argsToSkip) || string.IsNullOrEmpty(delimiter)) {
				throw new ArgumentNullException(nameof(args) + " || " + nameof(argsToSkip) + " || " + nameof(delimiter));
			}

			return string.Join(delimiter, args.Skip(argsToSkip));
		}

		[PublicAPI]
		public static string GetArgsAsText(string text, byte argsToSkip) {
			if (string.IsNullOrEmpty(text)) {
				throw new ArgumentNullException(nameof(text));
			}

			string[] args = text.Split(new char[0], argsToSkip + 1, StringSplitOptions.RemoveEmptyEntries);

			return args[^1];
		}

		[PublicAPI]
		public static string? GetCookieValue(this CookieContainer cookieContainer, string url, string name) {
			if ((cookieContainer == null) || string.IsNullOrEmpty(url) || string.IsNullOrEmpty(name)) {
				throw new ArgumentNullException(nameof(cookieContainer) + " || " + nameof(url) + " || " + nameof(name));
			}

			CookieCollection cookies = cookieContainer.GetCookies(new Uri(url));

#if NETFRAMEWORK
			return cookies.Count > 0 ? (from Cookie cookie in cookies where cookie.Name == name select cookie.Value).FirstOrDefault() : null;
#else
			return cookies.Count > 0 ? cookies.FirstOrDefault(cookie => cookie.Name == name)?.Value : null;
#endif
		}

		[PublicAPI]
		public static uint GetUnixTime() => (uint) DateTimeOffset.UtcNow.ToUnixTimeSeconds();

		[PublicAPI]
		public static async void InBackground(Action action, bool longRunning = false) {
			if (action == null) {
				throw new ArgumentNullException(nameof(action));
			}

			TaskCreationOptions options = TaskCreationOptions.DenyChildAttach;

			if (longRunning) {
				options |= TaskCreationOptions.LongRunning | TaskCreationOptions.PreferFairness;
			}

			await Task.Factory.StartNew(action, CancellationToken.None, options, TaskScheduler.Default).ConfigureAwait(false);
		}

		[PublicAPI]
		public static async void InBackground<T>(Func<T> function, bool longRunning = false) {
			if (function == null) {
				throw new ArgumentNullException(nameof(function));
			}

			TaskCreationOptions options = TaskCreationOptions.DenyChildAttach;

			if (longRunning) {
				options |= TaskCreationOptions.LongRunning | TaskCreationOptions.PreferFairness;
			}

			await Task.Factory.StartNew(function, CancellationToken.None, options, TaskScheduler.Default).ConfigureAwait(false);
		}

		[PublicAPI]
		public static async Task<IList<T>> InParallel<T>(IEnumerable<Task<T>> tasks) {
			if (tasks == null) {
				throw new ArgumentNullException(nameof(tasks));
			}

			IList<T> results;

			switch (ASF.GlobalConfig?.OptimizationMode) {
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

		[PublicAPI]
		public static async Task InParallel(IEnumerable<Task> tasks) {
			if (tasks == null) {
				throw new ArgumentNullException(nameof(tasks));
			}

			switch (ASF.GlobalConfig?.OptimizationMode) {
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

		[PublicAPI]
		public static bool IsClientErrorCode(this HttpStatusCode statusCode) => (statusCode >= HttpStatusCode.BadRequest) && (statusCode < HttpStatusCode.InternalServerError);

		[PublicAPI]
		public static bool IsServerErrorCode(this HttpStatusCode statusCode) => (statusCode >= HttpStatusCode.InternalServerError) && (statusCode < (HttpStatusCode) 600);

		[PublicAPI]
		public static bool IsValidCdKey(string key) {
			if (string.IsNullOrEmpty(key)) {
				throw new ArgumentNullException(nameof(key));
			}

			return Regex.IsMatch(key, @"^[0-9A-Z]{4,7}-[0-9A-Z]{4,7}-[0-9A-Z]{4,7}(?:(?:-[0-9A-Z]{4,7})?(?:-[0-9A-Z]{4,7}))?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
		}

		[PublicAPI]
		public static bool IsValidHexadecimalText(string text) {
			if (string.IsNullOrEmpty(text)) {
				throw new ArgumentNullException(nameof(text));
			}

			return (text.Length % 2 == 0) && text.All(Uri.IsHexDigit);
		}

		[PublicAPI]
		public static int RandomNext() {
			lock (Random) {
				return Random.Next();
			}
		}

		[PublicAPI]
		public static int RandomNext(int maxValue) {
			if (maxValue < 0) {
				throw new ArgumentOutOfRangeException(nameof(maxValue));
			}

			if (maxValue <= 1) {
				return maxValue;
			}

			lock (Random) {
				return Random.Next(maxValue);
			}
		}

		[PublicAPI]
		public static int RandomNext(int minValue, int maxValue) {
			if (minValue > maxValue) {
				throw new ArgumentOutOfRangeException(nameof(minValue) + " && " + nameof(maxValue));
			}

			if (minValue >= maxValue - 1) {
				return minValue;
			}

			lock (Random) {
				return Random.Next(minValue, maxValue);
			}
		}

		[PublicAPI]
		public static List<IElement> SelectElementNodes(this IElement element, string xpath) => element.SelectNodes(xpath).Cast<IElement>().ToList();

		[PublicAPI]
		public static List<IElement> SelectNodes(this IDocument document, string xpath) => document.Body.SelectNodes(xpath).Cast<IElement>().ToList();

		[PublicAPI]
		public static IElement? SelectSingleElementNode(this IElement element, string xpath) => (IElement?) element.SelectSingleNode(xpath);

		[PublicAPI]
		public static IElement? SelectSingleNode(this IDocument document, string xpath) => (IElement?) document.Body.SelectSingleNode(xpath);

		[PublicAPI]
		public static IEnumerable<T> ToEnumerable<T>(this T item) {
			yield return item;
		}

		[PublicAPI]
		public static string ToHumanReadable(this TimeSpan timeSpan) => timeSpan.Humanize(3, maxUnit: TimeUnit.Year, minUnit: TimeUnit.Second);

		[PublicAPI]
		public static Task<T> ToLongRunningTask<T>(this AsyncJob<T> job) where T : CallbackMsg {
			if (job == null) {
				throw new ArgumentNullException(nameof(job));
			}

			job.Timeout = TimeSpan.FromSeconds(TimeoutForLongRunningTasksInSeconds);

			return job.ToTask();
		}

		[PublicAPI]
		public static Task<AsyncJobMultiple<T>.ResultSet> ToLongRunningTask<T>(this AsyncJobMultiple<T> job) where T : CallbackMsg {
			if (job == null) {
				throw new ArgumentNullException(nameof(job));
			}

			job.Timeout = TimeSpan.FromSeconds(TimeoutForLongRunningTasksInSeconds);

			return job.ToTask();
		}

		internal static void DeleteEmptyDirectoriesRecursively(string directory) {
			if (string.IsNullOrEmpty(directory)) {
				throw new ArgumentNullException(nameof(directory));
			}

			if (!Directory.Exists(directory)) {
				return;
			}

			try {
				foreach (string subDirectory in Directory.EnumerateDirectories(directory)) {
					DeleteEmptyDirectoriesRecursively(subDirectory);
				}

				if (!Directory.EnumerateFileSystemEntries(directory).Any()) {
					Directory.Delete(directory);
				}
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);
			}
		}

		internal static bool RelativeDirectoryStartsWith(string directory, params string[] prefixes) {
			if (string.IsNullOrEmpty(directory) || (prefixes == null) || (prefixes.Length == 0)) {
				throw new ArgumentNullException(nameof(directory) + " || " + nameof(prefixes));
			}

			return (from prefix in prefixes where directory.Length > prefix.Length let pathSeparator = directory[prefix.Length] where (pathSeparator == Path.DirectorySeparatorChar) || (pathSeparator == Path.AltDirectorySeparatorChar) select prefix).Any(prefix => directory.StartsWith(prefix, StringComparison.Ordinal));
		}
	}
}
