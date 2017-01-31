/*
    _                _      _  ____   _                           _____
   / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
  / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
 / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
/_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|

 Copyright 2015-2017 Łukasz "JustArchi" Domeradzki
 Contact: JustArchi@JustArchi.net

 Licensed under the Apache License, Version 2.0 (the "License");
 you may not use this file except in compliance with the License.
 You may obtain a copy of the License at

 http://www.apache.org/licenses/LICENSE-2.0
					
 Unless required by applicable law or agreed to in writing, software
 distributed under the License is distributed on an "AS IS" BASIS,
 WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 See the License for the specific language governing permissions and
 limitations under the License.

*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Localization;
using NLog.Targets;
using SteamKit2;

namespace ArchiSteamFarm {
	internal static class Program {
		internal static bool IsWCFRunning => WCF.IsServerRunning;
		internal static GlobalConfig GlobalConfig { get; private set; }
		internal static GlobalDatabase GlobalDatabase { get; private set; }
		internal static bool IsRunningAsService { get; private set; }
		internal static EMode Mode { get; private set; } = EMode.Normal;
		internal static WebBrowser WebBrowser { get; private set; }

		private static readonly object ConsoleLock = new object();
		private static readonly ManualResetEventSlim ShutdownResetEvent = new ManualResetEventSlim(false);
		private static readonly WCF WCF = new WCF();

		private static bool ShutdownSequenceInitialized;

		internal static void Exit(byte exitCode = 0) {
			if (exitCode != 0) {
				ASF.ArchiLogger.LogGenericError(Strings.ErrorExitingWithNonZeroErrorCode);
			}

			Shutdown();
			Environment.Exit(exitCode);
		}

		internal static string GetUserInput(ASF.EUserInputType userInputType, string botName = SharedInfo.ASF) {
			if (userInputType == ASF.EUserInputType.Unknown) {
				return null;
			}

			if (GlobalConfig.Headless || !Runtime.IsUserInteractive) {
				ASF.ArchiLogger.LogGenericWarning(Strings.ErrorUserInputRunningInHeadlessMode);
				return null;
			}

			string result;
			lock (ConsoleLock) {
				Logging.OnUserInputStart();
				switch (userInputType) {
					case ASF.EUserInputType.DeviceID:
						Console.Write(Strings.UserInputDeviceID, botName);
						break;
					case ASF.EUserInputType.Login:
						Console.Write(Strings.UserInputSteamLogin, botName);
						break;
					case ASF.EUserInputType.Password:
						Console.Write(Strings.UserInputSteamPassword, botName);
						break;
					case ASF.EUserInputType.SteamGuard:
						Console.Write(Strings.UserInputSteamGuard, botName);
						break;
					case ASF.EUserInputType.SteamParentalPIN:
						Console.Write(Strings.UserInputSteamParentalPIN, botName);
						break;
					case ASF.EUserInputType.TwoFactorAuthentication:
						Console.Write(Strings.UserInputSteam2FA, botName);
						break;
					case ASF.EUserInputType.WCFHostname:
						Console.Write(Strings.UserInputWCFHost, botName);
						break;
					default:
						ASF.ArchiLogger.LogGenericWarning(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(userInputType), userInputType));
						Console.Write(Strings.UserInputUnknown, botName, userInputType);
						break;
				}

				result = Console.ReadLine();

				if (!Console.IsOutputRedirected) {
					Console.Clear(); // For security purposes
				}

				Logging.OnUserInputEnd();
			}

			return !string.IsNullOrEmpty(result) ? result.Trim() : null;
		}

		internal static void Restart() {
			if (!InitShutdownSequence()) {
				return;
			}

			try {
				Process.Start(Assembly.GetEntryAssembly().Location, string.Join(" ", Environment.GetCommandLineArgs().Skip(1)));
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);
			}

			ShutdownResetEvent.Set();
			Environment.Exit(0);
		}

		private static async Task Init(string[] args) {
			// We must register our logging target as soon as possible
			Target.Register<SteamTarget>("Steam");
			await InitCore(args).ConfigureAwait(false);
		}

		private static async Task InitCore(string[] args) {
			AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionHandler;
			TaskScheduler.UnobservedTaskException += UnobservedTaskExceptionHandler;

			string homeDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
			if (!string.IsNullOrEmpty(homeDirectory)) {
				Directory.SetCurrentDirectory(homeDirectory);

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
						Directory.SetCurrentDirectory(homeDirectory);
					}
				}
			}

			// Parse pre-init args
			if (args != null) {
				ParsePreInitArgs(args);
			}

			Logging.InitLoggers();
			ASF.ArchiLogger.LogGenericInfo("ASF V" + SharedInfo.Version);

			await InitServices().ConfigureAwait(false);

			if (!Runtime.IsRuntimeSupported) {
				ASF.ArchiLogger.LogGenericError(Strings.WarningRuntimeUnsupported);
				await Task.Delay(10 * 1000).ConfigureAwait(false);
			}

			// If debugging is on, we prepare debug directory prior to running
			if (GlobalConfig.Debug) {
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

			// Parse post-init args
			if (args != null) {
				await ParsePostInitArgs(args).ConfigureAwait(false);
			}

			// If we ran ASF as a client, we're done by now
			if (Mode.HasFlag(EMode.Client) && !Mode.HasFlag(EMode.Server)) {
				Exit();
			}

			await ASF.CheckForUpdate().ConfigureAwait(false);
			await ASF.InitBots().ConfigureAwait(false);
			ASF.InitFileWatcher();
		}

		private static async Task InitServices() {
			string globalConfigFile = Path.Combine(SharedInfo.ConfigDirectory, SharedInfo.GlobalConfigFileName);

			GlobalConfig = GlobalConfig.Load(globalConfigFile);
			if (GlobalConfig == null) {
				ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorGlobalConfigNotLoaded, globalConfigFile));
				await Task.Delay(5 * 1000).ConfigureAwait(false);
				Exit(1);
				return;
			}

			if (!string.IsNullOrEmpty(GlobalConfig.CurrentCulture)) {
				try {
					// GetCultureInfo() would be better but we can't use it for specifying neutral cultures such as "en"
					CultureInfo culture = CultureInfo.CreateSpecificCulture(GlobalConfig.CurrentCulture);
					CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = culture;
				} catch (CultureNotFoundException) {
					ASF.ArchiLogger.LogGenericError(Strings.ErrorInvalidCurrentCulture);
				}
			}

			int defaultResourceSetCount = 0;
			int currentResourceSetCount = 0;

			ResourceSet defaultResourceSet = Strings.ResourceManager.GetResourceSet(CultureInfo.CreateSpecificCulture("en-US"), true, true);
			if (defaultResourceSet != null) {
				defaultResourceSetCount = defaultResourceSet.Cast<object>().Count();
			}

			ResourceSet currentResourceSet = Strings.ResourceManager.GetResourceSet(CultureInfo.CurrentCulture, true, false);
			if (currentResourceSet != null) {
				currentResourceSetCount = currentResourceSet.Cast<object>().Count();
			}

			if (currentResourceSetCount < defaultResourceSetCount) {
				float translationCompleteness = currentResourceSetCount / (float) defaultResourceSetCount;
				ASF.ArchiLogger.LogGenericInfo(string.Format(Strings.TranslationIncomplete, CultureInfo.CurrentCulture.Name, translationCompleteness.ToString("P1")));
			}

			string globalDatabaseFile = Path.Combine(SharedInfo.ConfigDirectory, SharedInfo.GlobalDatabaseFileName);

			if (!File.Exists(globalDatabaseFile)) {
				ASF.ArchiLogger.LogGenericInfo(Strings.Welcome);
				ASF.ArchiLogger.LogGenericWarning(Strings.WarningPrivacyPolicy);
				await Task.Delay(15 * 1000).ConfigureAwait(false);
			}

			GlobalDatabase = GlobalDatabase.Load(globalDatabaseFile);
			if (GlobalDatabase == null) {
				ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorDatabaseInvalid, globalDatabaseFile));
				await Task.Delay(5 * 1000).ConfigureAwait(false);
				Exit(1);
				return;
			}

			ArchiWebHandler.Init();
			WebBrowser.Init();
			WCF.Init();

			WebBrowser = new WebBrowser(ASF.ArchiLogger);
		}

		private static bool InitShutdownSequence() {
			if (ShutdownSequenceInitialized) {
				return false;
			}

			ShutdownSequenceInitialized = true;

			WCF.StopServer();
			foreach (Bot bot in Bot.Bots.Values) {
				bot.Stop();
			}

			return true;
		}

		private static void Main(string[] args) {
			if (Runtime.IsUserInteractive) {
				// App
				Init(args).Wait();

				// Wait for signal to shutdown
				ShutdownResetEvent.Wait();

				// We got a signal to shutdown
				Exit();
			} else {
				// Service
				IsRunningAsService = true;
				using (Service service = new Service()) {
					ServiceBase.Run(service);
				}
			}
		}

		private static async Task ParsePostInitArgs(IEnumerable<string> args) {
			if (args == null) {
				ASF.ArchiLogger.LogNullError(nameof(args));
				return;
			}

			foreach (string arg in args) {
				switch (arg) {
					case "":
						break;
					case "--client":
						Mode |= EMode.Client;
						break;
					case "--server":
						Mode |= EMode.Server;
						WCF.StartServer();
						await ASF.InitBots().ConfigureAwait(false);
						break;
					default:
						if (arg.StartsWith("--", StringComparison.Ordinal)) {
							if (arg.StartsWith("--cryptkey=", StringComparison.Ordinal) && (arg.Length > 11)) {
								CryptoHelper.SetEncryptionKey(arg.Substring(11));
							}

							break;
						}

						if (!Mode.HasFlag(EMode.Client)) {
							ASF.ArchiLogger.LogGenericWarning(string.Format(Strings.WarningWCFIgnoringCommand, arg));
							break;
						}

						string response = WCF.SendCommand(arg);

						ASF.ArchiLogger.LogGenericInfo(string.Format(Strings.WCFResponseReceived, response));
						break;
				}
			}
		}

		private static void ParsePreInitArgs(IEnumerable<string> args) {
			if (args == null) {
				ASF.ArchiLogger.LogNullError(nameof(args));
				return;
			}

			foreach (string arg in args) {
				switch (arg) {
					case "":
						break;
					case "--client":
						Mode |= EMode.Client;
						break;
					case "--server":
						Mode |= EMode.Server;
						break;
					default:
						if (arg.StartsWith("--", StringComparison.Ordinal)) {
							if (arg.StartsWith("--path=", StringComparison.Ordinal) && (arg.Length > 7)) {
								Directory.SetCurrentDirectory(arg.Substring(7));
							}
						}

						break;
				}
			}
		}

		private static void Shutdown() {
			if (!InitShutdownSequence()) {
				return;
			}

			ShutdownResetEvent.Set();
		}

		private static void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs args) {
			if (args?.ExceptionObject == null) {
				ASF.ArchiLogger.LogNullError(nameof(args) + " || " + nameof(args.ExceptionObject));
				return;
			}

			ASF.ArchiLogger.LogFatalException((Exception) args.ExceptionObject);
			Exit(1);
		}

		private static void UnobservedTaskExceptionHandler(object sender, UnobservedTaskExceptionEventArgs args) {
			if (args?.Exception == null) {
				ASF.ArchiLogger.LogNullError(nameof(args) + " || " + nameof(args.Exception));
				return;
			}

			ASF.ArchiLogger.LogFatalException(args.Exception);
			Exit(1);
		}

		[Flags]
		internal enum EMode : byte {
			Normal = 0, // Standard most common usage
			Client = 1, // WCF client
			Server = 2 // WCF server
		}

		private sealed class Service : ServiceBase {
			internal Service() {
				ServiceName = SharedInfo.ServiceName;
			}

			protected override void OnStart(string[] args) => Task.Run(async () => {
				// Normally it'd make sense to use already provided string[] args parameter above
				// However, that one doesn't seem to work when ASF is started as a service, it's always null
				// Therefore, we will use Environment args in such case
				string[] envArgs = Environment.GetCommandLineArgs();
				await Init(envArgs).ConfigureAwait(false);

				ShutdownResetEvent.Wait();
				Stop();
			});

			protected override void OnStop() => Shutdown();
		}
	}
}