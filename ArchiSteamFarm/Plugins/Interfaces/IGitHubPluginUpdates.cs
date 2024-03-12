// ----------------------------------------------------------------------------------------------
//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// ----------------------------------------------------------------------------------------------
//
// Copyright 2015-2024 Łukasz "JustArchi" Domeradzki
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
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Storage;
using ArchiSteamFarm.Web.GitHub;
using ArchiSteamFarm.Web.GitHub.Data;
using JetBrains.Annotations;

namespace ArchiSteamFarm.Plugins.Interfaces;

[PublicAPI]
public interface IGitHubPluginUpdates : IPluginUpdates {
	/// <summary>
	///     Boolean value that determines whether your plugin is able to update at the time of calling. You may provide false if, for example, you're inside a critical section and you don't want to update at this time, despite supporting updates otherwise.
	/// </summary>
	bool CanUpdate => true;

	/// <summary>
	///     ASF will use this property as a target for GitHub updates. GitHub repository specified here must have valid releases that will be used for updates.
	/// </summary>
	/// <returns>Repository name in format of {Author}/{Repository}.</returns>
	/// <example>JustArchiNET/ArchiSteamFarm</example>
	string RepositoryName { get; }

	Task<Uri?> IPluginUpdates.GetTargetReleaseURL(Version asfVersion, string asfVariant, GlobalConfig.EUpdateChannel updateChannel) {
		ArgumentNullException.ThrowIfNull(asfVersion);
		ArgumentException.ThrowIfNullOrEmpty(asfVariant);

		if (!Enum.IsDefined(updateChannel)) {
			throw new InvalidEnumArgumentException(nameof(updateChannel), (int) updateChannel, typeof(GlobalConfig.EUpdateChannel));
		}

		return GetTargetReleaseURL(asfVersion, asfVariant, updateChannel == GlobalConfig.EUpdateChannel.Stable);
	}

	protected async Task<Uri?> GetTargetReleaseURL(Version asfVersion, string asfVariant, bool stable) {
		ArgumentNullException.ThrowIfNull(asfVersion);
		ArgumentException.ThrowIfNullOrEmpty(asfVariant);

		if (!CanUpdate) {
			return null;
		}

		if (string.IsNullOrEmpty(RepositoryName)) {
			ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, nameof(RepositoryName)));

			return null;
		}

		ReleaseResponse? releaseResponse = await GitHubService.GetLatestRelease(RepositoryName, stable).ConfigureAwait(false);

		if (releaseResponse == null) {
			return null;
		}

		Version newVersion = new(releaseResponse.Tag);

		if (Version >= newVersion) {
			ASF.ArchiLogger.LogGenericInfo($"No update available for {Name} plugin: {Version} >= {newVersion}.");

			return null;
		}

		ASF.ArchiLogger.LogGenericInfo($"Updating {Name} plugin from version {Version} to {newVersion}...");

		ReleaseAsset? asset = await GetTargetReleaseAsset(asfVersion, asfVariant, newVersion, releaseResponse.Assets).ConfigureAwait(false);

		if ((asset == null) || !releaseResponse.Assets.Contains(asset)) {
			return null;
		}

		return asset.DownloadURL;
	}

	/// <summary>
	///     ASF will call this function for determining the target asset name to update to. This asset should be available in specified release. It's permitted to return null/empty if you want to cancel update to given version. Default implementation provides simple resolve based on flow from JustArchiNET/ASF-PluginTemplate repository.
	/// </summary>
	/// <param name="asfVersion">Target ASF version that plugin update should be compatible with. In rare cases, this might not match currently running ASF version, in particular when updating to newer release and checking if any plugins are compatible with it.</param>
	/// <param name="asfVariant">ASF variant of current instance, which may be useful if you're providing different versions for different ASF variants.</param>
	/// <param name="newPluginVersion">The target (new) version of the plugin found available in <see cref="RepositoryName" />.</param>
	/// <param name="releaseAssets">Available release assets for auto-update. Those come directly from your release on GitHub.</param>
	/// <returns>Target release asset from those provided that should be used for auto-update. You may return null if the update is unavailable, for example, because ASF version/variant is determined unsupported, or due to any other custom reason.</returns>
	Task<ReleaseAsset?> GetTargetReleaseAsset(Version asfVersion, string asfVariant, Version newPluginVersion, IReadOnlyCollection<ReleaseAsset> releaseAssets) {
		ArgumentNullException.ThrowIfNull(asfVersion);
		ArgumentException.ThrowIfNullOrEmpty(asfVariant);
		ArgumentNullException.ThrowIfNull(newPluginVersion);
		ArgumentNullException.ThrowIfNull(releaseAssets);

		return Task.FromResult(releaseAssets.FirstOrDefault(asset => asset.Name == $"{Name}.zip"));
	}
}
