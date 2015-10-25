using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace ArchiSteamFarm {
	internal static class Program {
		internal const string ConfigDirectoryPath = "config";
		private static readonly HashSet<Bot> Bots = new HashSet<Bot>();
		internal static readonly object ConsoleLock = new object();

		internal static void Exit(int exitCode = 0) {
			ShutdownAllBots();
			Environment.Exit(exitCode);
		}

		internal static string GetSteamGuardCode(string botLogin, bool twoFactorAuthentication) {
			lock (ConsoleLock) {
				if (twoFactorAuthentication) {
					Console.Write("<" + botLogin + "> Please enter your 2 factor auth code from your authenticator app: ");
				} else {
					Console.Write("<" + botLogin + "> Please enter the auth code sent to your email : ");
				}
				return Console.ReadLine();
			}
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
