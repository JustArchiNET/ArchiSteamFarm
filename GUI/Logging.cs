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

using NLog;
using NLog.Config;
using NLog.Targets;
using NLog.Windows.Forms;

namespace ArchiSteamFarm {
	internal static class Logging {
		private const string GeneralLayout = @"${date:format=yyyy-MM-dd HH\:mm\:ss}|${processname}-${processid}|${level:uppercase=true}|${logger}|${message}${onexception:inner= ${exception:format=toString,Data}}";

		internal static void InitFormLogger() {
			RichTextBoxTarget formControlTarget = new RichTextBoxTarget {
				AutoScroll = true,
				ControlName = "LogTextBox",
				FormName = "MainForm",
				Layout = GeneralLayout,
				MaxLines = byte.MaxValue,
				Name = "RichTextBox"
			};

			formControlTarget.RowColoringRules.Add(new RichTextBoxRowColoringRule("level >= LogLevel.Error", "Red", "Black"));
			formControlTarget.RowColoringRules.Add(new RichTextBoxRowColoringRule("level >= LogLevel.Warn", "Yellow", "Black"));

			LogManager.Configuration.AddTarget(formControlTarget);
			LogManager.Configuration.LoggingRules.Add(new LoggingRule("*", LogLevel.Debug, formControlTarget));

			LogManager.ReconfigExistingLoggers();
		}

		internal static void InitLoggers() {
			if (LogManager.Configuration != null) {
				// User provided custom NLog config, or we have it set already, so don't override it
				return;
			}

			LoggingConfiguration config = new LoggingConfiguration();

			FileTarget fileTarget = new FileTarget("File") {
				DeleteOldFileOnStartup = true,
				FileName = SharedInfo.LogFile,
				Layout = GeneralLayout
			};

			config.AddTarget(fileTarget);
			config.LoggingRules.Add(new LoggingRule("*", LogLevel.Debug, fileTarget));

			MessageBoxTarget messageBoxTarget = new MessageBoxTarget {
				Name = "MessageBox",
				Layout = GeneralLayout
			};

			config.AddTarget(messageBoxTarget);
			config.LoggingRules.Add(new LoggingRule("*", LogLevel.Fatal, messageBoxTarget));

			LogManager.Configuration = config;
		}
	}
}