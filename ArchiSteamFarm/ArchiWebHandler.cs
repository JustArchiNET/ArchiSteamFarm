//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2020 Łukasz "JustArchi" Domeradzki
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
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using AngleSharp.Dom;
using ArchiSteamFarm.Helpers;
using ArchiSteamFarm.Json;
using ArchiSteamFarm.Localization;
using JetBrains.Annotations;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SteamKit2;
using Formatting = Newtonsoft.Json.Formatting;

namespace ArchiSteamFarm {
	public sealed class ArchiWebHandler : IDisposable {
		[PublicAPI]
		public const string SteamCommunityURL = "https://" + SteamCommunityHost;

		[PublicAPI]
		public const string SteamHelpURL = "https://" + SteamHelpHost;

		[PublicAPI]
		public const string SteamStoreURL = "https://" + SteamStoreHost;

		internal const ushort MaxItemsInSingleInventoryRequest = 5000;

		private const string IEconService = "IEconService";
		private const string IPlayerService = "IPlayerService";
		private const string ISteamApps = "ISteamApps";
		private const string ISteamUserAuth = "ISteamUserAuth";
		private const string ITwoFactorService = "ITwoFactorService";
		private const string SteamCommunityHost = "steamcommunity.com";
		private const string SteamHelpHost = "help.steampowered.com";
		private const string SteamStoreHost = "store.steampowered.com";

		[PublicAPI]
		public readonly ArchiCacheable<string> CachedApiKey;

		[PublicAPI]
		public readonly WebBrowser WebBrowser;

		private readonly Bot Bot;
		private readonly SemaphoreSlim SessionSemaphore = new SemaphoreSlim(1, 1);

		private bool Initialized;
		private DateTime LastSessionCheck;
		private DateTime LastSessionRefresh;
		private bool MarkingInventoryScheduled;
		private string? VanityURL;

		internal ArchiWebHandler(Bot bot) {
			Bot = bot ?? throw new ArgumentNullException(nameof(bot));

			CachedApiKey = new ArchiCacheable<string>(ResolveApiKey);
			WebBrowser = new WebBrowser(bot.ArchiLogger, ASF.GlobalConfig?.WebProxy);
		}

		public void Dispose() {
			CachedApiKey.Dispose();
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
		public async IAsyncEnumerable<Steam.Asset> GetInventoryAsync(ulong steamID = 0, uint appID = Steam.Asset.SteamAppID, ulong contextID = Steam.Asset.SteamCommunityContextID) {
			if ((appID == 0) || (contextID == 0) || (ASF.InventorySemaphore == null)) {
				throw new ArgumentNullException(nameof(appID) + " || " + nameof(contextID) + " || " + nameof(ASF.InventorySemaphore));
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
				throw new NotSupportedException(string.Format(Strings.ErrorObjectIsNull, nameof(steamID)));
			}

			string request = "/inventory/" + steamID + "/" + appID + "/" + contextID + "?count=" + MaxItemsInSingleInventoryRequest + "&l=english";
			ulong startAssetID = 0;

			// We need to store asset IDs to make sure we won't get duplicate items
			HashSet<ulong> assetIDs = new HashSet<ulong>();

			while (true) {
				await ASF.InventorySemaphore.WaitAsync().ConfigureAwait(false);

				try {
					WebBrowser.ObjectResponse<Steam.InventoryResponse>? response = await UrlGetToJsonObjectWithSession<Steam.InventoryResponse>(SteamCommunityURL, request + (startAssetID > 0 ? "&start_assetid=" + startAssetID : "")).ConfigureAwait(false);

					if (response?.Content == null) {
						throw new HttpRequestException(string.Format(Strings.ErrorObjectIsNull, nameof(response)));
					}

					if (response.Content.Result != EResult.OK) {
						throw new HttpRequestException(!string.IsNullOrEmpty(response.Content.Error) ? string.Format(Strings.WarningFailedWithError, response.Content.Error) : Strings.WarningFailed);
					}

					if (response.Content.TotalInventoryCount == 0) {
						// Empty inventory
						yield break;
					}

					if ((response.Content.Assets == null) || (response.Content.Assets.Count == 0) || (response.Content.Descriptions == null) || (response.Content.Descriptions.Count == 0)) {
						throw new NotSupportedException(string.Format(Strings.ErrorObjectIsNull, nameof(response.Content.Assets) + " || " + nameof(response.Content.Descriptions)));
					}

					Dictionary<(ulong ClassID, ulong InstanceID), Steam.InventoryResponse.Description> descriptions = new Dictionary<(ulong ClassID, ulong InstanceID), Steam.InventoryResponse.Description>();

					foreach (Steam.InventoryResponse.Description description in response.Content.Descriptions.Where(description => description != null)) {
						if (description.ClassID == 0) {
							throw new NotSupportedException(string.Format(Strings.ErrorObjectIsNull, nameof(description.ClassID)));
						}

						(ulong ClassID, ulong InstanceID) key = (description.ClassID, description.InstanceID);

						if (descriptions.ContainsKey(key)) {
							continue;
						}

						descriptions[key] = description;
					}

					foreach (Steam.Asset asset in response.Content.Assets.Where(asset => asset != null)) {
						if (!descriptions.TryGetValue((asset.ClassID, asset.InstanceID), out Steam.InventoryResponse.Description? description) || assetIDs.Contains(asset.AssetID)) {
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
						throw new NotSupportedException(string.Format(Strings.ErrorObjectIsNull, nameof(response.Content.LastAssetID)));
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
		public async Task<Dictionary<uint, string>?> GetMyOwnedGames() {
			const string request = "/my/games?l=english&xml=1";

			WebBrowser.XmlDocumentResponse? response = await UrlGetToXmlDocumentWithSession(SteamCommunityURL, request, checkSessionPreemptively: false).ConfigureAwait(false);

			using XmlNodeList? xmlNodeList = response?.Content?.SelectNodes("gamesList/games/game");

			if ((xmlNodeList == null) || (xmlNodeList.Count == 0)) {
				return null;
			}

			Dictionary<uint, string> result = new Dictionary<uint, string>(xmlNodeList.Count);

			foreach (XmlNode? xmlNode in xmlNodeList) {
				if (xmlNode == null) {
					ASF.ArchiLogger.LogNullError(nameof(xmlNode));

					return null;
				}

				XmlNode? appNode = xmlNode.SelectSingleNode("appID");

				if (appNode == null) {
					Bot.ArchiLogger.LogNullError(nameof(appNode));

					return null;
				}

				if (!uint.TryParse(appNode.InnerText, out uint appID) || (appID == 0)) {
					Bot.ArchiLogger.LogNullError(nameof(appID));

					return null;
				}

				XmlNode? nameNode = xmlNode.SelectSingleNode("name");

				if (nameNode == null) {
					Bot.ArchiLogger.LogNullError(nameof(nameNode));

					return null;
				}

				result[appID] = nameNode.InnerText;
			}

			return result;
		}

		[PublicAPI]
		public async Task<Dictionary<uint, string>?> GetOwnedGames(ulong steamID) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
				throw new ArgumentNullException(nameof(steamID));
			}

			(bool success, string? steamApiKey) = await CachedApiKey.GetValue().ConfigureAwait(false);

			if (!success || string.IsNullOrEmpty(steamApiKey)) {
				return null;
			}

			KeyValue? response = null;

			for (byte i = 0; (i < WebBrowser.MaxTries) && (response == null); i++) {
				using WebAPI.AsyncInterface iPlayerService = Bot.SteamConfiguration.GetAsyncWebAPIInterface(IPlayerService);

				iPlayerService.Timeout = WebBrowser.Timeout;

				try {
					response = await WebLimitRequest(
						WebAPI.DefaultBaseAddress.Host,

						// ReSharper disable once AccessToDisposedClosure
						async () => await iPlayerService.CallAsync(
							HttpMethod.Get, "GetOwnedGames", args: new Dictionary<string, object>(3, StringComparer.Ordinal) {
								{ "include_appinfo", 1 },
								{ "key", steamApiKey! },
								{ "steamid", steamID }
							}
						).ConfigureAwait(false)
					).ConfigureAwait(false);
				} catch (TaskCanceledException e) {
					Bot.ArchiLogger.LogGenericDebuggingException(e);
				} catch (Exception e) {
					Bot.ArchiLogger.LogGenericWarningException(e);
				}
			}

			if (response == null) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));

				return null;
			}

			List<KeyValue> games = response["games"].Children;

			Dictionary<uint, string> result = new Dictionary<uint, string>(games.Count);

			foreach (KeyValue game in games) {
				uint appID = game["appid"].AsUnsignedInteger();

				if (appID == 0) {
					Bot.ArchiLogger.LogNullError(nameof(appID));

					return null;
				}

				string? gameName = game["name"].AsString();

				if (string.IsNullOrEmpty(gameName)) {
					Bot.ArchiLogger.LogNullError(nameof(gameName));

					return null;
				}

				result[appID] = gameName!;
			}

			return result;
		}

		[PublicAPI]
		public async Task<bool?> HasValidApiKey() {
			(bool success, string? steamApiKey) = await CachedApiKey.GetValue().ConfigureAwait(false);

			return success ? !string.IsNullOrEmpty(steamApiKey) : (bool?) null;
		}

		[PublicAPI]
		public async Task<(bool Success, HashSet<ulong>? MobileTradeOfferIDs)> SendTradeOffer(ulong steamID, IReadOnlyCollection<Steam.Asset>? itemsToGive = null, IReadOnlyCollection<Steam.Asset>? itemsToReceive = null, string? token = null, bool forcedSingleOffer = false) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || (((itemsToGive == null) || (itemsToGive.Count == 0)) && ((itemsToReceive == null) || (itemsToReceive.Count == 0)))) {
				throw new ArgumentNullException(nameof(steamID) + " || (" + nameof(itemsToGive) + " && " + nameof(itemsToReceive) + ")");
			}

			Steam.TradeOfferSendRequest singleTrade = new Steam.TradeOfferSendRequest();
			HashSet<Steam.TradeOfferSendRequest> trades = new HashSet<Steam.TradeOfferSendRequest> { singleTrade };

			if (itemsToGive != null) {
				foreach (Steam.Asset itemToGive in itemsToGive) {
					if (!forcedSingleOffer && (singleTrade.ItemsToGive.Assets.Count + singleTrade.ItemsToReceive.Assets.Count >= Trading.MaxItemsPerTrade)) {
						if (trades.Count >= Trading.MaxTradesPerAccount) {
							break;
						}

						singleTrade = new Steam.TradeOfferSendRequest();
						trades.Add(singleTrade);
					}

					singleTrade.ItemsToGive.Assets.Add(itemToGive);
				}
			}

			if (itemsToReceive != null) {
				foreach (Steam.Asset itemToReceive in itemsToReceive) {
					if (!forcedSingleOffer && (singleTrade.ItemsToGive.Assets.Count + singleTrade.ItemsToReceive.Assets.Count >= Trading.MaxItemsPerTrade)) {
						if (trades.Count >= Trading.MaxTradesPerAccount) {
							break;
						}

						singleTrade = new Steam.TradeOfferSendRequest();
						trades.Add(singleTrade);
					}

					singleTrade.ItemsToReceive.Assets.Add(itemToReceive);
				}
			}

			const string request = "/tradeoffer/new/send";
			const string referer = SteamCommunityURL + "/tradeoffer/new";

			// Extra entry for sessionID
			Dictionary<string, string> data = new Dictionary<string, string>(6, StringComparer.Ordinal) {
				{ "partner", steamID.ToString() },
				{ "serverid", "1" },
				{ "trade_offer_create_params", !string.IsNullOrEmpty(token) ? new JObject { { "trade_offer_access_token", token } }.ToString(Formatting.None) : "" },
				{ "tradeoffermessage", "Sent by " + SharedInfo.PublicIdentifier + "/" + SharedInfo.Version }
			};

			HashSet<ulong> mobileTradeOfferIDs = new HashSet<ulong>();

			foreach (Steam.TradeOfferSendRequest trade in trades) {
				data["json_tradeoffer"] = JsonConvert.SerializeObject(trade);

				WebBrowser.ObjectResponse<Steam.TradeOfferSendResponse>? response = await UrlPostToJsonObjectWithSession<Steam.TradeOfferSendResponse>(SteamCommunityURL, request, data: data, referer: referer).ConfigureAwait(false);

				if (response?.Content == null) {
					return (false, mobileTradeOfferIDs);
				}

				if (response.Content.RequiresMobileConfirmation) {
					mobileTradeOfferIDs.Add(response.Content.TradeOfferID);
				}
			}

			return (true, mobileTradeOfferIDs);
		}

		[PublicAPI]
		public async Task<WebBrowser.HtmlDocumentResponse?> UrlGetToHtmlDocumentWithSession(string host, string request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, string? referer = null, WebBrowser.ERequestOptions requestOptions = WebBrowser.ERequestOptions.None, bool checkSessionPreemptively = true, byte maxTries = WebBrowser.MaxTries) {
			if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(request)) {
				throw new ArgumentNullException(nameof(host) + " || " + nameof(request));
			}

			if (maxTries == 0) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

				return null;
			}

			if (checkSessionPreemptively) {
				// Check session preemptively as this request might not get redirected to expiration
				bool? sessionExpired = await IsSessionExpired().ConfigureAwait(false);

				if (sessionExpired.GetValueOrDefault(true)) {
					if (await RefreshSession().ConfigureAwait(false)) {
						return await UrlGetToHtmlDocumentWithSession(host, request, headers, referer, requestOptions, true, --maxTries).ConfigureAwait(false);
					}

					Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
					Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

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
					Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

					return null;
				}
			}

			WebBrowser.HtmlDocumentResponse? response = await WebLimitRequest(host, async () => await WebBrowser.UrlGetToHtmlDocument(host + request, headers, referer, requestOptions).ConfigureAwait(false)).ConfigureAwait(false);

			if (response == null) {
				return null;
			}

			if (IsSessionExpiredUri(response.FinalUri)) {
				if (await RefreshSession().ConfigureAwait(false)) {
					return await UrlGetToHtmlDocumentWithSession(host, request, headers, referer, requestOptions, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
				}

				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

				return null;
			}

			// Under special brain-damaged circumstances, Steam might just return our own profile as a response to the request, for absolutely no reason whatsoever - just try again in this case
			if (await IsProfileUri(response.FinalUri).ConfigureAwait(false)) {
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.WarningWorkaroundTriggered, nameof(IsProfileUri)));

				return await UrlGetToHtmlDocumentWithSession(host, request, headers, referer, requestOptions, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
			}

			return response;
		}

		[PublicAPI]
		public async Task<WebBrowser.ObjectResponse<T>?> UrlGetToJsonObjectWithSession<T>(string host, string request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, string? referer = null, WebBrowser.ERequestOptions requestOptions = WebBrowser.ERequestOptions.None, bool checkSessionPreemptively = true, byte maxTries = WebBrowser.MaxTries) where T : class {
			if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(request)) {
				throw new ArgumentNullException(nameof(host) + " || " + nameof(request));
			}

			if (maxTries == 0) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

				return default;
			}

			if (checkSessionPreemptively) {
				// Check session preemptively as this request might not get redirected to expiration
				bool? sessionExpired = await IsSessionExpired().ConfigureAwait(false);

				if (sessionExpired.GetValueOrDefault(true)) {
					if (await RefreshSession().ConfigureAwait(false)) {
						return await UrlGetToJsonObjectWithSession<T>(host, request, headers, referer, requestOptions, true, --maxTries).ConfigureAwait(false);
					}

					Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
					Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

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
					Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

					return default;
				}
			}

			WebBrowser.ObjectResponse<T>? response = await WebLimitRequest(host, async () => await WebBrowser.UrlGetToJsonObject<T>(host + request, headers, referer, requestOptions).ConfigureAwait(false)).ConfigureAwait(false);

			if (response == null) {
				return default;
			}

			if (IsSessionExpiredUri(response.FinalUri)) {
				if (await RefreshSession().ConfigureAwait(false)) {
					return await UrlGetToJsonObjectWithSession<T>(host, request, headers, referer, requestOptions, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
				}

				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

				return null;
			}

			// Under special brain-damaged circumstances, Steam might just return our own profile as a response to the request, for absolutely no reason whatsoever - just try again in this case
			if (await IsProfileUri(response.FinalUri).ConfigureAwait(false)) {
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.WarningWorkaroundTriggered, nameof(IsProfileUri)));

				return await UrlGetToJsonObjectWithSession<T>(host, request, headers, referer, requestOptions, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
			}

			return response;
		}

		[PublicAPI]
		public async Task<WebBrowser.XmlDocumentResponse?> UrlGetToXmlDocumentWithSession(string host, string request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, string? referer = null, WebBrowser.ERequestOptions requestOptions = WebBrowser.ERequestOptions.None, bool checkSessionPreemptively = true, byte maxTries = WebBrowser.MaxTries) {
			if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(request)) {
				throw new ArgumentNullException(nameof(host) + " || " + nameof(request));
			}

			if (maxTries == 0) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

				return null;
			}

			if (checkSessionPreemptively) {
				// Check session preemptively as this request might not get redirected to expiration
				bool? sessionExpired = await IsSessionExpired().ConfigureAwait(false);

				if (sessionExpired.GetValueOrDefault(true)) {
					if (await RefreshSession().ConfigureAwait(false)) {
						return await UrlGetToXmlDocumentWithSession(host, request, headers, referer, requestOptions, true, --maxTries).ConfigureAwait(false);
					}

					Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
					Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

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
					Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

					return null;
				}
			}

			WebBrowser.XmlDocumentResponse? response = await WebLimitRequest(host, async () => await WebBrowser.UrlGetToXmlDocument(host + request, headers, referer, requestOptions).ConfigureAwait(false)).ConfigureAwait(false);

			if (response == null) {
				return null;
			}

			if (IsSessionExpiredUri(response.FinalUri)) {
				if (await RefreshSession().ConfigureAwait(false)) {
					return await UrlGetToXmlDocumentWithSession(host, request, headers, referer, requestOptions, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
				}

				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

				return null;
			}

			// Under special brain-damaged circumstances, Steam might just return our own profile as a response to the request, for absolutely no reason whatsoever - just try again in this case
			if (await IsProfileUri(response.FinalUri).ConfigureAwait(false)) {
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.WarningWorkaroundTriggered, nameof(IsProfileUri)));

				return await UrlGetToXmlDocumentWithSession(host, request, headers, referer, requestOptions, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
			}

			return response;
		}

		[PublicAPI]
		public async Task<bool> UrlHeadWithSession(string host, string request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, string? referer = null, WebBrowser.ERequestOptions requestOptions = WebBrowser.ERequestOptions.None, bool checkSessionPreemptively = true, byte maxTries = WebBrowser.MaxTries) {
			if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(request)) {
				throw new ArgumentNullException(nameof(host) + " || " + nameof(request));
			}

			if (maxTries == 0) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

				return false;
			}

			if (checkSessionPreemptively) {
				// Check session preemptively as this request might not get redirected to expiration
				bool? sessionExpired = await IsSessionExpired().ConfigureAwait(false);

				if (sessionExpired.GetValueOrDefault(true)) {
					if (await RefreshSession().ConfigureAwait(false)) {
						return await UrlHeadWithSession(host, request, headers, referer, requestOptions, true, --maxTries).ConfigureAwait(false);
					}

					Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
					Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

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
					Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

					return false;
				}
			}

			WebBrowser.BasicResponse? response = await WebLimitRequest(host, async () => await WebBrowser.UrlHead(host + request, headers, referer, requestOptions).ConfigureAwait(false)).ConfigureAwait(false);

			if (response == null) {
				return false;
			}

			if (IsSessionExpiredUri(response.FinalUri)) {
				if (await RefreshSession().ConfigureAwait(false)) {
					return await UrlHeadWithSession(host, request, headers, referer, requestOptions, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
				}

				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

				return false;
			}

			// Under special brain-damaged circumstances, Steam might just return our own profile as a response to the request, for absolutely no reason whatsoever - just try again in this case
			if (await IsProfileUri(response.FinalUri).ConfigureAwait(false)) {
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.WarningWorkaroundTriggered, nameof(IsProfileUri)));

				return await UrlHeadWithSession(host, request, headers, referer, requestOptions, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
			}

			return true;
		}

		[PublicAPI]
		public async Task<WebBrowser.HtmlDocumentResponse?> UrlPostToHtmlDocumentWithSession(string host, string request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, IDictionary<string, string>? data = null, string? referer = null, WebBrowser.ERequestOptions requestOptions = WebBrowser.ERequestOptions.None, ESession session = ESession.Lowercase, bool checkSessionPreemptively = true, byte maxTries = WebBrowser.MaxTries) {
			if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(request) || !Enum.IsDefined(typeof(ESession), session)) {
				throw new ArgumentNullException(nameof(host) + " || " + nameof(request) + " || " + nameof(session));
			}

			if (maxTries == 0) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

				return null;
			}

			if (checkSessionPreemptively) {
				// Check session preemptively as this request might not get redirected to expiration
				bool? sessionExpired = await IsSessionExpired().ConfigureAwait(false);

				if (sessionExpired.GetValueOrDefault(true)) {
					if (await RefreshSession().ConfigureAwait(false)) {
						return await UrlPostToHtmlDocumentWithSession(host, request, headers, data, referer, requestOptions, session, true, --maxTries).ConfigureAwait(false);
					}

					Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
					Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

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
					Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

					return null;
				}
			}

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
					data[sessionName] = sessionID!;
				} else {
					data = new Dictionary<string, string>(1, StringComparer.Ordinal) { { sessionName, sessionID! } };
				}
			}

			WebBrowser.HtmlDocumentResponse? response = await WebLimitRequest(host, async () => await WebBrowser.UrlPostToHtmlDocument(host + request, headers, data, referer, requestOptions).ConfigureAwait(false)).ConfigureAwait(false);

			if (response == null) {
				return null;
			}

			if (IsSessionExpiredUri(response.FinalUri)) {
				if (await RefreshSession().ConfigureAwait(false)) {
					return await UrlPostToHtmlDocumentWithSession(host, request, headers, data, referer, requestOptions, session, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
				}

				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

				return null;
			}

			// Under special brain-damaged circumstances, Steam might just return our own profile as a response to the request, for absolutely no reason whatsoever - just try again in this case
			if (await IsProfileUri(response.FinalUri).ConfigureAwait(false)) {
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.WarningWorkaroundTriggered, nameof(IsProfileUri)));

				return await UrlPostToHtmlDocumentWithSession(host, request, headers, data, referer, requestOptions, session, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
			}

			return response;
		}

		[PublicAPI]
		public async Task<WebBrowser.ObjectResponse<T>?> UrlPostToJsonObjectWithSession<T>(string host, string request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, IDictionary<string, string>? data = null, string? referer = null, WebBrowser.ERequestOptions requestOptions = WebBrowser.ERequestOptions.None, ESession session = ESession.Lowercase, bool checkSessionPreemptively = true, byte maxTries = WebBrowser.MaxTries) where T : class {
			if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(request) || !Enum.IsDefined(typeof(ESession), session)) {
				throw new ArgumentNullException(nameof(host) + " || " + nameof(request) + " || " + nameof(session));
			}

			if (maxTries == 0) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

				return null;
			}

			if (checkSessionPreemptively) {
				// Check session preemptively as this request might not get redirected to expiration
				bool? sessionExpired = await IsSessionExpired().ConfigureAwait(false);

				if (sessionExpired.GetValueOrDefault(true)) {
					if (await RefreshSession().ConfigureAwait(false)) {
						return await UrlPostToJsonObjectWithSession<T>(host, request, headers, data, referer, requestOptions, session, true, --maxTries).ConfigureAwait(false);
					}

					Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
					Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

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
					Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

					return null;
				}
			}

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
					data[sessionName] = sessionID!;
				} else {
					data = new Dictionary<string, string>(1, StringComparer.Ordinal) { { sessionName, sessionID! } };
				}
			}

			WebBrowser.ObjectResponse<T>? response = await WebLimitRequest(host, async () => await WebBrowser.UrlPostToJsonObject<T, IDictionary<string, string>>(host + request, headers, data, referer, requestOptions).ConfigureAwait(false)).ConfigureAwait(false);

			if (response == null) {
				return null;
			}

			if (IsSessionExpiredUri(response.FinalUri)) {
				if (await RefreshSession().ConfigureAwait(false)) {
					return await UrlPostToJsonObjectWithSession<T>(host, request, headers, data, referer, requestOptions, session, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
				}

				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

				return null;
			}

			// Under special brain-damaged circumstances, Steam might just return our own profile as a response to the request, for absolutely no reason whatsoever - just try again in this case
			if (await IsProfileUri(response.FinalUri).ConfigureAwait(false)) {
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.WarningWorkaroundTriggered, nameof(IsProfileUri)));

				return await UrlPostToJsonObjectWithSession<T>(host, request, headers, data, referer, requestOptions, session, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
			}

			return response;
		}

		[PublicAPI]
		public async Task<WebBrowser.ObjectResponse<T>?> UrlPostToJsonObjectWithSession<T>(string host, string request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, ICollection<KeyValuePair<string, string>>? data = null, string? referer = null, WebBrowser.ERequestOptions requestOptions = WebBrowser.ERequestOptions.None, ESession session = ESession.Lowercase, bool checkSessionPreemptively = true, byte maxTries = WebBrowser.MaxTries) where T : class {
			if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(request) || !Enum.IsDefined(typeof(ESession), session)) {
				throw new ArgumentNullException(nameof(host) + " || " + nameof(request) + " || " + nameof(session));
			}

			if (maxTries == 0) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

				return null;
			}

			if (checkSessionPreemptively) {
				// Check session preemptively as this request might not get redirected to expiration
				bool? sessionExpired = await IsSessionExpired().ConfigureAwait(false);

				if (sessionExpired.GetValueOrDefault(true)) {
					if (await RefreshSession().ConfigureAwait(false)) {
						return await UrlPostToJsonObjectWithSession<T>(host, request, headers, data, referer, requestOptions, session, true, --maxTries).ConfigureAwait(false);
					}

					Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
					Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

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
					Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

					return null;
				}
			}

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

				KeyValuePair<string, string> sessionValue = new KeyValuePair<string, string>(sessionName, sessionID!);

				if (data != null) {
					data.Remove(sessionValue);
					data.Add(sessionValue);
				} else {
					data = new List<KeyValuePair<string, string>>(1) { sessionValue };
				}
			}

			WebBrowser.ObjectResponse<T>? response = await WebLimitRequest(host, async () => await WebBrowser.UrlPostToJsonObject<T, ICollection<KeyValuePair<string, string>>>(host + request, headers, data, referer, requestOptions).ConfigureAwait(false)).ConfigureAwait(false);

			if (response == null) {
				return null;
			}

			if (IsSessionExpiredUri(response.FinalUri)) {
				if (await RefreshSession().ConfigureAwait(false)) {
					return await UrlPostToJsonObjectWithSession<T>(host, request, headers, data, referer, requestOptions, session, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
				}

				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

				return null;
			}

			// Under special brain-damaged circumstances, Steam might just return our own profile as a response to the request, for absolutely no reason whatsoever - just try again in this case
			if (await IsProfileUri(response.FinalUri).ConfigureAwait(false)) {
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.WarningWorkaroundTriggered, nameof(IsProfileUri)));

				return await UrlPostToJsonObjectWithSession<T>(host, request, headers, data, referer, requestOptions, session, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
			}

			return response;
		}

		[PublicAPI]
		public async Task<bool> UrlPostWithSession(string host, string request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, IDictionary<string, string>? data = null, string? referer = null, WebBrowser.ERequestOptions requestOptions = WebBrowser.ERequestOptions.None, ESession session = ESession.Lowercase, bool checkSessionPreemptively = true, byte maxTries = WebBrowser.MaxTries) {
			if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(request) || !Enum.IsDefined(typeof(ESession), session)) {
				throw new ArgumentNullException(nameof(host) + " || " + nameof(request) + " || " + nameof(session));
			}

			if (maxTries == 0) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

				return false;
			}

			if (checkSessionPreemptively) {
				// Check session preemptively as this request might not get redirected to expiration
				bool? sessionExpired = await IsSessionExpired().ConfigureAwait(false);

				if (sessionExpired.GetValueOrDefault(true)) {
					if (await RefreshSession().ConfigureAwait(false)) {
						return await UrlPostWithSession(host, request, headers, data, referer, requestOptions, session, true, --maxTries).ConfigureAwait(false);
					}

					Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
					Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

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
					Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

					return false;
				}
			}

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
					data[sessionName] = sessionID!;
				} else {
					data = new Dictionary<string, string>(1, StringComparer.Ordinal) { { sessionName, sessionID! } };
				}
			}

			WebBrowser.BasicResponse? response = await WebLimitRequest(host, async () => await WebBrowser.UrlPost(host + request, headers, data, referer, requestOptions).ConfigureAwait(false)).ConfigureAwait(false);

			if (response == null) {
				return false;
			}

			if (IsSessionExpiredUri(response.FinalUri)) {
				if (await RefreshSession().ConfigureAwait(false)) {
					return await UrlPostWithSession(host, request, headers, data, referer, requestOptions, session, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
				}

				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

				return false;
			}

			// Under special brain-damaged circumstances, Steam might just return our own profile as a response to the request, for absolutely no reason whatsoever - just try again in this case
			if (await IsProfileUri(response.FinalUri).ConfigureAwait(false)) {
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.WarningWorkaroundTriggered, nameof(IsProfileUri)));

				return await UrlPostWithSession(host, request, headers, data, referer, requestOptions, session, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
			}

			return true;
		}

		[PublicAPI]
		public static async Task<T?> WebLimitRequest<T>(string service, Func<Task<T?>> function) where T : class {
			if (string.IsNullOrEmpty(service) || (function == null) || (ASF.WebLimitingSemaphores == null)) {
				throw new ArgumentNullException(nameof(service) + " || " + nameof(function) + " || " + nameof(ASF.WebLimitingSemaphores));
			}

			ushort webLimiterDelay = ASF.GlobalConfig?.WebLimiterDelay ?? GlobalConfig.DefaultWebLimiterDelay;

			if (webLimiterDelay == 0) {
				return await function().ConfigureAwait(false);
			}

			if (!ASF.WebLimitingSemaphores.TryGetValue(service, out (ICrossProcessSemaphore RateLimitingSemaphore, SemaphoreSlim OpenConnectionsSemaphore) limiters)) {
				ASF.ArchiLogger.LogGenericWarning(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(service), service));

				if (!ASF.WebLimitingSemaphores.TryGetValue(nameof(ArchiWebHandler), out limiters)) {
					ASF.ArchiLogger.LogNullError(nameof(limiters));

					return await function().ConfigureAwait(false);
				}
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
				throw new ArgumentNullException(nameof(giftCardID));
			}

			const string request = "/gifts/0/resolvegiftcard";

			// Extra entry for sessionID
			Dictionary<string, string> data = new Dictionary<string, string>(3, StringComparer.Ordinal) {
				{ "accept", "1" },
				{ "giftcardid", giftCardID.ToString() }
			};

			WebBrowser.ObjectResponse<Steam.EResultResponse>? response = await UrlPostToJsonObjectWithSession<Steam.EResultResponse>(SteamStoreURL, request, data: data).ConfigureAwait(false);

			if (response?.Content == null) {
				return false;
			}

			if (response.Content.Result != EResult.OK) {
				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);

				return false;
			}

			return true;
		}

		internal async Task<(bool Success, bool RequiresMobileConfirmation)> AcceptTradeOffer(ulong tradeID, byte maxTries = WebBrowser.MaxTries) {
			if (tradeID == 0) {
				throw new ArgumentNullException(nameof(tradeID));
			}

			string request = "/tradeoffer/" + tradeID + "/accept";

			if (maxTries == 0) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, SteamCommunityURL + request));

				return (false, false);
			}

			string referer = SteamCommunityURL + "/tradeoffer/" + tradeID;

			// Extra entry for sessionID
			Dictionary<string, string> data = new Dictionary<string, string>(3, StringComparer.Ordinal) {
				{ "serverid", "1" },
				{ "tradeofferid", tradeID.ToString() }
			};

			WebBrowser.ObjectResponse<Steam.TradeOfferAcceptResponse>? response = await UrlPostToJsonObjectWithSession<Steam.TradeOfferAcceptResponse>(SteamCommunityURL, request, data: data, referer: referer, requestOptions: WebBrowser.ERequestOptions.ReturnServerErrors).ConfigureAwait(false);

			if (response == null) {
				return (false, false);
			}

			if (response.StatusCode.IsServerErrorCode()) {
				if (string.IsNullOrEmpty(response.Content?.ErrorText)) {
					// This is a generic server error without a reason, try again
					return await AcceptTradeOffer(tradeID, --maxTries).ConfigureAwait(false);
				}

				// This is actually client error with a reason, so it doesn't make sense to retry
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.WarningFailedWithError, response.Content!.ErrorText));

				return (false, false);
			}

			return response.Content != null ? (true, response.Content.RequiresMobileConfirmation) : (false, false);
		}

		internal async Task<bool> AddFreeLicense(uint subID) {
			if (subID == 0) {
				throw new ArgumentNullException(nameof(subID));
			}

			const string request = "/checkout/addfreelicense";

			// Extra entry for sessionID
			Dictionary<string, string> data = new Dictionary<string, string>(3, StringComparer.Ordinal) {
				{ "action", "add_to_cart" },
				{ "subid", subID.ToString() }
			};

			using WebBrowser.HtmlDocumentResponse? response = await UrlPostToHtmlDocumentWithSession(SteamStoreURL, request, data: data).ConfigureAwait(false);

			return response?.Content?.SelectSingleNode("//div[@class='add_free_content_success_area']") != null;
		}

		internal async Task<bool> ChangePrivacySettings(Steam.UserPrivacy userPrivacy) {
			if (userPrivacy == null) {
				throw new ArgumentNullException(nameof(userPrivacy));
			}

			string? profileURL = await GetAbsoluteProfileURL().ConfigureAwait(false);

			if (string.IsNullOrEmpty(profileURL)) {
				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);

				return false;
			}

			string request = profileURL + "/ajaxsetprivacy";

			// Extra entry for sessionID
			Dictionary<string, string> data = new Dictionary<string, string>(3, StringComparer.Ordinal) {
				{ "eCommentPermission", ((byte) userPrivacy.CommentPermission).ToString() },
				{ "Privacy", JsonConvert.SerializeObject(userPrivacy.Settings) }
			};

			WebBrowser.ObjectResponse<Steam.EResultResponse>? response = await UrlPostToJsonObjectWithSession<Steam.EResultResponse>(SteamCommunityURL, request, data: data).ConfigureAwait(false);

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
			if (appID == 0) {
				throw new ArgumentNullException(nameof(appID));
			}

			string request = "/app/" + appID;

			// Extra entry for sessionID
			Dictionary<string, string> data = new Dictionary<string, string>(2, StringComparer.Ordinal) { { "appid_to_clear_from_queue", appID.ToString() } };

			return await UrlPostWithSession(SteamStoreURL, request, data: data).ConfigureAwait(false);
		}

		internal async Task<bool> DeclineTradeOffer(ulong tradeID) {
			if (tradeID == 0) {
				throw new ArgumentNullException(nameof(tradeID));
			}

			(bool success, string? steamApiKey) = await CachedApiKey.GetValue().ConfigureAwait(false);

			if (!success || string.IsNullOrEmpty(steamApiKey)) {
				return false;
			}

			KeyValue? response = null;

			for (byte i = 0; (i < WebBrowser.MaxTries) && (response == null); i++) {
				using WebAPI.AsyncInterface iEconService = Bot.SteamConfiguration.GetAsyncWebAPIInterface(IEconService);

				iEconService.Timeout = WebBrowser.Timeout;

				try {
					response = await WebLimitRequest(
						WebAPI.DefaultBaseAddress.Host,

						// ReSharper disable once AccessToDisposedClosure
						async () => await iEconService.CallAsync(
							HttpMethod.Post, "DeclineTradeOffer", args: new Dictionary<string, object>(2, StringComparer.Ordinal) {
								{ "key", steamApiKey! },
								{ "tradeofferid", tradeID }
							}
						).ConfigureAwait(false)
					).ConfigureAwait(false);
				} catch (TaskCanceledException e) {
					Bot.ArchiLogger.LogGenericDebuggingException(e);
				} catch (Exception e) {
					Bot.ArchiLogger.LogGenericWarningException(e);
				}
			}

			if (response == null) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));

				return false;
			}

			return true;
		}

		internal HttpClient GenerateDisposableHttpClient() => WebBrowser.GenerateDisposableHttpClient();

		internal async Task<ImmutableHashSet<uint>?> GenerateNewDiscoveryQueue() {
			const string request = "/explore/generatenewdiscoveryqueue";

			// Extra entry for sessionID
			Dictionary<string, string> data = new Dictionary<string, string>(2, StringComparer.Ordinal) { { "queuetype", "0" } };

			WebBrowser.ObjectResponse<Steam.NewDiscoveryQueueResponse>? response = await UrlPostToJsonObjectWithSession<Steam.NewDiscoveryQueueResponse>(SteamStoreURL, request, data: data).ConfigureAwait(false);

			return response?.Content?.Queue;
		}

		internal async Task<HashSet<Steam.TradeOffer>?> GetActiveTradeOffers() {
			(bool success, string? steamApiKey) = await CachedApiKey.GetValue().ConfigureAwait(false);

			if (!success || string.IsNullOrEmpty(steamApiKey)) {
				return null;
			}

			KeyValue? response = null;

			for (byte i = 0; (i < WebBrowser.MaxTries) && (response == null); i++) {
				using WebAPI.AsyncInterface iEconService = Bot.SteamConfiguration.GetAsyncWebAPIInterface(IEconService);

				iEconService.Timeout = WebBrowser.Timeout;

				try {
					response = await WebLimitRequest(
						WebAPI.DefaultBaseAddress.Host,

						// ReSharper disable once AccessToDisposedClosure
						async () => await iEconService.CallAsync(
							HttpMethod.Get, "GetTradeOffers", args: new Dictionary<string, object>(5, StringComparer.Ordinal) {
								{ "active_only", 1 },
								{ "get_descriptions", 1 },
								{ "get_received_offers", 1 },
								{ "key", steamApiKey! },
								{ "time_historical_cutoff", uint.MaxValue }
							}
						).ConfigureAwait(false)
					).ConfigureAwait(false);
				} catch (TaskCanceledException e) {
					Bot.ArchiLogger.LogGenericDebuggingException(e);
				} catch (Exception e) {
					Bot.ArchiLogger.LogGenericWarningException(e);
				}
			}

			if (response == null) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));

				return null;
			}

			Dictionary<(uint AppID, ulong ClassID, ulong InstanceID), Steam.InventoryResponse.Description> descriptions = new Dictionary<(uint AppID, ulong ClassID, ulong InstanceID), Steam.InventoryResponse.Description>();

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

				Steam.InventoryResponse.Description parsedDescription = new Steam.InventoryResponse.Description {
					AppID = appID,
					ClassID = classID,
					InstanceID = instanceID,
					Marketable = description["marketable"].AsBoolean(),
					Tradable = true // We're parsing active trade offers, we can assume as much
				};

				List<KeyValue> tags = description["tags"].Children;

				if (tags.Count > 0) {
					HashSet<Steam.Asset.Tag> parsedTags = new HashSet<Steam.Asset.Tag>(tags.Count);

					foreach (KeyValue tag in tags) {
						string? identifier = tag["category"].AsString();

						if (string.IsNullOrEmpty(identifier)) {
							Bot.ArchiLogger.LogNullError(nameof(identifier));

							return null;
						}

						string? value = tag["internal_name"].AsString();

						if (string.IsNullOrEmpty(value)) {
							Bot.ArchiLogger.LogNullError(nameof(value));

							return null;
						}

						parsedTags.Add(new Steam.Asset.Tag(identifier!, value!));
					}

					parsedDescription.Tags = parsedTags.ToImmutableHashSet();
				}

				descriptions[key] = parsedDescription;
			}

			HashSet<Steam.TradeOffer> result = new HashSet<Steam.TradeOffer>();

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

				Steam.TradeOffer tradeOffer = new Steam.TradeOffer(tradeOfferID, otherSteamID3, state);

				List<KeyValue> itemsToGive = trade["items_to_give"].Children;

				if (itemsToGive.Count > 0) {
					if (!ParseItems(descriptions, itemsToGive, tradeOffer.ItemsToGive)) {
						Bot.ArchiLogger.LogGenericError(string.Format(Strings.ErrorParsingObject, nameof(itemsToGive)));

						return null;
					}
				}

				List<KeyValue> itemsToReceive = trade["items_to_receive"].Children;

				if (itemsToReceive.Count > 0) {
					if (!ParseItems(descriptions, itemsToReceive, tradeOffer.ItemsToReceive)) {
						Bot.ArchiLogger.LogGenericError(string.Format(Strings.ErrorParsingObject, nameof(itemsToReceive)));

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
				using WebAPI.AsyncInterface iSteamApps = Bot.SteamConfiguration.GetAsyncWebAPIInterface(ISteamApps);

				iSteamApps.Timeout = WebBrowser.Timeout;

				try {
					response = await WebLimitRequest(
						WebAPI.DefaultBaseAddress.Host,

						// ReSharper disable once AccessToDisposedClosure
						async () => await iSteamApps.CallAsync(HttpMethod.Get, "GetAppList", 2).ConfigureAwait(false)
					).ConfigureAwait(false);
				} catch (TaskCanceledException e) {
					Bot.ArchiLogger.LogGenericDebuggingException(e);
				} catch (Exception e) {
					Bot.ArchiLogger.LogGenericWarningException(e);
				}
			}

			if (response == null) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));

				return null;
			}

			List<KeyValue> apps = response["apps"].Children;

			if (apps.Count == 0) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorIsEmpty, nameof(apps)));

				return null;
			}

			HashSet<uint> result = new HashSet<uint>(apps.Count);

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
				throw new ArgumentNullException(nameof(page));
			}

			string request = "/my/badges?l=english&p=" + page;

			WebBrowser.HtmlDocumentResponse? response = await UrlGetToHtmlDocumentWithSession(SteamCommunityURL, request, checkSessionPreemptively: false).ConfigureAwait(false);

			return response?.Content;
		}

		internal async Task<IDocument?> GetConfirmationsPage(string deviceID, string confirmationHash, uint time) {
			if (string.IsNullOrEmpty(deviceID) || string.IsNullOrEmpty(confirmationHash) || (time == 0)) {
				throw new ArgumentNullException(nameof(deviceID) + " || " + nameof(confirmationHash) + " || " + nameof(time));
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

			string request = "/mobileconf/conf?a=" + Bot.SteamID + "&k=" + WebUtility.UrlEncode(confirmationHash) + "&l=english&m=android&p=" + WebUtility.UrlEncode(deviceID) + "&t=" + time + "&tag=conf";

			WebBrowser.HtmlDocumentResponse? response = await UrlGetToHtmlDocumentWithSession(SteamCommunityURL, request).ConfigureAwait(false);

			return response?.Content;
		}

		internal async Task<HashSet<ulong>?> GetDigitalGiftCards() {
			const string request = "/gifts";

			using WebBrowser.HtmlDocumentResponse? response = await UrlGetToHtmlDocumentWithSession(SteamStoreURL, request).ConfigureAwait(false);

			if (response?.Content == null) {
				return null;
			}

			List<IElement> htmlNodes = response.Content.SelectNodes("//div[@class='pending_gift']/div[starts-with(@id, 'pending_gift_')][count(div[@class='pending_giftcard_leftcol']) > 0]/@id");

			if (htmlNodes.Count == 0) {
				return new HashSet<ulong>(0);
			}

			HashSet<ulong> results = new HashSet<ulong>(htmlNodes.Count);

			foreach (string? giftCardIDText in htmlNodes.Select(node => node.GetAttribute("id"))) {
				if (string.IsNullOrEmpty(giftCardIDText)) {
					Bot.ArchiLogger.LogNullError(nameof(giftCardIDText));

					return null;
				}

				if (giftCardIDText.Length <= 13) {
					Bot.ArchiLogger.LogGenericError(string.Format(Strings.ErrorIsInvalid, nameof(giftCardIDText)));

					return null;
				}

				if (!ulong.TryParse(giftCardIDText.Substring(13), out ulong giftCardID) || (giftCardID == 0)) {
					Bot.ArchiLogger.LogGenericError(string.Format(Strings.ErrorParsingObject, nameof(giftCardID)));

					return null;
				}

				results.Add(giftCardID);
			}

			return results;
		}

		internal async Task<IDocument?> GetDiscoveryQueuePage() {
			const string request = "/explore?l=english";

			WebBrowser.HtmlDocumentResponse? response = await UrlGetToHtmlDocumentWithSession(SteamStoreURL, request).ConfigureAwait(false);

			return response?.Content;
		}

		internal async Task<HashSet<ulong>?> GetFamilySharingSteamIDs() {
			const string request = "/account/managedevices?l=english";

			using WebBrowser.HtmlDocumentResponse? response = await UrlGetToHtmlDocumentWithSession(SteamStoreURL, request).ConfigureAwait(false);

			if (response?.Content == null) {
				return null;
			}

			List<IElement> htmlNodes = response.Content.SelectNodes("(//table[@class='accountTable'])[2]//a/@data-miniprofile");

			if (htmlNodes.Count == 0) {
				// OK, no authorized steamIDs
				return new HashSet<ulong>(0);
			}

			HashSet<ulong> result = new HashSet<ulong>(htmlNodes.Count);

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
				throw new ArgumentNullException(nameof(appID));
			}

			string request = "/my/gamecards/" + appID + "?l=english";

			WebBrowser.HtmlDocumentResponse? response = await UrlGetToHtmlDocumentWithSession(SteamCommunityURL, request, checkSessionPreemptively: false).ConfigureAwait(false);

			return response?.Content;
		}

		internal async Task<uint> GetServerTime() {
			KeyValue? response = null;

			for (byte i = 0; (i < WebBrowser.MaxTries) && (response == null); i++) {
				using WebAPI.AsyncInterface iTwoFactorService = Bot.SteamConfiguration.GetAsyncWebAPIInterface(ITwoFactorService);

				iTwoFactorService.Timeout = WebBrowser.Timeout;

				try {
					response = await WebLimitRequest(
						WebAPI.DefaultBaseAddress.Host,

						// ReSharper disable once AccessToDisposedClosure
						async () => await iTwoFactorService.CallAsync(HttpMethod.Post, "QueryTime").ConfigureAwait(false)
					).ConfigureAwait(false);
				} catch (TaskCanceledException e) {
					Bot.ArchiLogger.LogGenericDebuggingException(e);
				} catch (Exception e) {
					Bot.ArchiLogger.LogGenericWarningException(e);
				}
			}

			if (response == null) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));

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
				throw new ArgumentNullException(nameof(tradeID));
			}

			string request = "/tradeoffer/" + tradeID + "?l=english";

			using WebBrowser.HtmlDocumentResponse? response = await UrlGetToHtmlDocumentWithSession(SteamCommunityURL, request).ConfigureAwait(false);

			IElement? htmlNode = response?.Content?.SelectSingleNode("//div[@class='pagecontent']/script");

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
			text = text.Substring(index);

			index = text.IndexOf(';');

			if (index < 0) {
				Bot.ArchiLogger.LogNullError(nameof(index));

				return null;
			}

			text = text.Substring(0, index);

			if (!byte.TryParse(text, out byte result)) {
				Bot.ArchiLogger.LogNullError(nameof(result));

				return null;
			}

			return result;
		}

		internal async Task<byte?> GetTradeHoldDurationForUser(ulong steamID, string? tradeToken = null) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
				throw new ArgumentNullException(nameof(steamID));
			}

			(bool success, string? steamApiKey) = await CachedApiKey.GetValue().ConfigureAwait(false);

			if (!success || string.IsNullOrEmpty(steamApiKey)) {
				return null;
			}

			Dictionary<string, object> arguments = new Dictionary<string, object>(!string.IsNullOrEmpty(tradeToken) ? 3 : 2, StringComparer.Ordinal) {
				{ "key", steamApiKey! },
				{ "steamid_target", steamID }
			};

			if (!string.IsNullOrEmpty(tradeToken)) {
				arguments["trade_offer_access_token"] = tradeToken!;
			}

			KeyValue? response = null;

			for (byte i = 0; (i < WebBrowser.MaxTries) && (response == null); i++) {
				using WebAPI.AsyncInterface iEconService = Bot.SteamConfiguration.GetAsyncWebAPIInterface(IEconService);

				iEconService.Timeout = WebBrowser.Timeout;

				try {
					response = await WebLimitRequest(
						WebAPI.DefaultBaseAddress.Host,

						// ReSharper disable once AccessToDisposedClosure
						async () => await iEconService.CallAsync(HttpMethod.Get, "GetTradeHoldDurations", args: arguments).ConfigureAwait(false)
					).ConfigureAwait(false);
				} catch (TaskCanceledException e) {
					Bot.ArchiLogger.LogGenericDebuggingException(e);
				} catch (Exception e) {
					Bot.ArchiLogger.LogGenericWarningException(e);
				}
			}

			if (response == null) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));

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
			if (string.IsNullOrEmpty(deviceID) || string.IsNullOrEmpty(confirmationHash) || (time == 0) || (confirmationID == 0) || (confirmationKey == 0)) {
				throw new ArgumentNullException(nameof(deviceID) + " || " + nameof(confirmationHash) + " || " + nameof(time) + " || " + nameof(confirmationID) + " || " + nameof(confirmationKey));
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

			string request = "/mobileconf/ajaxop?a=" + Bot.SteamID + "&cid=" + confirmationID + "&ck=" + confirmationKey + "&k=" + WebUtility.UrlEncode(confirmationHash) + "&l=english&m=android&op=" + (accept ? "allow" : "cancel") + "&p=" + WebUtility.UrlEncode(deviceID) + "&t=" + time + "&tag=conf";

			WebBrowser.ObjectResponse<Steam.BooleanResponse>? response = await UrlGetToJsonObjectWithSession<Steam.BooleanResponse>(SteamCommunityURL, request).ConfigureAwait(false);

			return response?.Content?.Success;
		}

		internal async Task<bool?> HandleConfirmations(string deviceID, string confirmationHash, uint time, IReadOnlyCollection<MobileAuthenticator.Confirmation> confirmations, bool accept) {
			if (string.IsNullOrEmpty(deviceID) || string.IsNullOrEmpty(confirmationHash) || (time == 0) || (confirmations == null) || (confirmations.Count == 0)) {
				throw new ArgumentNullException(nameof(deviceID) + " || " + nameof(confirmationHash) + " || " + nameof(time) + " || " + nameof(confirmations));
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

			const string request = "/mobileconf/multiajaxop";

			// Extra entry for sessionID
			List<KeyValuePair<string, string>> data = new List<KeyValuePair<string, string>>(8 + (confirmations.Count * 2)) {
				new KeyValuePair<string, string>("a", Bot.SteamID.ToString()),
				new KeyValuePair<string, string>("k", confirmationHash),
				new KeyValuePair<string, string>("m", "android"),
				new KeyValuePair<string, string>("op", accept ? "allow" : "cancel"),
				new KeyValuePair<string, string>("p", deviceID),
				new KeyValuePair<string, string>("t", time.ToString()),
				new KeyValuePair<string, string>("tag", "conf")
			};

			foreach (MobileAuthenticator.Confirmation confirmation in confirmations) {
				data.Add(new KeyValuePair<string, string>("cid[]", confirmation.ID.ToString()));
				data.Add(new KeyValuePair<string, string>("ck[]", confirmation.Key.ToString()));
			}

			WebBrowser.ObjectResponse<Steam.BooleanResponse>? response = await UrlPostToJsonObjectWithSession<Steam.BooleanResponse>(SteamCommunityURL, request, data: data).ConfigureAwait(false);

			return response?.Content?.Success;
		}

		internal async Task<bool> Init(ulong steamID, EUniverse universe, string webAPIUserNonce, string? parentalCode = null) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || (universe == EUniverse.Invalid) || !Enum.IsDefined(typeof(EUniverse), universe) || string.IsNullOrEmpty(webAPIUserNonce)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(universe) + " || " + nameof(webAPIUserNonce));
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

			using (RSACrypto rsa = new RSACrypto(publicKey)) {
				encryptedSessionKey = rsa.Encrypt(sessionKey);
			}

			// Generate login key from the user nonce that we've received from Steam network
			byte[] loginKey = Encoding.UTF8.GetBytes(webAPIUserNonce);

			// AES encrypt our login key with our session key
			byte[] encryptedLoginKey = CryptoHelper.SymmetricEncrypt(loginKey, sessionKey);

			// We're now ready to send the data to Steam API
			Bot.ArchiLogger.LogGenericInfo(string.Format(Strings.LoggingIn, ISteamUserAuth));

			KeyValue? response;

			// We do not use usual retry pattern here as webAPIUserNonce is valid only for a single request
			// Even during timeout, webAPIUserNonce is most likely already invalid
			// Instead, the caller is supposed to ask for new webAPIUserNonce and call Init() again on failure
			using (WebAPI.AsyncInterface iSteamUserAuth = Bot.SteamConfiguration.GetAsyncWebAPIInterface(ISteamUserAuth)) {
				iSteamUserAuth.Timeout = WebBrowser.Timeout;

				try {
					response = await WebLimitRequest(
						WebAPI.DefaultBaseAddress.Host,

						// ReSharper disable once AccessToDisposedClosure
						async () => await iSteamUserAuth.CallAsync(
							HttpMethod.Post, "AuthenticateUser", args: new Dictionary<string, object>(3, StringComparer.Ordinal) {
								{ "encrypted_loginkey", encryptedLoginKey },
								{ "sessionkey", encryptedSessionKey },
								{ "steamid", steamID }
							}
						).ConfigureAwait(false)
					).ConfigureAwait(false);
				} catch (TaskCanceledException e) {
					Bot.ArchiLogger.LogGenericDebuggingException(e);

					return false;
				} catch (Exception e) {
					Bot.ArchiLogger.LogGenericWarningException(e);

					return false;
				}
			}

			if (response == null) {
				return false;
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

			string sessionID = Convert.ToBase64String(Encoding.UTF8.GetBytes(steamID.ToString()));

			WebBrowser.CookieContainer.Add(new Cookie("sessionid", sessionID, "/", "." + SteamCommunityHost));
			WebBrowser.CookieContainer.Add(new Cookie("sessionid", sessionID, "/", "." + SteamHelpHost));
			WebBrowser.CookieContainer.Add(new Cookie("sessionid", sessionID, "/", "." + SteamStoreHost));

			WebBrowser.CookieContainer.Add(new Cookie("steamLogin", steamLogin, "/", "." + SteamCommunityHost));
			WebBrowser.CookieContainer.Add(new Cookie("steamLogin", steamLogin, "/", "." + SteamHelpHost));
			WebBrowser.CookieContainer.Add(new Cookie("steamLogin", steamLogin, "/", "." + SteamStoreHost));

			WebBrowser.CookieContainer.Add(new Cookie("steamLoginSecure", steamLoginSecure, "/", "." + SteamCommunityHost));
			WebBrowser.CookieContainer.Add(new Cookie("steamLoginSecure", steamLoginSecure, "/", "." + SteamHelpHost));
			WebBrowser.CookieContainer.Add(new Cookie("steamLoginSecure", steamLoginSecure, "/", "." + SteamStoreHost));

			// Report proper time when doing timezone-based calculations, see setTimezoneCookies() from https://steamcommunity-a.akamaihd.net/public/shared/javascript/shared_global.js
			string timeZoneOffset = DateTimeOffset.Now.Offset.TotalSeconds + WebUtility.UrlEncode(",") + "0";

			WebBrowser.CookieContainer.Add(new Cookie("timezoneOffset", timeZoneOffset, "/", "." + SteamCommunityHost));
			WebBrowser.CookieContainer.Add(new Cookie("timezoneOffset", timeZoneOffset, "/", "." + SteamHelpHost));
			WebBrowser.CookieContainer.Add(new Cookie("timezoneOffset", timeZoneOffset, "/", "." + SteamStoreHost));

			Bot.ArchiLogger.LogGenericInfo(Strings.Success);

			// Unlock Steam Parental if needed
			if ((parentalCode != null) && (parentalCode.Length == 4)) {
				if (!await UnlockParentalAccount(parentalCode).ConfigureAwait(false)) {
					return false;
				}
			}

			LastSessionCheck = LastSessionRefresh = DateTime.UtcNow;
			Initialized = true;

			return true;
		}

		internal async Task<bool> JoinGroup(ulong groupID) {
			if ((groupID == 0) || !new SteamID(groupID).IsClanAccount) {
				throw new ArgumentNullException(nameof(groupID));
			}

			string request = "/gid/" + groupID;

			// Extra entry for sessionID
			Dictionary<string, string> data = new Dictionary<string, string>(2, StringComparer.Ordinal) { { "action", "join" } };

			return await UrlPostWithSession(SteamCommunityURL, request, data: data, session: ESession.CamelCase).ConfigureAwait(false);
		}

		internal async Task MarkInventory() {
			if (ASF.InventorySemaphore == null) {
				throw new ArgumentNullException(nameof(ASF.InventorySemaphore));
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

				const string request = "/my/inventory";
				await UrlHeadWithSession(SteamCommunityURL, request, checkSessionPreemptively: false).ConfigureAwait(false);
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
			const string request = "/my/tradeoffers/sent";

			return await UrlHeadWithSession(SteamCommunityURL, request, checkSessionPreemptively: false).ConfigureAwait(false);
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

			const string requestValidateCode = "/account/ajaxredeemwalletcode";

			// Extra entry for sessionID
			Dictionary<string, string> data = new Dictionary<string, string>(2, StringComparer.Ordinal) { { "wallet_code", key } };

			WebBrowser.ObjectResponse<Steam.RedeemWalletResponse>? responseValidateCode = await UrlPostToJsonObjectWithSession<Steam.RedeemWalletResponse>(SteamStoreURL, requestValidateCode, data: data).ConfigureAwait(false);

			if (responseValidateCode?.Content == null) {
				return null;
			}

			// We can not trust EResult response, because it is OK even in the case of error, so changing it to Fail in this case
			if ((responseValidateCode.Content.Result != EResult.OK) || (responseValidateCode.Content.PurchaseResultDetail != EPurchaseResultDetail.NoDetail)) {
				return (responseValidateCode.Content.Result == EResult.OK ? EResult.Fail : responseValidateCode.Content.Result, responseValidateCode.Content.PurchaseResultDetail);
			}

			return (EResult.OK, EPurchaseResultDetail.NoDetail);
		}

		internal async Task<bool> UnpackBooster(uint appID, ulong itemID) {
			if ((appID == 0) || (itemID == 0)) {
				throw new ArgumentNullException(nameof(appID) + " || " + nameof(itemID));
			}

			string? profileURL = await GetAbsoluteProfileURL().ConfigureAwait(false);

			if (string.IsNullOrEmpty(profileURL)) {
				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);

				return false;
			}

			string request = profileURL + "/ajaxunpackbooster";

			// Extra entry for sessionID
			Dictionary<string, string> data = new Dictionary<string, string>(3, StringComparer.Ordinal) {
				{ "appid", appID.ToString() },
				{ "communityitemid", itemID.ToString() }
			};

			WebBrowser.ObjectResponse<Steam.EResultResponse>? response = await UrlPostToJsonObjectWithSession<Steam.EResultResponse>(SteamCommunityURL, request, data: data).ConfigureAwait(false);

			return response?.Content?.Result == EResult.OK;
		}

		private async Task<(ESteamApiKeyState State, string? Key)> GetApiKeyState() {
			const string request = "/dev/apikey?l=english";

			using WebBrowser.HtmlDocumentResponse? response = await UrlGetToHtmlDocumentWithSession(SteamCommunityURL, request).ConfigureAwait(false);

			IElement? titleNode = response?.Content?.SelectSingleNode("//div[@id='mainContents']/h2");

			if (titleNode == null) {
				return (ESteamApiKeyState.Timeout, null);
			}

			string title = titleNode.TextContent;

			if (string.IsNullOrEmpty(title)) {
				Bot.ArchiLogger.LogNullError(nameof(title));

				return (ESteamApiKeyState.Error, null);
			}

			if (title.Contains("Access Denied") || title.Contains("Validated email address required")) {
				return (ESteamApiKeyState.AccessDenied, null);
			}

			IElement? htmlNode = response!.Content!.SelectSingleNode("//div[@id='bodyContents_ex']/p");

			if (htmlNode == null) {
				Bot.ArchiLogger.LogNullError(nameof(htmlNode));

				return (ESteamApiKeyState.Error, null);
			}

			string text = htmlNode.TextContent;

			if (string.IsNullOrEmpty(text)) {
				Bot.ArchiLogger.LogNullError(nameof(text));

				return (ESteamApiKeyState.Error, null);
			}

			if (text.Contains("Registering for a Steam Web API Key")) {
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

			text = text.Substring(keyIndex);

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

			return uri.AbsolutePath.Equals(profileURL);
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

				const string host = SteamStoreURL;
				const string request = "/account";

				WebBrowser.BasicResponse? response = await WebLimitRequest(host, async () => await WebBrowser.UrlHead(host + request).ConfigureAwait(false)).ConfigureAwait(false);

				if (response?.FinalUri == null) {
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

			return uri.AbsolutePath.StartsWith("/login", StringComparison.Ordinal) || uri.Host.Equals("lostauth");
		}

		private static bool ParseItems(IReadOnlyDictionary<(uint AppID, ulong ClassID, ulong InstanceID), Steam.InventoryResponse.Description> descriptions, IReadOnlyCollection<KeyValue> input, ICollection<Steam.Asset> output) {
			if ((descriptions == null) || (input == null) || (input.Count == 0) || (output == null)) {
				throw new ArgumentNullException(nameof(descriptions) + " || " + nameof(input) + " || " + nameof(output));
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
				ImmutableHashSet<Steam.Asset.Tag>? tags = null;
				uint realAppID = 0;
				Steam.Asset.EType type = Steam.Asset.EType.Unknown;
				Steam.Asset.ERarity rarity = Steam.Asset.ERarity.Unknown;

				if (descriptions.TryGetValue(key, out Steam.InventoryResponse.Description? description)) {
					marketable = description.Marketable;
					tradable = description.Tradable;
					tags = description.Tags;
					realAppID = description.RealAppID;
					type = description.Type;
					rarity = description.Rarity;
				}

				Steam.Asset steamAsset = new Steam.Asset(appID, contextID, classID, amount, instanceID, assetID, marketable, tradable, tags, realAppID, type, rarity);
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
			const string request = "/dev/registerkey";

			// Extra entry for sessionID
			Dictionary<string, string> data = new Dictionary<string, string>(4, StringComparer.Ordinal) {
				{ "agreeToTerms", "agreed" },
				{ "domain", "generated.by." + SharedInfo.AssemblyName.ToLowerInvariant() + ".localhost" },
				{ "Submit", "Register" }
			};

			return await UrlPostWithSession(SteamCommunityURL, request, data: data).ConfigureAwait(false);
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
					Bot.ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(result.State), result.State));

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

		private async Task<bool> UnlockParentalAccountForService(string serviceURL, string parentalCode, byte maxTries = WebBrowser.MaxTries) {
			if (string.IsNullOrEmpty(serviceURL) || string.IsNullOrEmpty(parentalCode)) {
				throw new ArgumentNullException(nameof(serviceURL) + " || " + nameof(parentalCode));
			}

			const string request = "/parental/ajaxunlock";

			if (maxTries == 0) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, serviceURL + request));

				return false;
			}

			string? sessionID = WebBrowser.CookieContainer.GetCookieValue(serviceURL, "sessionid");

			if (string.IsNullOrEmpty(sessionID)) {
				Bot.ArchiLogger.LogNullError(nameof(sessionID));

				return false;
			}

			Dictionary<string, string> data = new Dictionary<string, string>(2, StringComparer.Ordinal) {
				{ "pin", parentalCode },
				{ "sessionid", sessionID! }
			};

			// This request doesn't go through UrlPostRetryWithSession as we have no access to session refresh capability (this is in fact session initialization)
			WebBrowser.BasicResponse? response = await WebLimitRequest(serviceURL, async () => await WebBrowser.UrlPost(serviceURL + request, data: data, referer: serviceURL).ConfigureAwait(false)).ConfigureAwait(false);

			if ((response == null) || IsSessionExpiredUri(response.FinalUri)) {
				// There is no session refresh capability at this stage
				return false;
			}

			// Under special brain-damaged circumstances, Steam might just return our own profile as a response to the request, for absolutely no reason whatsoever - just try again in this case
			if (await IsProfileUri(response.FinalUri, false).ConfigureAwait(false)) {
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.WarningWorkaroundTriggered, nameof(IsProfileUri)));

				return await UnlockParentalAccountForService(serviceURL, parentalCode, --maxTries).ConfigureAwait(false);
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
