/*
    _                _      _  ____   _                           _____
   / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
  / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
 / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
/_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|

 Copyright 2015-2017 Łukasz "JustArchi" Domeradzki
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
using System.Runtime.InteropServices;
using ArchiSteamFarm.Localization;
using Microsoft.Win32;

namespace ArchiSteamFarm {
	internal static class OS {
		private static readonly PlatformID PlatformID = Environment.OSVersion.Platform;

		internal static void Init(bool headless) {
			switch (PlatformID) {
				case PlatformID.Win32NT:
				case PlatformID.Win32S:
				case PlatformID.Win32Windows:
				case PlatformID.WinCE:
					DisableQuickEditMode();

					if (headless) {
						KeepWindowsSystemActive();
					}

					break;
			}

			SystemEvents.TimeChanged += OnTimeChanged;
		}

		private static void DisableQuickEditMode() {
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
			NativeMethods.EExecutionState result = NativeMethods.SetThreadExecutionState(NativeMethods.EExecutionState.AwayModeRequired | NativeMethods.EExecutionState.Continuous | NativeMethods.EExecutionState.SystemRequired);

			// SetThreadExecutionState() returns NULL on failure, which is mapped to 0 (EExecutionState.Error) in our case
			if (result == NativeMethods.EExecutionState.Error) {
				ASF.ArchiLogger.LogGenericError(string.Format(Strings.WarningFailedWithError, result));
			}
		}

		private static async void OnTimeChanged(object sender, EventArgs e) => await MobileAuthenticator.OnTimeChanged().ConfigureAwait(false);

		private static class NativeMethods {
			internal const uint EnableQuickEditMode = 0x0040;
			internal const int StandardInputHandle = -10;

			[DllImport("kernel32.dll", CharSet = CharSet.Auto)]
			internal static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

			[DllImport("kernel32.dll", CharSet = CharSet.Auto)]
			internal static extern IntPtr GetStdHandle(int nStdHandle);

			[DllImport("kernel32.dll", CharSet = CharSet.Auto)]
			internal static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

			[DllImport("kernel32.dll", CharSet = CharSet.Auto)]
			internal static extern EExecutionState SetThreadExecutionState(EExecutionState executionState);

			[Flags]
			internal enum EExecutionState : uint {
				Error = 0,
				SystemRequired = 0x00000001,
				//DisplayRequired = 0x00000002,
				//UserPresent = 0x00000004,
				AwayModeRequired = 0x00000040,
				Continuous = 0x80000000
			}
		}
	}
}