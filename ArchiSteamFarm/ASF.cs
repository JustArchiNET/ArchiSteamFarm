//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2020 Łukasz "JustArchi" Domeradzki
// Contact: JustArchi@JustArchi.net
// |
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// |
// http://www.apache.org/licenses/LICENSE-2.0
// |
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Helpers;
using ArchiSteamFarm.IPC;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.NLog;
using ArchiSteamFarm.Plugins;
using JetBrains.Annotations;
using SteamKit2;
using SteamKit2.Discovery;

namespace ArchiSteamFarm {
	public static class ASF {
		// This is based on internal Valve guidelines, we're not using it as a hard limit
		private const byte MaximumRecommendedBotsCount = 10;

		[PublicAPI]
		public static readonly ArchiLogger ArchiLogger = new ArchiLogger(SharedInfo.ASF);

		[PublicAPI]
		public static byte LoadBalancingDelay => Math.Max(GlobalConfig?.LoginLimiterDelay ?? 0, GlobalConfig.DefaultLoginLimiterDelay);

		[PublicAPI]
		public static GlobalConfig? GlobalConfig { get; private set; }

		[PublicAPI]
		public static GlobalDatabase? GlobalDatabase { get; private set; }

		[PublicAPI]
		public static WebBrowser? WebBrowser { get; internal set; }

		internal static ICrossProcessSemaphore? ConfirmationsSemaphore { get; private set; }
		internal static ICrossProcessSemaphore? GiftsSemaphore { get; private set; }
		internal static ICrossProcessSemaphore? InventorySemaphore { get; private set; }
		internal static ICrossProcessSemaphore? LoginRateLimitingSemaphore { get; private set; }
		internal static ICrossProcessSemaphore? LoginSemaphore { get; private set; }
		internal static ImmutableDictionary<string, (ICrossProcessSemaphore RateLimitingSemaphore, SemaphoreSlim OpenConnectionsSemaphore)>? WebLimitingSemaphores { get; private set; }

		private static readonly SemaphoreSlim UpdateSemaphore = new SemaphoreSlim(1, 1);

		private static Timer? AutoUpdatesTimer;
		private static FileSystemWatcher? FileSystemWatcher;
		private static ConcurrentDictionary<string, object>? LastWriteEvents;

		[PublicAPI]
		public static bool IsOwner(ulong steamID) {
			if (steamID == 0) {
				throw new ArgumentNullException(nameof(steamID));
			}

			return (steamID == GlobalConfig?.SteamOwnerID) || (Debugging.IsDebugBuild && (steamID == SharedInfo.ArchiSteamID));
		}

		internal static string GetFilePath(EFileType fileType) {
			if (!Enum.IsDefined(typeof(EFileType), fileType)) {
				throw new ArgumentNullException(nameof(fileType));
			}

			return fileType switch {
				EFileType.Config => Path.Combine(SharedInfo.ConfigDirectory, SharedInfo.GlobalConfigFileName),
				EFileType.Database => Path.Combine(SharedInfo.ConfigDirectory, SharedInfo.GlobalDatabaseFileName),
				_ => throw new ArgumentOutOfRangeException(nameof(fileType))
			};
		}

		internal static async Task Init() {
			if (GlobalConfig == null) {
				throw new ArgumentNullException(nameof(GlobalConfig));
			}

			if (!PluginsCore.InitPlugins()) {
				await Task.Delay(10000).ConfigureAwait(false);
			}

			WebBrowser = new WebBrowser(ArchiLogger, GlobalConfig.WebProxy, true);

			await UpdateAndRestart().ConfigureAwait(false);

			await PluginsCore.OnASFInitModules(GlobalConfig.AdditionalProperties).ConfigureAwait(false);

			StringComparer botsComparer = await PluginsCore.GetBotsComparer().ConfigureAwait(false);

			InitBotsComparer(botsComparer);

			if (!GlobalConfig.Headless && !Console.IsInputRedirected) {
				Logging.StartInteractiveConsole();
			}

			if (GlobalConfig.IPC) {
				await ArchiKestrel.Start().ConfigureAwait(false);
			}

			uint changeNumberToStartFrom = await PluginsCore.GetChangeNumberToStartFrom().ConfigureAwait(false);

			SteamPICSChanges.Init(changeNumberToStartFrom);

			await RegisterBots().ConfigureAwait(false);

			InitEvents();
		}

		internal static void InitGlobalConfig(GlobalConfig globalConfig) {
			if (globalConfig == null) {
				throw new ArgumentNullException(nameof(globalConfig));
			}

			if (GlobalConfig != null) {
				return;
			}

			GlobalConfig = globalConfig;

			// The only purpose of using hashingAlgorithm below is to cut on a potential size of the resource name - paths can be really long, and we almost certainly have some upper limit on the resource name we can allocate
			// At the same time it'd be the best if we avoided all special characters, such as '/' found e.g. in base64, as we can't be sure that it's not a prohibited character in regards to native OS implementation
			// Because of that, MD5 is sufficient for our case, as it generates alphanumeric characters only, and is barely 128-bit long. We don't need any kind of complex cryptography or collision detection here, any hashing algorithm will do, and the shorter the better
			string networkGroupText = "";

			if (!string.IsNullOrEmpty(Program.NetworkGroup)) {
				using MD5 hashingAlgorithm = MD5.Create();

				networkGroupText = "-" + BitConverter.ToString(hashingAlgorithm.ComputeHash(Encoding.UTF8.GetBytes(Program.NetworkGroup!))).Replace("-", "");
			} else if (!string.IsNullOrEmpty(globalConfig.WebProxyText)) {
				using MD5 hashingAlgorithm = MD5.Create();

				networkGroupText = "-" + BitConverter.ToString(hashingAlgorithm.ComputeHash(Encoding.UTF8.GetBytes(globalConfig.WebProxyText!))).Replace("-", "");
			}

			ConfirmationsSemaphore ??= OS.CreateCrossProcessSemaphore(nameof(ConfirmationsSemaphore) + networkGroupText);
			GiftsSemaphore ??= OS.CreateCrossProcessSemaphore(nameof(GiftsSemaphore) + networkGroupText);
			InventorySemaphore ??= OS.CreateCrossProcessSemaphore(nameof(InventorySemaphore) + networkGroupText);
			LoginRateLimitingSemaphore ??= OS.CreateCrossProcessSemaphore(nameof(LoginRateLimitingSemaphore) + networkGroupText);
			LoginSemaphore ??= OS.CreateCrossProcessSemaphore(nameof(LoginSemaphore) + networkGroupText);

			WebLimitingSemaphores ??= new Dictionary<string, (ICrossProcessSemaphore RateLimitingSemaphore, SemaphoreSlim OpenConnectionsSemaphore)>(4, StringComparer.OrdinalIgnoreCase) {
				{ nameof(ArchiWebHandler), (OS.CreateCrossProcessSemaphore(nameof(ArchiWebHandler) + networkGroupText), new SemaphoreSlim(WebBrowser.MaxConnections, WebBrowser.MaxConnections)) },
				{ ArchiWebHandler.SteamCommunityURL, (OS.CreateCrossProcessSemaphore(nameof(ArchiWebHandler) + networkGroupText + "-" + nameof(ArchiWebHandler.SteamCommunityURL)), new SemaphoreSlim(WebBrowser.MaxConnections, WebBrowser.MaxConnections)) },
				{ ArchiWebHandler.SteamHelpURL, (OS.CreateCrossProcessSemaphore(nameof(ArchiWebHandler) + networkGroupText + "-" + nameof(ArchiWebHandler.SteamHelpURL)), new SemaphoreSlim(WebBrowser.MaxConnections, WebBrowser.MaxConnections)) },
				{ ArchiWebHandler.SteamStoreURL, (OS.CreateCrossProcessSemaphore(nameof(ArchiWebHandler) + networkGroupText + "-" + nameof(ArchiWebHandler.SteamStoreURL)), new SemaphoreSlim(WebBrowser.MaxConnections, WebBrowser.MaxConnections)) },
				{ WebAPI.DefaultBaseAddress.Host, (OS.CreateCrossProcessSemaphore(nameof(ArchiWebHandler) + networkGroupText + "-" + nameof(WebAPI)), new SemaphoreSlim(WebBrowser.MaxConnections, WebBrowser.MaxConnections)) }
			}.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);
		}

		internal static void InitGlobalDatabase(GlobalDatabase globalDatabase) {
			if (globalDatabase == null) {
				throw new ArgumentNullException(nameof(globalDatabase));
			}

			if (GlobalDatabase != null) {
				return;
			}

			GlobalDatabase = globalDatabase;
		}

		internal static async Task RestartOrExit() {
			if (GlobalConfig == null) {
				throw new ArgumentNullException(nameof(GlobalConfig));
			}

			if (Program.RestartAllowed && GlobalConfig.AutoRestart) {
				ArchiLogger.LogGenericInfo(Strings.Restarting);
				await Task.Delay(5000).ConfigureAwait(false);
				await Program.Restart().ConfigureAwait(false);
			} else {
				ArchiLogger.LogGenericInfo(Strings.Exiting);
				await Task.Delay(5000).ConfigureAwait(false);
				await Program.Exit().ConfigureAwait(false);
			}
		}

		internal static async Task<Version?> Update(bool updateOverride = false) {
			if ((GlobalConfig == null) || (WebBrowser == null)) {
				throw new ArgumentNullException(nameof(GlobalConfig) + " || " + nameof(WebBrowser));
			}

			if (!SharedInfo.BuildInfo.CanUpdate || (GlobalConfig.UpdateChannel == GlobalConfig.EUpdateChannel.None)) {
				return null;
			}

			await UpdateSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				// If backup directory from previous update exists, it's a good idea to purge it now
				string backupDirectory = Path.Combine(SharedInfo.HomeDirectory, SharedInfo.UpdateDirectory);

				if (Directory.Exists(backupDirectory)) {
					ArchiLogger.LogGenericInfo(Strings.UpdateCleanup);

					// It's entirely possible that old process is still running, wait a short moment for eventual cleanup
					await Task.Delay(5000).ConfigureAwait(false);

					try {
						Directory.Delete(backupDirectory, true);
					} catch (Exception e) {
						ArchiLogger.LogGenericException(e);

						return null;
					}

					ArchiLogger.LogGenericInfo(Strings.Done);
				}

				ArchiLogger.LogGenericInfo(Strings.UpdateCheckingNewVersion);

				GitHub.ReleaseResponse? releaseResponse = await GitHub.GetLatestRelease(GlobalConfig.UpdateChannel == GlobalConfig.EUpdateChannel.Stable).ConfigureAwait(false);

				if (releaseResponse == null) {
					ArchiLogger.LogGenericWarning(Strings.ErrorUpdateCheckFailed);

					return null;
				}

				if (string.IsNullOrEmpty(releaseResponse.Tag)) {
					ArchiLogger.LogGenericWarning(Strings.ErrorUpdateCheckFailed);

					return null;
				}

				Version newVersion = new Version(releaseResponse.Tag!);

				ArchiLogger.LogGenericInfo(string.Format(Strings.UpdateVersionInfo, SharedInfo.Version, newVersion));

				if (SharedInfo.Version >= newVersion) {
					if (SharedInfo.Version > newVersion) {
						ArchiLogger.LogGenericWarning(Strings.WarningPreReleaseVersion);
						await Task.Delay(15 * 1000).ConfigureAwait(false);
					}

					return newVersion;
				}

				if (!updateOverride && (GlobalConfig.UpdatePeriod == 0)) {
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
				GitHub.ReleaseResponse.Asset? binaryAsset = releaseResponse.Assets.FirstOrDefault(asset => !string.IsNullOrEmpty(asset.Name) && asset.Name!.Equals(targetFile, StringComparison.OrdinalIgnoreCase));

				if (binaryAsset == null) {
					ArchiLogger.LogGenericWarning(Strings.ErrorUpdateNoAssetForThisVersion);

					return null;
				}

				if (string.IsNullOrEmpty(binaryAsset.DownloadURL)) {
					ArchiLogger.LogNullError(nameof(binaryAsset.DownloadURL));

					return null;
				}

				if (!string.IsNullOrEmpty(releaseResponse.ChangelogPlainText)) {
					ArchiLogger.LogGenericInfo(releaseResponse.ChangelogPlainText!);
				}

				ArchiLogger.LogGenericInfo(string.Format(Strings.UpdateDownloadingNewVersion, newVersion, binaryAsset.Size / 1024 / 1024));

				Progress<byte> progressReporter = new Progress<byte>();

				progressReporter.ProgressChanged += OnProgressChanged;

				WebBrowser.BinaryResponse? response;

				try {
					response = await WebBrowser.UrlGetToBinary(binaryAsset.DownloadURL!, progressReporter: progressReporter).ConfigureAwait(false);
				} finally {
					progressReporter.ProgressChanged -= OnProgressChanged;
				}

				if (response?.Content == null) {
					return null;
				}

				try {
					// We disable ArchiKestrel here as the update process moves the core files and might result in IPC crash
					// TODO: It might fail if the update was triggered from the API, this should be something to improve in the future, by changing the structure into request -> return response -> finish update
					await ArchiKestrel.Stop().ConfigureAwait(false);
				} catch (Exception e) {
					ArchiLogger.LogGenericWarningException(e);
				}

				try {
#if NETFRAMEWORK
					using MemoryStream memoryStream = new MemoryStream(response.Content);
#else
					await using MemoryStream memoryStream = new MemoryStream(response.Content);
#endif

					using ZipArchive zipArchive = new ZipArchive(memoryStream);

					if (!UpdateFromArchive(zipArchive, SharedInfo.HomeDirectory)) {
						ArchiLogger.LogGenericError(Strings.WarningFailed);
					}
				} catch (Exception e) {
					ArchiLogger.LogGenericException(e);

					return null;
				}

				if (OS.IsUnix) {
					string executable = Path.Combine(SharedInfo.HomeDirectory, SharedInfo.AssemblyName);

					if (File.Exists(executable)) {
						OS.UnixSetFileAccess(executable, OS.EUnixPermission.Combined755);
					}
				}

				ArchiLogger.LogGenericInfo(Strings.UpdateFinished);

				return newVersion;
			} finally {
				UpdateSemaphore.Release();
			}
		}

		private static async Task<bool> CanHandleWriteEvent(string filePath) {
			if (string.IsNullOrEmpty(filePath) || (LastWriteEvents == null)) {
				throw new ArgumentNullException(nameof(filePath) + " || " + nameof(LastWriteEvents));
			}

			// Save our event in dictionary
			object currentWriteEvent = new object();
			LastWriteEvents[filePath] = currentWriteEvent;

			// Wait a second for eventual other events to arrive
			await Task.Delay(1000).ConfigureAwait(false);

			// We're allowed to handle this event if the one that is saved after full second is our event and we succeed in clearing it (we don't care what we're clearing anymore, it doesn't have to be atomic operation)
			return LastWriteEvents.TryGetValue(filePath, out object? savedWriteEvent) && (currentWriteEvent == savedWriteEvent) && LastWriteEvents.TryRemove(filePath, out _);
		}

		private static void InitBotsComparer(StringComparer botsComparer) {
			if (botsComparer == null) {
				throw new ArgumentNullException(nameof(botsComparer));
			}

			if (Bot.Bots != null) {
				return;
			}

			Bot.Init(botsComparer);
		}

		private static void InitEvents() {
			if ((FileSystemWatcher != null) || (LastWriteEvents != null) || !Directory.Exists(SharedInfo.ConfigDirectory)) {
				return;
			}

			if (Bot.BotsComparer == null) {
				throw new ArgumentNullException(nameof(Bot.BotsComparer));
			}

			FileSystemWatcher = new FileSystemWatcher(SharedInfo.ConfigDirectory) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };

			FileSystemWatcher.Changed += OnChanged;
			FileSystemWatcher.Created += OnCreated;
			FileSystemWatcher.Deleted += OnDeleted;
			FileSystemWatcher.Renamed += OnRenamed;

			LastWriteEvents = new ConcurrentDictionary<string, object>(Bot.BotsComparer);

			FileSystemWatcher.EnableRaisingEvents = true;
		}

		private static bool IsValidBotName(string botName) {
			if (string.IsNullOrEmpty(botName)) {
				throw new ArgumentNullException(nameof(botName));
			}

			if (botName[0] == '.') {
				return false;
			}

			return !botName.Equals(SharedInfo.ASF, StringComparison.OrdinalIgnoreCase);
		}

		private static async void OnChanged(object sender, FileSystemEventArgs e) {
			if ((sender == null) || (e == null)) {
				throw new ArgumentNullException(nameof(sender) + " || " + nameof(e));
			}

			await OnChangedFile(e.Name, e.FullPath).ConfigureAwait(false);
		}

		private static async Task OnChangedConfigFile(string name, string fullPath) {
			if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(fullPath)) {
				throw new ArgumentNullException(nameof(name) + " || " + nameof(fullPath));
			}

			await OnCreatedConfigFile(name, fullPath).ConfigureAwait(false);
		}

		private static async Task OnChangedConfigFile(string name) {
			if (string.IsNullOrEmpty(name)) {
				throw new ArgumentNullException(nameof(name));
			}

			if (!name.Equals(SharedInfo.IPCConfigFile) || (GlobalConfig?.IPC != true)) {
				return;
			}

			if (!await CanHandleWriteEvent(name).ConfigureAwait(false)) {
				return;
			}

			ArchiLogger.LogGenericInfo(Strings.IPCConfigChanged);
			await ArchiKestrel.Stop().ConfigureAwait(false);
			await ArchiKestrel.Start().ConfigureAwait(false);
		}

		private static async Task OnChangedFile(string name, string fullPath) {
			if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(fullPath)) {
				throw new ArgumentNullException(nameof(name) + " || " + nameof(fullPath));
			}

			string extension = Path.GetExtension(name);

			switch (extension) {
				case SharedInfo.JsonConfigExtension:
				case SharedInfo.IPCConfigExtension:
					await OnChangedConfigFile(name, fullPath).ConfigureAwait(false);

					break;
				case SharedInfo.KeysExtension:
					await OnChangedKeysFile(name, fullPath).ConfigureAwait(false);

					break;
			}
		}

		private static async Task OnChangedKeysFile(string name, string fullPath) {
			if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(fullPath)) {
				throw new ArgumentNullException(nameof(name) + " || " + nameof(fullPath));
			}

			await OnCreatedKeysFile(name, fullPath).ConfigureAwait(false);
		}

		private static async void OnCreated(object sender, FileSystemEventArgs e) {
			if ((sender == null) || (e == null)) {
				throw new ArgumentNullException(nameof(sender) + " || " + nameof(e));
			}

			await OnCreatedFile(e.Name, e.FullPath).ConfigureAwait(false);
		}

		private static async Task OnCreatedConfigFile(string name, string fullPath) {
			if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(fullPath)) {
				throw new ArgumentNullException(nameof(name) + " || " + nameof(fullPath));
			}

			string extension = Path.GetExtension(name);

			switch (extension) {
				case SharedInfo.IPCConfigExtension:
					await OnChangedConfigFile(name).ConfigureAwait(false);

					break;
				case SharedInfo.JsonConfigExtension:
					await OnCreatedJsonFile(name, fullPath).ConfigureAwait(false);

					break;
			}
		}

		private static async Task OnCreatedFile(string name, string fullPath) {
			if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(fullPath)) {
				throw new ArgumentNullException(nameof(name) + " || " + nameof(fullPath));
			}

			string extension = Path.GetExtension(name);

			switch (extension) {
				case SharedInfo.JsonConfigExtension:
					await OnCreatedConfigFile(name, fullPath).ConfigureAwait(false);

					break;

				case SharedInfo.KeysExtension:
					await OnCreatedKeysFile(name, fullPath).ConfigureAwait(false);

					break;
			}
		}

		private static async Task OnCreatedJsonFile(string name, string fullPath) {
			if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(fullPath) || (Bot.Bots == null)) {
				throw new ArgumentNullException(nameof(name) + " || " + nameof(fullPath) + " || " + nameof(Bot.Bots));
			}

			string botName = Path.GetFileNameWithoutExtension(name);

			if (string.IsNullOrEmpty(botName) || (botName[0] == '.')) {
				return;
			}

			if (!await CanHandleWriteEvent(fullPath).ConfigureAwait(false)) {
				return;
			}

			if (botName.Equals(SharedInfo.ASF, StringComparison.OrdinalIgnoreCase)) {
				ArchiLogger.LogGenericInfo(Strings.GlobalConfigChanged);
				await RestartOrExit().ConfigureAwait(false);

				return;
			}

			if (!IsValidBotName(botName)) {
				return;
			}

			if (Bot.Bots.TryGetValue(botName, out Bot? bot)) {
				await bot.OnConfigChanged(false).ConfigureAwait(false);
			} else {
				await Bot.RegisterBot(botName).ConfigureAwait(false);

				if (Bot.Bots.Count > MaximumRecommendedBotsCount) {
					ArchiLogger.LogGenericWarning(string.Format(Strings.WarningExcessiveBotsCount, MaximumRecommendedBotsCount));
				}
			}
		}

		private static async Task OnCreatedKeysFile(string name, string fullPath) {
			if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(fullPath) || (Bot.Bots == null)) {
				throw new ArgumentNullException(nameof(name) + " || " + nameof(fullPath) + " || " + nameof(Bot.Bots));
			}

			string botName = Path.GetFileNameWithoutExtension(name);

			if (string.IsNullOrEmpty(botName) || (botName[0] == '.')) {
				return;
			}

			if (!await CanHandleWriteEvent(fullPath).ConfigureAwait(false)) {
				return;
			}

			if (!Bot.Bots.TryGetValue(botName, out Bot? bot)) {
				return;
			}

			await bot.ImportKeysToRedeem(fullPath).ConfigureAwait(false);
		}

		private static async void OnDeleted(object sender, FileSystemEventArgs e) {
			if ((sender == null) || (e == null)) {
				throw new ArgumentNullException(nameof(sender) + " || " + nameof(e));
			}

			await OnDeletedFile(e.Name, e.FullPath).ConfigureAwait(false);
		}

		private static async Task OnDeletedConfigFile(string name, string fullPath) {
			if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(fullPath)) {
				throw new ArgumentNullException(nameof(name) + " || " + nameof(fullPath));
			}

			string extension = Path.GetExtension(name);

			switch (extension) {
				case SharedInfo.IPCConfigExtension:
					await OnChangedConfigFile(name).ConfigureAwait(false);

					break;
				case SharedInfo.JsonConfigExtension:
					await OnDeletedJsonConfigFile(name, fullPath).ConfigureAwait(false);

					break;
			}
		}

		private static async Task OnDeletedFile(string name, string fullPath) {
			if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(fullPath)) {
				throw new ArgumentNullException(nameof(name) + " || " + nameof(fullPath));
			}

			string extension = Path.GetExtension(name);

			switch (extension) {
				case SharedInfo.JsonConfigExtension:
				case SharedInfo.IPCConfigExtension:
					await OnDeletedConfigFile(name, fullPath).ConfigureAwait(false);

					break;
			}
		}

		private static async Task OnDeletedJsonConfigFile(string name, string fullPath) {
			if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(fullPath) || (Bot.Bots == null)) {
				throw new ArgumentNullException(nameof(name) + " || " + nameof(fullPath) + " || " + nameof(Bot.Bots));
			}

			string botName = Path.GetFileNameWithoutExtension(name);

			if (string.IsNullOrEmpty(botName)) {
				return;
			}

			if (!await CanHandleWriteEvent(fullPath).ConfigureAwait(false)) {
				return;
			}

			if (botName.Equals(SharedInfo.ASF, StringComparison.OrdinalIgnoreCase)) {
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

			if (Bot.Bots.TryGetValue(botName, out Bot? bot)) {
				await bot.OnConfigChanged(true).ConfigureAwait(false);
			}
		}

		private static void OnProgressChanged(object? sender, byte progressPercentage) {
			const byte printEveryPercentage = 10;

			if (progressPercentage % printEveryPercentage != 0) {
				return;
			}

			ArchiLogger.LogGenericDebug($"{progressPercentage}%...");
		}

		private static async void OnRenamed(object sender, RenamedEventArgs e) {
			if ((sender == null) || (e == null)) {
				throw new ArgumentNullException(nameof(sender) + " || " + nameof(e));
			}

			await OnDeletedFile(e.OldName, e.OldFullPath).ConfigureAwait(false);
			await OnCreatedFile(e.Name, e.FullPath).ConfigureAwait(false);
		}

		private static async Task RegisterBots() {
			if ((GlobalConfig == null) || (GlobalDatabase == null) || (WebBrowser == null)) {
				throw new ArgumentNullException(nameof(GlobalConfig) + " || " + nameof(GlobalDatabase) + " || " + nameof(WebBrowser));
			}

			// Ensure that we ask for a list of servers if we don't have any saved servers available
			IEnumerable<ServerRecord> servers = await GlobalDatabase.ServerListProvider.FetchServerListAsync().ConfigureAwait(false);

			if (servers?.Any() != true) {
				ArchiLogger.LogGenericInfo(string.Format(Strings.Initializing, nameof(SteamDirectory)));

				SteamConfiguration steamConfiguration = SteamConfiguration.Create(builder => builder.WithProtocolTypes(GlobalConfig.SteamProtocols).WithCellID(GlobalDatabase.CellID).WithServerListProvider(GlobalDatabase.ServerListProvider).WithHttpClientFactory(() => WebBrowser.GenerateDisposableHttpClient()));

				try {
					await SteamDirectory.LoadAsync(steamConfiguration).ConfigureAwait(false);
					ArchiLogger.LogGenericInfo(Strings.Success);
				} catch (Exception e) {
					ArchiLogger.LogGenericWarningException(e);
					ArchiLogger.LogGenericWarning(Strings.BotSteamDirectoryInitializationFailed);

					await Task.Delay(5000).ConfigureAwait(false);
				}
			}

			HashSet<string> botNames;

			try {
				botNames = Directory.Exists(SharedInfo.ConfigDirectory) ? Directory.EnumerateFiles(SharedInfo.ConfigDirectory, "*" + SharedInfo.JsonConfigExtension).Select(Path.GetFileNameWithoutExtension).Where(botName => !string.IsNullOrEmpty(botName) && IsValidBotName(botName)).ToHashSet(Bot.BotsComparer)! : new HashSet<string>(0);
			} catch (Exception e) {
				ArchiLogger.LogGenericException(e);

				return;
			}

			if (botNames.Count == 0) {
				ArchiLogger.LogGenericWarning(Strings.ErrorNoBotsDefined);

				return;
			}

			if (botNames.Count > MaximumRecommendedBotsCount) {
				ArchiLogger.LogGenericWarning(string.Format(Strings.WarningExcessiveBotsCount, MaximumRecommendedBotsCount));
				await Task.Delay(10000).ConfigureAwait(false);
			}

			await Utilities.InParallel(botNames.OrderBy(botName => botName, Bot.BotsComparer).Select(Bot.RegisterBot)).ConfigureAwait(false);
		}

		private static async Task UpdateAndRestart() {
			if (GlobalConfig == null) {
				throw new ArgumentNullException(nameof(GlobalConfig));
			}

			if (!SharedInfo.BuildInfo.CanUpdate || (GlobalConfig.UpdateChannel == GlobalConfig.EUpdateChannel.None)) {
				return;
			}

			if ((AutoUpdatesTimer == null) && (GlobalConfig.UpdatePeriod > 0)) {
				TimeSpan autoUpdatePeriod = TimeSpan.FromHours(GlobalConfig.UpdatePeriod);

				AutoUpdatesTimer = new Timer(
					async e => await UpdateAndRestart().ConfigureAwait(false),
					null,
					autoUpdatePeriod, // Delay
					autoUpdatePeriod // Period
				);

				ArchiLogger.LogGenericInfo(string.Format(Strings.AutoUpdateCheckInfo, autoUpdatePeriod.ToHumanReadable()));
			}

			Version? newVersion = await Update().ConfigureAwait(false);

			if ((newVersion == null) || (newVersion <= SharedInfo.Version)) {
				return;
			}

			await RestartOrExit().ConfigureAwait(false);
		}

		private static bool UpdateFromArchive(ZipArchive archive, string targetDirectory) {
			if ((archive == null) || string.IsNullOrEmpty(targetDirectory)) {
				throw new ArgumentNullException(nameof(archive) + " || " + nameof(targetDirectory));
			}

			// Firstly we'll move all our existing files to a backup directory
			string backupDirectory = Path.Combine(targetDirectory, SharedInfo.UpdateDirectory);

			foreach (string file in Directory.EnumerateFiles(targetDirectory, "*", SearchOption.AllDirectories)) {
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

				string? relativeDirectoryName = Path.GetDirectoryName(relativeFilePath);

				switch (relativeDirectoryName) {
					case null:
						ArchiLogger.LogNullError(nameof(relativeDirectoryName));

						return false;
					case "":
						// No directory, root folder
						switch (fileName) {
							case Logging.NLogConfigurationFile:
							case SharedInfo.LogFile:
								// Files with those names in root directory we want to keep
								continue;
						}

						break;
					case SharedInfo.ArchivalLogsDirectory:
					case SharedInfo.ConfigDirectory:
					case SharedInfo.DebugDirectory:
					case SharedInfo.PluginsDirectory:
					case SharedInfo.UpdateDirectory:
						// Files in those directories we want to keep in their current place
						continue;
					default:
						// Files in subdirectories of those directories we want to keep as well
						if (Utilities.RelativeDirectoryStartsWith(relativeDirectoryName, SharedInfo.ArchivalLogsDirectory, SharedInfo.ConfigDirectory, SharedInfo.DebugDirectory, SharedInfo.PluginsDirectory, SharedInfo.UpdateDirectory)) {
							continue;
						}

						break;
				}

				string targetBackupDirectory = relativeDirectoryName.Length > 0 ? Path.Combine(backupDirectory, relativeDirectoryName) : backupDirectory;
				Directory.CreateDirectory(targetBackupDirectory);

				string targetBackupFile = Path.Combine(targetBackupDirectory, fileName);
				RuntimeCompatibility.File.Move(file, targetBackupFile, true);
			}

			// We can now get rid of directories that are empty
			Utilities.DeleteEmptyDirectoriesRecursively(targetDirectory);

			if (!Directory.Exists(targetDirectory)) {
				Directory.CreateDirectory(targetDirectory);
			}

			// Now enumerate over files in the zip archive, skip directory entries that we're not interested in (we can create them ourselves if needed)
			foreach (ZipArchiveEntry zipFile in archive.Entries.Where(zipFile => !string.IsNullOrEmpty(zipFile.Name))) {
				string file = Path.Combine(targetDirectory, zipFile.FullName);

				if (File.Exists(file)) {
					// This is possible only with files that we decided to leave in place during our backup function
					string targetBackupFile = file + ".bak";
					RuntimeCompatibility.File.Move(file, targetBackupFile, true);
				}

				// Check if this file requires its own folder
				if (zipFile.Name != zipFile.FullName) {
					string? directory = Path.GetDirectoryName(file);

					if (string.IsNullOrEmpty(directory)) {
						ArchiLogger.LogNullError(nameof(directory));

						return false;
					}

					if (!Directory.Exists(directory)) {
						Directory.CreateDirectory(directory!);
					}

					// We're not interested in extracting placeholder files (but we still want directories created for them, done above)
					switch (zipFile.Name) {
						case ".gitkeep":
							continue;
					}
				}

				zipFile.ExtractToFile(file);
			}

			return true;
		}

		[PublicAPI]
		public enum EUserInputType : byte {
			None,
			Login,
			Password,
			SteamGuard,
			SteamParentalCode,
			TwoFactorAuthentication
		}

		internal enum EFileType : byte {
			Config,
			Database
		}
	}
}
