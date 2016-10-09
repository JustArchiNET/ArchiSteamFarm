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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.JSON;

namespace ArchiSteamFarm {
	internal static class ASF {
		internal sealed class BotConfigEventArgs : EventArgs {
			internal readonly BotConfig BotConfig;

			internal BotConfigEventArgs(BotConfig botConfig = null) {
				BotConfig = botConfig;
			}
		}

		private static readonly ConcurrentDictionary<Bot, DateTime> LastWriteTimes = new ConcurrentDictionary<Bot, DateTime>();

		private static Timer AutoUpdatesTimer;
		private static FileSystemWatcher FileSystemWatcher;

		internal static async Task CheckForUpdate(bool updateOverride = false) {
			string exeFile = Assembly.GetEntryAssembly().Location;
			if (string.IsNullOrEmpty(exeFile)) {
				Logging.LogNullError(nameof(exeFile));
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
					Logging.LogGenericException(e);
					Logging.LogGenericError("Could not remove old ASF binary, please remove " + oldExeFile + " manually in order for update function to work!");
				}
			}

			if (Program.GlobalConfig.UpdateChannel == GlobalConfig.EUpdateChannel.None) {
				return;
			}

			if ((AutoUpdatesTimer == null) && Program.GlobalConfig.AutoUpdates) {
				AutoUpdatesTimer = new Timer(
					async e => await CheckForUpdate().ConfigureAwait(false),
					null,
					TimeSpan.FromDays(1), // Delay
					TimeSpan.FromDays(1) // Period
				);

				Logging.LogGenericInfo("ASF will automatically check for new versions every 24 hours");
			}

			string releaseURL = SharedInfo.GithubReleaseURL;
			if (Program.GlobalConfig.UpdateChannel == GlobalConfig.EUpdateChannel.Stable) {
				releaseURL += "/latest";
			}

			Logging.LogGenericInfo("Checking new version...");

			GitHub.ReleaseResponse releaseResponse;

			if (Program.GlobalConfig.UpdateChannel == GlobalConfig.EUpdateChannel.Stable) {
				releaseResponse = await Program.WebBrowser.UrlGetToJsonResultRetry<GitHub.ReleaseResponse>(releaseURL).ConfigureAwait(false);
				if (releaseResponse == null) {
					Logging.LogGenericWarning("Could not check latest version!");
					return;
				}
			} else {
				List<GitHub.ReleaseResponse> releases = await Program.WebBrowser.UrlGetToJsonResultRetry<List<GitHub.ReleaseResponse>>(releaseURL).ConfigureAwait(false);
				if ((releases == null) || (releases.Count == 0)) {
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

			Logging.LogGenericInfo("Local version: " + SharedInfo.Version + " | Remote version: " + newVersion);

			if (SharedInfo.Version.CompareTo(newVersion) >= 0) { // If local version is the same or newer than remote version
				return;
			}

			if (!updateOverride && !Program.GlobalConfig.AutoUpdates) {
				Logging.LogGenericInfo("New version is available!");
				Logging.LogGenericInfo("Consider updating yourself!");
				await Task.Delay(5000).ConfigureAwait(false);
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

			string exeFileName = Path.GetFileName(exeFile);
			GitHub.ReleaseResponse.Asset binaryAsset = releaseResponse.Assets.FirstOrDefault(asset => !string.IsNullOrEmpty(asset.Name) && asset.Name.Equals(exeFileName, StringComparison.OrdinalIgnoreCase));

			if (binaryAsset == null) {
				Logging.LogGenericWarning("Could not proceed with update because there is no asset that relates to currently running binary!");
				return;
			}

			if (string.IsNullOrEmpty(binaryAsset.DownloadURL)) {
				Logging.LogGenericWarning("Could not proceed with update because download URL is empty!");
				return;
			}

			Logging.LogGenericInfo("Downloading new version...");
			Logging.LogGenericInfo("While waiting, consider donating if you appreciate the work being done :)");

			byte[] result = await Program.WebBrowser.UrlGetToBytesRetry(binaryAsset.DownloadURL).ConfigureAwait(false);
			if (result == null) {
				return;
			}

			string newExeFile = exeFile + ".new";

			// Firstly we create new exec
			try {
				File.WriteAllBytes(newExeFile, result);
			} catch (Exception e) {
				Logging.LogGenericException(e);
				return;
			}

			// Now we move current -> old
			try {
				File.Move(exeFile, oldExeFile);
			} catch (Exception e) {
				Logging.LogGenericException(e);
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
				Logging.LogGenericException(e);
				try {
					// Cleanup
					File.Move(oldExeFile, exeFile);
					File.Delete(newExeFile);
				} catch {
					// Ignored
				}
				return;
			}

			Logging.LogGenericInfo("Update process finished!");

			if (Program.GlobalConfig.AutoRestart) {
				Logging.LogGenericInfo("Restarting...");
				await Task.Delay(5000).ConfigureAwait(false);
				Program.Restart();
			} else {
				Logging.LogGenericInfo("Exiting...");
				await Task.Delay(5000).ConfigureAwait(false);
				Program.Exit();
			}
		}

		internal static void InitFileWatcher() {
			if (FileSystemWatcher != null) {
				return;
			}

			FileSystemWatcher = new FileSystemWatcher(SharedInfo.ConfigDirectory, "*json") {
				NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
			};

			FileSystemWatcher.Changed += OnChanged;
			FileSystemWatcher.Created += OnCreated;
			FileSystemWatcher.Deleted += OnDeleted;
			FileSystemWatcher.Renamed += OnRenamed;

			FileSystemWatcher.EnableRaisingEvents = true;
		}

		private static string GetBotNameFromConfigFileName(string fileName) {
			if (string.IsNullOrEmpty(fileName)) {
				Logging.LogNullError(nameof(fileName));
				return null;
			}

			string botName = Path.GetFileNameWithoutExtension(fileName);
			if (!string.IsNullOrEmpty(botName)) {
				return !botName.Equals(SharedInfo.ASF) ? botName : null;
			}

			Logging.LogNullError(nameof(botName));
			return null;
		}

		private static Bot GetBotFromConfigFileName(string fileName) {
			if (string.IsNullOrEmpty(fileName)) {
				Logging.LogNullError(nameof(fileName));
				return null;
			}

			string botName = GetBotNameFromConfigFileName(fileName);
			if (string.IsNullOrEmpty(botName)) {
				Logging.LogNullError(nameof(botName));
				return null;
			}

			Bot bot;
			return Bot.Bots.TryGetValue(botName, out bot) ? bot : null;
		}

		private static async Task CreateBot(string botName) {
			if (string.IsNullOrEmpty(botName)) {
				Logging.LogNullError(nameof(botName));
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
				Logging.LogNullError(nameof(sender) + " || " + nameof(e));
				return;
			}

			string botName = GetBotNameFromConfigFileName(e.Name);
			if (string.IsNullOrEmpty(botName)) {
				return;
			}

			Bot bot;
			if (!Bot.Bots.TryGetValue(botName, out bot)) {
				CreateBot(botName).Forget();
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
				Logging.LogNullError(nameof(sender) + " || " + nameof(e));
				return;
			}

			string botName = GetBotNameFromConfigFileName(e.Name);
			if (string.IsNullOrEmpty(botName)) {
				return;
			}

			CreateBot(botName).Forget();
		}

		private static void OnDeleted(object sender, FileSystemEventArgs e) {
			if ((sender == null) || (e == null)) {
				Logging.LogNullError(nameof(sender) + " || " + nameof(e));
				return;
			}

			Bot bot = GetBotFromConfigFileName(e.Name);
			bot?.OnNewConfigLoaded(new BotConfigEventArgs()).Forget();
		}

		private static void OnRenamed(object sender, RenamedEventArgs e) {
			if ((sender == null) || (e == null)) {
				Logging.LogNullError(nameof(sender) + " || " + nameof(e));
				return;
			}

			Bot bot = GetBotFromConfigFileName(e.OldName);
			bot?.OnNewConfigLoaded(new BotConfigEventArgs()).Forget();
		}
	}
}
