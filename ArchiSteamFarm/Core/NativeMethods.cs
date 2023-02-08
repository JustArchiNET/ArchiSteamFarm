//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2023 Łukasz "JustArchi" Domeradzki
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
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ArchiSteamFarm.Core;

internal static partial class NativeMethods {
	[SupportedOSPlatform("Windows")]
	internal const EExecutionState AwakeExecutionState = EExecutionState.SystemRequired | EExecutionState.AwayModeRequired | EExecutionState.Continuous;

	[SupportedOSPlatform("Windows")]
	internal const uint EnableQuickEditMode = 0x0040;

	[SupportedOSPlatform("Windows")]
	internal const byte ShowWindowMinimize = 6;

	[SupportedOSPlatform("Windows")]
	internal const sbyte StandardInputHandle = -10;

	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[SupportedOSPlatform("FreeBSD")]
	[SupportedOSPlatform("Linux")]
	[SupportedOSPlatform("MacOS")]
#if NETFRAMEWORK
#pragma warning disable CA2101 // False positive, we can't use unicode charset on Unix, and it uses UTF-8 by default anyway
	[DllImport("libc", EntryPoint = "chmod", SetLastError = true)]
	internal static extern int Chmod(string path, int mode);
#pragma warning restore CA2101 // False positive, we can't use unicode charset on Unix, and it uses UTF-8 by default anyway
#else
	[LibraryImport("libc", EntryPoint = "chmod", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
	internal static partial int Chmod(string path, int mode);
#endif

	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[SupportedOSPlatform("Windows")]
	[return: MarshalAs(UnmanagedType.Bool)]
#if NETFRAMEWORK
	[DllImport("kernel32.dll")]
	internal static extern bool GetConsoleMode(nint hConsoleHandle, out uint lpMode);
#else
	[LibraryImport("kernel32.dll")]
	internal static partial bool GetConsoleMode(nint hConsoleHandle, out uint lpMode);
#endif

	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[SupportedOSPlatform("FreeBSD")]
	[SupportedOSPlatform("Linux")]
	[SupportedOSPlatform("MacOS")]
#if NETFRAMEWORK
	[DllImport("libc", EntryPoint = "geteuid", SetLastError = true)]
	internal static extern uint GetEuid();
#else
	[LibraryImport("libc", EntryPoint = "geteuid", SetLastError = true)]
	internal static partial uint GetEuid();
#endif

	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[SupportedOSPlatform("Windows")]
#if NETFRAMEWORK
	[DllImport("kernel32.dll")]
	internal static extern nint GetStdHandle(int nStdHandle);
#else
	[LibraryImport("kernel32.dll")]
	internal static partial nint GetStdHandle(int nStdHandle);
#endif

	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[SupportedOSPlatform("Windows")]
	[return: MarshalAs(UnmanagedType.Bool)]
#if NETFRAMEWORK
	[DllImport("kernel32.dll")]
	internal static extern bool SetConsoleMode(nint hConsoleHandle, uint dwMode);
#else
	[LibraryImport("kernel32.dll")]
	internal static partial bool SetConsoleMode(nint hConsoleHandle, uint dwMode);
#endif

	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[SupportedOSPlatform("Windows")]
#if NETFRAMEWORK
	[DllImport("kernel32.dll")]
	internal static extern EExecutionState SetThreadExecutionState(EExecutionState executionState);
#else
	[LibraryImport("kernel32.dll")]
	internal static partial EExecutionState SetThreadExecutionState(EExecutionState executionState);
#endif

	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[SupportedOSPlatform("Windows")]
	[return: MarshalAs(UnmanagedType.Bool)]
#if NETFRAMEWORK
	[DllImport("user32.dll")]
	internal static extern void ShowWindow(nint hWnd, int nCmdShow);
#else
	[LibraryImport("user32.dll")]
	internal static partial void ShowWindow(nint hWnd, int nCmdShow);
#endif

	[Flags]
	[SupportedOSPlatform("Windows")]
	internal enum EExecutionState : uint {
		None = 0,
		SystemRequired = 0x00000001,
		AwayModeRequired = 0x00000040,
		Continuous = 0x80000000
	}
}
