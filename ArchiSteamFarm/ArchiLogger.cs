//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// 
//  Copyright 2015-2018 Łukasz "JustArchi" Domeradzki
//  Contact: JustArchi@JustArchi.net
// 
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
// 
//  http://www.apache.org/licenses/LICENSE-2.0
//      
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.

using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ArchiSteamFarm.Localization;
using NLog;

namespace ArchiSteamFarm {
	internal sealed class ArchiLogger {
		private readonly Logger Logger;

		internal ArchiLogger(string name) {
			if (string.IsNullOrEmpty(name)) {
				throw new ArgumentNullException(nameof(name));
			}

			Logger = LogManager.GetLogger(name);
		}

		internal async Task LogFatalException(Exception exception, [CallerMemberName] string previousMethodName = null) {
			if (exception == null) {
				LogNullError(nameof(exception));
				return;
			}

			Logger.Fatal(exception, $"{previousMethodName}()");

			// If LogManager has been initialized already, don't do anything else
			if (LogManager.Configuration != null) {
				return;
			}

			// Otherwise, we ran into fatal exception before logging module could even get initialized, so activate fallback logging that involves file and console

			string message = string.Format(DateTime.Now + " " + Strings.ErrorEarlyFatalExceptionInfo, SharedInfo.Version) + Environment.NewLine;

			try {
				await RuntimeCompatibility.File.WriteAllTextAsync(SharedInfo.LogFile, message).ConfigureAwait(false);
			} catch {
				// Ignored, we can't do anything with this
			}

			try {
				Console.Write(message);
			} catch {
				// Ignored, we can't do anything with this
			}

			while (true) {
				message = string.Format(Strings.ErrorEarlyFatalExceptionPrint, previousMethodName, exception.Message, exception.StackTrace) + Environment.NewLine;

				try {
					await RuntimeCompatibility.File.AppendAllTextAsync(SharedInfo.LogFile, message).ConfigureAwait(false);
				} catch {
					// Ignored, we can't do anything with this
				}

				try {
					Console.Write(message);
				} catch {
					// Ignored, we can't do anything with this
				}

				if (exception.InnerException != null) {
					exception = exception.InnerException;
					continue;
				}

				break;
			}
		}

		internal void LogGenericDebug(string message, [CallerMemberName] string previousMethodName = null) {
			if (string.IsNullOrEmpty(message)) {
				LogNullError(nameof(message));
				return;
			}

			Logger.Debug($"{previousMethodName}() {message}");
		}

		internal void LogGenericDebuggingException(Exception exception, [CallerMemberName] string previousMethodName = null) {
			if (exception == null) {
				LogNullError(nameof(exception));
				return;
			}

			if (!Debugging.IsUserDebugging) {
				return;
			}

			Logger.Debug(exception, $"{previousMethodName}()");
		}

		internal void LogGenericError(string message, [CallerMemberName] string previousMethodName = null) {
			if (string.IsNullOrEmpty(message)) {
				LogNullError(nameof(message));
				return;
			}

			Logger.Error($"{previousMethodName}() {message}");
		}

		internal void LogGenericException(Exception exception, [CallerMemberName] string previousMethodName = null) {
			if (exception == null) {
				LogNullError(nameof(exception));
				return;
			}

			Logger.Error(exception, $"{previousMethodName}()");
		}

		internal void LogGenericInfo(string message, [CallerMemberName] string previousMethodName = null) {
			if (string.IsNullOrEmpty(message)) {
				LogNullError(nameof(message));
				return;
			}

			Logger.Info($"{previousMethodName}() {message}");
		}

		internal void LogGenericTrace(string message, [CallerMemberName] string previousMethodName = null) {
			if (string.IsNullOrEmpty(message)) {
				LogNullError(nameof(message));
				return;
			}

			Logger.Trace($"{previousMethodName}() {message}");
		}

		internal void LogGenericWarning(string message, [CallerMemberName] string previousMethodName = null) {
			if (string.IsNullOrEmpty(message)) {
				LogNullError(nameof(message));
				return;
			}

			Logger.Warn($"{previousMethodName}() {message}");
		}

		internal void LogGenericWarningException(Exception exception, [CallerMemberName] string previousMethodName = null) {
			if (exception == null) {
				LogNullError(nameof(exception));
				return;
			}

			Logger.Warn(exception, $"{previousMethodName}()");
		}

		internal void LogNullError(string nullObjectName, [CallerMemberName] string previousMethodName = null) {
			if (string.IsNullOrEmpty(nullObjectName)) {
				return;
			}

			LogGenericError(string.Format(Strings.ErrorObjectIsNull, nullObjectName), previousMethodName);
		}
	}
}