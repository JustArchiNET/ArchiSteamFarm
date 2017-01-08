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

using ConfigGenerator.Localization;

namespace ConfigGenerator {
	internal static class Tutorial {
		internal static bool Enabled { private get; set; } = true;

		private static EPhase NextPhase = EPhase.Start;

		internal static void OnAction(EPhase phase) {
			if (!Enabled || (phase != NextPhase)) {
				return;
			}

			switch (phase) {
				case EPhase.Unknown:
				case EPhase.Finished:
					break;
				case EPhase.Start:
					Logging.LogGenericInfoWithoutStacktrace(CGStrings.TutorialStart);
					break;
				case EPhase.Shown:
					Logging.LogGenericInfoWithoutStacktrace(CGStrings.TutorialMainFormShown);
					Logging.LogGenericInfoWithoutStacktrace(CGStrings.TutorialMainFormBotsManagementButtons);
					Logging.LogGenericInfoWithoutStacktrace(CGStrings.TutorialMainFormConfigurationWindow);

					if (!Runtime.IsRunningOnMono) {
						Logging.LogGenericInfoWithoutStacktrace(CGStrings.TutorialMainFormHelpButton);
					}

					Logging.LogGenericInfoWithoutStacktrace(CGStrings.TutorialMainFormConfigurationWiki);
					Logging.LogGenericInfoWithoutStacktrace(CGStrings.TutorialMainFormFinished);

					break;
				case EPhase.BotNickname:
					Logging.LogGenericInfoWithoutStacktrace(CGStrings.TutorialNewBotFormShown);
					break;
				case EPhase.BotNicknameFinished:
					Logging.LogGenericInfoWithoutStacktrace(string.Format(CGStrings.TutorialNewBotFormFinished, nameof(BotConfig.Enabled)));
					break;
				case EPhase.BotEnabled:
					Logging.LogGenericInfoWithoutStacktrace(string.Format(CGStrings.TutorialBotFormEnabled, nameof(BotConfig.SteamLogin), nameof(BotConfig.SteamPassword)));
					break;
				case EPhase.BotReady:
					Logging.LogGenericInfoWithoutStacktrace(CGStrings.TutorialBotFormReady);
					Logging.LogGenericInfoWithoutStacktrace(CGStrings.TutorialFinished);
					Enabled = false;
					break;
			}

			NextPhase++;
		}

		internal enum EPhase : byte {
			Unknown,
			Start,
			Shown,
			BotNickname,
			BotNicknameFinished,
			BotEnabled,
			BotReady,
			Finished
		}
	}
}