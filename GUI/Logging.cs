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
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using NLog;
using NLog.Config;
using NLog.Targets;
using NLog.Windows.Forms;

// ReSharper disable once CheckNamespace
namespace ArchiSteamFarm {
	internal static class Logging {
		private const string GeneralLayout = @"${date:format=yyyy-MM-dd HH\:mm\:ss} | ${level:uppercase=true} | ${message}${onexception:inner= | ${exception:format=toString,Data}}";

		private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

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
			LogManager.Configuration.LoggingRules.Add(new LoggingRule("*", LogLevel.Trace, fileTarget));

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
			LogManager.Configuration.LoggingRules.Add(new LoggingRule("*", LogLevel.Trace, formControlTarget));

			LogManager.ReconfigExistingLoggers();
		}

		internal static void LogGenericError(string message, string botName = SharedInfo.ASF, [CallerMemberName] string previousMethodName = null) {
			if (string.IsNullOrEmpty(message)) {
				LogNullError(nameof(message), botName);
				return;
			}

			Logger.Error($"{botName}|{previousMethodName}() {message}");
		}

		internal static void LogGenericException(Exception exception, string botName = SharedInfo.ASF, [CallerMemberName] string previousMethodName = null) {
			if (exception == null) {
				LogNullError(nameof(exception), botName);
				return;
			}

			Logger.Error(exception, $"{botName}|{previousMethodName}()");
		}

		internal static void LogFatalException(Exception exception, string botName = SharedInfo.ASF, [CallerMemberName] string previousMethodName = null) {
			if (exception == null) {
				LogNullError(nameof(exception), botName);
				return;
			}

			Logger.Fatal(exception, $"{botName}|{previousMethodName}()");

			// If LogManager has been initialized already, don't do anything else
			if (LogManager.Configuration != null) {
				return;
			}

			// Otherwise, if we run into fatal exception before logging module is even initialized, write exception to classic log file
			File.WriteAllText(SharedInfo.LogFile, DateTime.Now + " ASF V" + SharedInfo.Version + " has run into fatal exception before core logging module was even able to initialize!" + Environment.NewLine);

			while (true) {
				File.AppendAllText(SharedInfo.LogFile, "[!] EXCEPTION: " + previousMethodName + "() " + exception.Message + Environment.NewLine + "StackTrace:" + Environment.NewLine + exception.StackTrace);
				if (exception.InnerException != null) {
					exception = exception.InnerException;
					continue;
				}

				break;
			}
		}

		internal static void LogGenericWarning(string message, string botName = SharedInfo.ASF, [CallerMemberName] string previousMethodName = null) {
			if (string.IsNullOrEmpty(message)) {
				LogNullError(nameof(message), botName);
				return;
			}

			Logger.Warn($"{botName}|{previousMethodName}() {message}");
		}

		internal static void LogGenericInfo(string message, string botName = SharedInfo.ASF, [CallerMemberName] string previousMethodName = null) {
			if (string.IsNullOrEmpty(message)) {
				LogNullError(nameof(message), botName);
				return;
			}

			Logger.Info($"{botName}|{previousMethodName}() {message}");
		}

		[SuppressMessage("ReSharper", "ExplicitCallerInfoArgument")]
		internal static void LogNullError(string nullObjectName, string botName = SharedInfo.ASF, [CallerMemberName] string previousMethodName = null) {
			if (string.IsNullOrEmpty(nullObjectName)) {
				return;
			}

			LogGenericError(nullObjectName + " is null!", botName, previousMethodName);
		}

		[SuppressMessage("ReSharper", "UnusedMember.Global")]
		internal static void LogGenericDebug(string message, string botName = SharedInfo.ASF, [CallerMemberName] string previousMethodName = null) {
			if (string.IsNullOrEmpty(message)) {
				LogNullError(nameof(message), botName);
				return;
			}

			Logger.Debug($"{botName}|{previousMethodName}() {message}");
		}
	}
}
