using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Threading.Tasks;
using System.Windows.Forms;
using ArchiSteamFarm.Localization;
using NLog.Targets;
using SteamKit2;

namespace ArchiSteamFarm {
	internal static class Program {
		internal static GlobalConfig GlobalConfig { get; private set; }
		internal static GlobalDatabase GlobalDatabase { get; private set; }
		internal static WebBrowser WebBrowser { get; private set; }

		private static bool ShutdownSequenceInitialized;

		internal static async Task Exit(byte exitCode = 0) {
			if (exitCode != 0) {
				ASF.ArchiLogger.LogGenericError(Strings.ErrorExitingWithNonZeroErrorCode);
			}

			await Shutdown().ConfigureAwait(false);
			Application.Exit();
		}

		internal static string GetUserInput(ASF.EUserInputType userInputType, string botName = SharedInfo.ASF, string extraInformation = null) => null; // TODO

		internal static async Task InitASF() {
			ASF.ArchiLogger.LogGenericInfo("ASF V" + SharedInfo.Version);

			await InitGlobalConfigAndLanguage().ConfigureAwait(false);

			if (!Runtime.IsRuntimeSupported) {
				ASF.ArchiLogger.LogGenericError(Strings.WarningRuntimeUnsupported);
				await Task.Delay(60 * 1000).ConfigureAwait(false);
			}

			await InitGlobalDatabaseAndServices().ConfigureAwait(false);

			// If debugging is on, we prepare debug directory prior to running
			if (GlobalConfig.Debug) {
				if (Directory.Exists(SharedInfo.DebugDirectory)) {
					try {
						Directory.Delete(SharedInfo.DebugDirectory, true);
						await Task.Delay(1000).ConfigureAwait(false); // Dirty workaround giving Windows some time to sync
					} catch (IOException e) {
						ASF.ArchiLogger.LogGenericException(e);
					}
				}

				Directory.CreateDirectory(SharedInfo.DebugDirectory);

				DebugLog.AddListener(new Debugging.DebugListener());
				DebugLog.Enabled = true;
			}

			await ASF.CheckForUpdate().ConfigureAwait(false);
			await ASF.InitBots().ConfigureAwait(false);
			ASF.InitEvents();
		}

		internal static void InitCore() {
			string homeDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
			if (!string.IsNullOrEmpty(homeDirectory)) {
				Directory.SetCurrentDirectory(homeDirectory);

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
						Directory.SetCurrentDirectory(homeDirectory);
					}
				}
			}

			Logging.InitLoggers();
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

		private static void Init() {
			AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionHandler;
			TaskScheduler.UnobservedTaskException += UnobservedTaskExceptionHandler;

			// We must register our logging target as soon as possible
			Target.Register<SteamTarget>("Steam");

			// The rest of ASF is initialized from MainForm.cs
		}

		private static async Task InitGlobalConfigAndLanguage() {
			string globalConfigFile = Path.Combine(SharedInfo.ConfigDirectory, SharedInfo.GlobalConfigFileName);

			GlobalConfig = GlobalConfig.Load(globalConfigFile);
			if (GlobalConfig == null) {
				ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorGlobalConfigNotLoaded, globalConfigFile));
				await Task.Delay(5 * 1000).ConfigureAwait(false);
				await Exit(1).ConfigureAwait(false);
				return;
			}

			if (!string.IsNullOrEmpty(GlobalConfig.CurrentCulture)) {
				try {
					// GetCultureInfo() would be better but we can't use it for specifying neutral cultures such as "en"
					CultureInfo culture = CultureInfo.CreateSpecificCulture(GlobalConfig.CurrentCulture);
					CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = culture;
				} catch (CultureNotFoundException) {
					ASF.ArchiLogger.LogGenericError(Strings.ErrorInvalidCurrentCulture);
				}
			}

			ushort defaultResourceSetCount = 0;
			ResourceSet defaultResourceSet = Strings.ResourceManager.GetResourceSet(CultureInfo.GetCultureInfo("en-US"), true, true);
			if (defaultResourceSet != null) {
				defaultResourceSetCount = (ushort) defaultResourceSet.Cast<object>().Count();
			}

			if (defaultResourceSetCount == 0) {
				return;
			}

			ushort currentResourceSetCount = 0;
			ResourceSet currentResourceSet = Strings.ResourceManager.GetResourceSet(CultureInfo.CurrentCulture, true, false);
			if (currentResourceSet != null) {
				currentResourceSetCount = (ushort) currentResourceSet.Cast<object>().Count();
			}

			if (currentResourceSetCount < defaultResourceSetCount) {
				// We don't want to report "en-AU" as 0.00% only because we don't have it as a dialect, if "en" is available and translated
				// This typically will work only for English, as e.g. "nl-BE" doesn't fallback to "nl-NL", but "nl", and "nl" will be empty
				ushort neutralResourceSetCount = 0;
				ResourceSet neutralResourceSet = Strings.ResourceManager.GetResourceSet(CultureInfo.CurrentCulture.Parent, true, false);
				if (neutralResourceSet != null) {
					neutralResourceSetCount = (ushort) neutralResourceSet.Cast<object>().Count();
				}

				if (neutralResourceSetCount < defaultResourceSetCount) {
					float translationCompleteness = currentResourceSetCount / (float) defaultResourceSetCount;
					ASF.ArchiLogger.LogGenericInfo(string.Format(Strings.TranslationIncomplete, CultureInfo.CurrentCulture.Name, translationCompleteness.ToString("P1")));
				}
			}
		}

		private static async Task InitGlobalDatabaseAndServices() {
			string globalDatabaseFile = Path.Combine(SharedInfo.ConfigDirectory, SharedInfo.GlobalDatabaseFileName);

			if (!File.Exists(globalDatabaseFile)) {
				ASF.ArchiLogger.LogGenericInfo(Strings.Welcome);
				ASF.ArchiLogger.LogGenericWarning(Strings.WarningPrivacyPolicy);
				await Task.Delay(15 * 1000).ConfigureAwait(false);
			}

			GlobalDatabase = GlobalDatabase.Load(globalDatabaseFile);
			if (GlobalDatabase == null) {
				ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorDatabaseInvalid, globalDatabaseFile));
				await Task.Delay(5 * 1000).ConfigureAwait(false);
				await Exit(1).ConfigureAwait(false);
				return;
			}

			ArchiWebHandler.Init();
			OS.Init();
			WebBrowser.Init();

			WebBrowser = new WebBrowser(ASF.ArchiLogger);
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

		private static async Task Shutdown() {
			if (!await InitShutdownSequence().ConfigureAwait(false)) {
				return;
			}

			Application.Exit();
		}

		private static async void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs args) {
			if (args?.ExceptionObject == null) {
				ASF.ArchiLogger.LogNullError(nameof(args) + " || " + nameof(args.ExceptionObject));
				return;
			}

			ASF.ArchiLogger.LogFatalException((Exception) args.ExceptionObject);
			await Task.Delay(5000).ConfigureAwait(false);
			await Exit(1).ConfigureAwait(false);
		}

		private static void UnobservedTaskExceptionHandler(object sender, UnobservedTaskExceptionEventArgs args) {
			if (args?.Exception == null) {
				ASF.ArchiLogger.LogNullError(nameof(args) + " || " + nameof(args.Exception));
				return;
			}

			ASF.ArchiLogger.LogFatalException(args.Exception);
			// Normally we should abort the application here, but many tasks are in fact failing in SK2 code which we can't easily fix
			// Thanks Valve.
		}
	}
}