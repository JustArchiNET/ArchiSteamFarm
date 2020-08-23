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
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Resources;
using System.Threading.Tasks;
using ArchiSteamFarm.IPC;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.NLog;
using Newtonsoft.Json;
using NLog;
using NLog.Targets;
using SteamKit2;

namespace ArchiSteamFarm {
	internal static class Program {
		internal static string? NetworkGroup { get; private set; }
		internal static bool ProcessRequired { get; private set; }
		internal static bool RestartAllowed { get; private set; } = true;
		internal static bool ShutdownSequenceInitialized { get; private set; }

		private static readonly TaskCompletionSource<byte> ShutdownResetEvent = new TaskCompletionSource<byte>();

		private static bool SystemRequired;

		internal static async Task Exit(byte exitCode = 0) {
			if (exitCode != 0) {
				ASF.ArchiLogger.LogGenericError(Strings.ErrorExitingWithNonZeroErrorCode);
			}

			await Shutdown(exitCode).ConfigureAwait(false);
			Environment.Exit(exitCode);
		}

		internal static async Task Restart() {
			if (!await InitShutdownSequence().ConfigureAwait(false)) {
				return;
			}

			string executableName = Path.GetFileNameWithoutExtension(OS.ProcessFileName);

			if (string.IsNullOrEmpty(executableName)) {
				throw new ArgumentNullException(nameof(executableName));
			}

			IEnumerable<string> arguments = Environment.GetCommandLineArgs().Skip(executableName.Equals(SharedInfo.AssemblyName) ? 1 : 0);

			try {
				Process.Start(OS.ProcessFileName, string.Join(" ", arguments));
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);
			}

			// Give new process some time to take over the window (if needed)
			await Task.Delay(2000).ConfigureAwait(false);

			ShutdownResetEvent.TrySetResult(0);
			Environment.Exit(0);
		}

		private static void HandleCryptKeyArgument(string cryptKey) {
			if (string.IsNullOrEmpty(cryptKey)) {
				throw new ArgumentNullException(nameof(cryptKey));
			}

			ArchiCryptoHelper.SetEncryptionKey(cryptKey);
		}

		private static void HandleNetworkGroupArgument(string networkGroup) {
			if (string.IsNullOrEmpty(networkGroup)) {
				throw new ArgumentNullException(nameof(networkGroup));
			}

			NetworkGroup = networkGroup;
		}

		private static void HandlePathArgument(string path) {
			if (string.IsNullOrEmpty(path)) {
				throw new ArgumentNullException(nameof(path));
			}

			try {
				Directory.SetCurrentDirectory(path);
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);
			}
		}

		private static async Task Init(IReadOnlyCollection<string>? args) {
			AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
			AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
			TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

			// We must register our logging targets as soon as possible
			Target.Register<HistoryTarget>(HistoryTarget.TargetName);
			Target.Register<SteamTarget>(SteamTarget.TargetName);

			if (!await InitCore(args).ConfigureAwait(false)) {
				await Exit(1).ConfigureAwait(false);

				return;
			}

			await InitASF(args).ConfigureAwait(false);
		}

		private static async Task InitASF(IReadOnlyCollection<string>? args) {
			OS.CoreInit();

			Console.Title = SharedInfo.ProgramIdentifier;
			ASF.ArchiLogger.LogGenericInfo(SharedInfo.ProgramIdentifier);

			if (!await InitGlobalConfigAndLanguage().ConfigureAwait(false)) {
				return;
			}

			if (ASF.GlobalConfig == null) {
				throw new ArgumentNullException(nameof(ASF.GlobalConfig));
			}

			// Parse post-init args
			if (args != null) {
				ParsePostInitArgs(args);
			}

			OS.Init(SystemRequired, ASF.GlobalConfig.OptimizationMode);

			await InitGlobalDatabaseAndServices().ConfigureAwait(false);
			await ASF.Init().ConfigureAwait(false);
		}

		private static async Task<bool> InitCore(IReadOnlyCollection<string>? args) {
			Directory.SetCurrentDirectory(SharedInfo.HomeDirectory);

			// Allow loading configs from source tree if it's a debug build
			if (Debugging.IsDebugBuild) {
				// Common structure is bin/(x64/)Debug/ArchiSteamFarm.exe, so we allow up to 4 directories up
				for (byte i = 0; i < 4; i++) {
					Directory.SetCurrentDirectory("..");

					if (Directory.Exists(SharedInfo.ConfigDirectory)) {
						break;
					}
				}

				// If config directory doesn't exist after our adjustment, abort all of that
				if (!Directory.Exists(SharedInfo.ConfigDirectory)) {
					Directory.SetCurrentDirectory(SharedInfo.HomeDirectory);
				}
			}

			// Parse pre-init args
			if (args != null) {
				ParsePreInitArgs(args);
			}

			bool uniqueInstance = OS.RegisterProcess();
			Logging.InitCoreLoggers(uniqueInstance);

			if (!uniqueInstance) {
				ASF.ArchiLogger.LogGenericError(Strings.ErrorSingleInstanceRequired);
				await Task.Delay(5000).ConfigureAwait(false);

				return false;
			}

			return true;
		}

		private static async Task<bool> InitGlobalConfigAndLanguage() {
			string globalConfigFile = ASF.GetFilePath(ASF.EFileType.Config);

			if (string.IsNullOrEmpty(globalConfigFile)) {
				throw new ArgumentNullException(nameof(globalConfigFile));
			}

			GlobalConfig? globalConfig;

			if (File.Exists(globalConfigFile)) {
				globalConfig = await GlobalConfig.Load(globalConfigFile).ConfigureAwait(false);

				if (globalConfig == null) {
					ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorGlobalConfigNotLoaded, globalConfigFile));
					await Task.Delay(5 * 1000).ConfigureAwait(false);
					await Exit(1).ConfigureAwait(false);

					return false;
				}
			} else {
				globalConfig = new GlobalConfig();
			}

			ASF.InitGlobalConfig(globalConfig);

			if (Debugging.IsDebugConfigured) {
				ASF.ArchiLogger.LogGenericDebug(globalConfigFile + ": " + JsonConvert.SerializeObject(ASF.GlobalConfig, Formatting.Indented));
			}

			if (!string.IsNullOrEmpty(ASF.GlobalConfig?.CurrentCulture)) {
				try {
					// GetCultureInfo() would be better but we can't use it for specifying neutral cultures such as "en"
					CultureInfo culture = CultureInfo.CreateSpecificCulture(ASF.GlobalConfig!.CurrentCulture!);
					CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = culture;
				} catch (Exception e) {
					ASF.ArchiLogger.LogGenericWarningException(e);

					ASF.ArchiLogger.LogGenericError(Strings.ErrorInvalidCurrentCulture);
				}
			}

			// Skip translation progress for English and invariant (such as "C") cultures
			switch (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) {
				case "en":
				case "iv":
					return true;
			}

			// We can't dispose this resource set, as we can't be sure if it isn't used somewhere else, rely on GC in this case
			ResourceSet? defaultResourceSet = Strings.ResourceManager.GetResourceSet(CultureInfo.GetCultureInfo("en-US"), true, true);

			if (defaultResourceSet == null) {
				ASF.ArchiLogger.LogNullError(nameof(defaultResourceSet));

				return true;
			}

			HashSet<DictionaryEntry> defaultStringObjects = defaultResourceSet.Cast<DictionaryEntry>().ToHashSet();

			if (defaultStringObjects.Count == 0) {
				ASF.ArchiLogger.LogNullError(nameof(defaultStringObjects));

				return true;
			}

			// We can't dispose this resource set, as we can't be sure if it isn't used somewhere else, rely on GC in this case
			ResourceSet? currentResourceSet = Strings.ResourceManager.GetResourceSet(CultureInfo.CurrentUICulture, true, true);

			if (currentResourceSet == null) {
				ASF.ArchiLogger.LogNullError(nameof(currentResourceSet));

				return true;
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
				ASF.ArchiLogger.LogGenericInfo(string.Format(Strings.TranslationIncomplete, CultureInfo.CurrentUICulture.Name, translationCompleteness.ToString("P1")));
			}

			return true;
		}

		private static async Task InitGlobalDatabaseAndServices() {
			string globalDatabaseFile = ASF.GetFilePath(ASF.EFileType.Database);

			if (string.IsNullOrEmpty(globalDatabaseFile)) {
				throw new ArgumentNullException(nameof(globalDatabaseFile));
			}

			if (!File.Exists(globalDatabaseFile)) {
				ASF.ArchiLogger.LogGenericInfo(Strings.Welcome);
				await Task.Delay(10 * 1000).ConfigureAwait(false);
				ASF.ArchiLogger.LogGenericWarning(Strings.WarningPrivacyPolicy);
				await Task.Delay(5 * 1000).ConfigureAwait(false);
			}

			GlobalDatabase? globalDatabase = await GlobalDatabase.CreateOrLoad(globalDatabaseFile).ConfigureAwait(false);

			if (globalDatabase == null) {
				ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorDatabaseInvalid, globalDatabaseFile));
				await Task.Delay(5 * 1000).ConfigureAwait(false);
				await Exit(1).ConfigureAwait(false);

				return;
			}

			ASF.InitGlobalDatabase(globalDatabase);

			// If debugging is on, we prepare debug directory prior to running
			if (Debugging.IsUserDebugging) {
				if (Debugging.IsDebugConfigured) {
					ASF.ArchiLogger.LogGenericDebug(globalDatabaseFile + ": " + JsonConvert.SerializeObject(ASF.GlobalDatabase, Formatting.Indented));
				}

				Logging.EnableTraceLogging();

				if (Debugging.IsDebugConfigured) {
					DebugLog.AddListener(new Debugging.DebugListener());
					DebugLog.Enabled = true;

					if (Directory.Exists(SharedInfo.DebugDirectory)) {
						try {
							Directory.Delete(SharedInfo.DebugDirectory, true);
							await Task.Delay(1000).ConfigureAwait(false); // Dirty workaround giving Windows some time to sync
						} catch (Exception e) {
							ASF.ArchiLogger.LogGenericException(e);
						}
					}
				}

				try {
					Directory.CreateDirectory(SharedInfo.DebugDirectory);
				} catch (Exception e) {
					ASF.ArchiLogger.LogGenericException(e);
				}
			}

			WebBrowser.Init();
		}

		private static async Task<bool> InitShutdownSequence() {
			if (ShutdownSequenceInitialized) {
				return false;
			}

			ShutdownSequenceInitialized = true;

			// Sockets created by IPC might still be running for a short while after complete app shutdown
			// Ensure that IPC is stopped before we finalize shutdown sequence
			await ArchiKestrel.Stop().ConfigureAwait(false);

			// Stop all the active bots so they can disconnect cleanly
			if (Bot.Bots?.Count > 0) {
				// Stop() function can block due to SK2 sockets, don't forget a maximum delay
				await Task.WhenAny(Utilities.InParallel(Bot.Bots.Values.Select(bot => Task.Run(() => bot.Stop(true)))), Task.Delay(Bot.Bots.Count * WebBrowser.MaxTries * 1000)).ConfigureAwait(false);

				// Extra second for Steam requests to go through
				await Task.Delay(1000).ConfigureAwait(false);
			}

			// Flush all the pending writes to log files
			LogManager.Flush();

			// Unregister the process from single instancing
			OS.UnregisterProcess();

			return true;
		}

		private static async Task<int> Main(string[]? args) {
			// Initialize
			await Init(args).ConfigureAwait(false);

			// Wait for shutdown event
			return await ShutdownResetEvent.Task.ConfigureAwait(false);
		}

		private static async void OnProcessExit(object? sender, EventArgs e) => await Shutdown().ConfigureAwait(false);

		private static async void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e) {
			if (e == null) {
				throw new ArgumentNullException(nameof(e));
			}

			if (e.ExceptionObject == null) {
				throw new ArgumentNullException(nameof(e.ExceptionObject));
			}

			await ASF.ArchiLogger.LogFatalException((Exception) e.ExceptionObject).ConfigureAwait(false);
			await Exit(1).ConfigureAwait(false);
		}

		private static async void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e) {
			if (e == null) {
				throw new ArgumentNullException(nameof(e));
			}

			if (e.Exception == null) {
				throw new ArgumentNullException(nameof(e.Exception));
			}

			await ASF.ArchiLogger.LogFatalException(e.Exception).ConfigureAwait(false);

			// Normally we should abort the application here, but many tasks are in fact failing in SK2 code which we can't easily fix
			// Thanks Valve.
			e.SetObserved();
		}

		private static void ParsePostInitArgs(IReadOnlyCollection<string> args) {
			if (args == null) {
				throw new ArgumentNullException(nameof(args));
			}

			try {
				string? envCryptKey = Environment.GetEnvironmentVariable(SharedInfo.EnvironmentVariableCryptKey);

				if (!string.IsNullOrEmpty(envCryptKey)) {
					HandleCryptKeyArgument(envCryptKey!);
				}
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);
			}

			bool cryptKeyNext = false;

			foreach (string arg in args) {
				switch (arg) {
					case "--cryptkey" when !cryptKeyNext:
						cryptKeyNext = true;

						break;
					case "--no-restart" when !cryptKeyNext:
						RestartAllowed = false;

						break;
					case "--process-required" when !cryptKeyNext:
						ProcessRequired = true;

						break;
					case "--system-required" when !cryptKeyNext:
						SystemRequired = true;

						break;
					default:
						if (cryptKeyNext) {
							cryptKeyNext = false;
							HandleCryptKeyArgument(arg);
						} else if ((arg.Length > 11) && arg.StartsWith("--cryptkey=", StringComparison.Ordinal)) {
							HandleCryptKeyArgument(arg.Substring(11));
						}

						break;
				}
			}
		}

		private static void ParsePreInitArgs(IReadOnlyCollection<string> args) {
			if (args == null) {
				throw new ArgumentNullException(nameof(args));
			}

			try {
				string? envNetworkGroup = Environment.GetEnvironmentVariable(SharedInfo.EnvironmentVariableNetworkGroup);

				if (!string.IsNullOrEmpty(envNetworkGroup)) {
					HandleNetworkGroupArgument(envNetworkGroup!);
				}

				string? envPath = Environment.GetEnvironmentVariable(SharedInfo.EnvironmentVariablePath);

				if (!string.IsNullOrEmpty(envPath)) {
					HandlePathArgument(envPath!);
				}
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);
			}

			bool networkGroupNext = false;
			bool pathNext = false;

			foreach (string arg in args) {
				switch (arg) {
					case "--network-group" when !networkGroupNext:
						networkGroupNext = true;

						break;
					case "--path" when !pathNext:
						pathNext = true;

						break;
					default:
						if (networkGroupNext) {
							networkGroupNext = false;
							HandleNetworkGroupArgument(arg);
						} else if (pathNext) {
							pathNext = false;
							HandlePathArgument(arg);
						} else if ((arg.Length > 16) && arg.StartsWith("--network-group=", StringComparison.Ordinal)) {
							HandleNetworkGroupArgument(arg.Substring(16));
						} else if ((arg.Length > 7) && arg.StartsWith("--path=", StringComparison.Ordinal)) {
							HandlePathArgument(arg.Substring(7));
						}

						break;
				}
			}
		}

		private static async Task Shutdown(byte exitCode = 0) {
			if (!await InitShutdownSequence().ConfigureAwait(false)) {
				return;
			}

			ShutdownResetEvent.TrySetResult(exitCode);
		}
	}
}
