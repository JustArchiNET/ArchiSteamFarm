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
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace ConfigGenerator {
	internal static class Logging {
		internal static void LogGenericInfo(string message) {
			if (string.IsNullOrEmpty(message)) {
				return;
			}

			MessageBox.Show(message, "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
		}

		internal static void LogGenericWTF(string message, [CallerMemberName] string previousMethodName = "") {
			if (string.IsNullOrEmpty(message)) {
				return;
			}

			MessageBox.Show(previousMethodName + "() " + message, "WTF", MessageBoxButtons.OK, MessageBoxIcon.Error);
		}

		internal static void LogGenericError(string message, [CallerMemberName] string previousMethodName = "") {
			if (string.IsNullOrEmpty(message)) {
				return;
			}

			MessageBox.Show(previousMethodName + "() " + message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
		}

		internal static void LogGenericException(Exception exception, [CallerMemberName] string previousMethodName = "") {
			if (exception == null) {
				return;
			}

			MessageBox.Show(previousMethodName + "() " + exception.Message + Environment.NewLine + exception.StackTrace, "Exception", MessageBoxButtons.OK, MessageBoxIcon.Error);

			if (exception.InnerException != null) {
				LogGenericException(exception.InnerException, previousMethodName);
			}
		}

		internal static void LogGenericWarning(string message, [CallerMemberName] string previousMethodName = "") {
			if (string.IsNullOrEmpty(message)) {
				return;
			}

			MessageBox.Show(previousMethodName + "() " + message, "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
		}

		internal static void LogNullError(string nullObjectName, [CallerMemberName] string previousMethodName = "") {
			if (string.IsNullOrEmpty(nullObjectName)) {
				return;
			}

			LogGenericError(nullObjectName + " is null!", previousMethodName);
		}

		[Conditional("DEBUG")]
		internal static void LogGenericDebug(string message, [CallerMemberName] string previousMethodName = "") {
			if (string.IsNullOrEmpty(message)) {
				return;
			}

			MessageBox.Show(previousMethodName + "() " + message, "Debug", MessageBoxButtons.OK, MessageBoxIcon.Information);
		}
	}
}
