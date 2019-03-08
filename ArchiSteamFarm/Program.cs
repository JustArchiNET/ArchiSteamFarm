//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2019 Łukasz "JustArchi" Domeradzki
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
		internal static bool ProcessRequired { get; private set; }
		internal static bool RestartAllowed { get; private set; } = true;
		internal static bool ShutdownSequenceInitialized { get; private set; }

		// We need to keep this one assigned and not calculated on-demand
		private static readonly string ProcessFileName = Process.GetCurrentProcess().MainModule.FileName;

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

			string executableName = Path.GetFileNameWithoutExtension(ProcessFileName);

			if (string.IsNullOrEmpty(executableName)) {
				ASF.ArchiLogger.LogNullError(nameof(executableName));

				return;
			}

			IEnumerable<string> arguments = Environment.GetCommandLineArgs().Skip(executableName.Equals(SharedInfo.AssemblyName) ? 1 : 0);

			try {
				Process.Start(ProcessFileName, string.Join(" ", arguments));
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
				ASF.ArchiLogger.LogNullError(nameof(cryptKey));

				return;
			}

			ArchiCryptoHelper.SetEncryptionKey(cryptKey);
		}

		private static void HandlePathArgument(string path) {
			if (string.IsNullOrEmpty(path)) {
				ASF.ArchiLogger.LogNullError(nameof(path));

				return;
			}

			try {
				Directory.SetCurrentDirectory(path);
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);
			}
		}

		private static async Task Init(IReadOnlyCollection<string> args) {
			AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
			AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
			TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

			// We must register our logging targets as soon as possible
			Target.Register<HistoryTarget>(HistoryTarget.TargetName);
			Target.Register<SteamTarget>(SteamTarget.TargetName);

			InitCore(args);
			await InitASF(args).ConfigureAwait(false);
		}

		private static async Task InitASF(IReadOnlyCollection<string> args) {
			string programIdentifier = SharedInfo.PublicIdentifier + " V" + SharedInfo.Version + " (" + SharedInfo.BuildInfo.Variant + "/" + SharedInfo.ModuleVersion + " | " + OS.Variant + ")";

			Console.Title = programIdentifier;
			ASF.ArchiLogger.LogGenericInfo(programIdentifier);

			await InitGlobalConfigAndLanguage().ConfigureAwait(false);

			// Parse post-init args
			if (args != null) {
				ParsePostInitArgs(args);
			}

			OS.Init(SystemRequired, ASF.GlobalConfig.OptimizationMode);

			await InitGlobalDatabaseAndServices().ConfigureAwait(false);
			await ASF.Init().ConfigureAwait(false);
		}

		private static void InitCore(IReadOnlyCollection<string> args) {
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

			Logging.InitCoreLoggers();
		}

		private static async Task InitGlobalConfigAndLanguage() {
			string globalConfigFile = ASF.GetFilePath(ASF.EFileType.Config);

			if (string.IsNullOrEmpty(globalConfigFile)) {
				ASF.ArchiLogger.LogNullError(nameof(globalConfigFile));

				return;
			}

			GlobalConfig globalConfig;

			if (File.Exists(globalConfigFile)) {
				globalConfig = await GlobalConfig.Load(globalConfigFile).ConfigureAwait(false);

				if (globalConfig == null) {
					ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorGlobalConfigNotLoaded, globalConfigFile));
					await Task.Delay(5 * 1000).ConfigureAwait(false);
					await Exit(1).ConfigureAwait(false);

					return;
				}
			} else {
				globalConfig = GlobalConfig.Create();
			}

			ASF.InitGlobalConfig(globalConfig);

			if (Debugging.IsUserDebugging) {
				ASF.ArchiLogger.LogGenericDebug(globalConfigFile + ": " + JsonConvert.SerializeObject(ASF.GlobalConfig, Formatting.Indented));
			}

			if (!string.IsNullOrEmpty(ASF.GlobalConfig.CurrentCulture)) {
				try {
					// GetCultureInfo() would be better but we can't use it for specifying neutral cultures such as "en"
					CultureInfo culture = CultureInfo.CreateSpecificCulture(ASF.GlobalConfig.CurrentCulture);
					CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = culture;
				} catch (Exception) {
					ASF.ArchiLogger.LogGenericError(Strings.ErrorInvalidCurrentCulture);
				}
			}

			if (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("en")) {
				return;
			}

			ResourceSet defaultResourceSet = Strings.ResourceManager.GetResourceSet(CultureInfo.GetCultureInfo("en-US"), true, true);

			if (defaultResourceSet == null) {
				ASF.ArchiLogger.LogNullError(nameof(defaultResourceSet));

				return;
			}

			HashSet<DictionaryEntry> defaultStringObjects = defaultResourceSet.Cast<DictionaryEntry>().ToHashSet();

			if (defaultStringObjects.Count == 0) {
				ASF.ArchiLogger.LogNullError(nameof(defaultStringObjects));

				return;
			}

			ResourceSet currentResourceSet = Strings.ResourceManager.GetResourceSet(CultureInfo.CurrentUICulture, true, true);

			if (currentResourceSet == null) {
				ASF.ArchiLogger.LogNullError(nameof(currentResourceSet));

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
				ASF.ArchiLogger.LogGenericInfo(string.Format(Strings.TranslationIncomplete, CultureInfo.CurrentUICulture.Name, translationCompleteness.ToString("P1")));
			}
		}

		private static async Task InitGlobalDatabaseAndServices() {
			string globalDatabaseFile = ASF.GetFilePath(ASF.EFileType.Database);

			if (string.IsNullOrEmpty(globalDatabaseFile)) {
				ASF.ArchiLogger.LogNullError(nameof(globalDatabaseFile));

				return;
			}

			if (!File.Exists(globalDatabaseFile)) {
				ASF.ArchiLogger.LogGenericInfo(Strings.Welcome);
				await Task.Delay(10 * 1000).ConfigureAwait(false);
				ASF.ArchiLogger.LogGenericWarning(Strings.WarningPrivacyPolicy);
				await Task.Delay(5 * 1000).ConfigureAwait(false);
			}

			GlobalDatabase globalDatabase = await GlobalDatabase.CreateOrLoad(globalDatabaseFile).ConfigureAwait(false);

			if (globalDatabase == null) {
				ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorDatabaseInvalid, globalDatabaseFile));
				await Task.Delay(5 * 1000).ConfigureAwait(false);
				await Exit(1).ConfigureAwait(false);

				return;
			}

			ASF.InitGlobalDatabase(globalDatabase);

			// If debugging is on, we prepare debug directory prior to running
			if (Debugging.IsUserDebugging) {
				ASF.ArchiLogger.LogGenericDebug(globalDatabaseFile + ": " + JsonConvert.SerializeObject(ASF.GlobalDatabase, Formatting.Indented));
				Logging.EnableTraceLogging();

				if (Directory.Exists(SharedInfo.DebugDirectory)) {
					try {
						Directory.Delete(SharedInfo.DebugDirectory, true);
						await Task.Delay(1000).ConfigureAwait(false); // Dirty workaround giving Windows some time to sync
					} catch (IOException e) {
						ASF.ArchiLogger.LogGenericException(e);
					}
				}

				Directory.CreateDirectory(SharedInfo.DebugDirectory);

				DebugLog.AddListener(new Debugging.DebugListener());
				DebugLog.Enabled = true;
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

			if (Bot.Bots?.Count > 0) {
				// Stop() function can block due to SK2 sockets, don't forget a maximum delay
				await Task.WhenAny(Utilities.InParallel(Bot.Bots.Values.Select(bot => Task.Run(() => bot.Stop(true)))), Task.Delay(Bot.Bots.Count * WebBrowser.MaxTries * 1000)).ConfigureAwait(false);

				// Extra second for Steam requests to go through
				await Task.Delay(1000).ConfigureAwait(false);
			}

			LogManager.Flush();

			return true;
		}

		private static async Task<int> Main(string[] args) {
			// Initialize
			await Init(args).ConfigureAwait(false);

			// Wait for shutdown event
			return await ShutdownResetEvent.Task.ConfigureAwait(false);
		}

		private static async void OnProcessExit(object sender, EventArgs e) => await Shutdown().ConfigureAwait(false);

		private static async void OnUnhandledException(object sender, UnhandledExceptionEventArgs e) {
			if (e?.ExceptionObject == null) {
				ASF.ArchiLogger.LogNullError(nameof(e) + " || " + nameof(e.ExceptionObject));

				return;
			}

			await ASF.ArchiLogger.LogFatalException((Exception) e.ExceptionObject).ConfigureAwait(false);
			await Exit(1).ConfigureAwait(false);
		}

		private static async void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e) {
			if (e?.Exception == null) {
				ASF.ArchiLogger.LogNullError(nameof(e) + " || " + nameof(e.Exception));

				return;
			}

			await ASF.ArchiLogger.LogFatalException(e.Exception).ConfigureAwait(false);

			// Normally we should abort the application here, but many tasks are in fact failing in SK2 code which we can't easily fix
			// Thanks Valve.
			e.SetObserved();
		}

		private static void ParsePostInitArgs(IReadOnlyCollection<string> args) {
			if (args == null) {
				ASF.ArchiLogger.LogNullError(nameof(args));

				return;
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
				ASF.ArchiLogger.LogNullError(nameof(args));

				return;
			}

			bool pathNext = false;

			foreach (string arg in args) {
				switch (arg) {
					case "--path" when !pathNext:
						pathNext = true;

						break;
					default:

						if (pathNext) {
							pathNext = false;
							HandlePathArgument(arg);
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
