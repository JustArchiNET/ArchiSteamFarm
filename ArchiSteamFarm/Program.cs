//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2021 Łukasz "JustArchi" Domeradzki
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
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers;
using ArchiSteamFarm.IPC;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.NLog;
using ArchiSteamFarm.NLog.Targets;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Storage;
using ArchiSteamFarm.Web;
using Newtonsoft.Json;
using NLog;
using NLog.Targets;
using SteamKit2;

namespace ArchiSteamFarm;

internal static class Program {
	internal static bool ConfigMigrate { get; private set; } = true;
	internal static bool ConfigWatch { get; private set; } = true;
	internal static string? NetworkGroup { get; private set; }
	internal static bool ProcessRequired { get; private set; }
	internal static bool RestartAllowed { get; private set; } = true;
	internal static bool Service { get; private set; }
	internal static bool ShutdownSequenceInitialized { get; private set; }

#if !NETFRAMEWORK
	private static readonly Dictionary<PosixSignal, PosixSignalRegistration> RegisteredPosixSignals = new();
#endif

	private static readonly TaskCompletionSource<byte> ShutdownResetEvent = new();

#if !NETFRAMEWORK
	private static readonly ImmutableHashSet<PosixSignal> SupportedPosixSignals = ImmutableHashSet.Create(PosixSignal.SIGINT, PosixSignal.SIGTERM);
#endif

	private static bool IgnoreUnsupportedEnvironment;
	private static bool SystemRequired;

	internal static async Task Exit(byte exitCode = 0) {
		if (exitCode != 0) {
			ASF.ArchiLogger.LogGenericError(Strings.ErrorExitingWithNonZeroErrorCode);
		}

		await Shutdown(exitCode).ConfigureAwait(false);
		Environment.Exit(exitCode);
	}

	internal static async Task Restart() {
		if (!await InitShutdownSequence().ConfigureAwait(false)) {
			return;
		}

		string executableName = Path.GetFileNameWithoutExtension(OS.ProcessFileName);

		if (string.IsNullOrEmpty(executableName)) {
			throw new ArgumentNullException(nameof(executableName));
		}

		IEnumerable<string> arguments = Environment.GetCommandLineArgs().Skip(executableName.Equals(SharedInfo.AssemblyName, StringComparison.Ordinal) ? 1 : 0);

		try {
			Process.Start(OS.ProcessFileName, string.Join(" ", arguments));
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);
		}

		// Give new process some time to take over the window (if needed)
		await Task.Delay(2000).ConfigureAwait(false);

		ShutdownResetEvent.TrySetResult(0);
		Environment.Exit(0);
	}

	private static void HandleCryptKeyArgument(string cryptKey) {
		if (string.IsNullOrEmpty(cryptKey)) {
			throw new ArgumentNullException(nameof(cryptKey));
		}

		ArchiCryptoHelper.SetEncryptionKey(cryptKey);
	}

	private static void HandleNetworkGroupArgument(string networkGroup) {
		if (string.IsNullOrEmpty(networkGroup)) {
			throw new ArgumentNullException(nameof(networkGroup));
		}

		NetworkGroup = networkGroup;
	}

	private static bool HandlePathArgument(string path) {
		if (string.IsNullOrEmpty(path)) {
			throw new ArgumentNullException(nameof(path));
		}

		// Aid userspace and replace ~ with user's home directory if possible
		if (path.Contains('~', StringComparison.Ordinal)) {
			try {
				string homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify);

				if (!string.IsNullOrEmpty(homeDirectory)) {
					path = path.Replace("~", homeDirectory, StringComparison.Ordinal);
				}
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);

				return false;
			}
		}

		try {
			Directory.SetCurrentDirectory(path);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);

			return false;
		}

		return true;
	}

	private static async Task Init(IReadOnlyCollection<string>? args) {
		AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
		AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
		TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

#if !NETFRAMEWORK
		if (OperatingSystem.IsFreeBSD() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) {
			foreach (PosixSignal signal in SupportedPosixSignals) {
				RegisteredPosixSignals[signal] = PosixSignalRegistration.Create(signal, OnPosixSignal);
			}
		}
#endif

		Console.CancelKeyPress += OnCancelKeyPress;

		// Add support for custom encodings
		Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

		// Add support for custom logging targets
		Target.Register<HistoryTarget>(HistoryTarget.TargetName);
		Target.Register<SteamTarget>(SteamTarget.TargetName);

		if (!await InitCore(args).ConfigureAwait(false) || !await InitASF().ConfigureAwait(false)) {
			await Exit(1).ConfigureAwait(false);
		}
	}

	private static async Task<bool> InitASF() {
		if (!await InitGlobalConfigAndLanguage().ConfigureAwait(false)) {
			return false;
		}

		if (ASF.GlobalConfig == null) {
			throw new InvalidOperationException(nameof(ASF.GlobalConfig));
		}

		OS.Init(ASF.GlobalConfig.OptimizationMode);

		if (!await InitGlobalDatabaseAndServices().ConfigureAwait(false)) {
			return false;
		}

		await ASF.Init().ConfigureAwait(false);

		return true;
	}

	private static async Task<bool> InitCore(IReadOnlyCollection<string>? args) {
		// Init emergency loggers used for failures before setting up ones according to preference of the user
		Logging.InitEmergencyLoggers();

		Directory.SetCurrentDirectory(SharedInfo.HomeDirectory);

		// Allow loading configs from source tree if it's a debug build
		if (Debugging.IsDebugBuild) {
			// Common structure is bin/(x64/)Debug/ArchiSteamFarm.exe, so we allow up to 4 directories up
			for (byte i = 0; i < 4; i++) {
				Directory.SetCurrentDirectory("..");

				if (Directory.Exists(SharedInfo.ConfigDirectory)) {
					break;
				}
			}

			// If config directory doesn't exist after our adjustment, abort all of that
			if (!Directory.Exists(SharedInfo.ConfigDirectory)) {
				Directory.SetCurrentDirectory(SharedInfo.HomeDirectory);
			}
		}

		// Parse environment variables
		if (!ParseEnvironmentVariables()) {
			await Task.Delay(SharedInfo.InformationDelay).ConfigureAwait(false);

			return false;
		}

		// Parse args
		if (args?.Count > 0) {
			if (!ParseArgs(args)) {
				await Task.Delay(SharedInfo.InformationDelay).ConfigureAwait(false);

				return false;
			}
		}

		bool uniqueInstance = await OS.RegisterProcess().ConfigureAwait(false);

		// Init core loggers according to user's preferences
		Logging.InitCoreLoggers(uniqueInstance);

		if (!uniqueInstance) {
			ASF.ArchiLogger.LogGenericError(Strings.ErrorSingleInstanceRequired);
			await Task.Delay(SharedInfo.InformationDelay).ConfigureAwait(false);

			return false;
		}

		OS.CoreInit(SystemRequired);

		Console.Title = SharedInfo.ProgramIdentifier;
		ASF.ArchiLogger.LogGenericInfo(SharedInfo.ProgramIdentifier);

		string? copyright = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright;

		if (!string.IsNullOrEmpty(copyright)) {
			// ReSharper disable once RedundantSuppressNullableWarningExpression - required for .NET Framework
			ASF.ArchiLogger.LogGenericInfo(copyright!);
		}

		if (IgnoreUnsupportedEnvironment) {
			ASF.ArchiLogger.LogGenericWarning(Strings.WarningRunningInUnsupportedEnvironment);
		} else {
			if (!OS.VerifyEnvironment()) {
				ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnsupportedEnvironment, SharedInfo.BuildInfo.Variant, OS.Version));
				await Task.Delay(SharedInfo.InformationDelay).ConfigureAwait(false);

				return false;
			}

			if (OS.IsRunningAsRoot()) {
				ASF.ArchiLogger.LogGenericWarning(Strings.WarningRunningAsRoot);
				await Task.Delay(SharedInfo.ShortInformationDelay).ConfigureAwait(false);
			}
		}

		if (!Directory.Exists(SharedInfo.ConfigDirectory)) {
			ASF.ArchiLogger.LogGenericError(Strings.ErrorConfigDirectoryNotFound);
			await Task.Delay(SharedInfo.InformationDelay).ConfigureAwait(false);

			return false;
		}

		return true;
	}

	private static async Task<bool> InitGlobalConfigAndLanguage() {
		string globalConfigFile = ASF.GetFilePath(ASF.EFileType.Config);

		if (string.IsNullOrEmpty(globalConfigFile)) {
			throw new ArgumentNullException(nameof(globalConfigFile));
		}

		string? latestJson = null;

		GlobalConfig? globalConfig;

		if (File.Exists(globalConfigFile)) {
			(globalConfig, latestJson) = await GlobalConfig.Load(globalConfigFile).ConfigureAwait(false);

			if (globalConfig == null) {
				ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.ErrorGlobalConfigNotLoaded, globalConfigFile));
				await Task.Delay(SharedInfo.ShortInformationDelay).ConfigureAwait(false);

				return false;
			}
		} else {
			globalConfig = new GlobalConfig();
		}

		if (globalConfig.Debug) {
			ASF.ArchiLogger.LogGenericDebug($"{globalConfigFile}: {JsonConvert.SerializeObject(globalConfig, Formatting.Indented)}");
		}

		if (!string.IsNullOrEmpty(globalConfig.CurrentCulture)) {
			try {
				// GetCultureInfo() would be better but we can't use it for specifying neutral cultures such as "en"
				CultureInfo culture = CultureInfo.CreateSpecificCulture(globalConfig.CurrentCulture!);
				CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = culture;
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericWarningException(e);

				ASF.ArchiLogger.LogGenericError(Strings.ErrorInvalidCurrentCulture);
			}
		} else {
			// April Fools easter egg logic
			AprilFools.Init();
		}

		if (!string.IsNullOrEmpty(latestJson)) {
			ASF.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.AutomaticFileMigration, globalConfigFile));

			// ReSharper disable once RedundantSuppressNullableWarningExpression - required for .NET Framework
			await SerializableFile.Write(globalConfigFile, latestJson!).ConfigureAwait(false);

			ASF.ArchiLogger.LogGenericInfo(Strings.Done);
		}

		ASF.GlobalConfig = globalConfig;

		Utilities.WarnAboutIncompleteTranslation(Strings.ResourceManager);

		return true;
	}

	private static async Task<bool> InitGlobalDatabaseAndServices() {
		string globalDatabaseFile = ASF.GetFilePath(ASF.EFileType.Database);

		if (string.IsNullOrEmpty(globalDatabaseFile)) {
			throw new ArgumentNullException(nameof(globalDatabaseFile));
		}

		if (!File.Exists(globalDatabaseFile)) {
			ASF.ArchiLogger.LogGenericInfo(Strings.Welcome);
			await Task.Delay(SharedInfo.InformationDelay).ConfigureAwait(false);
			ASF.ArchiLogger.LogGenericWarning(Strings.WarningPrivacyPolicy);
			await Task.Delay(SharedInfo.ShortInformationDelay).ConfigureAwait(false);
		}

		GlobalDatabase? globalDatabase = await GlobalDatabase.CreateOrLoad(globalDatabaseFile).ConfigureAwait(false);

		if (globalDatabase == null) {
			ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.ErrorDatabaseInvalid, globalDatabaseFile));
			await Task.Delay(SharedInfo.ShortInformationDelay).ConfigureAwait(false);

			return false;
		}

		ASF.GlobalDatabase = globalDatabase;

		// If debugging is on, we prepare debug directory prior to running
		if (Debugging.IsUserDebugging) {
			if (Debugging.IsDebugConfigured) {
				ASF.ArchiLogger.LogGenericDebug($"{globalDatabaseFile}: {JsonConvert.SerializeObject(ASF.GlobalDatabase, Formatting.Indented)}");
			}

			Logging.EnableTraceLogging();

			if (Debugging.IsDebugConfigured) {
				DebugLog.AddListener(new Debugging.DebugListener());
				DebugLog.Enabled = true;

				if (Directory.Exists(SharedInfo.DebugDirectory)) {
					try {
						Directory.Delete(SharedInfo.DebugDirectory, true);
						await Task.Delay(1000).ConfigureAwait(false); // Dirty workaround giving Windows some time to sync
					} catch (Exception e) {
						ASF.ArchiLogger.LogGenericException(e);
					}
				}
			}

			try {
				Directory.CreateDirectory(SharedInfo.DebugDirectory);
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);
			}
		}

		WebBrowser.Init();

		return true;
	}

	private static async Task<bool> InitShutdownSequence() {
		if (ShutdownSequenceInitialized) {
			return false;
		}

		ShutdownSequenceInitialized = true;

#if !NETFRAMEWORK
		if (OperatingSystem.IsFreeBSD() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) {
			// Unregister from registered signals
			foreach (PosixSignalRegistration registration in RegisteredPosixSignals.Values) {
				registration.Dispose();
			}

			RegisteredPosixSignals.Clear();
		}
#endif

		// Sockets created by IPC might still be running for a short while after complete app shutdown
		// Ensure that IPC is stopped before we finalize shutdown sequence
		await ArchiKestrel.Stop().ConfigureAwait(false);

		// Stop all the active bots so they can disconnect cleanly
		if (Bot.Bots?.Count > 0) {
			// Stop() function can block due to SK2 sockets, don't forget a maximum delay
			await Task.WhenAny(Utilities.InParallel(Bot.Bots.Values.Select(static bot => Task.Run(() => bot.Stop(true)))), Task.Delay(Bot.Bots.Count * WebBrowser.MaxTries * 1000)).ConfigureAwait(false);

			// Extra second for Steam requests to go through
			await Task.Delay(1000).ConfigureAwait(false);
		}

		// Flush all the pending writes to log files
		LogManager.Flush();

		// Unregister the process from single instancing
		OS.UnregisterProcess();

		return true;
	}

	private static async Task<int> Main(string[] args) {
		if (args == null) {
			throw new ArgumentNullException(nameof(args));
		}

		// Initialize
		await Init(args.Length > 0 ? args : null).ConfigureAwait(false);

		// Wait for shutdown event
		return await ShutdownResetEvent.Task.ConfigureAwait(false);
	}

	private static async void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e) => await Exit(130).ConfigureAwait(false);

#if !NETFRAMEWORK
	private static async void OnPosixSignal(PosixSignalContext signal) {
		if (signal == null) {
			throw new ArgumentNullException(nameof(signal));
		}

		switch (signal.Signal) {
			case PosixSignal.SIGINT:
				await Exit(130).ConfigureAwait(false);

				break;
			case PosixSignal.SIGTERM:
				await Exit().ConfigureAwait(false);

				break;
		}
	}
#endif

	private static async void OnProcessExit(object? sender, EventArgs e) => await Shutdown().ConfigureAwait(false);

	private static async void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e) {
		if (e == null) {
			throw new ArgumentNullException(nameof(e));
		}

		if (e.ExceptionObject == null) {
			throw new ArgumentNullException(nameof(e));
		}

		await ASF.ArchiLogger.LogFatalException((Exception) e.ExceptionObject).ConfigureAwait(false);
		await Exit(1).ConfigureAwait(false);
	}

	private static async void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e) {
		if (e == null) {
			throw new ArgumentNullException(nameof(e));
		}

		if (e.Exception == null) {
			throw new ArgumentNullException(nameof(e));
		}

		// TODO: Silence https://github.com/dotnet/runtime/issues/60856 - pending for removal (after verification) in 6.0.1+
		if (e.Exception.InnerExceptions.All(static exception => exception is TimeoutException timeoutException && string.IsNullOrEmpty(timeoutException.StackTrace))) {
			e.SetObserved();

			return;
		}

		await ASF.ArchiLogger.LogFatalException(e.Exception).ConfigureAwait(false);

		// Normally we should abort the application, but due to the fact that unobserved exceptions do not have to do that, it's a better idea to log it and try to continue
		e.SetObserved();
	}

	private static bool ParseArgs(IReadOnlyCollection<string> args) {
		if ((args == null) || (args.Count == 0)) {
			throw new ArgumentNullException(nameof(args));
		}

		bool cryptKeyNext = false;
		bool networkGroupNext = false;
		bool pathNext = false;

		foreach (string arg in args) {
			switch (arg.ToUpperInvariant()) {
				case "--CRYPTKEY" when !cryptKeyNext && !networkGroupNext && !pathNext:
					cryptKeyNext = true;

					break;
				case "--IGNORE-UNSUPPORTED-ENVIRONMENT" when !cryptKeyNext && !networkGroupNext && !pathNext:
					IgnoreUnsupportedEnvironment = true;

					break;
				case "--NETWORK-GROUP" when !cryptKeyNext && !networkGroupNext && !pathNext:
					networkGroupNext = true;

					break;
				case "--NO-CONFIG-MIGRATE" when !cryptKeyNext && !networkGroupNext && !pathNext:
					ConfigMigrate = false;

					break;
				case "--NO-CONFIG-WATCH" when !cryptKeyNext && !networkGroupNext && !pathNext:
					ConfigWatch = false;

					break;
				case "--NO-RESTART" when !cryptKeyNext && !networkGroupNext && !pathNext:
					RestartAllowed = false;

					break;
				case "--PROCESS-REQUIRED" when !cryptKeyNext && !networkGroupNext && !pathNext:
					ProcessRequired = true;

					break;
				case "--PATH" when !cryptKeyNext && !networkGroupNext && !pathNext:
					pathNext = true;

					break;
				case "--SERVICE" when !cryptKeyNext && !networkGroupNext && !pathNext:
					Service = true;

					break;
				case "--SYSTEM-REQUIRED" when !cryptKeyNext && !networkGroupNext && !pathNext:
					SystemRequired = true;

					break;
				default:
					if (cryptKeyNext) {
						cryptKeyNext = false;
						HandleCryptKeyArgument(arg);
					} else if (networkGroupNext) {
						networkGroupNext = false;
						HandleNetworkGroupArgument(arg);
					} else if (pathNext) {
						pathNext = false;

						if (!HandlePathArgument(arg)) {
							return false;
						}
					} else {
						switch (arg.Length) {
							case > 16 when arg.StartsWith("--NETWORK-GROUP=", StringComparison.OrdinalIgnoreCase):
								HandleNetworkGroupArgument(arg[16..]);

								break;
							case > 11 when arg.StartsWith("--CRYPTKEY=", StringComparison.OrdinalIgnoreCase):
								HandleCryptKeyArgument(arg[11..]);

								break;
							case > 7 when arg.StartsWith("--PATH=", StringComparison.OrdinalIgnoreCase):
								if (!HandlePathArgument(arg[7..])) {
									return false;
								}

								break;
							default:
								ASF.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownCommandLineArgument, arg));

								break;
						}
					}

					break;
			}
		}

		return true;
	}

	private static bool ParseEnvironmentVariables() {
		// We're using a single try-catch block here, as a failure for getting one variable will result in the same failure for all other ones
		try {
			string? envCryptKey = Environment.GetEnvironmentVariable(SharedInfo.EnvironmentVariableCryptKey);

			if (!string.IsNullOrEmpty(envCryptKey)) {
				// ReSharper disable once RedundantSuppressNullableWarningExpression - required for .NET Framework
				HandleCryptKeyArgument(envCryptKey!);
			}

			string? envNetworkGroup = Environment.GetEnvironmentVariable(SharedInfo.EnvironmentVariableNetworkGroup);

			if (!string.IsNullOrEmpty(envNetworkGroup)) {
				// ReSharper disable once RedundantSuppressNullableWarningExpression - required for .NET Framework
				HandleNetworkGroupArgument(envNetworkGroup!);
			}

			string? envPath = Environment.GetEnvironmentVariable(SharedInfo.EnvironmentVariablePath);

			if (!string.IsNullOrEmpty(envPath)) {
				// ReSharper disable once RedundantSuppressNullableWarningExpression - required for .NET Framework
				if (!HandlePathArgument(envPath!)) {
					return false;
				}
			}
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);

			return false;
		}

		return true;
	}

	private static async Task Shutdown(byte exitCode = 0) {
		if (!await InitShutdownSequence().ConfigureAwait(false)) {
			return;
		}

		ShutdownResetEvent.TrySetResult(exitCode);
	}
}
