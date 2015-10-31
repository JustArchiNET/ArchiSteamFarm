/*
    _                _      _  ____   _                           _____
   / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
  / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
 / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
/_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|

 Copyright 2015 Łukasz "JustArchi" Domeradzki
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
		internal enum EUserInputType {
			Login,
			Password,
			SteamGuard,
			SteamParentalPIN,
			TwoFactorAuthentication,
		}

		internal const ulong ArchiSCFarmGroup = 103582791440160998;
		internal const string ConfigDirectoryPath = "config";
		private const string LatestGithubReleaseURL = "https://api.github.com/repos/JustArchi/ArchiSteamFarm/releases/latest";

		private static readonly ManualResetEvent ShutdownResetEvent = new ManualResetEvent(false);
		internal static readonly object ConsoleLock = new object();
		internal static string Version { get { return Assembly.GetExecutingAssembly().GetName().Version.ToString(); } }

		private static async Task CheckForUpdate() {
			JObject response = await Utilities.UrlToJObject(LatestGithubReleaseURL).ConfigureAwait(false);
			if (response == null) {
				return;
			}

			string remoteVersion = response["tag_name"].ToString();
			if (string.IsNullOrEmpty(remoteVersion)) {
				return;
			}

			string localVersion = Version;

			if (localVersion.CompareTo(remoteVersion) < 0) {
				Logging.LogGenericNotice("", "New version is available!");
				Logging.LogGenericNotice("", "Local version: " + localVersion);
				Logging.LogGenericNotice("", "Remote version: " + remoteVersion);
				Logging.LogGenericNotice("", "Consider updating yourself!");
				Thread.Sleep(5000);
			} else if (localVersion.CompareTo(remoteVersion) > 0) {
				Logging.LogGenericNotice("", "You're currently using pre-release version!");
				Logging.LogGenericNotice("", "Local version: " + localVersion);
				Logging.LogGenericNotice("", "Remote version: " + remoteVersion);
				Logging.LogGenericNotice("", "Be careful!");
			}
		}

		internal static async Task Exit(int exitCode = 0) {
			await Bot.ShutdownAllBots().ConfigureAwait(false);
			Environment.Exit(exitCode);
		}

		internal static string GetUserInput(string botLogin, EUserInputType userInputType) {
			string result;
			lock (ConsoleLock) {
				switch (userInputType) {
					case EUserInputType.Login:
						Console.Write("<" + botLogin + "> Please enter your login: ");
						break;
					case EUserInputType.Password:
						Console.Write("<" + botLogin + "> Please enter your password: ");
						break;
					case EUserInputType.SteamGuard:
						Console.Write("<" + botLogin + "> Please enter the auth code sent to your email: ");
						break;
					case EUserInputType.SteamParentalPIN:
						Console.Write("<" + botLogin + "> Please enter steam parental PIN: ");
						break;
					case EUserInputType.TwoFactorAuthentication:
						Console.Write("<" + botLogin + "> Please enter your 2 factor auth code from your authenticator app: ");
						break;
				}
				result = Console.ReadLine();
				Console.Clear(); // For security purposes
			}
			return result;
		}

		internal static void OnBotShutdown(Bot bot) {
			if (Bot.GetRunningBotsCount() == 0) {
				Logging.LogGenericInfo("Main", "No bots are running, exiting");
				Thread.Sleep(5000); // This might be the only message user gets, consider giving him some time
				ShutdownResetEvent.Set();
			}
		}

		private static void Main(string[] args) {
			Logging.LogGenericInfo("Main", "Archi's Steam Farm, version " + Version);

			Task.Run(async () => await CheckForUpdate().ConfigureAwait(false)).Wait();

			// Config directory may not be in the same directory as the .exe, check maximum of 3 levels lower
			for (var i = 0; i < 4 && !Directory.Exists(ConfigDirectoryPath); i++) {
				Directory.SetCurrentDirectory("..");
			}

			if (!Directory.Exists(ConfigDirectoryPath)) {
				Logging.LogGenericError("Main", "Config directory doesn't exist!");
				Console.ReadLine();
				Task.Run(async () => await Exit(1).ConfigureAwait(false)).Wait();
            }

			foreach (var configFile in Directory.EnumerateFiles(ConfigDirectoryPath, "*.xml")) {
				string botName = Path.GetFileNameWithoutExtension(configFile);
				Bot bot = new Bot(botName);
				if (!bot.Enabled) {
					Logging.LogGenericInfo(botName, "Not starting this instance because it's disabled in config file");
				}
			}

			// Check if we got any bots running
			OnBotShutdown(null);

			ShutdownResetEvent.WaitOne();
		}
	}
}
