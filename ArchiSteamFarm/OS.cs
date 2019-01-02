//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// 
// Copyright 2015-2019 Łukasz "JustArchi" Domeradzki
// Contact: JustArchi@JustArchi.net
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
// http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using ArchiSteamFarm.Localization;

namespace ArchiSteamFarm {
	internal static class OS {
		internal static bool IsUnix => RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
		internal static string Variant => RuntimeInformation.OSDescription.Trim();

		internal static void Init(bool systemRequired, GlobalConfig.EOptimizationMode optimizationMode) {
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
				DisableQuickEditMode();

				if (systemRequired) {
					KeepWindowsSystemActive();
				}
			}

			switch (optimizationMode) {
				case GlobalConfig.EOptimizationMode.MaxPerformance:

					// No specific tuning required for now, ASF is optimized for max performance by default
					break;
				case GlobalConfig.EOptimizationMode.MinMemoryUsage:

					// We can disable regex cache which will slightly lower memory usage (for a huge performance hit)
					Regex.CacheSize = 0;

					break;
				default:
					ASF.ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(optimizationMode), optimizationMode));

					return;
			}
		}

		internal static void UnixSetFileAccessExecutable(string path) {
			if (string.IsNullOrEmpty(path) || !File.Exists(path)) {
				ASF.ArchiLogger.LogNullError(nameof(path));

				return;
			}

			// Chmod() returns 0 on success, -1 on failure
			if (NativeMethods.Chmod(path, (int) NativeMethods.UnixExecutePermission) != 0) {
				ASF.ArchiLogger.LogGenericError(string.Format(Strings.WarningFailedWithError, Marshal.GetLastWin32Error()));
			}
		}

		private static void DisableQuickEditMode() {
			if (Console.IsOutputRedirected) {
				return;
			}

			// http://stackoverflow.com/questions/30418886/how-and-why-does-quickedit-mode-in-command-prompt-freeze-applications
			IntPtr consoleHandle = NativeMethods.GetStdHandle(NativeMethods.StandardInputHandle);

			if (!NativeMethods.GetConsoleMode(consoleHandle, out uint consoleMode)) {
				ASF.ArchiLogger.LogGenericError(Strings.WarningFailed);

				return;
			}

			consoleMode &= ~NativeMethods.EnableQuickEditMode;

			if (!NativeMethods.SetConsoleMode(consoleHandle, consoleMode)) {
				ASF.ArchiLogger.LogGenericError(Strings.WarningFailed);
			}
		}

		private static void KeepWindowsSystemActive() {
			// This function calls unmanaged API in order to tell Windows OS that it should not enter sleep state while the program is running
			// If user wishes to enter sleep mode, then he should use ShutdownOnFarmingFinished or manage ASF process with third-party tool or script
			// More info: https://msdn.microsoft.com/library/windows/desktop/aa373208(v=vs.85).aspx
			NativeMethods.EExecutionState result = NativeMethods.SetThreadExecutionState(NativeMethods.AwakeExecutionState);

			// SetThreadExecutionState() returns NULL on failure, which is mapped to 0 (EExecutionState.Error) in our case
			if (result == NativeMethods.EExecutionState.Error) {
				ASF.ArchiLogger.LogGenericError(string.Format(Strings.WarningFailedWithError, result));
			}
		}

		private static class NativeMethods {
			internal const EExecutionState AwakeExecutionState = EExecutionState.SystemRequired | EExecutionState.AwayModeRequired | EExecutionState.Continuous;
			internal const uint EnableQuickEditMode = 0x0040;
			internal const sbyte StandardInputHandle = -10;
			internal const EUnixPermission UnixExecutePermission = EUnixPermission.UserRead | EUnixPermission.UserWrite | EUnixPermission.UserExecute | EUnixPermission.GroupRead | EUnixPermission.GroupExecute | EUnixPermission.OtherRead | EUnixPermission.OtherExecute;

			[DllImport("libc", EntryPoint = "chmod", SetLastError = true)]
			internal static extern int Chmod(string path, int mode);

			[DllImport("kernel32.dll")]
			internal static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

			[DllImport("kernel32.dll")]
			internal static extern IntPtr GetStdHandle(int nStdHandle);

			[DllImport("kernel32.dll")]
			internal static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

			[DllImport("kernel32.dll")]
			internal static extern EExecutionState SetThreadExecutionState(EExecutionState executionState);

			[Flags]
			internal enum EExecutionState : uint {
				Error = 0,
				SystemRequired = 0x00000001,
				AwayModeRequired = 0x00000040,
				Continuous = 0x80000000
			}

			[Flags]
			internal enum EUnixPermission : ushort {
				OtherExecute = 0x1,
				OtherRead = 0x4,
				GroupExecute = 0x8,
				GroupRead = 0x20,
				UserExecute = 0x40,
				UserWrite = 0x80,
				UserRead = 0x100

				/*
				OtherWrite = 0x2
				GroupWrite = 0x10
				*/
			}
		}
	}
}
