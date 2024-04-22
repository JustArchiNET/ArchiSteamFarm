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
using System.Threading.Tasks;
using ArchiSteamFarm.Storage;
using JetBrains.Annotations;

namespace ArchiSteamFarm.Plugins.Interfaces;

/// <inheritdoc />
/// <summary>
///     Implementing this interface allows you to provide custom logic for updating your plugin to newer version.
///     Plugin updates are happening on usual basis per configuration of auto-updates from ASF, as well as other triggers such as update command.
///     If you're using GitHub platform with plugin releases, you might be interested in <see cref="IGitHubPluginUpdates" /> instead.
/// </summary>
[PublicAPI]
public interface IPluginUpdates : IPlugin {
	/// <summary>
	///     ASF will call this function for determining the target release asset URL to update to.
	/// </summary>
	/// <param name="asfVersion">Target ASF version that plugin update should be compatible with. In rare cases, this might not match currently running ASF version, in particular when updating to newer release and checking if any plugins are compatible with it.</param>
	/// <param name="asfVariant">ASF variant of current instance, which may be useful if you're providing different versions for different ASF variants.</param>
	/// <param name="asfUpdate">Boolean value specifying whether ASF is being updated right now to given version.</param>
	/// <param name="updateChannel">ASF update channel specified for this request. This might be different from the one specified in <see cref="GlobalConfig" />, as user might've specified other one for this request.</param>
	/// <param name="forced">Boolean value specifying whether user has requested forced plugin update, that is, one that should always execute if possible - even with the same and lower version (downgrade).</param>
	/// <returns>Target release asset URL that should be used for auto-update. It's permitted to return null if you want to skip update, e.g. because no new version is available.</returns>
	Task<Uri?> GetTargetReleaseURL(Version asfVersion, string asfVariant, bool asfUpdate, GlobalConfig.EUpdateChannel updateChannel, bool forced);

	/// <summary>
	///     ASF will call this method after update to the new plugin version has been finished, just before restart of the process.
	/// </summary>
	Task OnPluginUpdateFinished() => Task.CompletedTask;

	/// <summary>
	///     ASF will call this method before proceeding with an update to the new plugin version.
	/// </summary>
	Task OnPluginUpdateProceeding() => Task.CompletedTask;
}
