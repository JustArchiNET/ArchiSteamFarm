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
using System.Net;
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

namespace ArchiSteamFarm.OfficialPlugins.ItemsMatcher;

internal static class Backend {
	internal static async Task<HttpStatusCode?> AnnounceForListing(Bot bot, IReadOnlyList<Asset> inventory, IReadOnlyCollection<Asset.EType> acceptedMatchableTypes, string tradeToken, string? nickname = null, string? avatarHash = null) {
		ArgumentNullException.ThrowIfNull(bot);

		if ((inventory == null) || (inventory.Count == 0)) {
			throw new ArgumentNullException(nameof(inventory));
		}

		if ((acceptedMatchableTypes == null) || (acceptedMatchableTypes.Count == 0)) {
			throw new ArgumentNullException(nameof(acceptedMatchableTypes));
		}

		if (string.IsNullOrEmpty(tradeToken)) {
			throw new ArgumentNullException(nameof(tradeToken));
		}

		if (tradeToken.Length != BotConfig.SteamTradeTokenLength) {
			throw new ArgumentOutOfRangeException(nameof(tradeToken));
		}

		Uri request = new(ArchiNet.URL, "/Api/Listing/Announce");

		AnnouncementRequest data = new(ASF.GlobalDatabase?.Identifier ?? Guid.NewGuid(), bot.SteamID, tradeToken, inventory, acceptedMatchableTypes, bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.MatchEverything), ASF.GlobalConfig?.MaxTradeHoldDuration ?? GlobalConfig.DefaultMaxTradeHoldDuration, nickname, avatarHash);

		BasicResponse? response = await bot.ArchiWebHandler.WebBrowser.UrlPost(request, data: data, requestOptions: WebBrowser.ERequestOptions.ReturnClientErrors).ConfigureAwait(false);

		return response?.StatusCode;
	}

	internal static async Task<(HttpStatusCode StatusCode, ImmutableHashSet<ListedUser> Users)?> GetListedUsersForMatching(Guid licenseID, Bot bot, IReadOnlyCollection<Asset> inventory, IReadOnlyCollection<Asset.EType> acceptedMatchableTypes, string tradeToken) {
		if (licenseID == Guid.Empty) {
			throw new ArgumentOutOfRangeException(nameof(licenseID));
		}

		ArgumentNullException.ThrowIfNull(bot);

		if ((inventory == null) || (inventory.Count == 0)) {
			throw new ArgumentNullException(nameof(inventory));
		}

		if ((acceptedMatchableTypes == null) || (acceptedMatchableTypes.Count == 0)) {
			throw new ArgumentNullException(nameof(acceptedMatchableTypes));
		}

		if (string.IsNullOrEmpty(tradeToken)) {
			throw new ArgumentNullException(nameof(tradeToken));
		}

		if (tradeToken.Length != BotConfig.SteamTradeTokenLength) {
			throw new ArgumentOutOfRangeException(nameof(tradeToken));
		}

		Uri request = new(ArchiNet.URL, "/Api/Listing/Inventories");

		Dictionary<string, string> headers = new(1, StringComparer.Ordinal) {
			{ "X-License-Key", licenseID.ToString("N") }
		};

		InventoriesRequest data = new(ASF.GlobalDatabase?.Identifier ?? Guid.NewGuid(), bot.SteamID, tradeToken, inventory, acceptedMatchableTypes, ASF.GlobalConfig?.MaxTradeHoldDuration ?? GlobalConfig.DefaultMaxTradeHoldDuration);

		ObjectResponse<GenericResponse<ImmutableHashSet<ListedUser>>>? response = await bot.ArchiWebHandler.WebBrowser.UrlPostToJsonObject<GenericResponse<ImmutableHashSet<ListedUser>>, InventoriesRequest>(request, headers, data, requestOptions: WebBrowser.ERequestOptions.ReturnClientErrors | WebBrowser.ERequestOptions.AllowInvalidBodyOnErrors).ConfigureAwait(false);

		if (response == null) {
			return null;
		}

		return (response.StatusCode, response.Content?.Result ?? ImmutableHashSet<ListedUser>.Empty);
	}

	internal static async Task<HttpStatusCode?> HeartBeatForListing(Bot bot) {
		ArgumentNullException.ThrowIfNull(bot);

		Uri request = new(ArchiNet.URL, "/Api/Listing/HeartBeat");

		HeartBeatRequest data = new(ASF.GlobalDatabase?.Identifier ?? Guid.NewGuid(), bot.SteamID);

		BasicResponse? response = await bot.ArchiWebHandler.WebBrowser.UrlPost(request, data: data, requestOptions: WebBrowser.ERequestOptions.ReturnClientErrors).ConfigureAwait(false);

		return response?.StatusCode;
	}
}
