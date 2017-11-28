//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// 
//  Copyright 2015-2017 Łukasz "JustArchi" Domeradzki
//  Contact: JustArchi@JustArchi.net
// 
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
// 
//  http://www.apache.org/licenses/LICENSE-2.0
//      
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using ArchiSteamFarm.JSON;
using ArchiSteamFarm.Localization;
using HtmlAgilityPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SteamKit2;
using Formatting = Newtonsoft.Json.Formatting;

namespace ArchiSteamFarm {
	internal sealed class ArchiWebHandler : IDisposable {
		internal const byte MinSessionTTL = GlobalConfig.DefaultConnectionTimeout / 6; // Assume session is valid for at least that amount of seconds

		private const string IEconService = "IEconService";
		private const string IPlayerService = "IPlayerService";
		private const string ISteamUserAuth = "ISteamUserAuth";
		private const string ITwoFactorService = "ITwoFactorService";

		// We must use HTTPS for SteamCommunity, as http would make certain POST requests failing (trades)
		private const string SteamCommunityHost = "steamcommunity.com";

		private const string SteamCommunityURL = "https://" + SteamCommunityHost;

		// We could (and should) use HTTPS for SteamStore, but that would make certain POST requests failing
		private const string SteamStoreHost = "store.steampowered.com";

		private const string SteamStoreURL = "http://" + SteamStoreHost;

		private static readonly SemaphoreSlim InventorySemaphore = new SemaphoreSlim(1, 1);

		private readonly SemaphoreSlim ApiKeySemaphore = new SemaphoreSlim(1, 1);
		private readonly Bot Bot;
		private readonly SemaphoreSlim PublicInventorySemaphore = new SemaphoreSlim(1, 1);
		private readonly SemaphoreSlim SessionSemaphore = new SemaphoreSlim(1, 1);
		private readonly SemaphoreSlim TradeTokenSemaphore = new SemaphoreSlim(1, 1);
		private readonly WebBrowser WebBrowser;

		private string CachedApiKey;
		private bool? CachedPublicInventory;
		private string CachedTradeToken;
		private DateTime LastSessionRefreshCheck = DateTime.MinValue;
		private bool MarkingInventoryScheduled;
		private ulong SteamID;
		private string VanityURL;

		internal ArchiWebHandler(Bot bot) {
			Bot = bot ?? throw new ArgumentNullException(nameof(bot));
			WebBrowser = new WebBrowser(bot.ArchiLogger);
		}

		public void Dispose() {
			ApiKeySemaphore.Dispose();
			PublicInventorySemaphore.Dispose();
			SessionSemaphore.Dispose();
			TradeTokenSemaphore.Dispose();
			WebBrowser.Dispose();
		}

		internal async Task<bool> AcceptTradeOffer(ulong tradeID) {
			if (tradeID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(tradeID));
				return false;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return false;
			}

			string sessionID = WebBrowser.CookieContainer.GetCookieValue(SteamCommunityURL, "sessionid");
			if (string.IsNullOrEmpty(sessionID)) {
				Bot.ArchiLogger.LogNullError(nameof(sessionID));
				return false;
			}

			string referer = SteamCommunityURL + "/tradeoffer/" + tradeID;
			string request = referer + "/accept";

			Dictionary<string, string> data = new Dictionary<string, string>(3) {
				{ "sessionid", sessionID },
				{ "serverid", "1" },
				{ "tradeofferid", tradeID.ToString() }
			};

			return await WebBrowser.UrlPostRetry(request, data, referer).ConfigureAwait(false);
		}

		internal async Task<bool> AddFreeLicense(uint subID) {
			if (subID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(subID));
				return false;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return false;
			}

			string sessionID = WebBrowser.CookieContainer.GetCookieValue(SteamCommunityURL, "sessionid");
			if (string.IsNullOrEmpty(sessionID)) {
				Bot.ArchiLogger.LogNullError(nameof(sessionID));
				return false;
			}

			const string request = SteamStoreURL + "/checkout/addfreelicense";
			Dictionary<string, string> data = new Dictionary<string, string>(3) {
				{ "sessionid", sessionID },
				{ "subid", subID.ToString() },
				{ "action", "add_to_cart" }
			};

			HtmlDocument htmlDocument = await WebBrowser.UrlPostToHtmlDocumentRetry(request, data).ConfigureAwait(false);
			return htmlDocument?.DocumentNode.SelectSingleNode("//div[@class='add_free_content_success_area']") != null;
		}

		internal async Task<bool> ClearFromDiscoveryQueue(uint appID) {
			if (appID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(appID));
				return false;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return false;
			}

			string sessionID = WebBrowser.CookieContainer.GetCookieValue(SteamStoreURL, "sessionid");
			if (string.IsNullOrEmpty(sessionID)) {
				Bot.ArchiLogger.LogNullError(nameof(sessionID));
				return false;
			}

			string request = SteamStoreURL + "/app/" + appID;
			Dictionary<string, string> data = new Dictionary<string, string>(2) {
				{ "sessionid", sessionID },
				{ "appid_to_clear_from_queue", appID.ToString() }
			};

			return await WebBrowser.UrlPostRetry(request, data).ConfigureAwait(false);
		}

		internal async Task DeclineTradeOffer(ulong tradeID) {
			if (tradeID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(tradeID));
				return;
			}

			string steamApiKey = await GetApiKey().ConfigureAwait(false);
			if (string.IsNullOrEmpty(steamApiKey)) {
				return;
			}

			KeyValue response = null;
			for (byte i = 0; (i < WebBrowser.MaxTries) && (response == null); i++) {
				using (dynamic iEconService = WebAPI.GetAsyncInterface(IEconService, steamApiKey)) {
					iEconService.Timeout = WebBrowser.Timeout;

					try {
						response = await iEconService.DeclineTradeOffer(
							tradeofferid: tradeID.ToString(),
							method: WebRequestMethods.Http.Post,
							secure: true
						);
					} catch (Exception e) {
						Bot.ArchiLogger.LogGenericWarningException(e);
					}
				}
			}

			if (response == null) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));
			}
		}

		internal async Task<HashSet<uint>> GenerateNewDiscoveryQueue() {
			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			string sessionID = WebBrowser.CookieContainer.GetCookieValue(SteamStoreURL, "sessionid");
			if (string.IsNullOrEmpty(sessionID)) {
				Bot.ArchiLogger.LogNullError(nameof(sessionID));
				return null;
			}

			const string request = SteamStoreURL + "/explore/generatenewdiscoveryqueue";
			Dictionary<string, string> data = new Dictionary<string, string>(2) {
				{ "sessionid", sessionID },
				{ "queuetype", "0" }
			};

			Steam.NewDiscoveryQueueResponse output = await WebBrowser.UrlPostToJsonResultRetry<Steam.NewDiscoveryQueueResponse>(request, data).ConfigureAwait(false);
			return output?.Queue;
		}

		internal async Task<HashSet<Steam.TradeOffer>> GetActiveTradeOffers(IReadOnlyCollection<ulong> ignoredTradeOfferIDs = null) {
			string steamApiKey = await GetApiKey().ConfigureAwait(false);
			if (string.IsNullOrEmpty(steamApiKey)) {
				return null;
			}

			KeyValue response = null;
			for (byte i = 0; (i < WebBrowser.MaxTries) && (response == null); i++) {
				using (dynamic iEconService = WebAPI.GetAsyncInterface(IEconService, steamApiKey)) {
					iEconService.Timeout = WebBrowser.Timeout;

					try {
						response = await iEconService.GetTradeOffers(
							active_only: 1,
							get_descriptions: 1,
							get_received_offers: 1,
							secure: true,
							time_historical_cutoff: uint.MaxValue
						);
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

				if (ignoredTradeOfferIDs?.Contains(tradeOfferID) == true) {
					continue;
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

		internal async Task<HtmlDocument> GetBadgePage(byte page) {
			if (page == 0) {
				Bot.ArchiLogger.LogNullError(nameof(page));
				return null;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			string request = SteamCommunityURL + "/my/badges?l=english&p=" + page;
			return await WebBrowser.UrlGetToHtmlDocumentRetry(request).ConfigureAwait(false);
		}

		internal async Task<Steam.ConfirmationDetails> GetConfirmationDetails(string deviceID, string confirmationHash, uint time, MobileAuthenticator.Confirmation confirmation) {
			if (string.IsNullOrEmpty(deviceID) || string.IsNullOrEmpty(confirmationHash) || (time == 0) || (confirmation == null)) {
				Bot.ArchiLogger.LogNullError(nameof(deviceID) + " || " + nameof(confirmationHash) + " || " + nameof(time) + " || " + nameof(confirmation));
				return null;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			string request = SteamCommunityURL + "/mobileconf/details/" + confirmation.ID + "?l=english&p=" + deviceID + "&a=" + SteamID + "&k=" + WebUtility.UrlEncode(confirmationHash) + "&t=" + time + "&m=android&tag=conf";

			Steam.ConfirmationDetails response = await WebBrowser.UrlGetToJsonResultRetry<Steam.ConfirmationDetails>(request).ConfigureAwait(false);
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

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			string request = SteamCommunityURL + "/mobileconf/conf?l=english&p=" + deviceID + "&a=" + SteamID + "&k=" + WebUtility.UrlEncode(confirmationHash) + "&t=" + time + "&m=android&tag=conf";
			return await WebBrowser.UrlGetToHtmlDocumentRetry(request).ConfigureAwait(false);
		}

		internal async Task<HtmlDocument> GetDiscoveryQueuePage() {
			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			const string request = SteamStoreURL + "/explore?l=english";
			return await WebBrowser.UrlGetToHtmlDocumentRetry(request).ConfigureAwait(false);
		}

		internal async Task<HashSet<ulong>> GetFamilySharingSteamIDs() {
			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			const string request = SteamStoreURL + "/account/managedevices";
			HtmlDocument htmlDocument = await WebBrowser.UrlGetToHtmlDocumentRetry(request).ConfigureAwait(false);

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

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			string request = SteamCommunityURL + "/my/gamecards/" + appID + "?l=english";
			return await WebBrowser.UrlGetToHtmlDocumentRetry(request).ConfigureAwait(false);
		}

		internal async Task<Dictionary<uint, string>> GetMyOwnedGames() {
			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			const string request = SteamCommunityURL + "/my/games/?xml=1";

			XmlDocument response = await WebBrowser.UrlGetToXMLRetry(request).ConfigureAwait(false);

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

				if (!uint.TryParse(appNode.InnerText, out uint appID)) {
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

		[SuppressMessage("ReSharper", "FunctionComplexityOverflow")]
		internal async Task<HashSet<Steam.Asset>> GetMySteamInventory(bool tradableOnly = false, IReadOnlyCollection<Steam.Asset.EType> wantedTypes = null, IReadOnlyCollection<uint> wantedRealAppIDs = null) {
			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			HashSet<Steam.Asset> result = new HashSet<Steam.Asset>();

			// 5000 is maximum allowed count per single request
			string request = SteamCommunityURL + "/inventory/" + SteamID + "/" + Steam.Asset.SteamAppID + "/" + Steam.Asset.SteamCommunityContextID + "?l=english&count=5000";
			ulong startAssetID = 0;

			await InventorySemaphore.WaitAsync().ConfigureAwait(false);

			try {
				while (true) {
					Steam.InventoryResponse response = await WebBrowser.UrlGetToJsonResultRetry<Steam.InventoryResponse>(request + (startAssetID > 0 ? "&start_assetid=" + startAssetID : "")).ConfigureAwait(false);

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

					Dictionary<ulong, (uint AppID, Steam.Asset.EType Type, bool Tradable)> descriptionMap = new Dictionary<ulong, (uint AppID, Steam.Asset.EType Type, bool Tradable)>();
					foreach (Steam.InventoryResponse.Description description in response.Descriptions.Where(description => description != null)) {
						if (description.ClassID == 0) {
							Bot.ArchiLogger.LogNullError(nameof(description.ClassID));
							return null;
						}

						if (descriptionMap.ContainsKey(description.ClassID)) {
							continue;
						}

						uint appID = 0;

						if (!string.IsNullOrEmpty(description.MarketHashName)) {
							appID = GetAppIDFromMarketHashName(description.MarketHashName);
						}

						if (appID == 0) {
							appID = description.AppID;
						}

						Steam.Asset.EType type = Steam.Asset.EType.Unknown;

						if (!string.IsNullOrEmpty(description.Type)) {
							type = GetItemType(description.Type);
						}

						descriptionMap[description.ClassID] = (appID, type, description.Tradable);
					}

					foreach (Steam.Asset asset in response.Assets.Where(asset => asset != null)) {
						if (descriptionMap.TryGetValue(asset.ClassID, out (uint AppID, Steam.Asset.EType Type, bool Tradable) description)) {
							if (tradableOnly && !description.Tradable) {
								continue;
							}

							asset.RealAppID = description.AppID;
							asset.Type = description.Type;
						}

						if ((wantedTypes?.Contains(asset.Type) == false) || (wantedRealAppIDs?.Contains(asset.RealAppID) == false)) {
							continue;
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
				}
			} finally {
				if (Program.GlobalConfig.InventoryLimiterDelay == 0) {
					InventorySemaphore.Release();
				} else {
					Task.Run(async () => {
						await Task.Delay(Program.GlobalConfig.InventoryLimiterDelay * 1000).ConfigureAwait(false);
						InventorySemaphore.Release();
					}).Forget();
				}
			}
		}

		internal async Task<Dictionary<uint, string>> GetOwnedGames(ulong steamID) {
			if (steamID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(steamID));
				return null;
			}

			string steamApiKey = await GetApiKey().ConfigureAwait(false);
			if (string.IsNullOrEmpty(steamApiKey)) {
				return null;
			}

			KeyValue response = null;
			for (byte i = 0; (i < WebBrowser.MaxTries) && (response == null); i++) {
				using (dynamic iPlayerService = WebAPI.GetAsyncInterface(IPlayerService, steamApiKey)) {
					iPlayerService.Timeout = WebBrowser.Timeout;

					try {
						response = await iPlayerService.GetOwnedGames(
							steamid: steamID,
							include_appinfo: 1,
							secure: true
						);
					} catch (Exception e) {
						Bot.ArchiLogger.LogGenericWarningException(e);
					}
				}
			}

			if (response == null) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));
				return null;
			}

			Dictionary<uint, string> result = new Dictionary<uint, string>(response["games"].Children.Count);
			foreach (KeyValue game in response["games"].Children) {
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
						response = await iTwoFactorService.QueryTime(
							method: WebRequestMethods.Http.Post,
							secure: true
						);
					} catch (Exception e) {
						Bot.ArchiLogger.LogGenericWarningException(e);
					}
				}
			}

			if (response != null) {
				return response["server_time"].AsUnsignedInteger();
			}

			Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));
			return 0;
		}

		internal async Task<byte?> GetTradeHoldDuration(ulong tradeID) {
			if (tradeID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(tradeID));
				return null;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			string request = SteamCommunityURL + "/tradeoffer/" + tradeID + "?l=english";

			HtmlDocument htmlDocument = await WebBrowser.UrlGetToHtmlDocumentRetry(request).ConfigureAwait(false);

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

			if (byte.TryParse(text, out byte holdDuration)) {
				return holdDuration;
			}

			Bot.ArchiLogger.LogNullError(nameof(holdDuration));
			return null;
		}

		internal async Task<string> GetTradeToken() {
			if (CachedTradeToken != null) {
				return CachedTradeToken;
			}

			await TradeTokenSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				if (CachedTradeToken != null) {
					return CachedTradeToken;
				}

				const string request = SteamCommunityURL + "/my/tradeoffers/privacy?l=english";
				HtmlDocument htmlDocument = await WebBrowser.UrlGetToHtmlDocumentRetry(request).ConfigureAwait(false);

				if (htmlDocument == null) {
					return null;
				}

				HtmlNode tokenNode = htmlDocument.DocumentNode.SelectSingleNode("//input[@class='trade_offer_access_url']");
				if (tokenNode == null) {
					Bot.ArchiLogger.LogNullError(nameof(tokenNode));
					return null;
				}

				string value = tokenNode.GetAttributeValue("value", null);
				if (string.IsNullOrEmpty(value)) {
					Bot.ArchiLogger.LogNullError(nameof(value));
					return null;
				}

				int index = value.IndexOf("token=", StringComparison.Ordinal);
				if (index < 0) {
					Bot.ArchiLogger.LogNullError(nameof(index));
					return null;
				}

				index += 6;
				if (index + 8 < value.Length) {
					Bot.ArchiLogger.LogNullError(nameof(index));
					return null;
				}

				CachedTradeToken = value.Substring(index, 8);
				return CachedTradeToken;
			} finally {
				TradeTokenSemaphore.Release();
			}
		}

		internal async Task<bool?> HandleConfirmation(string deviceID, string confirmationHash, uint time, uint confirmationID, ulong confirmationKey, bool accept) {
			if (string.IsNullOrEmpty(deviceID) || string.IsNullOrEmpty(confirmationHash) || (time == 0) || (confirmationID == 0) || (confirmationKey == 0)) {
				Bot.ArchiLogger.LogNullError(nameof(deviceID) + " || " + nameof(confirmationHash) + " || " + nameof(time) + " || " + nameof(confirmationID) + " || " + nameof(confirmationKey));
				return null;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			string request = SteamCommunityURL + "/mobileconf/ajaxop?op=" + (accept ? "allow" : "cancel") + "&p=" + deviceID + "&a=" + SteamID + "&k=" + WebUtility.UrlEncode(confirmationHash) + "&t=" + time + "&m=android&tag=conf&cid=" + confirmationID + "&ck=" + confirmationKey;

			Steam.ConfirmationResponse response = await WebBrowser.UrlGetToJsonResultRetry<Steam.ConfirmationResponse>(request).ConfigureAwait(false);
			return response?.Success;
		}

		internal async Task<bool?> HandleConfirmations(string deviceID, string confirmationHash, uint time, IReadOnlyCollection<MobileAuthenticator.Confirmation> confirmations, bool accept) {
			if (string.IsNullOrEmpty(deviceID) || string.IsNullOrEmpty(confirmationHash) || (time == 0) || (confirmations == null) || (confirmations.Count == 0)) {
				Bot.ArchiLogger.LogNullError(nameof(deviceID) + " || " + nameof(confirmationHash) + " || " + nameof(time) + " || " + nameof(confirmations));
				return null;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			const string request = SteamCommunityURL + "/mobileconf/multiajaxop";
			List<KeyValuePair<string, string>> data = new List<KeyValuePair<string, string>>(7 + confirmations.Count * 2) {
				new KeyValuePair<string, string>("op", accept ? "allow" : "cancel"),
				new KeyValuePair<string, string>("p", deviceID),
				new KeyValuePair<string, string>("a", SteamID.ToString()),
				new KeyValuePair<string, string>("k", confirmationHash),
				new KeyValuePair<string, string>("t", time.ToString()),
				new KeyValuePair<string, string>("m", "android"),
				new KeyValuePair<string, string>("tag", "conf")
			};

			foreach (MobileAuthenticator.Confirmation confirmation in confirmations) {
				data.Add(new KeyValuePair<string, string>("cid[]", confirmation.ID.ToString()));
				data.Add(new KeyValuePair<string, string>("ck[]", confirmation.Key.ToString()));
			}

			Steam.ConfirmationResponse response = await WebBrowser.UrlPostToJsonResultRetry<Steam.ConfirmationResponse>(request, data).ConfigureAwait(false);
			return response?.Success;
		}

		internal async Task<bool> HasPublicInventory() {
			if (CachedPublicInventory.HasValue) {
				return CachedPublicInventory.Value;
			}

			// We didn't fetch state yet
			await PublicInventorySemaphore.WaitAsync().ConfigureAwait(false);

			try {
				if (CachedPublicInventory.HasValue) {
					return CachedPublicInventory.Value;
				}

				bool? isInventoryPublic = await IsInventoryPublic().ConfigureAwait(false);
				if (!isInventoryPublic.HasValue) {
					return false;
				}

				CachedPublicInventory = isInventoryPublic.Value;
				return isInventoryPublic.Value;
			} finally {
				PublicInventorySemaphore.Release();
			}
		}

		internal async Task<bool> HasValidApiKey() => !string.IsNullOrEmpty(await GetApiKey().ConfigureAwait(false));

		internal async Task<bool> Init(ulong steamID, EUniverse universe, string webAPIUserNonce, string parentalPin, string vanityURL = null) {
			if ((steamID == 0) || (universe == EUniverse.Invalid) || string.IsNullOrEmpty(webAPIUserNonce) || string.IsNullOrEmpty(parentalPin)) {
				Bot.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(universe) + " || " + nameof(webAPIUserNonce) + " || " + nameof(parentalPin));
				return false;
			}

			if (!string.IsNullOrEmpty(vanityURL)) {
				VanityURL = vanityURL;
			}

			string sessionID = Convert.ToBase64String(Encoding.UTF8.GetBytes(steamID.ToString()));

			// Generate an AES session key
			byte[] sessionKey = SteamKit2.CryptoHelper.GenerateRandomBlock(32);

			// RSA encrypt it with the public key for the universe we're on
			byte[] cryptedSessionKey;
			using (RSACrypto rsa = new RSACrypto(KeyDictionary.GetPublicKey(universe))) {
				cryptedSessionKey = rsa.Encrypt(sessionKey);
			}

			// Copy our login key
			byte[] loginKey = new byte[webAPIUserNonce.Length];
			Array.Copy(Encoding.ASCII.GetBytes(webAPIUserNonce), loginKey, webAPIUserNonce.Length);

			// AES encrypt the loginkey with our session key
			byte[] cryptedLoginKey = SteamKit2.CryptoHelper.SymmetricEncrypt(loginKey, sessionKey);

			// Do the magic
			Bot.ArchiLogger.LogGenericInfo(string.Format(Strings.LoggingIn, ISteamUserAuth));

			KeyValue authResult = null;
			using (dynamic iSteamUserAuth = WebAPI.GetAsyncInterface(ISteamUserAuth)) {
				iSteamUserAuth.Timeout = WebBrowser.Timeout;

				try {
					authResult = await iSteamUserAuth.AuthenticateUser(
						steamid: steamID,
						sessionkey: Encoding.ASCII.GetString(WebUtility.UrlEncodeToBytes(cryptedSessionKey, 0, cryptedSessionKey.Length)),
						encrypted_loginkey: Encoding.ASCII.GetString(WebUtility.UrlEncodeToBytes(cryptedLoginKey, 0, cryptedLoginKey.Length)),
						method: WebRequestMethods.Http.Post,
						secure: true
					);
				} catch (Exception e) {
					Bot.ArchiLogger.LogGenericWarningException(e);
				}
			}

			if (authResult == null) {
				return false;
			}

			string steamLogin = authResult["token"].Value;
			if (string.IsNullOrEmpty(steamLogin)) {
				Bot.ArchiLogger.LogNullError(nameof(steamLogin));
				return false;
			}

			string steamLoginSecure = authResult["tokensecure"].Value;
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
			if (!parentalPin.Equals("0")) {
				if (!await UnlockParentalAccount(parentalPin).ConfigureAwait(false)) {
					return false;
				}
			}

			SteamID = steamID;
			LastSessionRefreshCheck = DateTime.UtcNow;
			return true;
		}

		internal async Task<bool> JoinGroup(ulong groupID) {
			if (groupID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(groupID));
				return false;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return false;
			}

			string sessionID = WebBrowser.CookieContainer.GetCookieValue(SteamCommunityURL, "sessionid");
			if (string.IsNullOrEmpty(sessionID)) {
				Bot.ArchiLogger.LogNullError(nameof(sessionID));
				return false;
			}

			string request = SteamCommunityURL + "/gid/" + groupID;
			Dictionary<string, string> data = new Dictionary<string, string>(2) {
				{ "sessionID", sessionID },
				{ "action", "join" }
			};

			return await WebBrowser.UrlPostRetry(request, data).ConfigureAwait(false);
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

				if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
					return;
				}

				const string request = SteamCommunityURL + "/my/inventory";
				await WebBrowser.UrlHeadRetry(request).ConfigureAwait(false);
			} finally {
				if (Program.GlobalConfig.InventoryLimiterDelay == 0) {
					InventorySemaphore.Release();
				} else {
					Task.Run(async () => {
						await Task.Delay(Program.GlobalConfig.InventoryLimiterDelay * 1000).ConfigureAwait(false);
						InventorySemaphore.Release();
					}).Forget();
				}
			}
		}

		internal async Task<bool> MarkSentTrades() {
			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return false;
			}

			const string request = SteamCommunityURL + "/my/tradeoffers/sent";
			return await WebBrowser.UrlHeadRetry(request).ConfigureAwait(false);
		}

		internal void OnDisconnected() {
			CachedApiKey = CachedTradeToken = null;
			CachedPublicInventory = null;
			SteamID = 0;
		}

		internal async Task<(EResult Result, EPurchaseResultDetail? PurchaseResult)?> RedeemWalletKey(string key) {
			if (string.IsNullOrEmpty(key)) {
				Bot.ArchiLogger.LogNullError(nameof(key));
				return null;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			const string request = SteamStoreURL + "/account/validatewalletcode";
			Dictionary<string, string> data = new Dictionary<string, string>(1) { { "wallet_code", key } };

			Steam.RedeemWalletResponse response = await WebBrowser.UrlPostToJsonResultRetry<Steam.RedeemWalletResponse>(request, data).ConfigureAwait(false);
			if (response == null) {
				return null;
			}

			return (response.Result, response.PurchaseResultDetail);
		}

		internal async Task<bool> SendTradeOffer(IReadOnlyCollection<Steam.Asset> inventory, ulong partnerID, string token = null) {
			if ((inventory == null) || (inventory.Count == 0) || (partnerID == 0)) {
				Bot.ArchiLogger.LogNullError(nameof(inventory) + " || " + nameof(inventory.Count) + " || " + nameof(partnerID));
				return false;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return false;
			}

			string sessionID = WebBrowser.CookieContainer.GetCookieValue(SteamCommunityURL, "sessionid");
			if (string.IsNullOrEmpty(sessionID)) {
				Bot.ArchiLogger.LogNullError(nameof(sessionID));
				return false;
			}

			Steam.TradeOfferRequest singleTrade = new Steam.TradeOfferRequest();
			HashSet<Steam.TradeOfferRequest> trades = new HashSet<Steam.TradeOfferRequest> { singleTrade };

			foreach (Steam.Asset item in inventory) {
				if (singleTrade.ItemsToGive.Assets.Count >= Trading.MaxItemsPerTrade) {
					if (trades.Count >= Trading.MaxTradesPerAccount) {
						break;
					}

					singleTrade = new Steam.TradeOfferRequest();
					trades.Add(singleTrade);
				}

				singleTrade.ItemsToGive.Assets.Add(item);
			}

			const string referer = SteamCommunityURL + "/tradeoffer/new";
			const string request = referer + "/send";
			foreach (Dictionary<string, string> data in trades.Select(trade => new Dictionary<string, string>(6) {
				{ "sessionid", sessionID },
				{ "serverid", "1" },
				{ "partner", partnerID.ToString() },
				{ "tradeoffermessage", "Sent by ASF" },
				{ "json_tradeoffer", JsonConvert.SerializeObject(trade) },
				{ "trade_offer_create_params", string.IsNullOrEmpty(token) ? "" : new JObject { { "trade_offer_access_token", token } }.ToString(Formatting.None) }
			})) {
				if (!await WebBrowser.UrlPostRetry(request, data, referer).ConfigureAwait(false)) {
					return false;
				}
			}

			return true;
		}

		internal async Task<bool> UnpackBooster(uint appID, ulong itemID) {
			if ((appID == 0) || (itemID == 0)) {
				Bot.ArchiLogger.LogNullError(nameof(appID) + " || " + nameof(itemID));
				return false;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return false;
			}

			string sessionID = WebBrowser.CookieContainer.GetCookieValue(SteamCommunityURL, "sessionid");
			if (string.IsNullOrEmpty(sessionID)) {
				Bot.ArchiLogger.LogNullError(nameof(sessionID));
				return false;
			}

			string request = GetAbsoluteProfileURL() + "/ajaxunpackbooster";
			Dictionary<string, string> data = new Dictionary<string, string>(3) {
				{ "sessionid", sessionID },
				{ "appid", appID.ToString() },
				{ "communityitemid", itemID.ToString() }
			};

			Steam.GenericResponse response = await WebBrowser.UrlPostToJsonResultRetry<Steam.GenericResponse>(request, data).ConfigureAwait(false);
			return response?.Result == EResult.OK;
		}

		private string GetAbsoluteProfileURL() {
			if (!string.IsNullOrEmpty(VanityURL)) {
				return SteamCommunityURL + "/id/" + VanityURL;
			}

			return SteamCommunityURL + "/profiles/" + SteamID;
		}

		private async Task<string> GetApiKey() {
			if (CachedApiKey != null) {
				// We fetched API key already, and either got valid one, or permanent AccessDenied
				// In any case, this is our final result
				return CachedApiKey;
			}

			if (Bot.IsAccountLimited) {
				// API key is permanently unavailable for limited accounts
				return null;
			}

			// We didn't fetch API key yet
			await ApiKeySemaphore.WaitAsync().ConfigureAwait(false);

			try {
				if (CachedApiKey != null) {
					return CachedApiKey;
				}

				(ESteamApiKeyState State, string Key)? result = await GetApiKeyState().ConfigureAwait(false);
				if (result == null) {
					// Request timed out, bad luck, we'll try again later
					return null;
				}

				switch (result.Value.State) {
					case ESteamApiKeyState.AccessDenied:
						// We succeeded in fetching API key, but it resulted in access denied
						// Cache the result as empty, API key is unavailable permanently
						CachedApiKey = string.Empty;
						break;
					case ESteamApiKeyState.NotRegisteredYet:
						// We succeeded in fetching API key, and it resulted in no key registered yet
						// Let's try to register a new key
						if (!await RegisterApiKey().ConfigureAwait(false)) {
							// Request timed out, bad luck, we'll try again later
							return null;
						}

						// We should have the key ready, so let's fetch it again
						result = await GetApiKeyState().ConfigureAwait(false);
						if (result?.State != ESteamApiKeyState.Registered) {
							// Something went wrong, bad luck, we'll try again later
							return null;
						}

						goto case ESteamApiKeyState.Registered;
					case ESteamApiKeyState.Registered:
						// We succeeded in fetching API key, and it resulted in registered key
						// Cache the result, this is the API key we want
						CachedApiKey = result.Value.Key;
						break;
					default:
						// We got an unhandled error, this should never happen
						Bot.ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(result.Value.State), result.Value.State));
						break;
				}

				return CachedApiKey;
			} finally {
				ApiKeySemaphore.Release();
			}
		}

		private async Task<(ESteamApiKeyState State, string Key)?> GetApiKeyState() {
			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			const string request = SteamCommunityURL + "/dev/apikey?l=english";
			HtmlDocument htmlDocument = await WebBrowser.UrlGetToHtmlDocumentRetry(request).ConfigureAwait(false);

			HtmlNode titleNode = htmlDocument?.DocumentNode.SelectSingleNode("//div[@id='mainContents']/h2");
			if (titleNode == null) {
				return null;
			}

			string title = titleNode.InnerText;
			if (string.IsNullOrEmpty(title)) {
				Bot.ArchiLogger.LogNullError(nameof(title));
				return (ESteamApiKeyState.Error, null);
			}

			if (title.Contains("Access Denied")) {
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
			if (text.Length != 32) {
				Bot.ArchiLogger.LogNullError(nameof(text));
				return (ESteamApiKeyState.Error, null);
			}

			if (Utilities.IsValidHexadecimalString(text)) {
				return (ESteamApiKeyState.Registered, text);
			}

			Bot.ArchiLogger.LogNullError(nameof(text));
			return (ESteamApiKeyState.Error, null);
		}

		private static uint GetAppIDFromMarketHashName(string hashName) {
			if (string.IsNullOrEmpty(hashName)) {
				ASF.ArchiLogger.LogNullError(nameof(hashName));
				return 0;
			}

			int index = hashName.IndexOf('-');
			if (index <= 0) {
				return 0;
			}

			return uint.TryParse(hashName.Substring(0, index), out uint appID) ? appID : 0;
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

		private async Task<bool?> IsInventoryPublic() {
			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			const string request = SteamCommunityURL + "/my/edit/settings?l=english";
			HtmlDocument htmlDocument = await WebBrowser.UrlGetToHtmlDocumentRetry(request).ConfigureAwait(false);

			HtmlNode htmlNode = htmlDocument?.DocumentNode.SelectSingleNode("//input[@id='inventoryPrivacySetting_public']");
			if (htmlNode == null) {
				return null;
			}

			// Notice: checked doesn't have a value - null is lack of attribute, "" is attribute existing
			string state = htmlNode.GetAttributeValue("checked", null);

			return state != null;
		}

		private async Task<bool?> IsLoggedIn() {
			// It would make sense to use /my/profile here, but it dismisses notifications related to profile comments
			// So instead, we'll use some less intrusive link, such as /my/videos
			const string request = SteamCommunityURL + "/my/videos";

			Uri uri = await WebBrowser.UrlHeadToUriRetry(request).ConfigureAwait(false);
			return !uri?.AbsolutePath.StartsWith("/login", StringComparison.Ordinal);
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

		private async Task<bool> RefreshSessionIfNeeded() {
			if (SteamID == 0) {
				for (byte i = 0; (i < Program.GlobalConfig.ConnectionTimeout) && (SteamID == 0); i++) {
					await Task.Delay(1000).ConfigureAwait(false);
				}

				if (SteamID == 0) {
					return false;
				}
			}

			if (DateTime.UtcNow.Subtract(LastSessionRefreshCheck).TotalSeconds < MinSessionTTL) {
				return true;
			}

			await SessionSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				if (DateTime.UtcNow.Subtract(LastSessionRefreshCheck).TotalSeconds < MinSessionTTL) {
					return true;
				}

				bool? isLoggedIn = await IsLoggedIn().ConfigureAwait(false);
				if (isLoggedIn.GetValueOrDefault(true)) {
					LastSessionRefreshCheck = DateTime.UtcNow;
					return true;
				} else {
					Bot.ArchiLogger.LogGenericInfo(Strings.RefreshingOurSession);
					return await Bot.RefreshSession().ConfigureAwait(false);
				}
			} finally {
				SessionSemaphore.Release();
			}
		}

		private async Task<bool> RegisterApiKey() {
			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return false;
			}

			string sessionID = WebBrowser.CookieContainer.GetCookieValue(SteamCommunityURL, "sessionid");
			if (string.IsNullOrEmpty(sessionID)) {
				Bot.ArchiLogger.LogNullError(nameof(sessionID));
				return false;
			}

			const string request = SteamCommunityURL + "/dev/registerkey";
			Dictionary<string, string> data = new Dictionary<string, string>(4) {
				{ "domain", "localhost" },
				{ "agreeToTerms", "agreed" },
				{ "sessionid", sessionID },
				{ "Submit", "Register" }
			};

			return await WebBrowser.UrlPostRetry(request, data).ConfigureAwait(false);
		}

		private async Task<bool> UnlockParentalAccount(string parentalPin) {
			if (string.IsNullOrEmpty(parentalPin)) {
				Bot.ArchiLogger.LogNullError(nameof(parentalPin));
				return false;
			}

			Bot.ArchiLogger.LogGenericInfo(Strings.UnlockingParentalAccount);

			if (!await UnlockParentalCommunityAccount(parentalPin).ConfigureAwait(false)) {
				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				return false;
			}

			if (!await UnlockParentalStoreAccount(parentalPin).ConfigureAwait(false)) {
				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				return false;
			}

			Bot.ArchiLogger.LogGenericInfo(Strings.Success);
			return true;
		}

		private async Task<bool> UnlockParentalCommunityAccount(string parentalPin) {
			if (string.IsNullOrEmpty(parentalPin)) {
				Bot.ArchiLogger.LogNullError(nameof(parentalPin));
				return false;
			}

			const string request = SteamCommunityURL + "/parental/ajaxunlock";
			Dictionary<string, string> data = new Dictionary<string, string>(1) { { "pin", parentalPin } };

			return await WebBrowser.UrlPostRetry(request, data, SteamCommunityURL).ConfigureAwait(false);
		}

		private async Task<bool> UnlockParentalStoreAccount(string parentalPin) {
			if (string.IsNullOrEmpty(parentalPin)) {
				Bot.ArchiLogger.LogNullError(nameof(parentalPin));
				return false;
			}

			const string request = SteamStoreURL + "/parental/ajaxunlock";
			Dictionary<string, string> data = new Dictionary<string, string>(1) { { "pin", parentalPin } };

			return await WebBrowser.UrlPostRetry(request, data, SteamStoreURL).ConfigureAwait(false);
		}

		private enum ESteamApiKeyState : byte {
			Error,
			Registered,
			NotRegisteredYet,
			AccessDenied
		}
	}
}