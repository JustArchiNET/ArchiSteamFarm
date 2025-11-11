// ----------------------------------------------------------------------------------------------
//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// ----------------------------------------------------------------------------------------------
// |
// Copyright 2015-2025 Łukasz "JustArchi" Domeradzki
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
using System.Collections.Frozen;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Helpers;
using ArchiSteamFarm.IPC;
using ArchiSteamFarm.IPC.Controllers.Api;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.NLog;
using ArchiSteamFarm.Plugins;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Integration;
using ArchiSteamFarm.Storage;
using ArchiSteamFarm.Web;
using ArchiSteamFarm.Web.GitHub;
using ArchiSteamFarm.Web.GitHub.Data;
using ArchiSteamFarm.Web.Responses;
using JetBrains.Annotations;
using SteamKit2;

namespace ArchiSteamFarm.Core;

public static class ASF {
	// This is based on internal Valve guidelines, we're not using it as a hard limit
	private const byte MaximumRecommendedBotsCount = 10;

	[PublicAPI]
	public static readonly ArchiLogger ArchiLogger = new(SharedInfo.ASF);

	[PublicAPI]
	public static byte LoadBalancingDelay => Math.Max(GlobalConfig?.LoginLimiterDelay ?? 0, GlobalConfig.DefaultLoginLimiterDelay);

	[PublicAPI]
	public static GlobalConfig? GlobalConfig { get; internal set; }

	[PublicAPI]
	public static GlobalDatabase? GlobalDatabase { get; internal set; }

	[PublicAPI]
	public static WebBrowser? WebBrowser { get; private set; }

	internal static readonly SemaphoreSlim OpenConnectionsSemaphore = new(WebBrowser.MaxConnections, WebBrowser.MaxConnections);

	internal static string DebugDirectory => Path.Combine(SharedInfo.DebugDirectory, OS.ProcessStartTime.ToString("yyyy-MM-dd-THH-mm-ss", CultureInfo.InvariantCulture));

	internal static ICrossProcessSemaphore? ConfirmationsSemaphore { get; private set; }
	internal static ICrossProcessSemaphore? GiftsSemaphore { get; private set; }
	internal static ICrossProcessSemaphore? InventorySemaphore { get; private set; }
	internal static ICrossProcessSemaphore? LoginRateLimitingSemaphore { get; private set; }
	internal static ICrossProcessSemaphore? LoginSemaphore { get; private set; }
	internal static ICrossProcessSemaphore? RateLimitingSemaphore { get; private set; }
	internal static FrozenDictionary<Uri, (ICrossProcessSemaphore RateLimitingSemaphore, SemaphoreSlim OpenConnectionsSemaphore)>? WebLimitingSemaphores { get; private set; }

	private static readonly FrozenSet<string> AssembliesNeededBeforeUpdate = FrozenSet.Create(StringComparer.Ordinal, "System.IO.Pipes");
	private static readonly SemaphoreSlim UpdateSemaphore = new(1, 1);

	private static Timer? AutoUpdatesTimer;
	private static FileSystemWatcher? FileSystemWatcher;
	private static ConcurrentDictionary<string, object>? LastWriteEvents;

	[PublicAPI]
	public static bool IsOwner(ulong steamID) {
		if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		return steamID == GlobalConfig?.SteamOwnerID;
	}

	internal static string GetFilePath(EFileType fileType) {
		if (!Enum.IsDefined(fileType)) {
			throw new InvalidEnumArgumentException(nameof(fileType), (int) fileType, typeof(EFileType));
		}

		return fileType switch {
			EFileType.Config => Path.Combine(SharedInfo.ConfigDirectory, SharedInfo.GlobalConfigFileName),
			EFileType.Database => Path.Combine(SharedInfo.ConfigDirectory, SharedInfo.GlobalDatabaseFileName),
			EFileType.Crash => Path.Combine(SharedInfo.ConfigDirectory, SharedInfo.GlobalCrashFileName),
			_ => throw new InvalidOperationException(nameof(fileType))
		};
	}

	internal static async Task<bool> Init() {
		if (GlobalConfig == null) {
			throw new InvalidOperationException(nameof(GlobalConfig));
		}

		WebBrowser = new WebBrowser(ArchiLogger, GlobalConfig.WebProxy, true);

		if (!await PluginsCore.InitPlugins().ConfigureAwait(false)) {
			return false;
		}

		await UpdateAndRestart().ConfigureAwait(false);

		if (!Program.IgnoreUnsupportedEnvironment && !await ProtectAgainstCrashes().ConfigureAwait(false)) {
			ArchiLogger.LogFatalError(Strings.ErrorTooManyCrashes);

			return true;
		}

		Program.AllowCrashFileRemoval = true;

		await PluginsCore.OnASFInitModules(GlobalConfig.AdditionalProperties).ConfigureAwait(false);
		await InitRateLimiters().ConfigureAwait(false);

		StringComparer botsComparer = await PluginsCore.GetBotsComparer().ConfigureAwait(false);

		Bot.Init(botsComparer);

		if (!Program.Service && !GlobalConfig.Headless && !Console.IsInputRedirected) {
			Logging.StartInteractiveConsole();
		}

		if (GlobalConfig.IPC) {
			await ArchiKestrel.Start().ConfigureAwait(false);
		}

		uint changeNumberToStartFrom = await PluginsCore.GetChangeNumberToStartFrom().ConfigureAwait(false);

		SteamPICSChanges.Init(changeNumberToStartFrom);

		await RegisterBots().ConfigureAwait(false);

		if (Program.ConfigWatch) {
			InitConfigWatchEvents();
		}

		return true;
	}

	internal static bool IsValidBotName(string botName) {
		ArgumentException.ThrowIfNullOrEmpty(botName);

		if (botName[0] == '.') {
			return false;
		}

		if (botName.Equals(SharedInfo.ASF, StringComparison.OrdinalIgnoreCase)) {
			return false;
		}

		return Path.GetRelativePath(".", botName) == botName;
	}

	internal static async Task RestartOrExit() {
		if (GlobalConfig == null) {
			throw new InvalidOperationException(nameof(GlobalConfig));
		}

		if (Program.RestartAllowed && GlobalConfig.AutoRestart) {
			ArchiLogger.LogGenericInfo(Strings.Restarting);
			await Task.Delay(SharedInfo.ShortInformationDelay).ConfigureAwait(false);
			await Program.Restart().ConfigureAwait(false);
		} else {
			ArchiLogger.LogGenericInfo(Strings.Exiting);
			await Task.Delay(SharedInfo.ShortInformationDelay).ConfigureAwait(false);
			await Program.Exit().ConfigureAwait(false);
		}
	}

	internal static async Task<(bool Updated, Version? NewVersion)> Update(GlobalConfig.EUpdateChannel? updateChannel = null, bool updateOverride = false, bool forced = false) {
		if (updateChannel.HasValue && !Enum.IsDefined(updateChannel.Value)) {
			throw new InvalidEnumArgumentException(nameof(updateChannel), (int) updateChannel, typeof(GlobalConfig.EUpdateChannel));
		}

		if (GlobalConfig == null) {
			throw new InvalidOperationException(nameof(GlobalConfig));
		}

		(bool updated, Version? newVersion) = await UpdateASF(updateChannel, updateOverride, forced).ConfigureAwait(false);

		if (!updated) {
			// ASF wasn't updated as part of the process, update the plugins alone
			updated = await PluginsCore.UpdatePlugins(SharedInfo.Version, false, updateChannel, updateOverride, forced).ConfigureAwait(false);
		}

		return (updated, newVersion);
	}

	private static async Task<bool> CanHandleWriteEvent(string filePath) {
		ArgumentException.ThrowIfNullOrEmpty(filePath);

		if (LastWriteEvents == null) {
			throw new InvalidOperationException(nameof(LastWriteEvents));
		}

		// Save our event in dictionary
		object currentWriteEvent = new();
		LastWriteEvents[filePath] = currentWriteEvent;

		// Wait a second for eventual other events to arrive
		await Task.Delay(1000).ConfigureAwait(false);

		// We're allowed to handle this event if the one that is saved after full second is our event and we succeed in clearing it (we don't care what we're clearing anymore, it doesn't have to be atomic operation)
		return LastWriteEvents.TryGetValue(filePath, out object? savedWriteEvent) && (currentWriteEvent == savedWriteEvent) && LastWriteEvents.TryRemove(filePath, out _);
	}

	private static HashSet<string> GetLoadedAssembliesNames() {
		Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();

		return loadedAssemblies.Select(static loadedAssembly => loadedAssembly.FullName).Where(static name => !string.IsNullOrEmpty(name)).ToHashSet(StringComparer.Ordinal)!;
	}

	private static void InitConfigWatchEvents() {
		if ((FileSystemWatcher != null) || (LastWriteEvents != null)) {
			return;
		}

		if (Bot.BotsComparer == null) {
			throw new InvalidOperationException(nameof(Bot.BotsComparer));
		}

		FileSystemWatcher = new FileSystemWatcher(SharedInfo.ConfigDirectory) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };

		FileSystemWatcher.Changed += OnChanged;
		FileSystemWatcher.Created += OnCreated;
		FileSystemWatcher.Deleted += OnDeleted;
		FileSystemWatcher.Renamed += OnRenamed;

		LastWriteEvents = new ConcurrentDictionary<string, object>(Bot.BotsComparer);

		FileSystemWatcher.EnableRaisingEvents = true;
	}

	private static async Task InitRateLimiters() {
		ConfirmationsSemaphore ??= await PluginsCore.GetCrossProcessSemaphore(nameof(ConfirmationsSemaphore)).ConfigureAwait(false);
		GiftsSemaphore ??= await PluginsCore.GetCrossProcessSemaphore(nameof(GiftsSemaphore)).ConfigureAwait(false);
		InventorySemaphore ??= await PluginsCore.GetCrossProcessSemaphore(nameof(InventorySemaphore)).ConfigureAwait(false);
		LoginRateLimitingSemaphore ??= await PluginsCore.GetCrossProcessSemaphore(nameof(LoginRateLimitingSemaphore)).ConfigureAwait(false);
		LoginSemaphore ??= await PluginsCore.GetCrossProcessSemaphore(nameof(LoginSemaphore)).ConfigureAwait(false);
		RateLimitingSemaphore ??= await PluginsCore.GetCrossProcessSemaphore(nameof(RateLimitingSemaphore)).ConfigureAwait(false);

		WebLimitingSemaphores ??= new Dictionary<Uri, (ICrossProcessSemaphore RateLimitingSemaphore, SemaphoreSlim OpenConnectionsSemaphore)>(5) {
			{ ArchiWebHandler.SteamCheckoutURL, (await PluginsCore.GetCrossProcessSemaphore($"{nameof(ArchiWebHandler)}-{nameof(ArchiWebHandler.SteamCheckoutURL)}").ConfigureAwait(false), new SemaphoreSlim(WebBrowser.MaxConnections, WebBrowser.MaxConnections)) },
			{ ArchiWebHandler.SteamCommunityURL, (await PluginsCore.GetCrossProcessSemaphore($"{nameof(ArchiWebHandler)}-{nameof(ArchiWebHandler.SteamCommunityURL)}").ConfigureAwait(false), new SemaphoreSlim(WebBrowser.MaxConnections, WebBrowser.MaxConnections)) },
			{ ArchiWebHandler.SteamHelpURL, (await PluginsCore.GetCrossProcessSemaphore($"{nameof(ArchiWebHandler)}-{nameof(ArchiWebHandler.SteamHelpURL)}").ConfigureAwait(false), new SemaphoreSlim(WebBrowser.MaxConnections, WebBrowser.MaxConnections)) },
			{ ArchiWebHandler.SteamStoreURL, (await PluginsCore.GetCrossProcessSemaphore($"{nameof(ArchiWebHandler)}-{nameof(ArchiWebHandler.SteamStoreURL)}").ConfigureAwait(false), new SemaphoreSlim(WebBrowser.MaxConnections, WebBrowser.MaxConnections)) },
			{ WebAPI.DefaultBaseAddress, (await PluginsCore.GetCrossProcessSemaphore($"{nameof(ArchiWebHandler)}-{nameof(WebAPI)}").ConfigureAwait(false), new SemaphoreSlim(WebBrowser.MaxConnections, WebBrowser.MaxConnections)) }
		}.ToFrozenDictionary();
	}

	private static void LoadAllAssemblies() {
		HashSet<string> loadedAssembliesNames = GetLoadedAssembliesNames();

		LoadAssembliesRecursively(Assembly.GetExecutingAssembly(), loadedAssembliesNames);
	}

	private static void LoadAssembliesNeededBeforeUpdate() {
		HashSet<string> loadedAssembliesNames = GetLoadedAssembliesNames();

		foreach (string assemblyName in AssembliesNeededBeforeUpdate.Where(loadedAssembliesNames.Add)) {
			Assembly assembly;

			try {
				assembly = Assembly.Load(assemblyName);
			} catch (Exception e) {
				ArchiLogger.LogGenericDebuggingException(e);

				continue;
			}

			LoadAssembliesRecursively(assembly, loadedAssembliesNames);
		}
	}

	[UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL2026:RequiresUnreferencedCode", Justification = "We don't care about trimmed assemblies, as we need it to work only with the known (used) ones")]
	private static void LoadAssembliesRecursively(Assembly assembly, ISet<string> loadedAssembliesNames) {
		ArgumentNullException.ThrowIfNull(assembly);

		if ((loadedAssembliesNames == null) || (loadedAssembliesNames.Count == 0)) {
			throw new ArgumentNullException(nameof(loadedAssembliesNames));
		}

		foreach (AssemblyName assemblyName in assembly.GetReferencedAssemblies().Where(assemblyName => loadedAssembliesNames.Add(assemblyName.FullName))) {
			Assembly loadedAssembly;

			try {
				loadedAssembly = Assembly.Load(assemblyName);
			} catch (Exception e) {
				ArchiLogger.LogGenericDebuggingException(e);

				continue;
			}

			LoadAssembliesRecursively(loadedAssembly, loadedAssembliesNames);
		}
	}

	private static async void OnAutoUpdatesTimer(object? state = null) => await UpdateAndRestart().ConfigureAwait(false);

	private static async void OnChanged(object sender, FileSystemEventArgs e) {
		ArgumentNullException.ThrowIfNull(sender);
		ArgumentNullException.ThrowIfNull(e);

		if (string.IsNullOrEmpty(e.Name)) {
			throw new InvalidOperationException(nameof(e.Name));
		}

		if (string.IsNullOrEmpty(e.FullPath)) {
			throw new InvalidOperationException(nameof(e.FullPath));
		}

		await OnChangedFile(e.Name, e.FullPath).ConfigureAwait(false);
	}

	private static async Task OnChangedConfigFile(string name, string fullPath) {
		ArgumentException.ThrowIfNullOrEmpty(name);
		ArgumentException.ThrowIfNullOrEmpty(fullPath);

		await OnCreatedConfigFile(name, fullPath).ConfigureAwait(false);
	}

	private static async Task OnChangedConfigFile(string name) {
		ArgumentException.ThrowIfNullOrEmpty(name);

		if (!name.Equals(SharedInfo.IPCConfigFile, StringComparison.OrdinalIgnoreCase) || (GlobalConfig?.IPC != true)) {
			return;
		}

		if (!await CanHandleWriteEvent(name).ConfigureAwait(false)) {
			return;
		}

		ArchiLogger.LogGenericInfo(Strings.IPCConfigChanged);

		await ArchiKestrel.Restart().ConfigureAwait(false);
	}

	private static async Task OnChangedFile(string name, string fullPath) {
		ArgumentException.ThrowIfNullOrEmpty(name);
		ArgumentException.ThrowIfNullOrEmpty(fullPath);

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
		ArgumentException.ThrowIfNullOrEmpty(name);
		ArgumentException.ThrowIfNullOrEmpty(fullPath);

		await OnCreatedKeysFile(name, fullPath).ConfigureAwait(false);
	}

	private static async Task OnConfigChanged() {
		string globalConfigFile = GetFilePath(EFileType.Config);

		if (string.IsNullOrEmpty(globalConfigFile)) {
			throw new InvalidOperationException(nameof(globalConfigFile));
		}

		(GlobalConfig? globalConfig, _) = await GlobalConfig.Load(globalConfigFile).ConfigureAwait(false);

		if (globalConfig == null) {
			// Invalid config file, we allow user to fix it without destroying the ASF instance right away
			return;
		}

		if (globalConfig == GlobalConfig) {
			return;
		}

		ArchiLogger.LogGenericInfo(Strings.GlobalConfigChanged);
		await RestartOrExit().ConfigureAwait(false);
	}

	private static async void OnCreated(object sender, FileSystemEventArgs e) {
		ArgumentNullException.ThrowIfNull(sender);
		ArgumentNullException.ThrowIfNull(e);

		if (string.IsNullOrEmpty(e.Name)) {
			throw new InvalidOperationException(nameof(e.Name));
		}

		if (string.IsNullOrEmpty(e.FullPath)) {
			throw new InvalidOperationException(nameof(e.FullPath));
		}

		await OnCreatedFile(e.Name, e.FullPath).ConfigureAwait(false);
	}

	private static async Task OnCreatedConfigFile(string name, string fullPath) {
		ArgumentException.ThrowIfNullOrEmpty(name);
		ArgumentException.ThrowIfNullOrEmpty(fullPath);

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
		ArgumentException.ThrowIfNullOrEmpty(name);
		ArgumentException.ThrowIfNullOrEmpty(fullPath);

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
		ArgumentException.ThrowIfNullOrEmpty(name);
		ArgumentException.ThrowIfNullOrEmpty(fullPath);

		if (Bot.Bots == null) {
			throw new InvalidOperationException(nameof(Bot.Bots));
		}

		string botName = Path.GetFileNameWithoutExtension(name);

		if (string.IsNullOrEmpty(botName) || (botName[0] == '.')) {
			return;
		}

		if (!await CanHandleWriteEvent(fullPath).ConfigureAwait(false)) {
			return;
		}

		if (botName.Equals(SharedInfo.ASF, StringComparison.OrdinalIgnoreCase)) {
			await OnConfigChanged().ConfigureAwait(false);

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
				ArchiLogger.LogGenericWarning(Strings.FormatWarningExcessiveBotsCount(MaximumRecommendedBotsCount));
			}
		}
	}

	private static async Task OnCreatedKeysFile(string name, string fullPath) {
		ArgumentException.ThrowIfNullOrEmpty(name);
		ArgumentException.ThrowIfNullOrEmpty(fullPath);

		if (Bot.Bots == null) {
			throw new InvalidOperationException(nameof(Bot.Bots));
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
		ArgumentNullException.ThrowIfNull(sender);
		ArgumentNullException.ThrowIfNull(e);

		if (string.IsNullOrEmpty(e.Name)) {
			throw new InvalidOperationException(nameof(e.Name));
		}

		if (string.IsNullOrEmpty(e.FullPath)) {
			throw new InvalidOperationException(nameof(e.FullPath));
		}

		await OnDeletedFile(e.Name, e.FullPath).ConfigureAwait(false);
	}

	private static async Task OnDeletedConfigFile(string name, string fullPath) {
		ArgumentException.ThrowIfNullOrEmpty(name);
		ArgumentException.ThrowIfNullOrEmpty(fullPath);

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
		ArgumentException.ThrowIfNullOrEmpty(name);
		ArgumentException.ThrowIfNullOrEmpty(fullPath);

		string extension = Path.GetExtension(name);

		switch (extension) {
			case SharedInfo.JsonConfigExtension:
			case SharedInfo.IPCConfigExtension:
				await OnDeletedConfigFile(name, fullPath).ConfigureAwait(false);

				break;
		}
	}

	private static async Task OnDeletedJsonConfigFile(string name, string fullPath) {
		ArgumentException.ThrowIfNullOrEmpty(name);
		ArgumentException.ThrowIfNullOrEmpty(fullPath);

		if (Bot.Bots == null) {
			throw new InvalidOperationException(nameof(Bot.Bots));
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

	private static async void OnRenamed(object sender, RenamedEventArgs e) {
		// This function can be called with a possibility of OldName or (new) Name being null, we have to take it into account
		ArgumentNullException.ThrowIfNull(sender);
		ArgumentNullException.ThrowIfNull(e);

		if (!string.IsNullOrEmpty(e.OldName) && !string.IsNullOrEmpty(e.OldFullPath)) {
			await OnDeletedFile(e.OldName, e.OldFullPath).ConfigureAwait(false);
		}

		if (!string.IsNullOrEmpty(e.Name) && !string.IsNullOrEmpty(e.FullPath)) {
			await OnCreatedFile(e.Name, e.FullPath).ConfigureAwait(false);
		}
	}

	private static async Task<bool> ProtectAgainstCrashes() {
		if (Debugging.IsDebugBuild) {
			// Allow debug builds to run unconditionally, we expect to crash a lot in those
			return true;
		}

		string crashFilePath = GetFilePath(EFileType.Crash);

		CrashFile crashFile = await CrashFile.CreateOrLoad(crashFilePath).ConfigureAwait(false);

		if (crashFile.StartupCount >= WebBrowser.MaxTries) {
			// We've reached maximum allowed count of recent crashes, return failure
			return false;
		}

		DateTime now = DateTime.UtcNow;

		if (now - crashFile.LastStartup > TimeSpan.FromMinutes(5)) {
			// Last crash was long ago, restart counter
			crashFile.StartupCount = 1;
		} else if (++crashFile.StartupCount >= WebBrowser.MaxTries) {
			// We've reached maximum allowed count of recent crashes, return failure
			return false;
		}

		crashFile.LastStartup = now;

		// We're allowing this run to proceed
		return true;
	}

	private static async Task RegisterBots() {
		if (GlobalConfig == null) {
			throw new InvalidOperationException(nameof(GlobalConfig));
		}

		if (GlobalDatabase == null) {
			throw new InvalidOperationException(nameof(GlobalDatabase));
		}

		if (WebBrowser == null) {
			throw new InvalidOperationException(nameof(WebBrowser));
		}

		HashSet<string> botNames;

		try {
			botNames = Directory.EnumerateFiles(SharedInfo.ConfigDirectory, $"*{SharedInfo.JsonConfigExtension}").Select(Path.GetFileNameWithoutExtension).Where(static botName => !string.IsNullOrEmpty(botName) && IsValidBotName(botName)).ToHashSet(Bot.BotsComparer)!;
		} catch (Exception e) {
			ArchiLogger.LogGenericException(e);

			return;
		}

		switch (botNames.Count) {
			case 0:
				ArchiLogger.LogGenericWarning(Strings.ErrorNoBotsDefined);

				return;
			case > MaximumRecommendedBotsCount:
				ArchiLogger.LogGenericWarning(Strings.FormatWarningExcessiveBotsCount(MaximumRecommendedBotsCount));
				await Task.Delay(SharedInfo.InformationDelay).ConfigureAwait(false);

				break;
		}

		await Utilities.InParallel(botNames.OrderBy(static botName => botName, Bot.BotsComparer).Select(Bot.RegisterBot)).ConfigureAwait(false);
	}

	private static async Task UpdateAndRestart() {
		if (GlobalConfig == null) {
			throw new InvalidOperationException(nameof(GlobalConfig));
		}

		if (GlobalConfig.UpdateChannel == GlobalConfig.EUpdateChannel.None) {
			return;
		}

		if ((AutoUpdatesTimer == null) && (GlobalConfig.UpdatePeriod > 0)) {
			TimeSpan autoUpdatePeriod = TimeSpan.FromHours(GlobalConfig.UpdatePeriod);

			AutoUpdatesTimer = new Timer(
				OnAutoUpdatesTimer,
				null,
				autoUpdatePeriod, // Delay
				autoUpdatePeriod // Period
			);

			ArchiLogger.LogGenericInfo(Strings.FormatAutoUpdateCheckInfo(autoUpdatePeriod.ToHumanReadable()));
		}

		(bool updated, Version? newVersion) = await Update().ConfigureAwait(false);

		if (!updated) {
			if ((newVersion != null) && (SharedInfo.Version > newVersion)) {
				// User is running version newer than their channel allows
				ArchiLogger.LogGenericWarning(Strings.WarningPreReleaseVersion);
				await Task.Delay(SharedInfo.InformationDelay).ConfigureAwait(false);
			}

			return;
		}

		// Allow crash file recovery, if needed
		Program.AllowCrashFileRemoval = true;

		await RestartOrExit().ConfigureAwait(false);
	}

	private static async Task<(bool Updated, Version? NewVersion)> UpdateASF(GlobalConfig.EUpdateChannel? channel = null, bool updateOverride = false, bool forced = false) {
		if (channel.HasValue && !Enum.IsDefined(channel.Value)) {
			throw new InvalidEnumArgumentException(nameof(channel), (int) channel, typeof(GlobalConfig.EUpdateChannel));
		}

		if (GlobalConfig == null) {
			throw new InvalidOperationException(nameof(GlobalConfig));
		}

		if (WebBrowser == null) {
			throw new InvalidOperationException(nameof(WebBrowser));
		}

		channel ??= GlobalConfig.UpdateChannel;

		if (!BuildInfo.CanUpdate || (channel == GlobalConfig.EUpdateChannel.None)) {
			return (false, null);
		}

		string targetFile;

		await UpdateSemaphore.WaitAsync().ConfigureAwait(false);

		try {
			// If directories from previous update exist, it's a good idea to purge them now
			if (!await Utilities.UpdateCleanup(SharedInfo.HomeDirectory).ConfigureAwait(false)) {
				return (false, null);
			}

			ArchiLogger.LogGenericInfo(Strings.UpdateCheckingNewVersion);

			ReleaseResponse? releaseResponse = await GitHubService.GetLatestRelease(SharedInfo.GithubRepo, channel == GlobalConfig.EUpdateChannel.Stable).ConfigureAwait(false);

			if ((releaseResponse == null) || string.IsNullOrEmpty(releaseResponse.Tag)) {
				ArchiLogger.LogGenericWarning(Strings.ErrorUpdateCheckFailed);

				return (false, null);
			}

			Version newVersion = new(releaseResponse.Tag);

			ArchiLogger.LogGenericInfo(Strings.FormatUpdateVersionInfo(SharedInfo.Version, newVersion));

			if (!forced && (SharedInfo.Version >= newVersion)) {
				return (false, newVersion);
			}

			if (!updateOverride && (GlobalConfig.UpdatePeriod == 0)) {
				ArchiLogger.LogGenericInfo(Strings.UpdateNewVersionAvailable);
				await Task.Delay(SharedInfo.ShortInformationDelay).ConfigureAwait(false);

				return (false, newVersion);
			}

			// Auto update logic starts here
			if (releaseResponse.Assets.IsEmpty) {
				ArchiLogger.LogGenericWarning(Strings.ErrorUpdateNoAssets);

				return (false, newVersion);
			}

			targetFile = $"{SharedInfo.ASF}-{BuildInfo.Variant}.zip";
			ReleaseAsset? binaryAsset = releaseResponse.Assets.FirstOrDefault(asset => !string.IsNullOrEmpty(asset.Name) && asset.Name.Equals(targetFile, StringComparison.OrdinalIgnoreCase));

			if (binaryAsset == null) {
				ArchiLogger.LogGenericWarning(Strings.ErrorUpdateNoAssetForThisVersion);

				return (false, newVersion);
			}

			ArchiLogger.LogGenericInfo(Strings.FetchingChecksumFromRemoteServer);

			// Keep short timeout allowed for this call, as we don't want to hold the flow for too long
			using CancellationTokenSource archiNetCancellation = new(TimeSpan.FromSeconds(15));

			string? remoteChecksum = await ArchiNet.FetchBuildChecksum(newVersion, BuildInfo.Variant, archiNetCancellation.Token).ConfigureAwait(false);

			switch (remoteChecksum) {
				case null:
					// Timeout or error, refuse to update as a security measure
					ArchiLogger.LogGenericWarning(Strings.ChecksumTimeout);

					return (false, newVersion);
				case "":
					// Unknown checksum, release too new or actual malicious build published, no need to scare the user as it's 99.99% the first
					ArchiLogger.LogGenericWarning(Strings.ChecksumMissing);

					return (false, newVersion);
			}

			if (!string.IsNullOrEmpty(releaseResponse.ChangelogPlainText)) {
				ArchiLogger.LogGenericInfo(releaseResponse.ChangelogPlainText);
			}

			ArchiLogger.LogGenericInfo(Strings.FormatUpdateDownloadingNewVersion(newVersion, binaryAsset.Size / 1024 / 1024));

			Progress<byte> progressReporter = new();

			progressReporter.ProgressChanged += onProgressChanged;

			BinaryResponse? response;

			try {
				response = await WebBrowser.UrlGetToBinary(binaryAsset.DownloadURL, progressReporter: progressReporter, cancellationToken: CancellationToken.None).ConfigureAwait(false);
			} finally {
				progressReporter.ProgressChanged -= onProgressChanged;
			}

			if (response?.Content == null) {
				return (false, newVersion);
			}

			ArchiLogger.LogGenericInfo(Strings.VerifyingChecksumWithRemoteServer);

			byte[] responseBytes = response.Content as byte[] ?? response.Content.ToArray();

			string checksum = Utilities.GenerateChecksumFor(responseBytes);

			if (!checksum.Equals(remoteChecksum, StringComparison.OrdinalIgnoreCase)) {
				ArchiLogger.LogGenericError(Strings.ChecksumWrong);

				return (false, newVersion);
			}

			await PluginsCore.OnUpdateProceeding(newVersion).ConfigureAwait(false);

			bool kestrelWasRunning = ArchiKestrel.IsRunning;

			if (kestrelWasRunning) {
				// We disable ArchiKestrel here as the update process moves the core files and might result in IPC crash
				ASFController.PendingVersionUpdate = newVersion;

				try {
					await ArchiKestrel.Stop().ConfigureAwait(false);
				} catch (Exception e) {
					ArchiLogger.LogGenericWarningException(e);
				} finally {
					ASFController.PendingVersionUpdate = null;
				}
			}

			ArchiLogger.LogGenericInfo(Strings.PatchingFiles);

			try {
				MemoryStream memoryStream = new(responseBytes);

				await using (memoryStream.ConfigureAwait(false)) {
					ZipArchive zipArchive = new(memoryStream);

					await using (zipArchive.ConfigureAwait(false)) {
						if (!await UpdateFromArchive(newVersion, channel.Value, updateOverride, forced, zipArchive).ConfigureAwait(false)) {
							ArchiLogger.LogGenericError(Strings.WarningFailed);
						}
					}
				}
			} catch (Exception e) {
				ArchiLogger.LogGenericException(e);

				if (kestrelWasRunning) {
					// We've temporarily disabled ArchiKestrel but the update has failed, let's bring it back up
					// We can't even be sure if it's possible to bring it back up in this state, but it's worth trying anyway
					try {
						await ArchiKestrel.Start().ConfigureAwait(false);
					} catch (Exception ex) {
						ArchiLogger.LogGenericWarningException(ex);
					}
				}

				return (false, newVersion);
			}

			ArchiLogger.LogGenericInfo(Strings.UpdateFinished);

			await PluginsCore.OnUpdateFinished(newVersion).ConfigureAwait(false);

			return (true, newVersion);
		} finally {
			UpdateSemaphore.Release();
		}

		void onProgressChanged(object? sender, byte progressPercentage) {
			ArgumentOutOfRangeException.ThrowIfGreaterThan(progressPercentage, 100);

			Utilities.OnProgressChanged(targetFile, progressPercentage);
		}
	}

	private static async Task<bool> UpdateFromArchive(Version newVersion, GlobalConfig.EUpdateChannel updateChannel, bool updateOverride, bool forced, ZipArchive zipArchive) {
		ArgumentNullException.ThrowIfNull(newVersion);

		if (!Enum.IsDefined(updateChannel)) {
			throw new InvalidEnumArgumentException(nameof(updateChannel), (int) updateChannel, typeof(GlobalConfig.EUpdateChannel));
		}

		ArgumentNullException.ThrowIfNull(zipArchive);

		if (SharedInfo.HomeDirectory == AppContext.BaseDirectory) {
			// We're running a build that includes our dependencies in ASF's home
			// Before actually moving files in update procedure, let's minimize the risk of some assembly not being loaded that we may need in the process
			LoadAllAssemblies();
		} else {
			// This is a tricky one, for some reason we might need to preload some selected assemblies even in OS-specific builds that normally should be self-contained...
			// It's as if the executable file was directly mapped to memory and moving it out of the original path caused the whole thing to crash
			// TODO: This is a total hack, I wish we could get to the bottom of this hole and find out what is really going on there in regards to the above
			LoadAssembliesNeededBeforeUpdate();
		}

		// We're ready to start update process, handle any plugin updates ready for new version
		await PluginsCore.UpdatePlugins(newVersion, true, updateChannel, updateOverride, forced).ConfigureAwait(false);

		return await Utilities.UpdateFromArchive(zipArchive, SharedInfo.HomeDirectory).ConfigureAwait(false);
	}

	[PublicAPI]
	public enum EUserInputType : byte {
		None,
		Login,
		Password,
		SteamGuard,
		SteamParentalCode,
		TwoFactorAuthentication,
		Cryptkey,
		DeviceConfirmation
	}

	internal enum EFileType : byte {
		Config,
		Database,
		Crash
	}
}
