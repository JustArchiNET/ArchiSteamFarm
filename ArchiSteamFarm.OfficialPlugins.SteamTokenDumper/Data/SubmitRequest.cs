//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2023 Łukasz "JustArchi" Domeradzki
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
using System.Globalization;
using ArchiSteamFarm.Core;
using Newtonsoft.Json;
using SteamKit2;

namespace ArchiSteamFarm.OfficialPlugins.SteamTokenDumper.Data;

internal sealed class SubmitRequest {
	[JsonProperty("guid", Required = Required.Always)]
	private static string Guid => ASF.GlobalDatabase?.Identifier.ToString("N") ?? throw new InvalidOperationException(nameof(ASF.GlobalDatabase.Identifier));

	[JsonProperty("token", Required = Required.Always)]
	private static string Token => SharedInfo.Token;

	[JsonProperty("v", Required = Required.Always)]
	private static byte Version => SharedInfo.ApiVersion;

	[JsonProperty("apps", Required = Required.Always)]
	private readonly ImmutableDictionary<string, string> Apps;

	[JsonProperty("depots", Required = Required.Always)]
	private readonly ImmutableDictionary<string, string> Depots;

	private readonly ulong SteamID;

	[JsonProperty("subs", Required = Required.Always)]
	private readonly ImmutableDictionary<string, string> Subs;

	[JsonProperty("steamid", Required = Required.Always)]
	private string SteamIDText => new SteamID(SteamID).Render();

	internal SubmitRequest(ulong steamID, IReadOnlyCollection<KeyValuePair<uint, ulong>> apps, IReadOnlyCollection<KeyValuePair<uint, ulong>> accessTokens, IReadOnlyCollection<KeyValuePair<uint, string>> depots) {
		if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		ArgumentNullException.ThrowIfNull(apps);
		ArgumentNullException.ThrowIfNull(accessTokens);
		ArgumentNullException.ThrowIfNull(depots);

		SteamID = steamID;

		Apps = apps.ToImmutableDictionary(static app => app.Key.ToString(CultureInfo.InvariantCulture), static app => app.Value.ToString(CultureInfo.InvariantCulture));
		Subs = accessTokens.ToImmutableDictionary(static package => package.Key.ToString(CultureInfo.InvariantCulture), static package => package.Value.ToString(CultureInfo.InvariantCulture));
		Depots = depots.ToImmutableDictionary(static depot => depot.Key.ToString(CultureInfo.InvariantCulture), static depot => depot.Value);
	}
}
