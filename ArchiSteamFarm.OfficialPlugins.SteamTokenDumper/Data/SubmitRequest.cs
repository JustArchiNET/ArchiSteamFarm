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
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Serialization;
using ArchiSteamFarm.Core;
using SteamKit2;

namespace ArchiSteamFarm.OfficialPlugins.SteamTokenDumper.Data;

internal sealed class SubmitRequest {
	private readonly ulong SteamID;

#pragma warning disable CA1822 // We can't make it static, STJ doesn't serialize it otherwise
	[JsonInclude]
	[JsonPropertyName("guid")]
	private string Guid => ASF.GlobalDatabase?.Identifier.ToString("N") ?? throw new InvalidOperationException(nameof(ASF.GlobalDatabase.Identifier));
#pragma warning restore CA1822 // We can't make it static, STJ doesn't serialize it otherwise

	[JsonInclude]
	[JsonPropertyName("steamid")]
	private string SteamIDText => new SteamID(SteamID).Render();

#pragma warning disable CA1822 // We can't make it static, STJ doesn't serialize it otherwise
	[JsonInclude]
	[JsonPropertyName("token")]
	private string Token => SharedInfo.Token;
#pragma warning restore CA1822 // We can't make it static, STJ doesn't serialize it otherwise

#pragma warning disable CA1822 // We can't make it static, STJ doesn't serialize it otherwise
	[JsonInclude]
	[JsonPropertyName("v")]
	private byte Version => SharedInfo.ApiVersion;
#pragma warning restore CA1822 // We can't make it static, STJ doesn't serialize it otherwise

	[JsonInclude]
	[JsonPropertyName("apps")]
	[JsonRequired]
	private ImmutableDictionary<string, string> Apps { get; init; }

	[JsonInclude]
	[JsonPropertyName("depots")]
	[JsonRequired]
	private ImmutableDictionary<string, string> Depots { get; init; }

	[JsonInclude]
	[JsonPropertyName("subs")]
	[JsonRequired]
	private ImmutableDictionary<string, string> Subs { get; init; }

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
