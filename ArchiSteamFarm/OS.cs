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
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using ArchiSteamFarm.Helpers;
using ArchiSteamFarm.Localization;

namespace ArchiSteamFarm {
	internal static class OS {
		// We need to keep this one assigned and not calculated on-demand
		internal static readonly string ProcessFileName = Process.GetCurrentProcess().MainModule?.FileName ?? throw new ArgumentNullException(nameof(ProcessFileName));

		internal static bool IsUnix => RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
		internal static string Variant => RuntimeInformation.OSDescription.Trim();

		private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

		private static Mutex? SingleInstance;

		internal static void CoreInit() {
			if (IsWindows && !Console.IsOutputRedirected) {
				// Normally we should use UTF-8 encoding as it's the most correct one for our case, and we already use it on other OSes such as Linux
				// However, older Windows versions, mainly 7/8.1 can't into UTF-8 without appropriate console font, and expecting from users to change it manually is unwanted
				// As irrational as it can sound, those versions actually can work with unicode encoding instead, as they magically map it into proper chars despite of incorrect font
				// We could in theory conditionally use UTF-8 for Windows 10+ and unicode otherwise, but Windows version detection is simply not worth the hassle in this case
				// Therefore, until we can drop support for Windows < 10, we'll stick with Unicode for all Windows boxes, unless there will be valid reasoning for conditional switch
				// See https://github.com/JustArchiNET/ArchiSteamFarm/issues/1289 for more details
				Console.OutputEncoding = Encoding.Unicode;

				// Quick edit mode will freeze when user start selecting something on the console until the selection is cancelled
				// Users are very often doing it accidentally without any real purpose, and we want to avoid this common issue which causes the whole process to hang
				// See http://stackoverflow.com/questions/30418886/how-and-why-does-quickedit-mode-in-command-prompt-freeze-applications for more details
				WindowsDisableQuickEditMode();
			}
		}

		internal static ICrossProcessSemaphore CreateCrossProcessSemaphore(string objectName) {
			if (string.IsNullOrEmpty(objectName)) {
				throw new ArgumentNullException(nameof(objectName));
			}

			string resourceName = GetOsResourceName(objectName);

			return new CrossProcessFileBasedSemaphore(resourceName);
		}

		internal static void Init(bool systemRequired, GlobalConfig.EOptimizationMode optimizationMode) {
			if (!Enum.IsDefined(typeof(GlobalConfig.EOptimizationMode), optimizationMode)) {
				throw new ArgumentNullException(nameof(optimizationMode));
			}

			if (IsWindows) {
				if (systemRequired) {
					WindowsKeepSystemActive();
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
					throw new ArgumentOutOfRangeException(nameof(optimizationMode));
			}
		}

		internal static bool RegisterProcess() {
			if (SingleInstance != null) {
				return false;
			}

			string uniqueName;

			// The only purpose of using hashingAlgorithm here is to cut on a potential size of the resource name - paths can be really long, and we almost certainly have some upper limit on the resource name we can allocate
			// At the same time it'd be the best if we avoided all special characters, such as '/' found e.g. in base64, as we can't be sure that it's not a prohibited character in regards to native OS implementation
			// Because of that, MD5 is sufficient for our case, as it generates alphanumeric characters only, and is barely 128-bit long. We don't need any kind of complex cryptography or collision detection here, any hashing algorithm will do, and the shorter the better
			using (MD5 hashingAlgorithm = MD5.Create()) {
				uniqueName = "Global\\" + GetOsResourceName(nameof(SingleInstance)) + "-" + BitConverter.ToString(hashingAlgorithm.ComputeHash(Encoding.UTF8.GetBytes(Directory.GetCurrentDirectory()))).Replace("-", "");
			}

			Mutex singleInstance = new Mutex(true, uniqueName, out bool result);

			if (!result) {
				singleInstance.Dispose();

				return false;
			}

			SingleInstance = singleInstance;

			return true;
		}

		internal static void UnixSetFileAccess(string path, EUnixPermission permission) {
			if (string.IsNullOrEmpty(path)) {
				throw new ArgumentNullException(nameof(path));
			}

			if (!IsUnix) {
				return;
			}

			if (!File.Exists(path) && !Directory.Exists(path)) {
				ASF.ArchiLogger.LogGenericError(string.Format(Strings.WarningFailedWithError, "!" + nameof(path)));

				return;
			}

			// Chmod() returns 0 on success, -1 on failure
			if (NativeMethods.Chmod(path, (int) permission) != 0) {
				ASF.ArchiLogger.LogGenericError(string.Format(Strings.WarningFailedWithError, Marshal.GetLastWin32Error()));
			}
		}

		internal static void UnregisterProcess() {
			if (SingleInstance == null) {
				return;
			}

			// We should release the mutex here, but that can be done only from the same thread due to thread affinity
			// Instead, we'll dispose the mutex which should automatically release it by the CLR
			SingleInstance.Dispose();
			SingleInstance = null;
		}

		private static string GetOsResourceName(string objectName) {
			if (string.IsNullOrEmpty(objectName)) {
				throw new ArgumentNullException(nameof(objectName));
			}

			return SharedInfo.AssemblyName + "-" + objectName;
		}

		private static void WindowsDisableQuickEditMode() {
			if (!IsWindows) {
				return;
			}

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

		private static void WindowsKeepSystemActive() {
			if (!IsWindows) {
				return;
			}

			// This function calls unmanaged API in order to tell Windows OS that it should not enter sleep state while the program is running
			// If user wishes to enter sleep mode, then he should use ShutdownOnFarmingFinished or manage ASF process with third-party tool or script
			// See https://docs.microsoft.com/windows/win32/api/winbase/nf-winbase-setthreadexecutionstate for more details
			NativeMethods.EExecutionState result = NativeMethods.SetThreadExecutionState(NativeMethods.AwakeExecutionState);

			// SetThreadExecutionState() returns NULL on failure, which is mapped to 0 (EExecutionState.None) in our case
			if (result == NativeMethods.EExecutionState.None) {
				ASF.ArchiLogger.LogGenericError(string.Format(Strings.WarningFailedWithError, result));
			}
		}

		[Flags]
		internal enum EUnixPermission : ushort {
			OtherExecute = 0x1,
			OtherWrite = 0x2,
			OtherRead = 0x4,
			GroupExecute = 0x8,
			GroupWrite = 0x10,
			GroupRead = 0x20,
			UserExecute = 0x40,
			UserWrite = 0x80,
			UserRead = 0x100,
			Combined755 = UserRead | UserWrite | UserExecute | GroupRead | GroupExecute | OtherRead | OtherExecute,
			Combined777 = UserRead | UserWrite | UserExecute | GroupRead | GroupWrite | GroupExecute | OtherRead | OtherWrite | OtherExecute
		}

		private static class NativeMethods {
			internal const EExecutionState AwakeExecutionState = EExecutionState.SystemRequired | EExecutionState.AwayModeRequired | EExecutionState.Continuous;
			internal const uint EnableQuickEditMode = 0x0040;
			internal const sbyte StandardInputHandle = -10;

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
				None = 0,
				SystemRequired = 0x00000001,
				AwayModeRequired = 0x00000040,
				Continuous = 0x80000000
			}
		}
	}
}
