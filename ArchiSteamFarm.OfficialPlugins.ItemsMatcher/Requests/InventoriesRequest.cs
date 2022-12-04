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
using ArchiSteamFarm.Steam.Data;
using ArchiSteamFarm.Steam.Storage;
using Newtonsoft.Json;
using SteamKit2;

namespace ArchiSteamFarm.OfficialPlugins.ItemsMatcher.Requests;

internal sealed class InventoriesRequest {
	[JsonProperty(Required = Required.Always)]
	internal readonly Guid Guid;

	[JsonProperty(Required = Required.Always)]
	internal readonly ImmutableHashSet<AssetForListing> Inventory;

	[JsonProperty(Required = Required.Always)]
	internal readonly ImmutableHashSet<Asset.EType> MatchableTypes;

	[JsonProperty(Required = Required.Always)]
	internal readonly byte MaxTradeHoldDuration;

	[JsonProperty(Required = Required.Always)]
	internal readonly ulong SteamID;

	[JsonProperty(Required = Required.Always)]
	internal readonly string TradeToken;

	internal InventoriesRequest(Guid guid, ulong steamID, string tradeToken, IReadOnlyCollection<Asset> inventory, IReadOnlyCollection<Asset.EType> matchableTypes, byte maxTradeHoldDuration) {
		if (guid == Guid.Empty) {
			throw new ArgumentOutOfRangeException(nameof(guid));
		}

		if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		if (string.IsNullOrEmpty(tradeToken)) {
			throw new ArgumentNullException(nameof(tradeToken));
		}

		if (tradeToken.Length != BotConfig.SteamTradeTokenLength) {
			throw new ArgumentOutOfRangeException(nameof(tradeToken));
		}

		if ((inventory == null) || (inventory.Count == 0)) {
			throw new ArgumentNullException(nameof(inventory));
		}

		if ((matchableTypes == null) || (matchableTypes.Count == 0)) {
			throw new ArgumentNullException(nameof(matchableTypes));
		}

		Guid = guid;
		SteamID = steamID;
		TradeToken = tradeToken;
		Inventory = inventory.Select(static asset => new AssetForListing(asset)).ToImmutableHashSet();
		MatchableTypes = matchableTypes.ToImmutableHashSet();
		MaxTradeHoldDuration = maxTradeHoldDuration;
	}
}
