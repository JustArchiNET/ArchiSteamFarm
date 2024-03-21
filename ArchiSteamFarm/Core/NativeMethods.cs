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
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ArchiSteamFarm.Core;

internal static partial class NativeMethods {
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[LibraryImport("user32.dll")]
	[SupportedOSPlatform("Windows")]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static partial void FlashWindowEx(ref FlashWindowInfo pwfi);

	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[LibraryImport("kernel32.dll")]
	[SupportedOSPlatform("Windows")]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static partial bool GetConsoleMode(nint hConsoleHandle, out EConsoleMode lpMode);

	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[LibraryImport("libc", EntryPoint = "geteuid", SetLastError = true)]
	[SupportedOSPlatform("FreeBSD")]
	[SupportedOSPlatform("Linux")]
	[SupportedOSPlatform("MacOS")]
	internal static partial uint GetEuid();

	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[LibraryImport("kernel32.dll")]
	[SupportedOSPlatform("Windows")]
	internal static partial nint GetStdHandle(EStandardHandle nStdHandle);

	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[LibraryImport("kernel32.dll")]
	[SupportedOSPlatform("Windows")]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static partial bool SetConsoleMode(nint hConsoleHandle, EConsoleMode dwMode);

	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[LibraryImport("kernel32.dll")]
	[SupportedOSPlatform("Windows")]
	internal static partial EExecutionState SetThreadExecutionState(EExecutionState executionState);

	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[LibraryImport("user32.dll")]
	[SupportedOSPlatform("Windows")]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static partial void ShowWindow(nint hWnd, EShowWindow nCmdShow);

	[Flags]
	[SupportedOSPlatform("Windows")]
	internal enum EConsoleMode : uint {
		EnableQuickEditMode = 0x0040
	}

	[Flags]
	[SupportedOSPlatform("Windows")]
	internal enum EExecutionState : uint {
		None = 0,
		SystemRequired = 0x00000001,
		AwayModeRequired = 0x00000040,
		Continuous = 0x80000000,
		Awake = SystemRequired | AwayModeRequired | Continuous
	}

	[Flags]
	[SupportedOSPlatform("Windows")]
	internal enum EFlashFlags : uint {
		Stop = 0,
		Caption = 1,
		Tray = 2,
		All = Caption | Tray,
		Timer = 4
	}

	[SupportedOSPlatform("Windows")]
	internal enum EShowWindow : uint {
		Minimize = 6
	}

	[SupportedOSPlatform("Windows")]
	internal enum EStandardHandle {
		Input = -10
	}

	[StructLayout(LayoutKind.Sequential)]
	[SupportedOSPlatform("Windows")]
	internal struct FlashWindowInfo {
#pragma warning disable Reordering // TODO: This silly pragma doesn't do anything, but it stops Rider from reordering, we may be able to get rid of it later
		public uint StructSize;
		public nint WindowHandle;
		public EFlashFlags Flags;
		public uint Count;
		public uint TimeoutBetweenFlashes;
#pragma warning restore Reordering // TODO: This silly pragma doesn't do anything, but it stops Rider from reordering, we may be able to get rid of it later
	}
}
