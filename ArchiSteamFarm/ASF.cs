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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.JSON;
using ArchiSteamFarm.Localization;

namespace ArchiSteamFarm {
	internal static class ASF {
		private const byte AutoUpdatePeriodInHours = 24;

		internal static readonly ArchiLogger ArchiLogger = new ArchiLogger(SharedInfo.ASF);

		private static readonly ConcurrentDictionary<Bot, DateTime> LastWriteTimes = new ConcurrentDictionary<Bot, DateTime>();

		private static Timer AutoUpdatesTimer;
		private static FileSystemWatcher FileSystemWatcher;

		internal static async Task CheckForUpdate(bool updateOverride = false) {
			if (Debugging.IsDebugBuild && !updateOverride) {
				return;
			}

			string exeFile = Assembly.GetEntryAssembly().Location;
			if (string.IsNullOrEmpty(exeFile)) {
				ArchiLogger.LogNullError(nameof(exeFile));
				return;
			}

			string oldExeFile = exeFile + ".old";

			// We booted successfully so we can now remove old exe file
			if (File.Exists(oldExeFile)) {
				// It's entirely possible that old process is still running, allow at least a second before trying to remove the file
				await Task.Delay(1000).ConfigureAwait(false);

				try {
					File.Delete(oldExeFile);
				} catch (Exception e) {
					ArchiLogger.LogGenericException(e);
					ArchiLogger.LogGenericError(string.Format(Strings.ErrorRemovingOldBinary, oldExeFile));
				}
			}

			if (Program.GlobalConfig.UpdateChannel == GlobalConfig.EUpdateChannel.None) {
				return;
			}

			if ((AutoUpdatesTimer == null) && Program.GlobalConfig.AutoUpdates) {
				AutoUpdatesTimer = new Timer(
					async e => await CheckForUpdate().ConfigureAwait(false),
					null,
					TimeSpan.FromHours(AutoUpdatePeriodInHours), // Delay
					TimeSpan.FromHours(AutoUpdatePeriodInHours) // Period
				);

				ArchiLogger.LogGenericInfo(string.Format(Strings.AutoUpdateCheckInfo, AutoUpdatePeriodInHours));
			}

			string releaseURL = SharedInfo.GithubReleaseURL;
			if (Program.GlobalConfig.UpdateChannel == GlobalConfig.EUpdateChannel.Stable) {
				releaseURL += "/latest";
			}

			ArchiLogger.LogGenericInfo(Strings.UpdateCheckingNewVersion);

			GitHub.ReleaseResponse releaseResponse;

			if (Program.GlobalConfig.UpdateChannel == GlobalConfig.EUpdateChannel.Stable) {
				releaseResponse = await Program.WebBrowser.UrlGetToJsonResultRetry<GitHub.ReleaseResponse>(releaseURL).ConfigureAwait(false);
				if (releaseResponse == null) {
					ArchiLogger.LogGenericWarning(Strings.ErrorUpdateCheckFailed);
					return;
				}
			} else {
				List<GitHub.ReleaseResponse> releases = await Program.WebBrowser.UrlGetToJsonResultRetry<List<GitHub.ReleaseResponse>>(releaseURL).ConfigureAwait(false);
				if ((releases == null) || (releases.Count == 0)) {
					ArchiLogger.LogGenericWarning(Strings.ErrorUpdateCheckFailed);
					return;
				}

				releaseResponse = releases[0];
			}

			if (string.IsNullOrEmpty(releaseResponse.Tag)) {
				ArchiLogger.LogGenericWarning(Strings.ErrorUpdateCheckFailed);
				return;
			}

			Version newVersion = new Version(releaseResponse.Tag);

			ArchiLogger.LogGenericInfo(string.Format(Strings.UpdateVersionInfo, SharedInfo.Version, newVersion));

			if (SharedInfo.Version.CompareTo(newVersion) >= 0) { // If local version is the same or newer than remote version
				return;
			}

			if (!updateOverride && !Program.GlobalConfig.AutoUpdates) {
				ArchiLogger.LogGenericInfo(Strings.UpdateNewVersionAvailable);
				await Task.Delay(5000).ConfigureAwait(false);
				return;
			}

			if (File.Exists(oldExeFile)) {
				ArchiLogger.LogGenericError(string.Format(Strings.ErrorRemovingOldBinary, oldExeFile));
				return;
			}

			// Auto update logic starts here
			if (releaseResponse.Assets == null) {
				ArchiLogger.LogGenericWarning(Strings.ErrorUpdateNoAssets);
				return;
			}

			string exeFileName = Path.GetFileName(exeFile);
			GitHub.ReleaseResponse.Asset binaryAsset = releaseResponse.Assets.FirstOrDefault(asset => !string.IsNullOrEmpty(asset.Name) && asset.Name.Equals(exeFileName, StringComparison.OrdinalIgnoreCase));

			if (binaryAsset == null) {
				ArchiLogger.LogGenericWarning(Strings.ErrorUpdateNoAssetForThisBinary);
				return;
			}

			if (string.IsNullOrEmpty(binaryAsset.DownloadURL)) {
				ArchiLogger.LogNullError(nameof(binaryAsset.DownloadURL));
				return;
			}

			ArchiLogger.LogGenericInfo(Strings.UpdateDownloadingNewVersion);

			byte[] result = await Program.WebBrowser.UrlGetToBytesRetry(binaryAsset.DownloadURL).ConfigureAwait(false);
			if (result == null) {
				return;
			}

			string newExeFile = exeFile + ".new";

			// Firstly we create new exec
			try {
				File.WriteAllBytes(newExeFile, result);
			} catch (Exception e) {
				ArchiLogger.LogGenericException(e);
				return;
			}

			// Now we move current -> old
			try {
				File.Move(exeFile, oldExeFile);
			} catch (Exception e) {
				ArchiLogger.LogGenericException(e);
				try {
					// Cleanup
					File.Delete(newExeFile);
				} catch {
					// Ignored
				}
				return;
			}

			// Now we move new -> current
			try {
				File.Move(newExeFile, exeFile);
			} catch (Exception e) {
				ArchiLogger.LogGenericException(e);
				try {
					// Cleanup
					File.Move(oldExeFile, exeFile);
					File.Delete(newExeFile);
				} catch {
					// Ignored
				}
				return;
			}

			ArchiLogger.LogGenericInfo(Strings.UpdateFinished);
			await RestartOrExit().ConfigureAwait(false);
		}

		internal static async Task InitBots() {
			if (Bot.Bots.Count != 0) {
				return;
			}

			// Before attempting to connect, initialize our list of CMs
			await Bot.InitializeCMs(Program.GlobalDatabase.CellID, Program.GlobalDatabase.ServerListProvider).ConfigureAwait(false);

			foreach (string botName in Directory.EnumerateFiles(SharedInfo.ConfigDirectory, "*.json").Select(Path.GetFileNameWithoutExtension)) {
				switch (botName) {
					case SharedInfo.ASF:
					case "example":
					case "minimal":
						continue;
				}

				new Bot(botName).Forget();
			}

			if (Bot.Bots.Count == 0) {
				ArchiLogger.LogGenericWarning(Strings.ErrorNoBotsDefined);
			}
		}

		internal static void InitFileWatcher() {
			if (FileSystemWatcher != null) {
				return;
			}

			FileSystemWatcher = new FileSystemWatcher(SharedInfo.ConfigDirectory, "*.json") {
				NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
			};

			FileSystemWatcher.Changed += OnChanged;
			FileSystemWatcher.Created += OnCreated;
			FileSystemWatcher.Deleted += OnDeleted;
			FileSystemWatcher.Renamed += OnRenamed;

			FileSystemWatcher.EnableRaisingEvents = true;
		}

		private static async Task CreateBot(string botName) {
			if (string.IsNullOrEmpty(botName)) {
				ArchiLogger.LogNullError(nameof(botName));
				return;
			}

			if (Bot.Bots.ContainsKey(botName)) {
				return;
			}

			// It's entirely possible that some process is still accessing our file, allow at least a second before trying to read it
			await Task.Delay(1000).ConfigureAwait(false);

			if (Bot.Bots.ContainsKey(botName)) {
				return;
			}

			new Bot(botName).Forget();
		}

		private static async void OnChanged(object sender, FileSystemEventArgs e) {
			if ((sender == null) || (e == null)) {
				ArchiLogger.LogNullError(nameof(sender) + " || " + nameof(e));
				return;
			}

			string botName = Path.GetFileNameWithoutExtension(e.Name);
			if (string.IsNullOrEmpty(botName)) {
				return;
			}

			if (botName.Equals(SharedInfo.ASF)) {
				ArchiLogger.LogGenericInfo(Strings.GlobalConfigChanged);
				await RestartOrExit().ConfigureAwait(false);
				return;
			}

			Bot bot;
			if (!Bot.Bots.TryGetValue(botName, out bot)) {
				return;
			}

			DateTime lastWriteTime = File.GetLastWriteTime(e.FullPath);

			DateTime savedLastWriteTime;
			if (LastWriteTimes.TryGetValue(bot, out savedLastWriteTime)) {
				if (savedLastWriteTime >= lastWriteTime) {
					return;
				}
			}

			LastWriteTimes[bot] = lastWriteTime;

			// It's entirely possible that some process is still accessing our file, allow at least a second before trying to read it
			await Task.Delay(1000).ConfigureAwait(false);

			// It's also possible that we got some other event in the meantime
			if (LastWriteTimes.TryGetValue(bot, out savedLastWriteTime)) {
				if (lastWriteTime != savedLastWriteTime) {
					return;
				}

				if (LastWriteTimes.TryRemove(bot, out savedLastWriteTime)) {
					if (lastWriteTime != savedLastWriteTime) {
						return;
					}
				}
			}

			bot.OnNewConfigLoaded(new BotConfigEventArgs(BotConfig.Load(e.FullPath))).Forget();
		}

		private static void OnCreated(object sender, FileSystemEventArgs e) {
			if ((sender == null) || (e == null)) {
				ArchiLogger.LogNullError(nameof(sender) + " || " + nameof(e));
				return;
			}

			string botName = Path.GetFileNameWithoutExtension(e.Name);
			if (string.IsNullOrEmpty(botName)) {
				return;
			}

			CreateBot(botName).Forget();
		}

		private static void OnDeleted(object sender, FileSystemEventArgs e) {
			if ((sender == null) || (e == null)) {
				ArchiLogger.LogNullError(nameof(sender) + " || " + nameof(e));
				return;
			}

			string botName = Path.GetFileNameWithoutExtension(e.Name);
			if (string.IsNullOrEmpty(botName)) {
				return;
			}

			if (botName.Equals(SharedInfo.ASF)) {
				ArchiLogger.LogGenericError(Strings.ErrorGlobalConfigRemoved);
				Program.Exit(1);
				return;
			}

			Bot bot;
			if (Bot.Bots.TryGetValue(botName, out bot)) {
				bot.OnNewConfigLoaded(new BotConfigEventArgs()).Forget();
			}
		}

		private static void OnRenamed(object sender, RenamedEventArgs e) {
			if ((sender == null) || (e == null)) {
				ArchiLogger.LogNullError(nameof(sender) + " || " + nameof(e));
				return;
			}

			string oldBotName = Path.GetFileNameWithoutExtension(e.OldName);
			if (string.IsNullOrEmpty(oldBotName)) {
				return;
			}

			if (oldBotName.Equals(SharedInfo.ASF)) {
				ArchiLogger.LogGenericError(Strings.ErrorGlobalConfigRemoved);
				Program.Exit(1);
				return;
			}

			Bot bot;
			if (Bot.Bots.TryGetValue(oldBotName, out bot)) {
				bot.OnNewConfigLoaded(new BotConfigEventArgs()).Forget();
			}

			string newBotName = Path.GetFileNameWithoutExtension(e.Name);
			if (string.IsNullOrEmpty(newBotName)) {
				return;
			}

			CreateBot(newBotName).Forget();
		}

		private static async Task RestartOrExit() {
			if (Program.GlobalConfig.AutoRestart) {
				ArchiLogger.LogGenericInfo(Strings.Restarting);
				await Task.Delay(5000).ConfigureAwait(false);
				Program.Restart();
			} else {
				ArchiLogger.LogGenericInfo(Strings.Exiting);
				await Task.Delay(5000).ConfigureAwait(false);
				Program.Exit();
			}
		}

		internal sealed class BotConfigEventArgs : EventArgs {
			internal readonly BotConfig BotConfig;

			internal BotConfigEventArgs(BotConfig botConfig = null) {
				BotConfig = botConfig;
			}
		}

		internal enum EUserInputType : byte {
			Unknown,
			DeviceID,
			Login,
			Password,
			SteamGuard,
			SteamParentalPIN,
			TwoFactorAuthentication,
			WCFHostname
		}
	}
}