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

namespace ArchiSteamFarm.Core;

internal static class BuildInfo {
#if ASF_VARIANT_DOCKER
	internal static bool CanUpdate => false;
	internal static string Variant => "docker";
#elif ASF_VARIANT_GENERIC
	internal static bool CanUpdate => true;
	internal static string Variant => "generic";
#elif ASF_VARIANT_LINUX_ARM
	internal static bool CanUpdate => true;
	internal static string Variant => "linux-arm";
#elif ASF_VARIANT_LINUX_ARM64
	internal static bool CanUpdate => true;
	internal static string Variant => "linux-arm64";
#elif ASF_VARIANT_LINUX_X64
	internal static bool CanUpdate => true;
	internal static string Variant => "linux-x64";
#elif ASF_VARIANT_OSX_ARM64
	internal static bool CanUpdate => true;
	internal static string Variant => "osx-arm64";
#elif ASF_VARIANT_OSX_X64
	internal static bool CanUpdate => true;
	internal static string Variant => "osx-x64";
#elif ASF_VARIANT_WIN_ARM64
	internal static bool CanUpdate => true;
	internal static string Variant => "win-arm64";
#elif ASF_VARIANT_WIN_X64
	internal static bool CanUpdate => true;
	internal static string Variant => "win-x64";
#else
	internal static bool CanUpdate => false;
	internal static string Variant => SourceVariant;
#endif

#if ASF_RUNTIME_TRIMMED
	internal static bool IsRuntimeTrimmed => true;
#else
	internal static bool IsRuntimeTrimmed => false;
#endif

	private const string SourceVariant = "source";

	internal static bool IsCustomBuild => Variant == SourceVariant;
}
