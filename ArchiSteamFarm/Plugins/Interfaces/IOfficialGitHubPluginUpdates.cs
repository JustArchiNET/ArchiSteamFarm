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
using System.Collections.Generic;
using System.Threading.Tasks;
using ArchiSteamFarm.Web.GitHub.Data;

namespace ArchiSteamFarm.Plugins.Interfaces;

internal interface IOfficialGitHubPluginUpdates : IGitHubPluginUpdates {
	Task<ReleaseAsset?> IGitHubPluginUpdates.GetTargetReleaseAsset(Version asfVersion, string asfVariant, Version newPluginVersion, IReadOnlyCollection<ReleaseAsset> releaseAssets) {
		ArgumentNullException.ThrowIfNull(asfVersion);
		ArgumentException.ThrowIfNullOrEmpty(asfVariant);
		ArgumentNullException.ThrowIfNull(newPluginVersion);

		if ((releaseAssets == null) || (releaseAssets.Count == 0)) {
			throw new ArgumentNullException(nameof(releaseAssets));
		}

		// For official plugins, the ASF version must match the plugin version
		// Refuse to find the match if that's not the case, otherwise fallback to default implementation
		return Task.FromResult(asfVersion == newPluginVersion ? FindPossibleMatch(asfVersion, newPluginVersion, releaseAssets) : null);
	}
}
