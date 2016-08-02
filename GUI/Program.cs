using System;
using System.Windows.Forms;
using GUI;

// ReSharper disable once CheckNamespace
namespace ArchiSteamFarm {
	internal static class Program {
		internal static bool IsRunningAsService => false;

		internal static GlobalConfig GlobalConfig { get; private set; }
		internal static GlobalDatabase GlobalDatabase { get; private set; }
		internal static WebBrowser WebBrowser { get; private set; }

		internal static string GetUserInput(SharedInfo.EUserInputType userInputType, string botName = SharedInfo.ASF, string extraInformation = null) {
			return null;
		}

		internal static void Exit() {

		}

		internal static void Restart() {

		}

		internal static void OnBotShutdown() {

		}

		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		[STAThread]
		private static void Main() {
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			Application.Run(new Form1());
		}
	}
}
