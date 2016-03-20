using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace ConfigGenerator {
	public partial class MainForm : Form {
		private const byte ReservedTabs = 3;

		private ConfigPage ASFTab;

		private TabPage RemoveTab = new TabPage() {
			Text = "-",
		};

		private TabPage RenameTab = new TabPage() {
			Text = "~",
		};

		private TabPage NewTab = new TabPage() {
			Text = "+",
		};

		private TabPage OldTab;

		public MainForm() {
			InitializeComponent();
		}

		private void BotMenuNew_Click(object sender, EventArgs e) {
			if (sender == null || e == null) {
				return;
			}

			Logging.LogGenericError("This option is not ready yet!");
		}

		private void BotMenuDelete_Click(object sender, EventArgs e) {
			if (sender == null || e == null) {
				return;
			}

			Logging.LogGenericError("This option is not ready yet!");
		}

		private void FileMenuHelp_Click(object sender, EventArgs e) {
			if (sender == null || e == null) {
				return;
			}

			Process.Start("https://github.com/JustArchi/ArchiSteamFarm/wiki/Configuration");
		}

		private void FileMenuExit_Click(object sender, EventArgs e) {
			if (sender == null || e == null) {
				return;
			}

			Application.Exit();
		}

		private void MainForm_Load(object sender, EventArgs e) {
			if (sender == null || e == null) {
				return;
			}

			ASFTab = new ConfigPage(GlobalConfig.Load(Path.Combine(Program.ConfigDirectory, Program.GlobalConfigFile)));

			MainTab.TabPages.Add(ASFTab);

			foreach (var configFile in Directory.EnumerateFiles(Program.ConfigDirectory, "*.json")) {
				string botName = Path.GetFileNameWithoutExtension(configFile);
				switch (botName) {
					case Program.ASF:
					case "example":
					case "minimal":
						continue;
				}

				MainTab.TabPages.Add(new ConfigPage(BotConfig.Load(configFile)));
			}

			MainTab.TabPages.AddRange(new TabPage[] { RemoveTab, RenameTab, NewTab });
		}

		private void MainTab_Selected(object sender, TabControlEventArgs e) {
			if (sender == null || e == null) {
				return;
			}

			if (e.TabPage == RemoveTab) {
				ConfigPage configPage = OldTab as ConfigPage;
				if (configPage == null) {
					MainTab.SelectedIndex = -1;
					return;
				}

				if (configPage == ASFTab) {
					MainTab.SelectedTab = ASFTab;
					Logging.LogGenericError("You can't remove global config!");
					return;
				}

				MainTab.SelectedIndex = 0;
				configPage.ASFConfig.Remove();
				MainTab.TabPages.Remove(configPage);
			} else if (e.TabPage == RenameTab) {
				ConfigPage configPage = OldTab as ConfigPage;
				if (configPage == null) {
					MainTab.SelectedIndex = -1;
					return;
				}

				if (configPage == ASFTab) {
					MainTab.SelectedTab = ASFTab;
					Logging.LogGenericError("You can't rename global config!");
					return;
				}

				MainTab.SelectedTab = configPage;

				string input = null;
				if (DialogBox.InputBox("Rename", "Your new bot name:", ref input) != DialogResult.OK) {
					return;
				}

				if (string.IsNullOrEmpty(input)) {
					Logging.LogGenericError("Your bot name is empty!");
					return;
				}

				configPage.ASFConfig.Rename(input);
				configPage.RefreshText();
			} else if (e.TabPage == NewTab) {
				ConfigPage configPage = OldTab as ConfigPage;
				if (configPage == null) {
					MainTab.SelectedIndex = -1;
					return;
				}

				MainTab.SelectedTab = configPage;

				string input = null;
				if (DialogBox.InputBox("Rename", "Your new bot name:", ref input) != DialogResult.OK) {
					return;
				}

				if (string.IsNullOrEmpty(input)) {
					Logging.LogGenericError("Your bot name is empty!");
					return;
				}

				foreach (ASFConfig config in ASFConfig.ASFConfigs) {
					if (Path.GetFileNameWithoutExtension(config.FilePath).Equals(input)) {
						Logging.LogGenericError("Bot with such name exists already!");
						return;
					}
				}

				input = Path.Combine(Program.ConfigDirectory, input + ".json");

				ConfigPage newConfigPage = new ConfigPage(BotConfig.Load(input));
				MainTab.TabPages.Insert(MainTab.TabPages.Count - ReservedTabs, newConfigPage);
				MainTab.SelectedTab = newConfigPage;
			}
		}

		private void MainTab_Deselecting(object sender, TabControlCancelEventArgs e) {
			if (sender == null || e == null) {
				return;
			}

			OldTab = e.TabPage;
		}
	}
}
