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

using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace ArchiSteamFarm {
	internal static class Program {
		internal enum EUserInputType : byte {
			Login,
			Password,
			PhoneNumber,
			SMS,
			SteamGuard,
			SteamParentalPIN,
			RevocationCode,
			TwoFactorAuthentication,
		}

		internal enum EMode : byte {
			Normal, // Standard most common usage
			Client, // WCF client only
			Server // Normal + WCF server
		}

		private const string LatestGithubReleaseURL = "https://api.github.com/repos/JustArchi/ArchiSteamFarm/releases/latest";
		internal const string ConfigDirectory = "config";
		internal const string LogFile = "log.txt";
		internal const string GlobalConfigFile = "ASF.json";

		private static readonly object ConsoleLock = new object();
		private static readonly SemaphoreSlim SteamSemaphore = new SemaphoreSlim(1);
		private static readonly ManualResetEvent ShutdownResetEvent = new ManualResetEvent(false);
		private static readonly Assembly Assembly = Assembly.GetExecutingAssembly();
		private static readonly string ExecutableFile = Assembly.Location;
		private static readonly string ExecutableDirectory = Path.GetDirectoryName(ExecutableFile);
		private static readonly WCF WCF = new WCF();

		internal static readonly string Version = Assembly.GetName().Version.ToString();

		internal static GlobalConfig GlobalConfig { get; private set; }
		internal static bool ConsoleIsBusy { get; private set; } = false;

		private static EMode Mode = EMode.Normal;

		private static async Task CheckForUpdate() {
			JObject response = await WebBrowser.UrlGetToJObject(LatestGithubReleaseURL).ConfigureAwait(false);
			if (response == null) {
				return;
			}

			string remoteVersion = response["tag_name"].ToString();
			if (string.IsNullOrEmpty(remoteVersion)) {
				return;
			}

			string localVersion = Version;

			Logging.LogGenericInfo("Local version: " + localVersion);
			Logging.LogGenericInfo("Remote version: " + remoteVersion);

			int comparisonResult = localVersion.CompareTo(remoteVersion);
			if (comparisonResult < 0) {
				Logging.LogGenericInfo("New version is available!");
				Logging.LogGenericInfo("Consider updating yourself!");
				await Utilities.SleepAsync(5000).ConfigureAwait(false);
			} else if (comparisonResult > 0) {
				Logging.LogGenericInfo("You're currently using pre-release version!");
				Logging.LogGenericInfo("Be careful!");
			}
		}

		internal static void Exit(int exitCode = 0) {
			Environment.Exit(exitCode);
		}

		internal static void Restart() {
			System.Diagnostics.Process.Start(ExecutableFile, string.Join(" ", Environment.GetCommandLineArgs()));
			Exit();
		}

		internal static async Task LimitSteamRequestsAsync() {
			await SteamSemaphore.WaitAsync().ConfigureAwait(false);
			Task.Run(async () => {
				await Utilities.SleepAsync(GlobalConfig.RequestLimiterDelay * 1000).ConfigureAwait(false);
				SteamSemaphore.Release();
			}).Forget();
		}

		internal static string GetUserInput(string botLogin, EUserInputType userInputType, string extraInformation = null) {
			string result;
			lock (ConsoleLock) {
				ConsoleIsBusy = true;
				switch (userInputType) {
					case EUserInputType.Login:
						Console.Write("<" + botLogin + "> Please enter your login: ");
						break;
					case EUserInputType.Password:
						Console.Write("<" + botLogin + "> Please enter your password: ");
						break;
					case EUserInputType.PhoneNumber:
						Console.Write("<" + botLogin + "> Please enter your full phone number (e.g. +1234567890): ");
						break;
					case EUserInputType.SMS:
						Console.Write("<" + botLogin + "> Please enter SMS code sent on your mobile: ");
						break;
					case EUserInputType.SteamGuard:
						Console.Write("<" + botLogin + "> Please enter the auth code sent to your email: ");
						break;
					case EUserInputType.SteamParentalPIN:
						Console.Write("<" + botLogin + "> Please enter steam parental PIN: ");
						break;
					case EUserInputType.RevocationCode:
						Console.WriteLine("<" + botLogin + "> PLEASE WRITE DOWN YOUR REVOCATION CODE: " + extraInformation);
						Console.WriteLine("<" + botLogin + "> THIS IS THE ONLY WAY TO NOT GET LOCKED OUT OF YOUR ACCOUNT!");
						Console.Write("<" + botLogin + "> Hit enter once ready...");
						break;
					case EUserInputType.TwoFactorAuthentication:
						Console.Write("<" + botLogin + "> Please enter your 2 factor auth code from your authenticator app: ");
						break;
				}
				result = Console.ReadLine();
				Console.Clear(); // For security purposes
				ConsoleIsBusy = false;
			}

			return result.Trim(); // Get rid of all whitespace characters
		}

		internal static void OnBotShutdown() {
			foreach (Bot bot in Bot.Bots.Values) {
				if (bot.KeepRunning) {
					return;
				}
			}

			if (WCF.IsServerRunning()) {
				return;
			}

			Logging.LogGenericInfo("No bots are running, exiting");
			ShutdownResetEvent.Set();
		}

		private static void InitServices() {
			GlobalConfig = GlobalConfig.Load();
			if (GlobalConfig == null) {
				Logging.LogGenericError("Global config could not be loaded, please make sure that ASF.db exists and is valid!");
				Thread.Sleep(5000);
				Exit(1);
			}

			ArchiWebHandler.Init();
			WebBrowser.Init();
			WCF.Init();
		}

		private static void ParseArgs(string[] args) {
			foreach (string arg in args) {
				switch (arg) {
					case "--client":
						Mode = EMode.Client;
						Logging.LogToFile = false;
						break;
					case "--log":
						Logging.LogToFile = true;
						break;
					case "--no-log":
						Logging.LogToFile = false;
						break;
					case "--server":
						Mode = EMode.Server;
						WCF.StartServer();
						break;
					default:
						if (arg.StartsWith("--")) {
							Logging.LogGenericWarning("Unrecognized parameter: " + arg);
							continue;
						}

						if (Mode != EMode.Client) {
							Logging.LogGenericWarning("Ignoring command because --client wasn't specified: " + arg);
							continue;
						}

						Logging.LogGenericInfo("Command sent: \"" + arg + "\"");

						// We intentionally execute this async block synchronously
						Logging.LogGenericInfo("Response received: \"" + WCF.SendCommand(arg) + "\"");
						/*
						Task.Run(async () => {
							Logging.LogGenericNotice("WCF", "Response received: " + await WCF.SendCommand(arg).ConfigureAwait(false));
						}).Wait();
						*/
						break;
				}
			}
		}

		private static void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs args) {
			if (sender == null || args == null) {
				return;
			}

			Logging.LogGenericException((Exception) args.ExceptionObject);
		}

		private static void Main(string[] args) {
			AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionHandler;

			Logging.LogGenericInfo("Archi's Steam Farm, version " + Version);
			Directory.SetCurrentDirectory(ExecutableDirectory);
			InitServices();

			// Allow loading configs from source tree if it's a debug build
			if (Debugging.IsDebugBuild) {

				// Common structure is bin/(x64/)Debug/ArchiSteamFarm.exe, so we allow up to 4 directories up
				for (var i = 0; i < 4; i++) {
					Directory.SetCurrentDirectory("..");
					if (Directory.Exists(ConfigDirectory)) {
						break;
					}
				}

				// If config directory doesn't exist after our adjustment, abort all of that
				if (!Directory.Exists(ConfigDirectory)) {
					Directory.SetCurrentDirectory(ExecutableDirectory);
				}
			}

			// Parse args
			ParseArgs(args);

			// If we ran ASF as a client, we're done by now
			if (Mode == EMode.Client) {
				return;
			}

			// From now on it's server mode
			Logging.Init();

			if (!Directory.Exists(ConfigDirectory)) {
				Logging.LogGenericError("Config directory doesn't exist!");
				Thread.Sleep(5000);
				Exit(1);
			}

			Task.Run(async () => await CheckForUpdate().ConfigureAwait(false)).Wait();

			// Before attempting to connect, initialize our list of CMs
			Bot.RefreshCMs().Wait();

			string globalConfigName = GlobalConfigFile.Substring(0, GlobalConfigFile.LastIndexOf('.'));

			foreach (var configFile in Directory.EnumerateFiles(ConfigDirectory, "*.json")) {
				string botName = Path.GetFileNameWithoutExtension(configFile);
				if (botName.Equals(globalConfigName)) {
					continue;
				}

				Bot bot = new Bot(botName);
				if (!bot.BotConfig.Enabled) {
					Logging.LogGenericInfo("Not starting this instance because it's disabled in config file", botName);
				}
			}

			// CONVERSION START
			foreach (var configFile in Directory.EnumerateFiles(ConfigDirectory, "*.xml")) {
				string botName = Path.GetFileNameWithoutExtension(configFile);
				Logging.LogGenericWarning("Found legacy " + botName + ".xml config file, it will now be converted to new ASF V2.0 format!");
				Bot bot = new Bot(botName);
				if (!bot.BotConfig.Enabled) {
					Logging.LogGenericInfo("Not starting this instance because it's disabled in config file", botName);
				}
			}
			// CONVERSION END

			// Check if we got any bots running
			OnBotShutdown();

			// Wait for signal to shutdown
			ShutdownResetEvent.WaitOne();

			// We got a signal to shutdown, consider giving user some time to read the message
			Thread.Sleep(5000);

			// This is over, cleanup only now
			WCF.StopServer();
		}
	}
}
