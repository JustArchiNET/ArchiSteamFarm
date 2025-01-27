// ----------------------------------------------------------------------------------------------
//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// ----------------------------------------------------------------------------------------------
// |
// Copyright 2015-2025 Łukasz "JustArchi" Domeradzki
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
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Collections;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.IPC;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.NLog.Targets;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Storage;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace ArchiSteamFarm.NLog;

internal static class Logging {
	internal const string NLogConfigurationFile = "NLog.config";

	private const byte ConsoleResponsivenessDelay = 250; // In milliseconds
	private const string GeneralLayout = $@"${{date:format=yyyy-MM-dd HH\:mm\:ss}}|${{processname}}-${{processid}}|${{level:uppercase=true}}|{LayoutMessage}";
	private const string LayoutMessage = @"${logger}|${message}${onexception:inner= ${exception:format=toString,Data}}";

	internal static bool LogFileExists => File.Exists(SharedInfo.LogFile);

	private static readonly ConcurrentHashSet<LoggingRule> ConsoleLoggingRules = [];
	private static readonly SemaphoreSlim ConsoleSemaphore = new(1, 1);

	private static string Backspace => "\b \b";

	private static bool IsUsingCustomConfiguration;
	private static bool IsWaitingForUserInput;

	internal static void EnableTraceLogging() {
		if (IsUsingCustomConfiguration || (LogManager.Configuration == null)) {
			return;
		}

		bool reload = false;

		foreach (LoggingRule rule in LogManager.Configuration.LoggingRules.Where(static rule => rule.IsLoggingEnabledForLevel(LogLevel.Debug) && !rule.IsLoggingEnabledForLevel(LogLevel.Trace))) {
			rule.EnableLoggingForLevel(LogLevel.Trace);
			reload = true;
		}

		if (reload) {
			LogManager.ReconfigExistingLoggers();
		}
	}

	internal static async Task<string?> GetUserInput(ASF.EUserInputType userInputType, string botName = SharedInfo.ASF) {
		if ((userInputType == ASF.EUserInputType.None) || !Enum.IsDefined(userInputType)) {
			throw new InvalidEnumArgumentException(nameof(userInputType), (int) userInputType, typeof(ASF.EUserInputType));
		}

		ArgumentException.ThrowIfNullOrEmpty(botName);

		if (Program.Service || (ASF.GlobalConfig?.Headless ?? GlobalConfig.DefaultHeadless)) {
			ASF.ArchiLogger.LogGenericWarning(Strings.ErrorUserInputRunningInHeadlessMode);

			return null;
		}

		await ConsoleSemaphore.WaitAsync().ConfigureAwait(false);

		string? result;

		try {
			OnUserInputStart();

			try {
				// Since we're in the user input section, logging to NLog will not spam our console targets, but will still leave appropriate trace e.g. in the file targets
				switch (userInputType) {
					case ASF.EUserInputType.Cryptkey:
						string cryptKeyText = Bot.FormatBotResponse(Strings.UserInputCryptkey, botName);

						ASF.ArchiLogger.LogGenericWarning(cryptKeyText);

						Console.Write(cryptKeyText);
						result = ConsoleReadLineMasked();

						ASF.ArchiLogger.LogGenericInfo(Strings.FormatInput(result.AsMasked()));

						break;
					case ASF.EUserInputType.DeviceConfirmation:
						string deviceConfirmationText = Bot.FormatBotResponse(Strings.UserInputDeviceConfirmation, botName);

						while (true) {
							ASF.ArchiLogger.LogGenericWarning(deviceConfirmationText);

							Console.Write(deviceConfirmationText);
							result = ConsoleReadLine();

							ASF.ArchiLogger.LogGenericInfo(Strings.FormatInput(result));

							if (string.IsNullOrEmpty(result) || result.Equals("Y", StringComparison.OrdinalIgnoreCase) || result.Equals("N", StringComparison.OrdinalIgnoreCase)) {
								break;
							}
						}

						break;
					case ASF.EUserInputType.Login:
						string loginText = Bot.FormatBotResponse(Strings.UserInputSteamLogin, botName);

						ASF.ArchiLogger.LogGenericWarning(loginText);

						Console.Write(loginText);
						result = ConsoleReadLine();

						ASF.ArchiLogger.LogGenericInfo(Strings.FormatInput(result));

						break;
					case ASF.EUserInputType.Password:
						string passwordText = Bot.FormatBotResponse(Strings.UserInputSteamPassword, botName);

						ASF.ArchiLogger.LogGenericWarning(passwordText);

						Console.Write(passwordText);
						result = ConsoleReadLineMasked();

						ASF.ArchiLogger.LogGenericInfo(Strings.FormatInput(result.AsMasked()));

						break;
					case ASF.EUserInputType.SteamGuard:
						string steamGuardText = Bot.FormatBotResponse(Strings.UserInputSteamGuard, botName);

						ASF.ArchiLogger.LogGenericWarning(steamGuardText);

						Console.Write(steamGuardText);
						result = ConsoleReadLine();

						ASF.ArchiLogger.LogGenericInfo(Strings.FormatInput(result));

						break;
					case ASF.EUserInputType.SteamParentalCode:
						string steamParentalCodeText = Bot.FormatBotResponse(Strings.UserInputSteamParentalCode, botName);

						ASF.ArchiLogger.LogGenericWarning(steamParentalCodeText);

						Console.Write(steamParentalCodeText);
						result = ConsoleReadLineMasked();

						ASF.ArchiLogger.LogGenericInfo(Strings.FormatInput(result.AsMasked()));

						break;
					case ASF.EUserInputType.TwoFactorAuthentication:
						string twoFactorAuthenticationText = Bot.FormatBotResponse(Strings.UserInputSteam2FA, botName);

						ASF.ArchiLogger.LogGenericWarning(twoFactorAuthenticationText);

						Console.Write(twoFactorAuthenticationText);
						result = ConsoleReadLine();

						ASF.ArchiLogger.LogGenericInfo(Strings.FormatInput(result?.AsMasked()));

						break;
					default:
						ASF.ArchiLogger.LogGenericError(Strings.FormatWarningUnknownValuePleaseReport(nameof(userInputType), userInputType));

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

		return !string.IsNullOrEmpty(result) ? result.Trim() : null;
	}

	internal static void InitCoreLoggers(bool uniqueInstance) {
		try {
			if ((Directory.GetCurrentDirectory() != AppContext.BaseDirectory) && File.Exists(NLogConfigurationFile)) {
				IsUsingCustomConfiguration = true;

				LogManager.Configuration = new XmlLoggingConfiguration(NLogConfigurationFile);
			}
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);
		}

		if (IsUsingCustomConfiguration) {
			InitConsoleLoggers();
			LogManager.ConfigurationChanged += OnConfigurationChanged;

			return;
		}

		if (uniqueInstance) {
			try {
				Directory.CreateDirectory(SharedInfo.ArchivalLogsDirectory);
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);
			}

#pragma warning disable CA2000 // False positive, we're adding this disposable object to the global scope, so we can't dispose it
			FileTarget fileTarget = new("File") {
				ArchiveFileName = Path.Combine("${currentdir}", SharedInfo.ArchivalLogsDirectory, SharedInfo.ArchivalLogFile),
				ArchiveNumbering = ArchiveNumberingMode.Rolling,
				ArchiveOldFileOnStartup = true,
				CleanupFileName = false,
				DeleteOldFileOnStartup = true,
				FileName = Path.Combine("${currentdir}", SharedInfo.LogFile),

				// Windows OS prevents other apps from reading file when actively holding exclusive (write) lock over it
				// We require read access for GET /Api/NLog/File ASF API usage, therefore we shouldn't keep the lock all the time
				KeepFileOpen = !OperatingSystem.IsWindows(),

				Layout = GeneralLayout,
				MaxArchiveFiles = 10
			};
#pragma warning restore CA2000 // False positive, we're adding this disposable object to the global scope, so we can't dispose it

			InitializeTarget(LogManager.Configuration, fileTarget);

			LogManager.ReconfigExistingLoggers();
		}

		InitConsoleLoggers();
	}

	internal static void InitEmergencyLoggers() {
		if (LogManager.Configuration != null) {
			IsUsingCustomConfiguration = true;

			return;
		}

		// This is a temporary, bare, file-less configuration that must work until we're able to initialize it properly
		ConfigurationItemFactory.Default.ParseMessageTemplates = false;
		LoggingConfiguration config = new();

#pragma warning disable CA2000 // False positive, we're adding this disposable object to the global scope, so we can't dispose it
		ColoredConsoleTarget coloredConsoleTarget = new("ColoredConsole") { Layout = GeneralLayout };
#pragma warning restore CA2000 // False positive, we're adding this disposable object to the global scope, so we can't dispose it

		InitializeTarget(config, coloredConsoleTarget);

		LogManager.Configuration = config;
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

			InitializeTarget(LogManager.Configuration, historyTarget);

			LogManager.ReconfigExistingLoggers();
		}

		ArchiKestrel.OnNewHistoryTarget(historyTarget);
	}

	internal static async Task<string[]?> ReadLogFileLines() {
		try {
			return await File.ReadAllLinesAsync(SharedInfo.LogFile).ConfigureAwait(false);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);

			return null;
		}
	}

	internal static void StartInteractiveConsole() {
		Utilities.InBackground(HandleConsoleInteractively, true);
		ASF.ArchiLogger.LogGenericInfo(Strings.InteractiveConsoleEnabled);
	}

	private static async Task BeepUntilCanceled(CancellationToken cancellationToken, byte secondsDelay = 30) {
		ArgumentOutOfRangeException.ThrowIfZero(secondsDelay);

		while (!cancellationToken.IsCancellationRequested) {
			try {
				await Task.Delay(secondsDelay * 1000, cancellationToken).ConfigureAwait(false);
			} catch (TaskCanceledException) {
				return;
			}

			Console.Write('\a');
		}
	}

	private static string? ConsoleReadLine() {
		using CancellationTokenSource cts = new();

		try {
			CancellationToken token = cts.Token;

			Utilities.InBackground(() => BeepUntilCanceled(token));

			if (OperatingSystem.IsWindows()) {
				OS.WindowsStartFlashingConsoleWindow();
			}

			return Console.ReadLine();
		} finally {
			cts.Cancel();

			if (OperatingSystem.IsWindows()) {
				OS.WindowsStopFlashingConsoleWindow();
			}
		}
	}

	private static string ConsoleReadLineMasked(char mask = '*') {
		using CancellationTokenSource cts = new();

		try {
			CancellationToken token = cts.Token;

			Utilities.InBackground(() => BeepUntilCanceled(token));

			if (OperatingSystem.IsWindows()) {
				OS.WindowsStartFlashingConsoleWindow();
			}

			StringBuilder result = new();

			while (true) {
				ConsoleKeyInfo keyInfo = Console.ReadKey(true);

				if (keyInfo.KeyChar == '\u0004') {
					// Linux terminal closing STDIN, we're done here
					return result.ToString();
				}

				if (keyInfo.Key == ConsoleKey.Enter) {
					// User finishing input, as expected
					Console.WriteLine();

					return result.ToString();
				}

				// User continues input
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
						Console.Write(Backspace);
					}
				}
			}
		} finally {
			cts.Cancel();

			if (OperatingSystem.IsWindows()) {
				OS.WindowsStopFlashingConsoleWindow();
			}
		}
	}

	private static async Task HandleConsoleInteractively() {
		try {
			while (!Program.ShutdownSequenceInitialized) {
				if (IsWaitingForUserInput || !Console.KeyAvailable) {
					await Task.Delay(ConsoleResponsivenessDelay).ConfigureAwait(false);

					continue;
				}

				await ConsoleSemaphore.WaitAsync().ConfigureAwait(false);

				try {
					ConsoleKeyInfo keyInfo = Console.ReadKey(true);

					if (keyInfo.KeyChar == '\u0004') {
						// Linux terminal closing STDIN, we're done here
						return;
					}

					if (keyInfo.Key != ConsoleKey.C) {
						// Console input other than 'c', ignored
						continue;
					}

					OnUserInputStart();

					try {
						// Since we're in the user input section, logging to NLog will not spam our console targets, but will still leave appropriate trace e.g. in the file targets
						string enterCommandText = $@">> {Strings.EnterCommand}";

						ASF.ArchiLogger.LogGenericInfo(enterCommandText);

						Console.Write(enterCommandText);
						string? command = ConsoleReadLine();

						ASF.ArchiLogger.LogGenericInfo(Strings.FormatInput(command));

						if (string.IsNullOrEmpty(command)) {
							continue;
						}

						string? commandPrefix = ASF.GlobalConfig != null ? ASF.GlobalConfig.CommandPrefix : GlobalConfig.DefaultCommandPrefix;

						if (!string.IsNullOrEmpty(commandPrefix) && command.StartsWith(commandPrefix, StringComparison.Ordinal)) {
							if (command.Length == commandPrefix.Length) {
								// If the message starts with command prefix and is of the same length as command prefix, then it's just empty command trigger, useless
								continue;
							}

							command = command[commandPrefix.Length..];
						}

						Bot? targetBot = Bot.GetDefaultBot();

						if (targetBot == null) {
							string noBotsDefinedText = $@"<< {Strings.ErrorNoBotsDefined}";

							ASF.ArchiLogger.LogGenericInfo(noBotsDefinedText);

							Console.WriteLine(noBotsDefinedText);

							continue;
						}

						Console.WriteLine($@"<> {Strings.Executing}");

						string? response = await targetBot.Commands.Response(EAccess.Owner, command).ConfigureAwait(false);

						if (string.IsNullOrEmpty(response)) {
							ASF.ArchiLogger.LogNullError(response);

							string emptyResponseText = $@"<< {Strings.FormatErrorIsEmpty(nameof(response))}";

							ASF.ArchiLogger.LogGenericInfo(emptyResponseText);

							Console.WriteLine(emptyResponseText);

							continue;
						}

						string responseText = $@"<< {response}";

						ASF.ArchiLogger.LogGenericInfo(responseText);

						Console.WriteLine(responseText);
					} finally {
						OnUserInputEnd();
					}
				} finally {
					ConsoleSemaphore.Release();
				}
			}
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);
		}
	}

	private static void InitConsoleLoggers() {
		ConsoleLoggingRules.Clear();

		if (LogManager.Configuration == null) {
			return;
		}

		foreach (LoggingRule loggingRule in LogManager.Configuration.LoggingRules.Where(static loggingRule => loggingRule.Targets.Any(static target => target is ColoredConsoleTarget or ConsoleTarget))) {
			ConsoleLoggingRules.Add(loggingRule);
		}
	}

	private static void InitializeTarget(LoggingConfiguration config, Target target) {
		ArgumentNullException.ThrowIfNull(config);
		ArgumentNullException.ThrowIfNull(target);

		config.AddTarget(target);

		if (!Debugging.IsUserDebugging) {
			// Silence default ASP.NET logging
			config.LoggingRules.Add(new LoggingRule("Microsoft.*", target) { FinalMinLevel = LogLevel.Warn });
			config.LoggingRules.Add(new LoggingRule("Microsoft.Hosting.Lifetime", target) { FinalMinLevel = LogLevel.Info });
			config.LoggingRules.Add(new LoggingRule("System.*", target) { FinalMinLevel = LogLevel.Warn });
		}

		config.LoggingRules.Add(new LoggingRule("*", LogLevel.Debug, target));
	}

	private static void OnConfigurationChanged(object? sender, LoggingConfigurationChangedEventArgs e) {
		ArgumentNullException.ThrowIfNull(e);

		InitConsoleLoggers();

		if (IsWaitingForUserInput) {
			OnUserInputStart();
		}

		HistoryTarget? historyTarget = LogManager.Configuration?.AllTargets.OfType<HistoryTarget>().FirstOrDefault();
		ArchiKestrel.OnNewHistoryTarget(historyTarget);
	}

	private static void OnUserInputEnd() {
		IsWaitingForUserInput = false;

		if ((ConsoleLoggingRules.Count == 0) || (LogManager.Configuration == null)) {
			return;
		}

		bool reconfigure = false;

		foreach (LoggingRule consoleLoggingRule in ConsoleLoggingRules.Where(static consoleLoggingRule => !LogManager.Configuration.LoggingRules.Contains(consoleLoggingRule))) {
			LogManager.Configuration.LoggingRules.Add(consoleLoggingRule);
			reconfigure = true;
		}

		if (reconfigure) {
			LogManager.ReconfigExistingLoggers();
		}
	}

	private static void OnUserInputStart() {
		IsWaitingForUserInput = true;

		if ((ConsoleLoggingRules.Count == 0) || (LogManager.Configuration == null)) {
			return;
		}

		bool reconfigure = false;

		foreach (LoggingRule _ in ConsoleLoggingRules.Where(static consoleLoggingRule => LogManager.Configuration.LoggingRules.Remove(consoleLoggingRule))) {
			reconfigure = true;
		}

		if (reconfigure) {
			LogManager.ReconfigExistingLoggers();
		}
	}
}
