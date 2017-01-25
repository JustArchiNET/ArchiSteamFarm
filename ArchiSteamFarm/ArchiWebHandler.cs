/*
    _                _      _  ____   _                           _____
   / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
  / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
 / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
/_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|

 Copyright 2015-2017 Łukasz "JustArchi" Domeradzki
 Contact: JustArchi@JustArchi.net

 Licensed under the Apache License, Version 2.0 (the "License");
 you may not use this file except in compliance with the License.
 You may obtain a copy of the License at

 http://www.apache.org/licenses/LICENSE-2.0
					
 Unless required by applicable law or agreed to in writing, software
 distributed under the License is distributed on an "AS IS" BASIS,
 WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 See the License for the specific language governing permissions and
 limitations under the License.

*/

using System;
using System.Collections.Generic;
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
		private const string IEconService = "IEconService";
		private const string IPlayerService = "IPlayerService";
		private const string ISteamUserAuth = "ISteamUserAuth";
		private const string ITwoFactorService = "ITwoFactorService";

		private const byte MinSessionTTL = GlobalConfig.DefaultConnectionTimeout / 4; // Assume session is valid for at least that amount of seconds

		// We must use HTTPS for SteamCommunity, as http would make certain POST requests failing (trades)
		private const string SteamCommunityHost = "steamcommunity.com";
		private const string SteamCommunityURL = "https://" + SteamCommunityHost;

		// We could (and should) use HTTPS for SteamStore, but that would make certain POST requests failing
		private const string SteamStoreHost = "store.steampowered.com";
		private const string SteamStoreURL = "http://" + SteamStoreHost;

		private static int Timeout = GlobalConfig.DefaultConnectionTimeout * 1000; // This must be int type

		private readonly Bot Bot;
		private readonly SemaphoreSlim SessionSemaphore = new SemaphoreSlim(1);
		private readonly SemaphoreSlim SteamApiKeySemaphore = new SemaphoreSlim(1);
		private readonly WebBrowser WebBrowser;

		internal bool Ready { get; private set; }

		private DateTime LastSessionRefreshCheck = DateTime.MinValue;
		private string SteamApiKey;
		private ulong SteamID;

		internal ArchiWebHandler(Bot bot) {
			if (bot == null) {
				throw new ArgumentNullException(nameof(bot));
			}

			Bot = bot;

			WebBrowser = new WebBrowser(bot.ArchiLogger);
		}

		public void Dispose() {
			SessionSemaphore.Dispose();
			SteamApiKeySemaphore.Dispose();
		}

		internal async Task<bool> AcceptTradeOffer(ulong tradeID) {
			if (tradeID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(tradeID));
				return false;
			}

			if (!Ready || !await RefreshSessionIfNeeded().ConfigureAwait(false)) {
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

			if (!Ready || !await RefreshSessionIfNeeded().ConfigureAwait(false)) {
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

		/*
		internal async Task<bool> ClearFromDiscoveryQueue(uint appID) {
			if (appID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(appID));
				return false;
			}

			if (!Ready || !await RefreshSessionIfNeeded().ConfigureAwait(false)) {
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
		*/

		internal async Task DeclineTradeOffer(ulong tradeID) {
			if (tradeID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(tradeID));
				return;
			}

			string steamApiKey = await GetApiKey().ConfigureAwait(false);
			if (string.IsNullOrEmpty(SteamApiKey)) {
				return;
			}

			KeyValue response = null;
			for (byte i = 0; (i < WebBrowser.MaxRetries) && (response == null); i++) {
				using (dynamic iEconService = WebAPI.GetInterface(IEconService, steamApiKey)) {
					iEconService.Timeout = Timeout;

					try {
						response = iEconService.DeclineTradeOffer(
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
				Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxRetries));
			}
		}

		/*
		internal async Task<HashSet<uint>> GenerateNewDiscoveryQueue() {
			if (!Ready || !await RefreshSessionIfNeeded().ConfigureAwait(false)) {
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
		*/

		internal async Task<HashSet<Steam.TradeOffer>> GetActiveTradeOffers() {
			string steamApiKey = await GetApiKey().ConfigureAwait(false);
			if (string.IsNullOrEmpty(SteamApiKey)) {
				return null;
			}

			KeyValue response = null;
			for (byte i = 0; (i < WebBrowser.MaxRetries) && (response == null); i++) {
				using (dynamic iEconService = WebAPI.GetInterface(IEconService, steamApiKey)) {
					iEconService.Timeout = Timeout;

					try {
						response = iEconService.GetTradeOffers(
							get_received_offers: 1,
							active_only: 1,
							get_descriptions: 1,
							secure: true
						);
					} catch (Exception e) {
						Bot.ArchiLogger.LogGenericWarningException(e);
					}
				}
			}

			if (response == null) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxRetries));
				return null;
			}

			Dictionary<ulong, Tuple<uint, Steam.Item.EType>> descriptions = new Dictionary<ulong, Tuple<uint, Steam.Item.EType>>();
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

				Steam.Item.EType type = Steam.Item.EType.Unknown;

				string descriptionType = description["type"].Value;
				if (!string.IsNullOrEmpty(descriptionType)) {
					type = GetItemType(descriptionType);
				}

				descriptions[classID] = new Tuple<uint, Steam.Item.EType>(appID, type);
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

		internal async Task<HtmlDocument> GetBadgePage(byte page) {
			if (page == 0) {
				Bot.ArchiLogger.LogNullError(nameof(page));
				return null;
			}

			if (!Ready || !await RefreshSessionIfNeeded().ConfigureAwait(false)) {
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

			if (!Ready || !await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			string request = SteamCommunityURL + "/mobileconf/details/" + confirmation.ID + "?l=english&p=" + deviceID + "&a=" + SteamID + "&k=" + WebUtility.UrlEncode(confirmationHash) + "&t=" + time + "&m=android&tag=conf";

			Steam.ConfirmationDetails response = await WebBrowser.UrlGetToJsonResultRetry<Steam.ConfirmationDetails>(request).ConfigureAwait(false);
			if ((response == null) || !response.Success) {
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

			if (!Ready || !await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			string request = SteamCommunityURL + "/mobileconf/conf?l=english&p=" + deviceID + "&a=" + SteamID + "&k=" + WebUtility.UrlEncode(confirmationHash) + "&t=" + time + "&m=android&tag=conf";
			return await WebBrowser.UrlGetToHtmlDocumentRetry(request).ConfigureAwait(false);
		}

		/*
		internal async Task<HtmlDocument> GetDiscoveryQueuePage() {
			if (!Ready || !await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			const string request = SteamStoreURL + "/explore?l=english";
			return await WebBrowser.UrlGetToHtmlDocumentRetry(request).ConfigureAwait(false);
		}
		*/

		internal async Task<HashSet<ulong>> GetFamilySharingSteamIDs() {
			if (!Ready || !await RefreshSessionIfNeeded().ConfigureAwait(false)) {
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

				uint steamID3;
				if (!uint.TryParse(miniProfile, out steamID3) || (steamID3 == 0)) {
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

			if (!Ready || !await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			string request = SteamCommunityURL + "/my/gamecards/" + appID + "?l=english";
			return await WebBrowser.UrlGetToHtmlDocumentRetry(request).ConfigureAwait(false);
		}

		internal async Task<Dictionary<uint, string>> GetMyOwnedGames() {
			if (!Ready || !await RefreshSessionIfNeeded().ConfigureAwait(false)) {
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

				uint appID;
				if (!uint.TryParse(appNode.InnerText, out appID)) {
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

		internal async Task<HashSet<Steam.Item>> GetMySteamInventory(bool tradable, HashSet<Steam.Item.EType> wantedTypes) {
			if ((wantedTypes == null) || (wantedTypes.Count == 0)) {
				Bot.ArchiLogger.LogNullError(nameof(wantedTypes));
				return null;
			}

			if (!Ready || !await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			HashSet<Steam.Item> result = new HashSet<Steam.Item>();

			string request = SteamCommunityURL + "/my/inventory/json/" + Steam.Item.SteamAppID + "/" + Steam.Item.SteamCommunityContextID + "?l=english&trading=" + (tradable ? "1" : "0") + "&start=";
			uint currentPage = 0;

			while (true) {
				JObject jObject = await WebBrowser.UrlGetToJObjectRetry(request + currentPage).ConfigureAwait(false);

				IEnumerable<JToken> descriptions = jObject?.SelectTokens("$.rgDescriptions.*");
				if (descriptions == null) {
					return null; // OK, empty inventory
				}

				Dictionary<ulong, Tuple<uint, Steam.Item.EType>> descriptionMap = new Dictionary<ulong, Tuple<uint, Steam.Item.EType>>();
				foreach (JToken description in descriptions.Where(description => description != null)) {
					string classIDString = description["classid"]?.ToString();
					if (string.IsNullOrEmpty(classIDString)) {
						Bot.ArchiLogger.LogNullError(nameof(classIDString));
						continue;
					}

					ulong classID;
					if (!ulong.TryParse(classIDString, out classID) || (classID == 0)) {
						Bot.ArchiLogger.LogNullError(nameof(classID));
						continue;
					}

					if (descriptionMap.ContainsKey(classID)) {
						continue;
					}

					uint appID = 0;

					string hashName = description["market_hash_name"]?.ToString();
					if (!string.IsNullOrEmpty(hashName)) {
						appID = GetAppIDFromMarketHashName(hashName);
					}

					if (appID == 0) {
						string appIDString = description["appid"]?.ToString();
						if (string.IsNullOrEmpty(appIDString)) {
							Bot.ArchiLogger.LogNullError(nameof(appIDString));
							continue;
						}

						if (!uint.TryParse(appIDString, out appID) || (appID == 0)) {
							Bot.ArchiLogger.LogNullError(nameof(appID));
							continue;
						}
					}

					Steam.Item.EType type = Steam.Item.EType.Unknown;

					string descriptionType = description["type"]?.ToString();
					if (!string.IsNullOrEmpty(descriptionType)) {
						type = GetItemType(descriptionType);
					}

					descriptionMap[classID] = new Tuple<uint, Steam.Item.EType>(appID, type);
				}

				IEnumerable<JToken> items = jObject.SelectTokens("$.rgInventory.*");
				if (items == null) {
					Bot.ArchiLogger.LogNullError(nameof(items));
					return null;
				}

				foreach (JToken item in items.Where(item => item != null)) {
					Steam.Item steamItem;

					try {
						steamItem = item.ToObject<Steam.Item>();
					} catch (JsonException e) {
						Bot.ArchiLogger.LogGenericException(e);
						return null;
					}

					if (steamItem == null) {
						Bot.ArchiLogger.LogNullError(nameof(steamItem));
						return null;
					}

					steamItem.AppID = Steam.Item.SteamAppID;
					steamItem.ContextID = Steam.Item.SteamCommunityContextID;

					Tuple<uint, Steam.Item.EType> description;
					if (descriptionMap.TryGetValue(steamItem.ClassID, out description)) {
						steamItem.RealAppID = description.Item1;
						steamItem.Type = description.Item2;
					}

					if (!wantedTypes.Contains(steamItem.Type)) {
						continue;
					}

					result.Add(steamItem);
				}

				bool more;
				if (!bool.TryParse(jObject["more"]?.ToString(), out more) || !more) {
					break; // OK, last page
				}

				uint nextPage;
				if (!uint.TryParse(jObject["more_start"]?.ToString(), out nextPage) || (nextPage <= currentPage)) {
					Bot.ArchiLogger.LogNullError(nameof(nextPage));
					return null;
				}

				currentPage = nextPage;
			}

			return result;
		}

		internal async Task<Dictionary<uint, string>> GetOwnedGames(ulong steamID) {
			if (steamID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(steamID));
				return null;
			}

			string steamApiKey = await GetApiKey().ConfigureAwait(false);
			if (string.IsNullOrEmpty(SteamApiKey)) {
				return null;
			}

			KeyValue response = null;
			for (byte i = 0; (i < WebBrowser.MaxRetries) && (response == null); i++) {
				using (dynamic iPlayerService = WebAPI.GetInterface(IPlayerService, steamApiKey)) {
					iPlayerService.Timeout = Timeout;

					try {
						response = iPlayerService.GetOwnedGames(
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
				Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxRetries));
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

		internal uint GetServerTime() {
			KeyValue response = null;
			for (byte i = 0; (i < WebBrowser.MaxRetries) && (response == null); i++) {
				using (dynamic iTwoFactorService = WebAPI.GetInterface(ITwoFactorService)) {
					iTwoFactorService.Timeout = Timeout;

					try {
						response = iTwoFactorService.QueryTime(
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

			Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxRetries));
			return 0;
		}

		/*
		internal async Task<HtmlDocument> GetSteamAwardsPage() {
			if (!Ready || !await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			const string request = SteamStoreURL + "/SteamAwards?l=english";
			return await WebBrowser.UrlGetToHtmlDocumentRetry(request).ConfigureAwait(false);
		}
		*/

		internal async Task<byte?> GetTradeHoldDuration(ulong tradeID) {
			if (tradeID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(tradeID));
				return null;
			}

			if (!Ready || !await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			string request = SteamCommunityURL + "/tradeoffer/" + tradeID + "?l=english";

			HtmlDocument htmlDocument = await WebBrowser.UrlGetToHtmlDocumentRetry(request).ConfigureAwait(false);

			HtmlNode htmlNode = htmlDocument?.DocumentNode.SelectSingleNode("//div[@class='pagecontent']/script");
			if (htmlNode == null) { // Trade can be no longer valid
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

			byte holdDuration;
			if (byte.TryParse(text, out holdDuration)) {
				return holdDuration;
			}

			Bot.ArchiLogger.LogNullError(nameof(holdDuration));
			return null;
		}

		internal async Task<bool?> HandleConfirmation(string deviceID, string confirmationHash, uint time, uint confirmationID, ulong confirmationKey, bool accept) {
			if (string.IsNullOrEmpty(deviceID) || string.IsNullOrEmpty(confirmationHash) || (time == 0) || (confirmationID == 0) || (confirmationKey == 0)) {
				Bot.ArchiLogger.LogNullError(nameof(deviceID) + " || " + nameof(confirmationHash) + " || " + nameof(time) + " || " + nameof(confirmationID) + " || " + nameof(confirmationKey));
				return null;
			}

			if (!Ready || !await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			string request = SteamCommunityURL + "/mobileconf/ajaxop?op=" + (accept ? "allow" : "cancel") + "&p=" + deviceID + "&a=" + SteamID + "&k=" + WebUtility.UrlEncode(confirmationHash) + "&t=" + time + "&m=android&tag=conf&cid=" + confirmationID + "&ck=" + confirmationKey;

			Steam.ConfirmationResponse response = await WebBrowser.UrlGetToJsonResultRetry<Steam.ConfirmationResponse>(request).ConfigureAwait(false);
			return response?.Success;
		}

		internal async Task<bool?> HandleConfirmations(string deviceID, string confirmationHash, uint time, HashSet<MobileAuthenticator.Confirmation> confirmations, bool accept) {
			if (string.IsNullOrEmpty(deviceID) || string.IsNullOrEmpty(confirmationHash) || (time == 0) || (confirmations == null) || (confirmations.Count == 0)) {
				Bot.ArchiLogger.LogNullError(nameof(deviceID) + " || " + nameof(confirmationHash) + " || " + nameof(time) + " || " + nameof(confirmations));
				return null;
			}

			if (!Ready || !await RefreshSessionIfNeeded().ConfigureAwait(false)) {
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

		internal async Task<bool> HasValidApiKey() => !string.IsNullOrEmpty(await GetApiKey().ConfigureAwait(false));

		internal static void Init() => Timeout = Program.GlobalConfig.ConnectionTimeout * 1000;

		internal async Task<bool> Init(ulong steamID, EUniverse universe, string webAPIUserNonce, string parentalPin) {
			if ((steamID == 0) || (universe == EUniverse.Invalid) || string.IsNullOrEmpty(webAPIUserNonce) || string.IsNullOrEmpty(parentalPin)) {
				Bot.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(universe) + " || " + nameof(webAPIUserNonce) + " || " + nameof(parentalPin));
				return false;
			}

			SteamID = steamID;

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

			KeyValue authResult;
			using (dynamic iSteamUserAuth = WebAPI.GetInterface(ISteamUserAuth)) {
				iSteamUserAuth.Timeout = Timeout;

				try {
					authResult = iSteamUserAuth.AuthenticateUser(
						steamid: steamID,
						sessionkey: Encoding.ASCII.GetString(WebUtility.UrlEncodeToBytes(cryptedSessionKey, 0, cryptedSessionKey.Length)),
						encrypted_loginkey: Encoding.ASCII.GetString(WebUtility.UrlEncodeToBytes(cryptedLoginKey, 0, cryptedLoginKey.Length)),
						method: WebRequestMethods.Http.Post,
						secure: true
					);
				} catch (Exception e) {
					Bot.ArchiLogger.LogGenericWarningException(e);
					return false;
				}
			}

			if (authResult == null) {
				Bot.ArchiLogger.LogNullError(nameof(authResult));
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

			Ready = true;
			LastSessionRefreshCheck = DateTime.UtcNow;
			return true;
		}

		internal async Task<bool> JoinGroup(ulong groupID) {
			if (groupID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(groupID));
				return false;
			}

			if (!Ready || !await RefreshSessionIfNeeded().ConfigureAwait(false)) {
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

		internal async Task<bool> MarkInventory() {
			if (!Ready || !await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return false;
			}

			const string request = SteamCommunityURL + "/my/inventory";
			return await WebBrowser.UrlHeadRetry(request).ConfigureAwait(false);
		}

		internal void OnDisconnected() => Ready = false;

		internal async Task<ArchiHandler.PurchaseResponseCallback.EPurchaseResult> RedeemWalletKey(string key) {
			if (string.IsNullOrEmpty(key)) {
				Bot.ArchiLogger.LogNullError(nameof(key));
				return ArchiHandler.PurchaseResponseCallback.EPurchaseResult.Unknown;
			}

			if (!Ready || !await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return ArchiHandler.PurchaseResponseCallback.EPurchaseResult.Timeout;
			}

			const string request = SteamStoreURL + "/account/validatewalletcode";
			Dictionary<string, string> data = new Dictionary<string, string>(1) {
				{ "wallet_code", key }
			};

			Steam.RedeemWalletResponse response = await WebBrowser.UrlPostToJsonResultRetry<Steam.RedeemWalletResponse>(request, data).ConfigureAwait(false);
			return response?.PurchaseResult ?? ArchiHandler.PurchaseResponseCallback.EPurchaseResult.Timeout;
		}

		internal async Task<bool> SendTradeOffer(HashSet<Steam.Item> inventory, ulong partnerID, string token = null) {
			if ((inventory == null) || (inventory.Count == 0) || (partnerID == 0)) {
				Bot.ArchiLogger.LogNullError(nameof(inventory) + " || " + nameof(inventory.Count) + " || " + nameof(partnerID));
				return false;
			}

			if (!Ready || !await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return false;
			}

			string sessionID = WebBrowser.CookieContainer.GetCookieValue(SteamCommunityURL, "sessionid");
			if (string.IsNullOrEmpty(sessionID)) {
				Bot.ArchiLogger.LogNullError(nameof(sessionID));
				return false;
			}

			Steam.TradeOfferRequest singleTrade = new Steam.TradeOfferRequest();
			HashSet<Steam.TradeOfferRequest> trades = new HashSet<Steam.TradeOfferRequest> { singleTrade };

			byte itemID = 0;
			foreach (Steam.Item item in inventory) {
				if (itemID >= Trading.MaxItemsPerTrade) {
					if (trades.Count >= Trading.MaxTradesPerAccount) {
						break;
					}

					singleTrade = new Steam.TradeOfferRequest();
					trades.Add(singleTrade);
					itemID = 0;
				}

				singleTrade.ItemsToGive.Assets.Add(item);
				itemID++;
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

		private async Task<string> GetApiKey(bool allowRegister = true) {
			if (SteamApiKey != null) {
				// We fetched API key already, and either got valid one, or permanent AccessDenied
				// In any case, this is our final result
				return SteamApiKey;
			}

			// We didn't fetch API key yet
			await SteamApiKeySemaphore.WaitAsync().ConfigureAwait(false);

			try {
				if (SteamApiKey != null) {
					return SteamApiKey;
				}

				Tuple<ESteamApiKeyState, string> result = await GetApiKeyState().ConfigureAwait(false);
				if (result == null) {
					// Request timed out, bad luck, we'll try again later
					return null;
				}

				switch (result.Item1) {
					case ESteamApiKeyState.Registered:
						// We succeeded in fetching API key, and it resulted in registered key
						// Cache the result and return it
						SteamApiKey = result.Item2;
						return SteamApiKey;
					case ESteamApiKeyState.NotRegisteredYet:
						// We succeeded in fetching API key, and it resulted in no key registered yet
						if (!allowRegister) {
							// But this call doesn't allow us to register it, so return null
							return null;
						}

						// If we're allowed to register the key, let's do so
						if (!await RegisterApiKey().ConfigureAwait(false)) {
							// Request timed out, bad luck, we'll try again later
							return null;
						}

						// We should have the key ready, so let's fetch it again
						result = await GetApiKeyState().ConfigureAwait(false);
						if (result?.Item1 != ESteamApiKeyState.Registered) {
							// Something went wrong, bad luck, we'll try again later
							return null;
						}

						goto case ESteamApiKeyState.Registered;
					case ESteamApiKeyState.AccessDenied:
						// We succeeded in fetching API key, but it resulted in access denied
						// Cache the result as empty, and return null
						SteamApiKey = "";
						return null;
					default:
						// We got some kind of error, maybe it's temporary, maybe it's permanent
						// Don't cache anything, we'll try again later
						return null;
				}
			} finally {
				SteamApiKeySemaphore.Release();
			}
		}

		private async Task<Tuple<ESteamApiKeyState, string>> GetApiKeyState() {
			if (!Ready || !await RefreshSessionIfNeeded().ConfigureAwait(false)) {
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
				return new Tuple<ESteamApiKeyState, string>(ESteamApiKeyState.Error, null);
			}

			if (title.Contains("Access Denied")) {
				return new Tuple<ESteamApiKeyState, string>(ESteamApiKeyState.AccessDenied, null);
			}

			HtmlNode htmlNode = htmlDocument.DocumentNode.SelectSingleNode("//div[@id='bodyContents_ex']/p");
			if (htmlNode == null) {
				Bot.ArchiLogger.LogNullError(nameof(htmlNode));
				return new Tuple<ESteamApiKeyState, string>(ESteamApiKeyState.Error, null);
			}

			string text = htmlNode.InnerText;
			if (string.IsNullOrEmpty(text)) {
				Bot.ArchiLogger.LogNullError(nameof(text));
				return new Tuple<ESteamApiKeyState, string>(ESteamApiKeyState.Error, null);
			}

			if (text.Contains("Registering for a Steam Web API Key")) {
				return new Tuple<ESteamApiKeyState, string>(ESteamApiKeyState.NotRegisteredYet, null);
			}

			int keyIndex = text.IndexOf("Key: ", StringComparison.Ordinal);
			if (keyIndex < 0) {
				Bot.ArchiLogger.LogNullError(nameof(keyIndex));
				return new Tuple<ESteamApiKeyState, string>(ESteamApiKeyState.Error, null);
			}

			keyIndex += 5;

			if (text.Length <= keyIndex) {
				Bot.ArchiLogger.LogNullError(nameof(text));
				return new Tuple<ESteamApiKeyState, string>(ESteamApiKeyState.Error, null);
			}

			text = text.Substring(keyIndex);
			if (text.Length != 32) {
				Bot.ArchiLogger.LogNullError(nameof(text));
				return new Tuple<ESteamApiKeyState, string>(ESteamApiKeyState.Error, null);
			}

			if (Utilities.IsValidHexadecimalString(text)) {
				return new Tuple<ESteamApiKeyState, string>(ESteamApiKeyState.Registered, text);
			}

			Bot.ArchiLogger.LogNullError(nameof(text));
			return new Tuple<ESteamApiKeyState, string>(ESteamApiKeyState.Error, null);
		}

		/*
		internal async Task<bool> SteamAwardsVote(byte voteID, uint appID) {
			if ((voteID == 0) || (appID == 0)) {
				Bot.ArchiLogger.LogNullError(nameof(voteID) + " || " + nameof(appID));
				return false;
			}

			if (!Ready || !await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return false;
			}

			string sessionID = WebBrowser.CookieContainer.GetCookieValue(SteamStoreURL, "sessionid");
			if (string.IsNullOrEmpty(sessionID)) {
				Bot.ArchiLogger.LogNullError(nameof(sessionID));
				return false;
			}

			const string request = SteamStoreURL + "/salevote";
			Dictionary<string, string> data = new Dictionary<string, string>(3) {
				{ "sessionid", sessionID },
				{ "voteid", voteID.ToString() },
				{ "appid", appID.ToString() }
			};

			return await WebBrowser.UrlPostRetry(request, data).ConfigureAwait(false);
		}
		*/

		private static uint GetAppIDFromMarketHashName(string hashName) {
			if (string.IsNullOrEmpty(hashName)) {
				Program.ArchiLogger.LogNullError(nameof(hashName));
				return 0;
			}

			int index = hashName.IndexOf('-');
			if (index <= 0) {
				return 0;
			}

			uint appID;
			return uint.TryParse(hashName.Substring(0, index), out appID) ? appID : 0;
		}

		private static Steam.Item.EType GetItemType(string name) {
			if (string.IsNullOrEmpty(name)) {
				Program.ArchiLogger.LogNullError(nameof(name));
				return Steam.Item.EType.Unknown;
			}

			switch (name) {
				case "Booster Pack":
					return Steam.Item.EType.BoosterPack;
				case "Steam Gems":
					return Steam.Item.EType.SteamGems;
				default:
					if (name.EndsWith("Emoticon", StringComparison.Ordinal)) {
						return Steam.Item.EType.Emoticon;
					}

					if (name.EndsWith("Foil Trading Card", StringComparison.Ordinal)) {
						return Steam.Item.EType.FoilTradingCard;
					}

					if (name.EndsWith("Profile Background", StringComparison.Ordinal)) {
						return Steam.Item.EType.ProfileBackground;
					}

					return name.EndsWith("Trading Card", StringComparison.Ordinal) ? Steam.Item.EType.TradingCard : Steam.Item.EType.Unknown;
			}
		}

		private async Task<bool?> IsLoggedIn() {
			// It would make sense to use /my/profile here, but it dismisses notifications related to profile comments
			// So instead, we'll use some less intrusive link, such as /my/videos
			const string request = SteamCommunityURL + "/my/videos";

			Uri uri = await WebBrowser.UrlHeadToUriRetry(request).ConfigureAwait(false);
			return !uri?.AbsolutePath.StartsWith("/login", StringComparison.Ordinal);
		}

		private static bool ParseItems(Dictionary<ulong, Tuple<uint, Steam.Item.EType>> descriptions, List<KeyValue> input, HashSet<Steam.Item> output) {
			if ((descriptions == null) || (input == null) || (input.Count == 0) || (output == null)) {
				Program.ArchiLogger.LogNullError(nameof(descriptions) + " || " + nameof(input) + " || " + nameof(output));
				return false;
			}

			foreach (KeyValue item in input) {
				uint appID = item["appid"].AsUnsignedInteger();
				if (appID == 0) {
					Program.ArchiLogger.LogNullError(nameof(appID));
					return false;
				}

				ulong contextID = item["contextid"].AsUnsignedLong();
				if (contextID == 0) {
					Program.ArchiLogger.LogNullError(nameof(contextID));
					return false;
				}

				ulong classID = item["classid"].AsUnsignedLong();
				if (classID == 0) {
					Program.ArchiLogger.LogNullError(nameof(classID));
					return false;
				}

				uint amount = item["amount"].AsUnsignedInteger();
				if (amount == 0) {
					Program.ArchiLogger.LogNullError(nameof(amount));
					return false;
				}

				uint realAppID = appID;
				Steam.Item.EType type = Steam.Item.EType.Unknown;

				Tuple<uint, Steam.Item.EType> description;
				if (descriptions.TryGetValue(classID, out description)) {
					realAppID = description.Item1;
					type = description.Item2;
				}

				Steam.Item steamItem = new Steam.Item(appID, contextID, classID, amount, realAppID, type);
				output.Add(steamItem);
			}

			return true;
		}

		private async Task<bool> RefreshSessionIfNeeded() {
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
			if (!Ready || !await RefreshSessionIfNeeded().ConfigureAwait(false)) {
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
			Dictionary<string, string> data = new Dictionary<string, string>(1) {
				{ "pin", parentalPin }
			};

			return await WebBrowser.UrlPostRetry(request, data, SteamCommunityURL).ConfigureAwait(false);
		}

		private async Task<bool> UnlockParentalStoreAccount(string parentalPin) {
			if (string.IsNullOrEmpty(parentalPin)) {
				Bot.ArchiLogger.LogNullError(nameof(parentalPin));
				return false;
			}

			const string request = SteamStoreURL + "/parental/ajaxunlock";
			Dictionary<string, string> data = new Dictionary<string, string>(1) {
				{ "pin", parentalPin }
			};

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