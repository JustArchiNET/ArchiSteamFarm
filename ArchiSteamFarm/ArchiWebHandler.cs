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
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Threading;
using ArchiSteamFarm.JSON;

namespace ArchiSteamFarm {
	internal sealed class ArchiWebHandler : IDisposable {
		private const string SteamCommunityHost = "steamcommunity.com";
		private const byte MinSessionTTL = GlobalConfig.DefaultHttpTimeout / 4; // Assume session is valid for at least that amount of seconds

		private static string SteamCommunityURL = "https://" + SteamCommunityHost;
		private static int Timeout = GlobalConfig.DefaultHttpTimeout * 1000; // This must be int type

		private readonly Bot Bot;
		private readonly SemaphoreSlim SessionSemaphore = new SemaphoreSlim(1);
		private readonly WebBrowser WebBrowser;

		internal bool Ready { get; private set; }

		private ulong SteamID;
		private DateTime LastSessionRefreshCheck = DateTime.MinValue;

		internal static void Init() {
			Timeout = Program.GlobalConfig.HttpTimeout * 1000;
			SteamCommunityURL = (Program.GlobalConfig.ForceHttp ? "http://" : "https://") + SteamCommunityHost;
		}

		private static uint GetAppIDFromMarketHashName(string hashName) {
			if (string.IsNullOrEmpty(hashName)) {
				Logging.LogNullError(nameof(hashName));
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
				Logging.LogNullError(nameof(name));
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

		private static bool ParseItems(Dictionary<ulong, Tuple<uint, Steam.Item.EType>> descriptions, List<KeyValue> input, HashSet<Steam.Item> output) {
			if ((descriptions == null) || (input == null) || (input.Count == 0) || (output == null)) {
				Logging.LogNullError(nameof(descriptions) + " || " + nameof(input) + " || " + nameof(output));
				return false;
			}

			foreach (KeyValue item in input) {
				uint appID = item["appid"].AsUnsignedInteger();
				if (appID == 0) {
					Logging.LogNullError(nameof(appID));
					return false;
				}

				ulong contextID = item["contextid"].AsUnsignedLong();
				if (contextID == 0) {
					Logging.LogNullError(nameof(contextID));
					return false;
				}

				ulong classID = item["classid"].AsUnsignedLong();
				if (classID == 0) {
					Logging.LogNullError(nameof(classID));
					return false;
				}

				uint amount = item["amount"].AsUnsignedInteger();
				if (amount == 0) {
					Logging.LogNullError(nameof(amount));
					return false;
				}

				uint realAppID = 0;
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

		internal ArchiWebHandler(Bot bot) {
			if (bot == null) {
				throw new ArgumentNullException(nameof(bot));
			}

			Bot = bot;

			WebBrowser = new WebBrowser(bot.BotName);
		}

		public void Dispose() => SessionSemaphore.Dispose();

		internal void OnDisconnected() => Ready = false;

		internal async Task<bool> Init(ulong steamID, EUniverse universe, string webAPIUserNonce, string parentalPin) {
			if ((steamID == 0) || (universe == EUniverse.Invalid) || string.IsNullOrEmpty(webAPIUserNonce)) {
				Logging.LogNullError(nameof(steamID) + " || " + nameof(universe) + " || " + nameof(webAPIUserNonce), Bot.BotName);
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
				Logging.LogNullError(nameof(authResult), Bot.BotName);
				return false;
			}

			Logging.LogGenericInfo("Success!", Bot.BotName);

			WebBrowser.CookieContainer.Add(new Cookie("sessionid", sessionID, "/", "." + SteamCommunityHost));

			string steamLogin = authResult["token"].Value;
			WebBrowser.CookieContainer.Add(new Cookie("steamLogin", steamLogin, "/", "." + SteamCommunityHost));

			string steamLoginSecure = authResult["tokensecure"].Value;
			WebBrowser.CookieContainer.Add(new Cookie("steamLoginSecure", steamLoginSecure, "/", "." + SteamCommunityHost));

			// Unlock Steam Parental if needed
			if (!await UnlockParentalAccount(parentalPin).ConfigureAwait(false)) {
				return false;
			}

			Ready = true;
			LastSessionRefreshCheck = DateTime.Now;
			return true;
		}

		internal async Task<bool> AcceptGift(ulong gid) {
			if (gid == 0) {
				Logging.LogNullError(nameof(gid), Bot.BotName);
				return false;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return false;
			}

			string sessionID = WebBrowser.CookieContainer.GetCookieValue(SteamCommunityURL, "sessionid");
			if (string.IsNullOrEmpty(sessionID)) {
				Logging.LogNullError(nameof(sessionID), Bot.BotName);
				return false;
			}

			string request = SteamCommunityURL + "/gifts/" + gid + "/acceptunpack";
			Dictionary<string, string> data = new Dictionary<string, string>(1) {
				{ "sessionid", sessionID }
			};

			return await WebBrowser.UrlPostRetry(request, data).ConfigureAwait(false);
		}

		internal async Task<bool> JoinGroup(ulong groupID) {
			if (groupID == 0) {
				Logging.LogNullError(nameof(groupID), Bot.BotName);
				return false;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return false;
			}

			string sessionID = WebBrowser.CookieContainer.GetCookieValue(SteamCommunityURL, "sessionid");
			if (string.IsNullOrEmpty(sessionID)) {
				Logging.LogNullError(nameof(sessionID), Bot.BotName);
				return false;
			}

			string request = SteamCommunityURL + "/gid/" + groupID;
			Dictionary<string, string> data = new Dictionary<string, string>(2) {
				{ "sessionID", sessionID },
				{ "action", "join" }
			};

			return await WebBrowser.UrlPostRetry(request, data).ConfigureAwait(false);
		}

		internal async Task<HtmlDocument> GetConfirmations(string deviceID, string confirmationHash, uint time) {
			if (string.IsNullOrEmpty(deviceID) || string.IsNullOrEmpty(confirmationHash) || (time == 0)) {
				Logging.LogNullError(nameof(deviceID) + " || " + nameof(confirmationHash) + " || " + nameof(time), Bot.BotName);
				return null;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			string request = SteamCommunityURL + "/mobileconf/conf?l=english&p=" + deviceID + "&a=" + SteamID + "&k=" + WebUtility.UrlEncode(confirmationHash) + "&t=" + time + "&m=android&tag=conf";
			return await WebBrowser.UrlGetToHtmlDocumentRetry(request).ConfigureAwait(false);
		}

		internal async Task<Steam.ConfirmationDetails> GetConfirmationDetails(string deviceID, string confirmationHash, uint time, MobileAuthenticator.Confirmation confirmation) {
			if (string.IsNullOrEmpty(deviceID) || string.IsNullOrEmpty(confirmationHash) || (time == 0) || (confirmation == null)) {
				Logging.LogNullError(nameof(deviceID) + " || " + nameof(confirmationHash) + " || " + nameof(time) + " || " + nameof(confirmation), Bot.BotName);
				return null;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			string request = SteamCommunityURL + "/mobileconf/details/" + confirmation.ID + "?l=english&p=" + deviceID + "&a=" + SteamID + "&k=" + WebUtility.UrlEncode(confirmationHash) + "&t=" + time + "&m=android&tag=conf";

			string json = await WebBrowser.UrlGetToContentRetry(request).ConfigureAwait(false);
			if (string.IsNullOrEmpty(json)) {
				return null;
			}

			Steam.ConfirmationDetails response;

			try {
				response = JsonConvert.DeserializeObject<Steam.ConfirmationDetails>(json);
			} catch (JsonException e) {
				Logging.LogGenericException(e, Bot.BotName);
				return null;
			}

			if (response == null) {
				Logging.LogNullError(nameof(response), Bot.BotName);
				return null;
			}

			response.Confirmation = confirmation;
			return response;
		}

		internal async Task<bool> HandleConfirmations(string deviceID, string confirmationHash, uint time, HashSet<MobileAuthenticator.Confirmation> confirmations, bool accept) {
			if (string.IsNullOrEmpty(deviceID) || string.IsNullOrEmpty(confirmationHash) || (time == 0) || (confirmations == null) || (confirmations.Count == 0)) {
				Logging.LogNullError(nameof(deviceID) + " || " + nameof(confirmationHash) + " || " + nameof(time) + " || " + nameof(confirmations), Bot.BotName);
				return false;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return false;
			}

			string request = SteamCommunityURL + "/mobileconf/multiajaxop";

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

			string json = await WebBrowser.UrlPostToContentRetry(request, data).ConfigureAwait(false);
			if (string.IsNullOrEmpty(json)) {
				return false;
			}

			Steam.ConfirmationResponse response;

			try {
				response = JsonConvert.DeserializeObject<Steam.ConfirmationResponse>(json);
			} catch (JsonException e) {
				Logging.LogGenericException(e, Bot.BotName);
				return false;
			}

			if (response != null) {
				return response.Success;
			}

			Logging.LogNullError(nameof(response), Bot.BotName);
			return false;
		}

		internal async Task<Dictionary<uint, string>> GetOwnedGames() {
			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			string request = SteamCommunityURL + "/my/games/?xml=1";

			XmlDocument response = await WebBrowser.UrlGetToXMLRetry(request).ConfigureAwait(false);

			XmlNodeList xmlNodeList = response?.SelectNodes("gamesList/games/game");
			if ((xmlNodeList == null) || (xmlNodeList.Count == 0)) {
				return null;
			}

			Dictionary<uint, string> result = new Dictionary<uint, string>(xmlNodeList.Count);
			foreach (XmlNode xmlNode in xmlNodeList) {
				XmlNode appNode = xmlNode.SelectSingleNode("appID");
				if (appNode == null) {
					Logging.LogNullError(nameof(appNode), Bot.BotName);
					return null;
				}

				uint appID;
				if (!uint.TryParse(appNode.InnerText, out appID)) {
					Logging.LogNullError(nameof(appID), Bot.BotName);
					return null;
				}

				XmlNode nameNode = xmlNode.SelectSingleNode("name");
				if (nameNode == null) {
					Logging.LogNullError(nameof(nameNode), Bot.BotName);
					return null;
				}

				result[appID] = nameNode.InnerText;
			}

			return result;
		}

		internal Dictionary<uint, string> GetOwnedGames(ulong steamID) {
			if ((steamID == 0) || string.IsNullOrEmpty(Bot.BotConfig.SteamApiKey)) {
				Logging.LogNullError(nameof(steamID) + " || " + nameof(Bot.BotConfig.SteamApiKey), Bot.BotName);
				return null;
			}

			KeyValue response = null;
			for (byte i = 0; (i < WebBrowser.MaxRetries) && (response == null); i++) {
				using (dynamic iPlayerService = WebAPI.GetInterface("IPlayerService", Bot.BotConfig.SteamApiKey)) {
					iPlayerService.Timeout = Timeout;

					try {
						response = iPlayerService.GetOwnedGames(
							steamid: steamID,
							include_appinfo: 1,
							secure: !Program.GlobalConfig.ForceHttp
						);
					} catch (Exception e) {
						Logging.LogGenericException(e, Bot.BotName);
					}
				}
			}

			if (response == null) {
				Logging.LogGenericWarning("Request failed even after " + WebBrowser.MaxRetries + " tries", Bot.BotName);
				return null;
			}

			Dictionary<uint, string> result = new Dictionary<uint, string>(response["games"].Children.Count);
			foreach (KeyValue game in response["games"].Children) {
				uint appID = game["appid"].AsUnsignedInteger();
				if (appID == 0) {
					Logging.LogNullError(nameof(appID), Bot.BotName);
					return null;
				}

				result[appID] = game["name"].Value;
			}

			return result;
		}

		internal uint GetServerTime() {
			KeyValue response = null;
			for (byte i = 0; (i < WebBrowser.MaxRetries) && (response == null); i++) {
				using (dynamic iTwoFactorService = WebAPI.GetInterface("ITwoFactorService")) {
					iTwoFactorService.Timeout = Timeout;

					try {
						response = iTwoFactorService.QueryTime(
							method: WebRequestMethods.Http.Post,
							secure: !Program.GlobalConfig.ForceHttp
						);
					} catch (Exception e) {
						Logging.LogGenericException(e, Bot.BotName);
					}
				}
			}

			if (response != null) {
				return response["server_time"].AsUnsignedInteger();
			}

			Logging.LogGenericWarning("Request failed even after " + WebBrowser.MaxRetries + " tries", Bot.BotName);
			return 0;
		}

		internal async Task<byte?> GetTradeHoldDuration(ulong tradeID) {
			if (tradeID == 0) {
				Logging.LogNullError(nameof(tradeID), Bot.BotName);
				return null;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
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
				Logging.LogNullError(nameof(text), Bot.BotName);
				return null;
			}

			int index = text.IndexOf("g_daysTheirEscrow = ", StringComparison.Ordinal);
			if (index < 0) {
				Logging.LogNullError(nameof(index), Bot.BotName);
				return null;
			}

			index += 20;
			text = text.Substring(index);

			index = text.IndexOf(';');
			if (index < 0) {
				Logging.LogNullError(nameof(index), Bot.BotName);
				return null;
			}

			text = text.Substring(0, index);

			byte holdDuration;
			if (byte.TryParse(text, out holdDuration)) {
				return holdDuration;
			}

			Logging.LogNullError(nameof(holdDuration), Bot.BotName);
			return null;
		}

		internal HashSet<Steam.TradeOffer> GetActiveTradeOffers() {
			if (string.IsNullOrEmpty(Bot.BotConfig.SteamApiKey)) {
				Logging.LogNullError(nameof(Bot.BotConfig.SteamApiKey), Bot.BotName);
				return null;
			}

			KeyValue response = null;
			for (byte i = 0; (i < WebBrowser.MaxRetries) && (response == null); i++) {
				using (dynamic iEconService = WebAPI.GetInterface("IEconService", Bot.BotConfig.SteamApiKey)) {
					iEconService.Timeout = Timeout;

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
				Logging.LogGenericWarning("Request failed even after " + WebBrowser.MaxRetries + " tries", Bot.BotName);
				return null;
			}

			Dictionary<ulong, Tuple<uint, Steam.Item.EType>> descriptions = new Dictionary<ulong, Tuple<uint, Steam.Item.EType>>();
			foreach (KeyValue description in response["descriptions"].Children) {
				ulong classID = description["classid"].AsUnsignedLong();
				if (classID == 0) {
					Logging.LogNullError(nameof(classID), Bot.BotName);
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
					Logging.LogNullError(nameof(state));
					return null;
				}

				if (state != Steam.TradeOffer.ETradeOfferState.Active) {
					continue;
				}

				ulong tradeOfferID = trade["tradeofferid"].AsUnsignedLong();
				if (tradeOfferID == 0) {
					Logging.LogNullError(nameof(tradeOfferID));
					return null;
				}

				uint otherSteamID3 = trade["accountid_other"].AsUnsignedInteger();
				if (otherSteamID3 == 0) {
					Logging.LogNullError(nameof(otherSteamID3));
					return null;
				}

				Steam.TradeOffer tradeOffer = new Steam.TradeOffer(tradeOfferID, otherSteamID3, state);

				List<KeyValue> itemsToGive = trade["items_to_give"].Children;
				if (itemsToGive.Count > 0) {
					if (!ParseItems(descriptions, itemsToGive, tradeOffer.ItemsToGive)) {
						Logging.LogGenericError("Parsing " + nameof(itemsToGive) + " failed!", Bot.BotName);
						return null;
					}
				}

				List<KeyValue> itemsToReceive = trade["items_to_receive"].Children;
				if (itemsToReceive.Count > 0) {
					if (!ParseItems(descriptions, itemsToReceive, tradeOffer.ItemsToReceive)) {
						Logging.LogGenericError("Parsing " + nameof(itemsToReceive) + " failed!", Bot.BotName);
						return null;
					}
				}

				result.Add(tradeOffer);
			}

			return result;
		}

		internal async Task<bool> AcceptTradeOffer(ulong tradeID) {
			if (tradeID == 0) {
				Logging.LogNullError(nameof(tradeID), Bot.BotName);
				return false;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return false;
			}

			string sessionID = WebBrowser.CookieContainer.GetCookieValue(SteamCommunityURL, "sessionid");
			if (string.IsNullOrEmpty(sessionID)) {
				Logging.LogNullError(nameof(sessionID), Bot.BotName);
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

		internal bool DeclineTradeOffer(ulong tradeID) {
			if ((tradeID == 0) || string.IsNullOrEmpty(Bot.BotConfig.SteamApiKey)) {
				Logging.LogNullError(nameof(tradeID) + " || " + nameof(Bot.BotConfig.SteamApiKey), Bot.BotName);
				return false;
			}

			KeyValue response = null;
			for (byte i = 0; (i < WebBrowser.MaxRetries) && (response == null); i++) {
				using (dynamic iEconService = WebAPI.GetInterface("IEconService", Bot.BotConfig.SteamApiKey)) {
					iEconService.Timeout = Timeout;

					try {
						response = iEconService.DeclineTradeOffer(
							tradeofferid: tradeID.ToString(),
							method: WebRequestMethods.Http.Post,
							secure: !Program.GlobalConfig.ForceHttp
						);
					} catch (Exception e) {
						Logging.LogGenericException(e, Bot.BotName);
					}
				}
			}

			if (response != null) {
				return true;
			}

			Logging.LogGenericWarning("Request failed even after " + WebBrowser.MaxRetries + " tries", Bot.BotName);
			return false;
		}

		internal async Task<HashSet<Steam.Item>> GetMySteamInventory(bool tradable) {
			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			HashSet<Steam.Item> result = new HashSet<Steam.Item>();

			string request = SteamCommunityURL + "/my/inventory/json/" + Steam.Item.SteamAppID + "/" + Steam.Item.SteamContextID + "?l=english&trading=" + (tradable ? "1" : "0") + "&start=";
			uint currentPage = 0;

			while (true) {
				JObject jObject = await WebBrowser.UrlGetToJObjectRetry(request + currentPage).ConfigureAwait(false);

				IEnumerable<JToken> descriptions = jObject?.SelectTokens("$.rgDescriptions.*");
				if (descriptions == null) {
					return null; // OK, empty inventory
				}

				Dictionary<ulong, Tuple<uint, Steam.Item.EType>> descriptionMap = new Dictionary<ulong, Tuple<uint, Steam.Item.EType>>();
				foreach (JToken description in descriptions.Where(description => description != null)) {
					string classIDString = description["classid"].ToString();
					if (string.IsNullOrEmpty(classIDString)) {
						Logging.LogNullError(nameof(classIDString), Bot.BotName);
						return null;
					}

					ulong classID;
					if (!ulong.TryParse(classIDString, out classID) || (classID == 0)) {
						Logging.LogNullError(nameof(classID), Bot.BotName);
						return null;
					}

					if (descriptionMap.ContainsKey(classID)) {
						continue;
					}

					uint appID = 0;

					string hashName = description["market_hash_name"].ToString();
					if (!string.IsNullOrEmpty(hashName)) {
						appID = GetAppIDFromMarketHashName(hashName);
					}

					if (appID == 0) {
						string appIDString = description["appid"].ToString();
						if (string.IsNullOrEmpty(appIDString)) {
							Logging.LogNullError(nameof(appIDString), Bot.BotName);
							return null;
						}

						if (!uint.TryParse(appIDString, out appID)) {
							Logging.LogNullError(nameof(appID), Bot.BotName);
							return null;
						}
					}

					Steam.Item.EType type = Steam.Item.EType.Unknown;

					string descriptionType = description["type"].ToString();
					if (!string.IsNullOrEmpty(descriptionType)) {
						type = GetItemType(descriptionType);
					}

					descriptionMap[classID] = new Tuple<uint, Steam.Item.EType>(appID, type);
				}

				IEnumerable<JToken> items = jObject.SelectTokens("$.rgInventory.*");
				if (items == null) {
					Logging.LogNullError(nameof(items), Bot.BotName);
					return null;
				}

				foreach (JToken item in items.Where(item => item != null)) {
					Steam.Item steamItem;

					try {
						steamItem = item.ToObject<Steam.Item>();
					} catch (JsonException e) {
						Logging.LogGenericException(e, Bot.BotName);
						return null;
					}

					if (steamItem == null) {
						Logging.LogNullError(nameof(steamItem), Bot.BotName);
						return null;
					}

					Tuple<uint, Steam.Item.EType> description;
					if (descriptionMap.TryGetValue(steamItem.ClassID, out description)) {
						steamItem.RealAppID = description.Item1;
						steamItem.Type = description.Item2;
					}

					result.Add(steamItem);
				}

				bool more;
				if (!bool.TryParse(jObject["more"].ToString(), out more) || !more) {
					break; // OK, last page
				}

				uint nextPage;
				if (!uint.TryParse(jObject["more_start"].ToString(), out nextPage) || (nextPage <= currentPage)) {
					Logging.LogNullError(nameof(nextPage), Bot.BotName);
					return null;
				}

				currentPage = nextPage;
			}

			return result;
		}

		internal async Task<bool> SendTradeOffer(HashSet<Steam.Item> inventory, ulong partnerID, string token = null) {
			if ((inventory == null) || (inventory.Count == 0) || (partnerID == 0)) {
				Logging.LogNullError(nameof(inventory) + " || " + nameof(inventory.Count) + " || " + nameof(partnerID), Bot.BotName);
				return false;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return false;
			}

			string sessionID = WebBrowser.CookieContainer.GetCookieValue(SteamCommunityURL, "sessionid");
			if (string.IsNullOrEmpty(sessionID)) {
				Logging.LogNullError(nameof(sessionID), Bot.BotName);
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

				singleTrade.ItemsToGive.Assets.Add(new Steam.Item(Steam.Item.SteamAppID, Steam.Item.SteamContextID, item.AssetID, item.Amount));
				itemID++;
			}

			string referer = SteamCommunityURL + "/tradeoffer/new";
			string request = referer + "/send";
			foreach (Dictionary<string, string> data in trades.Select(trade => new Dictionary<string, string>(6) {
				{ "sessionid", sessionID },
				{ "serverid", "1" },
				{ "partner", partnerID.ToString() },
				{ "tradeoffermessage", "Sent by ASF" },
				{ "json_tradeoffer", JsonConvert.SerializeObject(trade) },
				{ "trade_offer_create_params", string.IsNullOrEmpty(token) ? "" : $"{{\"trade_offer_access_token\":\"{token}\"}}" }
			})) {
				if (!await WebBrowser.UrlPostRetry(request, data, referer).ConfigureAwait(false)) {
					return false;
				}
			}

			return true;
		}

		internal async Task<HtmlDocument> GetBadgePage(byte page) {
			if (page == 0) {
				Logging.LogNullError(nameof(page), Bot.BotName);
				return null;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			string request = SteamCommunityURL + "/my/badges?l=english&p=" + page;
			return await WebBrowser.UrlGetToHtmlDocumentRetry(request).ConfigureAwait(false);
		}

		internal async Task<HtmlDocument> GetGameCardsPage(ulong appID) {
			if (appID == 0) {
				Logging.LogNullError(nameof(appID), Bot.BotName);
				return null;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			string request = SteamCommunityURL + "/my/gamecards/" + appID + "?l=english";
			return await WebBrowser.UrlGetToHtmlDocumentRetry(request).ConfigureAwait(false);
		}

		internal async Task<bool> MarkInventory() {
			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return false;
			}

			string request = SteamCommunityURL + "/my/inventory";
			return await WebBrowser.UrlHeadRetry(request).ConfigureAwait(false);
		}

		private async Task<bool?> IsLoggedIn() {
			// It would make sense to use /my/profile here, but it dismisses notifications related to profile comments
			// So instead, we'll use some less intrusive link, such as /my/videos
			string request = SteamCommunityURL + "/my/videos";

			Uri uri = await WebBrowser.UrlHeadToUriRetry(request).ConfigureAwait(false);
			return !uri?.AbsolutePath.StartsWith("/login", StringComparison.Ordinal);
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
			if (string.IsNullOrEmpty(parentalPin)) {
				Logging.LogNullError(nameof(parentalPin), Bot.BotName);
				return false;
			}

			if (parentalPin.Equals("0")) {
				return true;
			}

			Logging.LogGenericInfo("Unlocking parental account...", Bot.BotName);

			string request = SteamCommunityURL + "/parental/ajaxunlock";
			Dictionary<string, string> data = new Dictionary<string, string>(1) {
				{ "pin", parentalPin }
			};

			bool result = await WebBrowser.UrlPostRetry(request, data, SteamCommunityURL).ConfigureAwait(false);
			if (!result) {
				Logging.LogGenericInfo("Failed!", Bot.BotName);
				return false;
			}

			Logging.LogGenericInfo("Success!", Bot.BotName);
			return true;
		}
	}
}
