/*
    _                _      _  ____   _                           _____
   / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
  / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
 / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
/_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|

 Copyright 2015-2016 Łukasz "JustArchi" Domeradzki
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
using System.IO;
using System.Linq;
using System.Reflection;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;

namespace ArchiSteamFarm {
	internal static class Program {
		[Flags]
		internal enum EMode : byte {
			Normal = 0, // Standard most common usage
			Client = 1, // WCF client
			Server = 2 // WCF server
		}

		private static readonly object ConsoleLock = new object();
		private static readonly ManualResetEventSlim ShutdownResetEvent = new ManualResetEventSlim(false);
		private static readonly WCF WCF = new WCF();

		internal static bool IsRunningAsService { get; private set; }
		internal static EMode Mode { get; private set; } = EMode.Normal;
		internal static GlobalConfig GlobalConfig { get; private set; }
		internal static GlobalDatabase GlobalDatabase { get; private set; }
		internal static WebBrowser WebBrowser { get; private set; }

		private static bool ShutdownSequenceInitialized;

		internal static bool IsWCFRunning => WCF.IsServerRunning;

		internal static void Exit(byte exitCode = 0) {
			Shutdown();
			Environment.Exit(exitCode);
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

		internal static string GetUserInput(ASF.EUserInputType userInputType, string botName = SharedInfo.ASF, string extraInformation = null) {
			if (userInputType == ASF.EUserInputType.Unknown) {
				return null;
			}

			if (GlobalConfig.Headless || !Runtime.IsUserInteractive) {
				ASF.ArchiLogger.LogGenericWarning("Received a request for user input, but process is running in headless mode!");
				return null;
			}

			string result;
			lock (ConsoleLock) {
				Logging.OnUserInputStart();
				switch (userInputType) {
					case ASF.EUserInputType.DeviceID:
						Console.Write("<" + botName + "> Please enter your Device ID (including \"android:\"): ");
						break;
					case ASF.EUserInputType.Login:
						Console.Write("<" + botName + "> Please enter your login: ");
						break;
					case ASF.EUserInputType.Password:
						Console.Write("<" + botName + "> Please enter your password: ");
						break;
					case ASF.EUserInputType.PhoneNumber:
						Console.Write("<" + botName + "> Please enter your full phone number (e.g. +1234567890): ");
						break;
					case ASF.EUserInputType.SMS:
						Console.Write("<" + botName + "> Please enter SMS code sent on your mobile: ");
						break;
					case ASF.EUserInputType.SteamGuard:
						Console.Write("<" + botName + "> Please enter the auth code sent to your email: ");
						break;
					case ASF.EUserInputType.SteamParentalPIN:
						Console.Write("<" + botName + "> Please enter steam parental PIN: ");
						break;
					case ASF.EUserInputType.RevocationCode:
						Console.WriteLine("<" + botName + "> PLEASE WRITE DOWN YOUR REVOCATION CODE: " + extraInformation);
						Console.Write("<" + botName + "> Hit enter once ready...");
						break;
					case ASF.EUserInputType.TwoFactorAuthentication:
						Console.Write("<" + botName + "> Please enter your 2 factor auth code from your authenticator app: ");
						break;
					case ASF.EUserInputType.WCFHostname:
						Console.Write("<" + botName + "> Please enter your WCF hostname: ");
						break;
					default:
						Console.Write("<" + botName + "> Please enter not documented yet value of \"" + userInputType + "\": ");
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

		internal static void Shutdown() {
			if (!InitShutdownSequence()) {
				return;
			}

			ShutdownResetEvent.Set();
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

		private static void InitServices() {
			string globalConfigFile = Path.Combine(SharedInfo.ConfigDirectory, SharedInfo.GlobalConfigFileName);

			GlobalConfig = GlobalConfig.Load(globalConfigFile);
			if (GlobalConfig == null) {
				ASF.ArchiLogger.LogGenericError("Global config could not be loaded, please make sure that " + globalConfigFile + " exists and is valid! Did you forget to read wiki?");
				Thread.Sleep(5000);
				Exit(1);
			}

			string globalDatabaseFile = Path.Combine(SharedInfo.ConfigDirectory, SharedInfo.GlobalDatabaseFileName);

			GlobalDatabase = GlobalDatabase.Load(globalDatabaseFile);
			if (GlobalDatabase == null) {
				ASF.ArchiLogger.LogGenericError("Global database could not be loaded, if issue persists, please remove " + globalDatabaseFile + " in order to recreate database!");
				Thread.Sleep(5000);
				Exit(1);
			}

			ArchiWebHandler.Init();
			WebBrowser.Init();
			WCF.Init();

			WebBrowser = new WebBrowser(ASF.ArchiLogger);
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

		private static void ParsePostInitArgs(IEnumerable<string> args) {
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
						ASF.InitBots();
						break;
					default:
						if (arg.StartsWith("--", StringComparison.Ordinal)) {
							if (arg.StartsWith("--cryptkey=", StringComparison.Ordinal) && (arg.Length > 11)) {
								CryptoHelper.SetEncryptionKey(arg.Substring(11));
							}

							break;
						}

						if (!Mode.HasFlag(EMode.Client)) {
							ASF.ArchiLogger.LogGenericWarning("Ignoring command because --client wasn't specified: " + arg);
							break;
						}

						ASF.ArchiLogger.LogGenericInfo("Command sent: " + arg);
						ASF.ArchiLogger.LogGenericInfo("Response received: " + WCF.SendCommand(arg));
						break;
				}
			}
		}

		private static void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs args) {
			if (args?.ExceptionObject == null) {
				ASF.ArchiLogger.LogNullError(nameof(args) + " || " + nameof(args.ExceptionObject));
				return;
			}

			ASF.ArchiLogger.LogFatalException((Exception) args.ExceptionObject);
		}

		private static void UnobservedTaskExceptionHandler(object sender, UnobservedTaskExceptionEventArgs args) {
			if (args?.Exception == null) {
				ASF.ArchiLogger.LogNullError(nameof(args) + " || " + nameof(args.Exception));
				return;
			}

			ASF.ArchiLogger.LogFatalException(args.Exception);
		}

		private static void Init(string[] args) {
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

			if (!Runtime.IsRuntimeSupported) {
				ASF.ArchiLogger.LogGenericError("ASF detected unsupported runtime version, program might NOT run correctly in current environment. You're running it at your own risk!");
				Thread.Sleep(10000);
			}

			InitServices();

			// If debugging is on, we prepare debug directory prior to running
			if (GlobalConfig.Debug) {
				if (Directory.Exists(SharedInfo.DebugDirectory)) {
					Directory.Delete(SharedInfo.DebugDirectory, true);
					Thread.Sleep(1000); // Dirty workaround giving Windows some time to sync
				}

				Directory.CreateDirectory(SharedInfo.DebugDirectory);

				SteamKit2.DebugLog.AddListener(new Debugging.DebugListener());
				SteamKit2.DebugLog.Enabled = true;
			}

			// Parse post-init args
			if (args != null) {
				ParsePostInitArgs(args);
			}

			// If we ran ASF as a client, we're done by now
			if (Mode.HasFlag(EMode.Client) && !Mode.HasFlag(EMode.Server)) {
				Exit();
			}

			ASF.CheckForUpdate().Wait();
			ASF.InitBots();
			ASF.InitFileWatcher();
		}

		private static void Main(string[] args) {
			if (Runtime.IsUserInteractive) {
				// App
				Init(args);

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

		private sealed class Service : ServiceBase {
			internal Service() {
				ServiceName = SharedInfo.ServiceName;
			}

			protected override void OnStart(string[] args) => Task.Run(() => {
				Init(args);
				ShutdownResetEvent.Wait();
				Stop();
			});

			protected override void OnStop() => Shutdown();
		}
	}

}
