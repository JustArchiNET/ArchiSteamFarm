//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// 
//  Copyright 2015-2018 Łukasz "JustArchi" Domeradzki
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
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Resources;
using System.Threading.Tasks;
using ArchiSteamFarm.Localization;
using Newtonsoft.Json;
using NLog;
using NLog.Targets;
using SteamKit2;

namespace ArchiSteamFarm {
	internal static class Program {
		internal static byte LoadBalancingDelay {
			get {
				byte result = GlobalConfig?.LoginLimiterDelay ?? GlobalConfig.DefaultLoginLimiterDelay;
				return result >= GlobalConfig.DefaultLoginLimiterDelay ? result : GlobalConfig.DefaultLoginLimiterDelay;
			}
		}

		internal static GlobalConfig GlobalConfig { get; private set; }
		internal static GlobalDatabase GlobalDatabase { get; private set; }
		internal static bool ProcessRequired { get; private set; }
		internal static bool RestartAllowed { get; private set; } = true;
		internal static WebBrowser WebBrowser { get; private set; }

		private static readonly object ConsoleLock = new object();

		// We need to keep this one assigned and not calculated on-demand
		private static readonly string ProcessFileName = Process.GetCurrentProcess().MainModule.FileName;

		private static readonly TaskCompletionSource<byte> ShutdownResetEvent = new TaskCompletionSource<byte>();

		private static bool ShutdownSequenceInitialized;
		private static bool SystemRequired;

		internal static async Task Exit(byte exitCode = 0) {
			if (exitCode != 0) {
				ASF.ArchiLogger.LogGenericError(Strings.ErrorExitingWithNonZeroErrorCode);
			}

			await Shutdown(exitCode).ConfigureAwait(false);
			Environment.Exit(exitCode);
		}

		internal static string GetUserInput(ASF.EUserInputType userInputType, string botName = SharedInfo.ASF) {
			if (userInputType == ASF.EUserInputType.Unknown) {
				return null;
			}

			if (GlobalConfig.Headless) {
				ASF.ArchiLogger.LogGenericWarning(Strings.ErrorUserInputRunningInHeadlessMode);
				return null;
			}

			string result;

			lock (ConsoleLock) {
				Logging.OnUserInputStart();

				try {
					switch (userInputType) {
						case ASF.EUserInputType.DeviceID:
							Console.Write(Bot.FormatBotResponse(Strings.UserInputDeviceID, botName));
							result = Console.ReadLine();
							break;
						case ASF.EUserInputType.Login:
							Console.Write(Bot.FormatBotResponse(Strings.UserInputSteamLogin, botName));
							result = Console.ReadLine();
							break;
						case ASF.EUserInputType.Password:
							Console.Write(Bot.FormatBotResponse(Strings.UserInputSteamPassword, botName));
							result = Utilities.ReadLineMasked();
							break;
						case ASF.EUserInputType.SteamGuard:
							Console.Write(Bot.FormatBotResponse(Strings.UserInputSteamGuard, botName));
							result = Console.ReadLine();
							break;
						case ASF.EUserInputType.SteamParentalPIN:
							Console.Write(Bot.FormatBotResponse(Strings.UserInputSteamParentalPIN, botName));
							result = Utilities.ReadLineMasked();
							break;
						case ASF.EUserInputType.TwoFactorAuthentication:
							Console.Write(Bot.FormatBotResponse(Strings.UserInputSteam2FA, botName));
							result = Console.ReadLine();
							break;
						default:
							ASF.ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(userInputType), userInputType));
							Console.Write(Bot.FormatBotResponse(string.Format(Strings.UserInputUnknown, userInputType), botName));
							result = Console.ReadLine();
							break;
					}

					if (!Console.IsOutputRedirected) {
						Console.Clear(); // For security purposes
					}
				} catch (Exception e) {
					Logging.OnUserInputEnd();
					ASF.ArchiLogger.LogGenericException(e);
					return null;
				}

				Logging.OnUserInputEnd();
			}

			return !string.IsNullOrEmpty(result) ? result.Trim() : null;
		}

		internal static async Task Restart() {
			if (!await InitShutdownSequence().ConfigureAwait(false)) {
				return;
			}

			string executableName = Path.GetFileNameWithoutExtension(ProcessFileName);
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

			CryptoHelper.SetEncryptionKey(cryptKey);
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
			ASF.ArchiLogger.LogGenericInfo(SharedInfo.PublicIdentifier + " V" + SharedInfo.Version + " (" + SharedInfo.BuildInfo.Variant + "/" + SharedInfo.ModuleVersion + ")");

			await InitGlobalConfigAndLanguage().ConfigureAwait(false);

			// Parse post-init args
			if (args != null) {
				ParsePostInitArgs(args);
			}

			OS.Init(SystemRequired);

			await InitGlobalDatabaseAndServices().ConfigureAwait(false);

			await ASF.CheckAndUpdateProgram().ConfigureAwait(false);
			await ASF.InitBots().ConfigureAwait(false);

			ASF.InitEvents();
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
			string globalConfigFile = Path.Combine(SharedInfo.ConfigDirectory, SharedInfo.GlobalConfigFileName);

			GlobalConfig = await GlobalConfig.Load(globalConfigFile).ConfigureAwait(false);
			if (GlobalConfig == null) {
				ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorGlobalConfigNotLoaded, globalConfigFile));
				await Task.Delay(5 * 1000).ConfigureAwait(false);
				await Exit(1).ConfigureAwait(false);
				return;
			}

			if (Debugging.IsUserDebugging) {
				ASF.ArchiLogger.LogGenericDebug(SharedInfo.GlobalConfigFileName + ": " + JsonConvert.SerializeObject(GlobalConfig, Formatting.Indented));
			}

			if (GlobalConfig.BackgroundGCPeriod > 0) {
				ASF.ArchiLogger.LogGenericWarning(string.Format(Strings.WarningDeprecated, nameof(GlobalConfig.BackgroundGCPeriod), "COMPlus_GCLatencyLevel=0"));
				Hacks.EnableBackgroundGC(GlobalConfig.BackgroundGCPeriod);
			}

			if (!string.IsNullOrEmpty(GlobalConfig.CurrentCulture)) {
				try {
					// GetCultureInfo() would be better but we can't use it for specifying neutral cultures such as "en"
					CultureInfo culture = CultureInfo.CreateSpecificCulture(GlobalConfig.CurrentCulture);
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
			string globalDatabaseFile = Path.Combine(SharedInfo.ConfigDirectory, SharedInfo.GlobalDatabaseFileName);

			if (!File.Exists(globalDatabaseFile)) {
				ASF.ArchiLogger.LogGenericInfo(Strings.Welcome);
				await Task.Delay(10 * 1000).ConfigureAwait(false);
				ASF.ArchiLogger.LogGenericWarning(Strings.WarningPrivacyPolicy);
				await Task.Delay(5 * 1000).ConfigureAwait(false);
			}

			GlobalDatabase = await GlobalDatabase.Load(globalDatabaseFile).ConfigureAwait(false);
			if (GlobalDatabase == null) {
				ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorDatabaseInvalid, globalDatabaseFile));
				await Task.Delay(5 * 1000).ConfigureAwait(false);
				await Exit(1).ConfigureAwait(false);
				return;
			}

			if (Debugging.IsUserDebugging) {
				ASF.ArchiLogger.LogGenericDebug(SharedInfo.GlobalDatabaseFileName + ": " + JsonConvert.SerializeObject(GlobalDatabase, Formatting.Indented));
			}

			// If debugging is on, we prepare debug directory prior to running
			if (GlobalConfig.Debug) {
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
			WebBrowser = new WebBrowser(ASF.ArchiLogger, GlobalConfig.WebProxy, true);

			if (GlobalConfig.IPC && (GlobalConfig.IPCPrefixes.Count > 0)) {
				IPC.Start(GlobalConfig.IPCPrefixes);
			}
		}

		private static async Task<bool> InitShutdownSequence() {
			if (ShutdownSequenceInitialized) {
				return false;
			}

			ShutdownSequenceInitialized = true;

			// Sockets created by HttpListener might still be running for a short while after complete app shutdown
			// Ensure that IPC is stopped before we finalize shutdown sequence
			if (IPC.IsRunning) {
				IPC.Stop();

				for (byte i = 0; (i < WebBrowser.MaxTries) && IPC.IsRunning; i++) {
					await Task.Delay(1000).ConfigureAwait(false);
				}
			}

			if (Bot.Bots.Count > 0) {
				IEnumerable<Task> tasks = Bot.Bots.Values.Select(bot => Task.Run(() => bot.Stop(true)));

				switch (GlobalConfig.OptimizationMode) {
					case GlobalConfig.EOptimizationMode.MinMemoryUsage:
						foreach (Task task in tasks) {
							await Task.WhenAny(task, Task.Delay(WebBrowser.MaxTries * 1000)).ConfigureAwait(false);
						}

						break;
					default:
						await Task.WhenAny(Task.WhenAll(tasks), Task.Delay(Bot.Bots.Count * WebBrowser.MaxTries * 1000)).ConfigureAwait(false);
						break;
				}

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