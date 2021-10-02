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

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Dom;
using AngleSharp.XPath;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Storage;
using Humanizer;
using Humanizer.Localisation;
using JetBrains.Annotations;
using SteamKit2;
#if NETFRAMEWORK
using JustArchiNET.Madness;
#endif

namespace ArchiSteamFarm.Core {
	public static class Utilities {
		private const byte MinimumRecommendedPasswordCharacters = 10;

		private const byte TimeoutForLongRunningTasksInSeconds = 60;

		private static readonly ImmutableHashSet<string> ForbiddenPasswordPhrases = ImmutableHashSet.Create(StringComparer.InvariantCultureIgnoreCase, "asf", "archi", "steam", "farm", "password", "ipc", "api", "gui", "asf-ui");

		// Normally we wouldn't need to use this singleton, but we want to ensure decent randomness across entire program's lifetime
		private static readonly Random Random = new();

		[PublicAPI]
		public static string GetArgsAsText(string[] args, byte argsToSkip, string delimiter) {
			if (args == null) {
				throw new ArgumentNullException(nameof(args));
			}

			if (args.Length <= argsToSkip) {
				throw new InvalidOperationException($"{nameof(args.Length)} && {nameof(argsToSkip)}");
			}

			if (string.IsNullOrEmpty(delimiter)) {
				throw new ArgumentNullException(nameof(delimiter));
			}

			return string.Join(delimiter, args.Skip(argsToSkip));
		}

		[PublicAPI]
		public static string GetArgsAsText(string text, byte argsToSkip) {
			if (string.IsNullOrEmpty(text)) {
				throw new ArgumentNullException(nameof(text));
			}

			string[] args = text.Split(Array.Empty<char>(), argsToSkip + 1, StringSplitOptions.RemoveEmptyEntries);

			return args[^1];
		}

		[PublicAPI]
		public static string? GetCookieValue(this CookieContainer cookieContainer, Uri uri, string name) {
			if (cookieContainer == null) {
				throw new ArgumentNullException(nameof(cookieContainer));
			}

			if (uri == null) {
				throw new ArgumentNullException(nameof(uri));
			}

			if (string.IsNullOrEmpty(name)) {
				throw new ArgumentNullException(nameof(name));
			}

			CookieCollection cookies = cookieContainer.GetCookies(uri);

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
		public static bool IsClientErrorCode(this HttpStatusCode statusCode) => statusCode is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError;

		[PublicAPI]
		public static bool IsServerErrorCode(this HttpStatusCode statusCode) => statusCode is >= HttpStatusCode.InternalServerError and < (HttpStatusCode) 600;

		[PublicAPI]
		public static bool IsSuccessCode(this HttpStatusCode statusCode) => statusCode is >= HttpStatusCode.OK and < HttpStatusCode.Ambiguous;

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
#pragma warning disable CA5394 // This call isn't used in a security-sensitive manner
				return Random.Next();
#pragma warning restore CA5394 // This call isn't used in a security-sensitive manner
			}
		}

		[Obsolete("ASF no longer uses this function, re-implement it yourself if needed.")]
		[PublicAPI]
		public static int RandomNext(int maxValue) {
			switch (maxValue) {
				case < 0:
					throw new ArgumentOutOfRangeException(nameof(maxValue));
				case <= 1:
					return 0;
				default:
					lock (Random) {
#pragma warning disable CA5394 // This call isn't used in a security-sensitive manner
						return Random.Next(maxValue);
#pragma warning restore CA5394 // This call isn't used in a security-sensitive manner
					}
			}
		}

		[PublicAPI]
		public static int RandomNext(int minValue, int maxValue) {
			if (minValue > maxValue) {
				throw new InvalidOperationException($"{nameof(minValue)} && {nameof(maxValue)}");
			}

			if (minValue >= maxValue - 1) {
				return minValue;
			}

			lock (Random) {
#pragma warning disable CA5394 // This call isn't used in a security-sensitive manner
				return Random.Next(minValue, maxValue);
#pragma warning restore CA5394 // This call isn't used in a security-sensitive manner
			}
		}

		[Obsolete("ASF no longer uses this function, re-implement it yourself if needed.")]
		[PublicAPI]
		public static IEnumerable<IElement> SelectElementNodes(this IElement element, string xpath) => element.SelectNodes(xpath).OfType<IElement>();

		[PublicAPI]
		public static IEnumerable<IElement> SelectNodes(this IDocument document, string xpath) {
			if (document == null) {
				throw new ArgumentNullException(nameof(document));
			}

			return document.Body.SelectNodes(xpath).OfType<IElement>();
		}

		[PublicAPI]
		public static IElement? SelectSingleElementNode(this IElement element, string xpath) => (IElement?) element.SelectSingleNode(xpath);

		[PublicAPI]
		public static IElement? SelectSingleNode(this IDocument document, string xpath) {
			if (document == null) {
				throw new ArgumentNullException(nameof(document));
			}

			return (IElement?) document.Body.SelectSingleNode(xpath);
		}

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

		internal static bool IsWeakPassword(string password, out string? reason, ISet<string>? additionallyForbiddenPhrases = null) {
			if (string.IsNullOrEmpty(password)) {
				throw new ArgumentNullException(nameof(password));
			}

			HashSet<string> forbiddenPhrases = ForbiddenPasswordPhrases.ToHashSet(StringComparer.InvariantCultureIgnoreCase);

			if (additionallyForbiddenPhrases != null) {
				forbiddenPhrases.UnionWith(additionallyForbiddenPhrases);
			}

			int remainingCharacters = password.Length;

			if (remainingCharacters < MinimumRecommendedPasswordCharacters) {
				reason = string.Format(CultureInfo.CurrentCulture, Strings.PasswordReasonTooShort, MinimumRecommendedPasswordCharacters);

				return true;
			}

			foreach (string forbiddenPhrase in forbiddenPhrases.Where(static phrase => !string.IsNullOrEmpty(phrase))) {
				for (int index = password.IndexOf(forbiddenPhrase, StringComparison.InvariantCultureIgnoreCase); index >= 0; index = password.IndexOf(forbiddenPhrase, index, StringComparison.InvariantCultureIgnoreCase)) {
					remainingCharacters -= forbiddenPhrase.Length - 1;

					if (remainingCharacters < MinimumRecommendedPasswordCharacters) {
						reason = string.Format(CultureInfo.CurrentCulture, Strings.PasswordReasonContextualPhrase, string.Join(", ", forbiddenPhrases));

						return true;
					}
				}
			}

			for (int i = 2; i < password.Length; i++) {
				ushort ch0 = password[i - 2];
				ushort ch1 = password[i - 1];
				ushort ch2 = password[i];

				if (ch0 == ch2 && ch1 == ch2) {
					reason = string.Format(CultureInfo.CurrentCulture, Strings.PasswordReasonRepetitiveCharacters, 3);

					return true;
				}

				if (ch0 == ch2 - 2 && ch1 == ch2 - 1) {
					reason = string.Format(CultureInfo.CurrentCulture, Strings.PasswordReasonSequentialCharacters, 3, "abc");

					return true;
				}

				if (ch0 == ch2 + 2 && ch1 == ch2 + 1) {
					reason = string.Format(CultureInfo.CurrentCulture, Strings.PasswordReasonSequentialCharacters, 3, "cba");

					return true;
				}
			}

			reason = null;

			return false;
		}

		internal static bool RelativeDirectoryStartsWith(string directory, params string[] prefixes) {
			if (string.IsNullOrEmpty(directory)) {
				throw new ArgumentNullException(nameof(directory));
			}

#pragma warning disable CA1508 // False positive, params could be null when explicitly set
			if ((prefixes == null) || (prefixes.Length == 0)) {
#pragma warning restore CA1508 // False positive, params could be null when explicitly set
				throw new ArgumentNullException(nameof(prefixes));
			}

			return (from prefix in prefixes where directory.Length > prefix.Length let pathSeparator = directory[prefix.Length] where (pathSeparator == Path.DirectorySeparatorChar) || (pathSeparator == Path.AltDirectorySeparatorChar) select prefix).Any(prefix => directory.StartsWith(prefix, StringComparison.Ordinal));
		}
	}
}
