//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// 
// Copyright 2015-2018 Łukasz "JustArchi" Domeradzki
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
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;

namespace ArchiSteamFarm {
	internal static class SharedInfo {
		internal const ulong ArchiSteamID = 76561198006963719;
		internal const string ASF = nameof(ASF);
		internal const ulong ASFGroupSteamID = 103582791440160998;
		internal const string AssemblyName = nameof(ArchiSteamFarm);
		internal const string ConfigDirectory = "config";
		internal const string ConfigExtension = ".json";
		internal const string DatabaseExtension = ".db";
		internal const string DebugDirectory = "debug";
		internal const string GithubReleaseURL = "https://api.github.com/repos/" + GithubRepo + "/releases"; // GitHub API is HTTPS only
		internal const string GithubRepo = "JustArchi/" + AssemblyName;
		internal const string GlobalConfigFileName = ASF + ConfigExtension;
		internal const string GlobalDatabaseFileName = ASF + DatabaseExtension;
		internal const string KeysExtension = ".keys";
		internal const string KeysUnusedExtension = ".unused";
		internal const string KeysUsedExtension = ".used";
		internal const string LogFile = "log.txt";
		internal const string MobileAuthenticatorExtension = ".maFile";
		internal const string ProjectURL = "https://github.com/" + GithubRepo;
		internal const string SentryHashExtension = ".bin";
		internal const string StatisticsServer = "asf.justarchi.net";
		internal const string UlongCompatibilityStringPrefix = "s_";
		internal const string UpdateDirectory = "_old";
		internal const string WebsiteDirectory = "www";

		internal static string HomeDirectory => Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
		internal static Guid ModuleVersion => Assembly.GetEntryAssembly().ManifestModule.ModuleVersionId;
		internal static string PublicIdentifier => AssemblyName + (BuildInfo.IsCustomBuild ? "-custom" : "");
		internal static Version Version => Assembly.GetEntryAssembly().GetName().Version;

		[SuppressMessage("ReSharper", "ConvertToConstant.Global")]
		internal static class BuildInfo {
#if ASF_VARIANT_DOCKER
			internal static readonly bool CanUpdate = false;
			internal static readonly string Variant = "docker";
#elif ASF_VARIANT_GENERIC
			internal static readonly bool CanUpdate = true;
			internal static readonly string Variant = "generic";
#elif ASF_VARIANT_GENERIC_NETF
			internal static readonly bool CanUpdate = true;
			internal static readonly string Variant = "generic-netf";
#elif ASF_VARIANT_LINUX_ARM
			internal static readonly bool CanUpdate = true;
			internal static readonly string Variant = "linux-arm";
#elif ASF_VARIANT_LINUX_X64
			internal static readonly bool CanUpdate = true;
			internal static readonly string Variant = "linux-x64";
#elif ASF_VARIANT_OSX_X64
			internal static readonly bool CanUpdate = true;
			internal static readonly string Variant = "osx-x64";
#elif ASF_VARIANT_WIN_X64
			internal static readonly bool CanUpdate = true;
			internal static readonly string Variant = "win-x64";
#else
			internal static readonly bool CanUpdate = false;
			internal static readonly string Variant = SourceVariant;
#endif

			private const string SourceVariant = "source";

			internal static bool IsCustomBuild => Variant == SourceVariant;
		}
	}
}
