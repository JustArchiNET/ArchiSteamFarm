/*
    _                _      _  ____   _                           _____
   / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
  / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
 / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
/_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|

 Copyright 2015-2017 Łukasz "JustArchi" Domeradzki
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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using ArchiSteamFarm;
using ConfigGenerator.Localization;

namespace ConfigGenerator {
	internal sealed partial class MainForm : Form {
		private const string GitHubWikiConfigurationURL = "https://github.com/" + SharedInfo.GithubRepo + "/wiki/Configuration";
		private const byte ReservedTabs = 3;

		private readonly TabPage NewTab = new TabPage { Text = @"+" };
		private readonly TabPage RemoveTab = new TabPage { Text = @"-" };
		private readonly TabPage RenameTab = new TabPage { Text = @"~" };

		private ConfigPage ASFTab;
		private TabPage OldTab;

		internal MainForm() {
			InitializeComponent();
		}

		private void MainForm_HelpButtonClicked(object sender, CancelEventArgs args) {
			if ((sender == null) || (args == null)) {
				Logging.LogNullError(nameof(sender) + " || " + nameof(args));
				return;
			}

			args.Cancel = true;
			Process.Start(GitHubWikiConfigurationURL);
		}

		private void MainForm_Load(object sender, EventArgs args) {
			if ((sender == null) || (args == null)) {
				Logging.LogNullError(nameof(sender) + " || " + nameof(args));
				return;
			}

			GlobalConfig globalConfig = GlobalConfig.Load(Path.Combine(SharedInfo.ConfigDirectory, SharedInfo.GlobalConfigFileName));
			if (!string.IsNullOrEmpty(globalConfig.CurrentCulture)) {
				try {
					// GetCultureInfo() would be better but we can't use it for specifying neutral cultures such as "en"
					CultureInfo culture = CultureInfo.CreateSpecificCulture(globalConfig.CurrentCulture);
					CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = culture;
				} catch (CultureNotFoundException) {
					Logging.LogGenericErrorWithoutStacktrace(CGStrings.ErrorInvalidCurrentCulture);
				}
			}

			ASFTab = new ConfigPage(globalConfig);
			MainTab.TabPages.Add(ASFTab);

			foreach (string configFile in Directory.EnumerateFiles(SharedInfo.ConfigDirectory, "*.json")) {
				string botName = Path.GetFileNameWithoutExtension(configFile);
				switch (botName) {
					case SharedInfo.ASF:
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

		private void MainForm_Shown(object sender, EventArgs args) {
			if ((sender == null) || (args == null)) {
				Logging.LogNullError(nameof(sender) + " || " + nameof(args));
				return;
			}

			Tutorial.OnAction(Tutorial.EPhase.Shown);
		}

		private void MainTab_Deselecting(object sender, TabControlCancelEventArgs args) {
			if ((sender == null) || (args == null)) {
				Logging.LogNullError(nameof(sender) + " || " + nameof(args));
				return;
			}

			OldTab = args.TabPage;
		}

		private void MainTab_Selected(object sender, TabControlEventArgs args) {
			if ((sender == null) || (args == null)) {
				Logging.LogNullError(nameof(sender) + " || " + nameof(args));
				return;
			}

			if (args.TabPage == RemoveTab) {
				ConfigPage configPage = OldTab as ConfigPage;
				if (configPage == null) {
					MainTab.SelectedIndex = -1;
					return;
				}

				if (configPage == ASFTab) {
					MainTab.SelectedTab = ASFTab;
					Logging.LogGenericErrorWithoutStacktrace(CGStrings.ErrorCantRemoveGlobalConfig);
					return;
				}

				MainTab.SelectedTab = configPage;

				if (DialogBox.YesNoBox(CGStrings.Removal, CGStrings.ConfirmRemoval) != DialogResult.Yes) {
					return;
				}

				MainTab.SelectedIndex = 0;
				configPage.ASFConfig.Remove();
				MainTab.TabPages.Remove(configPage);
			} else if (args.TabPage == RenameTab) {
				ConfigPage configPage = OldTab as ConfigPage;
				if (configPage == null) {
					MainTab.SelectedIndex = -1;
					return;
				}

				if (configPage == ASFTab) {
					MainTab.SelectedTab = ASFTab;
					Logging.LogGenericErrorWithoutStacktrace(CGStrings.ErrorCantRenameGlobalConfig);
					return;
				}

				MainTab.SelectedTab = configPage;

				string input;
				if (DialogBox.InputBox(CGStrings.Rename, CGStrings.UserInputBotName, out input) != DialogResult.OK) {
					return;
				}

				if (string.IsNullOrEmpty(input)) {
					Logging.LogGenericErrorWithoutStacktrace(CGStrings.ErrorBotNameEmpty);
					return;
				}

				// Get rid of any potential whitespaces in bot name
				input = Regex.Replace(input, @"\s+", "");

				configPage.ASFConfig.Rename(input);
				configPage.RefreshText();
			} else if (args.TabPage == NewTab) {
				ConfigPage configPage = OldTab as ConfigPage;
				if (configPage == null) {
					MainTab.SelectedIndex = -1;
					return;
				}

				MainTab.SelectedTab = configPage;

				Tutorial.OnAction(Tutorial.EPhase.BotNickname);

				string input;
				if (DialogBox.InputBox(CGStrings.New, CGStrings.UserInputBotName, out input) != DialogResult.OK) {
					return;
				}

				if (string.IsNullOrEmpty(input)) {
					Logging.LogGenericErrorWithoutStacktrace(CGStrings.ErrorBotNameEmpty);
					return;
				}

				// Get rid of any potential whitespaces in bot name
				input = Regex.Replace(input, @"\s+", "");

				if (string.IsNullOrEmpty(input)) {
					Logging.LogGenericErrorWithoutStacktrace(CGStrings.ErrorBotNameEmpty);
					return;
				}

				switch (input) {
					case SharedInfo.ASF:
					case "example":
					case "minimal":
						Logging.LogGenericErrorWithoutStacktrace(CGStrings.ErrorNameReserved);
						return;
				}

				if (ASFConfig.ASFConfigs.Select(config => Path.GetFileNameWithoutExtension(config.FilePath)).Any(fileNameWithoutExtension => (fileNameWithoutExtension == null) || fileNameWithoutExtension.Equals(input))) {
					Logging.LogGenericErrorWithoutStacktrace(CGStrings.ErrorNameAlreadyUsed);
					return;
				}

				input = Path.Combine(SharedInfo.ConfigDirectory, input + ".json");

				ConfigPage newConfigPage = new ConfigPage(BotConfig.Load(input));
				MainTab.TabPages.Insert(MainTab.TabPages.Count - ReservedTabs, newConfigPage);
				MainTab.SelectedTab = newConfigPage;
				Tutorial.OnAction(Tutorial.EPhase.BotNicknameFinished);
			}
		}
	}
}