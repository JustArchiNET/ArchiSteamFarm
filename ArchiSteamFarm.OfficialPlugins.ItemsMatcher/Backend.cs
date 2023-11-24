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
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.IPC.Responses;
using ArchiSteamFarm.OfficialPlugins.ItemsMatcher.Data;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Data;
using ArchiSteamFarm.Steam.Storage;
using ArchiSteamFarm.Storage;
using ArchiSteamFarm.Web;
using ArchiSteamFarm.Web.Responses;
using Newtonsoft.Json;
using SteamKit2;

namespace ArchiSteamFarm.OfficialPlugins.ItemsMatcher;

internal static class Backend {
	internal static async Task<BasicResponse?> AnnounceForListing(ulong steamID, WebBrowser webBrowser, ICollection<AssetForListing> inventory, string inventoryChecksum, IReadOnlyCollection<Asset.EType> acceptedMatchableTypes, uint totalInventoryCount, bool matchEverything, string tradeToken, ICollection<AssetForListing>? inventoryRemoved = null, string? previousInventoryChecksum = null, string? nickname = null, string? avatarHash = null) {
		if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		ArgumentNullException.ThrowIfNull(webBrowser);

		if ((inventory == null) || (inventory.Count == 0)) {
			throw new ArgumentNullException(nameof(inventory));
		}

		ArgumentException.ThrowIfNullOrEmpty(inventoryChecksum);

		if ((acceptedMatchableTypes == null) || (acceptedMatchableTypes.Count == 0)) {
			throw new ArgumentNullException(nameof(acceptedMatchableTypes));
		}

		ArgumentOutOfRangeException.ThrowIfZero(totalInventoryCount);
		ArgumentException.ThrowIfNullOrEmpty(tradeToken);

		if (tradeToken.Length != BotConfig.SteamTradeTokenLength) {
			throw new ArgumentOutOfRangeException(nameof(tradeToken));
		}

		Uri request = new(ArchiNet.URL, "/Api/Listing/Announce/v4");

		AnnouncementRequest data = new(ASF.GlobalDatabase?.Identifier ?? Guid.NewGuid(), steamID, tradeToken, inventory, inventoryChecksum, acceptedMatchableTypes, totalInventoryCount, matchEverything, ASF.GlobalConfig?.MaxTradeHoldDuration ?? GlobalConfig.DefaultMaxTradeHoldDuration, inventoryRemoved, previousInventoryChecksum, nickname, avatarHash);

		return await webBrowser.UrlPost(request, data: data, requestOptions: WebBrowser.ERequestOptions.ReturnRedirections | WebBrowser.ERequestOptions.ReturnClientErrors | WebBrowser.ERequestOptions.CompressRequest).ConfigureAwait(false);
	}

	internal static string GenerateChecksumFor(IList<AssetForListing> assetsForListings) {
		if ((assetsForListings == null) || (assetsForListings.Count == 0)) {
			throw new ArgumentNullException(nameof(assetsForListings));
		}

		string json = JsonConvert.SerializeObject(assetsForListings);
		byte[] bytes = Encoding.UTF8.GetBytes(json);

		return Utilities.GenerateChecksumFor(bytes);
	}

	internal static async Task<(HttpStatusCode StatusCode, ImmutableHashSet<ListedUser> Users)?> GetListedUsersForMatching(Guid licenseID, Bot bot, WebBrowser webBrowser, IReadOnlyCollection<Asset> inventory, IReadOnlyCollection<Asset.EType> acceptedMatchableTypes) {
		ArgumentOutOfRangeException.ThrowIfEqual(licenseID, Guid.Empty);
		ArgumentNullException.ThrowIfNull(bot);
		ArgumentNullException.ThrowIfNull(webBrowser);

		if ((inventory == null) || (inventory.Count == 0)) {
			throw new ArgumentNullException(nameof(inventory));
		}

		if ((acceptedMatchableTypes == null) || (acceptedMatchableTypes.Count == 0)) {
			throw new ArgumentNullException(nameof(acceptedMatchableTypes));
		}

		Uri request = new(ArchiNet.URL, "/Api/Listing/Inventories/v2");

		Dictionary<string, string> headers = new(1, StringComparer.Ordinal) {
			{ "X-License-Key", licenseID.ToString("N") }
		};

		InventoriesRequest data = new(ASF.GlobalDatabase?.Identifier ?? Guid.NewGuid(), bot.SteamID, inventory, acceptedMatchableTypes);

		ObjectResponse<GenericResponse<ImmutableHashSet<ListedUser>>>? response = await webBrowser.UrlPostToJsonObject<GenericResponse<ImmutableHashSet<ListedUser>>, InventoriesRequest>(request, headers, data, requestOptions: WebBrowser.ERequestOptions.ReturnClientErrors | WebBrowser.ERequestOptions.AllowInvalidBodyOnErrors | WebBrowser.ERequestOptions.CompressRequest).ConfigureAwait(false);

		if (response == null) {
			return null;
		}

		return (response.StatusCode, response.Content?.Result ?? ImmutableHashSet<ListedUser>.Empty);
	}

	internal static async Task<BasicResponse?> HeartBeatForListing(Bot bot, WebBrowser webBrowser) {
		ArgumentNullException.ThrowIfNull(bot);
		ArgumentNullException.ThrowIfNull(webBrowser);

		Uri request = new(ArchiNet.URL, "/Api/Listing/HeartBeat");

		HeartBeatRequest data = new(ASF.GlobalDatabase?.Identifier ?? Guid.NewGuid(), bot.SteamID);

		return await webBrowser.UrlPost(request, data: data, requestOptions: WebBrowser.ERequestOptions.ReturnRedirections | WebBrowser.ERequestOptions.ReturnClientErrors | WebBrowser.ERequestOptions.CompressRequest).ConfigureAwait(false);
	}
}
