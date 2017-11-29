//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// 
//  Copyright 2015-2017 Łukasz "JustArchi" Domeradzki
//  Contact: JustArchi@JustArchi.net
// 
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
// 
//  http://www.apache.org/licenses/LICENSE-2.0
//      
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.

using System;
using System.Reflection;

namespace ArchiSteamFarm {
	internal static class SharedInfo {
		internal const ulong ArchiSteamID = 76561198006963719;
		internal const string ASF = "ASF";
		internal const ulong ASFGroupSteamID = 103582791440160998;
		internal const string AssemblyName = "ArchiSteamFarm";
		internal const string ConfigDirectory = "config";
		internal const string DebugDirectory = "debug";
		internal const string GithubReleaseURL = "https://api.github.com/repos/" + GithubRepo + "/releases"; // GitHub API is HTTPS only
		internal const string GithubRepo = "JustArchi/" + AssemblyName;
		internal const string GlobalConfigFileName = ASF + ".json";
		internal const string GlobalDatabaseFileName = ASF + ".db";
		internal const string LogFile = "log.txt";
		internal const string StatisticsServer = "asf.justarchi.net";
		internal const string UpdateDirectory = "_old";

		internal static Guid ModuleVersion => Assembly.GetEntryAssembly().ManifestModule.ModuleVersionId;
		internal static Version Version => Assembly.GetEntryAssembly().GetName().Version;
	}
}