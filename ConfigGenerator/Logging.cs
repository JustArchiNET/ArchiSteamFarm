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
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using ConfigGenerator.Properties;

namespace ConfigGenerator {
	internal static class Logging {
		internal static void LogGenericInfoWithoutStacktrace(string message) {
			if (string.IsNullOrEmpty(message)) {
				LogNullError(nameof(message));
				return;
			}

			MessageBox.Show(message, Resources.Information, MessageBoxButtons.OK, MessageBoxIcon.Information);
		}

		internal static void LogGenericErrorWithoutStacktrace(string message) {
			if (string.IsNullOrEmpty(message)) {
				LogNullError(nameof(message));
				return;
			}

			MessageBox.Show(message, Resources.Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
		}

		internal static void LogGenericException(Exception exception, [CallerMemberName] string previousMethodName = null) {
			while (true) {
				if (exception == null) {
					LogNullError(nameof(exception));
					return;
				}

				MessageBox.Show(previousMethodName + @"() " + exception.Message + Environment.NewLine + exception.StackTrace, Resources.Exception, MessageBoxButtons.OK, MessageBoxIcon.Error);

				if (exception.InnerException != null) {
					exception = exception.InnerException;
					continue;
				}

				break;
			}
		}

		internal static void LogGenericWarning(string message, [CallerMemberName] string previousMethodName = null) {
			if (string.IsNullOrEmpty(message)) {
				LogNullError(nameof(message));
				return;
			}

			MessageBox.Show(previousMethodName + @"() " + message, Resources.Warning, MessageBoxButtons.OK, MessageBoxIcon.Warning);
		}

		[SuppressMessage("ReSharper", "ExplicitCallerInfoArgument")]
		internal static void LogNullError(string nullObjectName, [CallerMemberName] string previousMethodName = null) {
			while (true) {
				if (string.IsNullOrEmpty(nullObjectName)) {
					nullObjectName = nameof(nullObjectName);
					continue;
				}

				LogGenericError(nullObjectName + " is null!", previousMethodName);
				break;
			}
		}

		private static void LogGenericError(string message, [CallerMemberName] string previousMethodName = null) {
			if (string.IsNullOrEmpty(message)) {
				LogNullError(nameof(message));
				return;
			}

			LogGenericErrorWithoutStacktrace(previousMethodName + @"() " + message);
		}
	}
}
