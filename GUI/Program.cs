using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ArchiSteamFarm.Localization;
using SteamKit2;

namespace ArchiSteamFarm {
	internal static class Program {
		internal static readonly ArchiLogger ArchiLogger = new ArchiLogger(SharedInfo.ASF);

		internal static GlobalConfig GlobalConfig { get; private set; }
		internal static GlobalDatabase GlobalDatabase { get; private set; }
		internal static WebBrowser WebBrowser { get; private set; }

		private static bool ShutdownSequenceInitialized;

		internal static async Task Exit(byte exitCode = 0) {
			if (exitCode != 0) {
				ASF.ArchiLogger.LogGenericError(Strings.ErrorExitingWithNonZeroErrorCode);
			}

			await Shutdown().ConfigureAwait(false);
			Environment.Exit(exitCode);
		}

		internal static string GetUserInput(ASF.EUserInputType userInputType, string botName = SharedInfo.ASF, string extraInformation = null) {
			return null; // TODO
		}

		internal static async Task<bool> InitShutdownSequence() {
			if (ShutdownSequenceInitialized) {
				return false;
			}

			ShutdownSequenceInitialized = true;

			IEnumerable<Task> tasks = Bot.Bots.Values.Select(bot => Task.Run(() => bot.Stop()));
			await Task.WhenAny(Task.WhenAll(tasks), Task.Delay(10 * 1000));

			return true;
		}

		internal static async Task Restart() {
			if (!await InitShutdownSequence().ConfigureAwait(false)) {
				return;
			}

			Application.Restart();
		}

		private static async Task Init() {
			AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionHandler;
			TaskScheduler.UnobservedTaskException += UnobservedTaskExceptionHandler;

			Logging.InitCoreLoggers();

			if (!Runtime.IsRuntimeSupported) {
				ArchiLogger.LogGenericError("ASF detected unsupported runtime version, program might NOT run correctly in current environment. You're running it at your own risk!");
			}

			string homeDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
			if (!string.IsNullOrEmpty(homeDirectory)) {
				Directory.SetCurrentDirectory(homeDirectory);

				// Allow loading configs from source tree if it's a debug build
				if (Debugging.IsDebugBuild) {
					// Common structure is bin/(x64/)Debug/ArchiSteamFarm.exe, so we allow up to 4 directories up
					for (byte i = 0; i < 4; i++) {
						Directory.SetCurrentDirectory("..");
						if (!Directory.Exists(SharedInfo.ASFDirectory)) {
							continue;
						}

						Directory.SetCurrentDirectory(SharedInfo.ASFDirectory);
						break;
					}

					// If config directory doesn't exist after our adjustment, abort all of that
					if (!Directory.Exists(SharedInfo.ConfigDirectory)) {
						Directory.SetCurrentDirectory(homeDirectory);
					}
				}
			}

			await InitServices().ConfigureAwait(false);

			// If debugging is on, we prepare debug directory prior to running
			if (GlobalConfig.Debug) {
				if (Directory.Exists(SharedInfo.DebugDirectory)) {
					Directory.Delete(SharedInfo.DebugDirectory, true);
					Thread.Sleep(1000); // Dirty workaround giving Windows some time to sync
				}

				Directory.CreateDirectory(SharedInfo.DebugDirectory);

				DebugLog.AddListener(new Debugging.DebugListener());
				DebugLog.Enabled = true;
			}

			Logging.InitEnhancedLoggers();
		}

		private static async Task InitServices() {
			string globalConfigFile = Path.Combine(SharedInfo.ConfigDirectory, SharedInfo.GlobalConfigFileName);

			GlobalConfig = GlobalConfig.Load(globalConfigFile);
			if (GlobalConfig == null) {
				ArchiLogger.LogGenericError("Global config could not be loaded, please make sure that " + globalConfigFile + " exists and is valid!");
				await Exit(1).ConfigureAwait(false);
			}

			string globalDatabaseFile = Path.Combine(SharedInfo.ConfigDirectory, SharedInfo.GlobalDatabaseFileName);

			GlobalDatabase = GlobalDatabase.Load(globalDatabaseFile);
			if (GlobalDatabase == null) {
				ArchiLogger.LogGenericError("Global database could not be loaded, if issue persists, please remove " + globalDatabaseFile + " in order to recreate database!");
				await Exit(1).ConfigureAwait(false);
			}

			ArchiWebHandler.Init();
			WebBrowser.Init();

			WebBrowser = new WebBrowser(ArchiLogger);
		}

		/// <summary>
		///     The main entry point for the application.
		/// </summary>
		[STAThread]
		private static void Main() {
			Init().Wait();
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			Application.Run(new MainForm());
		}

		private static async Task Shutdown() {
			if (!await InitShutdownSequence().ConfigureAwait(false)) {
				return;
			}

			Application.Exit();
		}

		private static void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs args) {
			if (args?.ExceptionObject == null) {
				ArchiLogger.LogNullError(nameof(args) + " || " + nameof(args.ExceptionObject));
				return;
			}

			ArchiLogger.LogFatalException((Exception) args.ExceptionObject);
		}

		private static void UnobservedTaskExceptionHandler(object sender, UnobservedTaskExceptionEventArgs args) {
			if (args?.Exception == null) {
				ArchiLogger.LogNullError(nameof(args) + " || " + nameof(args.Exception));
				return;
			}

			ArchiLogger.LogFatalException(args.Exception);
		}
	}
}