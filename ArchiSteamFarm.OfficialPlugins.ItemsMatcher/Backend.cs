// ----------------------------------------------------------------------------------------------
//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// ----------------------------------------------------------------------------------------------
// |
// Copyright 2015-2026 Łukasz "JustArchi" Domeradzki
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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
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
using SteamKit2;

namespace ArchiSteamFarm.OfficialPlugins.ItemsMatcher;

internal static class Backend {
	internal static async Task<ObjectResponse<GenericResponse<BackgroundTaskResponse>>?> AnnounceDiffForListing(WebBrowser webBrowser, ulong steamID, IReadOnlyCollection<AssetForListing> inventory, string inventoryChecksum, IReadOnlyCollection<EAssetType> acceptedMatchableTypes, uint totalInventoryCount, bool matchEverything, string tradeToken, IReadOnlyCollection<AssetForListing> inventoryRemoved, string previousInventoryChecksum, string? nickname = null, string? avatarHash = null) {
		ArgumentNullException.ThrowIfNull(webBrowser);

		if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		ArgumentNullException.ThrowIfNull(inventory);
		ArgumentException.ThrowIfNullOrEmpty(inventoryChecksum);

		if ((acceptedMatchableTypes == null) || (acceptedMatchableTypes.Count == 0)) {
			throw new ArgumentNullException(nameof(acceptedMatchableTypes));
		}

		ArgumentOutOfRangeException.ThrowIfZero(totalInventoryCount);
		ArgumentException.ThrowIfNullOrEmpty(tradeToken);

		if (tradeToken.Length != BotConfig.SteamTradeTokenLength) {
			throw new ArgumentOutOfRangeException(nameof(tradeToken));
		}

		ArgumentNullException.ThrowIfNull(inventoryRemoved);
		ArgumentException.ThrowIfNullOrEmpty(previousInventoryChecksum);

		Uri request = new(ArchiNet.URL, "/Api/Listing/AnnounceDiff/v3");

		AnnouncementDiffRequest data = new(ASF.GlobalDatabase?.Identifier ?? Guid.NewGuid(), steamID, inventory, inventoryChecksum, acceptedMatchableTypes, totalInventoryCount, matchEverything, ASF.GlobalConfig?.MaxTradeHoldDuration ?? GlobalConfig.DefaultMaxTradeHoldDuration, tradeToken, inventoryRemoved, previousInventoryChecksum, nickname, avatarHash);

		return await webBrowser.UrlPostToJsonObject<GenericResponse<BackgroundTaskResponse>, AnnouncementDiffRequest>(request, data: data, requestOptions: WebBrowser.ERequestOptions.ReturnRedirections | WebBrowser.ERequestOptions.ReturnClientErrors | WebBrowser.ERequestOptions.AllowInvalidBodyOnErrors | WebBrowser.ERequestOptions.CompressRequest).ConfigureAwait(false);
	}

	internal static async Task<ObjectResponse<GenericResponse<BackgroundTaskResponse>>?> AnnounceForListing(WebBrowser webBrowser, ulong steamID, IReadOnlyList<AssetForListing> inventory, string inventoryChecksum, IReadOnlyCollection<EAssetType> acceptedMatchableTypes, uint totalInventoryCount, bool matchEverything, string tradeToken, string? nickname = null, string? avatarHash = null) {
		ArgumentNullException.ThrowIfNull(webBrowser);

		if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

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

		Uri request = new(ArchiNet.URL, "/Api/Listing/Announce/v6");

		AnnouncementRequest data = new(ASF.GlobalDatabase?.Identifier ?? Guid.NewGuid(), steamID, inventory, inventoryChecksum, acceptedMatchableTypes, totalInventoryCount, matchEverything, ASF.GlobalConfig?.MaxTradeHoldDuration ?? GlobalConfig.DefaultMaxTradeHoldDuration, tradeToken, nickname, avatarHash);

		return await webBrowser.UrlPostToJsonObject<GenericResponse<BackgroundTaskResponse>, AnnouncementRequest>(request, data: data, requestOptions: WebBrowser.ERequestOptions.ReturnRedirections | WebBrowser.ERequestOptions.ReturnClientErrors | WebBrowser.ERequestOptions.AllowInvalidBodyOnErrors | WebBrowser.ERequestOptions.CompressRequest).ConfigureAwait(false);
	}

	internal static async Task<ObjectResponse<GenericResponse<bool>>?> CheckAnnounceDiffForListing(WebBrowser webBrowser, ulong steamID, string previousInventoryChecksum) {
		ArgumentNullException.ThrowIfNull(webBrowser);

		if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		ArgumentException.ThrowIfNullOrEmpty(previousInventoryChecksum);

		Uri request = new(ArchiNet.URL, "/Api/Listing/AnnounceDiffCheck/v1");

		AnnouncementDiffCheckRequest data = new(ASF.GlobalDatabase?.Identifier ?? Guid.NewGuid(), steamID, previousInventoryChecksum);

		return await webBrowser.UrlPostToJsonObject<GenericResponse<bool>, AnnouncementDiffCheckRequest>(request, data: data, requestOptions: WebBrowser.ERequestOptions.ReturnRedirections | WebBrowser.ERequestOptions.ReturnClientErrors | WebBrowser.ERequestOptions.AllowInvalidBodyOnErrors | WebBrowser.ERequestOptions.CompressRequest).ConfigureAwait(false);
	}

	internal static string GenerateChecksumFor(IEnumerable<AssetForListing> assetsForListings) {
		ArgumentNullException.ThrowIfNull(assetsForListings);

		using IncrementalHash incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);

		Span<byte> buffer = stackalloc byte[sizeof(ulong)];

		foreach (AssetForListing asset in assetsForListings) {
			BinaryPrimitives.WriteUInt32LittleEndian(buffer[..sizeof(uint)], asset.Index);
			incrementalHash.AppendData(buffer[..sizeof(uint)]);

			BinaryPrimitives.WriteUInt64LittleEndian(buffer, asset.PreviousAssetID);
			incrementalHash.AppendData(buffer);

			BinaryPrimitives.WriteUInt64LittleEndian(buffer, asset.AssetID);
			incrementalHash.AppendData(buffer);

			BinaryPrimitives.WriteUInt64LittleEndian(buffer, asset.ClassID);
			incrementalHash.AppendData(buffer);

			BinaryPrimitives.WriteInt32LittleEndian(buffer[..sizeof(int)], (int) asset.Rarity);
			incrementalHash.AppendData(buffer[..sizeof(int)]);

			BinaryPrimitives.WriteUInt32LittleEndian(buffer[..sizeof(uint)], asset.RealAppID);
			incrementalHash.AppendData(buffer[..sizeof(uint)]);

			buffer[0] = asset.Tradable ? (byte) 1 : (byte) 0;
			incrementalHash.AppendData(buffer[..1]);

			BinaryPrimitives.WriteInt32LittleEndian(buffer[..sizeof(int)], (int) asset.Type);
			incrementalHash.AppendData(buffer[..sizeof(int)]);

			BinaryPrimitives.WriteUInt32LittleEndian(buffer[..sizeof(uint)], asset.Amount);
			incrementalHash.AppendData(buffer[..sizeof(uint)]);
		}

		return Convert.ToHexString(incrementalHash.GetHashAndReset());
	}

	internal static async Task<HttpStatusCode?> GetLicenseStatus(Guid licenseID, WebBrowser webBrowser) {
		ArgumentOutOfRangeException.ThrowIfEqual(licenseID, Guid.Empty);
		ArgumentNullException.ThrowIfNull(webBrowser);

		Uri request = new(ArchiNet.URL, "/Api/Licenses/Status");

		Dictionary<string, string> headers = new(1, StringComparer.Ordinal) {
			{ "X-License-Key", licenseID.ToString("N") }
		};

		ObjectResponse<GenericResponse>? response = await webBrowser.UrlGetToJsonObject<GenericResponse>(request, headers, requestOptions: WebBrowser.ERequestOptions.ReturnClientErrors | WebBrowser.ERequestOptions.AllowInvalidBodyOnErrors).ConfigureAwait(false);

		return response?.StatusCode;
	}

	internal static async Task<(HttpStatusCode StatusCode, ImmutableHashSet<ListedUser> Users)?> GetListedUsersForMatching(Guid licenseID, Bot bot, WebBrowser webBrowser, IReadOnlyCollection<Asset> inventory, IReadOnlyCollection<EAssetType> acceptedMatchableTypes) {
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

		return (response.StatusCode, response.Content?.Result ?? []);
	}

	internal static async Task<ObjectResponse<GenericResponse<ImmutableHashSet<SetPart>>>?> GetSetParts(WebBrowser webBrowser, ulong steamID, IReadOnlyCollection<EAssetType> matchableTypes, IReadOnlyCollection<uint> realAppIDs, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(webBrowser);

		if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		if ((matchableTypes == null) || (matchableTypes.Count == 0)) {
			throw new ArgumentNullException(nameof(matchableTypes));
		}

		if ((realAppIDs == null) || (realAppIDs.Count == 0)) {
			throw new ArgumentNullException(nameof(realAppIDs));
		}

		Uri request = new(ArchiNet.URL, "/Api/SetParts/Request");

		SetPartsRequest data = new(ASF.GlobalDatabase?.Identifier ?? Guid.NewGuid(), steamID, matchableTypes, realAppIDs);

		return await webBrowser.UrlPostToJsonObject<GenericResponse<ImmutableHashSet<SetPart>>, SetPartsRequest>(request, data: data, requestOptions: WebBrowser.ERequestOptions.ReturnRedirections | WebBrowser.ERequestOptions.ReturnClientErrors | WebBrowser.ERequestOptions.AllowInvalidBodyOnErrors | WebBrowser.ERequestOptions.CompressRequest, cancellationToken: cancellationToken).ConfigureAwait(false);
	}

	internal static async Task<BasicResponse?> HeartBeatForListing(Bot bot, WebBrowser webBrowser) {
		ArgumentNullException.ThrowIfNull(bot);
		ArgumentNullException.ThrowIfNull(webBrowser);

		Uri request = new(ArchiNet.URL, "/Api/Listing/HeartBeat");

		HeartBeatRequest data = new(ASF.GlobalDatabase?.Identifier ?? Guid.NewGuid(), bot.SteamID);

		return await webBrowser.UrlPost(request, data: data, requestOptions: WebBrowser.ERequestOptions.ReturnRedirections | WebBrowser.ERequestOptions.ReturnClientErrors | WebBrowser.ERequestOptions.CompressRequest).ConfigureAwait(false);
	}

	internal static async Task<ObjectResponse<GenericResponse<BackgroundTaskResponse>>?> PollResult(WebBrowser webBrowser, ulong steamID, Guid requestID) {
		ArgumentNullException.ThrowIfNull(webBrowser);

		if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		ArgumentOutOfRangeException.ThrowIfEqual(requestID, Guid.Empty);

		if (BuildInfo.IsCustomBuild) {
			return null;
		}

		Uri request = new(ArchiNet.URL, $"/Api/Listing/PollResult/{steamID}/{requestID:N}");

		return await webBrowser.UrlGetToJsonObject<GenericResponse<BackgroundTaskResponse>>(request, requestOptions: WebBrowser.ERequestOptions.ReturnRedirections | WebBrowser.ERequestOptions.ReturnClientErrors | WebBrowser.ERequestOptions.AllowInvalidBodyOnErrors).ConfigureAwait(false);
	}
}
