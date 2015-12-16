/*
    _                _      _  ____   _                           _____
   / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
  / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
 / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
/_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|

 Copyright 2015 Łukasz "JustArchi" Domeradzki
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
using System.Runtime.CompilerServices;

namespace ArchiSteamFarm {
	internal static class Logging {
		private static void Log(string message) {
			if (Program.ConsoleIsBusy) {
				return;
			}

			Console.WriteLine(DateTime.Now + " " + message);
		}

		internal static void LogGenericError(string botName, string message, [CallerMemberName] string previousMethodName = "") {
			Log("[!!] ERROR: " + previousMethodName + "() <" + botName + "> " + message);
		}

		internal static void LogGenericException(string botName, Exception exception, [CallerMemberName] string previousMethodName = "") {
			Log("[!] EXCEPTION: " + previousMethodName + "() <" + botName + "> " + exception.Message);
		}

		internal static void LogGenericWarning(string botName, string message, [CallerMemberName] string previousMethodName = "") {
			Log("[!] WARNING: " + previousMethodName + "() <" + botName + "> " + message);
		}

		internal static void LogGenericInfo(string botName, string message, [CallerMemberName] string previousMethodName = "") {
			Log("[*] INFO: " + previousMethodName + "() <" + botName + "> " + message);
		}

		internal static void LogGenericNotice(string botName, string message, [CallerMemberName] string previousMethodName = "") {
			Log("[*] NOTICE: " + previousMethodName + "() <" + botName + "> " + message);
		}

		[Conditional("DEBUG")]
		internal static void LogGenericDebug(string botName, string message, [CallerMemberName] string previousMethodName = "") {
			Log("[#] DEBUG: " + previousMethodName + "() <" + botName + "> " + message);
		}

		[Conditional("DEBUG")]
		internal static void LogGenericDebug(string message, [CallerMemberName] string previousMethodName = "") {
			LogGenericDebug("DEBUG", message, previousMethodName);
		}
	}
}
