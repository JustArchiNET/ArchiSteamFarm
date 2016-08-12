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

namespace ConfigGenerator {
	internal static class Tutorial {
		internal enum EPhase : byte {
			Unknown,
			Start,
			Shown,
			Help,
			HelpFinished,
			BotNickname,
			BotNicknameFinished,
			BotEnabled,
			BotReady,
			GlobalConfigOpened,
			GlobalConfigReady
		}

		internal static bool Enabled { private get; set; } = true;

		private static EPhase NextPhase = EPhase.Start;

		internal static void OnAction(EPhase phase) {
			if (!Enabled || (phase != NextPhase)) {
				return;
			}

			switch (phase) {
				case EPhase.Unknown:
					break;
				case EPhase.Start:
					Logging.LogGenericInfoWithoutStacktrace("Hello there! I noticed that you're using ASF Config Generator for the first time, so let me help you a bit.");
					break;
				case EPhase.Shown:
					Logging.LogGenericInfoWithoutStacktrace("You can now notice the main ASF Config Generator screen, it's really easy to use!");
					Logging.LogGenericInfoWithoutStacktrace("At the top of the window you can notice currently loaded configs, and 3 extra buttons for removing, renaming and adding new ones.");
					Logging.LogGenericInfoWithoutStacktrace("In the middle of the window you will be able to configure all config properties that are available for you.");
					if (!Runtime.IsRunningOnMono) {
						Logging.LogGenericInfoWithoutStacktrace("In the top right corner you can find help button [?] which will redirect you to ASF wiki where you can find more information.");
						Logging.LogGenericInfoWithoutStacktrace("Please click the help button to continue.");
					} else {
						Logging.LogGenericInfoWithoutStacktrace("Please visit ASF wiki if you're in doubt - you can find more information there.");
						Logging.LogGenericInfoWithoutStacktrace("Alright, let's start configuring our ASF. Click on the plus [+] button to add your first steam account to ASF!");
						NextPhase = EPhase.HelpFinished;
					}
					break;
				case EPhase.Help:
					Logging.LogGenericInfoWithoutStacktrace("Well done! On ASF wiki you can find detailed help about every config property you're going to configure in a moment.");
					break;
				case EPhase.HelpFinished:
					Logging.LogGenericInfoWithoutStacktrace("Alright, let's start configuring our ASF. Click on the plus [+] button to add your first steam account to ASF!");
					break;
				case EPhase.BotNickname:
					Logging.LogGenericInfoWithoutStacktrace("Good job! You'll be asked for your bot name now. A good example would be a nickname that you're using for the steam account you're configuring right now, or any other name of your choice which will be easy for you to connect with bot instance that is being configured. Please don't use spaces in the name.");
					break;
				case EPhase.BotNicknameFinished:
					Logging.LogGenericInfoWithoutStacktrace("As you can see your bot config is now ready to configure!");
					Logging.LogGenericInfoWithoutStacktrace("First thing that you want to do is switching \"Enabled\" property from False to True, try it!");
					break;
				case EPhase.BotEnabled:
					Logging.LogGenericInfoWithoutStacktrace("Excellent! Now your bot instance is enabled. You need to configure at least 2 more config properties - \"SteamLogin\" and \"SteamPassword\". The tutorial will continue after you're done with it. Remember to visit ASF wiki by clicking the help icon if you're unsure how given property should be configured!");
					break;
				case EPhase.BotReady:
					Logging.LogGenericInfoWithoutStacktrace("If the data you put is proper, then your bot is ready to run! We need to do only one more thing now. Visit global ASF config, which is labelled as \"ASF\" on your config tab.");
					break;
				case EPhase.GlobalConfigOpened:
					Logging.LogGenericInfoWithoutStacktrace("While bot config affects only given bot instance you're configuring, global config affects whole ASF process, including all configured bots.");
					Logging.LogGenericInfoWithoutStacktrace("In order to fully configure your ASF, I suggest to fill \"SteamOwnerID\" property. Remember, if you don't know what to put, help button is always there for you!");
					break;
				case EPhase.GlobalConfigReady:
					Logging.LogGenericInfoWithoutStacktrace("Your ASF is now ready! Simply launch ASF process by double-clicking ASF.exe binary and if you did everything properly, you should now notice that ASF logs in on your account and starts farming. If you have SteamGuard or 2FA authorization enabled, ASF will ask you for that once");
					Logging.LogGenericInfoWithoutStacktrace("Congratulations! You've done everything that is needed in order to make ASF \"work\". I highly recommend reading the wiki now, as ASF offers some really neat features for you to configure, such as offline farming or deciding upon most efficient cards farming algorithm.");
					Logging.LogGenericInfoWithoutStacktrace("If you'd like to add another steam account for farming, simply click the plus [+] button and add another instance. You can also rename bots [~] and remove them [-]. Good luck!");
					Enabled = false;
					break;
			}

			NextPhase++;
		}
	}
}
