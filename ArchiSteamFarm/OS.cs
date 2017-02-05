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

namespace ArchiSteamFarm {
	internal static class OS {
		private static readonly PlatformID PlatformID = Environment.OSVersion.Platform;

		internal static void Init() {
			switch (PlatformID) {
				case PlatformID.Win32NT:
				case PlatformID.Win32S:
				case PlatformID.Win32Windows:
				case PlatformID.WinCE:
					KeepWindowsSystemActive();
					break;
			}
		}

		private static void KeepWindowsSystemActive() {
			// This function calls unmanaged API in order to tell Windows OS that it should not enter sleep state while the program is running
			// If user wishes to enter sleep mode, then he should use ShutdownOnFarmingFinished or manage ASF process with third-party tool or script
			// More info: https://msdn.microsoft.com/library/windows/desktop/aa373208(v=vs.85).aspx
			EExecutionState result = SetThreadExecutionState(EExecutionState.Continuous | EExecutionState.AwayModeRequired | EExecutionState.SystemRequired);

			// SetThreadExecutionState() returns NULL on failure, which is mapped to 0 (EExecutionState.Error) in our case
			if (result == EExecutionState.Error) {
				ASF.ArchiLogger.LogGenericError(string.Format(Strings.WarningFailedWithError, result));
			}
		}

		[DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		private static extern EExecutionState SetThreadExecutionState(EExecutionState executionState);

		[Flags]
		private enum EExecutionState : uint {
			Error = 0,
			SystemRequired = 0x00000001,
//			DisplayRequired = 0x00000002,
//			UserPresent = 0x00000004,
			AwayModeRequired = 0x00000040,
			Continuous = 0x80000000
		}
	}
}