//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2020 Łukasz "JustArchi" Domeradzki
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
using Newtonsoft.Json;
using SteamKit2;

namespace ArchiSteamFarm.OfficialPlugins.SteamTokenDumper {
	internal sealed class RequestData {
#pragma warning disable IDE0052
		[JsonProperty(PropertyName = "apps", Required = Required.Always)]
		private readonly ImmutableDictionary<string, string> Apps;
#pragma warning restore IDE0052

#pragma warning disable IDE0052
		[JsonProperty(PropertyName = "depots", Required = Required.Always)]
		private readonly ImmutableDictionary<string, string> Depots;
#pragma warning restore IDE0052

#pragma warning disable IDE0052
		[JsonProperty(PropertyName = "guid", Required = Required.Always)]
		private readonly string Guid = ASF.GlobalDatabase?.Guid.ToString("N") ?? throw new ArgumentNullException(nameof(ASF.GlobalDatabase.Guid));
#pragma warning restore IDE0052

		private readonly ulong SteamID;

#pragma warning disable IDE0052
		[JsonProperty(PropertyName = "subs", Required = Required.Always)]
		private readonly ImmutableDictionary<string, string> Subs;
#pragma warning restore IDE0052

#pragma warning disable IDE0051, 414
		[JsonProperty(PropertyName = "token", Required = Required.Always)]
		private readonly string Token = SharedInfo.Token;
#pragma warning restore IDE0051, 414

#pragma warning disable IDE0051, 414
		[JsonProperty(PropertyName = "v", Required = Required.Always)]
		private readonly byte Version = SharedInfo.ApiVersion;
#pragma warning restore IDE0051, 414

#pragma warning disable IDE0051
		[JsonProperty(PropertyName = "steamid", Required = Required.Always)]
		private string SteamIDText => new SteamID(SteamID).Render();
#pragma warning restore IDE0051

		internal RequestData(ulong steamID, IEnumerable<KeyValuePair<uint, ulong>> apps, IEnumerable<KeyValuePair<uint, ulong>> accessTokens, IEnumerable<KeyValuePair<uint, string>> depots) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || (apps == null) || (accessTokens == null) || (depots == null)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(apps) + " || " + nameof(accessTokens) + " || " + nameof(depots));
			}

			SteamID = steamID;

			Apps = apps.ToImmutableDictionary(app => app.Key.ToString(), app => app.Value.ToString());
			Subs = accessTokens.ToImmutableDictionary(package => package.Key.ToString(), package => package.Value.ToString());
			Depots = depots.ToImmutableDictionary(depot => depot.Key.ToString(), depot => depot.Value);
		}
	}
}
