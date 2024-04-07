// ----------------------------------------------------------------------------------------------
//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// ----------------------------------------------------------------------------------------------
// |
// Copyright 2015-2024 Łukasz "JustArchi" Domeradzki
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
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Storage;
using ArchiSteamFarm.Web;

namespace ArchiSteamFarm.Core;

internal static class OS {
	// We need to keep this one assigned and not calculated on-demand
	internal static readonly string ProcessFileName = Environment.ProcessPath ?? throw new InvalidOperationException(nameof(ProcessFileName));

	internal static string? Description => TrimAndNullifyEmptyText(RuntimeInformation.OSDescription);
	internal static string? Framework => TrimAndNullifyEmptyText(RuntimeInformation.FrameworkDescription);

	internal static DateTime ProcessStartTime {
		get {
			using Process process = Process.GetCurrentProcess();

			return process.StartTime.ToUniversalTime();
		}
	}

	internal static string? Runtime => TrimAndNullifyEmptyText(RuntimeInformation.RuntimeIdentifier);

	internal static string Version {
		get {
			if (!string.IsNullOrEmpty(BackingVersion)) {
				return BackingVersion;
			}

			BackingVersion = $"{Framework ?? "Unknown Framework"}; {Runtime ?? "Unknown Runtime"}; {Description ?? "Unknown OS"}";

			return BackingVersion;
		}
	}

	private static string? BackingVersion;
	private static Mutex? SingleInstance;

	internal static void CoreInit(bool minimized, bool systemRequired) {
		if (minimized) {
			MinimizeConsoleWindow();
		}

		if (OperatingSystem.IsWindows()) {
			if (systemRequired) {
				WindowsKeepSystemActive();
			}

			if (!Console.IsOutputRedirected) {
				// Normally we should use UTF-8 console encoding as it's the most correct one for our case, and we already use it on other OSes such as Linux
				// However, older Windows versions, mainly 7/8.1 can't into UTF-8 without appropriate console font, and expecting from users to change it manually is unwanted
				// As irrational as it can sound, those versions actually can work with unicode encoding instead, as they magically map it into proper chars despite of incorrect font
				// See https://github.com/JustArchiNET/ArchiSteamFarm/issues/1289 for more details
				Console.OutputEncoding = OperatingSystem.IsWindowsVersionAtLeast(10) ? Encoding.UTF8 : Encoding.Unicode;

				// Quick edit mode will freeze when user start selecting something on the console until the selection is cancelled
				// Users are very often doing it accidentally without any real purpose, and we want to avoid this common issue which causes the whole process to hang
				// See http://stackoverflow.com/questions/30418886/how-and-why-does-quickedit-mode-in-command-prompt-freeze-applications for more details
				WindowsDisableQuickEditMode();
			}
		}
	}

	internal static string GetOsResourceName(string objectName) {
		ArgumentException.ThrowIfNullOrEmpty(objectName);

		return $"{SharedInfo.AssemblyName}-{objectName}";
	}

	internal static void Init(GlobalConfig.EOptimizationMode optimizationMode) {
		if (!Enum.IsDefined(optimizationMode)) {
			throw new InvalidEnumArgumentException(nameof(optimizationMode), (int) optimizationMode, typeof(GlobalConfig.EOptimizationMode));
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
				throw new InvalidOperationException(nameof(optimizationMode));
		}
	}

	internal static bool IsRunningAsRoot() {
		if (OperatingSystem.IsWindows()) {
			using WindowsIdentity identity = WindowsIdentity.GetCurrent();

			return identity.IsSystem || new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
		}

		if (OperatingSystem.IsFreeBSD() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) {
			return NativeMethods.GetEuid() == 0;
		}

		// We can't determine whether user is running as root or not, so fallback to that not happening
		return false;
	}

	internal static async Task<bool> RegisterProcess() {
		if (SingleInstance != null) {
			return false;
		}

		// The only purpose of using hashing here is to cut on a potential size of the resource name - paths can be really long, and we almost certainly have some upper limit on the resource name we can allocate
		// At the same time it'd be the best if we avoided all special characters, such as '/' found e.g. in base64, as we can't be sure that it's not a prohibited character in regards to native OS implementation
		// Because of that, SHA256 is sufficient for our case, as it generates alphanumeric characters only, and is barely 256-bit long. We don't need any kind of complex cryptography or collision detection here, any hashing will do, and the shorter the better
		string uniqueName = $"Global\\{GetOsResourceName(nameof(SingleInstance))}-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Directory.GetCurrentDirectory())))}";

		Mutex? singleInstance = null;

		for (byte i = 0; i < WebBrowser.MaxTries; i++) {
			if (i > 0) {
				await Task.Delay(2000).ConfigureAwait(false);
			}

			singleInstance = new Mutex(true, uniqueName, out bool result);

			if (result) {
				break;
			}

			singleInstance.Dispose();
			singleInstance = null;
		}

		if (singleInstance == null) {
			return false;
		}

		SingleInstance = singleInstance;

		return true;
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

	internal static bool VerifyEnvironment() {
		// We're not going to analyze source builds, as we don't know what changes the author has made, assume they have a point
		if (SharedInfo.BuildInfo.IsCustomBuild) {
			return true;
		}

		if (SharedInfo.BuildInfo.Variant == "generic") {
			// Generic is supported everywhere
			return true;
		}

		if ((SharedInfo.BuildInfo.Variant == "docker") || SharedInfo.BuildInfo.Variant.StartsWith("linux-", StringComparison.Ordinal)) {
			// OS-specific Linux and Docker builds are supported only on Linux
			return OperatingSystem.IsLinux();
		}

		if (SharedInfo.BuildInfo.Variant.StartsWith("osx-", StringComparison.Ordinal)) {
			// OS-specific macOS build is supported only on macOS
			return OperatingSystem.IsMacOS();
		}

		if (SharedInfo.BuildInfo.Variant.StartsWith("win-", StringComparison.Ordinal)) {
			// OS-specific Windows build is supported only on Windows
			return OperatingSystem.IsWindows();
		}

		// Unknown combination, we intend to cover all of the available ones above, so this results in an error
		ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(SharedInfo.BuildInfo.Variant), SharedInfo.BuildInfo.Variant));

		return false;
	}

	[SupportedOSPlatform("Windows")]
	internal static void WindowsStartFlashingConsoleWindow() {
		if (!OperatingSystem.IsWindows()) {
			throw new PlatformNotSupportedException();
		}

		using Process currentProcess = Process.GetCurrentProcess();

		nint handle = currentProcess.MainWindowHandle;

		if (handle == nint.Zero) {
			return;
		}

		NativeMethods.FlashWindowInfo flashInfo = new() {
			StructSize = (uint) Marshal.SizeOf<NativeMethods.FlashWindowInfo>(),
			Flags = NativeMethods.EFlashFlags.All | NativeMethods.EFlashFlags.Timer,
			WindowHandle = handle,
			Count = uint.MaxValue
		};

		NativeMethods.FlashWindowEx(ref flashInfo);
	}

	[SupportedOSPlatform("Windows")]
	internal static void WindowsStopFlashingConsoleWindow() {
		if (!OperatingSystem.IsWindows()) {
			throw new PlatformNotSupportedException();
		}

		using Process currentProcess = Process.GetCurrentProcess();
		nint handle = currentProcess.MainWindowHandle;

		if (handle == nint.Zero) {
			return;
		}

		NativeMethods.FlashWindowInfo flashInfo = new() {
			StructSize = (uint) Marshal.SizeOf<NativeMethods.FlashWindowInfo>(),
			Flags = NativeMethods.EFlashFlags.Stop,
			WindowHandle = handle
		};

		NativeMethods.FlashWindowEx(ref flashInfo);
	}

	private static void MinimizeConsoleWindow() {
		(_, int top) = Console.GetCursorPosition();

		// Will work if the terminal supports XTWINOPS "iconify" escape sequence, reference: https://invisible-island.net/xterm/ctlseqs/ctlseqs.html
		Console.Write('\x1b' + @"[2;0;0t");

		// Reset cursor position if terminal outputs escape sequences as-is
		Console.SetCursorPosition(0, top);

		// Fallback if we're using conhost on Windows
		if (OperatingSystem.IsWindows()) {
			using Process process = Process.GetCurrentProcess();

			nint windowHandle = process.MainWindowHandle;

			if (windowHandle != nint.Zero) {
				NativeMethods.ShowWindow(windowHandle, NativeMethods.EShowWindow.Minimize);
			}
		}
	}

	private static string? TrimAndNullifyEmptyText(string text) {
		ArgumentNullException.ThrowIfNull(text);

		text = text.Trim();

		return text.Length > 0 ? text : null;
	}

	[SupportedOSPlatform("Windows")]
	private static void WindowsDisableQuickEditMode() {
		if (!OperatingSystem.IsWindows()) {
			throw new PlatformNotSupportedException();
		}

		nint consoleHandle = NativeMethods.GetStdHandle(NativeMethods.EStandardHandle.Input);

		if (!NativeMethods.GetConsoleMode(consoleHandle, out NativeMethods.EConsoleMode consoleMode)) {
			ASF.ArchiLogger.LogGenericError(Strings.WarningFailed);

			return;
		}

		consoleMode &= ~NativeMethods.EConsoleMode.EnableQuickEditMode;

		if (!NativeMethods.SetConsoleMode(consoleHandle, consoleMode)) {
			ASF.ArchiLogger.LogGenericError(Strings.WarningFailed);
		}
	}

	[SupportedOSPlatform("Windows")]
	private static void WindowsKeepSystemActive() {
		if (!OperatingSystem.IsWindows()) {
			throw new PlatformNotSupportedException();
		}

		// This function calls unmanaged API in order to tell Windows OS that it should not enter sleep state while the program is running
		// If user wishes to enter sleep mode, then they should use ShutdownOnFarmingFinished or manage the ASF process with third-party tool or script
		// See https://docs.microsoft.com/windows/win32/api/winbase/nf-winbase-setthreadexecutionstate for more details
		NativeMethods.EExecutionState result = NativeMethods.SetThreadExecutionState(NativeMethods.EExecutionState.Awake);

		// SetThreadExecutionState() returns NULL on failure, which is mapped to 0 (EExecutionState.None) in our case
		if (result == NativeMethods.EExecutionState.None) {
			ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, result));
		}
	}
}
