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
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

namespace ArchiSteamFarm {
	internal static class Logging {
		private static readonly object FileLock = new object();

		private static bool LogToFile = false;

		internal static void Init() {
			LogToFile = Program.GlobalConfig.LogToFile;

			lock (FileLock) {
				try {
					File.Delete(Program.LogFile);
				} catch (Exception e) {
					LogGenericException(e);
				}
			}
		}

		internal static void LogGenericWTF(string message, string botName = "Main", [CallerMemberName] string previousMethodName = "") {
			if (string.IsNullOrEmpty(message)) {
				return;
			}

			Log("[!!] WTF: " + previousMethodName + "() <" + botName + "> " + message + ", WTF?");
		}

		internal static void LogGenericError(string message, string botName = "Main", [CallerMemberName] string previousMethodName = "") {
			if (string.IsNullOrEmpty(message)) {
				return;
			}

			Log("[!!] ERROR: " + previousMethodName + "() <" + botName + "> " + message);
		}

		internal static void LogGenericException(Exception exception, string botName = "Main", [CallerMemberName] string previousMethodName = "") {
			if (exception == null) {
				return;
			}

			Log("[!] EXCEPTION: " + previousMethodName + "() <" + botName + "> " + exception.Message);
			Log("[!] StackTrace:" + Environment.NewLine + exception.StackTrace);

			if (exception.InnerException != null) {
				LogGenericException(exception.InnerException, botName, previousMethodName);
			}
		}

		internal static void LogGenericWarning(string message, string botName = "Main", [CallerMemberName] string previousMethodName = "") {
			if (string.IsNullOrEmpty(message)) {
				return;
			}

			Log("[!] WARNING: " + previousMethodName + "() <" + botName + "> " + message);
		}

		internal static void LogGenericInfo(string message, string botName = "Main", [CallerMemberName] string previousMethodName = "") {
			if (string.IsNullOrEmpty(message)) {
				return;
			}

			Log("[*] INFO: " + previousMethodName + "() <" + botName + "> " + message);
		}

		internal static void LogNullError(string nullObjectName, string botName = "Main", [CallerMemberName] string previousMethodName = "") {
			if (string.IsNullOrEmpty(nullObjectName)) {
				return;
			}

			LogGenericError(nullObjectName + " is null!", botName, previousMethodName);
		}

		[Conditional("DEBUG")]
		internal static void LogGenericDebug(string message, string botName = "Main", [CallerMemberName] string previousMethodName = "") {
			if (string.IsNullOrEmpty(message)) {
				return;
			}

			Log("[#] DEBUG: " + previousMethodName + "() <" + botName + "> " + message);
		}

		private static void Log(string message) {
			if (string.IsNullOrEmpty(message)) {
				return;
			}

			string loggedMessage = DateTime.Now + " " + message + Environment.NewLine;

			// Write on console only when not awaiting response from user
			if (!Program.ConsoleIsBusy) {
				try {
					Console.Write(loggedMessage);
				} catch { }
			}

			if (LogToFile) {
				lock (FileLock) {
					try {
						File.AppendAllText(Program.LogFile, loggedMessage);
					} catch (Exception e) {
						LogToFile = false;
						LogGenericException(e);
					}
				}
			}
		}
	}
}
