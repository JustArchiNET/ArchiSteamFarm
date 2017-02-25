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

		internal static async Task Exit(byte exitCode = 0) {
			if (exitCode != 0) {
				ASF.ArchiLogger.LogGenericError(Strings.ErrorExitingWithNonZeroErrorCode);
			}

			await Shutdown().ConfigureAwait(false);
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
						Console.Write(Bot.FormatBotResponse(Strings.UserInputDeviceID, botName));
						break;
					case ASF.EUserInputType.Login:
						Console.Write(Bot.FormatBotResponse(Strings.UserInputSteamLogin, botName));
						break;
					case ASF.EUserInputType.Password:
						Console.Write(Bot.FormatBotResponse(Strings.UserInputSteamPassword, botName));
						break;
					case ASF.EUserInputType.SteamGuard:
						Console.Write(Bot.FormatBotResponse(Strings.UserInputSteamGuard, botName));
						break;
					case ASF.EUserInputType.SteamParentalPIN:
						Console.Write(Bot.FormatBotResponse(Strings.UserInputSteamParentalPIN, botName));
						break;
					case ASF.EUserInputType.TwoFactorAuthentication:
						Console.Write(Bot.FormatBotResponse(Strings.UserInputSteam2FA, botName));
						break;
					case ASF.EUserInputType.WCFHostname:
						Console.Write(Bot.FormatBotResponse(Strings.UserInputWCFHost, botName));
						break;
					default:
						ASF.ArchiLogger.LogGenericWarning(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(userInputType), userInputType));
						Console.Write(Bot.FormatBotResponse(string.Format(Strings.UserInputUnknown, userInputType), botName));
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

		internal static async Task Restart() {
			if (!await InitShutdownSequence().ConfigureAwait(false)) {
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
			AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionHandler;
			TaskScheduler.UnobservedTaskException += UnobservedTaskExceptionHandler;

			// We must register our logging target as soon as possible
			Target.Register<SteamTarget>("Steam");

			InitCore(args);
			await InitASF(args).ConfigureAwait(false);
		}

		private static async Task InitASF(string[] args) {
			ASF.ArchiLogger.LogGenericInfo("ASF V" + SharedInfo.Version);

			await InitGlobalConfigAndLanguage().ConfigureAwait(false);

			if (!Runtime.IsRuntimeSupported) {
				ASF.ArchiLogger.LogGenericError(Strings.WarningRuntimeUnsupported);
				await Task.Delay(60 * 1000).ConfigureAwait(false);
			}

			await InitGlobalDatabaseAndServices().ConfigureAwait(false);

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
				await Exit().ConfigureAwait(false);
			}

			await ASF.CheckForUpdate().ConfigureAwait(false);
			await ASF.InitBots().ConfigureAwait(false);
			ASF.InitEvents();
		}

		private static void InitCore(string[] args) {
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
		}

		private static async Task InitGlobalConfigAndLanguage() {
			string globalConfigFile = Path.Combine(SharedInfo.ConfigDirectory, SharedInfo.GlobalConfigFileName);

			GlobalConfig = GlobalConfig.Load(globalConfigFile);
			if (GlobalConfig == null) {
				ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorGlobalConfigNotLoaded, globalConfigFile));
				await Task.Delay(5 * 1000).ConfigureAwait(false);
				await Exit(1).ConfigureAwait(false);
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

			ushort defaultResourceSetCount = 0;
			ResourceSet defaultResourceSet = Strings.ResourceManager.GetResourceSet(CultureInfo.GetCultureInfo("en-US"), true, true);
			if (defaultResourceSet != null) {
				defaultResourceSetCount = (ushort) defaultResourceSet.Cast<object>().Count();
			}

			if (defaultResourceSetCount == 0) {
				return;
			}

			ushort currentResourceSetCount = 0;
			ResourceSet currentResourceSet = Strings.ResourceManager.GetResourceSet(CultureInfo.CurrentCulture, true, false);
			if (currentResourceSet != null) {
				currentResourceSetCount = (ushort) currentResourceSet.Cast<object>().Count();
			}

			if (currentResourceSetCount < defaultResourceSetCount) {
				// We don't want to report "en-AU" as 0.00% only because we don't have it as a dialect, if "en" is available and translated
				// This typically will work only for English, as e.g. "nl-BE" doesn't fallback to "nl-NL", but "nl", and "nl" will be empty
				ushort neutralResourceSetCount = 0;
				ResourceSet neutralResourceSet = Strings.ResourceManager.GetResourceSet(CultureInfo.CurrentCulture.Parent, true, false);
				if (neutralResourceSet != null) {
					neutralResourceSetCount = (ushort) neutralResourceSet.Cast<object>().Count();
				}

				if (neutralResourceSetCount < defaultResourceSetCount) {
					float translationCompleteness = currentResourceSetCount / (float) defaultResourceSetCount;
					ASF.ArchiLogger.LogGenericInfo(string.Format(Strings.TranslationIncomplete, CultureInfo.CurrentCulture.Name, translationCompleteness.ToString("P1")));
				}
			}
		}

		private static async Task InitGlobalDatabaseAndServices() {
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
				await Exit(1).ConfigureAwait(false);
				return;
			}

			ArchiWebHandler.Init();
			OS.Init();
			WCF.Init();
			WebBrowser.Init();

			WebBrowser = new WebBrowser(ASF.ArchiLogger);
		}

		private static async Task<bool> InitShutdownSequence() {
			if (ShutdownSequenceInitialized) {
				return false;
			}

			ShutdownSequenceInitialized = true;

			WCF.StopServer();

			IEnumerable<Task> tasks = Bot.Bots.Values.Select(bot => Task.Run(() => bot.Stop()));
			await Task.WhenAny(Task.WhenAll(tasks), Task.Delay(10 * 1000));

			return true;
		}

		private static void Main(string[] args) {
			if (Runtime.IsUserInteractive) {
				// App
				Init(args).Wait();

				// Wait for signal to shutdown
				ShutdownResetEvent.Wait();

				// We got a signal to shutdown
				Exit().Wait();
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

		private static async Task Shutdown() {
			if (!await InitShutdownSequence().ConfigureAwait(false)) {
				return;
			}

			ShutdownResetEvent.Set();
		}

		private static async void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs args) {
			if (args?.ExceptionObject == null) {
				ASF.ArchiLogger.LogNullError(nameof(args) + " || " + nameof(args.ExceptionObject));
				return;
			}

			ASF.ArchiLogger.LogFatalException((Exception) args.ExceptionObject);
			await Task.Delay(5000).ConfigureAwait(false);
			await Exit(1).ConfigureAwait(false);
		}

		private static void UnobservedTaskExceptionHandler(object sender, UnobservedTaskExceptionEventArgs args) {
			if (args?.Exception == null) {
				ASF.ArchiLogger.LogNullError(nameof(args) + " || " + nameof(args.Exception));
				return;
			}

			ASF.ArchiLogger.LogFatalException(args.Exception);
			// Normally we should abort the application here, but many tasks are in fact failing in SK2 code which we can't easily fix
			// Thanks Valve.
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

			protected override async void OnStop() => await Shutdown().ConfigureAwait(false);
		}
	}
}