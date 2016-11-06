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

using System.Linq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NLog.Windows.Forms;

// ReSharper disable once CheckNamespace
namespace ArchiSteamFarm {
	internal static class Logging {
		private const string GeneralLayout = @"${date:format=yyyy-MM-dd HH\:mm\:ss} | ${level:uppercase=true} | ${logger} | ${message}${onexception:inner= | ${exception:format=toString,Data}}";

		private static bool IsUsingCustomConfiguration;

		internal static void InitCoreLoggers() {
			if (LogManager.Configuration == null) {
				LogManager.Configuration = new LoggingConfiguration();
			} else {
				// User provided custom NLog config, but we still need to define our own logger
				IsUsingCustomConfiguration = true;
				if (LogManager.Configuration.AllTargets.Any(target => target is MessageBoxTarget)) {
					return;
				}
			}

			MessageBoxTarget messageBoxTarget = new MessageBoxTarget {
				Name = "MessageBox",
				Layout = GeneralLayout
			};

			LogManager.Configuration.AddTarget(messageBoxTarget);
			LogManager.Configuration.LoggingRules.Add(new LoggingRule("*", LogLevel.Fatal, messageBoxTarget));
			LogManager.ReconfigExistingLoggers();
		}

		internal static void InitEnhancedLoggers() {
			if (IsUsingCustomConfiguration) {
				return;
			}

			FileTarget fileTarget = new FileTarget("File") {
				DeleteOldFileOnStartup = true,
				FileName = SharedInfo.LogFile,
				Layout = GeneralLayout
			};

			LogManager.Configuration.AddTarget(fileTarget);
			LogManager.Configuration.LoggingRules.Add(new LoggingRule("*", LogLevel.Debug, fileTarget));

			LogManager.ReconfigExistingLoggers();
		}

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
	}
}
