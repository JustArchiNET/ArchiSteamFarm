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
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Threading;

namespace ArchiSteamFarm {
	internal sealed class ArchiWebHandler {
		private const string SteamCommunityHost = "steamcommunity.com";
		private const byte MinSessionTTL = 15; // Assume session is valid for at least that amount of seconds

		private static string SteamCommunityURL = "https://" + SteamCommunityHost;
		private static int Timeout = GlobalConfig.DefaultHttpTimeout * 1000;

		private readonly Bot Bot;
		private readonly SemaphoreSlim SessionSemaphore = new SemaphoreSlim(1);
		private readonly WebBrowser WebBrowser;

		private DateTime LastSessionRefreshCheck = DateTime.MinValue;

		internal static void Init() {
			Timeout = Program.GlobalConfig.HttpTimeout * 1000;
			SteamCommunityURL = (Program.GlobalConfig.ForceHttp ? "http://" : "https://") + SteamCommunityHost;
		}

		private static uint GetAppIDFromMarketHashName(string hashName) {
			if (string.IsNullOrEmpty(hashName)) {
				return 0;
			}

			int index = hashName.IndexOf('-');
			if (index < 1) {
				return 0;
			}

			uint appID;
			if (!uint.TryParse(hashName.Substring(0, index), out appID)) {
				return 0;
			}

			return appID;
		}

		private static Steam.Item.EType GetItemType(string name) {
			if (string.IsNullOrEmpty(name)) {
				return Steam.Item.EType.Unknown;
			}

			switch (name) {
				case "Booster Pack":
					return Steam.Item.EType.BoosterPack;
				case "Coupon":
					return Steam.Item.EType.Coupon;
				case "Gift":
					return Steam.Item.EType.Gift;
				case "Steam Gems":
					return Steam.Item.EType.SteamGems;
				default:
					if (name.EndsWith("Emoticon", StringComparison.Ordinal)) {
						return Steam.Item.EType.Emoticon;
					} else if (name.EndsWith("Foil Trading Card", StringComparison.Ordinal)) {
						return Steam.Item.EType.FoilTradingCard;
					} else if (name.EndsWith("Profile Background", StringComparison.Ordinal)) {
						return Steam.Item.EType.ProfileBackground;
					} else if (name.EndsWith("Trading Card", StringComparison.Ordinal)) {
						return Steam.Item.EType.TradingCard;
					} else {
						return Steam.Item.EType.Unknown;
					}
			}
		}

		internal ArchiWebHandler(Bot bot) {
			if (bot == null) {
				throw new ArgumentNullException("bot");
			}

			Bot = bot;

			WebBrowser = new WebBrowser(bot.BotName);
		}

		internal async Task<bool> AcceptGift(ulong gid) {
			if (gid == 0) {
				return false;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return false;
			}

			string sessionID = WebBrowser.CookieContainer.GetCookieValue(SteamCommunityURL, "sessionid");
			if (string.IsNullOrEmpty(sessionID)) {
				Logging.LogNullError("sessionID");
				return false;
			}

			string request = SteamCommunityURL + "/gifts/" + gid + "/acceptunpack";
			Dictionary<string, string> data = new Dictionary<string, string>(1) {
				{ "sessionid", sessionID }
			};

			bool result = false;
			for (byte i = 0; i < WebBrowser.MaxRetries && !result; i++) {
				result = await WebBrowser.UrlPost(request, data).ConfigureAwait(false);
			}

			if (!result) {
				Logging.LogGenericWTF("Request failed even after " + WebBrowser.MaxRetries + " tries", Bot.BotName);
				return false;
			}

			return true;
		}

		internal bool Init(SteamClient steamClient, string webAPIUserNonce, string parentalPin) {
			if (steamClient == null || steamClient.SteamID == null || string.IsNullOrEmpty(webAPIUserNonce)) {
				return false;
			}

			ulong steamID = steamClient.SteamID;

			string sessionID = Convert.ToBase64String(Encoding.UTF8.GetBytes(steamID.ToString()));

			// Generate an AES session key
			byte[] sessionKey = CryptoHelper.GenerateRandomBlock(32);

			// RSA encrypt it with the public key for the universe we're on
			byte[] cryptedSessionKey;
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
						steamid: steamID,
						sessionkey: Encoding.ASCII.GetString(WebUtility.UrlEncodeToBytes(cryptedSessionKey, 0, cryptedSessionKey.Length)),
						encrypted_loginkey: Encoding.ASCII.GetString(WebUtility.UrlEncodeToBytes(cryptedLoginKey, 0, cryptedLoginKey.Length)),
						method: WebRequestMethods.Http.Post,
						secure: !Program.GlobalConfig.ForceHttp
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

			WebBrowser.CookieContainer.Add(new Cookie("sessionid", sessionID, "/", "." + SteamCommunityHost));

			string steamLogin = authResult["token"].Value;
			WebBrowser.CookieContainer.Add(new Cookie("steamLogin", steamLogin, "/", "." + SteamCommunityHost));

			string steamLoginSecure = authResult["tokensecure"].Value;
			WebBrowser.CookieContainer.Add(new Cookie("steamLoginSecure", steamLoginSecure, "/", "." + SteamCommunityHost));

			if (!UnlockParentalAccount(parentalPin).Result) {
				return false;
			}

			LastSessionRefreshCheck = DateTime.Now;
			return true;
		}

		internal async Task<bool> JoinGroup(ulong groupID) {
			if (groupID == 0) {
				return false;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return false;
			}

			string sessionID = WebBrowser.CookieContainer.GetCookieValue(SteamCommunityURL, "sessionid");
			if (string.IsNullOrEmpty(sessionID)) {
				Logging.LogNullError("sessionID");
				return false;
			}

			string request = SteamCommunityURL + "/gid/" + groupID;
			Dictionary<string, string> data = new Dictionary<string, string>(2) {
				{ "sessionID", sessionID },
				{ "action", "join" }
			};

			bool result = false;
			for (byte i = 0; i < WebBrowser.MaxRetries && !result; i++) {
				result = await WebBrowser.UrlPost(request, data).ConfigureAwait(false);
			}

			if (!result) {
				Logging.LogGenericWTF("Request failed even after " + WebBrowser.MaxRetries + " tries", Bot.BotName);
				return false;
			}

			return true;
		}

		internal async Task<Dictionary<uint, string>> GetOwnedGames() {
			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			string request = SteamCommunityURL + "/my/games/?xml=1";

			XmlDocument response = null;
			for (byte i = 0; i < WebBrowser.MaxRetries && response == null; i++) {
				response = await WebBrowser.UrlGetToXML(request).ConfigureAwait(false);
			}

			if (response == null) {
				Logging.LogGenericWTF("Request failed even after " + WebBrowser.MaxRetries + " tries", Bot.BotName);
				return null;
			}

			XmlNodeList xmlNodeList = response.SelectNodes("gamesList/games/game");
			if (xmlNodeList == null || xmlNodeList.Count == 0) {
				return null;
			}

			Dictionary<uint, string> result = new Dictionary<uint, string>(xmlNodeList.Count);
			foreach (XmlNode xmlNode in xmlNodeList) {
				XmlNode appNode = xmlNode.SelectSingleNode("appID");
				if (appNode == null) {
					continue;
				}

				uint appID;
				if (!uint.TryParse(appNode.InnerText, out appID)) {
					continue;
				}

				XmlNode nameNode = xmlNode.SelectSingleNode("name");
				if (nameNode == null) {
					continue;
				}

				result[appID] = nameNode.InnerText;
			}

			return result;
		}

		internal HashSet<Steam.TradeOffer> GetTradeOffers() {
			if (string.IsNullOrEmpty(Bot.BotConfig.SteamApiKey)) {
				return null;
			}

			KeyValue response = null;
			using (dynamic iEconService = WebAPI.GetInterface("IEconService", Bot.BotConfig.SteamApiKey)) {
				iEconService.Timeout = Timeout;

				for (byte i = 0; i < WebBrowser.MaxRetries && response == null; i++) {
					try {
						response = iEconService.GetTradeOffers(
							get_received_offers: 1,
							active_only: 1,
							get_descriptions: 1,
							secure: !Program.GlobalConfig.ForceHttp
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

			Dictionary<Tuple<ulong, ulong>, Tuple<uint, Steam.Item.EType>> descriptions = new Dictionary<Tuple<ulong, ulong>, Tuple<uint, Steam.Item.EType>>();
			foreach (KeyValue description in response["descriptions"].Children) {
				ulong classID = description["classid"].AsUnsignedLong();
				if (classID == 0) {
					continue;
				}

				ulong instanceID = description["instanceid"].AsUnsignedLong();

				Tuple<ulong, ulong> key = new Tuple<ulong, ulong>(classID, instanceID);
				if (descriptions.ContainsKey(key)) {
					continue;
				}

				uint appID = 0;
				Steam.Item.EType type = Steam.Item.EType.Unknown;

				string hashName = description["market_hash_name"].Value;
				if (!string.IsNullOrEmpty(hashName)) {
					appID = GetAppIDFromMarketHashName(hashName);
				}

				string descriptionType = description["type"].Value;
				if (!string.IsNullOrEmpty(descriptionType)) {
					type = GetItemType(descriptionType);
				}

				descriptions[key] = new Tuple<uint, Steam.Item.EType>(appID, type);
			}

			HashSet<Steam.TradeOffer> result = new HashSet<Steam.TradeOffer>();
			foreach (KeyValue trade in response["trade_offers_received"].Children) {
				// TODO: Correct some of these when SK2 with https://github.com/SteamRE/SteamKit/pull/255 gets released
				Steam.TradeOffer tradeOffer = new Steam.TradeOffer {
					TradeOfferID = trade["tradeofferid"].AsUnsignedLong(),
					OtherSteamID3 = (uint) trade["accountid_other"].AsUnsignedLong(),
					State = trade["trade_offer_state"].AsEnum<Steam.TradeOffer.ETradeOfferState>()
				};

				foreach (KeyValue item in trade["items_to_give"].Children) {
					Steam.Item steamItem = new Steam.Item {
						AppID = (uint) item["appid"].AsUnsignedLong(),
						ContextID = item["contextid"].AsUnsignedLong(),
						AssetID = item["assetid"].AsUnsignedLong(),
						ClassID = item["classid"].AsUnsignedLong(),
						InstanceID = item["instanceid"].AsUnsignedLong(),
						Amount = (uint) item["amount"].AsUnsignedLong()
					};

					Tuple<ulong, ulong> key = new Tuple<ulong, ulong>(steamItem.ClassID, steamItem.InstanceID);

					Tuple<uint, Steam.Item.EType> description;
					if (descriptions.TryGetValue(key, out description)) {
						steamItem.RealAppID = description.Item1;
						steamItem.Type = description.Item2;
					}

					tradeOffer.ItemsToGive.Add(steamItem);
				}

				foreach (KeyValue item in trade["items_to_receive"].Children) {
					Steam.Item steamItem = new Steam.Item {
						AppID = (uint) item["appid"].AsUnsignedLong(),
						ContextID = item["contextid"].AsUnsignedLong(),
						AssetID = item["assetid"].AsUnsignedLong(),
						ClassID = item["classid"].AsUnsignedLong(),
						InstanceID = item["instanceid"].AsUnsignedLong(),
						Amount = (uint) item["amount"].AsUnsignedLong()
					};

					Tuple<ulong, ulong> key = new Tuple<ulong, ulong>(steamItem.ClassID, steamItem.InstanceID);

					Tuple<uint, Steam.Item.EType> description;
					if (descriptions.TryGetValue(key, out description)) {
						steamItem.RealAppID = description.Item1;
						steamItem.Type = description.Item2;
					}

					tradeOffer.ItemsToReceive.Add(steamItem);
				}

				result.Add(tradeOffer);
			}

			return result;
		}

		internal async Task<bool> AcceptTradeOffer(ulong tradeID) {
			if (tradeID == 0) {
				return false;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return false;
			}

			string sessionID = WebBrowser.CookieContainer.GetCookieValue(SteamCommunityURL, "sessionid");
			if (string.IsNullOrEmpty(sessionID)) {
				Logging.LogNullError("sessionID");
				return false;
			}

			string request = SteamCommunityURL + "/tradeoffer/" + tradeID + "/accept";
			Dictionary<string, string> data = new Dictionary<string, string>(3) {
				{ "sessionid", sessionID },
				{ "serverid", "1" },
				{ "tradeofferid", tradeID.ToString() }
			};

			bool result = false;
			for (byte i = 0; i < WebBrowser.MaxRetries && !result; i++) {
				result = await WebBrowser.UrlPost(request, data, SteamCommunityURL).ConfigureAwait(false);
			}

			if (!result) {
				Logging.LogGenericWTF("Request failed even after " + WebBrowser.MaxRetries + " tries", Bot.BotName);
				return false;
			}

			return true;
		}

		internal async Task<HashSet<Steam.Item>> GetMyTradableInventory() {
			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			HashSet<Steam.Item> result = new HashSet<Steam.Item>();

			ushort nextPage = 0;
			while (true) {
				string request = SteamCommunityURL + "/my/inventory/json/" + Steam.Item.SteamAppID + "/" + Steam.Item.SteamContextID + "?trading=1&start=" + nextPage;

				JObject jObject = null;
				for (byte i = 0; i < WebBrowser.MaxRetries && jObject == null; i++) {
					jObject = await WebBrowser.UrlGetToJObject(request).ConfigureAwait(false);
				}

				if (jObject == null) {
					Logging.LogGenericWTF("Request failed even after " + WebBrowser.MaxRetries + " tries", Bot.BotName);
					return null;
				}

				IEnumerable<JToken> descriptions = jObject.SelectTokens("$.rgDescriptions.*");
				if (descriptions == null) {
					return null;
				}

				Dictionary<Tuple<ulong, ulong>, Tuple<uint, Steam.Item.EType>> descriptionMap = new Dictionary<Tuple<ulong, ulong>, Tuple<uint, Steam.Item.EType>>();
				foreach (JToken description in descriptions) {
					string classIDString = description["classid"].ToString();
					if (string.IsNullOrEmpty(classIDString)) {
						continue;
					}

					ulong classID;
					if (!ulong.TryParse(classIDString, out classID) || classID == 0) {
						continue;
					}

					string instanceIDString = description["instanceid"].ToString();
					if (string.IsNullOrEmpty(instanceIDString)) {
						continue;
					}

					ulong instanceID;
					if (!ulong.TryParse(instanceIDString, out instanceID)) {
						continue;
					}

					Tuple<ulong, ulong> key = new Tuple<ulong, ulong>(classID, instanceID);
					if (descriptionMap.ContainsKey(key)) {
						continue;
					}

					uint appID = 0;
					Steam.Item.EType type = Steam.Item.EType.Unknown;

					string hashName = description["market_hash_name"].ToString();
					if (!string.IsNullOrEmpty(hashName)) {
						appID = GetAppIDFromMarketHashName(hashName);
					}

					string descriptionType = description["type"].ToString();
					if (!string.IsNullOrEmpty(descriptionType)) {
						type = GetItemType(descriptionType);
					}

					descriptionMap[key] = new Tuple<uint, Steam.Item.EType>(appID, type);
				}

				IEnumerable<JToken> items = jObject.SelectTokens("$.rgInventory.*");
				if (descriptions == null) {
					return null;
				}

				foreach (JToken item in items) {

					Steam.Item steamItem;

					try {
						steamItem = JsonConvert.DeserializeObject<Steam.Item>(item.ToString());
					} catch (JsonException e) {
						Logging.LogGenericException(e, Bot.BotName);
						continue;
					}

					if (steamItem == null) {
						continue;
					}

					Tuple<ulong, ulong> key = new Tuple<ulong, ulong>(steamItem.ClassID, steamItem.InstanceID);

					Tuple<uint, Steam.Item.EType> description;
					if (descriptionMap.TryGetValue(key, out description)) {
						steamItem.RealAppID = description.Item1;
						steamItem.Type = description.Item2;
					}

					result.Add(steamItem);
				}

				bool more;
				if (!bool.TryParse(jObject["more"].ToString(), out more) || !more) {
					break;
				}

				if (!ushort.TryParse(jObject["more_start"].ToString(), out nextPage)) {
					break;
				}
			}

			return result;
		}

		internal async Task<bool> SendTradeOffer(HashSet<Steam.Item> inventory, ulong partnerID, string token = null) {
			if (inventory == null || inventory.Count == 0 || partnerID == 0) {
				return false;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return false;
			}

			string sessionID = WebBrowser.CookieContainer.GetCookieValue(SteamCommunityURL, "sessionid");
			if (string.IsNullOrEmpty(sessionID)) {
				Logging.LogNullError("sessionID");
				return false;
			}

			HashSet<Steam.TradeOfferRequest> trades = new HashSet<Steam.TradeOfferRequest>();

			Steam.TradeOfferRequest singleTrade = null;

			byte itemID = 0;
			foreach (Steam.Item item in inventory) {
				if (itemID % Trading.MaxItemsPerTrade == 0) {
					if (trades.Count >= Trading.MaxTradesPerAccount) {
						break;
					}

					singleTrade = new Steam.TradeOfferRequest();
					trades.Add(singleTrade);
					itemID = 0;
				}

				singleTrade.ItemsToGive.Assets.Add(new Steam.Item() {
					AppID = Steam.Item.SteamAppID,
					ContextID = Steam.Item.SteamContextID,
					Amount = item.Amount,
					AssetID = item.AssetID
				});

				itemID++;
			}

			string request = SteamCommunityURL + "/tradeoffer/new/send";
			foreach (Steam.TradeOfferRequest trade in trades) {
				Dictionary<string, string> data = new Dictionary<string, string>(6) {
					{ "sessionid", sessionID },
					{ "serverid", "1" },
					{ "partner", partnerID.ToString() },
					{ "tradeoffermessage", "Sent by ASF" },
					{ "json_tradeoffer", JsonConvert.SerializeObject(trade) },
					{ "trade_offer_create_params", string.IsNullOrEmpty(token) ? "" : $"{{\"trade_offer_access_token\":\"{token}\"}}" }
				};

				bool result = false;
				for (byte i = 0; i < WebBrowser.MaxRetries && !result; i++) {
					result = await WebBrowser.UrlPost(request, data, SteamCommunityURL).ConfigureAwait(false);
				}

				if (!result) {
					Logging.LogGenericWTF("Request failed even after " + WebBrowser.MaxRetries + " tries", Bot.BotName);
					return false;
				}
			}

			return true;
		}

		internal async Task<HtmlDocument> GetBadgePage(byte page) {
			if (page == 0) {
				return null;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			string request = SteamCommunityURL + "/my/badges?p=" + page;

			HtmlDocument htmlDocument = null;
			for (byte i = 0; i < WebBrowser.MaxRetries && htmlDocument == null; i++) {
				htmlDocument = await WebBrowser.UrlGetToHtmlDocument(request).ConfigureAwait(false);
			}

			if (htmlDocument == null) {
				Logging.LogGenericWTF("Request failed even after " + WebBrowser.MaxRetries + " tries", Bot.BotName);
				return null;
			}

			return htmlDocument;
		}

		internal async Task<HtmlDocument> GetGameCardsPage(ulong appID) {
			if (appID == 0) {
				return null;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			string request = SteamCommunityURL + "/my/gamecards/" + appID;

			HtmlDocument htmlDocument = null;
			for (byte i = 0; i < WebBrowser.MaxRetries && htmlDocument == null; i++) {
				htmlDocument = await WebBrowser.UrlGetToHtmlDocument(request).ConfigureAwait(false);
			}

			if (htmlDocument == null) {
				Logging.LogGenericWTF("Request failed even after " + WebBrowser.MaxRetries + " tries", Bot.BotName);
				return null;
			}

			return htmlDocument;
		}

		internal async Task<bool> MarkInventory() {
			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return false;
			}

			string request = SteamCommunityURL + "/my/inventory";

			bool result = false;
			for (byte i = 0; i < WebBrowser.MaxRetries && !result; i++) {
				result = await WebBrowser.UrlGet(request).ConfigureAwait(false);
			}

			if (!result) {
				Logging.LogGenericWTF("Request failed even after " + WebBrowser.MaxRetries + " tries", Bot.BotName);
				return false;
			}

			return true;
		}

		private async Task<bool?> IsLoggedIn() {
			string request = SteamCommunityURL + "/my/profile";

			HtmlDocument htmlDocument = null;
			for (byte i = 0; i < WebBrowser.MaxRetries && htmlDocument == null; i++) {
				htmlDocument = await WebBrowser.UrlGetToHtmlDocument(request).ConfigureAwait(false);
			}

			if (htmlDocument == null) {
				Logging.LogGenericWTF("Request failed even after " + WebBrowser.MaxRetries + " tries", Bot.BotName);
				return null;
			}

			HtmlNode htmlNode = htmlDocument.DocumentNode.SelectSingleNode("//span[@id='account_pulldown']");
			return htmlNode != null;
		}

		private async Task<bool> RefreshSessionIfNeeded() {
			if (DateTime.Now.Subtract(LastSessionRefreshCheck).TotalSeconds < MinSessionTTL) {
				return true;
			}

			await SessionSemaphore.WaitAsync().ConfigureAwait(false);

			if (DateTime.Now.Subtract(LastSessionRefreshCheck).TotalSeconds < MinSessionTTL) {
				SessionSemaphore.Release();
				return true;
			}

			bool result;

			bool? isLoggedIn = await IsLoggedIn().ConfigureAwait(false);
			if (isLoggedIn.GetValueOrDefault(true)) {
				result = true;
				LastSessionRefreshCheck = DateTime.Now;
			} else {
				Logging.LogGenericInfo("Refreshing our session!", Bot.BotName);
				result = await Bot.RefreshSession().ConfigureAwait(false);
			}

			SessionSemaphore.Release();
			return result;
		}

		private async Task<bool> UnlockParentalAccount(string parentalPin) {
			if (string.IsNullOrEmpty(parentalPin) || parentalPin.Equals("0")) {
				return true;
			}

			Logging.LogGenericInfo("Unlocking parental account...", Bot.BotName);

			string request = SteamCommunityURL + "/parental/ajaxunlock";
			Dictionary<string, string> data = new Dictionary<string, string>(1) {
				{ "pin", parentalPin }
			};

			bool result = false;
			for (byte i = 0; i < WebBrowser.MaxRetries && !result; i++) {
				result = await WebBrowser.UrlPost(request, data, SteamCommunityURL).ConfigureAwait(false);
			}

			if (!result) {
				Logging.LogGenericWTF("Request failed even after " + WebBrowser.MaxRetries + " tries", Bot.BotName);
				return false;
			}

			Logging.LogGenericInfo("Success!", Bot.BotName);
			return true;
		}
	}
}
