//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2022 Łukasz "JustArchi" Domeradzki
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
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using ArchiSteamFarm.Helpers;
using ArchiSteamFarm.IPC.Responses;
using ArchiSteamFarm.Web.Responses;
using SteamKit2;

namespace ArchiSteamFarm.Core;

internal static class ArchiNet {
	internal static Uri URL => new("https://asf.JustArchi.net");

	private static readonly ArchiCacheable<IReadOnlyCollection<ulong>> CachedBadBots = new(ResolveCachedBadBots, TimeSpan.FromDays(1));

	internal static async Task<string?> FetchBuildChecksum(Version version, string variant) {
		ArgumentNullException.ThrowIfNull(version);

		if (string.IsNullOrEmpty(variant)) {
			throw new ArgumentNullException(nameof(variant));
		}

		if (ASF.WebBrowser == null) {
			throw new InvalidOperationException(nameof(ASF.WebBrowser));
		}

		Uri request = new(URL, $"/Api/Checksum/{version}/{variant}");

		ObjectResponse<GenericResponse<string>>? response = await ASF.WebBrowser.UrlGetToJsonObject<GenericResponse<string>>(request).ConfigureAwait(false);

		if (response?.Content == null) {
			return null;
		}

		return response.Content.Result ?? "";
	}

	internal static async Task<bool?> IsBadBot(ulong steamID) {
		if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		(_, IReadOnlyCollection<ulong>? badBots) = await CachedBadBots.GetValue(ArchiCacheable<IReadOnlyCollection<ulong>>.EFallback.FailedNow).ConfigureAwait(false);

		return badBots?.Contains(steamID);
	}

	private static async Task<(bool Success, IReadOnlyCollection<ulong>? Result)> ResolveCachedBadBots() {
		if (ASF.GlobalDatabase == null) {
			throw new InvalidOperationException(nameof(ASF.WebBrowser));
		}

		if (ASF.WebBrowser == null) {
			throw new InvalidOperationException(nameof(ASF.WebBrowser));
		}

		Uri request = new(URL, "/Api/BadBots");

		ObjectResponse<GenericResponse<ImmutableHashSet<ulong>>>? response = await ASF.WebBrowser.UrlGetToJsonObject<GenericResponse<ImmutableHashSet<ulong>>>(request).ConfigureAwait(false);

		if (response?.Content?.Result == null) {
			return (false, ASF.GlobalDatabase.CachedBadBots);
		}

		ASF.GlobalDatabase.CachedBadBots.ReplaceIfNeededWith(response.Content.Result);

		return (true, response.Content.Result);
	}
}
