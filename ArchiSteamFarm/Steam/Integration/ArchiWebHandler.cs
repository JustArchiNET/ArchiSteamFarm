//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2021 Łukasz "JustArchi" Domeradzki
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

#if NETFRAMEWORK
using JustArchiNET.Madness;
#endif
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Dom;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Steam.Data;
using ArchiSteamFarm.Steam.Exchange;
using ArchiSteamFarm.Steam.Security;
using ArchiSteamFarm.Steam.Storage;
using ArchiSteamFarm.Storage;
using ArchiSteamFarm.Web;
using ArchiSteamFarm.Web.Responses;
using JetBrains.Annotations;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SteamKit2;

namespace ArchiSteamFarm.Steam.Integration {
	public sealed class ArchiWebHandler : IDisposable {
		internal const ushort MaxItemsInSingleInventoryRequest = 5000;

		private const string EconService = "IEconService";
		private const string LoyaltyRewardsService = "ILoyaltyRewardsService";
		private const string SteamAppsService = "ISteamApps";
		private const string SteamUserAuthService = "ISteamUserAuth";
		private const string TwoFactorService = "ITwoFactorService";

		[PublicAPI]
		public static Uri SteamCommunityURL => new("https://steamcommunity.com");

		[PublicAPI]
		public static Uri SteamHelpURL => new("https://help.steampowered.com");

		[PublicAPI]
		public static Uri SteamStoreURL => new("https://store.steampowered.com");

		private static readonly ConcurrentDictionary<uint, byte> CachedCardCountsForGame = new();

		[PublicAPI]
		public ArchiCacheable<string> CachedAccessToken { get; }

		[PublicAPI]
		public ArchiCacheable<string> CachedApiKey { get; }

		[PublicAPI]
		public WebBrowser WebBrowser { get; }

		private readonly Bot Bot;
		private readonly SemaphoreSlim SessionSemaphore = new(1, 1);

		private bool Initialized;
		private DateTime LastSessionCheck;
		private DateTime LastSessionRefresh;
		private bool MarkingInventoryScheduled;
		private string? VanityURL;

		internal ArchiWebHandler(Bot bot) {
			Bot = bot ?? throw new ArgumentNullException(nameof(bot));

			CachedApiKey = new ArchiCacheable<string>(ResolveApiKey);
			CachedAccessToken = new ArchiCacheable<string>(ResolveAccessToken);

			WebBrowser = new WebBrowser(bot.ArchiLogger, ASF.GlobalConfig?.WebProxy);
		}

		public void Dispose() {
			CachedApiKey.Dispose();
			CachedAccessToken.Dispose();
			SessionSemaphore.Dispose();
			WebBrowser.Dispose();
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

			return string.IsNullOrEmpty(VanityURL) ? "/profiles/" + Bot.SteamID : "/id/" + VanityURL;
		}

		[PublicAPI]
		public async IAsyncEnumerable<Asset> GetInventoryAsync(ulong steamID = 0, uint appID = Asset.SteamAppID, ulong contextID = Asset.SteamCommunityContextID) {
			if (appID == 0) {
				throw new ArgumentOutOfRangeException(nameof(appID));
			}

			if (contextID == 0) {
				throw new ArgumentOutOfRangeException(nameof(contextID));
			}

			if (ASF.InventorySemaphore == null) {
				throw new InvalidOperationException(nameof(ASF.InventorySemaphore));
			}

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

			ulong startAssetID = 0;

			// We need to store asset IDs to make sure we won't get duplicate items
			HashSet<ulong>? assetIDs = null;

			while (true) {
				await ASF.InventorySemaphore.WaitAsync().ConfigureAwait(false);

				try {
					Uri request = new(SteamCommunityURL, "/inventory/" + steamID + "/" + appID + "/" + contextID + "?count=" + MaxItemsInSingleInventoryRequest + "&l=english" + (startAssetID > 0 ? "&start_assetid=" + startAssetID : ""));

					ObjectResponse<InventoryResponse>? response = await UrlGetToJsonObjectWithSession<InventoryResponse>(request).ConfigureAwait(false);

					if (response == null) {
						throw new HttpRequestException(string.Format(CultureInfo.CurrentCulture, Strings.ErrorObjectIsNull, nameof(response)));
					}

					if (response.Content.Result != EResult.OK) {
						throw new HttpRequestException(!string.IsNullOrEmpty(response.Content.Error) ? string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, response.Content.Error) : Strings.WarningFailed);
					}

					if (response.Content.TotalInventoryCount == 0) {
						// Empty inventory
						yield break;
					}

					assetIDs ??= new HashSet<ulong>((int) response.Content.TotalInventoryCount);

					if ((response.Content.Assets.Count == 0) || (response.Content.Descriptions.Count == 0)) {
						throw new NotSupportedException(string.Format(CultureInfo.CurrentCulture, Strings.ErrorObjectIsNull, nameof(response.Content.Assets) + " || " + nameof(response.Content.Descriptions)));
					}

					Dictionary<(ulong ClassID, ulong InstanceID), InventoryResponse.Description> descriptions = new();

					foreach (InventoryResponse.Description description in response.Content.Descriptions) {
						if (description.ClassID == 0) {
							throw new NotSupportedException(string.Format(CultureInfo.CurrentCulture, Strings.ErrorObjectIsNull, nameof(description.ClassID)));
						}

						(ulong ClassID, ulong InstanceID) key = (description.ClassID, description.InstanceID);

						if (descriptions.ContainsKey(key)) {
							continue;
						}

						descriptions[key] = description;
					}

					foreach (Asset asset in response.Content.Assets) {
						if (!descriptions.TryGetValue((asset.ClassID, asset.InstanceID), out InventoryResponse.Description? description) || assetIDs.Contains(asset.AssetID)) {
							continue;
						}

						asset.Marketable = description.Marketable;
						asset.Tradable = description.Tradable;
						asset.Tags = description.Tags;
						asset.RealAppID = description.RealAppID;
						asset.Type = description.Type;
						asset.Rarity = description.Rarity;

						if (description.AdditionalProperties != null) {
							asset.AdditionalProperties = description.AdditionalProperties;
						}

						assetIDs.Add(asset.AssetID);

						yield return asset;
					}

					if (!response.Content.MoreItems) {
						yield break;
					}

					if (response.Content.LastAssetID == 0) {
						throw new NotSupportedException(string.Format(CultureInfo.CurrentCulture, Strings.ErrorObjectIsNull, nameof(response.Content.LastAssetID)));
					}

					startAssetID = response.Content.LastAssetID;
				} finally {
					byte inventoryLimiterDelay = ASF.GlobalConfig?.InventoryLimiterDelay ?? GlobalConfig.DefaultInventoryLimiterDelay;

					if (inventoryLimiterDelay == 0) {
						ASF.InventorySemaphore.Release();
					} else {
						Utilities.InBackground(
							async () => {
								await Task.Delay(inventoryLimiterDelay * 1000).ConfigureAwait(false);
								ASF.InventorySemaphore.Release();
							}
						);
					}
				}
			}
		}

		[PublicAPI]
		public async Task<uint?> GetPointsBalance() {
			(bool success, string? accessToken) = await CachedAccessToken.GetValue().ConfigureAwait(false);

			if (!success || string.IsNullOrEmpty(accessToken)) {
				return null;
			}

			// Extra entry for format
			Dictionary<string, object> arguments = new(3, StringComparer.Ordinal) {
				// ReSharper disable once RedundantSuppressNullableWarningExpression - required for .NET Framework
				{ "access_token", accessToken! },

				{ "steamid", Bot.SteamID }
			};

			KeyValue? response = null;

			for (byte i = 0; (i < WebBrowser.MaxTries) && (response == null); i++) {
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
				Bot.ArchiLogger.LogNullError(nameof(pointsInfo));

				return null;
			}

			uint result = pointsInfo.AsUnsignedInteger(uint.MaxValue);

			if (result == uint.MaxValue) {
				Bot.ArchiLogger.LogNullError(nameof(result));

				return null;
			}

			return result;
		}

		[PublicAPI]
		public async Task<bool?> HasValidApiKey() {
			(bool success, string? steamApiKey) = await CachedApiKey.GetValue().ConfigureAwait(false);

			return success ? !string.IsNullOrEmpty(steamApiKey) : null;
		}

		[PublicAPI]
		public async Task<bool> JoinGroup(ulong groupID) {
			if ((groupID == 0) || !new SteamID(groupID).IsClanAccount) {
				throw new ArgumentOutOfRangeException(nameof(groupID));
			}

			Uri request = new(SteamCommunityURL, "/gid/" + groupID);

			// Extra entry for sessionID
			Dictionary<string, string> data = new(2, StringComparer.Ordinal) { { "action", "join" } };

			return await UrlPostWithSession(request, data: data, session: ESession.CamelCase).ConfigureAwait(false);
		}

		[PublicAPI]
		public async Task<(bool Success, HashSet<ulong>? MobileTradeOfferIDs)> SendTradeOffer(ulong steamID, IReadOnlyCollection<Asset>? itemsToGive = null, IReadOnlyCollection<Asset>? itemsToReceive = null, string? token = null, bool forcedSingleOffer = false, ushort itemsPerTrade = Trading.MaxItemsPerTrade) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
				throw new ArgumentOutOfRangeException(nameof(steamID));
			}

			if (((itemsToGive == null) || (itemsToGive.Count == 0)) && ((itemsToReceive == null) || (itemsToReceive.Count == 0))) {
				throw new ArgumentException(nameof(itemsToGive) + " && " + nameof(itemsToReceive));
			}

			if (itemsPerTrade <= 2) {
				throw new ArgumentOutOfRangeException(nameof(itemsPerTrade));
			}

			TradeOfferSendRequest singleTrade = new();
			HashSet<TradeOfferSendRequest> trades = new() { singleTrade };

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
				{ "trade_offer_create_params", !string.IsNullOrEmpty(token) ? new JObject { { "trade_offer_access_token", token } }.ToString(Formatting.None) : "" },
				{ "tradeoffermessage", "Sent by " + SharedInfo.PublicIdentifier + "/" + SharedInfo.Version }
			};

			HashSet<ulong> mobileTradeOfferIDs = new();

			foreach (TradeOfferSendRequest trade in trades) {
				data["json_tradeoffer"] = JsonConvert.SerializeObject(trade);

				ObjectResponse<TradeOfferSendResponse>? response = null;

				for (byte i = 0; (i < WebBrowser.MaxTries) && (response == null); i++) {
					response = await UrlPostToJsonObjectWithSession<TradeOfferSendResponse>(request, data: data, referer: referer, requestOptions: WebBrowser.ERequestOptions.ReturnServerErrors).ConfigureAwait(false);

					if (response == null) {
						return (false, mobileTradeOfferIDs);
					}

					if (response.StatusCode.IsServerErrorCode()) {
						if (string.IsNullOrEmpty(response.Content.ErrorText)) {
							// This is a generic server error without a reason, try again
							response = null;

							continue;
						}

						// This is actually client error with a reason, so it doesn't make sense to retry
						Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, response.Content.ErrorText));

						return (false, mobileTradeOfferIDs);
					}
				}

				if (response == null) {
					return (false, mobileTradeOfferIDs);
				}

				if (response.Content.TradeOfferID == 0) {
					Bot.ArchiLogger.LogNullError(nameof(response.Content.TradeOfferID));

					return (false, mobileTradeOfferIDs);
				}

				if (response.Content.RequiresMobileConfirmation) {
					mobileTradeOfferIDs.Add(response.Content.TradeOfferID);
				}
			}

			return (true, mobileTradeOfferIDs);
		}

		[PublicAPI]
		public async Task<HtmlDocumentResponse?> UrlGetToHtmlDocumentWithSession(Uri request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, Uri? referer = null, WebBrowser.ERequestOptions requestOptions = WebBrowser.ERequestOptions.None, bool checkSessionPreemptively = true, byte maxTries = WebBrowser.MaxTries) {
			if (request == null) {
				throw new ArgumentNullException(nameof(request));
			}

			if (maxTries == 0) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

				return null;
			}

			if (checkSessionPreemptively) {
				// Check session preemptively as this request might not get redirected to expiration
				bool? sessionExpired = await IsSessionExpired().ConfigureAwait(false);

				if (sessionExpired.GetValueOrDefault(true)) {
					if (await RefreshSession().ConfigureAwait(false)) {
						return await UrlGetToHtmlDocumentWithSession(request, headers, referer, requestOptions, true, --maxTries).ConfigureAwait(false);
					}

					Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
					Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

					return null;
				}
			} else {
				// If session refresh is already in progress, just wait for it
				await SessionSemaphore.WaitAsync().ConfigureAwait(false);
				SessionSemaphore.Release();
			}

			if (!Initialized) {
				byte connectionTimeout = ASF.GlobalConfig?.ConnectionTimeout ?? GlobalConfig.DefaultConnectionTimeout;

				for (byte i = 0; (i < connectionTimeout) && !Initialized && Bot.IsConnectedAndLoggedOn; i++) {
					await Task.Delay(1000).ConfigureAwait(false);
				}

				if (!Initialized) {
					Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
					Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

					return null;
				}
			}

			Uri host = new(request.GetLeftPart(UriPartial.Authority));

			HtmlDocumentResponse? response = await WebLimitRequest(host, async () => await WebBrowser.UrlGetToHtmlDocument(request, headers, referer, requestOptions).ConfigureAwait(false)).ConfigureAwait(false);

			if (response == null) {
				return null;
			}

			if (IsSessionExpiredUri(response.FinalUri)) {
				if (await RefreshSession().ConfigureAwait(false)) {
					return await UrlGetToHtmlDocumentWithSession(request, headers, referer, requestOptions, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
				}

				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

				return null;
			}

			// Under special brain-damaged circumstances, Steam might just return our own profile as a response to the request, for absolutely no reason whatsoever - just try again in this case
			if (await IsProfileUri(response.FinalUri).ConfigureAwait(false)) {
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.WarningWorkaroundTriggered, nameof(IsProfileUri)));

				return await UrlGetToHtmlDocumentWithSession(request, headers, referer, requestOptions, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
			}

			return response;
		}

		[PublicAPI]
		public async Task<ObjectResponse<T>?> UrlGetToJsonObjectWithSession<T>(Uri request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, Uri? referer = null, WebBrowser.ERequestOptions requestOptions = WebBrowser.ERequestOptions.None, bool checkSessionPreemptively = true, byte maxTries = WebBrowser.MaxTries) {
			if (request == null) {
				throw new ArgumentNullException(nameof(request));
			}

			if (maxTries == 0) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

				return default(ObjectResponse<T>?);
			}

			if (checkSessionPreemptively) {
				// Check session preemptively as this request might not get redirected to expiration
				bool? sessionExpired = await IsSessionExpired().ConfigureAwait(false);

				if (sessionExpired.GetValueOrDefault(true)) {
					if (await RefreshSession().ConfigureAwait(false)) {
						return await UrlGetToJsonObjectWithSession<T>(request, headers, referer, requestOptions, true, --maxTries).ConfigureAwait(false);
					}

					Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
					Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

					return null;
				}
			} else {
				// If session refresh is already in progress, just wait for it
				await SessionSemaphore.WaitAsync().ConfigureAwait(false);
				SessionSemaphore.Release();
			}

			if (!Initialized) {
				byte connectionTimeout = ASF.GlobalConfig?.ConnectionTimeout ?? GlobalConfig.DefaultConnectionTimeout;

				for (byte i = 0; (i < connectionTimeout) && !Initialized && Bot.IsConnectedAndLoggedOn; i++) {
					await Task.Delay(1000).ConfigureAwait(false);
				}

				if (!Initialized) {
					Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
					Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

					return default(ObjectResponse<T>?);
				}
			}

			Uri host = new(request.GetLeftPart(UriPartial.Authority));

			ObjectResponse<T>? response = await WebLimitRequest(host, async () => await WebBrowser.UrlGetToJsonObject<T>(request, headers, referer, requestOptions).ConfigureAwait(false)).ConfigureAwait(false);

			if (response == null) {
				return default(ObjectResponse<T>?);
			}

			if (IsSessionExpiredUri(response.FinalUri)) {
				if (await RefreshSession().ConfigureAwait(false)) {
					return await UrlGetToJsonObjectWithSession<T>(request, headers, referer, requestOptions, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
				}

				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

				return null;
			}

			// Under special brain-damaged circumstances, Steam might just return our own profile as a response to the request, for absolutely no reason whatsoever - just try again in this case
			if (await IsProfileUri(response.FinalUri).ConfigureAwait(false)) {
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.WarningWorkaroundTriggered, nameof(IsProfileUri)));

				return await UrlGetToJsonObjectWithSession<T>(request, headers, referer, requestOptions, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
			}

			return response;
		}

		[Obsolete("ASF no longer uses any XML-related functions, re-implement it yourself if needed.")]
		[PublicAPI]
		public async Task<XmlDocumentResponse?> UrlGetToXmlDocumentWithSession(Uri request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, Uri? referer = null, WebBrowser.ERequestOptions requestOptions = WebBrowser.ERequestOptions.None, bool checkSessionPreemptively = true, byte maxTries = WebBrowser.MaxTries) {
			if (request == null) {
				throw new ArgumentNullException(nameof(request));
			}

			if (maxTries == 0) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

				return null;
			}

			if (checkSessionPreemptively) {
				// Check session preemptively as this request might not get redirected to expiration
				bool? sessionExpired = await IsSessionExpired().ConfigureAwait(false);

				if (sessionExpired.GetValueOrDefault(true)) {
					if (await RefreshSession().ConfigureAwait(false)) {
						return await UrlGetToXmlDocumentWithSession(request, headers, referer, requestOptions, true, --maxTries).ConfigureAwait(false);
					}

					Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
					Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

					return null;
				}
			} else {
				// If session refresh is already in progress, just wait for it
				await SessionSemaphore.WaitAsync().ConfigureAwait(false);
				SessionSemaphore.Release();
			}

			if (!Initialized) {
				byte connectionTimeout = ASF.GlobalConfig?.ConnectionTimeout ?? GlobalConfig.DefaultConnectionTimeout;

				for (byte i = 0; (i < connectionTimeout) && !Initialized && Bot.IsConnectedAndLoggedOn; i++) {
					await Task.Delay(1000).ConfigureAwait(false);
				}

				if (!Initialized) {
					Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
					Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

					return null;
				}
			}

			Uri host = new(request.GetLeftPart(UriPartial.Authority));

			XmlDocumentResponse? response = await WebLimitRequest(host, async () => await WebBrowser.UrlGetToXmlDocument(request, headers, referer, requestOptions).ConfigureAwait(false)).ConfigureAwait(false);

			if (response == null) {
				return null;
			}

			if (IsSessionExpiredUri(response.FinalUri)) {
				if (await RefreshSession().ConfigureAwait(false)) {
					return await UrlGetToXmlDocumentWithSession(request, headers, referer, requestOptions, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
				}

				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

				return null;
			}

			// Under special brain-damaged circumstances, Steam might just return our own profile as a response to the request, for absolutely no reason whatsoever - just try again in this case
			if (await IsProfileUri(response.FinalUri).ConfigureAwait(false)) {
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.WarningWorkaroundTriggered, nameof(IsProfileUri)));

				return await UrlGetToXmlDocumentWithSession(request, headers, referer, requestOptions, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
			}

			return response;
		}

		[PublicAPI]
		public async Task<bool> UrlHeadWithSession(Uri request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, Uri? referer = null, WebBrowser.ERequestOptions requestOptions = WebBrowser.ERequestOptions.None, bool checkSessionPreemptively = true, byte maxTries = WebBrowser.MaxTries) {
			if (request == null) {
				throw new ArgumentNullException(nameof(request));
			}

			if (maxTries == 0) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

				return false;
			}

			if (checkSessionPreemptively) {
				// Check session preemptively as this request might not get redirected to expiration
				bool? sessionExpired = await IsSessionExpired().ConfigureAwait(false);

				if (sessionExpired.GetValueOrDefault(true)) {
					if (await RefreshSession().ConfigureAwait(false)) {
						return await UrlHeadWithSession(request, headers, referer, requestOptions, true, --maxTries).ConfigureAwait(false);
					}

					Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
					Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

					return false;
				}
			} else {
				// If session refresh is already in progress, just wait for it
				await SessionSemaphore.WaitAsync().ConfigureAwait(false);
				SessionSemaphore.Release();
			}

			if (!Initialized) {
				byte connectionTimeout = ASF.GlobalConfig?.ConnectionTimeout ?? GlobalConfig.DefaultConnectionTimeout;

				for (byte i = 0; (i < connectionTimeout) && !Initialized && Bot.IsConnectedAndLoggedOn; i++) {
					await Task.Delay(1000).ConfigureAwait(false);
				}

				if (!Initialized) {
					Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
					Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

					return false;
				}
			}

			Uri host = new(request.GetLeftPart(UriPartial.Authority));

			BasicResponse? response = await WebLimitRequest(host, async () => await WebBrowser.UrlHead(request, headers, referer, requestOptions).ConfigureAwait(false)).ConfigureAwait(false);

			if (response == null) {
				return false;
			}

			if (IsSessionExpiredUri(response.FinalUri)) {
				if (await RefreshSession().ConfigureAwait(false)) {
					return await UrlHeadWithSession(request, headers, referer, requestOptions, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
				}

				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

				return false;
			}

			// Under special brain-damaged circumstances, Steam might just return our own profile as a response to the request, for absolutely no reason whatsoever - just try again in this case
			if (await IsProfileUri(response.FinalUri).ConfigureAwait(false)) {
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.WarningWorkaroundTriggered, nameof(IsProfileUri)));

				return await UrlHeadWithSession(request, headers, referer, requestOptions, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
			}

			return true;
		}

		[PublicAPI]
		public async Task<HtmlDocumentResponse?> UrlPostToHtmlDocumentWithSession(Uri request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, IDictionary<string, string>? data = null, Uri? referer = null, WebBrowser.ERequestOptions requestOptions = WebBrowser.ERequestOptions.None, ESession session = ESession.Lowercase, bool checkSessionPreemptively = true, byte maxTries = WebBrowser.MaxTries) {
			if (request == null) {
				throw new ArgumentNullException(nameof(request));
			}

			if (!Enum.IsDefined(typeof(ESession), session)) {
				throw new InvalidEnumArgumentException(nameof(session), (int) session, typeof(ESession));
			}

			if (maxTries == 0) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

				return null;
			}

			if (checkSessionPreemptively) {
				// Check session preemptively as this request might not get redirected to expiration
				bool? sessionExpired = await IsSessionExpired().ConfigureAwait(false);

				if (sessionExpired.GetValueOrDefault(true)) {
					if (await RefreshSession().ConfigureAwait(false)) {
						return await UrlPostToHtmlDocumentWithSession(request, headers, data, referer, requestOptions, session, true, --maxTries).ConfigureAwait(false);
					}

					Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
					Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

					return null;
				}
			} else {
				// If session refresh is already in progress, just wait for it
				await SessionSemaphore.WaitAsync().ConfigureAwait(false);
				SessionSemaphore.Release();
			}

			if (!Initialized) {
				byte connectionTimeout = ASF.GlobalConfig?.ConnectionTimeout ?? GlobalConfig.DefaultConnectionTimeout;

				for (byte i = 0; (i < connectionTimeout) && !Initialized && Bot.IsConnectedAndLoggedOn; i++) {
					await Task.Delay(1000).ConfigureAwait(false);
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
					Bot.ArchiLogger.LogNullError(nameof(sessionID));

					return null;
				}

				string sessionName = session switch {
					ESession.CamelCase => "sessionID",
					ESession.Lowercase => "sessionid",
					ESession.PascalCase => "SessionID",
					_ => throw new ArgumentOutOfRangeException(nameof(session))
				};

				if (data != null) {
					// ReSharper disable once RedundantSuppressNullableWarningExpression - required for .NET Framework
					data[sessionName] = sessionID!;
				} else {
					// ReSharper disable once RedundantSuppressNullableWarningExpression - required for .NET Framework
					data = new Dictionary<string, string>(1, StringComparer.Ordinal) { { sessionName, sessionID! } };
				}
			}

			HtmlDocumentResponse? response = await WebLimitRequest(host, async () => await WebBrowser.UrlPostToHtmlDocument(request, headers, data, referer, requestOptions).ConfigureAwait(false)).ConfigureAwait(false);

			if (response == null) {
				return null;
			}

			if (IsSessionExpiredUri(response.FinalUri)) {
				if (await RefreshSession().ConfigureAwait(false)) {
					return await UrlPostToHtmlDocumentWithSession(request, headers, data, referer, requestOptions, session, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
				}

				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

				return null;
			}

			// Under special brain-damaged circumstances, Steam might just return our own profile as a response to the request, for absolutely no reason whatsoever - just try again in this case
			if (await IsProfileUri(response.FinalUri).ConfigureAwait(false)) {
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.WarningWorkaroundTriggered, nameof(IsProfileUri)));

				return await UrlPostToHtmlDocumentWithSession(request, headers, data, referer, requestOptions, session, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
			}

			return response;
		}

		[PublicAPI]
		public async Task<ObjectResponse<T>?> UrlPostToJsonObjectWithSession<T>(Uri request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, IDictionary<string, string>? data = null, Uri? referer = null, WebBrowser.ERequestOptions requestOptions = WebBrowser.ERequestOptions.None, ESession session = ESession.Lowercase, bool checkSessionPreemptively = true, byte maxTries = WebBrowser.MaxTries) {
			if (request == null) {
				throw new ArgumentNullException(nameof(request));
			}

			if (!Enum.IsDefined(typeof(ESession), session)) {
				throw new InvalidEnumArgumentException(nameof(session), (int) session, typeof(ESession));
			}

			if (maxTries == 0) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

				return null;
			}

			if (checkSessionPreemptively) {
				// Check session preemptively as this request might not get redirected to expiration
				bool? sessionExpired = await IsSessionExpired().ConfigureAwait(false);

				if (sessionExpired.GetValueOrDefault(true)) {
					if (await RefreshSession().ConfigureAwait(false)) {
						return await UrlPostToJsonObjectWithSession<T>(request, headers, data, referer, requestOptions, session, true, --maxTries).ConfigureAwait(false);
					}

					Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
					Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

					return null;
				}
			} else {
				// If session refresh is already in progress, just wait for it
				await SessionSemaphore.WaitAsync().ConfigureAwait(false);
				SessionSemaphore.Release();
			}

			if (!Initialized) {
				byte connectionTimeout = ASF.GlobalConfig?.ConnectionTimeout ?? GlobalConfig.DefaultConnectionTimeout;

				for (byte i = 0; (i < connectionTimeout) && !Initialized && Bot.IsConnectedAndLoggedOn; i++) {
					await Task.Delay(1000).ConfigureAwait(false);
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
					Bot.ArchiLogger.LogNullError(nameof(sessionID));

					return null;
				}

				string sessionName = session switch {
					ESession.CamelCase => "sessionID",
					ESession.Lowercase => "sessionid",
					ESession.PascalCase => "SessionID",
					_ => throw new ArgumentOutOfRangeException(nameof(session))
				};

				if (data != null) {
					// ReSharper disable once RedundantSuppressNullableWarningExpression - required for .NET Framework
					data[sessionName] = sessionID!;
				} else {
					// ReSharper disable once RedundantSuppressNullableWarningExpression - required for .NET Framework
					data = new Dictionary<string, string>(1, StringComparer.Ordinal) { { sessionName, sessionID! } };
				}
			}

			ObjectResponse<T>? response = await WebLimitRequest(host, async () => await WebBrowser.UrlPostToJsonObject<T, IDictionary<string, string>>(request, headers, data, referer, requestOptions).ConfigureAwait(false)).ConfigureAwait(false);

			if (response == null) {
				return null;
			}

			if (IsSessionExpiredUri(response.FinalUri)) {
				if (await RefreshSession().ConfigureAwait(false)) {
					return await UrlPostToJsonObjectWithSession<T>(request, headers, data, referer, requestOptions, session, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
				}

				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

				return null;
			}

			// Under special brain-damaged circumstances, Steam might just return our own profile as a response to the request, for absolutely no reason whatsoever - just try again in this case
			if (await IsProfileUri(response.FinalUri).ConfigureAwait(false)) {
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.WarningWorkaroundTriggered, nameof(IsProfileUri)));

				return await UrlPostToJsonObjectWithSession<T>(request, headers, data, referer, requestOptions, session, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
			}

			return response;
		}

		[PublicAPI]
		public async Task<ObjectResponse<T>?> UrlPostToJsonObjectWithSession<T>(Uri request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, ICollection<KeyValuePair<string, string>>? data = null, Uri? referer = null, WebBrowser.ERequestOptions requestOptions = WebBrowser.ERequestOptions.None, ESession session = ESession.Lowercase, bool checkSessionPreemptively = true, byte maxTries = WebBrowser.MaxTries) {
			if (request == null) {
				throw new ArgumentNullException(nameof(request));
			}

			if (!Enum.IsDefined(typeof(ESession), session)) {
				throw new InvalidEnumArgumentException(nameof(session), (int) session, typeof(ESession));
			}

			if (maxTries == 0) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

				return null;
			}

			if (checkSessionPreemptively) {
				// Check session preemptively as this request might not get redirected to expiration
				bool? sessionExpired = await IsSessionExpired().ConfigureAwait(false);

				if (sessionExpired.GetValueOrDefault(true)) {
					if (await RefreshSession().ConfigureAwait(false)) {
						return await UrlPostToJsonObjectWithSession<T>(request, headers, data, referer, requestOptions, session, true, --maxTries).ConfigureAwait(false);
					}

					Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
					Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

					return null;
				}
			} else {
				// If session refresh is already in progress, just wait for it
				await SessionSemaphore.WaitAsync().ConfigureAwait(false);
				SessionSemaphore.Release();
			}

			if (!Initialized) {
				byte connectionTimeout = ASF.GlobalConfig?.ConnectionTimeout ?? GlobalConfig.DefaultConnectionTimeout;

				for (byte i = 0; (i < connectionTimeout) && !Initialized && Bot.IsConnectedAndLoggedOn; i++) {
					await Task.Delay(1000).ConfigureAwait(false);
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
					Bot.ArchiLogger.LogNullError(nameof(sessionID));

					return null;
				}

				string sessionName = session switch {
					ESession.CamelCase => "sessionID",
					ESession.Lowercase => "sessionid",
					ESession.PascalCase => "SessionID",
					_ => throw new ArgumentOutOfRangeException(nameof(session))
				};

				// ReSharper disable once RedundantSuppressNullableWarningExpression - required for .NET Framework
				KeyValuePair<string, string> sessionValue = new(sessionName, sessionID!);

				if (data != null) {
					data.Remove(sessionValue);
					data.Add(sessionValue);
				} else {
					data = new List<KeyValuePair<string, string>>(1) { sessionValue };
				}
			}

			ObjectResponse<T>? response = await WebLimitRequest(host, async () => await WebBrowser.UrlPostToJsonObject<T, ICollection<KeyValuePair<string, string>>>(request, headers, data, referer, requestOptions).ConfigureAwait(false)).ConfigureAwait(false);

			if (response == null) {
				return null;
			}

			if (IsSessionExpiredUri(response.FinalUri)) {
				if (await RefreshSession().ConfigureAwait(false)) {
					return await UrlPostToJsonObjectWithSession<T>(request, headers, data, referer, requestOptions, session, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
				}

				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

				return null;
			}

			// Under special brain-damaged circumstances, Steam might just return our own profile as a response to the request, for absolutely no reason whatsoever - just try again in this case
			if (await IsProfileUri(response.FinalUri).ConfigureAwait(false)) {
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.WarningWorkaroundTriggered, nameof(IsProfileUri)));

				return await UrlPostToJsonObjectWithSession<T>(request, headers, data, referer, requestOptions, session, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
			}

			return response;
		}

		[PublicAPI]
		public async Task<bool> UrlPostWithSession(Uri request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, IDictionary<string, string>? data = null, Uri? referer = null, WebBrowser.ERequestOptions requestOptions = WebBrowser.ERequestOptions.None, ESession session = ESession.Lowercase, bool checkSessionPreemptively = true, byte maxTries = WebBrowser.MaxTries) {
			if (request == null) {
				throw new ArgumentNullException(nameof(request));
			}

			if (!Enum.IsDefined(typeof(ESession), session)) {
				throw new InvalidEnumArgumentException(nameof(session), (int) session, typeof(ESession));
			}

			if (maxTries == 0) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

				return false;
			}

			if (checkSessionPreemptively) {
				// Check session preemptively as this request might not get redirected to expiration
				bool? sessionExpired = await IsSessionExpired().ConfigureAwait(false);

				if (sessionExpired.GetValueOrDefault(true)) {
					if (await RefreshSession().ConfigureAwait(false)) {
						return await UrlPostWithSession(request, headers, data, referer, requestOptions, session, true, --maxTries).ConfigureAwait(false);
					}

					Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
					Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

					return false;
				}
			} else {
				// If session refresh is already in progress, just wait for it
				await SessionSemaphore.WaitAsync().ConfigureAwait(false);
				SessionSemaphore.Release();
			}

			if (!Initialized) {
				byte connectionTimeout = ASF.GlobalConfig?.ConnectionTimeout ?? GlobalConfig.DefaultConnectionTimeout;

				for (byte i = 0; (i < connectionTimeout) && !Initialized && Bot.IsConnectedAndLoggedOn; i++) {
					await Task.Delay(1000).ConfigureAwait(false);
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
					Bot.ArchiLogger.LogNullError(nameof(sessionID));

					return false;
				}

				string sessionName = session switch {
					ESession.CamelCase => "sessionID",
					ESession.Lowercase => "sessionid",
					ESession.PascalCase => "SessionID",
					_ => throw new ArgumentOutOfRangeException(nameof(session))
				};

				if (data != null) {
					// ReSharper disable once RedundantSuppressNullableWarningExpression - required for .NET Framework
					data[sessionName] = sessionID!;
				} else {
					// ReSharper disable once RedundantSuppressNullableWarningExpression - required for .NET Framework
					data = new Dictionary<string, string>(1, StringComparer.Ordinal) { { sessionName, sessionID! } };
				}
			}

			BasicResponse? response = await WebLimitRequest(host, async () => await WebBrowser.UrlPost(request, headers, data, referer, requestOptions).ConfigureAwait(false)).ConfigureAwait(false);

			if (response == null) {
				return false;
			}

			if (IsSessionExpiredUri(response.FinalUri)) {
				if (await RefreshSession().ConfigureAwait(false)) {
					return await UrlPostWithSession(request, headers, data, referer, requestOptions, session, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
				}

				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

				return false;
			}

			// Under special brain-damaged circumstances, Steam might just return our own profile as a response to the request, for absolutely no reason whatsoever - just try again in this case
			if (await IsProfileUri(response.FinalUri).ConfigureAwait(false)) {
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.WarningWorkaroundTriggered, nameof(IsProfileUri)));

				return await UrlPostWithSession(request, headers, data, referer, requestOptions, session, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
			}

			return true;
		}

		[PublicAPI]
		public static async Task<T> WebLimitRequest<T>(Uri service, Func<Task<T>> function) {
			if (service == null) {
				throw new ArgumentNullException(nameof(service));
			}

			if (function == null) {
				throw new ArgumentNullException(nameof(function));
			}

			if (ASF.RateLimitingSemaphore == null) {
				throw new InvalidOperationException(nameof(ASF.RateLimitingSemaphore));
			}

			if (ASF.WebLimitingSemaphores == null) {
				throw new InvalidOperationException(nameof(ASF.WebLimitingSemaphores));
			}

			ushort webLimiterDelay = ASF.GlobalConfig?.WebLimiterDelay ?? GlobalConfig.DefaultWebLimiterDelay;

			if (webLimiterDelay == 0) {
				return await function().ConfigureAwait(false);
			}

			if (!ASF.WebLimitingSemaphores.TryGetValue(service, out (ICrossProcessSemaphore RateLimitingSemaphore, SemaphoreSlim OpenConnectionsSemaphore) limiters)) {
				ASF.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(service), service));

				limiters.RateLimitingSemaphore = ASF.RateLimitingSemaphore;
			}

			// Sending a request opens a new connection
			await limiters.OpenConnectionsSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				// It also increases number of requests
				await limiters.RateLimitingSemaphore.WaitAsync().ConfigureAwait(false);

				// We release rate-limiter semaphore regardless of our task completion, since we use that one only to guarantee rate-limiting of their creation
				Utilities.InBackground(
					async () => {
						await Task.Delay(webLimiterDelay).ConfigureAwait(false);
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
			if (giftCardID == 0) {
				throw new ArgumentOutOfRangeException(nameof(giftCardID));
			}

			Uri request = new(SteamStoreURL, "/gifts/0/resolvegiftcard");

			// Extra entry for sessionID
			Dictionary<string, string> data = new(3, StringComparer.Ordinal) {
				{ "accept", "1" },
				{ "giftcardid", giftCardID.ToString(CultureInfo.InvariantCulture) }
			};

			ObjectResponse<ResultResponse>? response = await UrlPostToJsonObjectWithSession<ResultResponse>(request, data: data).ConfigureAwait(false);

			if (response == null) {
				return false;
			}

			if (response.Content.Result != EResult.OK) {
				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);

				return false;
			}

			return true;
		}

		internal async Task<(bool Success, bool RequiresMobileConfirmation)> AcceptTradeOffer(ulong tradeID) {
			if (tradeID == 0) {
				throw new ArgumentOutOfRangeException(nameof(tradeID));
			}

			Uri request = new(SteamCommunityURL, "/tradeoffer/" + tradeID + "/accept");
			Uri referer = new(SteamCommunityURL, "/tradeoffer/" + tradeID);

			// Extra entry for sessionID
			Dictionary<string, string> data = new(3, StringComparer.Ordinal) {
				{ "serverid", "1" },
				{ "tradeofferid", tradeID.ToString(CultureInfo.InvariantCulture) }
			};

			ObjectResponse<TradeOfferAcceptResponse>? response = null;

			for (byte i = 0; (i < WebBrowser.MaxTries) && (response == null); i++) {
				response = await UrlPostToJsonObjectWithSession<TradeOfferAcceptResponse>(request, data: data, referer: referer, requestOptions: WebBrowser.ERequestOptions.ReturnServerErrors).ConfigureAwait(false);

				if (response == null) {
					return (false, false);
				}

				if (response.StatusCode.IsServerErrorCode()) {
					if (string.IsNullOrEmpty(response.Content.ErrorText)) {
						// This is a generic server error without a reason, try again
						response = null;

						continue;
					}

					// This is actually client error with a reason, so it doesn't make sense to retry
					Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, response.Content.ErrorText));

					return (false, false);
				}
			}

			return response != null ? (true, response.Content.RequiresMobileConfirmation) : (false, false);
		}

		internal async Task<bool> AddFreeLicense(uint subID) {
			if (subID == 0) {
				throw new ArgumentOutOfRangeException(nameof(subID));
			}

			Uri request = new(SteamStoreURL, "/checkout/addfreelicense");

			// Extra entry for sessionID
			Dictionary<string, string> data = new(3, StringComparer.Ordinal) {
				{ "action", "add_to_cart" },
				{ "subid", subID.ToString(CultureInfo.InvariantCulture) }
			};

			using HtmlDocumentResponse? response = await UrlPostToHtmlDocumentWithSession(request, data: data).ConfigureAwait(false);

			return response?.Content.SelectSingleNode("//div[@class='add_free_content_success_area']") != null;
		}

		internal async Task<bool> ChangePrivacySettings(UserPrivacy userPrivacy) {
			if (userPrivacy == null) {
				throw new ArgumentNullException(nameof(userPrivacy));
			}

			string? profileURL = await GetAbsoluteProfileURL().ConfigureAwait(false);

			if (string.IsNullOrEmpty(profileURL)) {
				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);

				return false;
			}

			Uri request = new(SteamCommunityURL, profileURL + "/ajaxsetprivacy");

			// Extra entry for sessionID
			Dictionary<string, string> data = new(3, StringComparer.Ordinal) {
				{ "eCommentPermission", ((byte) userPrivacy.CommentPermission).ToString(CultureInfo.InvariantCulture) },
				{ "Privacy", JsonConvert.SerializeObject(userPrivacy.Settings) }
			};

			ObjectResponse<ResultResponse>? response = await UrlPostToJsonObjectWithSession<ResultResponse>(request, data: data).ConfigureAwait(false);

			if (response == null) {
				return false;
			}

			if (response.Content.Result != EResult.OK) {
				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);

				return false;
			}

			return true;
		}

		internal async Task<bool> ClearFromDiscoveryQueue(uint appID) {
			if (appID == 0) {
				throw new ArgumentOutOfRangeException(nameof(appID));
			}

			Uri request = new(SteamStoreURL, "/app/" + appID);

			// Extra entry for sessionID
			Dictionary<string, string> data = new(2, StringComparer.Ordinal) { { "appid_to_clear_from_queue", appID.ToString(CultureInfo.InvariantCulture) } };

			return await UrlPostWithSession(request, data: data).ConfigureAwait(false);
		}

		internal async Task<bool> DeclineTradeOffer(ulong tradeID) {
			if (tradeID == 0) {
				throw new ArgumentOutOfRangeException(nameof(tradeID));
			}

			(bool success, string? steamApiKey) = await CachedApiKey.GetValue().ConfigureAwait(false);

			if (!success || string.IsNullOrEmpty(steamApiKey)) {
				return false;
			}

			// Extra entry for format
			Dictionary<string, object> arguments = new(3, StringComparer.Ordinal) {
				// ReSharper disable once RedundantSuppressNullableWarningExpression - required for .NET Framework
				{ "key", steamApiKey! },

				{ "tradeofferid", tradeID }
			};

			KeyValue? response = null;

			for (byte i = 0; (i < WebBrowser.MaxTries) && (response == null); i++) {
				using WebAPI.AsyncInterface econService = Bot.SteamConfiguration.GetAsyncWebAPIInterface(EconService);

				econService.Timeout = WebBrowser.Timeout;

				try {
					response = await WebLimitRequest(
						WebAPI.DefaultBaseAddress,

						// ReSharper disable once AccessToDisposedClosure
						async () => await econService.CallAsync(HttpMethod.Post, "DeclineTradeOffer", args: arguments).ConfigureAwait(false)
					).ConfigureAwait(false);
				} catch (TaskCanceledException e) {
					Bot.ArchiLogger.LogGenericDebuggingException(e);
				} catch (Exception e) {
					Bot.ArchiLogger.LogGenericWarningException(e);
				}
			}

			if (response == null) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));

				return false;
			}

			return true;
		}

		internal HttpClient GenerateDisposableHttpClient() => WebBrowser.GenerateDisposableHttpClient();

		internal async Task<ImmutableHashSet<uint>?> GenerateNewDiscoveryQueue() {
			Uri request = new(SteamStoreURL, "/explore/generatenewdiscoveryqueue");

			// Extra entry for sessionID
			Dictionary<string, string> data = new(2, StringComparer.Ordinal) { { "queuetype", "0" } };

			ObjectResponse<NewDiscoveryQueueResponse>? response = await UrlPostToJsonObjectWithSession<NewDiscoveryQueueResponse>(request, data: data).ConfigureAwait(false);

			return response?.Content.Queue;
		}

		internal async Task<HashSet<TradeOffer>?> GetActiveTradeOffers() {
			(bool success, string? steamApiKey) = await CachedApiKey.GetValue().ConfigureAwait(false);

			if (!success || string.IsNullOrEmpty(steamApiKey)) {
				return null;
			}

			// Extra entry for format
			Dictionary<string, object> arguments = new(6, StringComparer.Ordinal) {
				{ "active_only", 1 },
				{ "get_descriptions", 1 },
				{ "get_received_offers", 1 },

				// ReSharper disable once RedundantSuppressNullableWarningExpression - required for .NET Framework
				{ "key", steamApiKey! },

				{ "time_historical_cutoff", uint.MaxValue }
			};

			KeyValue? response = null;

			for (byte i = 0; (i < WebBrowser.MaxTries) && (response == null); i++) {
				using WebAPI.AsyncInterface econService = Bot.SteamConfiguration.GetAsyncWebAPIInterface(EconService);

				econService.Timeout = WebBrowser.Timeout;

				try {
					response = await WebLimitRequest(
						WebAPI.DefaultBaseAddress,

						// ReSharper disable once AccessToDisposedClosure
						async () => await econService.CallAsync(HttpMethod.Get, "GetTradeOffers", args: arguments).ConfigureAwait(false)
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

			Dictionary<(uint AppID, ulong ClassID, ulong InstanceID), InventoryResponse.Description> descriptions = new();

			foreach (KeyValue description in response["descriptions"].Children) {
				uint appID = description["appid"].AsUnsignedInteger();

				if (appID == 0) {
					Bot.ArchiLogger.LogNullError(nameof(appID));

					return null;
				}

				ulong classID = description["classid"].AsUnsignedLong();

				if (classID == 0) {
					Bot.ArchiLogger.LogNullError(nameof(classID));

					return null;
				}

				ulong instanceID = description["instanceid"].AsUnsignedLong();

				(uint AppID, ulong ClassID, ulong InstanceID) key = (appID, classID, instanceID);

				if (descriptions.ContainsKey(key)) {
					continue;
				}

				InventoryResponse.Description parsedDescription = new() {
					AppID = appID,
					ClassID = classID,
					InstanceID = instanceID,
					Marketable = description["marketable"].AsBoolean(),
					Tradable = true // We're parsing active trade offers, we can assume as much
				};

				List<KeyValue> tags = description["tags"].Children;

				if (tags.Count > 0) {
					HashSet<Tag> parsedTags = new(tags.Count);

					foreach (KeyValue tag in tags) {
						string? identifier = tag["category"].AsString();

						if (string.IsNullOrEmpty(identifier)) {
							Bot.ArchiLogger.LogNullError(nameof(identifier));

							return null;
						}

						string? value = tag["internal_name"].AsString();

						// Apparently, name can be empty, but not null
						if (value == null) {
							Bot.ArchiLogger.LogNullError(nameof(value));

							return null;
						}

						// ReSharper disable once RedundantSuppressNullableWarningExpression - required for .NET Framework
						parsedTags.Add(new Tag(identifier!, value));
					}

					parsedDescription.Tags = parsedTags.ToImmutableHashSet();
				}

				descriptions[key] = parsedDescription;
			}

			HashSet<TradeOffer> result = new();

			foreach (KeyValue trade in response["trade_offers_received"].Children) {
				ETradeOfferState state = trade["trade_offer_state"].AsEnum<ETradeOfferState>();

				if (!Enum.IsDefined(typeof(ETradeOfferState), state)) {
					Bot.ArchiLogger.LogNullError(nameof(state));

					return null;
				}

				if (state != ETradeOfferState.Active) {
					continue;
				}

				ulong tradeOfferID = trade["tradeofferid"].AsUnsignedLong();

				if (tradeOfferID == 0) {
					Bot.ArchiLogger.LogNullError(nameof(tradeOfferID));

					return null;
				}

				uint otherSteamID3 = trade["accountid_other"].AsUnsignedInteger();

				if (otherSteamID3 == 0) {
					Bot.ArchiLogger.LogNullError(nameof(otherSteamID3));

					return null;
				}

				TradeOffer tradeOffer = new(tradeOfferID, otherSteamID3, state);

				List<KeyValue> itemsToGive = trade["items_to_give"].Children;

				if (itemsToGive.Count > 0) {
					if (!ParseItems(descriptions, itemsToGive, tradeOffer.ItemsToGive)) {
						Bot.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.ErrorParsingObject, nameof(itemsToGive)));

						return null;
					}
				}

				List<KeyValue> itemsToReceive = trade["items_to_receive"].Children;

				if (itemsToReceive.Count > 0) {
					if (!ParseItems(descriptions, itemsToReceive, tradeOffer.ItemsToReceive)) {
						Bot.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.ErrorParsingObject, nameof(itemsToReceive)));

						return null;
					}
				}

				result.Add(tradeOffer);
			}

			return result;
		}

		internal async Task<HashSet<uint>?> GetAppList() {
			KeyValue? response = null;

			for (byte i = 0; (i < WebBrowser.MaxTries) && (response == null); i++) {
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

			foreach (uint appID in apps.Select(app => app["appid"].AsUnsignedInteger())) {
				if (appID == 0) {
					Bot.ArchiLogger.LogNullError(nameof(appID));

					return null;
				}

				result.Add(appID);
			}

			return result;
		}

		internal async Task<IDocument?> GetBadgePage(byte page) {
			if (page == 0) {
				throw new ArgumentOutOfRangeException(nameof(page));
			}

			Uri request = new(SteamCommunityURL, "/my/badges?l=english&p=" + page);

			HtmlDocumentResponse? response = await UrlGetToHtmlDocumentWithSession(request, checkSessionPreemptively: false).ConfigureAwait(false);

			return response?.Content;
		}

		internal async Task<byte> GetCardCountForGame(uint appID) {
			if (appID == 0) {
				throw new ArgumentOutOfRangeException(nameof(appID));
			}

			if (CachedCardCountsForGame.TryGetValue(appID, out byte result)) {
				return result;
			}

			using IDocument? htmlDocument = await GetGameCardsPage(appID).ConfigureAwait(false);

			if (htmlDocument == null) {
				Bot.ArchiLogger.LogNullError(nameof(htmlDocument));

				return 0;
			}

			IEnumerable<IElement> htmlNodes = htmlDocument.SelectNodes("//div[@class='badge_card_set_cards']/div[starts-with(@class, 'badge_card_set_card')]");

			result = (byte) htmlNodes.Count();

			if (result == 0) {
				Bot.ArchiLogger.LogNullError(nameof(result));

				return 0;
			}

			CachedCardCountsForGame.TryAdd(appID, result);

			return result;
		}

		internal async Task<IDocument?> GetConfirmationsPage(string deviceID, string confirmationHash, uint time) {
			if (string.IsNullOrEmpty(deviceID)) {
				throw new ArgumentNullException(nameof(deviceID));
			}

			if (string.IsNullOrEmpty(confirmationHash)) {
				throw new ArgumentNullException(nameof(confirmationHash));
			}

			if (time == 0) {
				throw new ArgumentOutOfRangeException(nameof(time));
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

			Uri request = new(SteamCommunityURL, "/mobileconf/conf?a=" + Bot.SteamID + "&k=" + WebUtility.UrlEncode(confirmationHash) + "&l=english&m=android&p=" + WebUtility.UrlEncode(deviceID) + "&t=" + time + "&tag=conf");

			HtmlDocumentResponse? response = await UrlGetToHtmlDocumentWithSession(request).ConfigureAwait(false);

			return response?.Content;
		}

		internal async Task<HashSet<ulong>?> GetDigitalGiftCards() {
			Uri request = new(SteamStoreURL, "/gifts");

			using HtmlDocumentResponse? response = await UrlGetToHtmlDocumentWithSession(request).ConfigureAwait(false);

			if (response == null) {
				return null;
			}

			IEnumerable<IElement> htmlNodes = response.Content.SelectNodes("//div[@class='pending_gift']/div[starts-with(@id, 'pending_gift_')][count(div[@class='pending_giftcard_leftcol']) > 0]/@id");

			HashSet<ulong> results = new();

			foreach (string? giftCardIDText in htmlNodes.Select(node => node.GetAttribute("id"))) {
				if (string.IsNullOrEmpty(giftCardIDText)) {
					Bot.ArchiLogger.LogNullError(nameof(giftCardIDText));

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

			HtmlDocumentResponse? response = await UrlGetToHtmlDocumentWithSession(request).ConfigureAwait(false);

			return response?.Content;
		}

		internal async Task<HashSet<ulong>?> GetFamilySharingSteamIDs() {
			Uri request = new(SteamStoreURL, "/account/managedevices?l=english");

			using HtmlDocumentResponse? response = await UrlGetToHtmlDocumentWithSession(request).ConfigureAwait(false);

			if (response == null) {
				return null;
			}

			IEnumerable<IElement> htmlNodes = response.Content.SelectNodes("(//table[@class='accountTable'])[2]//a/@data-miniprofile");

			HashSet<ulong> result = new();

			foreach (string? miniProfile in htmlNodes.Select(htmlNode => htmlNode.GetAttribute("data-miniprofile"))) {
				if (string.IsNullOrEmpty(miniProfile)) {
					Bot.ArchiLogger.LogNullError(nameof(miniProfile));

					return null;
				}

				if (!uint.TryParse(miniProfile, out uint steamID3) || (steamID3 == 0)) {
					Bot.ArchiLogger.LogNullError(nameof(steamID3));

					return null;
				}

				ulong steamID = new SteamID(steamID3, EUniverse.Public, EAccountType.Individual);
				result.Add(steamID);
			}

			return result;
		}

		internal async Task<IDocument?> GetGameCardsPage(uint appID) {
			if (appID == 0) {
				throw new ArgumentOutOfRangeException(nameof(appID));
			}

			Uri request = new(SteamCommunityURL, "/my/gamecards/" + appID + "?l=english");

			HtmlDocumentResponse? response = await UrlGetToHtmlDocumentWithSession(request, checkSessionPreemptively: false).ConfigureAwait(false);

			return response?.Content;
		}

		internal async Task<uint> GetServerTime() {
			KeyValue? response = null;

			for (byte i = 0; (i < WebBrowser.MaxTries) && (response == null); i++) {
				using WebAPI.AsyncInterface twoFactorService = Bot.SteamConfiguration.GetAsyncWebAPIInterface(TwoFactorService);

				twoFactorService.Timeout = WebBrowser.Timeout;

				try {
					response = await WebLimitRequest(
						WebAPI.DefaultBaseAddress,

						// ReSharper disable once AccessToDisposedClosure
						async () => await twoFactorService.CallAsync(HttpMethod.Post, "QueryTime").ConfigureAwait(false)
					).ConfigureAwait(false);
				} catch (TaskCanceledException e) {
					Bot.ArchiLogger.LogGenericDebuggingException(e);
				} catch (Exception e) {
					Bot.ArchiLogger.LogGenericWarningException(e);
				}
			}

			if (response == null) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));

				return 0;
			}

			uint result = response["server_time"].AsUnsignedInteger();

			if (result == 0) {
				Bot.ArchiLogger.LogNullError(nameof(result));

				return 0;
			}

			return result;
		}

		internal async Task<byte?> GetTradeHoldDurationForTrade(ulong tradeID) {
			if (tradeID == 0) {
				throw new ArgumentOutOfRangeException(nameof(tradeID));
			}

			Uri request = new(SteamCommunityURL, "/tradeoffer/" + tradeID + "?l=english");

			using HtmlDocumentResponse? response = await UrlGetToHtmlDocumentWithSession(request).ConfigureAwait(false);

			IElement? htmlNode = response?.Content.SelectSingleNode("//div[@class='pagecontent']/script");

			if (htmlNode == null) {
				// Trade can be no longer valid
				return null;
			}

			string text = htmlNode.TextContent;

			if (string.IsNullOrEmpty(text)) {
				Bot.ArchiLogger.LogNullError(nameof(text));

				return null;
			}

			const string daysTheirVariableName = "g_daysTheirEscrow = ";
			int index = text.IndexOf(daysTheirVariableName, StringComparison.Ordinal);

			if (index < 0) {
				Bot.ArchiLogger.LogNullError(nameof(index));

				return null;
			}

			index += daysTheirVariableName.Length;
			text = text[index..];

			index = text.IndexOf(';', StringComparison.Ordinal);

			if (index < 0) {
				Bot.ArchiLogger.LogNullError(nameof(index));

				return null;
			}

			text = text[..index];

			if (!byte.TryParse(text, out byte result)) {
				Bot.ArchiLogger.LogNullError(nameof(result));

				return null;
			}

			return result;
		}

		internal async Task<byte?> GetTradeHoldDurationForUser(ulong steamID, string? tradeToken = null) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
				throw new ArgumentOutOfRangeException(nameof(steamID));
			}

			(bool success, string? steamApiKey) = await CachedApiKey.GetValue().ConfigureAwait(false);

			if (!success || string.IsNullOrEmpty(steamApiKey)) {
				return null;
			}

			// Extra entry for format
			Dictionary<string, object> arguments = new(!string.IsNullOrEmpty(tradeToken) ? 4 : 3, StringComparer.Ordinal) {
				// ReSharper disable once RedundantSuppressNullableWarningExpression - required for .NET Framework
				{ "key", steamApiKey! },

				{ "steamid_target", steamID }
			};

			if (!string.IsNullOrEmpty(tradeToken)) {
				// ReSharper disable once RedundantSuppressNullableWarningExpression - required for .NET Framework
				arguments["trade_offer_access_token"] = tradeToken!;
			}

			KeyValue? response = null;

			for (byte i = 0; (i < WebBrowser.MaxTries) && (response == null); i++) {
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

			uint resultInSeconds = response["their_escrow"]["escrow_end_duration_seconds"].AsUnsignedInteger(uint.MaxValue);

			if (resultInSeconds == uint.MaxValue) {
				Bot.ArchiLogger.LogNullError(nameof(resultInSeconds));

				return null;
			}

			return resultInSeconds == 0 ? (byte) 0 : (byte) (resultInSeconds / 86400);
		}

		internal async Task<bool?> HandleConfirmation(string deviceID, string confirmationHash, uint time, ulong confirmationID, ulong confirmationKey, bool accept) {
			if (string.IsNullOrEmpty(deviceID)) {
				throw new ArgumentNullException(nameof(deviceID));
			}

			if (string.IsNullOrEmpty(confirmationHash)) {
				throw new ArgumentNullException(nameof(confirmationHash));
			}

			if (time == 0) {
				throw new ArgumentOutOfRangeException(nameof(time));
			}

			if (confirmationID == 0) {
				throw new ArgumentOutOfRangeException(nameof(confirmationID));
			}

			if (confirmationKey == 0) {
				throw new ArgumentOutOfRangeException(nameof(confirmationKey));
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

			Uri request = new(SteamCommunityURL, "/mobileconf/ajaxop?a=" + Bot.SteamID + "&cid=" + confirmationID + "&ck=" + confirmationKey + "&k=" + WebUtility.UrlEncode(confirmationHash) + "&l=english&m=android&op=" + (accept ? "allow" : "cancel") + "&p=" + WebUtility.UrlEncode(deviceID) + "&t=" + time + "&tag=conf");

			ObjectResponse<BooleanResponse>? response = await UrlGetToJsonObjectWithSession<BooleanResponse>(request).ConfigureAwait(false);

			return response?.Content.Success;
		}

		internal async Task<bool?> HandleConfirmations(string deviceID, string confirmationHash, uint time, IReadOnlyCollection<Confirmation> confirmations, bool accept) {
			if (string.IsNullOrEmpty(deviceID)) {
				throw new ArgumentNullException(nameof(deviceID));
			}

			if (string.IsNullOrEmpty(confirmationHash)) {
				throw new ArgumentNullException(nameof(confirmationHash));
			}

			if (time == 0) {
				throw new ArgumentOutOfRangeException(nameof(time));
			}

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
				new KeyValuePair<string, string>("m", "android"),
				new KeyValuePair<string, string>("op", accept ? "allow" : "cancel"),
				new KeyValuePair<string, string>("p", deviceID),
				new KeyValuePair<string, string>("t", time.ToString(CultureInfo.InvariantCulture)),
				new KeyValuePair<string, string>("tag", "conf")
			};

			foreach (Confirmation confirmation in confirmations) {
				data.Add(new KeyValuePair<string, string>("cid[]", confirmation.ID.ToString(CultureInfo.InvariantCulture)));
				data.Add(new KeyValuePair<string, string>("ck[]", confirmation.Key.ToString(CultureInfo.InvariantCulture)));
			}

			ObjectResponse<BooleanResponse>? response = await UrlPostToJsonObjectWithSession<BooleanResponse>(request, data: data).ConfigureAwait(false);

			return response?.Content.Success;
		}

		internal async Task<bool> Init(ulong steamID, EUniverse universe, string webAPIUserNonce, string? parentalCode = null) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
				throw new ArgumentOutOfRangeException(nameof(steamID));
			}

			if ((universe == EUniverse.Invalid) || !Enum.IsDefined(typeof(EUniverse), universe)) {
				throw new InvalidEnumArgumentException(nameof(universe), (int) universe, typeof(EUniverse));
			}

			if (string.IsNullOrEmpty(webAPIUserNonce)) {
				throw new ArgumentNullException(nameof(webAPIUserNonce));
			}

			byte[]? publicKey = KeyDictionary.GetPublicKey(universe);

			if ((publicKey == null) || (publicKey.Length == 0)) {
				Bot.ArchiLogger.LogNullError(nameof(publicKey));

				return false;
			}

			// Generate a random 32-byte session key
			byte[] sessionKey = CryptoHelper.GenerateRandomBlock(32);

			// RSA encrypt our session key with the public key for the universe we're on
			byte[] encryptedSessionKey;

			using (RSACrypto rsa = new(publicKey)) {
				encryptedSessionKey = rsa.Encrypt(sessionKey);
			}

			// Generate login key from the user nonce that we've received from Steam network
			byte[] loginKey = Encoding.UTF8.GetBytes(webAPIUserNonce);

			// AES encrypt our login key with our session key
			byte[] encryptedLoginKey = CryptoHelper.SymmetricEncrypt(loginKey, sessionKey);

			// Extra entry for format
			Dictionary<string, object> arguments = new(4, StringComparer.Ordinal) {
				{ "encrypted_loginkey", encryptedLoginKey },
				{ "sessionkey", encryptedSessionKey },
				{ "steamid", steamID }
			};

			// We're now ready to send the data to Steam API
			Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.LoggingIn, SteamUserAuthService));

			KeyValue? response;

			// We do not use usual retry pattern here as webAPIUserNonce is valid only for a single request
			// Even during timeout, webAPIUserNonce is most likely already invalid
			// Instead, the caller is supposed to ask for new webAPIUserNonce and call Init() again on failure
			using (WebAPI.AsyncInterface steamUserAuthService = Bot.SteamConfiguration.GetAsyncWebAPIInterface(SteamUserAuthService)) {
				steamUserAuthService.Timeout = WebBrowser.Timeout;

				try {
					response = await WebLimitRequest(
						WebAPI.DefaultBaseAddress,

						// ReSharper disable once AccessToDisposedClosure
						async () => await steamUserAuthService.CallAsync(HttpMethod.Post, "AuthenticateUser", args: arguments).ConfigureAwait(false)
					).ConfigureAwait(false);
				} catch (TaskCanceledException e) {
					Bot.ArchiLogger.LogGenericDebuggingException(e);

					return false;
				} catch (Exception e) {
					Bot.ArchiLogger.LogGenericWarningException(e);

					return false;
				}
			}

			string? steamLogin = response["token"].AsString();

			if (string.IsNullOrEmpty(steamLogin)) {
				Bot.ArchiLogger.LogNullError(nameof(steamLogin));

				return false;
			}

			string? steamLoginSecure = response["tokensecure"].AsString();

			if (string.IsNullOrEmpty(steamLoginSecure)) {
				Bot.ArchiLogger.LogNullError(nameof(steamLoginSecure));

				return false;
			}

			string sessionID = Convert.ToBase64String(Encoding.UTF8.GetBytes(steamID.ToString(CultureInfo.InvariantCulture)));

			WebBrowser.CookieContainer.Add(new Cookie("sessionid", sessionID, "/", "." + SteamCommunityURL.Host));
			WebBrowser.CookieContainer.Add(new Cookie("sessionid", sessionID, "/", "." + SteamHelpURL.Host));
			WebBrowser.CookieContainer.Add(new Cookie("sessionid", sessionID, "/", "." + SteamStoreURL.Host));

			WebBrowser.CookieContainer.Add(new Cookie("steamLogin", steamLogin, "/", "." + SteamCommunityURL.Host));
			WebBrowser.CookieContainer.Add(new Cookie("steamLogin", steamLogin, "/", "." + SteamHelpURL.Host));
			WebBrowser.CookieContainer.Add(new Cookie("steamLogin", steamLogin, "/", "." + SteamStoreURL.Host));

			WebBrowser.CookieContainer.Add(new Cookie("steamLoginSecure", steamLoginSecure, "/", "." + SteamCommunityURL.Host));
			WebBrowser.CookieContainer.Add(new Cookie("steamLoginSecure", steamLoginSecure, "/", "." + SteamHelpURL.Host));
			WebBrowser.CookieContainer.Add(new Cookie("steamLoginSecure", steamLoginSecure, "/", "." + SteamStoreURL.Host));

			// Report proper time when doing timezone-based calculations, see setTimezoneCookies() from https://steamcommunity-a.akamaihd.net/public/shared/javascript/shared_global.js
			string timeZoneOffset = DateTimeOffset.Now.Offset.TotalSeconds + WebUtility.UrlEncode(",") + "0";

			WebBrowser.CookieContainer.Add(new Cookie("timezoneOffset", timeZoneOffset, "/", "." + SteamCommunityURL.Host));
			WebBrowser.CookieContainer.Add(new Cookie("timezoneOffset", timeZoneOffset, "/", "." + SteamHelpURL.Host));
			WebBrowser.CookieContainer.Add(new Cookie("timezoneOffset", timeZoneOffset, "/", "." + SteamStoreURL.Host));

			Bot.ArchiLogger.LogGenericInfo(Strings.Success);

			// Unlock Steam Parental if needed
			if (parentalCode?.Length == BotConfig.SteamParentalCodeLength) {
				if (!await UnlockParentalAccount(parentalCode).ConfigureAwait(false)) {
					return false;
				}
			}

			LastSessionCheck = LastSessionRefresh = DateTime.UtcNow;
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

			await ASF.InventorySemaphore.WaitAsync().ConfigureAwait(false);

			try {
				lock (ASF.InventorySemaphore) {
					MarkingInventoryScheduled = false;
				}

				Uri request = new(SteamCommunityURL, "/my/inventory");

				await UrlHeadWithSession(request, checkSessionPreemptively: false).ConfigureAwait(false);
			} finally {
				byte inventoryLimiterDelay = ASF.GlobalConfig?.InventoryLimiterDelay ?? GlobalConfig.DefaultInventoryLimiterDelay;

				if (inventoryLimiterDelay == 0) {
					ASF.InventorySemaphore.Release();
				} else {
					Utilities.InBackground(
						async () => {
							await Task.Delay(inventoryLimiterDelay * 1000).ConfigureAwait(false);
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

		internal void OnDisconnected() {
			Initialized = false;
			Utilities.InBackground(CachedApiKey.Reset);
		}

		internal void OnVanityURLChanged(string? vanityURL = null) => VanityURL = !string.IsNullOrEmpty(vanityURL) ? vanityURL : null;

		internal async Task<(EResult Result, EPurchaseResultDetail? PurchaseResult)?> RedeemWalletKey(string key) {
			if (string.IsNullOrEmpty(key)) {
				throw new ArgumentNullException(nameof(key));
			}

			// ASF should redeem wallet key only in case of existing wallet
			if (Bot.WalletCurrency == ECurrencyCode.Invalid) {
				Bot.ArchiLogger.LogNullError(nameof(Bot.WalletCurrency));

				return null;
			}

			Uri request = new(SteamStoreURL, "/account/ajaxredeemwalletcode");

			// Extra entry for sessionID
			Dictionary<string, string> data = new(2, StringComparer.Ordinal) { { "wallet_code", key } };

			ObjectResponse<RedeemWalletResponse>? response = await UrlPostToJsonObjectWithSession<RedeemWalletResponse>(request, data: data).ConfigureAwait(false);

			if (response == null) {
				return null;
			}

			// We can not trust EResult response, because it is OK even in the case of error, so changing it to Fail in this case
			if ((response.Content.Result != EResult.OK) || (response.Content.PurchaseResultDetail != EPurchaseResultDetail.NoDetail)) {
				return (response.Content.Result == EResult.OK ? EResult.Fail : response.Content.Result, response.Content.PurchaseResultDetail);
			}

			return (EResult.OK, EPurchaseResultDetail.NoDetail);
		}

		internal async Task<bool> UnpackBooster(uint appID, ulong itemID) {
			if (appID == 0) {
				throw new ArgumentOutOfRangeException(nameof(appID));
			}

			if (itemID == 0) {
				throw new ArgumentOutOfRangeException(nameof(itemID));
			}

			string? profileURL = await GetAbsoluteProfileURL().ConfigureAwait(false);

			if (string.IsNullOrEmpty(profileURL)) {
				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);

				return false;
			}

			Uri request = new(SteamCommunityURL, profileURL + "/ajaxunpackbooster");

			// Extra entry for sessionID
			Dictionary<string, string> data = new(3, StringComparer.Ordinal) {
				{ "appid", appID.ToString(CultureInfo.InvariantCulture) },
				{ "communityitemid", itemID.ToString(CultureInfo.InvariantCulture) }
			};

			ObjectResponse<ResultResponse>? response = await UrlPostToJsonObjectWithSession<ResultResponse>(request, data: data).ConfigureAwait(false);

			return response?.Content.Result == EResult.OK;
		}

		private async Task<(ESteamApiKeyState State, string? Key)> GetApiKeyState() {
			Uri request = new(SteamCommunityURL, "/dev/apikey?l=english");

			using HtmlDocumentResponse? response = await UrlGetToHtmlDocumentWithSession(request).ConfigureAwait(false);

			if (response == null) {
				return (ESteamApiKeyState.Timeout, null);
			}

			IElement? titleNode = response.Content.SelectSingleNode("//div[@id='mainContents']/h2");

			if (titleNode == null) {
				Bot.ArchiLogger.LogNullError(nameof(titleNode));

				return (ESteamApiKeyState.Error, null);
			}

			string title = titleNode.TextContent;

			if (string.IsNullOrEmpty(title)) {
				Bot.ArchiLogger.LogNullError(nameof(title));

				return (ESteamApiKeyState.Error, null);
			}

			if (title.Contains("Access Denied", StringComparison.OrdinalIgnoreCase) || title.Contains("Validated email address required", StringComparison.OrdinalIgnoreCase)) {
				return (ESteamApiKeyState.AccessDenied, null);
			}

			IElement? htmlNode = response.Content.SelectSingleNode("//div[@id='bodyContents_ex']/p");

			if (htmlNode == null) {
				Bot.ArchiLogger.LogNullError(nameof(htmlNode));

				return (ESteamApiKeyState.Error, null);
			}

			string text = htmlNode.TextContent;

			if (string.IsNullOrEmpty(text)) {
				Bot.ArchiLogger.LogNullError(nameof(text));

				return (ESteamApiKeyState.Error, null);
			}

			if (text.Contains("Registering for a Steam Web API Key", StringComparison.OrdinalIgnoreCase)) {
				return (ESteamApiKeyState.NotRegisteredYet, null);
			}

			int keyIndex = text.IndexOf("Key: ", StringComparison.Ordinal);

			if (keyIndex < 0) {
				Bot.ArchiLogger.LogNullError(nameof(keyIndex));

				return (ESteamApiKeyState.Error, null);
			}

			keyIndex += 5;

			if (text.Length <= keyIndex) {
				Bot.ArchiLogger.LogNullError(nameof(text));

				return (ESteamApiKeyState.Error, null);
			}

			text = text[keyIndex..];

			if ((text.Length != 32) || !Utilities.IsValidHexadecimalText(text)) {
				Bot.ArchiLogger.LogNullError(nameof(text));

				return (ESteamApiKeyState.Error, null);
			}

			return (ESteamApiKeyState.Registered, text);
		}

		private async Task<bool> IsProfileUri(Uri uri, bool waitForInitialization = true) {
			if (uri == null) {
				throw new ArgumentNullException(nameof(uri));
			}

			string? profileURL = await GetAbsoluteProfileURL(waitForInitialization).ConfigureAwait(false);

			if (string.IsNullOrEmpty(profileURL)) {
				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);

				return false;
			}

			return uri.AbsolutePath.Equals(profileURL, StringComparison.OrdinalIgnoreCase);
		}

		private async Task<bool?> IsSessionExpired() {
			DateTime triggeredAt = DateTime.UtcNow;

			if (triggeredAt <= LastSessionCheck) {
				return LastSessionCheck != LastSessionRefresh;
			}

			await SessionSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				if (triggeredAt <= LastSessionCheck) {
					return LastSessionCheck != LastSessionRefresh;
				}

				// Choosing proper URL to check against is actually much harder than it initially looks like, we must abide by several rules to make this function as lightweight and reliable as possible
				// We should prefer to use Steam store, as the community is much more unstable and broken, plus majority of our requests get there anyway, so load-balancing with store makes much more sense. It also has a higher priority than the community, so all eventual issues should be fixed there first
				// The URL must be fast enough to render, as this function will be called reasonably often, and every extra delay adds up. We're already making our best effort by using HEAD request, but the URL itself plays a very important role as well
				// The page should have as little internal dependencies as possible, since every extra chunk increases likelihood of broken functionality. We can only make a guess here based on the amount of content that the page returns to us
				// It should also be URL with fairly fixed address that isn't going to disappear anytime soon, preferably something staple that is a dependency of other requests, so it's very unlikely to change in a way that would add overhead in the future
				// Lastly, it should be a request that is preferably generic enough as a routine check, not something specialized and targetted, to make it very clear that we're just checking if session is up, and to further aid internal dependencies specified above by rendering as general Steam info as possible
				Uri request = new(SteamStoreURL, "/account");

				BasicResponse? response = await WebLimitRequest(SteamStoreURL, async () => await WebBrowser.UrlHead(request).ConfigureAwait(false)).ConfigureAwait(false);

				if (response == null) {
					return null;
				}

				bool result = IsSessionExpiredUri(response.FinalUri);

				DateTime now = DateTime.UtcNow;

				if (result) {
					Initialized = false;
				} else {
					LastSessionRefresh = now;
				}

				LastSessionCheck = now;

				return result;
			} finally {
				SessionSemaphore.Release();
			}
		}

		private static bool IsSessionExpiredUri(Uri uri) {
			if (uri == null) {
				throw new ArgumentNullException(nameof(uri));
			}

			return uri.AbsolutePath.StartsWith("/login", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("lostauth", StringComparison.OrdinalIgnoreCase);
		}

		private static bool ParseItems(IReadOnlyDictionary<(uint AppID, ulong ClassID, ulong InstanceID), InventoryResponse.Description> descriptions, IReadOnlyCollection<KeyValue> input, ICollection<Asset> output) {
			if (descriptions == null) {
				throw new ArgumentNullException(nameof(descriptions));
			}

			if ((input == null) || (input.Count == 0)) {
				throw new ArgumentNullException(nameof(input));
			}

			if (output == null) {
				throw new ArgumentNullException(nameof(output));
			}

			foreach (KeyValue item in input) {
				uint appID = item["appid"].AsUnsignedInteger();

				if (appID == 0) {
					ASF.ArchiLogger.LogNullError(nameof(appID));

					return false;
				}

				ulong contextID = item["contextid"].AsUnsignedLong();

				if (contextID == 0) {
					ASF.ArchiLogger.LogNullError(nameof(contextID));

					return false;
				}

				ulong classID = item["classid"].AsUnsignedLong();

				if (classID == 0) {
					ASF.ArchiLogger.LogNullError(nameof(classID));

					return false;
				}

				ulong instanceID = item["instanceid"].AsUnsignedLong();

				(uint AppID, ulong ClassID, ulong InstanceID) key = (appID, classID, instanceID);

				uint amount = item["amount"].AsUnsignedInteger();

				if (amount == 0) {
					ASF.ArchiLogger.LogNullError(nameof(amount));

					return false;
				}

				ulong assetID = item["assetid"].AsUnsignedLong();

				bool marketable = true;
				bool tradable = true;
				ImmutableHashSet<Tag>? tags = null;
				uint realAppID = 0;
				Asset.EType type = Asset.EType.Unknown;
				Asset.ERarity rarity = Asset.ERarity.Unknown;

				if (descriptions.TryGetValue(key, out InventoryResponse.Description? description)) {
					marketable = description.Marketable;
					tradable = description.Tradable;
					tags = description.Tags;
					realAppID = description.RealAppID;
					type = description.Type;
					rarity = description.Rarity;
				}

				Asset steamAsset = new(appID, contextID, classID, amount, instanceID, assetID, marketable, tradable, tags, realAppID, type, rarity);
				output.Add(steamAsset);
			}

			return true;
		}

		private async Task<bool> RefreshSession() {
			if (!Bot.IsConnectedAndLoggedOn) {
				return false;
			}

			DateTime triggeredAt = DateTime.UtcNow;

			if (triggeredAt <= LastSessionCheck) {
				return LastSessionCheck == LastSessionRefresh;
			}

			await SessionSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				if (triggeredAt <= LastSessionCheck) {
					return LastSessionCheck == LastSessionRefresh;
				}

				Initialized = false;

				if (!Bot.IsConnectedAndLoggedOn) {
					return false;
				}

				Bot.ArchiLogger.LogGenericInfo(Strings.RefreshingOurSession);
				bool result = await Bot.RefreshSession().ConfigureAwait(false);

				DateTime now = DateTime.UtcNow;

				if (result) {
					LastSessionRefresh = now;
				}

				LastSessionCheck = now;

				return result;
			} finally {
				SessionSemaphore.Release();
			}
		}

		private async Task<bool> RegisterApiKey() {
			Uri request = new(SteamCommunityURL, "/dev/registerkey");

			// Extra entry for sessionID
			Dictionary<string, string> data = new(4, StringComparer.Ordinal) {
				{ "agreeToTerms", "agreed" },
#pragma warning disable CA1308 // False positive, we're intentionally converting this part to lowercase and it's not used for any security decisions based on the result of the normalization
				{ "domain", "generated.by." + SharedInfo.AssemblyName.ToLowerInvariant() + ".localhost" },
#pragma warning restore CA1308 // False positive, we're intentionally converting this part to lowercase and it's not used for any security decisions based on the result of the normalization
				{ "Submit", "Register" }
			};

			return await UrlPostWithSession(request, data: data).ConfigureAwait(false);
		}

		private async Task<(bool Success, string? Result)> ResolveAccessToken() {
			Uri request = new(SteamStoreURL, "/pointssummary/ajaxgetasyncconfig");

			ObjectResponse<AccessTokenResponse>? response = await UrlGetToJsonObjectWithSession<AccessTokenResponse>(request).ConfigureAwait(false);

			// ReSharper disable once RedundantSuppressNullableWarningExpression - required for .NET Framework
			return !string.IsNullOrEmpty(response?.Content.Data.WebAPIToken) ? (true, response!.Content.Data.WebAPIToken) : (false, null);
		}

		private async Task<(bool Success, string? Result)> ResolveApiKey() {
			if (Bot.IsAccountLimited) {
				// API key is permanently unavailable for limited accounts
				return (true, null);
			}

			(ESteamApiKeyState State, string? Key) result = await GetApiKeyState().ConfigureAwait(false);

			switch (result.State) {
				case ESteamApiKeyState.AccessDenied:
					// We succeeded in fetching API key, but it resulted in access denied
					// Return empty result, API key is unavailable permanently
					return (true, "");
				case ESteamApiKeyState.NotRegisteredYet:
					// We succeeded in fetching API key, and it resulted in no key registered yet
					// Let's try to register a new key
					if (!await RegisterApiKey().ConfigureAwait(false)) {
						// Request timed out, bad luck, we'll try again later
						goto case ESteamApiKeyState.Timeout;
					}

					// We should have the key ready, so let's fetch it again
					result = await GetApiKeyState().ConfigureAwait(false);

					if (result.State == ESteamApiKeyState.Timeout) {
						// Request timed out, bad luck, we'll try again later
						goto case ESteamApiKeyState.Timeout;
					}

					if (result.State != ESteamApiKeyState.Registered) {
						// Something went wrong, report error
						goto default;
					}

					goto case ESteamApiKeyState.Registered;
				case ESteamApiKeyState.Registered:
					// We succeeded in fetching API key, and it resulted in registered key
					// Cache the result, this is the API key we want
					return (true, result.Key);
				case ESteamApiKeyState.Timeout:
					// Request timed out, bad luck, we'll try again later
					return (false, null);
				default:
					// We got an unhandled error, this should never happen
					Bot.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(result.State), result.State));

					return (false, null);
			}
		}

		private async Task<bool> UnlockParentalAccount(string parentalCode) {
			if (string.IsNullOrEmpty(parentalCode)) {
				throw new ArgumentNullException(nameof(parentalCode));
			}

			Bot.ArchiLogger.LogGenericInfo(Strings.UnlockingParentalAccount);

			bool[] results = await Task.WhenAll(UnlockParentalAccountForService(SteamCommunityURL, parentalCode), UnlockParentalAccountForService(SteamStoreURL, parentalCode)).ConfigureAwait(false);

			if (results.Any(result => !result)) {
				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);

				return false;
			}

			Bot.ArchiLogger.LogGenericInfo(Strings.Success);

			return true;
		}

		private async Task<bool> UnlockParentalAccountForService(Uri service, string parentalCode, byte maxTries = WebBrowser.MaxTries) {
			if (service == null) {
				throw new ArgumentNullException(nameof(service));
			}

			if (string.IsNullOrEmpty(parentalCode)) {
				throw new ArgumentNullException(nameof(parentalCode));
			}

			Uri request = new(service, "/parental/ajaxunlock");

			if (maxTries == 0) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

				return false;
			}

			string? sessionID = WebBrowser.CookieContainer.GetCookieValue(service, "sessionid");

			if (string.IsNullOrEmpty(sessionID)) {
				Bot.ArchiLogger.LogNullError(nameof(sessionID));

				return false;
			}

			Dictionary<string, string> data = new(2, StringComparer.Ordinal) {
				{ "pin", parentalCode },

				// ReSharper disable once RedundantSuppressNullableWarningExpression - required for .NET Framework
				{ "sessionid", sessionID! }
			};

			// This request doesn't go through UrlPostRetryWithSession as we have no access to session refresh capability (this is in fact session initialization)
			BasicResponse? response = await WebLimitRequest(service, async () => await WebBrowser.UrlPost(request, data: data, referer: service).ConfigureAwait(false)).ConfigureAwait(false);

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

		private enum ESteamApiKeyState : byte {
			Error,
			Timeout,
			Registered,
			NotRegisteredYet,
			AccessDenied
		}
	}
}
