//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// 
// Copyright 2015-2018 Łukasz "JustArchi" Domeradzki
// Contact: JustArchi@JustArchi.net
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
// http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.NLog;

namespace ArchiSteamFarm {
	internal static class ASF {
		internal static readonly ArchiLogger ArchiLogger = new ArchiLogger(SharedInfo.ASF);

		private static readonly ConcurrentDictionary<string, object> LastWriteEvents = new ConcurrentDictionary<string, object>();
		private static readonly SemaphoreSlim UpdateSemaphore = new SemaphoreSlim(1, 1);

		private static Timer AutoUpdatesTimer;
		private static FileSystemWatcher FileSystemWatcher;

		internal static async Task InitBots() {
			if (Bot.Bots.Count != 0) {
				return;
			}

			// Before attempting to connect, initialize our configuration
			await Bot.InitializeSteamConfiguration(Program.GlobalConfig.SteamProtocols, Program.GlobalDatabase.CellID, Program.GlobalDatabase.ServerListProvider).ConfigureAwait(false);

			try {
				await Utilities.InParallel(Directory.EnumerateFiles(SharedInfo.ConfigDirectory, "*" + SharedInfo.ConfigExtension).Select(Path.GetFileNameWithoutExtension).Where(botName => !string.IsNullOrEmpty(botName) && IsValidBotName(botName)).OrderBy(botName => botName).Select(Bot.RegisterBot)).ConfigureAwait(false);
			} catch (Exception e) {
				ArchiLogger.LogGenericException(e);
				return;
			}

			if (Bot.Bots.Count == 0) {
				ArchiLogger.LogGenericWarning(Strings.ErrorNoBotsDefined);
			}
		}

		internal static void InitEvents() {
			if (FileSystemWatcher != null) {
				return;
			}

			FileSystemWatcher = new FileSystemWatcher(SharedInfo.ConfigDirectory) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };

			FileSystemWatcher.Changed += OnChanged;
			FileSystemWatcher.Created += OnCreated;
			FileSystemWatcher.Deleted += OnDeleted;
			FileSystemWatcher.Renamed += OnRenamed;

			FileSystemWatcher.EnableRaisingEvents = true;
		}

		internal static bool IsOwner(ulong steamID) {
			if (steamID == 0) {
				ArchiLogger.LogNullError(nameof(steamID));
				return false;
			}

			return (steamID == Program.GlobalConfig.SteamOwnerID) || (Debugging.IsDebugBuild && (steamID == SharedInfo.ArchiSteamID));
		}

		internal static async Task RestartOrExit() {
			if (Program.RestartAllowed && Program.GlobalConfig.AutoRestart) {
				ArchiLogger.LogGenericInfo(Strings.Restarting);
				await Task.Delay(5000).ConfigureAwait(false);
				await Program.Restart().ConfigureAwait(false);
			} else {
				ArchiLogger.LogGenericInfo(Strings.Exiting);
				await Task.Delay(5000).ConfigureAwait(false);
				await Program.Exit().ConfigureAwait(false);
			}
		}

		internal static async Task<Version> Update(bool updateOverride = false) {
			if (!SharedInfo.BuildInfo.CanUpdate || (Program.GlobalConfig.UpdateChannel == GlobalConfig.EUpdateChannel.None)) {
				return null;
			}

			await UpdateSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				ArchiLogger.LogGenericInfo(Strings.UpdateCheckingNewVersion);

				// If backup directory from previous update exists, it's a good idea to purge it now
				string backupDirectory = Path.Combine(SharedInfo.HomeDirectory, SharedInfo.UpdateDirectory);
				if (Directory.Exists(backupDirectory)) {
					// It's entirely possible that old process is still running, wait a short moment for eventual cleanup
					await Task.Delay(5000).ConfigureAwait(false);

					try {
						Directory.Delete(backupDirectory, true);
					} catch (Exception e) {
						ArchiLogger.LogGenericException(e);
						return null;
					}
				}

				// TODO: cleanup from previous update, remove this after next stable
				try {
					foreach (string file in Directory.EnumerateFiles(SharedInfo.HomeDirectory, "*.old", SearchOption.AllDirectories)) {
						File.Delete(file);
					}
				} catch (Exception e) {
					ArchiLogger.LogGenericException(e);
					return null;
				}

				GitHub.ReleaseResponse releaseResponse = await GitHub.GetLatestRelease(Program.GlobalConfig.UpdateChannel == GlobalConfig.EUpdateChannel.Stable).ConfigureAwait(false);
				if (releaseResponse == null) {
					ArchiLogger.LogGenericWarning(Strings.ErrorUpdateCheckFailed);
					return null;
				}

				if (string.IsNullOrEmpty(releaseResponse.Tag)) {
					ArchiLogger.LogGenericWarning(Strings.ErrorUpdateCheckFailed);
					return null;
				}

				Version newVersion = new Version(releaseResponse.Tag);

				ArchiLogger.LogGenericInfo(string.Format(Strings.UpdateVersionInfo, SharedInfo.Version, newVersion));

				if (SharedInfo.Version == newVersion) {
					return SharedInfo.Version;
				}

				if (SharedInfo.Version > newVersion) {
					ArchiLogger.LogGenericWarning(Strings.WarningPreReleaseVersion);
					await Task.Delay(15 * 1000).ConfigureAwait(false);
					return SharedInfo.Version;
				}

				if (!updateOverride && (Program.GlobalConfig.UpdatePeriod == 0)) {
					ArchiLogger.LogGenericInfo(Strings.UpdateNewVersionAvailable);
					await Task.Delay(5000).ConfigureAwait(false);
					return null;
				}

				// Auto update logic starts here
				if (releaseResponse.Assets == null) {
					ArchiLogger.LogGenericWarning(Strings.ErrorUpdateNoAssets);
					return null;
				}

				string targetFile = SharedInfo.ASF + "-" + SharedInfo.BuildInfo.Variant + ".zip";
				GitHub.ReleaseResponse.Asset binaryAsset = releaseResponse.Assets.FirstOrDefault(asset => asset.Name.Equals(targetFile, StringComparison.OrdinalIgnoreCase));

				if (binaryAsset == null) {
					ArchiLogger.LogGenericWarning(Strings.ErrorUpdateNoAssetForThisVersion);
					return null;
				}

				if (string.IsNullOrEmpty(binaryAsset.DownloadURL)) {
					ArchiLogger.LogNullError(nameof(binaryAsset.DownloadURL));
					return null;
				}

				if (!string.IsNullOrEmpty(releaseResponse.ChangelogPlainText)) {
					ArchiLogger.LogGenericInfo(releaseResponse.ChangelogPlainText);
				}

				ArchiLogger.LogGenericInfo(string.Format(Strings.UpdateDownloadingNewVersion, newVersion, binaryAsset.Size / 1024 / 1024));

				WebBrowser.BinaryResponse response = await Program.WebBrowser.UrlGetToBinaryWithProgress(binaryAsset.DownloadURL).ConfigureAwait(false);
				if (response?.Content == null) {
					return null;
				}

				try {
					using (ZipArchive zipArchive = new ZipArchive(new MemoryStream(response.Content))) {
						if (!UpdateFromArchive(zipArchive, SharedInfo.HomeDirectory)) {
							ArchiLogger.LogGenericError(Strings.WarningFailed);
						}
					}
				} catch (Exception e) {
					ArchiLogger.LogGenericException(e);
					return null;
				}

				if (OS.IsUnix) {
					string executable = Path.Combine(SharedInfo.HomeDirectory, SharedInfo.AssemblyName);
					if (File.Exists(executable)) {
						OS.UnixSetFileAccessExecutable(executable);
					}
				}

				ArchiLogger.LogGenericInfo(Strings.UpdateFinished);
				return newVersion;
			} finally {
				UpdateSemaphore.Release();
			}
		}

		internal static async Task UpdateAndRestart() {
			if (!SharedInfo.BuildInfo.CanUpdate || (Program.GlobalConfig.UpdateChannel == GlobalConfig.EUpdateChannel.None)) {
				return;
			}

			if ((AutoUpdatesTimer == null) && (Program.GlobalConfig.UpdatePeriod > 0)) {
				TimeSpan autoUpdatePeriod = TimeSpan.FromHours(Program.GlobalConfig.UpdatePeriod);

				AutoUpdatesTimer = new Timer(
					async e => await UpdateAndRestart().ConfigureAwait(false),
					null,
					autoUpdatePeriod, // Delay
					autoUpdatePeriod // Period
				);

				ArchiLogger.LogGenericInfo(string.Format(Strings.AutoUpdateCheckInfo, autoUpdatePeriod.ToHumanReadable()));
			}

			Version newVersion = await Update().ConfigureAwait(false);
			if ((newVersion == null) || (newVersion <= SharedInfo.Version)) {
				return;
			}

			await RestartOrExit().ConfigureAwait(false);
		}

		private static async Task<bool> CanHandleWriteEvent(string name) {
			if (string.IsNullOrEmpty(name)) {
				ArchiLogger.LogNullError(nameof(name));
				return false;
			}

			// Save our event in dictionary
			object currentWriteEvent = new object();
			LastWriteEvents[name] = currentWriteEvent;

			// Wait a second for eventual other events to arrive
			await Task.Delay(1000).ConfigureAwait(false);

			// We're allowed to handle this event if the one that is saved after full second is our event and we succeed in clearing it (we don't care what we're clearing anymore, it doesn't have to be atomic operation)
			return LastWriteEvents.TryGetValue(name, out object savedWriteEvent) && (currentWriteEvent == savedWriteEvent) && LastWriteEvents.TryRemove(name, out _);
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
					return false;
				default:
					return true;
			}
		}

		private static async void OnChanged(object sender, FileSystemEventArgs e) {
			if ((sender == null) || (e == null)) {
				ArchiLogger.LogNullError(nameof(sender) + " || " + nameof(e));
				return;
			}

			await OnChangedFile(e.Name, e.FullPath).ConfigureAwait(false);
		}

		private static async Task OnChangedConfigFile(string name, string fullPath) {
			if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(fullPath)) {
				ArchiLogger.LogNullError(nameof(name) + " || " + nameof(fullPath));
				return;
			}

			await OnCreatedConfigFile(name, fullPath).ConfigureAwait(false);
		}

		private static async Task OnChangedFile(string name, string fullPath) {
			string extension = Path.GetExtension(name);

			switch (extension) {
				case SharedInfo.ConfigExtension:
					await OnChangedConfigFile(name, fullPath).ConfigureAwait(false);
					break;
				case SharedInfo.KeysExtension:
					await OnChangedKeysFile(name, fullPath).ConfigureAwait(false);
					break;
			}
		}

		private static async Task OnChangedKeysFile(string name, string fullPath) {
			if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(fullPath)) {
				ArchiLogger.LogNullError(nameof(name) + " || " + nameof(fullPath));
				return;
			}

			await OnCreatedKeysFile(name, fullPath).ConfigureAwait(false);
		}

		private static async void OnCreated(object sender, FileSystemEventArgs e) {
			if ((sender == null) || (e == null)) {
				ArchiLogger.LogNullError(nameof(sender) + " || " + nameof(e));
				return;
			}

			await OnCreatedFile(e.Name, e.FullPath).ConfigureAwait(false);
		}

		private static async Task OnCreatedConfigFile(string name, string fullPath) {
			if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(fullPath)) {
				ArchiLogger.LogNullError(nameof(name) + " || " + nameof(fullPath));
				return;
			}

			string botName = Path.GetFileNameWithoutExtension(name);
			if (string.IsNullOrEmpty(botName) || (botName[0] == '.')) {
				return;
			}

			if (!await CanHandleWriteEvent(name).ConfigureAwait(false)) {
				return;
			}

			if (botName.Equals(SharedInfo.ASF)) {
				ArchiLogger.LogGenericInfo(Strings.GlobalConfigChanged);
				await RestartOrExit().ConfigureAwait(false);
				return;
			}

			if (!IsValidBotName(botName)) {
				return;
			}

			if (Bot.Bots.TryGetValue(botName, out Bot bot)) {
				await bot.OnConfigChanged(false).ConfigureAwait(false);
			} else {
				await Bot.RegisterBot(botName).ConfigureAwait(false);
			}
		}

		private static async Task OnCreatedFile(string name, string fullPath) {
			if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(fullPath)) {
				ArchiLogger.LogNullError(nameof(name) + " || " + nameof(fullPath));
				return;
			}

			string extension = Path.GetExtension(name);

			switch (extension) {
				case SharedInfo.ConfigExtension:
					await OnCreatedConfigFile(name, fullPath).ConfigureAwait(false);
					break;
				case SharedInfo.KeysExtension:
					await OnCreatedKeysFile(name, fullPath).ConfigureAwait(false);
					break;
			}
		}

		private static async Task OnCreatedKeysFile(string name, string fullPath) {
			if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(fullPath)) {
				ArchiLogger.LogNullError(nameof(name) + " || " + nameof(fullPath));
				return;
			}

			string botName = Path.GetFileNameWithoutExtension(name);
			if (string.IsNullOrEmpty(botName) || (botName[0] == '.')) {
				return;
			}

			if (!await CanHandleWriteEvent(name).ConfigureAwait(false)) {
				return;
			}

			if (!Bot.Bots.TryGetValue(botName, out Bot bot)) {
				return;
			}

			await bot.ImportKeysToRedeem(fullPath).ConfigureAwait(false);
		}

		private static async void OnDeleted(object sender, FileSystemEventArgs e) {
			if ((sender == null) || (e == null)) {
				ArchiLogger.LogNullError(nameof(sender) + " || " + nameof(e));
				return;
			}

			await OnDeletedFile(e.Name, e.FullPath).ConfigureAwait(false);
		}

		private static async Task OnDeletedConfigFile(string name, string fullPath) {
			if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(fullPath)) {
				ArchiLogger.LogNullError(nameof(name) + " || " + nameof(fullPath));
				return;
			}

			string botName = Path.GetFileNameWithoutExtension(name);
			if (string.IsNullOrEmpty(botName)) {
				return;
			}

			if (!await CanHandleWriteEvent(name).ConfigureAwait(false)) {
				return;
			}

			if (botName.Equals(SharedInfo.ASF)) {
				if (File.Exists(fullPath)) {
					return;
				}

				// Some editors might decide to delete file and re-create it in order to modify it
				// If that's the case, we wait for maximum of 5 seconds before shutting down
				await Task.Delay(5000).ConfigureAwait(false);
				if (File.Exists(fullPath)) {
					return;
				}

				ArchiLogger.LogGenericError(Strings.ErrorGlobalConfigRemoved);
				await Program.Exit(1).ConfigureAwait(false);
				return;
			}

			if (!IsValidBotName(botName)) {
				return;
			}

			if (Bot.Bots.TryGetValue(botName, out Bot bot)) {
				await bot.OnConfigChanged(true).ConfigureAwait(false);
			}
		}

		private static async Task OnDeletedFile(string name, string fullPath) {
			if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(fullPath)) {
				ArchiLogger.LogNullError(nameof(name) + " || " + nameof(fullPath));
				return;
			}

			string extension = Path.GetExtension(name);

			switch (extension) {
				case SharedInfo.ConfigExtension:
					await OnDeletedConfigFile(name, fullPath).ConfigureAwait(false);
					break;
			}
		}

		private static async void OnRenamed(object sender, RenamedEventArgs e) {
			if ((sender == null) || (e == null)) {
				ArchiLogger.LogNullError(nameof(sender) + " || " + nameof(e));
				return;
			}

			await OnDeletedFile(e.OldName, e.OldFullPath).ConfigureAwait(false);
			await OnCreatedFile(e.Name, e.FullPath).ConfigureAwait(false);
		}

		private static bool UpdateFromArchive(ZipArchive archive, string targetDirectory) {
			if ((archive == null) || string.IsNullOrEmpty(targetDirectory)) {
				ArchiLogger.LogNullError(nameof(archive) + " || " + nameof(targetDirectory));
				return false;
			}

			// Firstly we'll move all our existing files to a backup directory
			string backupDirectory = Path.Combine(targetDirectory, SharedInfo.UpdateDirectory);

			// We can't use EnumerateFiles here as we're going to actively move them
			foreach (string file in Directory.GetFiles(targetDirectory, "*", SearchOption.AllDirectories)) {
				string fileName = Path.GetFileName(file);

				if (string.IsNullOrEmpty(fileName)) {
					ArchiLogger.LogNullError(nameof(fileName));
					return false;
				}

				string relativeFilePath = RuntimeCompatibility.Path.GetRelativePath(targetDirectory, file);

				if (string.IsNullOrEmpty(relativeFilePath)) {
					ArchiLogger.LogNullError(nameof(relativeFilePath));
					return false;
				}

				string relativeDirectoryName = Path.GetDirectoryName(relativeFilePath);

				switch (relativeDirectoryName) {
					// Files in those directories we want to keep in their current place
					case SharedInfo.ConfigDirectory:
						continue;
					case "":
						switch (fileName) {
							// Files with those names in root directory we want to keep
							case SharedInfo.LogFile:
							case "NLog.config":
								continue;
						}

						break;
					case null:
						ArchiLogger.LogNullError(nameof(relativeDirectoryName));
						return false;
				}

				string targetBackupDirectory = relativeDirectoryName.Length > 0 ? Path.Combine(backupDirectory, relativeDirectoryName) : backupDirectory;
				Directory.CreateDirectory(targetBackupDirectory);

				string targetBackupFile = Path.Combine(targetBackupDirectory, fileName);
				File.Move(file, targetBackupFile);
			}

			// We can now get rid of directories that are empty
			foreach (string directory in Directory.EnumerateDirectories(targetDirectory).Where(directory => !Directory.EnumerateFiles(directory).Any())) {
				Directory.Delete(directory, true);
			}

			// Now enumerate over files in the zip archive, skip directory entries that we're not interested in (we can create them ourselves if needed)
			foreach (ZipArchiveEntry zipFile in archive.Entries.Where(zipFile => !string.IsNullOrEmpty(zipFile.Name))) {
				string file = Path.Combine(targetDirectory, zipFile.FullName);

				if (File.Exists(file)) {
					// This is possible only with files that we decided to leave in place during our backup function
					// Those files should never be overwritten with anything, ignore
					continue;
				}

				string directory = Path.GetDirectoryName(file);

				if (string.IsNullOrEmpty(directory)) {
					ArchiLogger.LogNullError(nameof(directory));
					return false;
				}

				if (!Directory.Exists(directory)) {
					Directory.CreateDirectory(directory);
				}

				zipFile.ExtractToFile(file);
			}

			return true;
		}

		internal enum EUserInputType : byte {
			Unknown,
			DeviceID,
			Login,
			Password,
			SteamGuard,
			SteamParentalCode,
			TwoFactorAuthentication
		}
	}
}
