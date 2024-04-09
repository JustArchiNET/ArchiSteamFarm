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
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using AngleSharp.Dom;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers;
using ArchiSteamFarm.Helpers.Json;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Steam.Data;
using ArchiSteamFarm.Steam.Exchange;
using ArchiSteamFarm.Storage;
using ArchiSteamFarm.Web;
using ArchiSteamFarm.Web.Responses;
using JetBrains.Annotations;
using SteamKit2;

namespace ArchiSteamFarm.Steam.Integration;

public sealed class ArchiWebHandler : IDisposable {
	// Steam network (ArchiHandler) works unstable with more items than this (throwing upon description details), while Steam web (ArchiWebHandler) silently limits to this value maximum
	internal const ushort MaxItemsInSingleInventoryRequest = 5000;

	private const string EconService = "IEconService";
	private const string LoyaltyRewardsService = "ILoyaltyRewardsService";
	private const byte MinimumSessionValidityInSeconds = 10;
	private const byte SessionIDLength = 24; // For maximum compatibility, should be divisible by 2 and match the length of "sessionid" property that Steam uses across their websites
	private const string SteamAppsService = "ISteamApps";

	[PublicAPI]
	public static Uri SteamCheckoutURL => new("https://checkout.steampowered.com");

	[PublicAPI]
	public static Uri SteamCommunityURL => new("https://steamcommunity.com");

	[PublicAPI]
	public static Uri SteamHelpURL => new("https://help.steampowered.com");

	[PublicAPI]
	public static Uri SteamStoreURL => new("https://store.steampowered.com");

	private static ushort WebLimiterDelay => ASF.GlobalConfig?.WebLimiterDelay ?? GlobalConfig.DefaultWebLimiterDelay;

	[PublicAPI]
	public WebBrowser WebBrowser { get; }

	private readonly Bot Bot;
	private readonly SemaphoreSlim SessionSemaphore = new(1, 1);

	private bool Initialized;
	private DateTime LastSessionCheck;
	private bool MarkingInventoryScheduled;
	private DateTime SessionValidUntil;
	private string? VanityURL;

	internal ArchiWebHandler(Bot bot) {
		ArgumentNullException.ThrowIfNull(bot);

		Bot = bot;
		WebBrowser = new WebBrowser(bot.ArchiLogger, ASF.GlobalConfig?.WebProxy);
	}

	public void Dispose() {
		SessionSemaphore.Dispose();
		WebBrowser.Dispose();
	}

	[PublicAPI]
	public async Task<bool> CancelTradeOffer(ulong tradeID) {
		ArgumentOutOfRangeException.ThrowIfZero(tradeID);

		Uri request = new(SteamCommunityURL, $"/tradeoffer/{tradeID}/cancel");

		return await UrlPostWithSession(request).ConfigureAwait(false);
	}

	[PublicAPI]
	public async Task<string?> GetAbsoluteProfileURL(bool waitForInitialization = true) {
		if (waitForInitialization && !Initialized) {
			byte connectionTimeout = ASF.GlobalConfig?.ConnectionTimeout ?? GlobalConfig.DefaultConnectionTimeout;

			for (byte i = 0; (i < connectionTimeout) && !Initialized && Bot.IsConnectedAndLoggedOn; i++) {
				await Task.Delay(1000).ConfigureAwait(false);
			}

			if (!Initialized) {
				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);

				return null;
			}
		}

		return string.IsNullOrEmpty(VanityURL) ? $"/profiles/{Bot.SteamID}" : $"/id/{VanityURL}";
	}

	[PublicAPI]
	public async Task<ImmutableHashSet<BoosterCreatorEntry>?> GetBoosterCreatorEntries() {
		Uri request = new(SteamCommunityURL, "/tradingcards/boostercreator?l=english");

		using HtmlDocumentResponse? response = await UrlGetToHtmlDocumentWithSession(request, checkSessionPreemptively: false).ConfigureAwait(false);

		if (response?.Content == null) {
			return null;
		}

		IList<INode> scriptNodes = response.Content.SelectNodes("//script[@type='text/javascript']");

		if (scriptNodes.Count == 0) {
			Bot.ArchiLogger.LogNullError(scriptNodes);

			return null;
		}

		ImmutableHashSet<BoosterCreatorEntry>? result = null;

		foreach (INode scriptNode in scriptNodes) {
			int startIndex = scriptNode.TextContent.IndexOf("CBoosterCreatorPage.Init(", StringComparison.Ordinal);

			if (startIndex < 0) {
				continue;
			}

			startIndex += 25;

			int endIndex = scriptNode.TextContent.IndexOf("],", startIndex, StringComparison.Ordinal);

			if (endIndex <= startIndex) {
				Bot.ArchiLogger.LogNullError(endIndex);

				return null;
			}

			string json = scriptNode.TextContent[startIndex..(endIndex + 1)];

			try {
				result = json.ToJsonObject<ImmutableHashSet<BoosterCreatorEntry>>();
			} catch (Exception e) {
				Bot.ArchiLogger.LogGenericException(e);

				return null;
			}

			break;
		}

		if (result == null) {
			Bot.ArchiLogger.LogNullError(result);

			return null;
		}

		return result;
	}

	[PublicAPI]
	public async Task<HashSet<uint>?> GetBoosterEligibility() {
		Uri request = new(SteamCommunityURL, "/my/ajaxgetboostereligibility?l=english");

		using HtmlDocumentResponse? response = await UrlGetToHtmlDocumentWithSession(request, checkSessionPreemptively: false).ConfigureAwait(false);

		if (response?.Content == null) {
			return null;
		}

		HashSet<uint> result = [];

		IEnumerable<IAttr> linkNodes = response.Content.SelectNodes<IAttr>("//li[@class='booster_eligibility_game']/a/@href");

		foreach (string hrefText in linkNodes.Select(static linkNode => linkNode.Value)) {
			if (string.IsNullOrEmpty(hrefText)) {
				Bot.ArchiLogger.LogNullError(hrefText);

				return null;
			}

			int index = hrefText.LastIndexOf('/');

			if ((index <= 0) || (hrefText.Length <= index + 2)) {
				Bot.ArchiLogger.LogNullError(index);

				return null;
			}

			string appIDText = hrefText[(index + 1)..];

			if (string.IsNullOrEmpty(appIDText) || !uint.TryParse(appIDText, out uint appID) || (appID == 0)) {
				Bot.ArchiLogger.LogNullError(appIDText);

				return null;
			}

			result.Add(appID);
		}

		return result;
	}

	/// <remarks>
	///     If targetting inventory of this <see cref="Bot" /> instance, consider using much more efficient <see cref="ArchiHandler.GetMyInventoryAsync" /> instead.
	///     This method should be used exclusively for foreign inventories (other users), but in special circumstances it can be used for fetching bot's own inventory as well.
	/// </remarks>
	[PublicAPI]
	public async IAsyncEnumerable<Asset> GetInventoryAsync(ulong steamID = 0, uint appID = Asset.SteamAppID, ulong contextID = Asset.SteamCommunityContextID) {
		ArgumentOutOfRangeException.ThrowIfZero(appID);
		ArgumentOutOfRangeException.ThrowIfZero(contextID);

		if (steamID == 0) {
			if (!Initialized) {
				byte connectionTimeout = ASF.GlobalConfig?.ConnectionTimeout ?? GlobalConfig.DefaultConnectionTimeout;

				for (byte i = 0; (i < connectionTimeout) && !Initialized && Bot.IsConnectedAndLoggedOn; i++) {
					await Task.Delay(1000).ConfigureAwait(false);
				}

				if (!Initialized) {
					throw new HttpRequestException(Strings.WarningFailed);
				}
			}

			steamID = Bot.SteamID;
		} else if (!new SteamID(steamID).IsIndividualAccount) {
			throw new NotSupportedException(string.Format(CultureInfo.CurrentCulture, Strings.ErrorObjectIsNull, nameof(steamID)));
		}

		if (ASF.InventorySemaphore == null) {
			throw new InvalidOperationException(nameof(ASF.InventorySemaphore));
		}

		ulong startAssetID = 0;

		// We need to store asset IDs to make sure we won't get duplicate items
		HashSet<ulong>? assetIDs = null;

		Dictionary<(ulong ClassID, ulong InstanceID), InventoryDescription>? descriptions = null;

		int rateLimitingDelay = (ASF.GlobalConfig?.InventoryLimiterDelay ?? GlobalConfig.DefaultInventoryLimiterDelay) * 1000;

		while (true) {
			Uri request = new(SteamCommunityURL, $"/inventory/{steamID}/{appID}/{contextID}?l=english&count={MaxItemsInSingleInventoryRequest}{(startAssetID > 0 ? $"&start_assetid={startAssetID}" : "")}");

			await ASF.InventorySemaphore.WaitAsync().ConfigureAwait(false);

			ObjectResponse<InventoryResponse>? response = null;

			try {
				for (byte i = 0; (i < WebBrowser.MaxTries) && (response?.Content is not { Result: EResult.OK } || !response.StatusCode.IsSuccessCode()); i++) {
					if ((i > 0) && (rateLimitingDelay > 0)) {
						await Task.Delay(rateLimitingDelay).ConfigureAwait(false);
					}

					response = await UrlGetToJsonObjectWithSession<InventoryResponse>(request, requestOptions: WebBrowser.ERequestOptions.ReturnClientErrors | WebBrowser.ERequestOptions.ReturnServerErrors | WebBrowser.ERequestOptions.AllowInvalidBodyOnErrors, rateLimitingDelay: rateLimitingDelay).ConfigureAwait(false);

					if (response == null) {
						throw new HttpRequestException(string.Format(CultureInfo.CurrentCulture, Strings.ErrorObjectIsNull, nameof(response)));
					}

					if (response.StatusCode.IsClientErrorCode()) {
						throw new HttpRequestException(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, response.StatusCode), null, response.StatusCode);
					}

					if (response.StatusCode.IsServerErrorCode()) {
						if (string.IsNullOrEmpty(response.Content?.ErrorText)) {
							// This is a generic server error without a reason, try again
							continue;
						}

						Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, response.Content.ErrorText));

						// Try to interpret the failure reason and see if we should try again
						switch (response.Content.ErrorCode) {
							case EResult.Busy:
							case EResult.DuplicateRequest:
							case EResult.Fail:
							case EResult.RemoteCallFailed:
							case EResult.ServiceUnavailable:
							case EResult.Timeout:
								// Expected failures that we should be able to retry
								continue;
							case EResult.NoMatch:
								// Expected failures that we're not going to retry
								throw new HttpRequestException(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, response.Content.ErrorText), null, response.StatusCode);
							default:
								// Unknown failures, report them and do not retry since we're unsure if we should
								Bot.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(response.Content.ErrorText), response.Content.ErrorText));

								throw new HttpRequestException(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, response.Content.ErrorText), null, response.StatusCode);
						}
					}
				}
			} finally {
				if (rateLimitingDelay == 0) {
					ASF.InventorySemaphore.Release();
				} else {
					Utilities.InBackground(
						async () => {
							await Task.Delay(rateLimitingDelay).ConfigureAwait(false);
							ASF.InventorySemaphore.Release();
						}
					);
				}
			}

			if (response == null) {
				throw new HttpRequestException(string.Format(CultureInfo.CurrentCulture, Strings.ErrorObjectIsNull, nameof(response)));
			}

			if (response.Content is not { Result: EResult.OK } || !response.StatusCode.IsSuccessCode()) {
				throw new HttpRequestException(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, response.Content?.ErrorText ?? response.Content?.Result?.ToString() ?? response.StatusCode.ToString()));
			}

			if ((response.Content.TotalInventoryCount == 0) || (response.Content.Assets.Count == 0)) {
				// Empty inventory
				yield break;
			}

			if (response.Content.Descriptions.Count == 0) {
				throw new InvalidOperationException(nameof(response.Content.Descriptions));
			}

			if (response.Content.TotalInventoryCount > Array.MaxLength) {
				throw new InvalidOperationException(nameof(response.Content.TotalInventoryCount));
			}

			assetIDs ??= new HashSet<ulong>((int) response.Content.TotalInventoryCount);

			if (descriptions == null) {
				descriptions = new Dictionary<(ulong ClassID, ulong InstanceID), InventoryDescription>();
			} else {
				// We don't need descriptions from the previous request
				descriptions.Clear();
			}

			foreach (InventoryDescription description in response.Content.Descriptions) {
				if (description.ClassID == 0) {
					throw new NotSupportedException(string.Format(CultureInfo.CurrentCulture, Strings.ErrorObjectIsNull, nameof(description.ClassID)));
				}

				(ulong ClassID, ulong InstanceID) key = (description.ClassID, description.InstanceID);

				descriptions.TryAdd(key, description);
			}

			foreach (Asset asset in response.Content.Assets.Where(asset => assetIDs.Add(asset.AssetID))) {
				if (descriptions.TryGetValue((asset.ClassID, asset.InstanceID), out InventoryDescription? description)) {
					asset.Description = description;
				}

				yield return asset;
			}

			if (!response.Content.MoreItems) {
				yield break;
			}

			if (response.Content.LastAssetID == 0) {
				throw new NotSupportedException(string.Format(CultureInfo.CurrentCulture, Strings.ErrorObjectIsNull, nameof(response.Content.LastAssetID)));
			}

			startAssetID = response.Content.LastAssetID;
		}
	}

	[PublicAPI]
	public async Task<uint?> GetPointsBalance() {
		string? accessToken = Bot.AccessToken;

		if (string.IsNullOrEmpty(accessToken)) {
			return null;
		}

		Dictionary<string, object?> arguments = new(2, StringComparer.Ordinal) {
			{ "access_token", accessToken },
			{ "steamid", Bot.SteamID }
		};

		KeyValue? response = null;

		for (byte i = 0; (i < WebBrowser.MaxTries) && (response == null); i++) {
			if ((i > 0) && (WebLimiterDelay > 0)) {
				await Task.Delay(WebLimiterDelay).ConfigureAwait(false);
			}

			using WebAPI.AsyncInterface loyaltyRewardsService = Bot.SteamConfiguration.GetAsyncWebAPIInterface(LoyaltyRewardsService);

			loyaltyRewardsService.Timeout = WebBrowser.Timeout;

			try {
				response = await WebLimitRequest(
					WebAPI.DefaultBaseAddress,

					// ReSharper disable once AccessToDisposedClosure
					async () => await loyaltyRewardsService.CallAsync(HttpMethod.Get, "GetSummary", args: arguments).ConfigureAwait(false)
				).ConfigureAwait(false);
			} catch (TaskCanceledException e) {
				Bot.ArchiLogger.LogGenericDebuggingException(e);
			} catch (Exception e) {
				Bot.ArchiLogger.LogGenericWarningException(e);
			}
		}

		if (response == null) {
			Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));

			return null;
		}

		KeyValue pointsInfo = response["summary"]["points"];

		if (pointsInfo == KeyValue.Invalid) {
			Bot.ArchiLogger.LogNullError(pointsInfo);

			return null;
		}

		uint result = pointsInfo.AsUnsignedInteger(uint.MaxValue);

		if (result == uint.MaxValue) {
			Bot.ArchiLogger.LogNullError(result);

			return null;
		}

		return result;
	}

	[PublicAPI]
	public async Task<HashSet<TradeOffer>?> GetTradeOffers(bool? activeOffers = null, bool? receivedOffers = null, bool? sentOffers = null, bool? withDescriptions = null) {
		if ((receivedOffers == false) && (sentOffers == false)) {
			throw new ArgumentException($"{nameof(receivedOffers)} && {nameof(sentOffers)}");
		}

		string? accessToken = Bot.AccessToken;

		if (string.IsNullOrEmpty(accessToken)) {
			return null;
		}

		Dictionary<string, object> arguments = new(StringComparer.Ordinal) {
			{ "access_token", accessToken }
		};

		if (activeOffers.HasValue) {
			arguments["active_only"] = activeOffers.Value ? "true" : "false";

			// This is ridiculous, active_only without historical cutoff is actually active right now + inactive ones that changed their status since our preview request, what the fuck
			// We're going to make it work as everybody sane expects, by being active ONLY, as the name implies, not active + some shit nobody asked for
			// https://developer.valvesoftware.com/wiki/Steam_Web_API/IEconService#GetTradeOffers_.28v1.29
			if (activeOffers.Value) {
				arguments["time_historical_cutoff"] = uint.MaxValue;
			}
		}

		if (receivedOffers.HasValue) {
			arguments["get_received_offers"] = receivedOffers.Value ? "true" : "false";
		}

		if (sentOffers.HasValue) {
			arguments["get_sent_offers"] = sentOffers.Value ? "true" : "false";
		}

		if (withDescriptions.HasValue) {
			arguments["get_descriptions"] = withDescriptions.Value ? "true" : "false";
		}

		string queryString = string.Join('&', arguments.Select(static argument => $"{argument.Key}={HttpUtility.UrlEncode(argument.Value.ToString())}"));

		Uri request = new(WebAPI.DefaultBaseAddress, $"/{EconService}/GetTradeOffers/v1?{queryString}");

		TradeOffersResponse? response = (await WebLimitRequest(WebAPI.DefaultBaseAddress, async () => await WebBrowser.UrlGetToJsonObject<APIWrappedResponse<TradeOffersResponse>>(request).ConfigureAwait(false)).ConfigureAwait(false))?.Content?.Response;

		if (response == null) {
			Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));

			return null;
		}

		IEnumerable<TradeOffer> trades = [];

		if (receivedOffers.GetValueOrDefault(true)) {
			trades = trades.Concat(response.TradeOffersReceived);
		}

		if (sentOffers.GetValueOrDefault(true)) {
			trades = trades.Concat(response.TradeOffersSent);
		}

		HashSet<TradeOffer> result = trades.Where(tradeOffer => !activeOffers.HasValue || ((!activeOffers.Value || (tradeOffer.State == ETradeOfferState.Active)) && (activeOffers.Value || (tradeOffer.State != ETradeOfferState.Active)))).ToHashSet();

		if ((result.Count == 0) || (response.Descriptions.Count == 0)) {
			return result;
		}

		// Due to a possibility of duplicate descriptions, we can't simply call ToDictionary() here
		Dictionary<(uint AppID, ulong ClassID, ulong InstanceID), InventoryDescription> descriptions = new();

		foreach (InventoryDescription description in response.Descriptions) {
			if (description.AppID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(description.AppID));

				continue;
			}

			if (description.ClassID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(description.ClassID));

				continue;
			}

			(uint AppID, ulong ClassID, ulong InstanceID) key = (description.AppID, description.ClassID, description.InstanceID);

			descriptions.TryAdd(key, description);
		}

		if (descriptions.Count == 0) {
			return result;
		}

		foreach (TradeOffer tradeOffer in result) {
			if (tradeOffer.ItemsToGive.Count > 0) {
				SetDescriptionsToAssets(tradeOffer.ItemsToGive, descriptions);
			}

			if (tradeOffer.ItemsToReceive.Count > 0) {
				SetDescriptionsToAssets(tradeOffer.ItemsToReceive, descriptions);
			}
		}

		return result;
	}

	[PublicAPI]
	public async Task<bool> JoinGroup(ulong groupID) {
		if ((groupID == 0) || !new SteamID(groupID).IsClanAccount) {
			throw new ArgumentOutOfRangeException(nameof(groupID));
		}

		Uri request = new(SteamCommunityURL, $"/gid/{groupID}");

		// Extra entry for sessionID
		Dictionary<string, string> data = new(2, StringComparer.Ordinal) { { "action", "join" } };

		return await UrlPostWithSession(request, data: data, session: ESession.CamelCase).ConfigureAwait(false);
	}

	[PublicAPI]
	public async Task<(bool Success, HashSet<ulong>? TradeOfferIDs, HashSet<ulong>? MobileTradeOfferIDs)> SendTradeOffer(ulong steamID, IReadOnlyCollection<Asset>? itemsToGive = null, IReadOnlyCollection<Asset>? itemsToReceive = null, string? token = null, bool forcedSingleOffer = false, ushort itemsPerTrade = Trading.MaxItemsPerTrade) {
		if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		if (((itemsToGive == null) || (itemsToGive.Count == 0)) && ((itemsToReceive == null) || (itemsToReceive.Count == 0))) {
			throw new ArgumentException($"{nameof(itemsToGive)} && {nameof(itemsToReceive)}");
		}

		ArgumentOutOfRangeException.ThrowIfZero(itemsPerTrade);

		TradeOfferSendRequest singleTrade = new();
		HashSet<TradeOfferSendRequest> trades = [singleTrade];

		if (itemsToGive != null) {
			foreach (Asset itemToGive in itemsToGive) {
				if (!forcedSingleOffer && (singleTrade.ItemsToGive.Assets.Count + singleTrade.ItemsToReceive.Assets.Count >= itemsPerTrade)) {
					if (trades.Count >= Trading.MaxTradesPerAccount) {
						break;
					}

					singleTrade = new TradeOfferSendRequest();
					trades.Add(singleTrade);
				}

				singleTrade.ItemsToGive.Assets.Add(itemToGive);
			}
		}

		if (itemsToReceive != null) {
			foreach (Asset itemToReceive in itemsToReceive) {
				if (!forcedSingleOffer && (singleTrade.ItemsToGive.Assets.Count + singleTrade.ItemsToReceive.Assets.Count >= itemsPerTrade)) {
					if (trades.Count >= Trading.MaxTradesPerAccount) {
						break;
					}

					singleTrade = new TradeOfferSendRequest();
					trades.Add(singleTrade);
				}

				singleTrade.ItemsToReceive.Assets.Add(itemToReceive);
			}
		}

		Uri request = new(SteamCommunityURL, "/tradeoffer/new/send");
		Uri referer = new(SteamCommunityURL, "/tradeoffer/new");

		// Extra entry for sessionID
		Dictionary<string, string> data = new(6, StringComparer.Ordinal) {
			{ "partner", steamID.ToString(CultureInfo.InvariantCulture) },
			{ "serverid", "1" },
			{ "trade_offer_create_params", !string.IsNullOrEmpty(token) ? new JsonObject { { "trade_offer_access_token", token } }.ToJsonText() : "" },
			{ "tradeoffermessage", $"Sent by {SharedInfo.PublicIdentifier}/{SharedInfo.Version}" }
		};

		HashSet<ulong> tradeOfferIDs = new(trades.Count);
		HashSet<ulong> mobileTradeOfferIDs = new(trades.Count);

		foreach (TradeOfferSendRequest trade in trades) {
			data["json_tradeoffer"] = trade.ToJsonText();

			ObjectResponse<TradeOfferSendResponse>? response = null;

			for (byte i = 0; (i < WebBrowser.MaxTries) && (response == null); i++) {
				response = await UrlPostToJsonObjectWithSession<TradeOfferSendResponse>(request, data: data, referer: referer, requestOptions: WebBrowser.ERequestOptions.ReturnServerErrors | WebBrowser.ERequestOptions.AllowInvalidBodyOnErrors).ConfigureAwait(false);

				if (response == null) {
					return (false, tradeOfferIDs, mobileTradeOfferIDs);
				}

				if (response.StatusCode.IsServerErrorCode()) {
					if (string.IsNullOrEmpty(response.Content?.ErrorText)) {
						// This is a generic server error without a reason, try again
						response = null;

						continue;
					}

					// This is actually client error with a reason, so it doesn't make sense to retry
					Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, response.Content.ErrorText));

					return (false, tradeOfferIDs, mobileTradeOfferIDs);
				}
			}

			if (response?.Content == null) {
				return (false, tradeOfferIDs, mobileTradeOfferIDs);
			}

			if (response.Content.TradeOfferID == 0) {
				Bot.ArchiLogger.LogNullError(response.Content.TradeOfferID);

				return (false, tradeOfferIDs, mobileTradeOfferIDs);
			}

			tradeOfferIDs.Add(response.Content.TradeOfferID);

			if (response.Content.RequiresMobileConfirmation) {
				mobileTradeOfferIDs.Add(response.Content.TradeOfferID);
			}
		}

		return (true, tradeOfferIDs, mobileTradeOfferIDs);
	}

	[PublicAPI]
	public async Task<HtmlDocumentResponse?> UrlGetToHtmlDocumentWithSession(Uri request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, Uri? referer = null, WebBrowser.ERequestOptions requestOptions = WebBrowser.ERequestOptions.None, bool checkSessionPreemptively = true, byte maxTries = WebBrowser.MaxTries, int rateLimitingDelay = 0, bool allowSessionRefresh = true, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(request);
		ArgumentOutOfRangeException.ThrowIfZero(maxTries);
		ArgumentOutOfRangeException.ThrowIfNegative(rateLimitingDelay);

		if (WebLimiterDelay > rateLimitingDelay) {
			rateLimitingDelay = WebLimiterDelay;
		}

		if (checkSessionPreemptively) {
			// Check session preemptively as this request might not get redirected to expiration
			bool? sessionExpired = await IsSessionExpired().ConfigureAwait(false);

			if (sessionExpired.GetValueOrDefault(true)) {
				if (allowSessionRefresh && await RefreshSession().ConfigureAwait(false)) {
					return await UrlGetToHtmlDocumentWithSession(request, headers, referer, requestOptions, true, maxTries, rateLimitingDelay, false, cancellationToken).ConfigureAwait(false);
				}

				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

				return null;
			}
		} else {
			// If session refresh is already in progress, just wait for it
			await SessionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
			SessionSemaphore.Release();
		}

		if (!Initialized) {
			byte connectionTimeout = ASF.GlobalConfig?.ConnectionTimeout ?? GlobalConfig.DefaultConnectionTimeout;

			for (byte i = 0; (i < connectionTimeout) && !Initialized && Bot.IsConnectedAndLoggedOn; i++) {
				await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
			}

			if (!Initialized) {
				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

				return null;
			}
		}

		Uri host = new(request.GetLeftPart(UriPartial.Authority));

		// ReSharper disable once AccessToModifiedClosure - evaluated fully before returning
		HtmlDocumentResponse? response = await WebLimitRequest(host, async () => await WebBrowser.UrlGetToHtmlDocument(request, headers, referer, requestOptions, maxTries, rateLimitingDelay, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

		if (response == null) {
			return null;
		}

		if (IsSessionExpiredUri(response.FinalUri)) {
			if (allowSessionRefresh && await RefreshSession().ConfigureAwait(false)) {
				return await UrlGetToHtmlDocumentWithSession(request, headers, referer, requestOptions, checkSessionPreemptively, maxTries, rateLimitingDelay, false, cancellationToken).ConfigureAwait(false);
			}

			Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
			Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

			return null;
		}

		// Under special brain-damaged circumstances, Steam might just return our own profile as a response to the request, for absolutely no reason whatsoever - just try again in this case
		if (!requestOptions.HasFlag(WebBrowser.ERequestOptions.ReturnRedirections) && await IsProfileUri(response.FinalUri).ConfigureAwait(false) && !await IsProfileUri(request).ConfigureAwait(false)) {
			Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.WarningWorkaroundTriggered, nameof(IsProfileUri)));

			if (--maxTries == 0) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

				return null;
			}

			return await UrlGetToHtmlDocumentWithSession(request, headers, referer, requestOptions, checkSessionPreemptively, maxTries, rateLimitingDelay, allowSessionRefresh, cancellationToken).ConfigureAwait(false);
		}

		return response;
	}

	[PublicAPI]
	public async Task<ObjectResponse<T>?> UrlGetToJsonObjectWithSession<T>(Uri request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, Uri? referer = null, WebBrowser.ERequestOptions requestOptions = WebBrowser.ERequestOptions.None, bool checkSessionPreemptively = true, byte maxTries = WebBrowser.MaxTries, int rateLimitingDelay = 0, bool allowSessionRefresh = true, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(request);
		ArgumentOutOfRangeException.ThrowIfZero(maxTries);
		ArgumentOutOfRangeException.ThrowIfNegative(rateLimitingDelay);

		if (WebLimiterDelay > rateLimitingDelay) {
			rateLimitingDelay = WebLimiterDelay;
		}

		if (checkSessionPreemptively) {
			// Check session preemptively as this request might not get redirected to expiration
			bool? sessionExpired = await IsSessionExpired().ConfigureAwait(false);

			if (sessionExpired.GetValueOrDefault(true)) {
				if (allowSessionRefresh && await RefreshSession().ConfigureAwait(false)) {
					return await UrlGetToJsonObjectWithSession<T>(request, headers, referer, requestOptions, true, maxTries, rateLimitingDelay, false, cancellationToken).ConfigureAwait(false);
				}

				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

				return null;
			}
		} else {
			// If session refresh is already in progress, just wait for it
			await SessionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
			SessionSemaphore.Release();
		}

		if (!Initialized) {
			byte connectionTimeout = ASF.GlobalConfig?.ConnectionTimeout ?? GlobalConfig.DefaultConnectionTimeout;

			for (byte i = 0; (i < connectionTimeout) && !Initialized && Bot.IsConnectedAndLoggedOn; i++) {
				await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
			}

			if (!Initialized) {
				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

				return default(ObjectResponse<T>?);
			}
		}

		Uri host = new(request.GetLeftPart(UriPartial.Authority));

		// ReSharper disable once AccessToModifiedClosure - evaluated fully before returning
		ObjectResponse<T>? response = await WebLimitRequest(host, async () => await WebBrowser.UrlGetToJsonObject<T>(request, headers, referer, requestOptions, maxTries, rateLimitingDelay, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

		if (response == null) {
			return default(ObjectResponse<T>?);
		}

		if (IsSessionExpiredUri(response.FinalUri)) {
			if (allowSessionRefresh && await RefreshSession().ConfigureAwait(false)) {
				return await UrlGetToJsonObjectWithSession<T>(request, headers, referer, requestOptions, checkSessionPreemptively, maxTries, rateLimitingDelay, false, cancellationToken).ConfigureAwait(false);
			}

			Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
			Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

			return null;
		}

		// Under special brain-damaged circumstances, Steam might just return our own profile as a response to the request, for absolutely no reason whatsoever - just try again in this case
		if (!requestOptions.HasFlag(WebBrowser.ERequestOptions.ReturnRedirections) && await IsProfileUri(response.FinalUri).ConfigureAwait(false) && !await IsProfileUri(request).ConfigureAwait(false)) {
			Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.WarningWorkaroundTriggered, nameof(IsProfileUri)));

			if (--maxTries == 0) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

				return null;
			}

			return await UrlGetToJsonObjectWithSession<T>(request, headers, referer, requestOptions, checkSessionPreemptively, maxTries, rateLimitingDelay, allowSessionRefresh, cancellationToken).ConfigureAwait(false);
		}

		return response;
	}

	[PublicAPI]
	public async Task<bool> UrlHeadWithSession(Uri request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, Uri? referer = null, WebBrowser.ERequestOptions requestOptions = WebBrowser.ERequestOptions.None, bool checkSessionPreemptively = true, byte maxTries = WebBrowser.MaxTries, int rateLimitingDelay = 0, bool allowSessionRefresh = true, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(request);
		ArgumentOutOfRangeException.ThrowIfZero(maxTries);
		ArgumentOutOfRangeException.ThrowIfNegative(rateLimitingDelay);

		if (WebLimiterDelay > rateLimitingDelay) {
			rateLimitingDelay = WebLimiterDelay;
		}

		if (checkSessionPreemptively) {
			// Check session preemptively as this request might not get redirected to expiration
			bool? sessionExpired = await IsSessionExpired().ConfigureAwait(false);

			if (sessionExpired.GetValueOrDefault(true)) {
				if (allowSessionRefresh && await RefreshSession().ConfigureAwait(false)) {
					return await UrlHeadWithSession(request, headers, referer, requestOptions, true, maxTries, rateLimitingDelay, false, cancellationToken).ConfigureAwait(false);
				}

				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

				return false;
			}
		} else {
			// If session refresh is already in progress, just wait for it
			await SessionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
			SessionSemaphore.Release();
		}

		if (!Initialized) {
			byte connectionTimeout = ASF.GlobalConfig?.ConnectionTimeout ?? GlobalConfig.DefaultConnectionTimeout;

			for (byte i = 0; (i < connectionTimeout) && !Initialized && Bot.IsConnectedAndLoggedOn; i++) {
				await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
			}

			if (!Initialized) {
				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

				return false;
			}
		}

		Uri host = new(request.GetLeftPart(UriPartial.Authority));

		// ReSharper disable once AccessToModifiedClosure - evaluated fully before returning
		BasicResponse? response = await WebLimitRequest(host, async () => await WebBrowser.UrlHead(request, headers, referer, requestOptions, maxTries, rateLimitingDelay, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

		if (response == null) {
			return false;
		}

		if (IsSessionExpiredUri(response.FinalUri)) {
			if (allowSessionRefresh && await RefreshSession().ConfigureAwait(false)) {
				return await UrlHeadWithSession(request, headers, referer, requestOptions, checkSessionPreemptively, maxTries, rateLimitingDelay, false, cancellationToken).ConfigureAwait(false);
			}

			Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
			Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

			return false;
		}

		// Under special brain-damaged circumstances, Steam might just return our own profile as a response to the request, for absolutely no reason whatsoever - just try again in this case
		if (!requestOptions.HasFlag(WebBrowser.ERequestOptions.ReturnRedirections) && await IsProfileUri(response.FinalUri).ConfigureAwait(false) && !await IsProfileUri(request).ConfigureAwait(false)) {
			Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.WarningWorkaroundTriggered, nameof(IsProfileUri)));

			if (--maxTries == 0) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

				return false;
			}

			return await UrlHeadWithSession(request, headers, referer, requestOptions, checkSessionPreemptively, maxTries, rateLimitingDelay, allowSessionRefresh, cancellationToken).ConfigureAwait(false);
		}

		return true;
	}

	[PublicAPI]
	public async Task<HtmlDocumentResponse?> UrlPostToHtmlDocumentWithSession(Uri request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, IDictionary<string, string>? data = null, Uri? referer = null, WebBrowser.ERequestOptions requestOptions = WebBrowser.ERequestOptions.None, ESession session = ESession.Lowercase, bool checkSessionPreemptively = true, byte maxTries = WebBrowser.MaxTries, int rateLimitingDelay = 0, bool allowSessionRefresh = true, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(request);

		if (!Enum.IsDefined(session)) {
			throw new InvalidEnumArgumentException(nameof(session), (int) session, typeof(ESession));
		}

		ArgumentOutOfRangeException.ThrowIfZero(maxTries);
		ArgumentOutOfRangeException.ThrowIfNegative(rateLimitingDelay);

		if (WebLimiterDelay > rateLimitingDelay) {
			rateLimitingDelay = WebLimiterDelay;
		}

		if (checkSessionPreemptively) {
			// Check session preemptively as this request might not get redirected to expiration
			bool? sessionExpired = await IsSessionExpired().ConfigureAwait(false);

			if (sessionExpired.GetValueOrDefault(true)) {
				if (allowSessionRefresh && await RefreshSession().ConfigureAwait(false)) {
					return await UrlPostToHtmlDocumentWithSession(request, headers, data, referer, requestOptions, session, true, maxTries, rateLimitingDelay, false, cancellationToken).ConfigureAwait(false);
				}

				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

				return null;
			}
		} else {
			// If session refresh is already in progress, just wait for it
			await SessionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
			SessionSemaphore.Release();
		}

		if (!Initialized) {
			byte connectionTimeout = ASF.GlobalConfig?.ConnectionTimeout ?? GlobalConfig.DefaultConnectionTimeout;

			for (byte i = 0; (i < connectionTimeout) && !Initialized && Bot.IsConnectedAndLoggedOn; i++) {
				await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
			}

			if (!Initialized) {
				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

				return null;
			}
		}

		Uri host = new(request.GetLeftPart(UriPartial.Authority));

		if (session != ESession.None) {
			string? sessionID = WebBrowser.CookieContainer.GetCookieValue(host, "sessionid");

			if (string.IsNullOrEmpty(sessionID)) {
				Bot.ArchiLogger.LogNullError(sessionID);

				return null;
			}

			string sessionName = session switch {
				ESession.CamelCase => "sessionID",
				ESession.Lowercase => "sessionid",
				ESession.PascalCase => "SessionID",
				_ => throw new InvalidOperationException(nameof(session))
			};

			if (data != null) {
				data[sessionName] = sessionID;
			} else {
				data = new Dictionary<string, string>(1, StringComparer.Ordinal) { { sessionName, sessionID } };
			}
		}

		// ReSharper disable once AccessToModifiedClosure - evaluated fully before returning
		HtmlDocumentResponse? response = await WebLimitRequest(host, async () => await WebBrowser.UrlPostToHtmlDocument(request, headers, data, referer, requestOptions, maxTries, rateLimitingDelay, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

		if (response == null) {
			return null;
		}

		if (IsSessionExpiredUri(response.FinalUri)) {
			if (allowSessionRefresh && await RefreshSession().ConfigureAwait(false)) {
				return await UrlPostToHtmlDocumentWithSession(request, headers, data, referer, requestOptions, session, checkSessionPreemptively, maxTries, rateLimitingDelay, false, cancellationToken).ConfigureAwait(false);
			}

			Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
			Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

			return null;
		}

		// Under special brain-damaged circumstances, Steam might just return our own profile as a response to the request, for absolutely no reason whatsoever - just try again in this case
		if (!requestOptions.HasFlag(WebBrowser.ERequestOptions.ReturnRedirections) && await IsProfileUri(response.FinalUri).ConfigureAwait(false) && !await IsProfileUri(request).ConfigureAwait(false)) {
			Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.WarningWorkaroundTriggered, nameof(IsProfileUri)));

			if (--maxTries == 0) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

				return null;
			}

			return await UrlPostToHtmlDocumentWithSession(request, headers, data, referer, requestOptions, session, checkSessionPreemptively, maxTries, rateLimitingDelay, allowSessionRefresh, cancellationToken).ConfigureAwait(false);
		}

		return response;
	}

	[PublicAPI]
	public async Task<ObjectResponse<T>?> UrlPostToJsonObjectWithSession<T>(Uri request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, IDictionary<string, string>? data = null, Uri? referer = null, WebBrowser.ERequestOptions requestOptions = WebBrowser.ERequestOptions.None, ESession session = ESession.Lowercase, bool checkSessionPreemptively = true, byte maxTries = WebBrowser.MaxTries, int rateLimitingDelay = 0, bool allowSessionRefresh = true, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(request);

		if (!Enum.IsDefined(session)) {
			throw new InvalidEnumArgumentException(nameof(session), (int) session, typeof(ESession));
		}

		ArgumentOutOfRangeException.ThrowIfZero(maxTries);
		ArgumentOutOfRangeException.ThrowIfNegative(rateLimitingDelay);

		if (WebLimiterDelay > rateLimitingDelay) {
			rateLimitingDelay = WebLimiterDelay;
		}

		if (checkSessionPreemptively) {
			// Check session preemptively as this request might not get redirected to expiration
			bool? sessionExpired = await IsSessionExpired().ConfigureAwait(false);

			if (sessionExpired.GetValueOrDefault(true)) {
				if (allowSessionRefresh && await RefreshSession().ConfigureAwait(false)) {
					return await UrlPostToJsonObjectWithSession<T>(request, headers, data, referer, requestOptions, session, true, maxTries, rateLimitingDelay, false, cancellationToken).ConfigureAwait(false);
				}

				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

				return null;
			}
		} else {
			// If session refresh is already in progress, just wait for it
			await SessionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
			SessionSemaphore.Release();
		}

		if (!Initialized) {
			byte connectionTimeout = ASF.GlobalConfig?.ConnectionTimeout ?? GlobalConfig.DefaultConnectionTimeout;

			for (byte i = 0; (i < connectionTimeout) && !Initialized && Bot.IsConnectedAndLoggedOn; i++) {
				await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
			}

			if (!Initialized) {
				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

				return null;
			}
		}

		Uri host = new(request.GetLeftPart(UriPartial.Authority));

		if (session != ESession.None) {
			string? sessionID = WebBrowser.CookieContainer.GetCookieValue(host, "sessionid");

			if (string.IsNullOrEmpty(sessionID)) {
				Bot.ArchiLogger.LogNullError(sessionID);

				return null;
			}

			string sessionName = session switch {
				ESession.CamelCase => "sessionID",
				ESession.Lowercase => "sessionid",
				ESession.PascalCase => "SessionID",
				_ => throw new InvalidOperationException(nameof(session))
			};

			if (data != null) {
				data[sessionName] = sessionID;
			} else {
				data = new Dictionary<string, string>(1, StringComparer.Ordinal) { { sessionName, sessionID } };
			}
		}

		// ReSharper disable once AccessToModifiedClosure - evaluated fully before returning
		ObjectResponse<T>? response = await WebLimitRequest(host, async () => await WebBrowser.UrlPostToJsonObject<T, IDictionary<string, string>>(request, headers, data, referer, requestOptions, maxTries, rateLimitingDelay, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

		if (response == null) {
			return null;
		}

		if (IsSessionExpiredUri(response.FinalUri)) {
			if (allowSessionRefresh && await RefreshSession().ConfigureAwait(false)) {
				return await UrlPostToJsonObjectWithSession<T>(request, headers, data, referer, requestOptions, session, checkSessionPreemptively, maxTries, rateLimitingDelay, false, cancellationToken).ConfigureAwait(false);
			}

			Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
			Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

			return null;
		}

		// Under special brain-damaged circumstances, Steam might just return our own profile as a response to the request, for absolutely no reason whatsoever - just try again in this case
		if (!requestOptions.HasFlag(WebBrowser.ERequestOptions.ReturnRedirections) && await IsProfileUri(response.FinalUri).ConfigureAwait(false) && !await IsProfileUri(request).ConfigureAwait(false)) {
			Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.WarningWorkaroundTriggered, nameof(IsProfileUri)));

			if (--maxTries == 0) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

				return null;
			}

			return await UrlPostToJsonObjectWithSession<T>(request, headers, data, referer, requestOptions, session, checkSessionPreemptively, maxTries, rateLimitingDelay, allowSessionRefresh, cancellationToken).ConfigureAwait(false);
		}

		return response;
	}

	[PublicAPI]
	public async Task<ObjectResponse<T>?> UrlPostToJsonObjectWithSession<T>(Uri request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, ICollection<KeyValuePair<string, string>>? data = null, Uri? referer = null, WebBrowser.ERequestOptions requestOptions = WebBrowser.ERequestOptions.None, ESession session = ESession.Lowercase, bool checkSessionPreemptively = true, byte maxTries = WebBrowser.MaxTries, int rateLimitingDelay = 0, bool allowSessionRefresh = true, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(request);

		if (!Enum.IsDefined(session)) {
			throw new InvalidEnumArgumentException(nameof(session), (int) session, typeof(ESession));
		}

		ArgumentOutOfRangeException.ThrowIfZero(maxTries);
		ArgumentOutOfRangeException.ThrowIfNegative(rateLimitingDelay);

		if (WebLimiterDelay > rateLimitingDelay) {
			rateLimitingDelay = WebLimiterDelay;
		}

		if (checkSessionPreemptively) {
			// Check session preemptively as this request might not get redirected to expiration
			bool? sessionExpired = await IsSessionExpired().ConfigureAwait(false);

			if (sessionExpired.GetValueOrDefault(true)) {
				if (allowSessionRefresh && await RefreshSession().ConfigureAwait(false)) {
					return await UrlPostToJsonObjectWithSession<T>(request, headers, data, referer, requestOptions, session, true, maxTries, rateLimitingDelay, false, cancellationToken).ConfigureAwait(false);
				}

				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

				return null;
			}
		} else {
			// If session refresh is already in progress, just wait for it
			await SessionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
			SessionSemaphore.Release();
		}

		if (!Initialized) {
			byte connectionTimeout = ASF.GlobalConfig?.ConnectionTimeout ?? GlobalConfig.DefaultConnectionTimeout;

			for (byte i = 0; (i < connectionTimeout) && !Initialized && Bot.IsConnectedAndLoggedOn; i++) {
				await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
			}

			if (!Initialized) {
				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

				return null;
			}
		}

		Uri host = new(request.GetLeftPart(UriPartial.Authority));

		if (session != ESession.None) {
			string? sessionID = WebBrowser.CookieContainer.GetCookieValue(host, "sessionid");

			if (string.IsNullOrEmpty(sessionID)) {
				Bot.ArchiLogger.LogNullError(sessionID);

				return null;
			}

			string sessionName = session switch {
				ESession.CamelCase => "sessionID",
				ESession.Lowercase => "sessionid",
				ESession.PascalCase => "SessionID",
				_ => throw new InvalidOperationException(nameof(session))
			};

			KeyValuePair<string, string> sessionValue = new(sessionName, sessionID);

			if (data != null) {
				data.Remove(sessionValue);
				data.Add(sessionValue);
			} else {
				data = new List<KeyValuePair<string, string>>(1) { sessionValue };
			}
		}

		// ReSharper disable once AccessToModifiedClosure - evaluated fully before returning
		ObjectResponse<T>? response = await WebLimitRequest(host, async () => await WebBrowser.UrlPostToJsonObject<T, ICollection<KeyValuePair<string, string>>>(request, headers, data, referer, requestOptions, maxTries, rateLimitingDelay, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

		if (response == null) {
			return null;
		}

		if (IsSessionExpiredUri(response.FinalUri)) {
			if (allowSessionRefresh && await RefreshSession().ConfigureAwait(false)) {
				return await UrlPostToJsonObjectWithSession<T>(request, headers, data, referer, requestOptions, session, checkSessionPreemptively, maxTries, rateLimitingDelay, false, cancellationToken).ConfigureAwait(false);
			}

			Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
			Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

			return null;
		}

		// Under special brain-damaged circumstances, Steam might just return our own profile as a response to the request, for absolutely no reason whatsoever - just try again in this case
		if (!requestOptions.HasFlag(WebBrowser.ERequestOptions.ReturnRedirections) && await IsProfileUri(response.FinalUri).ConfigureAwait(false) && !await IsProfileUri(request).ConfigureAwait(false)) {
			Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.WarningWorkaroundTriggered, nameof(IsProfileUri)));

			if (--maxTries == 0) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

				return null;
			}

			return await UrlPostToJsonObjectWithSession<T>(request, headers, data, referer, requestOptions, session, checkSessionPreemptively, maxTries, rateLimitingDelay, allowSessionRefresh, cancellationToken).ConfigureAwait(false);
		}

		return response;
	}

	[PublicAPI]
	public async Task<bool> UrlPostWithSession(Uri request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, IDictionary<string, string>? data = null, Uri? referer = null, WebBrowser.ERequestOptions requestOptions = WebBrowser.ERequestOptions.None, ESession session = ESession.Lowercase, bool checkSessionPreemptively = true, byte maxTries = WebBrowser.MaxTries, int rateLimitingDelay = 0, bool allowSessionRefresh = true, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(request);

		if (!Enum.IsDefined(session)) {
			throw new InvalidEnumArgumentException(nameof(session), (int) session, typeof(ESession));
		}

		ArgumentOutOfRangeException.ThrowIfZero(maxTries);
		ArgumentOutOfRangeException.ThrowIfNegative(rateLimitingDelay);

		if (WebLimiterDelay > rateLimitingDelay) {
			rateLimitingDelay = WebLimiterDelay;
		}

		if (checkSessionPreemptively) {
			// Check session preemptively as this request might not get redirected to expiration
			bool? sessionExpired = await IsSessionExpired().ConfigureAwait(false);

			if (sessionExpired.GetValueOrDefault(true)) {
				if (allowSessionRefresh && await RefreshSession().ConfigureAwait(false)) {
					return await UrlPostWithSession(request, headers, data, referer, requestOptions, session, true, maxTries, rateLimitingDelay, false, cancellationToken).ConfigureAwait(false);
				}

				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

				return false;
			}
		} else {
			// If session refresh is already in progress, just wait for it
			await SessionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
			SessionSemaphore.Release();
		}

		if (!Initialized) {
			byte connectionTimeout = ASF.GlobalConfig?.ConnectionTimeout ?? GlobalConfig.DefaultConnectionTimeout;

			for (byte i = 0; (i < connectionTimeout) && !Initialized && Bot.IsConnectedAndLoggedOn; i++) {
				await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
			}

			if (!Initialized) {
				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

				return false;
			}
		}

		Uri host = new(request.GetLeftPart(UriPartial.Authority));

		if (session != ESession.None) {
			string? sessionID = WebBrowser.CookieContainer.GetCookieValue(host, "sessionid");

			if (string.IsNullOrEmpty(sessionID)) {
				Bot.ArchiLogger.LogNullError(sessionID);

				return false;
			}

			string sessionName = session switch {
				ESession.CamelCase => "sessionID",
				ESession.Lowercase => "sessionid",
				ESession.PascalCase => "SessionID",
				_ => throw new InvalidOperationException(nameof(session))
			};

			if (data != null) {
				data[sessionName] = sessionID;
			} else {
				data = new Dictionary<string, string>(1, StringComparer.Ordinal) { { sessionName, sessionID } };
			}
		}

		// ReSharper disable once AccessToModifiedClosure - evaluated fully before returning
		BasicResponse? response = await WebLimitRequest(host, async () => await WebBrowser.UrlPost(request, headers, data, referer, requestOptions, maxTries, rateLimitingDelay, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

		if (response == null) {
			return false;
		}

		if (IsSessionExpiredUri(response.FinalUri)) {
			if (allowSessionRefresh && await RefreshSession().ConfigureAwait(false)) {
				return await UrlPostWithSession(request, headers, data, referer, requestOptions, session, checkSessionPreemptively, maxTries, rateLimitingDelay, false, cancellationToken).ConfigureAwait(false);
			}

			Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
			Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

			return false;
		}

		// Under special brain-damaged circumstances, Steam might just return our own profile as a response to the request, for absolutely no reason whatsoever - just try again in this case
		if (!requestOptions.HasFlag(WebBrowser.ERequestOptions.ReturnRedirections) && await IsProfileUri(response.FinalUri).ConfigureAwait(false) && !await IsProfileUri(request).ConfigureAwait(false)) {
			Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.WarningWorkaroundTriggered, nameof(IsProfileUri)));

			if (--maxTries == 0) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

				return false;
			}

			return await UrlPostWithSession(request, headers, data, referer, requestOptions, session, checkSessionPreemptively, maxTries, rateLimitingDelay, allowSessionRefresh, cancellationToken).ConfigureAwait(false);
		}

		return true;
	}

	[PublicAPI]
	public static async Task<T> WebLimitRequest<T>(Uri service, Func<Task<T>> function, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(service);
		ArgumentNullException.ThrowIfNull(function);

		if (ASF.RateLimitingSemaphore == null) {
			throw new InvalidOperationException(nameof(ASF.RateLimitingSemaphore));
		}

		if (ASF.WebLimitingSemaphores == null) {
			throw new InvalidOperationException(nameof(ASF.WebLimitingSemaphores));
		}

		if (WebLimiterDelay == 0) {
			return await function().ConfigureAwait(false);
		}

		if (!ASF.WebLimitingSemaphores.TryGetValue(service, out (ICrossProcessSemaphore RateLimitingSemaphore, SemaphoreSlim OpenConnectionsSemaphore) limiters)) {
			ASF.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(service), service));

			limiters.RateLimitingSemaphore = ASF.RateLimitingSemaphore;
			limiters.OpenConnectionsSemaphore = ASF.OpenConnectionsSemaphore;
		}

		// Sending a request opens a new connection
		await limiters.OpenConnectionsSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

		try {
			// It also increases number of requests
			await limiters.RateLimitingSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

			// We release rate-limiter semaphore regardless of our task completion, since we use that one only to guarantee rate-limiting of their creation
			Utilities.InBackground(
				async () => {
					// ReSharper disable once MethodSupportsCancellation - we must always wait given time before releasing semaphore
					await Task.Delay(WebLimiterDelay).ConfigureAwait(false);
					limiters.RateLimitingSemaphore.Release();
				}
			);

			return await function().ConfigureAwait(false);
		} finally {
			// We release open connections semaphore only once we're indeed done sending a particular request
			limiters.OpenConnectionsSemaphore.Release();
		}
	}

	internal async Task<bool> AcceptDigitalGiftCard(ulong giftCardID) {
		ArgumentOutOfRangeException.ThrowIfZero(giftCardID);

		Uri request = new(SteamStoreURL, "/gifts/0/resolvegiftcard");

		// Extra entry for sessionID
		Dictionary<string, string> data = new(3, StringComparer.Ordinal) {
			{ "accept", "1" },
			{ "giftcardid", giftCardID.ToString(CultureInfo.InvariantCulture) }
		};

		ObjectResponse<ResultResponse>? response = await UrlPostToJsonObjectWithSession<ResultResponse>(request, data: data).ConfigureAwait(false);

		if (response?.Content == null) {
			return false;
		}

		if (response.Content.Result != EResult.OK) {
			Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);

			return false;
		}

		return true;
	}

	internal async Task<(bool Success, bool RequiresMobileConfirmation)> AcceptTradeOffer(ulong tradeID) {
		ArgumentOutOfRangeException.ThrowIfZero(tradeID);

		Uri request = new(SteamCommunityURL, $"/tradeoffer/{tradeID}/accept");
		Uri referer = new(SteamCommunityURL, $"/tradeoffer/{tradeID}");

		// Extra entry for sessionID
		Dictionary<string, string> data = new(3, StringComparer.Ordinal) {
			{ "serverid", "1" },
			{ "tradeofferid", tradeID.ToString(CultureInfo.InvariantCulture) }
		};

		ObjectResponse<TradeOfferAcceptResponse>? response = null;

		for (byte i = 0; (i < WebBrowser.MaxTries) && (response == null); i++) {
			response = await UrlPostToJsonObjectWithSession<TradeOfferAcceptResponse>(request, data: data, referer: referer, requestOptions: WebBrowser.ERequestOptions.ReturnServerErrors | WebBrowser.ERequestOptions.AllowInvalidBodyOnErrors).ConfigureAwait(false);

			if (response == null) {
				return (false, false);
			}

			if (response.StatusCode.IsServerErrorCode()) {
				if (string.IsNullOrEmpty(response.Content?.ErrorText)) {
					// This is a generic server error without a reason, try again
					response = null;

					continue;
				}

				// This is actually client error with a reason, so it doesn't make sense to retry
				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, response.Content.ErrorText));

				return (false, false);
			}
		}

		return response?.Content != null ? (true, response.Content.RequiresMobileConfirmation) : (false, false);
	}

	internal async Task<(EResult Result, EPurchaseResultDetail PurchaseResult)> AddFreeLicense(uint subID) {
		ArgumentOutOfRangeException.ThrowIfZero(subID);

		Uri request = new(SteamStoreURL, $"/freelicense/addfreelicense/{subID}");

		// Extra entry for sessionID
		Dictionary<string, string> data = new(2, StringComparer.Ordinal) {
			{ "ajax", "true" }
		};

		ObjectResponse<JsonNode>? response = await UrlPostToJsonObjectWithSession<JsonNode>(request, data: data, requestOptions: WebBrowser.ERequestOptions.ReturnClientErrors | WebBrowser.ERequestOptions.ReturnServerErrors | WebBrowser.ERequestOptions.AllowInvalidBodyOnErrors).ConfigureAwait(false);

		if (response == null) {
			return (EResult.Fail, EPurchaseResultDetail.Timeout);
		}

		switch (response.StatusCode) {
			case HttpStatusCode.Forbidden:
				// Let's convert this into something reasonable
				return (EResult.AccessDenied, EPurchaseResultDetail.InvalidPackage);
			case HttpStatusCode.InternalServerError:
			case HttpStatusCode.OK:
				// This API is total nuts, it returns sometimes [ ], sometimes { "purchaseresultdetail": int }, sometimes { "error": "stuff" } and sometimes null because f**k you, that's why, I wouldn't be surprised if it returned XML one day
				// There is not much we can do apart from trying to extract the result and returning it along with the OK and non-OK response, it's also why it doesn't make any sense to strong-type it
				EResult result = response.StatusCode.IsSuccessCode() ? EResult.OK : EResult.Fail;

				if (response.Content is not JsonObject jsonObject) {
					// Who knows what piece of crap that is?
					return (result, EPurchaseResultDetail.NoDetail);
				}

				try {
					byte? numberResult = jsonObject["purchaseresultdetail"]?.GetValue<byte>();

					if (numberResult.HasValue) {
						return (result, (EPurchaseResultDetail) numberResult.Value);
					}

					// Attempt to do limited parsing from error message, if it exists that is
					string? errorMessage = jsonObject["error"]?.GetValue<string>();

					switch (errorMessage) {
						case null:
						case "":
							// Thanks Steam, very useful
							return (result, EPurchaseResultDetail.NoDetail);
						case "You got rate limited, try again in an hour.":
							return (result, EPurchaseResultDetail.RateLimited);
						default:
							Bot.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(errorMessage), errorMessage));

							return (result, EPurchaseResultDetail.ContactSupport);
					}
				} catch (Exception e) {
					Bot.ArchiLogger.LogGenericException(e);

					return (result, EPurchaseResultDetail.ContactSupport);
				}
			case HttpStatusCode.Unauthorized:
				// Let's convert this into something reasonable
				return (EResult.AccessDenied, EPurchaseResultDetail.NoDetail);
			default:
				// We should handle all expected status codes above, this is a generic fallback for those that we don't
				Bot.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(response.StatusCode), response.StatusCode));

				return (response.StatusCode.IsSuccessCode() ? EResult.OK : EResult.Fail, EPurchaseResultDetail.ContactSupport);
		}
	}

	internal async Task<bool> ChangePrivacySettings(UserPrivacy userPrivacy) {
		ArgumentNullException.ThrowIfNull(userPrivacy);

		string? profileURL = await GetAbsoluteProfileURL().ConfigureAwait(false);

		if (string.IsNullOrEmpty(profileURL)) {
			Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);

			return false;
		}

		Uri request = new(SteamCommunityURL, $"{profileURL}/ajaxsetprivacy");

		// Extra entry for sessionID
		Dictionary<string, string> data = new(3, StringComparer.Ordinal) {
			{ "eCommentPermission", ((byte) userPrivacy.CommentPermission).ToString(CultureInfo.InvariantCulture) },
			{ "Privacy", userPrivacy.Settings.ToJsonText() }
		};

		ObjectResponse<ResultResponse>? response = await UrlPostToJsonObjectWithSession<ResultResponse>(request, data: data).ConfigureAwait(false);

		if (response?.Content == null) {
			return false;
		}

		if (response.Content.Result != EResult.OK) {
			Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);

			return false;
		}

		return true;
	}

	internal async Task<bool> ClearFromDiscoveryQueue(uint appID) {
		ArgumentOutOfRangeException.ThrowIfZero(appID);

		Uri request = new(SteamStoreURL, $"/app/{appID}");

		// Extra entry for sessionID
		Dictionary<string, string> data = new(2, StringComparer.Ordinal) { { "appid_to_clear_from_queue", appID.ToString(CultureInfo.InvariantCulture) } };

		return await UrlPostWithSession(request, data: data).ConfigureAwait(false);
	}

	internal async Task<bool> DeclineTradeOffer(ulong tradeID) {
		ArgumentOutOfRangeException.ThrowIfZero(tradeID);

		Uri request = new(SteamCommunityURL, $"/tradeoffer/{tradeID}/decline");

		return await UrlPostWithSession(request).ConfigureAwait(false);
	}

	internal HttpClient GenerateDisposableHttpClient() => WebBrowser.GenerateDisposableHttpClient();

	internal async Task<ImmutableHashSet<uint>?> GenerateNewDiscoveryQueue() {
		Uri request = new(SteamStoreURL, "/explore/generatenewdiscoveryqueue");

		// Extra entry for sessionID
		Dictionary<string, string> data = new(2, StringComparer.Ordinal) { { "queuetype", "0" } };

		ObjectResponse<NewDiscoveryQueueResponse>? response = await UrlPostToJsonObjectWithSession<NewDiscoveryQueueResponse>(request, data: data).ConfigureAwait(false);

		return response?.Content?.Queue;
	}

	internal async Task<HashSet<uint>?> GetAppList() {
		KeyValue? response = null;

		for (byte i = 0; (i < WebBrowser.MaxTries) && (response == null); i++) {
			if ((i > 0) && (WebLimiterDelay > 0)) {
				await Task.Delay(WebLimiterDelay).ConfigureAwait(false);
			}

			using WebAPI.AsyncInterface steamAppsService = Bot.SteamConfiguration.GetAsyncWebAPIInterface(SteamAppsService);

			steamAppsService.Timeout = WebBrowser.Timeout;

			try {
				response = await WebLimitRequest(
					WebAPI.DefaultBaseAddress,

					// ReSharper disable once AccessToDisposedClosure
					async () => await steamAppsService.CallAsync(HttpMethod.Get, "GetAppList", 2).ConfigureAwait(false)
				).ConfigureAwait(false);
			} catch (TaskCanceledException e) {
				Bot.ArchiLogger.LogGenericDebuggingException(e);
			} catch (Exception e) {
				Bot.ArchiLogger.LogGenericWarningException(e);
			}
		}

		if (response == null) {
			Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));

			return null;
		}

		List<KeyValue> apps = response["apps"].Children;

		if (apps.Count == 0) {
			Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(apps)));

			return null;
		}

		HashSet<uint> result = new(apps.Count);

		foreach (uint appID in apps.Select(static app => app["appid"].AsUnsignedInteger())) {
			if (appID == 0) {
				Bot.ArchiLogger.LogNullError(appID);

				return null;
			}

			result.Add(appID);
		}

		return result;
	}

	internal async Task<IDocument?> GetBadgePage(byte page, byte maxTries = WebBrowser.MaxTries) {
		ArgumentOutOfRangeException.ThrowIfZero(page);
		ArgumentOutOfRangeException.ThrowIfZero(maxTries);

		Uri request = new(SteamCommunityURL, $"/my/badges?l=english&p={page}");

		HtmlDocumentResponse? response = await UrlGetToHtmlDocumentWithSession(request, checkSessionPreemptively: false, maxTries: maxTries).ConfigureAwait(false);

		return response?.Content;
	}

	internal async Task<byte> GetCardCountForGame(uint appID) {
		ArgumentOutOfRangeException.ThrowIfZero(appID);

		if (ASF.GlobalDatabase?.CardCountsPerGame.TryGetValue(appID, out byte result) == true) {
			return result;
		}

		using IDocument? htmlDocument = await GetGameCardsPage(appID).ConfigureAwait(false);

		if (htmlDocument == null) {
			Bot.ArchiLogger.LogNullError(htmlDocument);

			return 0;
		}

		IList<INode> htmlNodes = htmlDocument.SelectNodes("//div[@class='badge_card_set_cards']/div[starts-with(@class, 'badge_card_set_card')]");

		if (htmlNodes.Count == 0) {
			Bot.ArchiLogger.LogNullError(htmlNodes);

			return 0;
		}

		result = (byte) htmlNodes.Count;

		ASF.GlobalDatabase?.CardCountsPerGame.TryAdd(appID, result);

		return result;
	}

	internal async Task<byte?> GetCombinedTradeHoldDurationAgainstUser(ulong steamID, string? tradeToken = null) {
		if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		string? accessToken = Bot.AccessToken;

		if (string.IsNullOrEmpty(accessToken)) {
			return null;
		}

		Dictionary<string, object?> arguments = new(!string.IsNullOrEmpty(tradeToken) ? 3 : 2, StringComparer.Ordinal) {
			{ "access_token", accessToken },
			{ "steamid_target", steamID }
		};

		if (!string.IsNullOrEmpty(tradeToken)) {
			arguments["trade_offer_access_token"] = tradeToken;
		}

		KeyValue? response = null;

		for (byte i = 0; (i < WebBrowser.MaxTries) && (response == null); i++) {
			if ((i > 0) && (WebLimiterDelay > 0)) {
				await Task.Delay(WebLimiterDelay).ConfigureAwait(false);
			}

			using WebAPI.AsyncInterface econService = Bot.SteamConfiguration.GetAsyncWebAPIInterface(EconService);

			econService.Timeout = WebBrowser.Timeout;

			try {
				response = await WebLimitRequest(
					WebAPI.DefaultBaseAddress,

					// ReSharper disable once AccessToDisposedClosure
					async () => await econService.CallAsync(HttpMethod.Get, "GetTradeHoldDurations", args: arguments).ConfigureAwait(false)
				).ConfigureAwait(false);
			} catch (TaskCanceledException e) {
				Bot.ArchiLogger.LogGenericDebuggingException(e);
			} catch (Exception e) {
				Bot.ArchiLogger.LogGenericWarningException(e);
			}
		}

		if (response == null) {
			Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));

			return null;
		}

		uint resultInSeconds = response["both_escrow"]["escrow_end_duration_seconds"].AsUnsignedInteger(uint.MaxValue);

		if (resultInSeconds == uint.MaxValue) {
			Bot.ArchiLogger.LogNullError(resultInSeconds);

			return null;
		}

		return resultInSeconds == 0 ? (byte) 0 : (byte) (resultInSeconds / 86400);
	}

	internal async Task<ConfirmationsResponse?> GetConfirmations(string deviceID, string confirmationHash, ulong time) {
		ArgumentException.ThrowIfNullOrEmpty(deviceID);
		ArgumentException.ThrowIfNullOrEmpty(confirmationHash);
		ArgumentOutOfRangeException.ThrowIfZero(time);

		if (!Initialized) {
			byte connectionTimeout = ASF.GlobalConfig?.ConnectionTimeout ?? GlobalConfig.DefaultConnectionTimeout;

			for (byte i = 0; (i < connectionTimeout) && !Initialized && Bot.IsConnectedAndLoggedOn; i++) {
				await Task.Delay(1000).ConfigureAwait(false);
			}

			if (!Initialized) {
				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);

				return null;
			}
		}

		// Confirmations page is notorious for freezing, not returning confirmations and other issues
		// It's unknown what exactly causes those problems, but restart of the bot fixes those in almost all cases
		// Normally this wouldn't make any sense, but let's ensure that we've refreshed our session recently as a possible workaround
		if (DateTime.UtcNow - SessionValidUntil > TimeSpan.FromMinutes(5)) {
			if (!await RefreshSession().ConfigureAwait(false)) {
				return null;
			}
		}

		Uri request = new(SteamCommunityURL, $"/mobileconf/getlist?a={Bot.SteamID}&k={Uri.EscapeDataString(confirmationHash)}&l=english&m=react&p={Uri.EscapeDataString(deviceID)}&t={time}&tag=conf");

		ObjectResponse<ConfirmationsResponse>? response = await UrlGetToJsonObjectWithSession<ConfirmationsResponse>(request, checkSessionPreemptively: false).ConfigureAwait(false);

		return response?.Content;
	}

	internal async Task<HashSet<ulong>?> GetDigitalGiftCards() {
		Uri request = new(SteamStoreURL, "/gifts?l=english");

		using HtmlDocumentResponse? response = await UrlGetToHtmlDocumentWithSession(request, checkSessionPreemptively: false).ConfigureAwait(false);

		if (response?.Content == null) {
			return null;
		}

		IEnumerable<IAttr> htmlNodes = response.Content.SelectNodes<IAttr>("//div[@class='pending_gift']/div[starts-with(@id, 'pending_gift_')][count(div[@class='pending_giftcard_leftcol']) > 0]/@id");

		HashSet<ulong> results = [];

		foreach (string giftCardIDText in htmlNodes.Select(static htmlNode => htmlNode.Value)) {
			if (string.IsNullOrEmpty(giftCardIDText)) {
				Bot.ArchiLogger.LogNullError(giftCardIDText);

				return null;
			}

			if (giftCardIDText.Length <= 13) {
				Bot.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, nameof(giftCardIDText)));

				return null;
			}

			if (!ulong.TryParse(giftCardIDText[13..], out ulong giftCardID) || (giftCardID == 0)) {
				Bot.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.ErrorParsingObject, nameof(giftCardID)));

				return null;
			}

			results.Add(giftCardID);
		}

		return results;
	}

	internal async Task<IDocument?> GetDiscoveryQueuePage() {
		Uri request = new(SteamStoreURL, "/explore?l=english");

		HtmlDocumentResponse? response = await UrlGetToHtmlDocumentWithSession(request, checkSessionPreemptively: false).ConfigureAwait(false);

		return response?.Content;
	}

	internal async Task<HashSet<ulong>?> GetFamilySharingSteamIDs() {
		Uri request = new(SteamStoreURL, "/account/managedevices?l=english");

		using HtmlDocumentResponse? response = await UrlGetToHtmlDocumentWithSession(request, checkSessionPreemptively: false).ConfigureAwait(false);

		if (response?.Content == null) {
			return null;
		}

		IEnumerable<IAttr> htmlNodes = response.Content.SelectNodes<IAttr>("(//table[@class='accountTable'])[2]//a/@data-miniprofile");

		HashSet<ulong> result = [];

		foreach (string miniProfile in htmlNodes.Select(static htmlNode => htmlNode.Value)) {
			if (string.IsNullOrEmpty(miniProfile)) {
				Bot.ArchiLogger.LogNullError(miniProfile);

				return null;
			}

			if (!uint.TryParse(miniProfile, out uint steamID3) || (steamID3 == 0)) {
				Bot.ArchiLogger.LogNullError(steamID3);

				return null;
			}

			ulong steamID = new SteamID(steamID3, EUniverse.Public, EAccountType.Individual);
			result.Add(steamID);
		}

		return result;
	}

	internal async Task<IDocument?> GetGameCardsPage(uint appID) {
		ArgumentOutOfRangeException.ThrowIfZero(appID);

		Uri request = new(SteamCommunityURL, $"/my/gamecards/{appID}?l=english");

		HtmlDocumentResponse? response = await UrlGetToHtmlDocumentWithSession(request, checkSessionPreemptively: false).ConfigureAwait(false);

		return response?.Content;
	}

	internal async Task<byte?> GetTradeHoldDurationForTrade(ulong tradeID) {
		ArgumentOutOfRangeException.ThrowIfZero(tradeID);

		Uri request = new(SteamCommunityURL, $"/tradeoffer/{tradeID}?l=english");

		using HtmlDocumentResponse? response = await UrlGetToHtmlDocumentWithSession(request, checkSessionPreemptively: false).ConfigureAwait(false);

		INode? htmlNode = response?.Content?.SelectSingleNode("//div[@class='pagecontent']/script");

		if (htmlNode == null) {
			// Trade can be no longer valid
			return null;
		}

		string text = htmlNode.TextContent;

		if (string.IsNullOrEmpty(text)) {
			Bot.ArchiLogger.LogNullError(text);

			return null;
		}

		const string daysEscrowVariableName = "g_daysBothEscrow = ";
		int index = text.IndexOf(daysEscrowVariableName, StringComparison.Ordinal);

		if (index < 0) {
			Bot.ArchiLogger.LogNullError(index);

			return null;
		}

		index += daysEscrowVariableName.Length;
		text = text[index..];

		index = text.IndexOf(';', StringComparison.Ordinal);

		if (index < 0) {
			Bot.ArchiLogger.LogNullError(index);

			return null;
		}

		text = text[..index];

		if (!byte.TryParse(text, out byte result)) {
			Bot.ArchiLogger.LogNullError(result);

			return null;
		}

		return result;
	}

	internal async Task<bool?> HandleConfirmation(string deviceID, string confirmationHash, ulong time, ulong confirmationID, ulong confirmationKey, bool accept) {
		ArgumentException.ThrowIfNullOrEmpty(deviceID);
		ArgumentException.ThrowIfNullOrEmpty(confirmationHash);
		ArgumentOutOfRangeException.ThrowIfZero(time);
		ArgumentOutOfRangeException.ThrowIfZero(confirmationID);
		ArgumentOutOfRangeException.ThrowIfZero(confirmationKey);

		if (!Initialized) {
			byte connectionTimeout = ASF.GlobalConfig?.ConnectionTimeout ?? GlobalConfig.DefaultConnectionTimeout;

			for (byte i = 0; (i < connectionTimeout) && !Initialized && Bot.IsConnectedAndLoggedOn; i++) {
				await Task.Delay(1000).ConfigureAwait(false);
			}

			if (!Initialized) {
				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);

				return null;
			}
		}

		Uri request = new(SteamCommunityURL, $"/mobileconf/ajaxop?a={Bot.SteamID}&cid={confirmationID}&ck={confirmationKey}&k={Uri.EscapeDataString(confirmationHash)}&l=english&m=react&op={(accept ? "allow" : "cancel")}&p={Uri.EscapeDataString(deviceID)}&t={time}&tag=conf");

		ObjectResponse<BooleanResponse>? response = await UrlGetToJsonObjectWithSession<BooleanResponse>(request).ConfigureAwait(false);

		return response?.Content?.Success;
	}

	internal async Task<bool?> HandleConfirmations(string deviceID, string confirmationHash, ulong time, IReadOnlyCollection<Confirmation> confirmations, bool accept) {
		ArgumentException.ThrowIfNullOrEmpty(deviceID);
		ArgumentException.ThrowIfNullOrEmpty(confirmationHash);
		ArgumentOutOfRangeException.ThrowIfZero(time);

		if ((confirmations == null) || (confirmations.Count == 0)) {
			throw new ArgumentNullException(nameof(confirmations));
		}

		if (!Initialized) {
			byte connectionTimeout = ASF.GlobalConfig?.ConnectionTimeout ?? GlobalConfig.DefaultConnectionTimeout;

			for (byte i = 0; (i < connectionTimeout) && !Initialized && Bot.IsConnectedAndLoggedOn; i++) {
				await Task.Delay(1000).ConfigureAwait(false);
			}

			if (!Initialized) {
				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);

				return null;
			}
		}

		Uri request = new(SteamCommunityURL, "/mobileconf/multiajaxop");

		// Extra entry for sessionID
		List<KeyValuePair<string, string>> data = new(8 + (confirmations.Count * 2)) {
			new KeyValuePair<string, string>("a", Bot.SteamID.ToString(CultureInfo.InvariantCulture)),
			new KeyValuePair<string, string>("k", confirmationHash),
			new KeyValuePair<string, string>("m", "react"),
			new KeyValuePair<string, string>("op", accept ? "allow" : "cancel"),
			new KeyValuePair<string, string>("p", deviceID),
			new KeyValuePair<string, string>("t", time.ToString(CultureInfo.InvariantCulture)),
			new KeyValuePair<string, string>("tag", "conf")
		};

		foreach (Confirmation confirmation in confirmations) {
			data.Add(new KeyValuePair<string, string>("cid[]", confirmation.ID.ToString(CultureInfo.InvariantCulture)));
			data.Add(new KeyValuePair<string, string>("ck[]", confirmation.Nonce.ToString(CultureInfo.InvariantCulture)));
		}

		ObjectResponse<BooleanResponse>? response = await UrlPostToJsonObjectWithSession<BooleanResponse>(request, data: data).ConfigureAwait(false);

		return response?.Content?.Success;
	}

	internal async Task<bool> Init(ulong steamID, EUniverse universe, string accessToken, string? parentalCode = null) {
		if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		if ((universe == EUniverse.Invalid) || !Enum.IsDefined(universe)) {
			throw new InvalidEnumArgumentException(nameof(universe), (int) universe, typeof(EUniverse));
		}

		ArgumentException.ThrowIfNullOrEmpty(accessToken);

		string steamLoginSecure = $"{steamID}||{accessToken}";

		if (Initialized) {
			string? previousSteamLoginSecure = WebBrowser.CookieContainer.GetCookieValue(SteamCommunityURL, "steamLoginSecure");

			if (previousSteamLoginSecure == steamLoginSecure) {
				// We have nothing to update, skip this request
				return true;
			}
		}

		Initialized = false;

#pragma warning disable CA1308 // False positive, we're intentionally converting this part to lowercase and it's not used for any security decisions based on the result of the normalization
		string sessionID = Convert.ToHexString(RandomNumberGenerator.GetBytes(SessionIDLength / 2)).ToLowerInvariant();
#pragma warning restore CA1308 // False positive, we're intentionally converting this part to lowercase and it's not used for any security decisions based on the result of the normalization

		WebBrowser.CookieContainer.Add(new Cookie("sessionid", sessionID, "/", $".{SteamCheckoutURL.Host}"));
		WebBrowser.CookieContainer.Add(new Cookie("sessionid", sessionID, "/", $".{SteamCommunityURL.Host}"));
		WebBrowser.CookieContainer.Add(new Cookie("sessionid", sessionID, "/", $".{SteamHelpURL.Host}"));
		WebBrowser.CookieContainer.Add(new Cookie("sessionid", sessionID, "/", $".{SteamStoreURL.Host}"));

		WebBrowser.CookieContainer.Add(new Cookie("steamLoginSecure", steamLoginSecure, "/", $".{SteamCheckoutURL.Host}"));
		WebBrowser.CookieContainer.Add(new Cookie("steamLoginSecure", steamLoginSecure, "/", $".{SteamCommunityURL.Host}"));
		WebBrowser.CookieContainer.Add(new Cookie("steamLoginSecure", steamLoginSecure, "/", $".{SteamHelpURL.Host}"));
		WebBrowser.CookieContainer.Add(new Cookie("steamLoginSecure", steamLoginSecure, "/", $".{SteamStoreURL.Host}"));

		// Report proper time when doing timezone-based calculations, see setTimezoneCookies() from https://steamcommunity-a.akamaihd.net/public/shared/javascript/shared_global.js
		string timeZoneOffset = $"{(int) DateTimeOffset.Now.Offset.TotalSeconds}{Uri.EscapeDataString(",")}0";

		WebBrowser.CookieContainer.Add(new Cookie("timezoneOffset", timeZoneOffset, "/", $".{SteamCheckoutURL.Host}"));
		WebBrowser.CookieContainer.Add(new Cookie("timezoneOffset", timeZoneOffset, "/", $".{SteamCommunityURL.Host}"));
		WebBrowser.CookieContainer.Add(new Cookie("timezoneOffset", timeZoneOffset, "/", $".{SteamHelpURL.Host}"));
		WebBrowser.CookieContainer.Add(new Cookie("timezoneOffset", timeZoneOffset, "/", $".{SteamStoreURL.Host}"));

		Bot.ArchiLogger.LogGenericInfo(Strings.Success);

		// Unlock Steam Parental if needed
		if (!string.IsNullOrEmpty(parentalCode)) {
			if (!await UnlockParentalAccount(parentalCode).ConfigureAwait(false)) {
				return false;
			}
		}

		LastSessionCheck = DateTime.UtcNow;
		SessionValidUntil = LastSessionCheck.AddSeconds(MinimumSessionValidityInSeconds);
		Initialized = true;

		return true;
	}

	internal async Task MarkInventory() {
		if (ASF.InventorySemaphore == null) {
			throw new InvalidOperationException(nameof(ASF.InventorySemaphore));
		}

		// We aim to have a maximum of 2 tasks, one already working, and one waiting in the queue
		// This way we can call this function as many times as needed e.g. because of Steam events
		lock (ASF.InventorySemaphore) {
			if (MarkingInventoryScheduled) {
				return;
			}

			MarkingInventoryScheduled = true;
		}

		Uri request = new(SteamCommunityURL, "/my/inventory");

		int rateLimitingDelay = (ASF.GlobalConfig?.InventoryLimiterDelay ?? GlobalConfig.DefaultInventoryLimiterDelay) * 1000;

		await ASF.InventorySemaphore.WaitAsync().ConfigureAwait(false);

		try {
			lock (ASF.InventorySemaphore) {
				MarkingInventoryScheduled = false;
			}

			await UrlHeadWithSession(request, checkSessionPreemptively: false, rateLimitingDelay: rateLimitingDelay).ConfigureAwait(false);
		} finally {
			if (rateLimitingDelay == 0) {
				ASF.InventorySemaphore.Release();
			} else {
				Utilities.InBackground(
					async () => {
						await Task.Delay(rateLimitingDelay).ConfigureAwait(false);
						ASF.InventorySemaphore.Release();
					}
				);
			}
		}
	}

	internal async Task<bool> MarkSentTrades() {
		Uri request = new(SteamCommunityURL, "/my/tradeoffers/sent");

		return await UrlHeadWithSession(request, checkSessionPreemptively: false).ConfigureAwait(false);
	}

	internal void OnDisconnected() => Initialized = false;

	internal void OnVanityURLChanged(string? vanityURL = null) => VanityURL = !string.IsNullOrEmpty(vanityURL) ? vanityURL : null;

	internal async Task<(EResult Result, EPurchaseResultDetail? PurchaseResult, string? BalanceText)?> RedeemWalletKey(string key) {
		ArgumentException.ThrowIfNullOrEmpty(key);

		Uri request = new(SteamStoreURL, "/account/ajaxredeemwalletcode");

		// Extra entry for sessionID
		Dictionary<string, string> data = new(2, StringComparer.Ordinal) { { "wallet_code", key } };

		ObjectResponse<RedeemWalletResponse>? response = await UrlPostToJsonObjectWithSession<RedeemWalletResponse>(request, data: data).ConfigureAwait(false);

		if (response?.Content == null) {
			return null;
		}

		// We can not trust EResult response, because it is OK even in the case of error, so changing it to Fail in this case
		if ((response.Content.Result != EResult.OK) || (response.Content.PurchaseResultDetail != EPurchaseResultDetail.NoDetail)) {
			return (response.Content.Result == EResult.OK ? EResult.Fail : response.Content.Result, response.Content.PurchaseResultDetail, response.Content.BalanceText);
		}

		return (EResult.OK, EPurchaseResultDetail.NoDetail, response.Content.BalanceText);
	}

	internal async Task<bool> UnpackBooster(uint appID, ulong itemID) {
		ArgumentOutOfRangeException.ThrowIfZero(appID);
		ArgumentOutOfRangeException.ThrowIfZero(itemID);

		string? profileURL = await GetAbsoluteProfileURL().ConfigureAwait(false);

		if (string.IsNullOrEmpty(profileURL)) {
			Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);

			return false;
		}

		Uri request = new(SteamCommunityURL, $"{profileURL}/ajaxunpackbooster");

		// Extra entry for sessionID
		Dictionary<string, string> data = new(3, StringComparer.Ordinal) {
			{ "appid", appID.ToString(CultureInfo.InvariantCulture) },
			{ "communityitemid", itemID.ToString(CultureInfo.InvariantCulture) }
		};

		ObjectResponse<ResultResponse>? response = await UrlPostToJsonObjectWithSession<ResultResponse>(request, data: data).ConfigureAwait(false);

		return response?.Content?.Result == EResult.OK;
	}

	private async Task<bool> IsProfileUri(Uri uri, bool waitForInitialization = true) {
		ArgumentNullException.ThrowIfNull(uri);

		string? profileURL = await GetAbsoluteProfileURL(waitForInitialization).ConfigureAwait(false);

		if (string.IsNullOrEmpty(profileURL)) {
			Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);

			return false;
		}

		return uri.AbsolutePath.Equals(profileURL, StringComparison.OrdinalIgnoreCase);
	}

	private async Task<bool?> IsSessionExpired() {
		DateTime triggeredAt = DateTime.UtcNow;

		if (triggeredAt <= SessionValidUntil) {
			// Assume session is still valid
			return false;
		}

		await SessionSemaphore.WaitAsync().ConfigureAwait(false);

		try {
			if (triggeredAt <= SessionValidUntil) {
				// Other request already checked the session for us in the meantime, nice
				return false;
			}

			if (triggeredAt <= LastSessionCheck) {
				// Other request already checked the session for us in the meantime and failed, pointless to try again
				return true;
			}

			// Choosing proper URL to check against is actually much harder than it initially looks like, we must abide by several rules to make this function as lightweight and reliable as possible
			// We should prefer to use Steam store, as the community is much more unstable and broken, plus majority of our requests get there anyway, so load-balancing with store makes much more sense. It also has a higher priority than the community, so all eventual issues should be fixed there first
			// The URL must be fast enough to render, as this function will be called reasonably often, and every extra delay adds up. We're already making our best effort by using HEAD request, but the URL itself plays a very important role as well
			// The page should have as little internal dependencies as possible, since every extra chunk increases likelihood of broken functionality. We can only make a guess here based on the amount of content that the page returns to us
			// It should also be URL with fairly fixed address that isn't going to disappear anytime soon, preferably something staple that is a dependency of other requests, so it's very unlikely to change in a way that would add overhead in the future
			// Lastly, it should be a request that is preferably generic enough as a routine check, not something specialized and targetted, to make it very clear that we're just checking if session is up, and to further aid internal dependencies specified above by rendering as general Steam info as possible
			Uri request = new(SteamStoreURL, "/account");

			BasicResponse? response = await WebLimitRequest(SteamStoreURL, async () => await WebBrowser.UrlHead(request, rateLimitingDelay: WebLimiterDelay).ConfigureAwait(false)).ConfigureAwait(false);

			if (response == null) {
				return null;
			}

			bool result = IsSessionExpiredUri(response.FinalUri);

			DateTime now = DateTime.UtcNow;

			if (result) {
				Initialized = false;
				SessionValidUntil = DateTime.MinValue;
			} else {
				SessionValidUntil = now.AddSeconds(MinimumSessionValidityInSeconds);
			}

			LastSessionCheck = now;

			return result;
		} finally {
			SessionSemaphore.Release();
		}
	}

	private static bool IsSessionExpiredUri(Uri uri) {
		ArgumentNullException.ThrowIfNull(uri);

		return uri.AbsolutePath.StartsWith("/login", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("lostauth", StringComparison.OrdinalIgnoreCase);
	}

	private async Task<bool> RefreshSession() {
		if (!Bot.IsConnectedAndLoggedOn) {
			return false;
		}

		DateTime previousSessionValidUntil = SessionValidUntil;

		DateTime triggeredAt = DateTime.UtcNow;

		await SessionSemaphore.WaitAsync().ConfigureAwait(false);

		try {
			if ((triggeredAt <= SessionValidUntil) && (SessionValidUntil > previousSessionValidUntil)) {
				// Other request already refreshed the session for us in the meantime, nice
				return true;
			}

			if (triggeredAt <= LastSessionCheck) {
				// Other request already checked the session for us in the meantime and failed, pointless to try again
				return false;
			}

			Initialized = false;
			SessionValidUntil = DateTime.MinValue;

			if (!Bot.IsConnectedAndLoggedOn) {
				return false;
			}

			Bot.ArchiLogger.LogGenericInfo(Strings.RefreshingOurSession);
			bool result = await Bot.RefreshWebSession(true).ConfigureAwait(false);

			DateTime now = DateTime.UtcNow;

			if (result) {
				SessionValidUntil = now.AddSeconds(MinimumSessionValidityInSeconds);
			}

			LastSessionCheck = now;

			return result;
		} finally {
			SessionSemaphore.Release();
		}
	}

	private static void SetDescriptionsToAssets(IEnumerable<Asset> assets, [SuppressMessage("ReSharper", "SuggestBaseTypeForParameter")] Dictionary<(uint AppID, ulong ClassID, ulong InstanceID), InventoryDescription> descriptions) {
		ArgumentNullException.ThrowIfNull(assets);
		ArgumentNullException.ThrowIfNull(descriptions);

		foreach (Asset asset in assets) {
			(uint AppID, ulong ClassID, ulong InstanceID) key = (asset.AppID, asset.ClassID, asset.InstanceID);

			if (descriptions.TryGetValue(key, out InventoryDescription? description)) {
				asset.Description = description;
			}
		}
	}

	private async Task<bool> UnlockParentalAccount(string parentalCode) {
		ArgumentException.ThrowIfNullOrEmpty(parentalCode);

		Bot.ArchiLogger.LogGenericInfo(Strings.UnlockingParentalAccount);

		bool[] results = await Task.WhenAll(UnlockParentalAccountForService(SteamCommunityURL, parentalCode), UnlockParentalAccountForService(SteamStoreURL, parentalCode)).ConfigureAwait(false);

		if (results.Any(static result => !result)) {
			Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);

			return false;
		}

		Bot.ArchiLogger.LogGenericInfo(Strings.Success);

		return true;
	}

	private async Task<bool> UnlockParentalAccountForService(Uri service, string parentalCode, byte maxTries = WebBrowser.MaxTries) {
		ArgumentNullException.ThrowIfNull(service);
		ArgumentException.ThrowIfNullOrEmpty(parentalCode);

		Uri request = new(service, "/parental/ajaxunlock");

		if (maxTries == 0) {
			Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));
			Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

			return false;
		}

		string? sessionID = WebBrowser.CookieContainer.GetCookieValue(service, "sessionid");

		if (string.IsNullOrEmpty(sessionID)) {
			Bot.ArchiLogger.LogNullError(sessionID);

			return false;
		}

		Dictionary<string, string> data = new(2, StringComparer.Ordinal) {
			{ "pin", parentalCode },
			{ "sessionid", sessionID }
		};

		// This request doesn't go through UrlPostRetryWithSession as we have no access to session refresh capability (this is in fact session initialization)
		BasicResponse? response = await WebLimitRequest(service, async () => await WebBrowser.UrlPost(request, data: data, referer: service, rateLimitingDelay: WebLimiterDelay).ConfigureAwait(false)).ConfigureAwait(false);

		if ((response == null) || IsSessionExpiredUri(response.FinalUri)) {
			// There is no session refresh capability at this stage
			return false;
		}

		// Under special brain-damaged circumstances, Steam might just return our own profile as a response to the request, for absolutely no reason whatsoever - just try again in this case
		if (await IsProfileUri(response.FinalUri, false).ConfigureAwait(false)) {
			Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.WarningWorkaroundTriggered, nameof(IsProfileUri)));

			return await UnlockParentalAccountForService(service, parentalCode, --maxTries).ConfigureAwait(false);
		}

		return true;
	}

	public enum ESession : byte {
		None,
		Lowercase,
		CamelCase,
		PascalCase
	}
}
