// ----------------------------------------------------------------------------------------------
//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// ----------------------------------------------------------------------------------------------
// |
// Copyright 2015-2025 Łukasz "JustArchi" Domeradzki
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
using System.Collections;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Resources;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.NLog;
using ArchiSteamFarm.Storage;
using Humanizer;
using JetBrains.Annotations;
using Microsoft.IdentityModel.JsonWebTokens;
using SteamKit2;

namespace ArchiSteamFarm.Core;

public static class Utilities {
	private const byte MaxSharingViolationTries = 15;
	private const uint SharingViolationHResult = 0x80070020;
	private const byte TimeoutForLongRunningTasksInSeconds = 60;
	private const uint UnauthorizedAccessHResult = 0x80070005;

	private static readonly FrozenSet<char> DirectorySeparators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

	[PublicAPI]
	public static IEnumerable<T> AsLinqThreadSafeEnumerable<T>(this ICollection<T> collection) {
		ArgumentNullException.ThrowIfNull(collection);

		// See: https://github.com/dotnet/runtime/discussions/50687
		return collection.Select(static entry => entry);
	}

	[PublicAPI]
	public static string AsMasked(this string text, char mask = '*') {
		ArgumentNullException.ThrowIfNull(text);

		return new string(mask, text.Length);
	}

	[PublicAPI]
	public static string GenerateChecksumFor(byte[] source) {
		ArgumentNullException.ThrowIfNull(source);

		byte[] hash = SHA512.HashData(source);

		return Convert.ToHexString(hash);
	}

	[PublicAPI]
	public static string GetArgsAsText(string[] args, byte argsToSkip, string delimiter) {
		ArgumentNullException.ThrowIfNull(args);

		if (args.Length <= argsToSkip) {
			throw new InvalidOperationException($"{nameof(args.Length)} && {nameof(argsToSkip)}");
		}

		ArgumentException.ThrowIfNullOrEmpty(delimiter);

		return string.Join(delimiter, args.Skip(argsToSkip));
	}

	[PublicAPI]
	public static string GetArgsAsText(string text, byte argsToSkip) {
		ArgumentException.ThrowIfNullOrEmpty(text);

		string[] args = text.Split(Array.Empty<char>(), argsToSkip + 1, StringSplitOptions.RemoveEmptyEntries);

		return args[^1];
	}

	[PublicAPI]
	public static string? GetCookieValue(this CookieContainer cookieContainer, Uri uri, string name) {
		ArgumentNullException.ThrowIfNull(cookieContainer);
		ArgumentNullException.ThrowIfNull(uri);
		ArgumentException.ThrowIfNullOrEmpty(name);

		CookieCollection cookies = cookieContainer.GetCookies(uri);

		return cookies.FirstOrDefault(cookie => cookie.Name == name)?.Value;
	}

	[PublicAPI]
	public static ulong GetUnixTime() => (ulong) DateTimeOffset.UtcNow.ToUnixTimeSeconds();

	[PublicAPI]
	public static async void InBackground(Action action, bool longRunning = false) {
		ArgumentNullException.ThrowIfNull(action);

		TaskCreationOptions options = TaskCreationOptions.DenyChildAttach;

		if (longRunning) {
			options |= TaskCreationOptions.LongRunning | TaskCreationOptions.PreferFairness;
		}

		await Task.Factory.StartNew(action, CancellationToken.None, options, TaskScheduler.Default).ConfigureAwait(false);
	}

	[PublicAPI]
	public static void InBackground<T>(Func<T> function, bool longRunning = false) {
		ArgumentNullException.ThrowIfNull(function);

		InBackground(void () => function(), longRunning);
	}

	[PublicAPI]
	public static async Task<IList<T>> InParallel<T>(IEnumerable<Task<T>> tasks) {
		ArgumentNullException.ThrowIfNull(tasks);

		switch (ASF.GlobalConfig?.OptimizationMode) {
			case GlobalConfig.EOptimizationMode.MinMemoryUsage:
				List<T> results = [];

				foreach (Task<T> task in tasks) {
					results.Add(await task.ConfigureAwait(false));
				}

				return results;
			default:
				return await Task.WhenAll(tasks).ConfigureAwait(false);
		}
	}

	[PublicAPI]
	public static async Task InParallel(IEnumerable<Task> tasks) {
		ArgumentNullException.ThrowIfNull(tasks);

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
	public static bool IsValidCdKey(string key) {
		ArgumentException.ThrowIfNullOrEmpty(key);

		return GeneratedRegexes.CdKey().IsMatch(key);
	}

	[PublicAPI]
	public static bool IsValidHexadecimalText(string text) {
		ArgumentException.ThrowIfNullOrEmpty(text);

		return (text.Length % 2 == 0) && text.All(Uri.IsHexDigit);
	}

	[PublicAPI]
	public static IEnumerable<T> ToEnumerable<T>(this T item) {
		yield return item;
	}

	[PublicAPI]
	public static string ToHumanReadable(this TimeSpan timeSpan) => timeSpan.Humanize(3, maxUnit: TimeUnit.Year, minUnit: TimeUnit.Second);

	[PublicAPI]
	public static Task<T> ToLongRunningTask<T>(this AsyncJob<T> job) where T : CallbackMsg {
		ArgumentNullException.ThrowIfNull(job);

		job.Timeout = TimeSpan.FromSeconds(TimeoutForLongRunningTasksInSeconds);

		return job.ToTask();
	}

	[PublicAPI]
	public static Task<AsyncJobMultiple<T>.ResultSet> ToLongRunningTask<T>(this AsyncJobMultiple<T> job) where T : CallbackMsg {
		ArgumentNullException.ThrowIfNull(job);

		job.Timeout = TimeSpan.FromSeconds(TimeoutForLongRunningTasksInSeconds);

		return job.ToTask();
	}

	[PublicAPI]
	public static bool TryReadJsonWebToken(string token, [NotNullWhen(true)] out JsonWebToken? result) {
		ArgumentException.ThrowIfNullOrEmpty(token);

		try {
			result = new JsonWebToken(token);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericDebuggingException(e);

			result = null;

			return false;
		}

		return true;
	}

	internal static ulong MathAdd(ulong first, int second) {
		if (second >= 0) {
			return first + (uint) second;
		}

		return first - (uint) -second;
	}

	internal static void OnProgressChanged(string fileName, byte progressPercentage) {
		ArgumentException.ThrowIfNullOrEmpty(fileName);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(progressPercentage, 100);

		const byte printEveryPercentage = 10;

		if (progressPercentage % printEveryPercentage != 0) {
			return;
		}

		ASF.ArchiLogger.LogGenericDebug($"{fileName} {progressPercentage}%...");
	}

	internal static async Task<bool> UpdateCleanup(string targetDirectory) {
		ArgumentException.ThrowIfNullOrEmpty(targetDirectory);

		bool updateCleanup = false;

		try {
			string updateDirectory = Path.Combine(targetDirectory, SharedInfo.UpdateDirectoryNew);

			if (Directory.Exists(updateDirectory)) {
				if (!updateCleanup) {
					updateCleanup = true;

					ASF.ArchiLogger.LogGenericInfo(Strings.UpdateCleanup);
				}

				Directory.Delete(updateDirectory, true);
			}

			string backupDirectory = Path.Combine(targetDirectory, SharedInfo.UpdateDirectoryOld);

			if (Directory.Exists(backupDirectory)) {
				if (!updateCleanup) {
					updateCleanup = true;

					ASF.ArchiLogger.LogGenericInfo(Strings.UpdateCleanup);
				}

				await DeletePotentiallyUsedDirectory(backupDirectory).ConfigureAwait(false);
			}
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);

			return false;
		}

		if (updateCleanup) {
			ASF.ArchiLogger.LogGenericInfo(Strings.Done);
		}

		return true;
	}

	internal static async Task<bool> UpdateFromArchive(ZipArchive zipArchive, string targetDirectory) {
		ArgumentNullException.ThrowIfNull(zipArchive);
		ArgumentException.ThrowIfNullOrEmpty(targetDirectory);

		// Firstly, ensure once again our directories are purged and ready to work with
		if (!await UpdateCleanup(targetDirectory).ConfigureAwait(false)) {
			return false;
		}

		// Now extract the zip file to entirely new location, this decreases chance of corruptions if user kills the process during this stage
		string updateDirectory = Path.Combine(targetDirectory, SharedInfo.UpdateDirectoryNew);

		await zipArchive.ExtractToDirectoryAsync(updateDirectory, true).ConfigureAwait(false);

		// Now, critical section begins, we're going to move all files from target directory to a backup directory
		string backupDirectory = Path.Combine(targetDirectory, SharedInfo.UpdateDirectoryOld);

		Directory.CreateDirectory(backupDirectory);

		MoveAllUpdateFiles(targetDirectory, backupDirectory);

		// Finally, we can move the newly extracted files to target directory
		MoveAllUpdateFiles(updateDirectory, targetDirectory, backupDirectory);

		// Critical section has finished, we can now cleanup the update directory, backup directory must wait for the process restart
		Directory.Delete(updateDirectory, true);

		// The update process is done
		return true;
	}

	internal static void WarnAboutIncompleteTranslation(ResourceManager resourceManager) {
		ArgumentNullException.ThrowIfNull(resourceManager);

		// Skip translation progress for English and invariant (such as "C") cultures
		switch (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) {
			case "en" or "iv" or "qps":
				return;
		}

		// We can't dispose this resource set, as we can't be sure if it isn't used somewhere else, rely on GC in this case
		ResourceSet? defaultResourceSet = resourceManager.GetResourceSet(CultureInfo.GetCultureInfo("en-US"), true, true);

		if (defaultResourceSet == null) {
			ASF.ArchiLogger.LogNullError(defaultResourceSet);

			return;
		}

		HashSet<DictionaryEntry> defaultStringObjects = defaultResourceSet.Cast<DictionaryEntry>().ToHashSet();

		if (defaultStringObjects.Count == 0) {
			// This means we don't have entries for English, so there is nothing to check against
			// Can happen e.g. for plugins with no strings declared which are calling this function
			return;
		}

		// We can't dispose this resource set, as we can't be sure if it isn't used somewhere else, rely on GC in this case
		ResourceSet? currentResourceSet = resourceManager.GetResourceSet(CultureInfo.CurrentUICulture, true, true);

		if (currentResourceSet == null) {
			ASF.ArchiLogger.LogNullError(currentResourceSet);

			return;
		}

		HashSet<DictionaryEntry> currentStringObjects = currentResourceSet.Cast<DictionaryEntry>().ToHashSet();

		if (currentStringObjects.Count >= defaultStringObjects.Count) {
			// Either we have 100% finished translation, or we're missing it entirely and using en-US
			HashSet<DictionaryEntry> testStringObjects = currentStringObjects.ToHashSet();
			testStringObjects.ExceptWith(defaultStringObjects);

			// If we got 0 as final result, this is the missing language
			// Otherwise it's just a small amount of strings that happen to be the same
			if (testStringObjects.Count == 0) {
				currentStringObjects = testStringObjects;
			}
		}

		if (currentStringObjects.Count < defaultStringObjects.Count) {
			float translationCompleteness = currentStringObjects.Count / (float) defaultStringObjects.Count;
			ASF.ArchiLogger.LogGenericInfo(Strings.FormatTranslationIncomplete($"{CultureInfo.CurrentUICulture.Name} ({CultureInfo.CurrentUICulture.EnglishName})", translationCompleteness.ToString("P1", CultureInfo.CurrentCulture)));
		}
	}

	private static async Task DeletePotentiallyUsedDirectory(string directory) {
		ArgumentException.ThrowIfNullOrEmpty(directory);

		for (byte i = 1; (i <= MaxSharingViolationTries) && Directory.Exists(directory); i++) {
			if (i > 1) {
				await Task.Delay(1000).ConfigureAwait(false);
			}

			try {
				Directory.Delete(directory, true);
			} catch (IOException e) when ((i < MaxSharingViolationTries) && ((uint) e.HResult == SharingViolationHResult)) {
				// It's entirely possible that old process is still running, we allow this to happen and add additional delay
				ASF.ArchiLogger.LogGenericDebuggingException(e);

				continue;
			} catch (UnauthorizedAccessException e) when ((i < MaxSharingViolationTries) && ((uint) e.HResult == UnauthorizedAccessHResult)) {
				// It's entirely possible that old process is still running, we allow this to happen and add additional delay
				ASF.ArchiLogger.LogGenericDebuggingException(e);

				continue;
			}

			return;
		}
	}

	private static void MoveAllUpdateFiles(string sourceDirectory, string targetDirectory, string? backupDirectory = null) {
		ArgumentException.ThrowIfNullOrEmpty(sourceDirectory);
		ArgumentException.ThrowIfNullOrEmpty(targetDirectory);

		// Determine if targetDirectory is within sourceDirectory, if yes we need to skip it from enumeration further below
		string targetRelativeDirectoryPath = Path.GetRelativePath(sourceDirectory, targetDirectory);

		// We keep user files if backup directory is null, as it means we're creating one
		bool keepUserFiles = string.IsNullOrEmpty(backupDirectory);

		foreach (string file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories)) {
			string fileName = Path.GetFileName(file);

			if (string.IsNullOrEmpty(fileName)) {
				throw new InvalidOperationException(nameof(fileName));
			}

			string relativeFilePath = Path.GetRelativePath(sourceDirectory, file);

			if (string.IsNullOrEmpty(relativeFilePath)) {
				throw new InvalidOperationException(nameof(relativeFilePath));
			}

			string? relativeDirectoryName = Path.GetDirectoryName(relativeFilePath);

			switch (relativeDirectoryName) {
				case null:
					throw new InvalidOperationException(nameof(relativeDirectoryName));
				case "":
					// No directory, root folder
					switch (fileName) {
						case Logging.NLogConfigurationFile when keepUserFiles:
						case SharedInfo.LogFile when keepUserFiles:
							// Files with those names in root directory we want to keep
							continue;
					}

					break;
				case SharedInfo.ArchivalLogsDirectory when keepUserFiles:
				case SharedInfo.ConfigDirectory when keepUserFiles:
				case SharedInfo.DebugDirectory when keepUserFiles:
				case SharedInfo.PluginsDirectory when keepUserFiles:
				case SharedInfo.UpdateDirectoryNew:
				case SharedInfo.UpdateDirectoryOld:
					// Files in those constant directories we want to keep in their current place
					continue;
				default:
					// If we're moving files deeper into source location, we need to skip the newly created location from it
					if (!string.IsNullOrEmpty(targetRelativeDirectoryPath) && ((relativeDirectoryName == targetRelativeDirectoryPath) || RelativeDirectoryStartsWith(relativeDirectoryName, targetRelativeDirectoryPath))) {
						continue;
					}

					// Below code block should match the case above, it handles subdirectories
					if (RelativeDirectoryStartsWith(relativeDirectoryName, SharedInfo.UpdateDirectoryNew, SharedInfo.UpdateDirectoryOld)) {
						continue;
					}

					if (keepUserFiles && RelativeDirectoryStartsWith(relativeDirectoryName, SharedInfo.ArchivalLogsDirectory, SharedInfo.ConfigDirectory, SharedInfo.DebugDirectory, SharedInfo.PluginsDirectory)) {
						continue;
					}

					break;
			}

			// We're going to move this file out of the current place, overwriting existing one if needed
			string targetUpdateDirectory;

			if (relativeDirectoryName.Length > 0) {
				// File inside a subdirectory
				targetUpdateDirectory = Path.Combine(targetDirectory, relativeDirectoryName);

				Directory.CreateDirectory(targetUpdateDirectory);
			} else {
				// File in root directory
				targetUpdateDirectory = targetDirectory;
			}

			string targetUpdateFile = Path.Combine(targetUpdateDirectory, fileName);

			// If target update file exists and we have a backup directory, we should consider moving it to the backup directory regardless whether or not we did that before as part of backup procedure
			// This achieves two purposes, firstly, we ensure additional backup of user file in case something goes wrong, and secondly, we decrease a possibility of overwriting files that are in-use on Windows, since we move them out of the picture first
			if (!string.IsNullOrEmpty(backupDirectory) && File.Exists(targetUpdateFile)) {
				string targetBackupDirectory;

				if (relativeDirectoryName.Length > 0) {
					// File inside a subdirectory
					targetBackupDirectory = Path.Combine(backupDirectory, relativeDirectoryName);

					Directory.CreateDirectory(targetBackupDirectory);
				} else {
					// File in root directory
					targetBackupDirectory = backupDirectory;
				}

				string targetBackupFile = Path.Combine(targetBackupDirectory, fileName);

				File.Move(targetUpdateFile, targetBackupFile, true);
			}

			File.Move(file, targetUpdateFile, true);
		}
	}

	private static bool RelativeDirectoryStartsWith(string directory, params string[] prefixes) {
		ArgumentException.ThrowIfNullOrEmpty(directory);

		if ((prefixes == null) || (prefixes.Length == 0)) {
			throw new ArgumentNullException(nameof(prefixes));
		}

		return prefixes.Any(prefix => !string.IsNullOrEmpty(prefix) && (directory.Length > prefix.Length) && DirectorySeparators.Contains(directory[prefix.Length]) && directory.StartsWith(prefix, StringComparison.Ordinal));
	}

#pragma warning disable CA1034 // False positive, there's no other way we can declare this block
	extension(HttpStatusCode statusCode) {
		[PublicAPI]
		public bool IsClientErrorCode() => statusCode is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError;

		[PublicAPI]
		public bool IsRedirectionCode() => statusCode is >= HttpStatusCode.Ambiguous and < HttpStatusCode.BadRequest;

		[PublicAPI]
		public bool IsServerErrorCode() => statusCode is >= HttpStatusCode.InternalServerError and < (HttpStatusCode) 600;

		[PublicAPI]
		public bool IsSuccessCode() => statusCode is >= HttpStatusCode.OK and < HttpStatusCode.Ambiguous;
	}
#pragma warning restore CA1034 // False positive, there's no other way we can declare this block
}
