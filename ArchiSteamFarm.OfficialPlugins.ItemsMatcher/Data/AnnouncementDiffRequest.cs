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
using System.Text.Json.Serialization;
using ArchiSteamFarm.Steam.Data;
using ArchiSteamFarm.Steam.Storage;
using SteamKit2;

namespace ArchiSteamFarm.OfficialPlugins.ItemsMatcher.Data;

internal sealed class AnnouncementDiffRequest : AnnouncementRequest {
	[JsonInclude]
	[JsonRequired]
	private ImmutableHashSet<AssetForListing> InventoryRemoved { get; init; }

	[JsonInclude]
	[JsonRequired]
	private string PreviousInventoryChecksum { get; init; }

	internal AnnouncementDiffRequest(Guid guid, ulong steamID, IReadOnlyCollection<AssetForListing> inventory, string inventoryChecksum, IReadOnlyCollection<EAssetType> matchableTypes, uint totalInventoryCount, bool matchEverything, byte maxTradeHoldDuration, string tradeToken, IReadOnlyCollection<AssetForListing> inventoryRemoved, string previousInventoryChecksum, string? nickname = null, string? avatarHash = null) : base(guid, steamID, inventory, inventoryChecksum, matchableTypes, totalInventoryCount, matchEverything, maxTradeHoldDuration, tradeToken, nickname, avatarHash) {
		ArgumentOutOfRangeException.ThrowIfEqual(guid, Guid.Empty);

		if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		ArgumentNullException.ThrowIfNull(inventory);
		ArgumentException.ThrowIfNullOrEmpty(inventoryChecksum);

		if ((matchableTypes == null) || (matchableTypes.Count == 0)) {
			throw new ArgumentNullException(nameof(matchableTypes));
		}

		ArgumentOutOfRangeException.ThrowIfZero(totalInventoryCount);
		ArgumentException.ThrowIfNullOrEmpty(tradeToken);

		if (tradeToken.Length != BotConfig.SteamTradeTokenLength) {
			throw new ArgumentOutOfRangeException(nameof(tradeToken));
		}

		ArgumentNullException.ThrowIfNull(inventoryRemoved);
		ArgumentException.ThrowIfNullOrEmpty(previousInventoryChecksum);

		InventoryRemoved = inventoryRemoved.ToImmutableHashSet();
		PreviousInventoryChecksum = previousInventoryChecksum;
	}
}
