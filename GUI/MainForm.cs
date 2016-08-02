using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using ArchiSteamFarm;

namespace GUI {
	internal sealed partial class MainForm : Form {
		internal MainForm() {
			InitializeComponent();
		}

		private void MainForm_Resize(object sender, EventArgs e) {
			switch (WindowState) {
				case FormWindowState.Minimized:
					MinimizeIcon.Visible = true;
					MinimizeIcon.ShowBalloonTip(5000);
					break;
				case FormWindowState.Normal:
					MinimizeIcon.Visible = false;
					break;
			}
		}

		private void MainForm_Load(object sender, EventArgs e) {
			Logging.InitFormLogger();

			if (!Directory.Exists(SharedInfo.ConfigDirectory)) {
				Logging.LogGenericError("Config directory could not be found!");
				Environment.Exit(1);
			}

			ASF.CheckForUpdate().Wait();

			// Before attempting to connect, initialize our list of CMs
			Bot.InitializeCMs(Program.GlobalDatabase.CellID, Program.GlobalDatabase.ServerListProvider);

			foreach (string botName in Directory.EnumerateFiles(SharedInfo.ConfigDirectory, "*.json").Select(Path.GetFileNameWithoutExtension)) {
				switch (botName) {
					case SharedInfo.ASF:
					case "example":
					case "minimal":
						continue;
				}

				Bot bot = new Bot(botName);
			}
		}

		private void MinimizeIcon_DoubleClick(object sender, EventArgs e) {
			Show();
			WindowState = FormWindowState.Normal;
		}

		private void MainForm_FormClosed(object sender, FormClosedEventArgs e) => Program.InitShutdownSequence();
	}
}
