//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// 
// Copyright 2015-2019 Łukasz "JustArchi" Domeradzki
// Contact: JustArchi@JustArchi.net
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
// http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using ArchiSteamFarm.Helpers;
using ArchiSteamFarm.Json;
using ArchiSteamFarm.Localization;
using HtmlAgilityPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SteamKit2;
using Formatting = Newtonsoft.Json.Formatting;

namespace ArchiSteamFarm {
	internal sealed class ArchiWebHandler : IDisposable {
		private const string IEconService = "IEconService";
		private const string IPlayerService = "IPlayerService";
		private const string ISteamApps = "ISteamApps";
		private const string ISteamUserAuth = "ISteamUserAuth";
		private const string ITwoFactorService = "ITwoFactorService";
		private const ushort MaxItemsInSingleInventoryRequest = 5000;
		private const byte MinSessionValidityInSeconds = GlobalConfig.DefaultConnectionTimeout / 6;
		private const string SteamCommunityHost = "steamcommunity.com";
		private const string SteamCommunityURL = "https://" + SteamCommunityHost;
		private const string SteamStoreHost = "store.steampowered.com";
		private const string SteamStoreURL = "https://" + SteamStoreHost;

		private static readonly SemaphoreSlim InventorySemaphore = new SemaphoreSlim(1, 1);

		private static readonly ImmutableDictionary<string, (SemaphoreSlim RateLimitingSemaphore, SemaphoreSlim OpenConnectionsSemaphore)> WebLimitingSemaphores = new Dictionary<string, (SemaphoreSlim RateLimitingSemaphore, SemaphoreSlim OpenConnectionsSemaphore)>(3) {
			{ SteamCommunityURL, (new SemaphoreSlim(1, 1), new SemaphoreSlim(WebBrowser.MaxConnections, WebBrowser.MaxConnections)) },
			{ SteamStoreURL, (new SemaphoreSlim(1, 1), new SemaphoreSlim(WebBrowser.MaxConnections, WebBrowser.MaxConnections)) },
			{ WebAPI.DefaultBaseAddress.Host, (new SemaphoreSlim(1, 1), new SemaphoreSlim(WebBrowser.MaxConnections, WebBrowser.MaxConnections)) }
		}.ToImmutableDictionary();

		private readonly Bot Bot;
		private readonly ArchiCacheable<string> CachedApiKey;
		private readonly ArchiCacheable<bool> CachedPublicInventory;
		private readonly SemaphoreSlim SessionSemaphore = new SemaphoreSlim(1, 1);
		private readonly WebBrowser WebBrowser;

		private DateTime LastSessionCheck;
		private DateTime LastSessionRefresh;
		private bool MarkingInventoryScheduled;
		private ulong SteamID;
		private string VanityURL;

		internal ArchiWebHandler(Bot bot) {
			Bot = bot ?? throw new ArgumentNullException(nameof(bot));

			CachedApiKey = new ArchiCacheable<string>(ResolveApiKey, TimeSpan.FromHours(1));
			CachedPublicInventory = new ArchiCacheable<bool>(ResolvePublicInventory, TimeSpan.FromHours(1));
			WebBrowser = new WebBrowser(bot.ArchiLogger, Program.GlobalConfig.WebProxy);
		}

		public void Dispose() {
			CachedApiKey.Dispose();
			CachedPublicInventory.Dispose();
			SessionSemaphore.Dispose();
			WebBrowser.Dispose();
		}

		internal async Task<bool> AcceptDigitalGiftCard(ulong giftCardID) {
			if (giftCardID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(giftCardID));

				return false;
			}

			const string request = "/gifts/0/resolvegiftcard";

			// Extra entry for sessionID
			Dictionary<string, string> data = new Dictionary<string, string>(3) {
				{ "accept", "1" },
				{ "giftcardid", giftCardID.ToString() }
			};

			Steam.NumberResponse result = await UrlPostToJsonObjectWithSession<Steam.NumberResponse>(SteamStoreURL, request, data).ConfigureAwait(false);

			return result?.Success == true;
		}

		internal async Task<(bool Success, bool RequiresMobileConfirmation)> AcceptTradeOffer(ulong tradeID) {
			if (tradeID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(tradeID));

				return (false, false);
			}

			string request = "/tradeoffer/" + tradeID + "/accept";
			string referer = SteamCommunityURL + "/tradeoffer/" + tradeID;

			// Extra entry for sessionID
			Dictionary<string, string> data = new Dictionary<string, string>(3) {
				{ "serverid", "1" },
				{ "tradeofferid", tradeID.ToString() }
			};

			Steam.TradeOfferAcceptResponse response = await UrlPostToJsonObjectWithSession<Steam.TradeOfferAcceptResponse>(SteamCommunityURL, request, data, referer).ConfigureAwait(false);

			return response != null ? (true, response.RequiresMobileConfirmation) : (false, false);
		}

		internal async Task<bool> AddFreeLicense(uint subID) {
			if (subID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(subID));

				return false;
			}

			const string request = "/checkout/addfreelicense";

			// Extra entry for sessionID
			Dictionary<string, string> data = new Dictionary<string, string>(3) {
				{ "action", "add_to_cart" },
				{ "subid", subID.ToString() }
			};

			HtmlDocument htmlDocument = await UrlPostToHtmlDocumentWithSession(SteamStoreURL, request, data).ConfigureAwait(false);

			return htmlDocument?.DocumentNode.SelectSingleNode("//div[@class='add_free_content_success_area']") != null;
		}

		internal async Task<bool> ChangePrivacySettings(Steam.UserPrivacy userPrivacy) {
			if (userPrivacy == null) {
				Bot.ArchiLogger.LogNullError(nameof(userPrivacy));

				return false;
			}

			string profileURL = await GetAbsoluteProfileURL().ConfigureAwait(false);

			if (string.IsNullOrEmpty(profileURL)) {
				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);

				return false;
			}

			string request = profileURL + "/ajaxsetprivacy";

			// Extra entry for sessionID
			Dictionary<string, string> data = new Dictionary<string, string>(3) {
				{ "eCommentPermission", ((byte) userPrivacy.CommentPermission).ToString() },
				{ "Privacy", JsonConvert.SerializeObject(userPrivacy.Settings) }
			};

			Steam.NumberResponse response = await UrlPostToJsonObjectWithSession<Steam.NumberResponse>(SteamCommunityURL, request, data).ConfigureAwait(false);

			if (response == null) {
				return false;
			}

			if (!response.Success) {
				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);

				return false;
			}

			return true;
		}

		internal async Task<bool> ClearFromDiscoveryQueue(uint appID) {
			if (appID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(appID));

				return false;
			}

			string request = "/app/" + appID;

			// Extra entry for sessionID
			Dictionary<string, string> data = new Dictionary<string, string>(2) { { "appid_to_clear_from_queue", appID.ToString() } };

			return await UrlPostWithSession(SteamStoreURL, request, data).ConfigureAwait(false);
		}

		internal async Task<bool> DeclineTradeOffer(ulong tradeID) {
			if (tradeID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(tradeID));

				return false;
			}

			(bool success, string steamApiKey) = await CachedApiKey.GetValue().ConfigureAwait(false);

			if (!success || string.IsNullOrEmpty(steamApiKey)) {
				return false;
			}

			KeyValue response = null;

			for (byte i = 0; (i < WebBrowser.MaxTries) && (response == null); i++) {
				using (dynamic iEconService = WebAPI.GetAsyncInterface(IEconService, steamApiKey)) {
					iEconService.Timeout = WebBrowser.Timeout;

					try {
						response = await WebLimitRequest(
							WebAPI.DefaultBaseAddress.Host,

							// ReSharper disable once AccessToDisposedClosure
							async () => await iEconService.DeclineTradeOffer(
								method: WebRequestMethods.Http.Post,
								tradeofferid: tradeID
							)
						).ConfigureAwait(false);
					} catch (TaskCanceledException e) {
						Bot.ArchiLogger.LogGenericDebuggingException(e);
					} catch (Exception e) {
						Bot.ArchiLogger.LogGenericWarningException(e);
					}
				}
			}

			if (response == null) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));

				return false;
			}

			return true;
		}

		internal HttpClient GenerateDisposableHttpClient() => WebBrowser.GenerateDisposableHttpClient();

		internal async Task<HashSet<uint>> GenerateNewDiscoveryQueue() {
			const string request = "/explore/generatenewdiscoveryqueue";

			// Extra entry for sessionID
			Dictionary<string, string> data = new Dictionary<string, string>(2) { { "queuetype", "0" } };

			Steam.NewDiscoveryQueueResponse output = await UrlPostToJsonObjectWithSession<Steam.NewDiscoveryQueueResponse>(SteamStoreURL, request, data).ConfigureAwait(false);

			return output?.Queue;
		}

		internal async Task<HashSet<Steam.TradeOffer>> GetActiveTradeOffers() {
			(bool success, string steamApiKey) = await CachedApiKey.GetValue().ConfigureAwait(false);

			if (!success || string.IsNullOrEmpty(steamApiKey)) {
				return null;
			}

			KeyValue response = null;

			for (byte i = 0; (i < WebBrowser.MaxTries) && (response == null); i++) {
				using (dynamic iEconService = WebAPI.GetAsyncInterface(IEconService, steamApiKey)) {
					iEconService.Timeout = WebBrowser.Timeout;

					try {
						response = await WebLimitRequest(
							WebAPI.DefaultBaseAddress.Host,

							// ReSharper disable once AccessToDisposedClosure
							async () => await iEconService.GetTradeOffers(
								active_only: 1,
								get_descriptions: 1,
								get_received_offers: 1,
								time_historical_cutoff: uint.MaxValue
							)
						).ConfigureAwait(false);
					} catch (TaskCanceledException e) {
						Bot.ArchiLogger.LogGenericDebuggingException(e);
					} catch (Exception e) {
						Bot.ArchiLogger.LogGenericWarningException(e);
					}
				}
			}

			if (response == null) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));

				return null;
			}

			Dictionary<ulong, (uint AppID, Steam.Asset.EType Type)> descriptions = new Dictionary<ulong, (uint AppID, Steam.Asset.EType Type)>();

			foreach (KeyValue description in response["descriptions"].Children) {
				ulong classID = description["classid"].AsUnsignedLong();

				if (classID == 0) {
					Bot.ArchiLogger.LogNullError(nameof(classID));

					return null;
				}

				if (descriptions.ContainsKey(classID)) {
					continue;
				}

				uint appID = 0;

				string hashName = description["market_hash_name"].Value;

				if (!string.IsNullOrEmpty(hashName)) {
					appID = GetAppIDFromMarketHashName(hashName);
				}

				if (appID == 0) {
					appID = description["appid"].AsUnsignedInteger();
				}

				Steam.Asset.EType type = Steam.Asset.EType.Unknown;

				string descriptionType = description["type"].Value;

				if (!string.IsNullOrEmpty(descriptionType)) {
					type = GetItemType(descriptionType);
				}

				descriptions[classID] = (appID, type);
			}

			HashSet<Steam.TradeOffer> result = new HashSet<Steam.TradeOffer>();

			foreach (KeyValue trade in response["trade_offers_received"].Children) {
				Steam.TradeOffer.ETradeOfferState state = trade["trade_offer_state"].AsEnum<Steam.TradeOffer.ETradeOfferState>();

				if (state == Steam.TradeOffer.ETradeOfferState.Unknown) {
					Bot.ArchiLogger.LogNullError(nameof(state));

					return null;
				}

				if (state != Steam.TradeOffer.ETradeOfferState.Active) {
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

		internal async Task<HashSet<uint>> GetAppList() {
			KeyValue response = null;

			for (byte i = 0; (i < WebBrowser.MaxTries) && (response == null); i++) {
				using (dynamic iSteamApps = WebAPI.GetAsyncInterface(ISteamApps)) {
					iSteamApps.Timeout = WebBrowser.Timeout;

					try {
						response = await WebLimitRequest(
							WebAPI.DefaultBaseAddress.Host,

							// ReSharper disable once AccessToDisposedClosure
							async () => await iSteamApps.GetAppList2()
						).ConfigureAwait(false);
					} catch (TaskCanceledException e) {
						Bot.ArchiLogger.LogGenericDebuggingException(e);
					} catch (Exception e) {
						Bot.ArchiLogger.LogGenericWarningException(e);
					}
				}
			}

			if (response == null) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));

				return null;
			}

			List<KeyValue> apps = response["apps"].Children;

			if ((apps == null) || (apps.Count == 0)) {
				Bot.ArchiLogger.LogNullError(nameof(apps));

				return null;
			}

			HashSet<uint> result = new HashSet<uint>(apps.Count);

			foreach (KeyValue app in apps) {
				uint appID = app["appid"].AsUnsignedInteger();

				if (appID == 0) {
					Bot.ArchiLogger.LogNullError(nameof(appID));

					return null;
				}

				result.Add(appID);
			}

			return result;
		}

		internal async Task<HtmlDocument> GetBadgePage(byte page) {
			if (page == 0) {
				Bot.ArchiLogger.LogNullError(nameof(page));

				return null;
			}

			string request = "/my/badges?l=english&p=" + page;

			return await UrlGetToHtmlDocumentWithSession(SteamCommunityURL, request, false).ConfigureAwait(false);
		}

		internal async Task<Steam.ConfirmationDetails> GetConfirmationDetails(string deviceID, string confirmationHash, uint time, MobileAuthenticator.Confirmation confirmation) {
			if (string.IsNullOrEmpty(deviceID) || string.IsNullOrEmpty(confirmationHash) || (time == 0) || (confirmation == null)) {
				Bot.ArchiLogger.LogNullError(nameof(deviceID) + " || " + nameof(confirmationHash) + " || " + nameof(time) + " || " + nameof(confirmation));

				return null;
			}

			if (SteamID == 0) {
				for (byte i = 0; (i < Program.GlobalConfig.ConnectionTimeout) && (SteamID == 0) && Bot.IsConnectedAndLoggedOn; i++) {
					await Task.Delay(1000).ConfigureAwait(false);
				}

				if (SteamID == 0) {
					Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);

					return null;
				}
			}

			string request = "/mobileconf/details/" + confirmation.ID + "?a=" + SteamID + "&k=" + WebUtility.UrlEncode(confirmationHash) + "&l=english&m=android&p=" + WebUtility.UrlEncode(deviceID) + "&t=" + time + "&tag=conf";

			Steam.ConfirmationDetails response = await UrlGetToJsonObjectWithSession<Steam.ConfirmationDetails>(SteamCommunityURL, request).ConfigureAwait(false);

			if (response?.Success != true) {
				return null;
			}

			response.Confirmation = confirmation;

			return response;
		}

		internal async Task<HtmlDocument> GetConfirmations(string deviceID, string confirmationHash, uint time) {
			if (string.IsNullOrEmpty(deviceID) || string.IsNullOrEmpty(confirmationHash) || (time == 0)) {
				Bot.ArchiLogger.LogNullError(nameof(deviceID) + " || " + nameof(confirmationHash) + " || " + nameof(time));

				return null;
			}

			if (SteamID == 0) {
				for (byte i = 0; (i < Program.GlobalConfig.ConnectionTimeout) && (SteamID == 0) && Bot.IsConnectedAndLoggedOn; i++) {
					await Task.Delay(1000).ConfigureAwait(false);
				}

				if (SteamID == 0) {
					Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);

					return null;
				}
			}

			string request = "/mobileconf/conf?a=" + SteamID + "&k=" + WebUtility.UrlEncode(confirmationHash) + "&l=english&m=android&p=" + WebUtility.UrlEncode(deviceID) + "&t=" + time + "&tag=conf";

			return await UrlGetToHtmlDocumentWithSession(SteamCommunityURL, request).ConfigureAwait(false);
		}

		internal async Task<HashSet<ulong>> GetDigitalGiftCards() {
			const string request = "/gifts";
			HtmlDocument response = await UrlGetToHtmlDocumentWithSession(SteamStoreURL, request).ConfigureAwait(false);

			HtmlNodeCollection htmlNodes = response?.DocumentNode.SelectNodes("//div[@class='pending_gift']/div[starts-with(@id, 'pending_gift_')][count(div[@class='pending_giftcard_leftcol']) > 0]/@id");

			if (htmlNodes == null) {
				return null;
			}

			HashSet<ulong> results = new HashSet<ulong>();

			foreach (string giftCardIDText in htmlNodes.Select(node => node.GetAttributeValue("id", null))) {
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

		internal async Task<HtmlDocument> GetDiscoveryQueuePage() {
			const string request = "/explore?l=english";

			return await UrlGetToHtmlDocumentWithSession(SteamStoreURL, request).ConfigureAwait(false);
		}

		internal async Task<HashSet<ulong>> GetFamilySharingSteamIDs() {
			const string request = "/account/managedevices?l=english";
			HtmlDocument htmlDocument = await UrlGetToHtmlDocumentWithSession(SteamStoreURL, request).ConfigureAwait(false);

			HtmlNodeCollection htmlNodes = htmlDocument?.DocumentNode.SelectNodes("(//table[@class='accountTable'])[last()]//a/@data-miniprofile");

			if (htmlNodes == null) {
				return null; // OK, no authorized steamIDs
			}

			HashSet<ulong> result = new HashSet<ulong>();

			foreach (string miniProfile in htmlNodes.Select(htmlNode => htmlNode.GetAttributeValue("data-miniprofile", null))) {
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

		internal async Task<HtmlDocument> GetGameCardsPage(ulong appID) {
			if (appID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(appID));

				return null;
			}

			string request = "/my/gamecards/" + appID + "?l=english";

			return await UrlGetToHtmlDocumentWithSession(SteamCommunityURL, request, false).ConfigureAwait(false);
		}

		[SuppressMessage("ReSharper", "FunctionComplexityOverflow")]
		internal async Task<HashSet<Steam.Asset>> GetInventory(ulong steamID = 0, uint appID = Steam.Asset.SteamAppID, uint contextID = Steam.Asset.SteamCommunityContextID, bool? tradable = null, IReadOnlyCollection<Steam.Asset.EType> wantedTypes = null, IReadOnlyCollection<uint> wantedRealAppIDs = null, IReadOnlyCollection<(uint AppID, Steam.Asset.EType Type)> wantedSets = null, IReadOnlyCollection<(uint AppID, Steam.Asset.EType Type)> skippedSets = null) {
			if ((appID == 0) || (contextID == 0)) {
				Bot.ArchiLogger.LogNullError(nameof(appID) + " || " + nameof(contextID));

				return null;
			}

			if (steamID == 0) {
				if (SteamID == 0) {
					for (byte i = 0; (i < Program.GlobalConfig.ConnectionTimeout) && (SteamID == 0) && Bot.IsConnectedAndLoggedOn; i++) {
						await Task.Delay(1000).ConfigureAwait(false);
					}

					if (SteamID == 0) {
						Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);

						return null;
					}
				}

				steamID = SteamID;
			}

			string request = "/inventory/" + steamID + "/" + appID + "/" + contextID + "?count=" + MaxItemsInSingleInventoryRequest + "&l=english";
			ulong startAssetID = 0;

			HashSet<Steam.Asset> result = new HashSet<Steam.Asset>();

			while (true) {
				await InventorySemaphore.WaitAsync().ConfigureAwait(false);

				try {
					Steam.InventoryResponse response = await UrlGetToJsonObjectWithSession<Steam.InventoryResponse>(SteamCommunityURL, request + (startAssetID > 0 ? "&start_assetid=" + startAssetID : "")).ConfigureAwait(false);

					if (response == null) {
						return null;
					}

					if (!response.Success) {
						Bot.ArchiLogger.LogGenericWarning(!string.IsNullOrEmpty(response.Error) ? string.Format(Strings.WarningFailedWithError, response.Error) : Strings.WarningFailed);

						return null;
					}

					if (response.TotalInventoryCount == 0) {
						// Empty inventory
						return result;
					}

					if ((response.Assets == null) || (response.Assets.Count == 0) || (response.Descriptions == null) || (response.Descriptions.Count == 0)) {
						Bot.ArchiLogger.LogNullError(nameof(response.Assets) + " || " + nameof(response.Descriptions));

						return null;
					}

					Dictionary<ulong, (bool Tradable, Steam.Asset.EType Type, uint RealAppID)> descriptionMap = new Dictionary<ulong, (bool Tradable, Steam.Asset.EType Type, uint RealAppID)>();

					foreach (Steam.InventoryResponse.Description description in response.Descriptions.Where(description => description != null)) {
						if (description.ClassID == 0) {
							Bot.ArchiLogger.LogNullError(nameof(description.ClassID));

							return null;
						}

						if (descriptionMap.ContainsKey(description.ClassID)) {
							continue;
						}

						Steam.Asset.EType type = Steam.Asset.EType.Unknown;

						if (!string.IsNullOrEmpty(description.Type)) {
							type = GetItemType(description.Type);
						}

						uint realAppID = 0;

						if (!string.IsNullOrEmpty(description.MarketHashName)) {
							realAppID = GetAppIDFromMarketHashName(description.MarketHashName);
						}

						if (realAppID == 0) {
							realAppID = description.AppID;
						}

						descriptionMap[description.ClassID] = (description.Tradable, type, realAppID);
					}

					foreach (Steam.Asset asset in response.Assets.Where(asset => asset != null)) {
						if (descriptionMap.TryGetValue(asset.ClassID, out (bool Tradable, Steam.Asset.EType Type, uint RealAppID) description)) {
							if ((tradable.HasValue && (description.Tradable != tradable.Value)) || (wantedTypes?.Contains(description.Type) == false) || (wantedRealAppIDs?.Contains(description.RealAppID) == false) || (wantedSets?.Contains((description.RealAppID, description.Type)) == false) || (skippedSets?.Contains((description.RealAppID, description.Type)) == true)) {
								continue;
							}

							asset.RealAppID = description.RealAppID;
							asset.Tradable = description.Tradable;
							asset.Type = description.Type;
						}

						result.Add(asset);
					}

					if (!response.MoreItems) {
						return result;
					}

					if (response.LastAssetID == 0) {
						Bot.ArchiLogger.LogNullError(nameof(response.LastAssetID));

						return null;
					}

					startAssetID = response.LastAssetID;
				} finally {
					if (Program.GlobalConfig.InventoryLimiterDelay == 0) {
						InventorySemaphore.Release();
					} else {
						Utilities.InBackground(
							async () => {
								await Task.Delay(Program.GlobalConfig.InventoryLimiterDelay * 1000).ConfigureAwait(false);
								InventorySemaphore.Release();
							}
						);
					}
				}
			}
		}

		internal async Task<Dictionary<uint, string>> GetMyOwnedGames() {
			const string request = "/my/games?l=english&xml=1";

			XmlDocument response = await UrlGetToXmlDocumentWithSession(SteamCommunityURL, request, false).ConfigureAwait(false);

			XmlNodeList xmlNodeList = response?.SelectNodes("gamesList/games/game");

			if ((xmlNodeList == null) || (xmlNodeList.Count == 0)) {
				return null;
			}

			Dictionary<uint, string> result = new Dictionary<uint, string>(xmlNodeList.Count);

			foreach (XmlNode xmlNode in xmlNodeList) {
				XmlNode appNode = xmlNode.SelectSingleNode("appID");

				if (appNode == null) {
					Bot.ArchiLogger.LogNullError(nameof(appNode));

					return null;
				}

				if (!uint.TryParse(appNode.InnerText, out uint appID) || (appID == 0)) {
					Bot.ArchiLogger.LogNullError(nameof(appID));

					return null;
				}

				XmlNode nameNode = xmlNode.SelectSingleNode("name");

				if (nameNode == null) {
					Bot.ArchiLogger.LogNullError(nameof(nameNode));

					return null;
				}

				result[appID] = nameNode.InnerText;
			}

			return result;
		}

		internal async Task<Dictionary<uint, string>> GetOwnedGames(ulong steamID) {
			if (steamID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(steamID));

				return null;
			}

			(bool success, string steamApiKey) = await CachedApiKey.GetValue().ConfigureAwait(false);

			if (!success || string.IsNullOrEmpty(steamApiKey)) {
				return null;
			}

			KeyValue response = null;

			for (byte i = 0; (i < WebBrowser.MaxTries) && (response == null); i++) {
				using (dynamic iPlayerService = WebAPI.GetAsyncInterface(IPlayerService, steamApiKey)) {
					iPlayerService.Timeout = WebBrowser.Timeout;

					try {
						response = await WebLimitRequest(
							WebAPI.DefaultBaseAddress.Host,

							// ReSharper disable once AccessToDisposedClosure
							async () => await iPlayerService.GetOwnedGames(
								include_appinfo: 1,
								steamid: steamID
							)
						).ConfigureAwait(false);
					} catch (TaskCanceledException e) {
						Bot.ArchiLogger.LogGenericDebuggingException(e);
					} catch (Exception e) {
						Bot.ArchiLogger.LogGenericWarningException(e);
					}
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

				result[appID] = game["name"].Value;
			}

			return result;
		}

		internal async Task<uint> GetServerTime() {
			KeyValue response = null;

			for (byte i = 0; (i < WebBrowser.MaxTries) && (response == null); i++) {
				using (dynamic iTwoFactorService = WebAPI.GetAsyncInterface(ITwoFactorService)) {
					iTwoFactorService.Timeout = WebBrowser.Timeout;

					try {
						response = await WebLimitRequest(
							WebAPI.DefaultBaseAddress.Host,

							// ReSharper disable once AccessToDisposedClosure
							async () => await iTwoFactorService.QueryTime(method: WebRequestMethods.Http.Post)
						).ConfigureAwait(false);
					} catch (TaskCanceledException e) {
						Bot.ArchiLogger.LogGenericDebuggingException(e);
					} catch (Exception e) {
						Bot.ArchiLogger.LogGenericWarningException(e);
					}
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
				Bot.ArchiLogger.LogNullError(nameof(tradeID));

				return null;
			}

			string request = "/tradeoffer/" + tradeID + "?l=english";

			HtmlDocument htmlDocument = await UrlGetToHtmlDocumentWithSession(SteamCommunityURL, request).ConfigureAwait(false);

			HtmlNode htmlNode = htmlDocument?.DocumentNode.SelectSingleNode("//div[@class='pagecontent']/script");

			if (htmlNode == null) {
				// Trade can be no longer valid
				return null;
			}

			string text = htmlNode.InnerText;

			if (string.IsNullOrEmpty(text)) {
				Bot.ArchiLogger.LogNullError(nameof(text));

				return null;
			}

			int index = text.IndexOf("g_daysTheirEscrow = ", StringComparison.Ordinal);

			if (index < 0) {
				Bot.ArchiLogger.LogNullError(nameof(index));

				return null;
			}

			index += 20;
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

		internal async Task<byte?> GetTradeHoldDurationForUser(ulong steamID, string tradeToken = null) {
			if (steamID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(steamID));

				return null;
			}

			(bool success, string steamApiKey) = await CachedApiKey.GetValue().ConfigureAwait(false);

			if (!success || string.IsNullOrEmpty(steamApiKey)) {
				return null;
			}

			KeyValue response = null;

			for (byte i = 0; (i < WebBrowser.MaxTries) && (response == null); i++) {
				using (dynamic iEconService = WebAPI.GetAsyncInterface(IEconService, steamApiKey)) {
					iEconService.Timeout = WebBrowser.Timeout;

					try {
						response = await WebLimitRequest(
							WebAPI.DefaultBaseAddress.Host,

							// ReSharper disable once AccessToDisposedClosure
							async () => await iEconService.GetTradeHoldDurations(
								steamid_target: steamID,
								trade_offer_access_token: tradeToken ?? "" // TODO: Change me once https://github.com/SteamRE/SteamKit/pull/522 is merged
							)
						).ConfigureAwait(false);
					} catch (TaskCanceledException e) {
						Bot.ArchiLogger.LogGenericDebuggingException(e);
					} catch (Exception e) {
						Bot.ArchiLogger.LogGenericWarningException(e);
					}
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
				Bot.ArchiLogger.LogNullError(nameof(deviceID) + " || " + nameof(confirmationHash) + " || " + nameof(time) + " || " + nameof(confirmationID) + " || " + nameof(confirmationKey));

				return null;
			}

			if (SteamID == 0) {
				for (byte i = 0; (i < Program.GlobalConfig.ConnectionTimeout) && (SteamID == 0) && Bot.IsConnectedAndLoggedOn; i++) {
					await Task.Delay(1000).ConfigureAwait(false);
				}

				if (SteamID == 0) {
					Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);

					return null;
				}
			}

			string request = "/mobileconf/ajaxop?a=" + SteamID + "&cid=" + confirmationID + "&ck=" + confirmationKey + "&k=" + WebUtility.UrlEncode(confirmationHash) + "&l=english&m=android&op=" + (accept ? "allow" : "cancel") + "&p=" + WebUtility.UrlEncode(deviceID) + "&t=" + time + "&tag=conf";

			Steam.BooleanResponse response = await UrlGetToJsonObjectWithSession<Steam.BooleanResponse>(SteamCommunityURL, request).ConfigureAwait(false);

			return response?.Success;
		}

		internal async Task<bool?> HandleConfirmations(string deviceID, string confirmationHash, uint time, IReadOnlyCollection<MobileAuthenticator.Confirmation> confirmations, bool accept) {
			if (string.IsNullOrEmpty(deviceID) || string.IsNullOrEmpty(confirmationHash) || (time == 0) || (confirmations == null) || (confirmations.Count == 0)) {
				Bot.ArchiLogger.LogNullError(nameof(deviceID) + " || " + nameof(confirmationHash) + " || " + nameof(time) + " || " + nameof(confirmations));

				return null;
			}

			if (SteamID == 0) {
				for (byte i = 0; (i < Program.GlobalConfig.ConnectionTimeout) && (SteamID == 0) && Bot.IsConnectedAndLoggedOn; i++) {
					await Task.Delay(1000).ConfigureAwait(false);
				}

				if (SteamID == 0) {
					Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);

					return null;
				}
			}

			const string request = "/mobileconf/multiajaxop";

			// Extra entry for sessionID
			List<KeyValuePair<string, string>> data = new List<KeyValuePair<string, string>>(8 + confirmations.Count * 2) {
				new KeyValuePair<string, string>("a", SteamID.ToString()),
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

			Steam.BooleanResponse response = await UrlPostToJsonObjectWithSession<Steam.BooleanResponse>(SteamCommunityURL, request, data).ConfigureAwait(false);

			return response?.Success;
		}

		internal async Task<bool> HasPublicInventory() {
			(bool success, bool hasPublicInventory) = await CachedPublicInventory.GetValue().ConfigureAwait(false);

			return success && hasPublicInventory;
		}

		internal async Task<bool> HasValidApiKey() {
			(bool success, string steamApiKey) = await CachedApiKey.GetValue().ConfigureAwait(false);

			return success && !string.IsNullOrEmpty(steamApiKey);
		}

		internal async Task<bool> Init(ulong steamID, EUniverse universe, string webAPIUserNonce, string parentalCode = null) {
			if ((steamID == 0) || (universe == EUniverse.Invalid) || string.IsNullOrEmpty(webAPIUserNonce)) {
				Bot.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(universe) + " || " + nameof(webAPIUserNonce));

				return false;
			}

			string sessionID = Convert.ToBase64String(Encoding.UTF8.GetBytes(steamID.ToString()));

			// Generate an AES session key
			byte[] sessionKey = CryptoHelper.GenerateRandomBlock(32);

			// RSA encrypt it with the public key for the universe we're on
			byte[] encryptedSessionKey;

			using (RSACrypto rsa = new RSACrypto(KeyDictionary.GetPublicKey(universe))) {
				encryptedSessionKey = rsa.Encrypt(sessionKey);
			}

			// Copy our login key
			byte[] loginKey = new byte[webAPIUserNonce.Length];
			Array.Copy(Encoding.ASCII.GetBytes(webAPIUserNonce), loginKey, webAPIUserNonce.Length);

			// AES encrypt the login key with our session key
			byte[] encryptedLoginKey = CryptoHelper.SymmetricEncrypt(loginKey, sessionKey);

			// Do the magic
			Bot.ArchiLogger.LogGenericInfo(string.Format(Strings.LoggingIn, ISteamUserAuth));

			KeyValue response;

			// We do not use usual retry pattern here as webAPIUserNonce is valid only for a single request
			// Even during timeout, webAPIUserNonce is most likely already invalid
			// Instead, the caller is supposed to ask for new webAPIUserNonce and call Init() again on failure
			using (dynamic iSteamUserAuth = WebAPI.GetAsyncInterface(ISteamUserAuth)) {
				iSteamUserAuth.Timeout = WebBrowser.Timeout;

				try {
					response = await WebLimitRequest(
						WebAPI.DefaultBaseAddress.Host,

						// ReSharper disable once AccessToDisposedClosure
						async () => await iSteamUserAuth.AuthenticateUser(
							encrypted_loginkey: Encoding.ASCII.GetString(WebUtility.UrlEncodeToBytes(encryptedLoginKey, 0, encryptedLoginKey.Length)),
							method: WebRequestMethods.Http.Post,
							sessionkey: Encoding.ASCII.GetString(WebUtility.UrlEncodeToBytes(encryptedSessionKey, 0, encryptedSessionKey.Length)),
							steamid: steamID
						)
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

			string steamLogin = response["token"].Value;

			if (string.IsNullOrEmpty(steamLogin)) {
				Bot.ArchiLogger.LogNullError(nameof(steamLogin));

				return false;
			}

			string steamLoginSecure = response["tokensecure"].Value;

			if (string.IsNullOrEmpty(steamLoginSecure)) {
				Bot.ArchiLogger.LogNullError(nameof(steamLoginSecure));

				return false;
			}

			WebBrowser.CookieContainer.Add(new Cookie("sessionid", sessionID, "/", "." + SteamCommunityHost));
			WebBrowser.CookieContainer.Add(new Cookie("sessionid", sessionID, "/", "." + SteamStoreHost));

			WebBrowser.CookieContainer.Add(new Cookie("steamLogin", steamLogin, "/", "." + SteamCommunityHost));
			WebBrowser.CookieContainer.Add(new Cookie("steamLogin", steamLogin, "/", "." + SteamStoreHost));

			WebBrowser.CookieContainer.Add(new Cookie("steamLoginSecure", steamLoginSecure, "/", "." + SteamCommunityHost));
			WebBrowser.CookieContainer.Add(new Cookie("steamLoginSecure", steamLoginSecure, "/", "." + SteamStoreHost));

			Bot.ArchiLogger.LogGenericInfo(Strings.Success);

			// Unlock Steam Parental if needed
			if ((parentalCode != null) && (parentalCode.Length == 4)) {
				if (!await UnlockParentalAccount(parentalCode).ConfigureAwait(false)) {
					return false;
				}
			}

			SteamID = steamID;
			LastSessionCheck = LastSessionRefresh = DateTime.UtcNow;

			return true;
		}

		internal async Task<bool> JoinGroup(ulong groupID) {
			if (groupID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(groupID));

				return false;
			}

			string request = "/gid/" + groupID;

			// Extra entry for sessionID
			Dictionary<string, string> data = new Dictionary<string, string>(2) { { "action", "join" } };

			return await UrlPostWithSession(SteamCommunityURL, request, data, session: ESession.CamelCase).ConfigureAwait(false);
		}

		internal async Task MarkInventory() {
			// We aim to have a maximum of 2 tasks, one already working, and one waiting in the queue
			// This way we can call this function as many times as needed e.g. because of Steam events
			lock (InventorySemaphore) {
				if (MarkingInventoryScheduled) {
					return;
				}

				MarkingInventoryScheduled = true;
			}

			await InventorySemaphore.WaitAsync().ConfigureAwait(false);

			try {
				lock (InventorySemaphore) {
					MarkingInventoryScheduled = false;
				}

				const string request = "/my/inventory";
				await UrlHeadWithSession(SteamCommunityURL, request, false).ConfigureAwait(false);
			} finally {
				if (Program.GlobalConfig.InventoryLimiterDelay == 0) {
					InventorySemaphore.Release();
				} else {
					Utilities.InBackground(
						async () => {
							await Task.Delay(Program.GlobalConfig.InventoryLimiterDelay * 1000).ConfigureAwait(false);
							InventorySemaphore.Release();
						}
					);
				}
			}
		}

		internal async Task<bool> MarkSentTrades() {
			const string request = "/my/tradeoffers/sent";

			return await UrlHeadWithSession(SteamCommunityURL, request, false).ConfigureAwait(false);
		}

		internal void OnDisconnected() {
			SteamID = 0;
			Utilities.InBackground(CachedApiKey.Reset);
			Utilities.InBackground(CachedPublicInventory.Reset);
		}

		internal void OnVanityURLChanged(string vanityURL = null) => VanityURL = string.IsNullOrEmpty(vanityURL) ? null : vanityURL;

		internal async Task<(EResult Result, EPurchaseResultDetail? PurchaseResult)?> RedeemWalletKey(string key) {
			if (string.IsNullOrEmpty(key)) {
				Bot.ArchiLogger.LogNullError(nameof(key));

				return null;
			}

			// ASF should redeem wallet key only in case of existing wallet
			if (Bot.WalletCurrency == ECurrencyCode.Invalid) {
				Bot.ArchiLogger.LogNullError(nameof(Bot.WalletCurrency));

				return null;
			}

			const string requestValidateCode = "/account/validatewalletcode";

			// Extra entry for sessionID
			Dictionary<string, string> data = new Dictionary<string, string>(2) { { "wallet_code", key } };

			Steam.RedeemWalletResponse responseValidateCode = await UrlPostToJsonObjectWithSession<Steam.RedeemWalletResponse>(SteamStoreURL, requestValidateCode, data).ConfigureAwait(false);

			if (responseValidateCode == null) {
				return null;
			}

			// We can not trust EResult response, because it is OK even in the case of error, so changing it to Fail in this case
			if ((responseValidateCode.Result != EResult.OK) || (responseValidateCode.PurchaseResultDetail != EPurchaseResultDetail.NoDetail)) {
				return (responseValidateCode.Result == EResult.OK ? EResult.Fail : responseValidateCode.Result, responseValidateCode.PurchaseResultDetail);
			}

			if (responseValidateCode.KeyDetails == null) {
				Bot.ArchiLogger.LogNullError(nameof(responseValidateCode.KeyDetails));

				return null;
			}

			if (responseValidateCode.WalletCurrencyCode != responseValidateCode.KeyDetails.CurrencyCode) {
				const string requestCheckFunds = "/account/createwalletandcheckfunds";
				Steam.EResultResponse responseCheckFunds = await UrlPostToJsonObjectWithSession<Steam.EResultResponse>(SteamStoreURL, requestCheckFunds, data).ConfigureAwait(false);

				if (responseCheckFunds == null) {
					return null;
				}

				if (responseCheckFunds.Result != EResult.OK) {
					return (responseCheckFunds.Result, null);
				}
			}

			const string requestConfirmRedeem = "/account/confirmredeemwalletcode";
			Steam.RedeemWalletResponse responseConfirmRedeem = await UrlPostToJsonObjectWithSession<Steam.RedeemWalletResponse>(SteamStoreURL, requestConfirmRedeem, data).ConfigureAwait(false);

			if (responseConfirmRedeem == null) {
				return null;
			}

			// Same, returning OK EResult only if PurchaseResultDetail is NoDetail (no error occured)
			return ((responseConfirmRedeem.PurchaseResultDetail == EPurchaseResultDetail.NoDetail) && (responseConfirmRedeem.Result == EResult.OK) ? responseConfirmRedeem.Result : EResult.Fail, responseConfirmRedeem.PurchaseResultDetail);
		}

		internal async Task<(bool Success, HashSet<ulong> MobileTradeOfferIDs)> SendTradeOffer(ulong partnerID, IReadOnlyCollection<Steam.Asset> itemsToGive = null, IReadOnlyCollection<Steam.Asset> itemsToReceive = null, string token = null, bool forcedSingleOffer = false) {
			if ((partnerID == 0) || (((itemsToGive == null) || (itemsToGive.Count == 0)) && ((itemsToReceive == null) || (itemsToReceive.Count == 0)))) {
				Bot.ArchiLogger.LogNullError(nameof(partnerID) + " || (" + nameof(itemsToGive) + " && " + nameof(itemsToReceive) + ")");

				return (false, null);
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
			Dictionary<string, string> data = new Dictionary<string, string>(6) {
				{ "partner", partnerID.ToString() },
				{ "serverid", "1" },
				{ "trade_offer_create_params", string.IsNullOrEmpty(token) ? "" : new JObject { { "trade_offer_access_token", token } }.ToString(Formatting.None) },
				{ "tradeoffermessage", "Sent by " + SharedInfo.PublicIdentifier + "/" + SharedInfo.Version }
			};

			HashSet<ulong> mobileTradeOfferIDs = new HashSet<ulong>();

			foreach (Steam.TradeOfferSendRequest trade in trades) {
				data["json_tradeoffer"] = JsonConvert.SerializeObject(trade);

				Steam.TradeOfferSendResponse response = await UrlPostToJsonObjectWithSession<Steam.TradeOfferSendResponse>(SteamCommunityURL, request, data, referer).ConfigureAwait(false);

				if (response == null) {
					return (false, mobileTradeOfferIDs);
				}

				if (response.RequiresMobileConfirmation) {
					mobileTradeOfferIDs.Add(response.TradeOfferID);
				}
			}

			return (true, mobileTradeOfferIDs);
		}

		internal async Task<bool> UnpackBooster(uint appID, ulong itemID) {
			if ((appID == 0) || (itemID == 0)) {
				Bot.ArchiLogger.LogNullError(nameof(appID) + " || " + nameof(itemID));

				return false;
			}

			string profileURL = await GetAbsoluteProfileURL().ConfigureAwait(false);

			if (string.IsNullOrEmpty(profileURL)) {
				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);

				return false;
			}

			string request = profileURL + "/ajaxunpackbooster";

			// Extra entry for sessionID
			Dictionary<string, string> data = new Dictionary<string, string>(3) {
				{ "appid", appID.ToString() },
				{ "communityitemid", itemID.ToString() }
			};

			Steam.EResultResponse response = await UrlPostToJsonObjectWithSession<Steam.EResultResponse>(SteamCommunityURL, request, data).ConfigureAwait(false);

			return response?.Result == EResult.OK;
		}

		private async Task<string> GetAbsoluteProfileURL(bool waitForInitialization = true) {
			if (waitForInitialization && (SteamID == 0)) {
				for (byte i = 0; (i < Program.GlobalConfig.ConnectionTimeout) && (SteamID == 0) && Bot.IsConnectedAndLoggedOn; i++) {
					await Task.Delay(1000).ConfigureAwait(false);
				}

				if (SteamID == 0) {
					Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);

					return null;
				}
			}

			return string.IsNullOrEmpty(VanityURL) ? "/profiles/" + SteamID : "/id/" + VanityURL;
		}

		private async Task<(ESteamApiKeyState State, string Key)> GetApiKeyState() {
			const string request = "/dev/apikey?l=english";
			HtmlDocument htmlDocument = await UrlGetToHtmlDocumentWithSession(SteamCommunityURL, request).ConfigureAwait(false);

			HtmlNode titleNode = htmlDocument?.DocumentNode.SelectSingleNode("//div[@id='mainContents']/h2");

			if (titleNode == null) {
				return (ESteamApiKeyState.Timeout, null);
			}

			string title = titleNode.InnerText;

			if (string.IsNullOrEmpty(title)) {
				Bot.ArchiLogger.LogNullError(nameof(title));

				return (ESteamApiKeyState.Error, null);
			}

			if (title.Contains("Access Denied") || title.Contains("Validated email address required")) {
				return (ESteamApiKeyState.AccessDenied, null);
			}

			HtmlNode htmlNode = htmlDocument.DocumentNode.SelectSingleNode("//div[@id='bodyContents_ex']/p");

			if (htmlNode == null) {
				Bot.ArchiLogger.LogNullError(nameof(htmlNode));

				return (ESteamApiKeyState.Error, null);
			}

			string text = htmlNode.InnerText;

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

			if ((text.Length != 32) || !Utilities.IsValidHexadecimalString(text)) {
				Bot.ArchiLogger.LogNullError(nameof(text));

				return (ESteamApiKeyState.Error, null);
			}

			return (ESteamApiKeyState.Registered, text);
		}

		private static uint GetAppIDFromMarketHashName(string hashName) {
			if (string.IsNullOrEmpty(hashName)) {
				ASF.ArchiLogger.LogNullError(nameof(hashName));

				return 0;
			}

			int index = hashName.IndexOf('-');

			return (index > 0) && uint.TryParse(hashName.Substring(0, index), out uint appID) && (appID != 0) ? appID : 0;
		}

		private static Steam.Asset.EType GetItemType(string name) {
			if (string.IsNullOrEmpty(name)) {
				ASF.ArchiLogger.LogNullError(nameof(name));

				return Steam.Asset.EType.Unknown;
			}

			switch (name) {
				case "Booster Pack":

					return Steam.Asset.EType.BoosterPack;
				case "Steam Gems":

					return Steam.Asset.EType.SteamGems;
				default:

					if (name.EndsWith("Emoticon", StringComparison.Ordinal)) {
						return Steam.Asset.EType.Emoticon;
					}

					if (name.EndsWith("Foil Trading Card", StringComparison.Ordinal)) {
						return Steam.Asset.EType.FoilTradingCard;
					}

					if (name.EndsWith("Profile Background", StringComparison.Ordinal)) {
						return Steam.Asset.EType.ProfileBackground;
					}

					return name.EndsWith("Trading Card", StringComparison.Ordinal) ? Steam.Asset.EType.TradingCard : Steam.Asset.EType.Unknown;
			}
		}

		private async Task<bool> IsProfileUri(Uri uri, bool waitForInitialization = true) {
			if (uri == null) {
				ASF.ArchiLogger.LogNullError(nameof(uri));

				return false;
			}

			string profileURL = await GetAbsoluteProfileURL(waitForInitialization).ConfigureAwait(false);

			if (string.IsNullOrEmpty(profileURL)) {
				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);

				return false;
			}

			return uri.AbsolutePath.Equals(profileURL);
		}

		private async Task<bool?> IsSessionExpired() {
			if (DateTime.UtcNow < LastSessionCheck.AddSeconds(MinSessionValidityInSeconds)) {
				return LastSessionCheck != LastSessionRefresh;
			}

			await SessionSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				if (DateTime.UtcNow < LastSessionCheck.AddSeconds(MinSessionValidityInSeconds)) {
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

				WebBrowser.BasicResponse response = await WebLimitRequest(host, async () => await WebBrowser.UrlHead(host + request).ConfigureAwait(false)).ConfigureAwait(false);

				if (response?.FinalUri == null) {
					return null;
				}

				bool result = IsSessionExpiredUri(response.FinalUri);

				DateTime now = DateTime.UtcNow;

				if (!result) {
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
				ASF.ArchiLogger.LogNullError(nameof(uri));

				return false;
			}

			return uri.AbsolutePath.StartsWith("/login", StringComparison.Ordinal) || uri.Host.Equals("lostauth");
		}

		private static bool ParseItems(Dictionary<ulong, (uint AppID, Steam.Asset.EType Type)> descriptions, IReadOnlyCollection<KeyValue> input, ICollection<Steam.Asset> output) {
			if ((descriptions == null) || (input == null) || (input.Count == 0) || (output == null)) {
				ASF.ArchiLogger.LogNullError(nameof(descriptions) + " || " + nameof(input) + " || " + nameof(output));

				return false;
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

				uint amount = item["amount"].AsUnsignedInteger();

				if (amount == 0) {
					ASF.ArchiLogger.LogNullError(nameof(amount));

					return false;
				}

				uint realAppID = appID;
				Steam.Asset.EType type = Steam.Asset.EType.Unknown;

				if (descriptions.TryGetValue(classID, out (uint AppID, Steam.Asset.EType Type) description)) {
					realAppID = description.AppID;
					type = description.Type;
				}

				Steam.Asset steamAsset = new Steam.Asset(appID, contextID, classID, amount, realAppID, type);
				output.Add(steamAsset);
			}

			return true;
		}

		private async Task<bool> RefreshSession() {
			if (!Bot.IsConnectedAndLoggedOn) {
				return false;
			}

			DateTime now = DateTime.UtcNow;

			await SessionSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				if (now < LastSessionRefresh) {
					return true;
				}

				if (!Bot.IsConnectedAndLoggedOn) {
					return false;
				}

				Bot.ArchiLogger.LogGenericInfo(Strings.RefreshingOurSession);
				bool result = await Bot.RefreshSession().ConfigureAwait(false);

				if (result) {
					LastSessionCheck = LastSessionRefresh = DateTime.UtcNow;
				}

				return result;
			} finally {
				SessionSemaphore.Release();
			}
		}

		private async Task<bool> RegisterApiKey() {
			const string request = "/dev/registerkey";

			// Extra entry for sessionID
			Dictionary<string, string> data = new Dictionary<string, string>(4) {
				{ "agreeToTerms", "agreed" },
				{ "domain", "localhost" },
				{ "Submit", "Register" }
			};

			return await UrlPostWithSession(SteamCommunityURL, request, data).ConfigureAwait(false);
		}

		private async Task<(bool Success, string Result)> ResolveApiKey() {
			if (Bot.IsAccountLimited) {
				// API key is permanently unavailable for limited accounts
				return (true, null);
			}

			(ESteamApiKeyState State, string Key) result = await GetApiKeyState().ConfigureAwait(false);

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

		private async Task<(bool Success, bool Result)> ResolvePublicInventory() {
			const string request = "/my/edit/settings?l=english";
			HtmlDocument htmlDocument = await UrlGetToHtmlDocumentWithSession(SteamCommunityURL, request, false).ConfigureAwait(false);

			if (htmlDocument == null) {
				return (false, false);
			}

			HtmlNode htmlNode = htmlDocument.DocumentNode.SelectSingleNode("//div[@data-component='ProfilePrivacySettings']/@data-privacysettings");

			if (htmlNode == null) {
				Bot.ArchiLogger.LogNullError(nameof(htmlNode));

				return (false, false);
			}

			string json = htmlNode.GetAttributeValue("data-privacysettings", null);

			if (string.IsNullOrEmpty(json)) {
				Bot.ArchiLogger.LogNullError(nameof(json));

				return (false, false);
			}

			// This json is encoded as html attribute, don't forget to decode it
			json = WebUtility.HtmlDecode(json);

			Steam.UserPrivacy userPrivacy;

			try {
				userPrivacy = JsonConvert.DeserializeObject<Steam.UserPrivacy>(json);
			} catch (JsonException e) {
				Bot.ArchiLogger.LogGenericException(e);

				return (false, false);
			}

			if (userPrivacy == null) {
				Bot.ArchiLogger.LogNullError(nameof(userPrivacy));

				return (false, false);
			}

			switch (userPrivacy.Settings.Profile) {
				case Steam.UserPrivacy.PrivacySettings.EPrivacySetting.FriendsOnly:
				case Steam.UserPrivacy.PrivacySettings.EPrivacySetting.Private:

					return (true, false);
				case Steam.UserPrivacy.PrivacySettings.EPrivacySetting.Public:

					switch (userPrivacy.Settings.Inventory) {
						case Steam.UserPrivacy.PrivacySettings.EPrivacySetting.FriendsOnly:
						case Steam.UserPrivacy.PrivacySettings.EPrivacySetting.Private:

							return (true, false);
						case Steam.UserPrivacy.PrivacySettings.EPrivacySetting.Public:

							return (true, true);
						default:
							Bot.ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(userPrivacy.Settings.Inventory), userPrivacy.Settings.Inventory));

							return (false, false);
					}
				default:
					Bot.ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(userPrivacy.Settings.Profile), userPrivacy.Settings.Profile));

					return (false, false);
			}
		}

		private async Task<bool> UnlockParentalAccount(string parentalCode) {
			if (string.IsNullOrEmpty(parentalCode)) {
				Bot.ArchiLogger.LogNullError(nameof(parentalCode));

				return false;
			}

			Bot.ArchiLogger.LogGenericInfo(Strings.UnlockingParentalAccount);

			if (!await UnlockParentalAccountForService(SteamCommunityURL, parentalCode).ConfigureAwait(false)) {
				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);

				return false;
			}

			if (!await UnlockParentalAccountForService(SteamStoreURL, parentalCode).ConfigureAwait(false)) {
				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);

				return false;
			}

			Bot.ArchiLogger.LogGenericInfo(Strings.Success);

			return true;
		}

		private async Task<bool> UnlockParentalAccountForService(string serviceURL, string parentalCode, byte maxTries = WebBrowser.MaxTries) {
			if (string.IsNullOrEmpty(serviceURL) || string.IsNullOrEmpty(parentalCode)) {
				Bot.ArchiLogger.LogNullError(nameof(serviceURL) + " || " + nameof(parentalCode));

				return false;
			}

			const string request = "/parental/ajaxunlock";

			if (maxTries == 0) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, serviceURL + request));

				return false;
			}

			Dictionary<string, string> data = new Dictionary<string, string>(1) { { "pin", parentalCode } };

			// This request doesn't go through UrlPostRetryWithSession as we have no access to session refresh capability (this is in fact session initialization)
			WebBrowser.BasicResponse response = await WebLimitRequest(serviceURL, async () => await WebBrowser.UrlPost(serviceURL + request, data, serviceURL).ConfigureAwait(false)).ConfigureAwait(false);

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

		private async Task<HtmlDocument> UrlGetToHtmlDocumentWithSession(string host, string request, bool checkSessionPreemptively = true, byte maxTries = WebBrowser.MaxTries) {
			if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(request)) {
				Bot.ArchiLogger.LogNullError(nameof(host) + " || " + nameof(request));

				return null;
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
						return await UrlGetToHtmlDocumentWithSession(host, request, true, --maxTries).ConfigureAwait(false);
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

			if (SteamID == 0) {
				for (byte i = 0; (i < Program.GlobalConfig.ConnectionTimeout) && (SteamID == 0) && Bot.IsConnectedAndLoggedOn; i++) {
					await Task.Delay(1000).ConfigureAwait(false);
				}

				if (SteamID == 0) {
					Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
					Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

					return null;
				}
			}

			WebBrowser.HtmlDocumentResponse response = await WebLimitRequest(host, async () => await WebBrowser.UrlGetToHtmlDocument(host + request).ConfigureAwait(false)).ConfigureAwait(false);

			if (response == null) {
				return null;
			}

			if (IsSessionExpiredUri(response.FinalUri)) {
				if (await RefreshSession().ConfigureAwait(false)) {
					return await UrlGetToHtmlDocumentWithSession(host, request, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
				}

				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

				return null;
			}

			// Under special brain-damaged circumstances, Steam might just return our own profile as a response to the request, for absolutely no reason whatsoever - just try again in this case
			if (await IsProfileUri(response.FinalUri).ConfigureAwait(false)) {
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.WarningWorkaroundTriggered, nameof(IsProfileUri)));

				return await UrlGetToHtmlDocumentWithSession(host, request, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
			}

			return response.Content;
		}

		private async Task<T> UrlGetToJsonObjectWithSession<T>(string host, string request, bool checkSessionPreemptively = true, byte maxTries = WebBrowser.MaxTries) where T : class {
			if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(request)) {
				Bot.ArchiLogger.LogNullError(nameof(host) + " || " + nameof(request));

				return default;
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
						return await UrlGetToJsonObjectWithSession<T>(host, request, true, --maxTries).ConfigureAwait(false);
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

			if (SteamID == 0) {
				for (byte i = 0; (i < Program.GlobalConfig.ConnectionTimeout) && (SteamID == 0) && Bot.IsConnectedAndLoggedOn; i++) {
					await Task.Delay(1000).ConfigureAwait(false);
				}

				if (SteamID == 0) {
					Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
					Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

					return default;
				}
			}

			WebBrowser.ObjectResponse<T> response = await WebLimitRequest(host, async () => await WebBrowser.UrlGetToJsonObject<T>(host + request).ConfigureAwait(false)).ConfigureAwait(false);

			if (response == null) {
				return default;
			}

			if (IsSessionExpiredUri(response.FinalUri)) {
				if (await RefreshSession().ConfigureAwait(false)) {
					return await UrlGetToJsonObjectWithSession<T>(host, request, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
				}

				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

				return null;
			}

			// Under special brain-damaged circumstances, Steam might just return our own profile as a response to the request, for absolutely no reason whatsoever - just try again in this case
			if (await IsProfileUri(response.FinalUri).ConfigureAwait(false)) {
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.WarningWorkaroundTriggered, nameof(IsProfileUri)));

				return await UrlGetToJsonObjectWithSession<T>(host, request, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
			}

			return response.Content;
		}

		private async Task<XmlDocument> UrlGetToXmlDocumentWithSession(string host, string request, bool checkSessionPreemptively = true, byte maxTries = WebBrowser.MaxTries) {
			if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(request)) {
				Bot.ArchiLogger.LogNullError(nameof(host) + " || " + nameof(request));

				return null;
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
						return await UrlGetToXmlDocumentWithSession(host, request, true, --maxTries).ConfigureAwait(false);
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

			if (SteamID == 0) {
				for (byte i = 0; (i < Program.GlobalConfig.ConnectionTimeout) && (SteamID == 0) && Bot.IsConnectedAndLoggedOn; i++) {
					await Task.Delay(1000).ConfigureAwait(false);
				}

				if (SteamID == 0) {
					Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
					Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

					return null;
				}
			}

			WebBrowser.XmlDocumentResponse response = await WebLimitRequest(host, async () => await WebBrowser.UrlGetToXmlDocument(host + request).ConfigureAwait(false)).ConfigureAwait(false);

			if (response == null) {
				return null;
			}

			if (IsSessionExpiredUri(response.FinalUri)) {
				if (await RefreshSession().ConfigureAwait(false)) {
					return await UrlGetToXmlDocumentWithSession(host, request, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
				}

				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

				return null;
			}

			// Under special brain-damaged circumstances, Steam might just return our own profile as a response to the request, for absolutely no reason whatsoever - just try again in this case
			if (await IsProfileUri(response.FinalUri).ConfigureAwait(false)) {
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.WarningWorkaroundTriggered, nameof(IsProfileUri)));

				return await UrlGetToXmlDocumentWithSession(host, request, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
			}

			return response.Content;
		}

		private async Task<bool> UrlHeadWithSession(string host, string request, bool checkSessionPreemptively = true, byte maxTries = WebBrowser.MaxTries) {
			if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(request)) {
				Bot.ArchiLogger.LogNullError(nameof(host) + " || " + nameof(request));

				return false;
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
						return await UrlHeadWithSession(host, request, true, --maxTries).ConfigureAwait(false);
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

			if (SteamID == 0) {
				for (byte i = 0; (i < Program.GlobalConfig.ConnectionTimeout) && (SteamID == 0) && Bot.IsConnectedAndLoggedOn; i++) {
					await Task.Delay(1000).ConfigureAwait(false);
				}

				if (SteamID == 0) {
					Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
					Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

					return false;
				}
			}

			WebBrowser.BasicResponse response = await WebLimitRequest(host, async () => await WebBrowser.UrlHead(host + request).ConfigureAwait(false)).ConfigureAwait(false);

			if (response == null) {
				return false;
			}

			if (IsSessionExpiredUri(response.FinalUri)) {
				if (await RefreshSession().ConfigureAwait(false)) {
					return await UrlHeadWithSession(host, request, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
				}

				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

				return false;
			}

			// Under special brain-damaged circumstances, Steam might just return our own profile as a response to the request, for absolutely no reason whatsoever - just try again in this case
			if (await IsProfileUri(response.FinalUri).ConfigureAwait(false)) {
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.WarningWorkaroundTriggered, nameof(IsProfileUri)));

				return await UrlHeadWithSession(host, request, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
			}

			return true;
		}

		private async Task<HtmlDocument> UrlPostToHtmlDocumentWithSession(string host, string request, Dictionary<string, string> data = null, string referer = null, ESession session = ESession.Lowercase, bool checkSessionPreemptively = true, byte maxTries = WebBrowser.MaxTries) {
			if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(request)) {
				Bot.ArchiLogger.LogNullError(nameof(host) + " || " + nameof(request));

				return null;
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
						return await UrlPostToHtmlDocumentWithSession(host, request, data, referer, session, true, --maxTries).ConfigureAwait(false);
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

			if (SteamID == 0) {
				for (byte i = 0; (i < Program.GlobalConfig.ConnectionTimeout) && (SteamID == 0) && Bot.IsConnectedAndLoggedOn; i++) {
					await Task.Delay(1000).ConfigureAwait(false);
				}

				if (SteamID == 0) {
					Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
					Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

					return null;
				}
			}

			if (session != ESession.None) {
				string sessionID = WebBrowser.CookieContainer.GetCookieValue(host, "sessionid");

				if (string.IsNullOrEmpty(sessionID)) {
					Bot.ArchiLogger.LogNullError(nameof(sessionID));

					return null;
				}

				string sessionName;

				switch (session) {
					case ESession.CamelCase:
						sessionName = "sessionID";

						break;
					case ESession.Lowercase:
						sessionName = "sessionid";

						break;
					default:
						Bot.ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(session), session));

						return null;
				}

				if (data != null) {
					data[sessionName] = sessionID;
				} else {
					data = new Dictionary<string, string>(1) { { sessionName, sessionID } };
				}
			}

			WebBrowser.HtmlDocumentResponse response = await WebLimitRequest(host, async () => await WebBrowser.UrlPostToHtmlDocument(host + request, data, referer).ConfigureAwait(false)).ConfigureAwait(false);

			if (response == null) {
				return null;
			}

			if (IsSessionExpiredUri(response.FinalUri)) {
				if (await RefreshSession().ConfigureAwait(false)) {
					return await UrlPostToHtmlDocumentWithSession(host, request, data, referer, session, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
				}

				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

				return null;
			}

			// Under special brain-damaged circumstances, Steam might just return our own profile as a response to the request, for absolutely no reason whatsoever - just try again in this case
			if (await IsProfileUri(response.FinalUri).ConfigureAwait(false)) {
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.WarningWorkaroundTriggered, nameof(IsProfileUri)));

				return await UrlPostToHtmlDocumentWithSession(host, request, data, referer, session, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
			}

			return response.Content;
		}

		private async Task<T> UrlPostToJsonObjectWithSession<T>(string host, string request, Dictionary<string, string> data = null, string referer = null, ESession session = ESession.Lowercase, bool checkSessionPreemptively = true, byte maxTries = WebBrowser.MaxTries) where T : class {
			if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(request)) {
				Bot.ArchiLogger.LogNullError(nameof(host) + " || " + nameof(request));

				return null;
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
						return await UrlPostToJsonObjectWithSession<T>(host, request, data, referer, session, true, --maxTries).ConfigureAwait(false);
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

			if (SteamID == 0) {
				for (byte i = 0; (i < Program.GlobalConfig.ConnectionTimeout) && (SteamID == 0) && Bot.IsConnectedAndLoggedOn; i++) {
					await Task.Delay(1000).ConfigureAwait(false);
				}

				if (SteamID == 0) {
					Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
					Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

					return null;
				}
			}

			if (session != ESession.None) {
				string sessionID = WebBrowser.CookieContainer.GetCookieValue(host, "sessionid");

				if (string.IsNullOrEmpty(sessionID)) {
					Bot.ArchiLogger.LogNullError(nameof(sessionID));

					return null;
				}

				string sessionName;

				switch (session) {
					case ESession.CamelCase:
						sessionName = "sessionID";

						break;
					case ESession.Lowercase:
						sessionName = "sessionid";

						break;
					default:
						Bot.ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(session), session));

						return null;
				}

				if (data != null) {
					data[sessionName] = sessionID;
				} else {
					data = new Dictionary<string, string>(1) { { sessionName, sessionID } };
				}
			}

			WebBrowser.ObjectResponse<T> response = await WebLimitRequest(host, async () => await WebBrowser.UrlPostToJsonObject<T>(host + request, data, referer).ConfigureAwait(false)).ConfigureAwait(false);

			if (response == null) {
				return null;
			}

			if (IsSessionExpiredUri(response.FinalUri)) {
				if (await RefreshSession().ConfigureAwait(false)) {
					return await UrlPostToJsonObjectWithSession<T>(host, request, data, referer, session, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
				}

				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

				return null;
			}

			// Under special brain-damaged circumstances, Steam might just return our own profile as a response to the request, for absolutely no reason whatsoever - just try again in this case
			if (await IsProfileUri(response.FinalUri).ConfigureAwait(false)) {
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.WarningWorkaroundTriggered, nameof(IsProfileUri)));

				return await UrlPostToJsonObjectWithSession<T>(host, request, data, referer, session, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
			}

			return response.Content;
		}

		private async Task<T> UrlPostToJsonObjectWithSession<T>(string host, string request, List<KeyValuePair<string, string>> data = null, string referer = null, ESession session = ESession.Lowercase, bool checkSessionPreemptively = true, byte maxTries = WebBrowser.MaxTries) where T : class {
			if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(request)) {
				Bot.ArchiLogger.LogNullError(nameof(host) + " || " + nameof(request));

				return null;
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
						return await UrlPostToJsonObjectWithSession<T>(host, request, data, referer, session, true, --maxTries).ConfigureAwait(false);
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

			if (SteamID == 0) {
				for (byte i = 0; (i < Program.GlobalConfig.ConnectionTimeout) && (SteamID == 0) && Bot.IsConnectedAndLoggedOn; i++) {
					await Task.Delay(1000).ConfigureAwait(false);
				}

				if (SteamID == 0) {
					Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
					Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

					return null;
				}
			}

			if (session != ESession.None) {
				string sessionID = WebBrowser.CookieContainer.GetCookieValue(host, "sessionid");

				if (string.IsNullOrEmpty(sessionID)) {
					Bot.ArchiLogger.LogNullError(nameof(sessionID));

					return null;
				}

				string sessionName;

				switch (session) {
					case ESession.CamelCase:
						sessionName = "sessionID";

						break;
					case ESession.Lowercase:
						sessionName = "sessionid";

						break;
					default:
						Bot.ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(session), session));

						return null;
				}

				KeyValuePair<string, string> sessionValue = new KeyValuePair<string, string>(sessionName, sessionID);

				if (data != null) {
					data.Remove(sessionValue);
					data.Add(sessionValue);
				} else {
					data = new List<KeyValuePair<string, string>>(1) { sessionValue };
				}
			}

			WebBrowser.ObjectResponse<T> response = await WebLimitRequest(host, async () => await WebBrowser.UrlPostToJsonObject<T>(host + request, data, referer).ConfigureAwait(false)).ConfigureAwait(false);

			if (response == null) {
				return null;
			}

			if (IsSessionExpiredUri(response.FinalUri)) {
				if (await RefreshSession().ConfigureAwait(false)) {
					return await UrlPostToJsonObjectWithSession<T>(host, request, data, referer, session, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
				}

				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

				return null;
			}

			// Under special brain-damaged circumstances, Steam might just return our own profile as a response to the request, for absolutely no reason whatsoever - just try again in this case
			if (await IsProfileUri(response.FinalUri).ConfigureAwait(false)) {
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.WarningWorkaroundTriggered, nameof(IsProfileUri)));

				return await UrlPostToJsonObjectWithSession<T>(host, request, data, referer, session, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
			}

			return response.Content;
		}

		private async Task<bool> UrlPostWithSession(string host, string request, Dictionary<string, string> data = null, string referer = null, ESession session = ESession.Lowercase, bool checkSessionPreemptively = true, byte maxTries = WebBrowser.MaxTries) {
			if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(request)) {
				Bot.ArchiLogger.LogNullError(nameof(host) + " || " + nameof(request));

				return false;
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
						return await UrlPostWithSession(host, request, data, referer, session, true, --maxTries).ConfigureAwait(false);
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

			if (SteamID == 0) {
				for (byte i = 0; (i < Program.GlobalConfig.ConnectionTimeout) && (SteamID == 0) && Bot.IsConnectedAndLoggedOn; i++) {
					await Task.Delay(1000).ConfigureAwait(false);
				}

				if (SteamID == 0) {
					Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
					Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

					return false;
				}
			}

			if (session != ESession.None) {
				string sessionID = WebBrowser.CookieContainer.GetCookieValue(host, "sessionid");

				if (string.IsNullOrEmpty(sessionID)) {
					Bot.ArchiLogger.LogNullError(nameof(sessionID));

					return false;
				}

				string sessionName;

				switch (session) {
					case ESession.CamelCase:
						sessionName = "sessionID";

						break;
					case ESession.Lowercase:
						sessionName = "sessionid";

						break;
					default:
						Bot.ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(session), session));

						return false;
				}

				if (data != null) {
					data[sessionName] = sessionID;
				} else {
					data = new Dictionary<string, string>(1) { { sessionName, sessionID } };
				}
			}

			WebBrowser.BasicResponse response = await WebLimitRequest(host, async () => await WebBrowser.UrlPost(host + request, data, referer).ConfigureAwait(false)).ConfigureAwait(false);

			if (response == null) {
				return false;
			}

			if (IsSessionExpiredUri(response.FinalUri)) {
				if (await RefreshSession().ConfigureAwait(false)) {
					return await UrlPostWithSession(host, request, data, referer, session, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
				}

				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

				return false;
			}

			// Under special brain-damaged circumstances, Steam might just return our own profile as a response to the request, for absolutely no reason whatsoever - just try again in this case
			if (await IsProfileUri(response.FinalUri).ConfigureAwait(false)) {
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.WarningWorkaroundTriggered, nameof(IsProfileUri)));

				return await UrlPostWithSession(host, request, data, referer, session, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
			}

			return true;
		}

		private static async Task<T> WebLimitRequest<T>(string service, Func<Task<T>> function) {
			if (string.IsNullOrEmpty(service) || (function == null)) {
				ASF.ArchiLogger.LogNullError(nameof(service) + " || " + nameof(function));

				return default;
			}

			if (Program.GlobalConfig.WebLimiterDelay == 0) {
				return await function().ConfigureAwait(false);
			}

			if (!WebLimitingSemaphores.TryGetValue(service, out (SemaphoreSlim RateLimitingSemaphore, SemaphoreSlim OpenConnectionsSemaphore) limiters)) {
				ASF.ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(service), service));

				return await function().ConfigureAwait(false);
			}

			// Sending a request opens a new connection
			await limiters.OpenConnectionsSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				// It also increases number of requests
				await limiters.RateLimitingSemaphore.WaitAsync().ConfigureAwait(false);

				// We release rate-limiter semaphore regardless of our task completion, since we use that one only to guarantee rate-limiting of their creation
				Utilities.InBackground(
					async () => {
						await Task.Delay(Program.GlobalConfig.WebLimiterDelay).ConfigureAwait(false);
						limiters.RateLimitingSemaphore.Release();
					}
				);

				return await function().ConfigureAwait(false);
			} finally {
				// We release open connections semaphore only once we're indeed done sending a particular request
				limiters.OpenConnectionsSemaphore.Release();
			}
		}

		private enum ESession : byte {
			None,
			Lowercase,
			CamelCase
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
