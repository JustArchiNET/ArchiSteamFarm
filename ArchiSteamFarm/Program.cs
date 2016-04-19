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

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace ArchiSteamFarm {
	internal static class Program {
		internal enum EUserInputType : byte {
			Unknown,
			DeviceID,
			Login,
			Password,
			PhoneNumber,
			SMS,
			SteamGuard,
			SteamParentalPIN,
			RevocationCode,
			TwoFactorAuthentication,
			WCFHostname
		}

		internal enum EMode : byte {
			Unknown,
			Normal, // Standard most common usage
			Client, // WCF client only
			Server // Normal + WCF server
		}

		internal const string ASF = "ASF";
		internal const string ConfigDirectory = "config";
		internal const string DebugDirectory = "debug";
		internal const string LogFile = "log.txt";
		internal const string GithubRepo = "JustArchi/ArchiSteamFarm";
		internal const string GlobalConfigFile = ASF + ".json";
		internal const string GlobalDatabaseFile = ASF + ".db";

		private const string GithubReleaseURL = "https://api.github.com/repos/" + GithubRepo + "/releases"; // GitHub API is HTTPS only

		internal static readonly Version Version = Assembly.GetExecutingAssembly().GetName().Version;

		private static readonly object ConsoleLock = new object();
		private static readonly SemaphoreSlim SteamSemaphore = new SemaphoreSlim(1);
		private static readonly ManualResetEventSlim ShutdownResetEvent = new ManualResetEventSlim(false);
		private static readonly string ExecutableFile = Assembly.GetEntryAssembly().Location;
		private static readonly string ExecutableName = Path.GetFileName(ExecutableFile);
		private static readonly string ExecutableDirectory = Path.GetDirectoryName(ExecutableFile);
		private static readonly WCF WCF = new WCF();

		internal static GlobalConfig GlobalConfig { get; private set; }
		internal static GlobalDatabase GlobalDatabase { get; private set; }
		internal static bool ConsoleIsBusy { get; private set; } = false;

		private static Timer AutoUpdatesTimer;
		private static EMode Mode = EMode.Normal;
		private static WebBrowser WebBrowser;

		internal static async Task CheckForUpdate() {
			string oldExeFile = ExecutableFile + ".old";

			// We booted successfully so we can now remove old exe file
			if (File.Exists(oldExeFile)) {
				// It's entirely possible that old process is still running, allow at least a second before trying to remove the file
				await Utilities.SleepAsync(1000).ConfigureAwait(false);

				try {
					File.Delete(oldExeFile);
				} catch (Exception e) {
					Logging.LogGenericException(e);
					Logging.LogGenericError("Could not remove old ASF binary, please remove " + oldExeFile + " manually in order for update function to work!");
				}
			}

			if (GlobalConfig.UpdateChannel == GlobalConfig.EUpdateChannel.Unknown) {
				return;
			}

			string releaseURL = GithubReleaseURL;
			if (GlobalConfig.UpdateChannel == GlobalConfig.EUpdateChannel.Stable) {
				releaseURL += "/latest";
			}

			string response = null;
			Logging.LogGenericInfo("Checking new version...");
			for (byte i = 0; i < WebBrowser.MaxRetries && string.IsNullOrEmpty(response); i++) {
				response = await WebBrowser.UrlGetToContent(releaseURL).ConfigureAwait(false);
			}

			if (string.IsNullOrEmpty(response)) {
				Logging.LogGenericWarning("Could not check latest version!");
				return;
			}

			GitHub.ReleaseResponse releaseResponse;
			if (GlobalConfig.UpdateChannel == GlobalConfig.EUpdateChannel.Stable) {
				try {
					releaseResponse = JsonConvert.DeserializeObject<GitHub.ReleaseResponse>(response);
				} catch (JsonException e) {
					Logging.LogGenericException(e);
					return;
				}
			} else {
				List<GitHub.ReleaseResponse> releases;
				try {
					releases = JsonConvert.DeserializeObject<List<GitHub.ReleaseResponse>>(response);
				} catch (JsonException e) {
					Logging.LogGenericException(e);
					return;
				}

				if (releases == null || releases.Count == 0) {
					Logging.LogGenericWarning("Could not check latest version!");
					return;
				}

				releaseResponse = releases[0];
			}

			if (string.IsNullOrEmpty(releaseResponse.Tag)) {
				Logging.LogGenericWarning("Could not check latest version!");
				return;
			}

			Version newVersion = new Version(releaseResponse.Tag);

			Logging.LogGenericInfo("Local version: " + Version + " | Remote version: " + newVersion);

			if (Version.CompareTo(newVersion) >= 0) { // If local version is the same or newer than remote version
				if (AutoUpdatesTimer == null && GlobalConfig.AutoUpdates) {
					Logging.LogGenericInfo("ASF will automatically check for new versions every 24 hours");
					AutoUpdatesTimer = new Timer(
						async e => await CheckForUpdate().ConfigureAwait(false),
						null,
						TimeSpan.FromDays(1), // Delay
						TimeSpan.FromDays(1) // Period
					);
				}
				return;
			}

			if (!GlobalConfig.AutoUpdates) {
				Logging.LogGenericInfo("New version is available!");
				Logging.LogGenericInfo("Consider updating yourself!");
				await Utilities.SleepAsync(5000).ConfigureAwait(false);
				return;
			}

			if (File.Exists(oldExeFile)) {
				Logging.LogGenericWarning("Refusing to proceed with auto update as old " + oldExeFile + " binary could not be removed, please remove it manually");
				return;
			}

			// Auto update logic starts here
			if (releaseResponse.Assets == null) {
				Logging.LogGenericWarning("Could not proceed with update because that version doesn't include assets!");
				return;
			}

			GitHub.Asset binaryAsset = null;
			foreach (var asset in releaseResponse.Assets) {
				if (string.IsNullOrEmpty(asset.Name) || !asset.Name.Equals(ExecutableName, StringComparison.OrdinalIgnoreCase)) {
					continue;
				}

				binaryAsset = asset;
				break;
			}

			if (binaryAsset == null) {
				Logging.LogGenericWarning("Could not proceed with update because there is no asset that relates to currently running binary!");
				return;
			}

			if (string.IsNullOrEmpty(binaryAsset.DownloadURL)) {
				Logging.LogGenericWarning("Could not proceed with update because download URL is empty!");
				return;
			}

			byte[] result = null;
			for (byte i = 0; i < WebBrowser.MaxRetries && result == null; i++) {
				Logging.LogGenericInfo("Downloading new version...");
				result = await WebBrowser.UrlGetToBytes(binaryAsset.DownloadURL).ConfigureAwait(false);
			}

			if (result == null) {
				Logging.LogGenericWTF("Request failed even after " + WebBrowser.MaxRetries + " tries");
				return;
			}

			string newExeFile = ExecutableFile + ".new";

			// Firstly we create new exec
			try {
				File.WriteAllBytes(newExeFile, result);
			} catch (Exception e) {
				Logging.LogGenericException(e);
				return;
			}

			// Now we move current -> old
			try {
				File.Move(ExecutableFile, oldExeFile);
			} catch (Exception e) {
				Logging.LogGenericException(e);
				try {
					// Cleanup
					File.Delete(newExeFile);
				} catch { }
				return;
			}

			// Now we move new -> current
			try {
				File.Move(newExeFile, ExecutableFile);
			} catch (Exception e) {
				Logging.LogGenericException(e);
				try {
					// Cleanup
					File.Move(oldExeFile, ExecutableFile);
					File.Delete(newExeFile);
				} catch { }
				return;
			}

			Logging.LogGenericInfo("Update process finished!");

			if (GlobalConfig.AutoRestart) {
				Logging.LogGenericInfo("Restarting...");
				await Utilities.SleepAsync(5000).ConfigureAwait(false);
				Restart();
			} else {
				Logging.LogGenericInfo("Exiting...");
				await Utilities.SleepAsync(5000).ConfigureAwait(false);
				Exit();
			}
		}

		internal static void Exit(int exitCode = 0) {
			WCF.StopServer();
			Environment.Exit(exitCode);
		}

		internal static void Restart() {
			try {
				Process.Start(ExecutableFile, string.Join(" ", Environment.GetCommandLineArgs().Skip(1)));
			} catch (Exception e) {
				Logging.LogGenericException(e);
			}

			Exit();
		}

		internal static async Task LimitSteamRequestsAsync() {
			await SteamSemaphore.WaitAsync().ConfigureAwait(false);
			Task.Run(async () => {
				await Utilities.SleepAsync(GlobalConfig.LoginLimiterDelay * 1000).ConfigureAwait(false);
				SteamSemaphore.Release();
			}).Forget();
		}

		internal static string GetUserInput(EUserInputType userInputType, string botName = null, string extraInformation = null) {
			if (userInputType == EUserInputType.Unknown) {
				return null;
			}

			if (GlobalConfig.Headless) {
				Logging.LogGenericWarning("Received a request for user input, but process is running in headless mode!");
				return null;
			}

			string result;
			lock (ConsoleLock) {
				ConsoleIsBusy = true;
				switch (userInputType) {
					case EUserInputType.DeviceID:
						Console.Write((string.IsNullOrEmpty(botName) ? "" : "<" + botName + "> ") + "Please enter your Device ID (including \"android:\"): ");
						break;
					case EUserInputType.Login:
						Console.Write((string.IsNullOrEmpty(botName) ? "" : "<" + botName + "> ") + "Please enter your login: ");
						break;
					case EUserInputType.Password:
						Console.Write((string.IsNullOrEmpty(botName) ? "" : "<" + botName + "> ") + "Please enter your password: ");
						break;
					case EUserInputType.PhoneNumber:
						Console.Write((string.IsNullOrEmpty(botName) ? "" : "<" + botName + "> ") + "Please enter your full phone number (e.g. +1234567890): ");
						break;
					case EUserInputType.SMS:
						Console.Write((string.IsNullOrEmpty(botName) ? "" : "<" + botName + "> ") + "Please enter SMS code sent on your mobile: ");
						break;
					case EUserInputType.SteamGuard:
						Console.Write((string.IsNullOrEmpty(botName) ? "" : "<" + botName + "> ") + "Please enter the auth code sent to your email: ");
						break;
					case EUserInputType.SteamParentalPIN:
						Console.Write((string.IsNullOrEmpty(botName) ? "" : "<" + botName + "> ") + "Please enter steam parental PIN: ");
						break;
					case EUserInputType.RevocationCode:
						Console.WriteLine((string.IsNullOrEmpty(botName) ? "" : "<" + botName + "> ") + "PLEASE WRITE DOWN YOUR REVOCATION CODE: " + extraInformation);
						Console.Write("Hit enter once ready...");
						break;
					case EUserInputType.TwoFactorAuthentication:
						Console.Write((string.IsNullOrEmpty(botName) ? "" : "<" + botName + "> ") + "Please enter your 2 factor auth code from your authenticator app: ");
						break;
					case EUserInputType.WCFHostname:
						Console.Write((string.IsNullOrEmpty(botName) ? "" : "<" + botName + "> ") + "Please enter your WCF hostname: ");
						break;
					default:
						Console.Write((string.IsNullOrEmpty(botName) ? "" : "<" + botName + "> ") + "Please enter not documented yet value of \"" + userInputType + "\": ");
						break;
				}
				result = Console.ReadLine();
				if (!Console.IsOutputRedirected) {
					Console.Clear(); // For security purposes
				}
				ConsoleIsBusy = false;
			}

			return string.IsNullOrEmpty(result) ? null : result.Trim();
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
			Thread.Sleep(5000);
			ShutdownResetEvent.Set();
		}

		private static void InitServices() {
			GlobalConfig = GlobalConfig.Load(Path.Combine(ConfigDirectory, GlobalConfigFile));
			if (GlobalConfig == null) {
				Logging.LogGenericError("Global config could not be loaded, please make sure that ASF.json exists and is valid!");
				Thread.Sleep(5000);
				Exit(1);
			}

			GlobalDatabase = GlobalDatabase.Load(Path.Combine(ConfigDirectory, GlobalDatabaseFile));
			if (GlobalDatabase == null) {
				Logging.LogGenericError("Global database could not be loaded!");
				Thread.Sleep(5000);
				Exit(1);
			}

			ArchiWebHandler.Init();
			WebBrowser.Init();
			WCF.Init();

			WebBrowser = new WebBrowser("Main");
		}

		private static void ParseArgs(string[] args) {
			foreach (string arg in args) {
				switch (arg) {
					case "--client":
						Mode = EMode.Client;
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

						Logging.LogGenericInfo("Command sent: " + arg);

						// We intentionally execute this async block synchronously
						Logging.LogGenericInfo("Response received: " + WCF.SendCommand(arg));
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

		private static void UnobservedTaskExceptionHandler(object sender, UnobservedTaskExceptionEventArgs args) {
			if (sender == null || args == null) {
				return;
			}

			Logging.LogGenericException(args.Exception);
		}

		private static void Init(string[] args) {
			AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionHandler;
			TaskScheduler.UnobservedTaskException += UnobservedTaskExceptionHandler;

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

			// If debugging is on, we prepare debug directory prior to running
			if (GlobalConfig.Debug) {
				if (Directory.Exists(DebugDirectory)) {
					Directory.Delete(DebugDirectory, true);
					Thread.Sleep(1000); // Dirty workaround giving Windows some time to sync
				}
				Directory.CreateDirectory(DebugDirectory);

				SteamKit2.DebugLog.AddListener(new Debugging.DebugListener(Path.Combine(Program.DebugDirectory, "debug.txt")));
				SteamKit2.DebugLog.Enabled = true;
			}

			// Parse args
			ParseArgs(args);

			// If we ran ASF as a client, we're done by now
			if (Mode == EMode.Client) {
				Exit();
			}

			// From now on it's server mode
			Logging.Init();

			if (!Directory.Exists(ConfigDirectory)) {
				Logging.LogGenericError("Config directory doesn't exist!");
				Thread.Sleep(5000);
				Exit(1);
			}

			CheckForUpdate().Wait();

			// Before attempting to connect, initialize our list of CMs
			Bot.RefreshCMs(GlobalDatabase.CellID).Wait();

			bool isRunning = false;

			foreach (var configFile in Directory.EnumerateFiles(ConfigDirectory, "*.json")) {
				string botName = Path.GetFileNameWithoutExtension(configFile);
				switch (botName) {
					case ASF:
					case "example":
					case "minimal":
						continue;
				}

				Bot bot = new Bot(botName);
				if (bot.BotConfig != null && bot.BotConfig.Enabled) {
					isRunning = true;
				} else {
					Logging.LogGenericInfo("Not starting this instance because it's disabled in config file", botName);
				}
			}

			// Check if we got any bots running
			if (!isRunning) {
				OnBotShutdown();
			}
		}

		private static void Main(string[] args) {
			Init(args);

			// Wait for signal to shutdown
			ShutdownResetEvent.Wait();

			// We got a signal to shutdown
			Exit();
		}
	}
}
