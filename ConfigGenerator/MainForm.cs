/*
    _                _      _  ____   _                           _____
   / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
  / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
 / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
/_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|

 Copyright 2015-2016 Łukasz "JustArchi" Domeradzki
 Contact: JustArchi@JustArchi.net

 Licensed under the Apache License, Version 2.0 (the "License");
 you may not use this file except in compliance with the License.
 You may obtain a copy of the License at

 http://www.apache.org/licenses/LICENSE-2.0
					
 Unless required by applicable law or agreed to in writing, software
 distributed under the License is distributed on an "AS IS" BASIS,
 WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 See the License for the specific language governing permissions and
 limitations under the License.

*/

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ConfigGenerator {
	internal sealed partial class MainForm : Form {
		private const byte ReservedTabs = 3;

		private readonly TabPage NewTab = new TabPage { Text = "+" };
		private readonly TabPage RemoveTab = new TabPage { Text = "-" };
		private readonly TabPage RenameTab = new TabPage { Text = "~" };

		private ConfigPage ASFTab;
		private TabPage OldTab;

		internal MainForm() {
			InitializeComponent();
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
				Tutorial.Enabled = false;
			}

			MainTab.TabPages.AddRange(new[] { RemoveTab, RenameTab, NewTab });
			Tutorial.OnAction(Tutorial.EPhase.Start);
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

				MainTab.SelectedTab = configPage;

				if (DialogBox.YesNoBox("Removal", "Do you really want to remove this config?") != DialogResult.Yes) {
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

				string input;
				if (DialogBox.InputBox("Rename", "Your new bot name:", out input) != DialogResult.OK) {
					return;
				}

				if (string.IsNullOrEmpty(input)) {
					Logging.LogGenericError("Your bot name is empty!");
					return;
				}

				// Get rid of any potential whitespaces in bot name
				input = Regex.Replace(input, @"\s+", "");

				configPage.ASFConfig.Rename(input);
				configPage.RefreshText();
			} else if (e.TabPage == NewTab) {
				ConfigPage configPage = OldTab as ConfigPage;
				if (configPage == null) {
					MainTab.SelectedIndex = -1;
					return;
				}

				MainTab.SelectedTab = configPage;

				Tutorial.OnAction(Tutorial.EPhase.BotNickname);

				string input;
				if (DialogBox.InputBox("New", "Your new bot name:", out input) != DialogResult.OK) {
					return;
				}

				if (string.IsNullOrEmpty(input)) {
					Logging.LogGenericError("Your bot name is empty!");
					return;
				}

				// Get rid of any potential whitespaces in bot name
				input = Regex.Replace(input, @"\s+", "");

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
				Tutorial.OnAction(Tutorial.EPhase.BotNicknameFinished);
			} else if (e.TabPage == ASFTab) {
				Tutorial.OnAction(Tutorial.EPhase.GlobalConfigOpened);
			}
		}

		private void MainTab_Deselecting(object sender, TabControlCancelEventArgs e) {
			if (sender == null || e == null) {
				return;
			}

			OldTab = e.TabPage;
		}

		private void MainForm_Shown(object sender, EventArgs e) {
			if (sender == null || e == null) {
				return;
			}

			Tutorial.OnAction(Tutorial.EPhase.Shown);
		}

		private void MainForm_HelpButtonClicked(object sender, CancelEventArgs e) {
			if (sender == null || e == null) {
				return;
			}

			e.Cancel = true;
			Tutorial.OnAction(Tutorial.EPhase.Help);
			Process.Start("https://github.com/JustArchi/ArchiSteamFarm/wiki/Configuration");
			Tutorial.OnAction(Tutorial.EPhase.HelpFinished);
		}
	}
}
