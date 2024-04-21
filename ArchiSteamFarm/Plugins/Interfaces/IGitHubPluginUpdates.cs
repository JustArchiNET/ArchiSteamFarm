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

/// <inheritdoc />
/// <summary>
///     Implementing this interface allows your plugin to update from published releases on GitHub.
///     At the minimum you must provide <see cref="RepositoryName" />.
///     If you're not following our ASF-PluginTemplate flow, that is, providing release asset named differently than "{PluginName}.zip" then you may also need to override <see cref="GetTargetReleaseAsset" /> function in order to select target asset based on custom rules.
///     If you have even more complex needs for updating your plugin, you should probably consider implementing base <see cref="IPluginUpdates" /> interface instead, where you can provide your own <see cref="GetTargetReleaseURL" /> implementation, with optional help from our <see cref="GitHubService" />.
/// </summary>
[PublicAPI]
public interface IGitHubPluginUpdates : IPluginUpdates {
	/// <summary>
	///     Boolean value that determines whether your plugin is able to update at the time of calling. You may provide false if, for example, you're inside a critical section and you don't want to update at this time, despite supporting updates otherwise.
	///     This effectively skips unnecessary request to GitHub if you're certain that you're not interested in any updates right now.
	/// </summary>
	bool CanUpdate => true;

	/// <summary>
	///     ASF will use this property as a target for GitHub updates. GitHub repository specified here must have valid releases that will be used for updates.
	/// </summary>
	/// <returns>Repository name in format of {Author}/{Repository}.</returns>
	/// <example>JustArchiNET/ArchiSteamFarm</example>
	string RepositoryName { get; }

	Task<Uri?> IPluginUpdates.GetTargetReleaseURL(Version asfVersion, string asfVariant, bool asfUpdate, GlobalConfig.EUpdateChannel updateChannel, bool forced) {
		ArgumentNullException.ThrowIfNull(asfVersion);
		ArgumentException.ThrowIfNullOrEmpty(asfVariant);

		if (!Enum.IsDefined(updateChannel)) {
			throw new InvalidEnumArgumentException(nameof(updateChannel), (int) updateChannel, typeof(GlobalConfig.EUpdateChannel));
		}

		return GetTargetReleaseURL(asfVersion, asfVariant, asfUpdate, updateChannel == GlobalConfig.EUpdateChannel.Stable, forced);
	}

	/// <summary>
	///     ASF will call this function for determining the target asset name to update to. This asset should be available in specified release. It's permitted to return null if you want to cancel update to given version. Default implementation provides vastly universal generic matching, see remarks for more info.
	/// </summary>
	/// <param name="asfVersion">Target ASF version that plugin update should be compatible with. In rare cases, this might not match currently running ASF version, in particular when updating to newer release and checking if any plugins are compatible with it.</param>
	/// <param name="asfVariant">ASF variant of current instance, which may be useful if you're providing different versions for different ASF variants.</param>
	/// <param name="newPluginVersion">The target (new) version of the plugin found available in <see cref="RepositoryName" />.</param>
	/// <param name="releaseAssets">Available release assets for auto-update. Those come directly from your release on GitHub.</param>
	/// <remarks>
	///     Default implementation will select release asset in following order:
	///     - {Name}-V{Major}-{Minor}-{Build}-{Revision}.zip
	///     - {Name}-V{Major}-{Minor}-{Build}.zip
	///     - {Name}-V{Major}-{Minor}.zip
	///     - {Name}-V{Major}.zip
	///     - {Name}.zip
	///     - *.zip, if exactly 1 release asset matching in the release
	///     Where:
	///     - {Name} will be tried out of <see cref="IPlugin.Name" /> and assembly name that provides your plugin type
	///     - {Major} is target major ASF version (A from A.B.C.D)
	///     - {Minor} is target minor ASF version (B from A.B.C.D)
	///     - {Build} is target build (patch) ASF version (C from A.B.C.D)
	///     - {Revision} is target revision ASF version (D from A.B.C.D)
	///     - * is a wildcard matching any string value
	///     For example, when updating MyAwesomePlugin declared in JustArchiNET.MyAwesomePlugin assembly with ASF version V6.0.1.3, it will select the first zip file available from those below:
	///     - MyAwesomePlugin-V6.0.1.3.zip
	///     - MyAwesomePlugin-V6.0.1.zip
	///     - MyAwesomePlugin-V6.0.zip
	///     - MyAwesomePlugin-V6.zip
	///     - MyAwesomePlugin.zip
	///     - JustArchiNET.MyAwesomePlugin-V6.0.1.3.zip
	///     - JustArchiNET.MyAwesomePlugin-V6.0.1.zip
	///     - JustArchiNET.MyAwesomePlugin-V6.0.zip
	///     - JustArchiNET.MyAwesomePlugin-V6.zip
	///     - JustArchiNET.MyAwesomePlugin.zip
	///     - *.zip, if exactly one match is found
	/// </remarks>
	/// <returns>Target release asset from those provided that should be used for auto-update. You may return null if the update is unavailable, for example, because ASF version/variant is determined unsupported, or due to any other reason.</returns>
	Task<ReleaseAsset?> GetTargetReleaseAsset(Version asfVersion, string asfVariant, Version newPluginVersion, IReadOnlyCollection<ReleaseAsset> releaseAssets) {
		ArgumentNullException.ThrowIfNull(asfVersion);
		ArgumentException.ThrowIfNullOrEmpty(asfVariant);
		ArgumentNullException.ThrowIfNull(newPluginVersion);

		if ((releaseAssets == null) || (releaseAssets.Count == 0)) {
			throw new ArgumentNullException(nameof(releaseAssets));
		}

		return Task.FromResult(FindPossibleMatch(asfVersion, newPluginVersion, releaseAssets));
	}

	protected ReleaseAsset? FindPossibleMatch(Version asfVersion, Version newPluginVersion, IReadOnlyCollection<ReleaseAsset> releaseAssets) {
		ArgumentNullException.ThrowIfNull(asfVersion);
		ArgumentNullException.ThrowIfNull(newPluginVersion);

		if ((releaseAssets == null) || (releaseAssets.Count == 0)) {
			throw new ArgumentNullException(nameof(releaseAssets));
		}

		Dictionary<string, ReleaseAsset> assetsByName = releaseAssets.ToDictionary(static asset => asset.Name, StringComparer.OrdinalIgnoreCase);

		foreach (string possibleMatch in GetPossibleMatches(asfVersion)) {
			if (assetsByName.TryGetValue(possibleMatch, out ReleaseAsset? targetAsset)) {
				return targetAsset;
			}
		}

		// The very last fallback in case user uses different naming scheme
		HashSet<ReleaseAsset> zipAssets = releaseAssets.Where(static asset => asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToHashSet();

		if (zipAssets.Count == 1) {
			return zipAssets.First();
		}

		ASF.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.PluginUpdateConflictingAssetsFound, Name, Version, newPluginVersion));

		return null;
	}

	protected async Task<Uri?> GetTargetReleaseURL(Version asfVersion, string asfVariant, bool asfUpdate, bool stable, bool forced) {
		ArgumentNullException.ThrowIfNull(asfVersion);
		ArgumentException.ThrowIfNullOrEmpty(asfVariant);

		if (!CanUpdate) {
			return null;
		}

		if (string.IsNullOrEmpty(RepositoryName) || (RepositoryName == SharedInfo.DefaultPluginTemplateGithubRepo)) {
			ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, nameof(RepositoryName)));

			return null;
		}

		ReleaseResponse? releaseResponse = await GitHubService.GetLatestRelease(RepositoryName, stable).ConfigureAwait(false);

		if (releaseResponse == null) {
			return null;
		}

		Version newVersion = new(releaseResponse.Tag);

		if (!forced && (Version >= newVersion)) {
			ASF.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.PluginUpdateNotFound, Name, Version, newVersion));

			return null;
		}

		if (releaseResponse.Assets.Count == 0) {
			ASF.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.PluginUpdateNoAssetFound, Name, Version, newVersion));

			return null;
		}

		ReleaseAsset? asset = await GetTargetReleaseAsset(asfVersion, asfVariant, newVersion, releaseResponse.Assets).ConfigureAwait(false);

		if ((asset == null) || !releaseResponse.Assets.Contains(asset)) {
			ASF.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.PluginUpdateNoAssetFound, Name, Version, newVersion));

			return null;
		}

		ASF.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.PluginUpdateFound, Name, Version, newVersion));

		return asset.DownloadURL;
	}

	private IEnumerable<string> GetPossibleMatches(Version version) {
		ArgumentNullException.ThrowIfNull(version);

		string pluginName = Name;

		if (!string.IsNullOrEmpty(pluginName)) {
			foreach (string possibleMatch in GetPossibleMatchesByName(version, pluginName)) {
				yield return possibleMatch;
			}
		}

		string? assemblyName = GetType().Assembly.GetName().Name;

		if (!string.IsNullOrEmpty(assemblyName)) {
			foreach (string possibleMatch in GetPossibleMatchesByName(version, assemblyName)) {
				yield return possibleMatch;
			}
		}
	}

	private static IEnumerable<string> GetPossibleMatchesByName(Version version, string name) {
		ArgumentNullException.ThrowIfNull(version);
		ArgumentException.ThrowIfNullOrEmpty(name);

		yield return $"{name}-V{version.Major}-{version.Minor}-{version.Build}-{version.Revision}.zip";
		yield return $"{name}-V{version.Major}-{version.Minor}-{version.Build}.zip";
		yield return $"{name}-V{version.Major}-{version.Minor}.zip";
		yield return $"{name}-V{version.Major}.zip";
		yield return $"{name}.zip";
	}
}
