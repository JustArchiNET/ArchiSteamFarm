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

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace ArchiSteamFarm {
	internal static class Program {
		internal enum EUserInputType {
			Login,
			Password,
			SteamGuard,
			TwoFactorAuthentication,
		}

		internal const string ConfigDirectoryPath = "config";
		private static readonly HashSet<Bot> Bots = new HashSet<Bot>();
		internal static readonly object ConsoleLock = new object();

		internal static void Exit(int exitCode = 0) {
			ShutdownAllBots();
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
						Console.Write("<" + botLogin + "> Please enter the auth code sent to your email : ");
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

		private static void ShutdownAllBots() {
			lock (Bots) {
				foreach (Bot bot in Bots) {
					bot.Stop();
				}
				Bots.Clear();
			}
		}

		private static void Main(string[] args) {
			// Config directory may not be in the same directory as the .exe, check maximum of 3 levels lower
			for (var i = 0; i < 4 && !Directory.Exists(ConfigDirectoryPath); i++) {
				Directory.SetCurrentDirectory("..");
			}

			if (!Directory.Exists(ConfigDirectoryPath)) {
				Logging.LogGenericError("Main", "Config directory doesn't exist!");
				Console.ReadLine();
				Exit(1);
			}

			lock (Bots) {
				foreach (var configFile in Directory.EnumerateFiles(ConfigDirectoryPath, "*.xml")) {
					string botName = Path.GetFileNameWithoutExtension(configFile);
					Bots.Add(new Bot(botName));
				}
			}

			Thread.Sleep(Timeout.Infinite);
		}
	}
}
