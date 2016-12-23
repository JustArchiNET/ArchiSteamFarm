using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI;
using SteamKit2;

// ReSharper disable once CheckNamespace

namespace ArchiSteamFarm {
	internal static class Program {
		internal static readonly ArchiLogger ArchiLogger = new ArchiLogger(SharedInfo.ASF);

		internal static GlobalConfig GlobalConfig { get; private set; }
		internal static GlobalDatabase GlobalDatabase { get; private set; }
		internal static WebBrowser WebBrowser { get; private set; }

		internal static void Exit(int exitCode = 0) {
			InitShutdownSequence();
			Environment.Exit(exitCode);
		}

		internal static string GetUserInput(ASF.EUserInputType userInputType, string botName = SharedInfo.ASF, string extraInformation = null) {
			return null; // TODO
		}

		internal static void InitShutdownSequence() {
			foreach (Bot bot in Bot.Bots.Values.Where(bot => bot.KeepRunning)) {
				bot.Stop();
			}
		}

		internal static void Restart() {
			InitShutdownSequence();

			try {
				Process.Start(Assembly.GetEntryAssembly().Location, string.Join(" ", Environment.GetCommandLineArgs().Skip(1)));
			} catch (Exception e) {
				ArchiLogger.LogGenericException(e);
			}

			Environment.Exit(0);
		}

		private static void Init() {
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

			InitServices();

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

		private static void InitServices() {
			string globalConfigFile = Path.Combine(SharedInfo.ConfigDirectory, SharedInfo.GlobalConfigFileName);

			GlobalConfig = GlobalConfig.Load(globalConfigFile);
			if (GlobalConfig == null) {
				ArchiLogger.LogGenericError("Global config could not be loaded, please make sure that " + globalConfigFile + " exists and is valid!");
				Exit(1);
			}

			string globalDatabaseFile = Path.Combine(SharedInfo.ConfigDirectory, SharedInfo.GlobalDatabaseFileName);

			GlobalDatabase = GlobalDatabase.Load(globalDatabaseFile);
			if (GlobalDatabase == null) {
				ArchiLogger.LogGenericError("Global database could not be loaded, if issue persists, please remove " + globalDatabaseFile + " in order to recreate database!");
				Exit(1);
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
			Init();
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			Application.Run(new MainForm());
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