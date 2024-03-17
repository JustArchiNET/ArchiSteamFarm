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

namespace ArchiSteamFarm.OfficialPlugins.SteamTokenDumper;

internal static class SharedInfo {
	internal const byte ApiVersion = 2;
	internal const byte AppInfosPerSingleRequest = byte.MaxValue;
	internal const byte HoursBetweenUploads = 24;
	internal const byte MaximumHoursBetweenRefresh = 8; // Per single bot account, makes sense to be 2 or 3 times less than MinimumHoursBetweenUploads
	internal const byte MaximumMinutesBeforeFirstUpload = 60; // Must be greater or equal to MinimumMinutesBeforeFirstUpload
	internal const byte MinimumMinutesBeforeFirstUpload = 10; // Must be less or equal to MaximumMinutesBeforeFirstUpload
	internal const byte MinimumMinutesBetweenUploads = 5; // Rate limiting for the server
	internal const string ServerURL = "https://tokendumper-asf.steamdb.info";
	internal const string Token = "STEAM_TOKEN_DUMPER_TOKEN"; // This is filled automatically during our CI build with API key provided by xPaw for ASF project

	internal static bool HasValidToken => Token.Length == 128;
}
