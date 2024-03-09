//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
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
using JetBrains.Annotations;

namespace ArchiSteamFarm.Plugins.Interfaces;

[PublicAPI]
public interface IPluginUpdates : IPlugin {
	/// <summary>
	///     ASF will use this property as a target for GitHub updates. GitHub repository specified here must have valid releases that will be used for updates.
	/// </summary>
	/// <returns>Repository name in format of {Author}/{Repository}.</returns>
	/// <example>JustArchiNET/ArchiSteamFarm</example>
	string RepositoryName { get; }

	/// <summary>
	///     ASF will call this function for determining the target asset name to update to. This asset should be available in specified release. It's permitted to return null/empty if you want to cancel update to given version.
	/// </summary>
	/// <param name="asfVersion">Target ASF version that plugin update should be compatible with. In rare cases, this might not match currently running ASF version, in particular when updating to newer release and checking if any plugins are compatible with it.</param>
	/// <param name="asfVariant">ASF variant of current instance, which may be useful if you're providing different versions for different ASF variants.</param>
	/// <param name="newPluginVersion">The target (new) version of the plugin found available in <see cref="RepositoryName" />.</param>
	/// <param name="releaseAssets">Available release assets for auto-update. Those come directly from your release on GitHub.</param>
	/// <returns>Target release asset from <see cref="releaseAssets" /> that should be used for auto-update. You may return null if the update is unavailable, for example, because ASF version/variant is determined unsupported, or due to any other custom reason.</returns>
	Task<ReleaseAsset?> GetTargetReleaseAsset(Version asfVersion, string asfVariant, Version newPluginVersion, IReadOnlyCollection<ReleaseAsset> releaseAssets);

	/// <summary>
	///     ASF will call this method after update to a particular ASF version has been finished, just before restart of the process.
	/// </summary>
	/// <param name="currentVersion">The current (old) version of ASF program.</param>
	/// <param name="newVersion">The target (new) version of ASF program.</param>
	Task OnUpdateFinished(Version currentVersion, Version newVersion);

	/// <summary>
	///     ASF will call this method before proceeding with an update to a particular ASF version.
	/// </summary>
	/// <param name="currentVersion">The current (old) version of ASF program.</param>
	/// <param name="newVersion">The target (new) version of ASF program.</param>
	Task OnUpdateProceeding(Version currentVersion, Version newVersion);
}
