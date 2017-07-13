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
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.JSON;
using ArchiSteamFarm.Localization;

namespace ArchiSteamFarm {
	internal static class ASF {
		private const byte AutoUpdatePeriodInHours = 24;
		private const string DefaultVersion = "source"; // Default entry of ArchiSteamFarm.version

		internal static readonly ArchiLogger ArchiLogger = new ArchiLogger(SharedInfo.ASF);

		private static readonly ConcurrentDictionary<Bot, DateTime> LastWriteTimes = new ConcurrentDictionary<Bot, DateTime>();

		private static Timer AutoUpdatesTimer;
		private static FileSystemWatcher FileSystemWatcher;

		internal static async Task CheckForUpdate(bool updateOverride = false) {
			if (Program.GlobalConfig.UpdateChannel == GlobalConfig.EUpdateChannel.None) {
				return;
			}

			string assemblyFile = Assembly.GetEntryAssembly().Location;
			if (string.IsNullOrEmpty(assemblyFile)) {
				ArchiLogger.LogNullError(nameof(assemblyFile));
				return;
			}

			if (!File.Exists(SharedInfo.VersionFile)) {
				ArchiLogger.LogGenericError(string.Format(Strings.ErrorIsEmpty, SharedInfo.VersionFile));
				return;
			}

			string version;

			try {
				version = await File.ReadAllTextAsync(SharedInfo.VersionFile).ConfigureAwait(false);
			} catch (Exception e) {
				ArchiLogger.LogGenericException(e);
				return;
			}

			if (string.IsNullOrEmpty(version)) {
				ArchiLogger.LogGenericError(string.Format(Strings.ErrorIsInvalid, SharedInfo.VersionFile));
				return;
			}

			version = version.TrimEnd();
			if (string.IsNullOrEmpty(version) || !IsValidVersion(version)) {
				ArchiLogger.LogGenericError(string.Format(Strings.ErrorIsInvalid, SharedInfo.VersionFile));
				return;
			}

			if (version.Equals(DefaultVersion)) {
				return;
			}

			if ((AutoUpdatesTimer == null) && Program.GlobalConfig.AutoUpdates) {
				TimeSpan autoUpdatePeriod = TimeSpan.FromHours(AutoUpdatePeriodInHours);

				AutoUpdatesTimer = new Timer(
					async e => await CheckForUpdate().ConfigureAwait(false),
					null,
					autoUpdatePeriod, // Delay
					autoUpdatePeriod // Period
				);

				ArchiLogger.LogGenericInfo(string.Format(Strings.AutoUpdateCheckInfo, autoUpdatePeriod.ToHumanReadable()));
			}

			ArchiLogger.LogGenericInfo(Strings.UpdateCheckingNewVersion);

			string targetDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

			// Cleanup from previous update - update directory for old in-use runtime files
			string backupDirectory = Path.Combine(targetDirectory, SharedInfo.UpdateDirectory);
			if (Directory.Exists(backupDirectory)) {
				// It's entirely possible that old process is still running, wait a short moment for eventual cleanup
				await Task.Delay(5000).ConfigureAwait(false);

				try {
					Directory.Delete(backupDirectory, true);
				} catch (Exception e) {
					ArchiLogger.LogGenericException(e);
					return;
				}
			}

			// Cleanup from previous update - old non-runtime in-use files
			foreach (string file in Directory.EnumerateFiles(targetDirectory, "*.old", SearchOption.AllDirectories)) {
				try {
					File.Delete(file);
				} catch (Exception e) {
					ArchiLogger.LogGenericException(e);
					return;
				}
			}

			string releaseURL = SharedInfo.GithubReleaseURL;
			if (Program.GlobalConfig.UpdateChannel == GlobalConfig.EUpdateChannel.Stable) {
				releaseURL += "/latest";
			}

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

			if (SharedInfo.Version == newVersion) {
				return;
			}

			if (SharedInfo.Version > newVersion) {
				ArchiLogger.LogGenericWarning(Strings.WarningPreReleaseVersion);
				await Task.Delay(15 * 1000).ConfigureAwait(false);
				return;
			}

			if (!updateOverride && !Program.GlobalConfig.AutoUpdates) {
				ArchiLogger.LogGenericInfo(Strings.UpdateNewVersionAvailable);
				await Task.Delay(5000).ConfigureAwait(false);
				return;
			}

			// Auto update logic starts here
			if (releaseResponse.Assets == null) {
				ArchiLogger.LogGenericWarning(Strings.ErrorUpdateNoAssets);
				return;
			}

			string targetFile = SharedInfo.ASF + "-" + version + ".zip";
			GitHub.ReleaseResponse.Asset binaryAsset = releaseResponse.Assets.FirstOrDefault(asset => asset.Name.Equals(targetFile, StringComparison.OrdinalIgnoreCase));

			if (binaryAsset == null) {
				ArchiLogger.LogGenericWarning(Strings.ErrorUpdateNoAssetForThisVersion);
				return;
			}

			if (string.IsNullOrEmpty(binaryAsset.DownloadURL)) {
				ArchiLogger.LogNullError(nameof(binaryAsset.DownloadURL));
				return;
			}

			ArchiLogger.LogGenericInfo(string.Format(Strings.UpdateDownloadingNewVersion, newVersion, binaryAsset.Size / 1024 / 1024));

			byte[] result = await Program.WebBrowser.UrlGetToBytesRetry(binaryAsset.DownloadURL).ConfigureAwait(false);
			if (result == null) {
				return;
			}

			try {
				using (ZipArchive zipArchive = new ZipArchive(new MemoryStream(result))) {
					UpdateFromArchive(zipArchive, targetDirectory);
				}
			} catch (Exception e) {
				ArchiLogger.LogGenericException(e);
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

			foreach (string botName in Directory.EnumerateFiles(SharedInfo.ConfigDirectory, "*.json").Select(Path.GetFileNameWithoutExtension).Where(botName => !string.IsNullOrEmpty(botName) && IsValidBotName(botName)).OrderBy(botName => botName)) {
				Bot.RegisterBot(botName);
			}

			if (Bot.Bots.Count == 0) {
				ArchiLogger.LogGenericWarning(Strings.ErrorNoBotsDefined);
			}
		}

		internal static void InitEvents() {
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

			Bot.RegisterBot(botName);
		}

		private static bool IsValidBotName(string botName) {
			if (string.IsNullOrEmpty(botName)) {
				ArchiLogger.LogNullError(nameof(botName));
				return false;
			}

			if (botName[0] == '.') {
				return false;
			}

			switch (botName) {
				case SharedInfo.ASF:
				case "example":
				case "minimal":
					return false;
				default:
					return true;
			}
		}

		private static bool IsValidVersion(string version) {
			if (string.IsNullOrEmpty(version)) {
				ArchiLogger.LogNullError(nameof(version));
				return false;
			}

			switch (version) {
				case DefaultVersion:
				case "generic":
				case "linux-arm":
				case "linux-x64":
				case "osx-x64":
				case "win-x64":
					return true;
				default:
					return false;
			}
		}

		private static async void OnChanged(object sender, FileSystemEventArgs e) {
			if ((sender == null) || (e == null)) {
				ArchiLogger.LogNullError(nameof(sender) + " || " + nameof(e));
				return;
			}

			string botName = Path.GetFileNameWithoutExtension(e.Name);
			if (string.IsNullOrEmpty(botName) || (botName[0] == '.')) {
				return;
			}

			if (botName.Equals(SharedInfo.ASF)) {
				ArchiLogger.LogGenericInfo(Strings.GlobalConfigChanged);
				await RestartOrExit().ConfigureAwait(false);
				return;
			}

			if (!Bot.Bots.TryGetValue(botName, out Bot bot)) {
				return;
			}

			DateTime lastWriteTime = File.GetLastWriteTime(e.FullPath);

			if (LastWriteTimes.TryGetValue(bot, out DateTime savedLastWriteTime)) {
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

			await bot.OnNewConfigLoaded(new BotConfigEventArgs(BotConfig.Load(e.FullPath))).ConfigureAwait(false);
		}

		private static async void OnCreated(object sender, FileSystemEventArgs e) {
			if ((sender == null) || (e == null)) {
				ArchiLogger.LogNullError(nameof(sender) + " || " + nameof(e));
				return;
			}

			string botName = Path.GetFileNameWithoutExtension(e.Name);
			if (string.IsNullOrEmpty(botName) || (botName[0] == '.')) {
				return;
			}

			if (botName.Equals(SharedInfo.ASF)) {
				ArchiLogger.LogGenericInfo(Strings.GlobalConfigChanged);
				await RestartOrExit().ConfigureAwait(false);
				return;
			}

			await CreateBot(botName).ConfigureAwait(false);
		}

		private static async void OnDeleted(object sender, FileSystemEventArgs e) {
			if ((sender == null) || (e == null)) {
				ArchiLogger.LogNullError(nameof(sender) + " || " + nameof(e));
				return;
			}

			string botName = Path.GetFileNameWithoutExtension(e.Name);
			if (string.IsNullOrEmpty(botName)) {
				return;
			}

			if (botName.Equals(SharedInfo.ASF)) {
				// Some editors might decide to delete file and re-create it in order to modify it
				// If that's the case, we wait for maximum of 5 seconds before shutting down
				await Task.Delay(5000).ConfigureAwait(false);
				if (File.Exists(e.FullPath)) {
					return;
				}

				ArchiLogger.LogGenericError(Strings.ErrorGlobalConfigRemoved);
				await Program.Exit(1).ConfigureAwait(false);
				return;
			}

			if (Bot.Bots.TryGetValue(botName, out Bot bot)) {
				await bot.OnNewConfigLoaded(new BotConfigEventArgs()).ConfigureAwait(false);
			}
		}

		private static async void OnRenamed(object sender, RenamedEventArgs e) {
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
				await Program.Exit(1).ConfigureAwait(false);
				return;
			}

			if (Bot.Bots.TryGetValue(oldBotName, out Bot bot)) {
				await bot.OnNewConfigLoaded(new BotConfigEventArgs()).ConfigureAwait(false);
			}

			string newBotName = Path.GetFileNameWithoutExtension(e.Name);
			if (string.IsNullOrEmpty(newBotName) || (newBotName[0] == '.')) {
				return;
			}

			await CreateBot(newBotName).ConfigureAwait(false);
		}

		private static async Task RestartOrExit() {
			if (Program.GlobalConfig.AutoRestart) {
				ArchiLogger.LogGenericInfo(Strings.Restarting);
				await Task.Delay(5000).ConfigureAwait(false);
				await Program.Restart().ConfigureAwait(false);
			} else {
				ArchiLogger.LogGenericInfo(Strings.Exiting);
				await Task.Delay(5000).ConfigureAwait(false);
				await Program.Exit().ConfigureAwait(false);
			}
		}

		private static void UpdateFromArchive(ZipArchive archive, string targetDirectory) {
			if ((archive == null) || string.IsNullOrEmpty(targetDirectory)) {
				ArchiLogger.LogNullError(nameof(archive) + " || " + nameof(targetDirectory));
				return;
			}

			string backupDirectory = Path.Combine(targetDirectory, SharedInfo.UpdateDirectory);
			Directory.CreateDirectory(backupDirectory);

			// Move top-level runtime in-use files to other directory
			// We must do it in order to not crash at later stage - all libraries/executables must keep original names
			foreach (string file in Directory.EnumerateFiles(targetDirectory)) {
				string target = Path.Combine(backupDirectory, Path.GetFileName(file));
				File.Move(file, target);
			}

			// In generic ASF variant there can also be "runtimes" directory in need of same approach
			string runtimesDirectory = Path.Combine(targetDirectory, "runtimes");
			if (Directory.Exists(runtimesDirectory)) {
				foreach (string file in Directory.EnumerateFiles(runtimesDirectory, "*", SearchOption.AllDirectories)) {
					string directory = Path.Combine(backupDirectory, Path.GetDirectoryName(Path.GetRelativePath(targetDirectory, file)));
					Directory.CreateDirectory(directory);

					string target = Path.Combine(directory, Path.GetFileName(file));
					File.Move(file, target);
				}
			}

			foreach (ZipArchiveEntry zipFile in archive.Entries) {
				string file = Path.Combine(targetDirectory, zipFile.FullName);
				string directory = Path.GetDirectoryName(file);

				if (!Directory.Exists(directory)) {
					Directory.CreateDirectory(directory);
				}

				if (string.IsNullOrEmpty(zipFile.Name) || zipFile.Name.Equals(SharedInfo.GlobalConfigFileName)) {
					continue;
				}

				string backupFile = file + ".old";

				if (File.Exists(file)) {
					// This is non-runtime file to be replaced, probably in use, move it away
					File.Move(file, backupFile);
				}

				zipFile.ExtractToFile(file);

				try {
					File.Delete(backupFile);
				} catch {
					// Ignored - that file is indeed in use, it will be deleted after restart
				}
			}
		}

		internal sealed class BotConfigEventArgs : EventArgs {
			internal readonly BotConfig BotConfig;

			internal BotConfigEventArgs(BotConfig botConfig = null) => BotConfig = botConfig;
		}

		internal enum EUserInputType : byte {
			Unknown,
			DeviceID,
			IPCHostname,
			Login,
			Password,
			SteamGuard,
			SteamParentalPIN,
			TwoFactorAuthentication
		}
	}
}