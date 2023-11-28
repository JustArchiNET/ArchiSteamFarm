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
using ArchiSteamFarm.Steam.Data;
using Newtonsoft.Json;
using SteamKit2;

namespace ArchiSteamFarm.OfficialPlugins.ItemsMatcher.Data;

internal sealed class SetPartsRequest {
	[JsonProperty(Required = Required.Always)]
	internal readonly Guid Guid;

	[JsonProperty(Required = Required.Always)]
	internal readonly ImmutableHashSet<Asset.EType> MatchableTypes;

	[JsonProperty(Required = Required.Always)]
	internal readonly ImmutableHashSet<uint> RealAppIDs;

	[JsonProperty(Required = Required.Always)]
	internal readonly ulong SteamID;

	internal SetPartsRequest(Guid guid, ulong steamID, ICollection<Asset.EType> matchableTypes, ICollection<uint> realAppIDs) {
		ArgumentOutOfRangeException.ThrowIfEqual(guid, Guid.Empty);

		if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		if ((matchableTypes == null) || (matchableTypes.Count == 0)) {
			throw new ArgumentNullException(nameof(matchableTypes));
		}

		if ((realAppIDs == null) || (realAppIDs.Count == 0)) {
			throw new ArgumentNullException(nameof(realAppIDs));
		}

		Guid = guid;
		SteamID = steamID;
		MatchableTypes = matchableTypes.ToImmutableHashSet();
		RealAppIDs = realAppIDs.ToImmutableHashSet();
	}
}
