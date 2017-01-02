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
using System.Windows.Forms;

namespace ConfigGenerator {
	internal sealed class EnhancedPropertyGrid : PropertyGrid {
		private readonly ASFConfig ASFConfig;

		internal EnhancedPropertyGrid(ASFConfig config) {
			if (config == null) {
				throw new ArgumentNullException(nameof(config));
			}

			ASFConfig = config;

			SelectedObject = config;
			Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
			Dock = DockStyle.Fill;
			HelpVisible = false;
			ToolbarVisible = false;
		}

		protected override void OnGotFocus(EventArgs args) {
			if (args == null) {
				Logging.LogNullError(nameof(args));
				return;
			}

			base.OnGotFocus(args);
			ASFConfig.Save();
		}

		protected override void OnPropertyValueChanged(PropertyValueChangedEventArgs args) {
			if (args == null) {
				Logging.LogNullError(nameof(args));
				return;
			}

			base.OnPropertyValueChanged(args);
			ASFConfig.Save();

			BotConfig botConfig = ASFConfig as BotConfig;
			if (botConfig != null) {
				if (!botConfig.Enabled) {
					return;
				}

				Tutorial.OnAction(Tutorial.EPhase.BotEnabled);
				if (!string.IsNullOrEmpty(botConfig.SteamLogin) && !string.IsNullOrEmpty(botConfig.SteamPassword)) {
					Tutorial.OnAction(Tutorial.EPhase.BotReady);
				}
				return;
			}

			GlobalConfig globalConfig = ASFConfig as GlobalConfig;
			if (globalConfig == null) {
				return;
			}

			if (globalConfig.SteamOwnerID != 0) {
				Tutorial.OnAction(Tutorial.EPhase.GlobalConfigReady);
			}
		}
	}
}