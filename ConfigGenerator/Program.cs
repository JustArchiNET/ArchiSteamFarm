using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ConfigGenerator {
	internal static class Program {
		internal const string ASF = "ASF";
		internal const string ConfigDirectory = "config";
		internal const string GlobalConfigFile = ASF + ".json";

		private const string ASFDirectory = "ArchiSteamFarm";

		private static readonly Assembly Assembly = Assembly.GetExecutingAssembly();
		private static readonly string ExecutableFile = Assembly.Location;
		private static readonly string ExecutableName = Path.GetFileName(ExecutableFile);
		private static readonly string ExecutableDirectory = Path.GetDirectoryName(ExecutableFile);

		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		[STAThread]
		private static void Main() {
			Init();
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			Application.Run(new MainForm());
		}

		private static void Init() {
			AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionHandler;
			TaskScheduler.UnobservedTaskException += UnobservedTaskExceptionHandler;

			Directory.SetCurrentDirectory(ExecutableDirectory);

			// Allow loading configs from source tree if it's a debug build
			if (Debugging.IsDebugBuild) {

				// Common structure is bin/(x64/)Debug/ArchiSteamFarm.exe, so we allow up to 4 directories up
				for (byte i = 0; i < 4; i++) {
					Directory.SetCurrentDirectory("..");
					if (Directory.Exists(ASFDirectory)) {
						Directory.SetCurrentDirectory(ASFDirectory);
						break;
					}
				}

				// If config directory doesn't exist after our adjustment, abort all of that
				if (!Directory.Exists(ConfigDirectory)) {
					Directory.SetCurrentDirectory(ExecutableDirectory);
				}
			}

			if (!Directory.Exists(ConfigDirectory)) {
				Logging.LogGenericError("Config directory could not be found!");
				Application.Exit();
			}
		}

		private static void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs args) {
			if (sender == null || args == null) {
				return;
			}

			Logging.LogGenericException((Exception) args.ExceptionObject);
		}

		private static void UnobservedTaskExceptionHandler(object sender, UnobservedTaskExceptionEventArgs args) {
			if (sender == null || args == null) {
				return;
			}

			Logging.LogGenericException(args.Exception);
		}
	}
}
