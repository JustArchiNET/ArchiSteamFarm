/*
    _                _      _  ____   _                           _____
   / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
  / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
 / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
/_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|

 Copyright 2015-2016 Łukasz "JustArchi" Domeradzki
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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using HtmlAgilityPack;
using SteamKit2;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace ArchiSteamFarm {
	internal sealed class ArchiWebHandler {
		private const int Timeout = 1000 * WebBrowser.HttpTimeout; // In miliseconds

		private readonly Bot Bot;
		private readonly string ApiKey;
		private readonly Dictionary<string, string> Cookie = new Dictionary<string, string>();

		private ulong SteamID;
		private string VanityURL;

		// This is required because home_process request must be done on final URL
		private string GetHomeProcess() {
			if (!string.IsNullOrEmpty(VanityURL)) {
				return "https://steamcommunity.com/id/" + VanityURL + "/home_process";
			} else if (SteamID != 0) {
				return "https://steamcommunity.com/profiles/" + SteamID + "/home_process";
			} else {
				return null;
			}
		}

		private async Task UnlockParentalAccount(string parentalPin) {
			if (string.IsNullOrEmpty(parentalPin) || parentalPin.Equals("0")) {
				return;
			}

			Logging.LogGenericInfo("Unlocking parental account...", Bot.BotName);
			Dictionary<string, string> data = new Dictionary<string, string>() {
				{ "pin", parentalPin }
			};

			HttpResponseMessage response = null;
			for (byte i = 0; i < WebBrowser.MaxRetries && response == null; i++) {
				response = await WebBrowser.UrlPost("https://steamcommunity.com/parental/ajaxunlock", data, Cookie, "https://steamcommunity.com/").ConfigureAwait(false);
			}

			if (response == null) {
				Logging.LogGenericWTF("Request failed even after " + WebBrowser.MaxRetries + " tries", Bot.BotName);
				return;
			}

			IEnumerable<string> setCookieValues;
			if (!response.Headers.TryGetValues("Set-Cookie", out setCookieValues)) {
				Logging.LogNullError("setCookieValues", Bot.BotName);
				return;
			}

			foreach (string setCookieValue in setCookieValues) {
				if (setCookieValue.Contains("steamparental=")) {
					string setCookie = setCookieValue.Substring(setCookieValue.IndexOf("steamparental=") + 14);
					setCookie = setCookie.Substring(0, setCookie.IndexOf(';'));
					Cookie["steamparental"] = setCookie;
					Logging.LogGenericInfo("Success!", Bot.BotName);
					return;
				}
			}

			Logging.LogGenericWarning("Failed to unlock parental account!", Bot.BotName);
		}

		internal ArchiWebHandler(Bot bot, string apiKey) {
			Bot = bot;

			if (!string.IsNullOrEmpty(apiKey) && !apiKey.Equals("null")) {
				ApiKey = apiKey;
			}
		}

		internal async Task<bool> Init(SteamClient steamClient, string webAPIUserNonce, string vanityURL, string parentalPin) {
			if (steamClient == null || steamClient.SteamID == null || string.IsNullOrEmpty(webAPIUserNonce)) {
				return false;
			}

			SteamID = steamClient.SteamID;
			VanityURL = vanityURL;

			string sessionID = Convert.ToBase64String(Encoding.UTF8.GetBytes(SteamID.ToString()));

			// Generate an AES session key
			byte[] sessionKey = CryptoHelper.GenerateRandomBlock(32);

			// RSA encrypt it with the public key for the universe we're on
			byte[] cryptedSessionKey = null;
			using (RSACrypto rsa = new RSACrypto(KeyDictionary.GetPublicKey(steamClient.ConnectedUniverse))) {
				cryptedSessionKey = rsa.Encrypt(sessionKey);
			}

			// Copy our login key
			byte[] loginKey = new byte[webAPIUserNonce.Length];
			Array.Copy(Encoding.ASCII.GetBytes(webAPIUserNonce), loginKey, webAPIUserNonce.Length);

			// AES encrypt the loginkey with our session key
			byte[] cryptedLoginKey = CryptoHelper.SymmetricEncrypt(loginKey, sessionKey);

			// Do the magic
			Logging.LogGenericInfo("Logging in to ISteamUserAuth...", Bot.BotName);

			KeyValue authResult;
			using (dynamic iSteamUserAuth = WebAPI.GetInterface("ISteamUserAuth")) {
				iSteamUserAuth.Timeout = Timeout;

				try {
					authResult = iSteamUserAuth.AuthenticateUser(
						steamid: SteamID,
						sessionkey: Encoding.ASCII.GetString(WebUtility.UrlEncodeToBytes(cryptedSessionKey, 0, cryptedSessionKey.Length)),
						encrypted_loginkey: Encoding.ASCII.GetString(WebUtility.UrlEncodeToBytes(cryptedLoginKey, 0, cryptedLoginKey.Length)),
						method: WebRequestMethods.Http.Post,
						secure: true
					);
				} catch (Exception e) {
					Logging.LogGenericException(e, Bot.BotName);
					return false;
				}
			}

			if (authResult == null) {
				return false;
			}

			Logging.LogGenericInfo("Success!", Bot.BotName);

			string steamLogin = authResult["token"].AsString();
			string steamLoginSecure = authResult["tokensecure"].AsString();

			Cookie["sessionid"] = sessionID;
			Cookie["steamLogin"] = steamLogin;
			Cookie["steamLoginSecure"] = steamLoginSecure;
			Cookie["birthtime"] = "-473356799";

			await UnlockParentalAccount(parentalPin).ConfigureAwait(false);
			return true;
		}

		internal async Task<bool?> IsLoggedIn() {
			if (SteamID == 0) {
				return false;
			}

			HtmlDocument htmlDocument = null;
			for (byte i = 0; i < WebBrowser.MaxRetries && htmlDocument == null; i++) {
				htmlDocument = await WebBrowser.UrlGetToHtmlDocument("https://steamcommunity.com/my/profile", Cookie).ConfigureAwait(false);
			}

			if (htmlDocument == null) {
				Logging.LogGenericWTF("Request failed even after " + WebBrowser.MaxRetries + " tries", Bot.BotName);
				return null;
			}

			HtmlNode htmlNode = htmlDocument.DocumentNode.SelectSingleNode("//span[@id='account_pulldown']");
			return htmlNode != null;
		}

		internal async Task<bool> ReconnectIfNeeded() {
			bool? isLoggedIn = await IsLoggedIn().ConfigureAwait(false);
			if (isLoggedIn.HasValue && !isLoggedIn.Value) {
				Logging.LogGenericInfo("Reconnecting because our sessionID expired!", Bot.BotName);
				var restart = Task.Run(async () => await Bot.Restart().ConfigureAwait(false));
				return true;
			}

			return false;
		}

		internal List<SteamTradeOffer> GetTradeOffers() {
			if (ApiKey == null) {
				return null;
			}

			KeyValue response = null;
			using (dynamic iEconService = WebAPI.GetInterface("IEconService", ApiKey)) {
				iEconService.Timeout = Timeout;

				for (byte i = 0; i < WebBrowser.MaxRetries && response == null; i++) {
					try {
						response = iEconService.GetTradeOffers(
							get_received_offers: 1,
							active_only: 1,
							secure: true
						);
					} catch (Exception e) {
						Logging.LogGenericException(e, Bot.BotName);
					}
				}
			}

			if (response == null) {
				Logging.LogGenericWTF("Request failed even after " + WebBrowser.MaxRetries + " tries", Bot.BotName);
				return null;
			}

			List<SteamTradeOffer> result = new List<SteamTradeOffer>();
			foreach (KeyValue trade in response["trade_offers_received"].Children) {
				SteamTradeOffer tradeOffer = new SteamTradeOffer {
					tradeofferid = trade["tradeofferid"].AsString(),
					accountid_other = trade["accountid_other"].AsInteger(),
					message = trade["message"].AsString(),
					expiration_time = trade["expiration_time"].AsInteger(),
					trade_offer_state = trade["trade_offer_state"].AsEnum<SteamTradeOffer.ETradeOfferState>(),
					items_to_give = new List<SteamItem>(),
					items_to_receive = new List<SteamItem>(),
					is_our_offer = trade["is_our_offer"].AsBoolean(),
					time_created = trade["time_created"].AsInteger(),
					time_updated = trade["time_updated"].AsInteger(),
					from_real_time_trade = trade["from_real_time_trade"].AsBoolean(),
					escrow_end_date = trade["escrow_end_date"].AsInteger(),
					confirmation_method = trade["confirmation_method"].AsEnum<SteamTradeOffer.ETradeOfferConfirmationMethod>()
				};
				foreach (KeyValue item in trade["items_to_give"].Children) {
					tradeOffer.items_to_give.Add(new SteamItem {
						appid = item["appid"].AsString(),
						contextid = item["contextid"].AsString(),
						assetid = item["assetid"].AsString(),
						currencyid = item["currencyid"].AsString(),
						classid = item["classid"].AsString(),
						instanceid = item["instanceid"].AsString(),
						amount = item["amount"].AsString(),
						missing = item["missing"].AsBoolean()
					});
				}
				foreach (KeyValue item in trade["items_to_receive"].Children) {
					tradeOffer.items_to_receive.Add(new SteamItem {
						appid = item["appid"].AsString(),
						contextid = item["contextid"].AsString(),
						assetid = item["assetid"].AsString(),
						currencyid = item["currencyid"].AsString(),
						classid = item["classid"].AsString(),
						instanceid = item["instanceid"].AsString(),
						amount = item["amount"].AsString(),
						missing = item["missing"].AsBoolean()
					});
				}
				result.Add(tradeOffer);
			}

			return result;
		}

		internal async Task<bool> JoinClan(ulong clanID) {
			if (clanID == 0) {
				return false;
			}

			string sessionID;
			if (!Cookie.TryGetValue("sessionid", out sessionID)) {
				return false;
			}

			string request = "https://steamcommunity.com/gid/" + clanID;

			Dictionary<string, string> data = new Dictionary<string, string>() {
				{"sessionID", sessionID},
				{"action", "join"}
			};

			HttpResponseMessage response = null;
			for (byte i = 0; i < WebBrowser.MaxRetries && response == null; i++) {
				response = await WebBrowser.UrlPost(request, data, Cookie).ConfigureAwait(false);
			}

			if (response == null) {
				Logging.LogGenericWTF("Request failed even after " + WebBrowser.MaxRetries + " tries", Bot.BotName);
				return false;
			}

			return true;
		}

		internal async Task<bool> LeaveClan(ulong clanID) {
			if (clanID == 0) {
				return false;
			}

			string sessionID;
			if (!Cookie.TryGetValue("sessionid", out sessionID)) {
				return false;
			}

			string request = GetHomeProcess();
			Dictionary<string, string> data = new Dictionary<string, string>() {
				{"sessionID", sessionID},
				{"action", "leaveGroup"},
				{"groupId", clanID.ToString()}
			};

			HttpResponseMessage response = null;
			for (byte i = 0; i < WebBrowser.MaxRetries && response == null; i++) {
				response = await WebBrowser.UrlPost(request, data, Cookie).ConfigureAwait(false);
			}

			if (response == null) {
				Logging.LogGenericWTF("Request failed even after " + WebBrowser.MaxRetries + " tries", Bot.BotName);
				return false;
			}

			return true;
		}

		internal async Task<bool> AcceptTradeOffer(ulong tradeID) {
			if (tradeID == 0) {
				return false;
			}

			string sessionID;
			if (!Cookie.TryGetValue("sessionid", out sessionID)) {
				return false;
			}

			string referer = "https://steamcommunity.com/tradeoffer/" + tradeID;
			string request = referer + "/accept";

			Dictionary<string, string> data = new Dictionary<string, string>() {
				{"sessionid", sessionID},
				{"serverid", "1"},
				{"tradeofferid", tradeID.ToString()}
			};

			HttpResponseMessage response = null;
			for (byte i = 0; i < WebBrowser.MaxRetries && response == null; i++) {
				response = await WebBrowser.UrlPost(request, data, Cookie, referer).ConfigureAwait(false);
			}

			if (response == null) {
				Logging.LogGenericWTF("Request failed even after " + WebBrowser.MaxRetries + " tries", Bot.BotName);
				return false;
			}

			return true;
		}

		internal bool DeclineTradeOffer(ulong tradeID) {
			if (tradeID == 0 || ApiKey == null) {
				return false;
			}

			KeyValue response = null;
			using (dynamic iEconService = WebAPI.GetInterface("IEconService", ApiKey)) {
				iEconService.Timeout = Timeout;

				for (byte i = 0; i < WebBrowser.MaxRetries && response == null; i++) {
					try {
						response = iEconService.DeclineTradeOffer(
							tradeofferid: tradeID.ToString(),
							method: WebRequestMethods.Http.Post,
							secure: true
						);
					} catch (Exception e) {
						Logging.LogGenericException(e, Bot.BotName);
					}
				}
			}

			if (response == null) {
				Logging.LogGenericWTF("Request failed even after " + WebBrowser.MaxRetries + " tries", Bot.BotName);
				return false;
			}

			return true;
		}

		internal async Task<List<SteamItem>> GetMyTradableInventory() {
			JObject jObject = null;
			for (byte i = 0; i < WebBrowser.MaxRetries && jObject == null; i++) {
				jObject = await WebBrowser.UrlGetToJObject("https://steamcommunity.com/my/inventory/json/753/6?trading=1", Cookie).ConfigureAwait(false);
			}

			if (jObject == null) {
				Logging.LogGenericWTF("Request failed even after " + WebBrowser.MaxRetries + " tries", Bot.BotName);
				return null;
			}

			List<SteamItem> result = new List<SteamItem>();

			IEnumerable<JToken> jTokens = jObject.SelectTokens("$.rgInventory.*");
			foreach (JToken jToken in jTokens) {
				result.Add(JsonConvert.DeserializeObject<SteamItem>(jToken.ToString()));
			}

			return result;
		}

		internal async Task<bool> SendTradeOffer(List<SteamItem> inventory, ulong partnerID, string token = null) {
			if (inventory == null || inventory.Count == 0 || partnerID == 0) {
				return false;
			}

			string sessionID;
			if (!Cookie.TryGetValue("sessionid", out sessionID)) {
				return false;
			}

			List<SteamTradeOfferRequest> trades = new List<SteamTradeOfferRequest>();

			SteamTradeOfferRequest singleTrade = null;
			for (ushort i = 0; i < inventory.Count; i++) {
				if (i % Trading.MaxItemsPerTrade == 0) {
					if (trades.Count >= Trading.MaxTradesPerAccount) {
						break;
					}

					singleTrade = new SteamTradeOfferRequest();
					trades.Add(singleTrade);
				}

				SteamItem item = inventory[i];
				singleTrade.me.assets.Add(new SteamItem() {
					appid = "753",
					contextid = "6",
					amount = item.amount,
					assetid = item.id
				});
			}

			string referer = "https://steamcommunity.com/tradeoffer/new";
			string request = referer + "/send";

			foreach (SteamTradeOfferRequest trade in trades) {
				Dictionary<string, string> data = new Dictionary<string, string>() {
					{"sessionid", sessionID},
					{"serverid", "1"},
					{"partner", partnerID.ToString()},
					{"tradeoffermessage", "Sent by ASF"},
					{"json_tradeoffer", JsonConvert.SerializeObject(trade)},
					{"trade_offer_create_params", string.IsNullOrEmpty(token) ? "" : string.Format("{{ \"trade_offer_access_token\":\"{0}\" }}", token)} // TODO: This should be rewrote
				};

				HttpResponseMessage response = null;
				for (byte i = 0; i < WebBrowser.MaxRetries && response == null; i++) {
					response = await WebBrowser.UrlPost(request, data, Cookie, referer).ConfigureAwait(false);
				}

				if (response == null) {
					Logging.LogGenericWTF("Request failed even after " + WebBrowser.MaxRetries + " tries", Bot.BotName);
					return false;
				}
			}

			return true;
		}

		internal async Task<HtmlDocument> GetBadgePage(byte page) {
			if (page == 0 || SteamID == 0) {
				return null;
			}

			HtmlDocument htmlDocument = null;
			for (byte i = 0; i < WebBrowser.MaxRetries && htmlDocument == null; i++) {
				htmlDocument = await WebBrowser.UrlGetToHtmlDocument("https://steamcommunity.com/profiles/" + SteamID + "/badges?l=english&p=" + page, Cookie).ConfigureAwait(false);
			}

			if (htmlDocument == null) {
				Logging.LogGenericWTF("Request failed even after " + WebBrowser.MaxRetries + " tries", Bot.BotName);
				return null;
			}

			return htmlDocument;
		}

		internal async Task<HtmlDocument> GetGameCardsPage(ulong appID) {
			if (appID == 0 || SteamID == 0) {
				return null;
			}

			HtmlDocument htmlDocument = null;
			for (byte i = 0; i < WebBrowser.MaxRetries && htmlDocument == null; i++) {
				htmlDocument = await WebBrowser.UrlGetToHtmlDocument("https://steamcommunity.com/profiles/" + SteamID + "/gamecards/" + appID + "?l=english", Cookie).ConfigureAwait(false);
			}

			if (htmlDocument == null) {
				Logging.LogGenericWTF("Request failed even after " + WebBrowser.MaxRetries + " tries", Bot.BotName);
				return null;
			}

			return htmlDocument;
		}
	}
}
