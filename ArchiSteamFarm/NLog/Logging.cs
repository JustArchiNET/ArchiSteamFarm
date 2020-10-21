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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Collections;
using ArchiSteamFarm.IPC;
using ArchiSteamFarm.Localization;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace ArchiSteamFarm.NLog {
	internal static class Logging {
		internal const string NLogConfigurationFile = "NLog.config";

		private const byte ConsoleResponsivenessDelay = 250; // In milliseconds
		private const string GeneralLayout = @"${date:format=yyyy-MM-dd HH\:mm\:ss}|${processname}-${processid}|${level:uppercase=true}|" + LayoutMessage;
		private const string LayoutMessage = @"${logger}|${message}${onexception:inner= ${exception:format=toString,Data}}";

		private static readonly ConcurrentHashSet<LoggingRule> ConsoleLoggingRules = new ConcurrentHashSet<LoggingRule>();
		private static readonly SemaphoreSlim ConsoleSemaphore = new SemaphoreSlim(1, 1);

		private static bool IsUsingCustomConfiguration;
		private static bool IsWaitingForUserInput;

		internal static void EnableTraceLogging() {
			if (IsUsingCustomConfiguration || (LogManager.Configuration == null)) {
				return;
			}

			bool reload = false;

			foreach (LoggingRule rule in LogManager.Configuration.LoggingRules.Where(rule => rule.IsLoggingEnabledForLevel(LogLevel.Debug) && !rule.IsLoggingEnabledForLevel(LogLevel.Trace))) {
				rule.EnableLoggingForLevel(LogLevel.Trace);
				reload = true;
			}

			if (reload) {
				LogManager.ReconfigExistingLoggers();
			}
		}

		internal static async Task<string?> GetUserInput(ASF.EUserInputType userInputType, string botName = SharedInfo.ASF) {
			if ((userInputType == ASF.EUserInputType.None) || !Enum.IsDefined(typeof(ASF.EUserInputType), userInputType) || string.IsNullOrEmpty(botName)) {
				throw new ArgumentNullException(nameof(userInputType) + " || " + nameof(botName));
			}

			if (ASF.GlobalConfig?.Headless ?? GlobalConfig.DefaultHeadless) {
				ASF.ArchiLogger.LogGenericWarning(Strings.ErrorUserInputRunningInHeadlessMode);

				return null;
			}

			await ConsoleSemaphore.WaitAsync().ConfigureAwait(false);

			string? result;

			try {
				OnUserInputStart();

				try {
					Console.Beep();

					switch (userInputType) {
						case ASF.EUserInputType.Login:
							Console.Write(Bot.FormatBotResponse(Strings.UserInputSteamLogin, botName));
							result = ConsoleReadLine();

							break;
						case ASF.EUserInputType.Password:
							Console.Write(Bot.FormatBotResponse(Strings.UserInputSteamPassword, botName));
							result = ConsoleReadLineMasked();

							break;
						case ASF.EUserInputType.SteamGuard:
							Console.Write(Bot.FormatBotResponse(Strings.UserInputSteamGuard, botName));
							result = ConsoleReadLine();

							break;
						case ASF.EUserInputType.SteamParentalCode:
							Console.Write(Bot.FormatBotResponse(Strings.UserInputSteamParentalCode, botName));
							result = ConsoleReadLineMasked();

							break;
						case ASF.EUserInputType.TwoFactorAuthentication:
							Console.Write(Bot.FormatBotResponse(Strings.UserInputSteam2FA, botName));
							result = ConsoleReadLine();

							break;
						default:
							ASF.ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(userInputType), userInputType));

							return null;
					}

					if (!Console.IsOutputRedirected) {
						Console.Clear(); // For security purposes
					}
				} catch (Exception e) {
					OnUserInputEnd();
					ASF.ArchiLogger.LogGenericException(e);

					return null;
				} finally {
					OnUserInputEnd();
				}
			} finally {
				ConsoleSemaphore.Release();
			}

			return !string.IsNullOrEmpty(result) ? result!.Trim() : null;
		}

		internal static void InitCoreLoggers(bool uniqueInstance) {
			try {
				if ((Directory.GetCurrentDirectory() != AppContext.BaseDirectory) && File.Exists(NLogConfigurationFile)) {
					LogManager.Configuration = new XmlLoggingConfiguration(NLogConfigurationFile);
				}
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);
			}

			if (LogManager.Configuration != null) {
				IsUsingCustomConfiguration = true;
				InitConsoleLoggers();
				LogManager.ConfigurationChanged += OnConfigurationChanged;

				return;
			}

			ConfigurationItemFactory.Default.ParseMessageTemplates = false;
			LoggingConfiguration config = new LoggingConfiguration();

			ColoredConsoleTarget coloredConsoleTarget = new ColoredConsoleTarget("ColoredConsole") { Layout = GeneralLayout };

			config.AddTarget(coloredConsoleTarget);
			config.LoggingRules.Add(new LoggingRule("*", LogLevel.Debug, coloredConsoleTarget));

			if (uniqueInstance) {
				try {
					if (!Directory.Exists(SharedInfo.ArchivalLogsDirectory)) {
						Directory.CreateDirectory(SharedInfo.ArchivalLogsDirectory);
					}
				} catch (Exception e) {
					ASF.ArchiLogger.LogGenericException(e);
				}

				FileTarget fileTarget = new FileTarget("File") {
					ArchiveFileName = Path.Combine("${currentdir}", SharedInfo.ArchivalLogsDirectory, SharedInfo.ArchivalLogFile),
					ArchiveNumbering = ArchiveNumberingMode.Rolling,
					ArchiveOldFileOnStartup = true,
					CleanupFileName = false,
					ConcurrentWrites = false,
					DeleteOldFileOnStartup = true,
					FileName = Path.Combine("${currentdir}", SharedInfo.LogFile),
					Layout = GeneralLayout,
					MaxArchiveFiles = 10
				};

				config.AddTarget(fileTarget);
				config.LoggingRules.Add(new LoggingRule("*", LogLevel.Debug, fileTarget));
			}

			LogManager.Configuration = config;
			InitConsoleLoggers();
		}

		internal static void InitHistoryLogger() {
			if (LogManager.Configuration == null) {
				return;
			}

			HistoryTarget? historyTarget = LogManager.Configuration.AllTargets.OfType<HistoryTarget>().FirstOrDefault();

			if ((historyTarget == null) && !IsUsingCustomConfiguration) {
				historyTarget = new HistoryTarget("History") {
					Layout = GeneralLayout,
					MaxCount = 20
				};

				LogManager.Configuration.AddTarget(historyTarget);
				LogManager.Configuration.LoggingRules.Add(new LoggingRule("*", LogLevel.Debug, historyTarget));

				LogManager.ReconfigExistingLoggers();
			}

			ArchiKestrel.OnNewHistoryTarget(historyTarget);
		}

		internal static void StartInteractiveConsole() {
			if ((ASF.GlobalConfig?.SteamOwnerID ?? GlobalConfig.DefaultSteamOwnerID) == 0) {
				ASF.ArchiLogger.LogGenericWarning(string.Format(Strings.InteractiveConsoleNotAvailable, nameof(ASF.GlobalConfig.SteamOwnerID)));

				return;
			}

			Utilities.InBackground(HandleConsoleInteractively, true);
			ASF.ArchiLogger.LogGenericInfo(Strings.InteractiveConsoleEnabled);
		}

		private static string? ConsoleReadLine() {
			Console.Beep();

			return Console.ReadLine();
		}

		private static string ConsoleReadLineMasked(char mask = '*') {
			StringBuilder result = new StringBuilder();

			Console.Beep();

			ConsoleKeyInfo keyInfo;

			while ((keyInfo = Console.ReadKey(true)).Key != ConsoleKey.Enter) {
				if (!char.IsControl(keyInfo.KeyChar)) {
					result.Append(keyInfo.KeyChar);
					Console.Write(mask);
				} else if ((keyInfo.Key == ConsoleKey.Backspace) && (result.Length > 0)) {
					result.Length--;

					if (Console.CursorLeft == 0) {
						Console.SetCursorPosition(Console.BufferWidth - 1, Console.CursorTop - 1);
						Console.Write(' ');
						Console.SetCursorPosition(Console.BufferWidth - 1, Console.CursorTop - 1);
					} else {
						// There are two \b characters here
						Console.Write(@" ");
					}
				}
			}

			Console.WriteLine();

			return result.ToString();
		}

		private static async Task HandleConsoleInteractively() {
			while (!Program.ShutdownSequenceInitialized) {
				try {
					if (IsWaitingForUserInput || !Console.KeyAvailable) {
						continue;
					}

					await ConsoleSemaphore.WaitAsync().ConfigureAwait(false);

					try {
						ConsoleKeyInfo keyInfo = Console.ReadKey(true);

						if (keyInfo.Key != ConsoleKey.C) {
							continue;
						}

						OnUserInputStart();

						try {
							Console.Write(@">> " + Strings.EnterCommand);
							string? command = ConsoleReadLine();

							if (string.IsNullOrEmpty(command)) {
								continue;
							}

							string? commandPrefix = ASF.GlobalConfig != null ? ASF.GlobalConfig.CommandPrefix : GlobalConfig.DefaultCommandPrefix;

							if (!string.IsNullOrEmpty(commandPrefix) && command!.StartsWith(commandPrefix!, StringComparison.Ordinal)) {
								if (command.Length == commandPrefix!.Length) {
									// If the message starts with command prefix and is of the same length as command prefix, then it's just empty command trigger, useless
									continue;
								}

								command = command.Substring(commandPrefix!.Length);
							}

							Bot? targetBot = Bot.Bots?.OrderBy(bot => bot.Key, Bot.BotsComparer).Select(bot => bot.Value).FirstOrDefault();

							if (targetBot == null) {
								Console.WriteLine(@"<< " + Strings.ErrorNoBotsDefined);

								continue;
							}

							Console.WriteLine(@"<> " + Strings.Executing);

							ulong steamOwnerID = ASF.GlobalConfig?.SteamOwnerID ?? GlobalConfig.DefaultSteamOwnerID;

							string? response = await targetBot.Commands.Response(steamOwnerID, command!).ConfigureAwait(false);

							if (string.IsNullOrEmpty(response)) {
								ASF.ArchiLogger.LogNullError(nameof(response));
								Console.WriteLine(Strings.ErrorIsEmpty, nameof(response));

								continue;
							}

							Console.WriteLine(@"<< " + response);
						} finally {
							OnUserInputEnd();
						}
					} finally {
						ConsoleSemaphore.Release();
					}
				} catch (Exception e) {
					ASF.ArchiLogger.LogGenericException(e);

					return;
				} finally {
					await Task.Delay(ConsoleResponsivenessDelay).ConfigureAwait(false);
				}
			}
		}

		private static void InitConsoleLoggers() {
			ConsoleLoggingRules.Clear();

			foreach (LoggingRule loggingRule in LogManager.Configuration.LoggingRules.Where(loggingRule => loggingRule.Targets.Any(target => target is ColoredConsoleTarget || target is ConsoleTarget))) {
				ConsoleLoggingRules.Add(loggingRule);
			}
		}

		private static void OnConfigurationChanged(object? sender, LoggingConfigurationChangedEventArgs e) {
			if (e == null) {
				throw new ArgumentNullException(nameof(e));
			}

			InitConsoleLoggers();

			if (IsWaitingForUserInput) {
				OnUserInputStart();
			}

			HistoryTarget? historyTarget = LogManager.Configuration.AllTargets.OfType<HistoryTarget>().FirstOrDefault();
			ArchiKestrel.OnNewHistoryTarget(historyTarget);
		}

		private static void OnUserInputEnd() {
			IsWaitingForUserInput = false;

			if (ConsoleLoggingRules.Count == 0) {
				return;
			}

			bool reconfigure = false;

			foreach (LoggingRule consoleLoggingRule in ConsoleLoggingRules.Where(consoleLoggingRule => !LogManager.Configuration.LoggingRules.Contains(consoleLoggingRule))) {
				LogManager.Configuration.LoggingRules.Add(consoleLoggingRule);
				reconfigure = true;
			}

			if (reconfigure) {
				LogManager.ReconfigExistingLoggers();
			}
		}

		private static void OnUserInputStart() {
			IsWaitingForUserInput = true;

			if (ConsoleLoggingRules.Count == 0) {
				return;
			}

			bool reconfigure = false;

			foreach (LoggingRule _ in ConsoleLoggingRules.Where(consoleLoggingRule => LogManager.Configuration.LoggingRules.Remove(consoleLoggingRule))) {
				reconfigure = true;
			}

			if (reconfigure) {
				LogManager.ReconfigExistingLoggers();
			}
		}
	}
}
