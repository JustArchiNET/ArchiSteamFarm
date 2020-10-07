//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2020 Łukasz "JustArchi" Domeradzki
// Contact: JustArchi@JustArchi.net
// |
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// |
// http://www.apache.org/licenses/LICENSE-2.0
// |
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using ArchiSteamFarm.Localization;
using JetBrains.Annotations;
using NLog;
using SteamKit2;

namespace ArchiSteamFarm.NLog {
	public sealed class ArchiLogger {
		private readonly Logger Logger;

		public ArchiLogger(string name) {
			if (string.IsNullOrEmpty(name)) {
				throw new ArgumentNullException(nameof(name));
			}

			Logger = LogManager.GetLogger(name);
		}

		[PublicAPI]
		public void LogGenericDebug(string message, [CallerMemberName] string? previousMethodName = null) {
			if (string.IsNullOrEmpty(message)) {
				throw new ArgumentNullException(nameof(message));
			}

			Logger.Debug($"{previousMethodName}() {message}");
		}

		[PublicAPI]
		public void LogGenericDebuggingException(Exception exception, [CallerMemberName] string? previousMethodName = null) {
			if (exception == null) {
				throw new ArgumentNullException(nameof(exception));
			}

			if (!Debugging.IsUserDebugging) {
				return;
			}

			Logger.Debug(exception, $"{previousMethodName}()");
		}

		[PublicAPI]
		public void LogGenericError(string message, [CallerMemberName] string? previousMethodName = null) {
			if (string.IsNullOrEmpty(message)) {
				throw new ArgumentNullException(nameof(message));
			}

			Logger.Error($"{previousMethodName}() {message}");
		}

		[PublicAPI]
		public void LogGenericException(Exception exception, [CallerMemberName] string? previousMethodName = null) {
			if (exception == null) {
				throw new ArgumentNullException(nameof(exception));
			}

			Logger.Error(exception, $"{previousMethodName}()");
		}

		[PublicAPI]
		public void LogGenericInfo(string message, [CallerMemberName] string? previousMethodName = null) {
			if (string.IsNullOrEmpty(message)) {
				throw new ArgumentNullException(nameof(message));
			}

			Logger.Info($"{previousMethodName}() {message}");
		}

		[PublicAPI]
		public void LogGenericTrace(string message, [CallerMemberName] string? previousMethodName = null) {
			if (string.IsNullOrEmpty(message)) {
				throw new ArgumentNullException(nameof(message));
			}

			Logger.Trace($"{previousMethodName}() {message}");
		}

		[PublicAPI]
		public void LogGenericWarning(string message, [CallerMemberName] string? previousMethodName = null) {
			if (string.IsNullOrEmpty(message)) {
				throw new ArgumentNullException(nameof(message));
			}

			Logger.Warn($"{previousMethodName}() {message}");
		}

		[PublicAPI]
		public void LogGenericWarningException(Exception exception, [CallerMemberName] string? previousMethodName = null) {
			if (exception == null) {
				throw new ArgumentNullException(nameof(exception));
			}

			Logger.Warn(exception, $"{previousMethodName}()");
		}

		[PublicAPI]
		public void LogNullError(string nullObjectName, [CallerMemberName] string? previousMethodName = null) {
			if (string.IsNullOrEmpty(nullObjectName)) {
				throw new ArgumentNullException(nameof(nullObjectName));
			}

			LogGenericError(string.Format(Strings.ErrorObjectIsNull, nullObjectName), previousMethodName);
		}

		internal void LogChatMessage(bool echo, string message, ulong chatGroupID = 0, ulong chatID = 0, ulong steamID = 0, [CallerMemberName] string? previousMethodName = null) {
			if (string.IsNullOrEmpty(message) || (((chatGroupID == 0) || (chatID == 0)) && (steamID == 0))) {
				throw new ArgumentNullException(nameof(message) + " || " + "((" + nameof(chatGroupID) + " || " + nameof(chatID) + ") && " + nameof(steamID) + ")");
			}

			StringBuilder loggedMessage = new StringBuilder(previousMethodName + "() " + message + " " + (echo ? "->" : "<-") + " ");

			if ((chatGroupID != 0) && (chatID != 0)) {
				loggedMessage.Append(chatGroupID + "-" + chatID);

				if (steamID != 0) {
					loggedMessage.Append("/" + steamID);
				}
			} else if (steamID != 0) {
				loggedMessage.Append(steamID);
			}

			LogEventInfo logEventInfo = new LogEventInfo(LogLevel.Trace, Logger.Name, loggedMessage.ToString());
			logEventInfo.Properties["Echo"] = echo;
			logEventInfo.Properties["Message"] = message;
			logEventInfo.Properties["ChatGroupID"] = chatGroupID;
			logEventInfo.Properties["ChatID"] = chatID;
			logEventInfo.Properties["SteamID"] = steamID;

			Logger.Log(logEventInfo);
		}

		internal async Task LogFatalException(Exception exception, [CallerMemberName] string? previousMethodName = null) {
			if (exception == null) {
				throw new ArgumentNullException(nameof(exception));
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
				// Ignored, we can't do anything about this
			}

			try {
				Console.Write(message);
			} catch {
				// Ignored, we can't do anything about this
			}

			while (true) {
				message = string.Format(Strings.ErrorEarlyFatalExceptionPrint, previousMethodName, exception.Message, exception.StackTrace) + Environment.NewLine;

				try {
					await RuntimeCompatibility.File.AppendAllTextAsync(SharedInfo.LogFile, message).ConfigureAwait(false);
				} catch {
					// Ignored, we can't do anything about this
				}

				try {
					Console.Write(message);
				} catch {
					// Ignored, we can't do anything about this
				}

				if (exception.InnerException != null) {
					exception = exception.InnerException;

					continue;
				}

				break;
			}
		}

		internal void LogInvite(SteamID steamID, [CallerMemberName] string? previousMethodName = null) {
			if ((steamID == null) || (steamID.AccountType == EAccountType.Invalid)) {
				throw new ArgumentNullException(nameof(steamID));
			}

			ulong steamID64 = steamID;

			string loggedMessage = previousMethodName + "() " + steamID.AccountType + " " + steamID64;

			LogEventInfo logEventInfo = new LogEventInfo(LogLevel.Trace, Logger.Name, loggedMessage);
			logEventInfo.Properties["AccountType"] = steamID.AccountType;
			logEventInfo.Properties["SteamID"] = steamID64;

			Logger.Log(logEventInfo);
		}
	}
}
