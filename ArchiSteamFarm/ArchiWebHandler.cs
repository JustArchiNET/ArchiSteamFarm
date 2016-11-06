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
		private const string SteamStoreHost = "store.steampowered.com";
		private const byte MinSessionTTL = GlobalConfig.DefaultHttpTimeout / 4; // Assume session is valid for at least that amount of seconds

		private static string SteamCommunityURL = "https://" + SteamCommunityHost;
		private static string SteamStoreURL = "https://" + SteamStoreHost;
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
			SteamStoreURL = (Program.GlobalConfig.ForceHttp ? "http://" : "https://") + SteamStoreHost;
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

			uint appID;
			return uint.TryParse(hashName.Substring(0, index), out appID) ? appID : 0;
		}

		private static Steam.Item.EType GetItemType(string name) {
			if (string.IsNullOrEmpty(name)) {
				ASF.ArchiLogger.LogNullError(nameof(name));
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

			WebBrowser = new WebBrowser(bot.ArchiLogger);
		}

		public void Dispose() => SessionSemaphore.Dispose();

		internal void OnDisconnected() => Ready = false;

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
			Bot.ArchiLogger.LogGenericInfo("Logging in to ISteamUserAuth...");

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
					Bot.ArchiLogger.LogGenericException(e);
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

			Bot.ArchiLogger.LogGenericInfo("Success!");

			// Unlock Steam Parental if needed
			if (!parentalPin.Equals("0")) {
				if (!await UnlockParentalAccount(parentalPin).ConfigureAwait(false)) {
					return false;
				}
			}

			Ready = true;
			LastSessionRefreshCheck = DateTime.Now;
			return true;
		}

		internal async Task<HashSet<ulong>> GetFamilySharingSteamIDs() {
			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			string request = SteamStoreURL + "/account/managedevices";
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
			if ((response == null) || !response.Success) {
				return null;
			}

			response.Confirmation = confirmation;
			return response;
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

		internal async Task<bool?> HandleConfirmations(string deviceID, string confirmationHash, uint time, HashSet<MobileAuthenticator.Confirmation> confirmations, bool accept) {
			if (string.IsNullOrEmpty(deviceID) || string.IsNullOrEmpty(confirmationHash) || (time == 0) || (confirmations == null) || (confirmations.Count == 0)) {
				Bot.ArchiLogger.LogNullError(nameof(deviceID) + " || " + nameof(confirmationHash) + " || " + nameof(time) + " || " + nameof(confirmations));
				return null;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
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

			Steam.ConfirmationResponse response = await WebBrowser.UrlPostToJsonResultRetry<Steam.ConfirmationResponse>(request, data).ConfigureAwait(false);
			return response?.Success;
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

		internal Dictionary<uint, string> GetOwnedGames(ulong steamID) {
			if ((steamID == 0) || string.IsNullOrEmpty(Bot.BotConfig.SteamApiKey)) {
				Bot.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(Bot.BotConfig.SteamApiKey));
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
						Bot.ArchiLogger.LogGenericException(e);
					}
				}
			}

			if (response == null) {
				Bot.ArchiLogger.LogGenericWarning("Request failed even after " + WebBrowser.MaxRetries + " tries");
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
				using (dynamic iTwoFactorService = WebAPI.GetInterface("ITwoFactorService")) {
					iTwoFactorService.Timeout = Timeout;

					try {
						response = iTwoFactorService.QueryTime(
							method: WebRequestMethods.Http.Post,
							secure: !Program.GlobalConfig.ForceHttp
						);
					} catch (Exception e) {
						Bot.ArchiLogger.LogGenericException(e);
					}
				}
			}

			if (response != null) {
				return response["server_time"].AsUnsignedInteger();
			}

			Bot.ArchiLogger.LogGenericWarning("Request failed even after " + WebBrowser.MaxRetries + " tries");
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

		internal async Task<ArchiHandler.PurchaseResponseCallback.EPurchaseResult> RedeemWalletKey(string key) {
			if (string.IsNullOrEmpty(key)) {
				Bot.ArchiLogger.LogNullError(nameof(key));
				return ArchiHandler.PurchaseResponseCallback.EPurchaseResult.Unknown;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return ArchiHandler.PurchaseResponseCallback.EPurchaseResult.Timeout;
			}

			string request = SteamStoreURL + "/account/validatewalletcode";
			Dictionary<string, string> data = new Dictionary<string, string>(1) {
				{ "wallet_code", key }
			};

			Steam.RedeemWalletResponse response = await WebBrowser.UrlPostToJsonResultRetry<Steam.RedeemWalletResponse>(request, data).ConfigureAwait(false);
			return response?.PurchaseResult ?? ArchiHandler.PurchaseResponseCallback.EPurchaseResult.Timeout;
		}

		internal HashSet<Steam.TradeOffer> GetActiveTradeOffers() {
			if (string.IsNullOrEmpty(Bot.BotConfig.SteamApiKey)) {
				Bot.ArchiLogger.LogNullError(nameof(Bot.BotConfig.SteamApiKey));
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
						Bot.ArchiLogger.LogGenericException(e);
					}
				}
			}

			if (response == null) {
				Bot.ArchiLogger.LogGenericWarning("Request failed even after " + WebBrowser.MaxRetries + " tries");
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
						Bot.ArchiLogger.LogGenericError("Parsing " + nameof(itemsToGive) + " failed!");
						return null;
					}
				}

				List<KeyValue> itemsToReceive = trade["items_to_receive"].Children;
				if (itemsToReceive.Count > 0) {
					if (!ParseItems(descriptions, itemsToReceive, tradeOffer.ItemsToReceive)) {
						Bot.ArchiLogger.LogGenericError("Parsing " + nameof(itemsToReceive) + " failed!");
						return null;
					}
				}

				result.Add(tradeOffer);
			}

			return result;
		}

		internal async Task AcceptTradeOffer(ulong tradeID) {
			if (tradeID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(tradeID));
				return;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return;
			}

			string sessionID = WebBrowser.CookieContainer.GetCookieValue(SteamCommunityURL, "sessionid");
			if (string.IsNullOrEmpty(sessionID)) {
				Bot.ArchiLogger.LogNullError(nameof(sessionID));
				return;
			}

			string referer = SteamCommunityURL + "/tradeoffer/" + tradeID;
			string request = referer + "/accept";

			Dictionary<string, string> data = new Dictionary<string, string>(3) {
				{ "sessionid", sessionID },
				{ "serverid", "1" },
				{ "tradeofferid", tradeID.ToString() }
			};

			await WebBrowser.UrlPostRetry(request, data, referer).ConfigureAwait(false);
		}

		internal void DeclineTradeOffer(ulong tradeID) {
			if ((tradeID == 0) || string.IsNullOrEmpty(Bot.BotConfig.SteamApiKey)) {
				Bot.ArchiLogger.LogNullError(nameof(tradeID) + " || " + nameof(Bot.BotConfig.SteamApiKey));
				return;
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
						Bot.ArchiLogger.LogGenericException(e);
					}
				}
			}

			if (response == null) {
				Bot.ArchiLogger.LogGenericWarning("Request failed even after " + WebBrowser.MaxRetries + " tries");
			}
		}

		internal async Task<HashSet<Steam.Item>> GetMySteamInventory(bool tradable) {
			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
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

		internal async Task<bool> SendTradeOffer(HashSet<Steam.Item> inventory, ulong partnerID, string token = null) {
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
				Bot.ArchiLogger.LogNullError(nameof(page));
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
				Bot.ArchiLogger.LogNullError(nameof(appID));
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

			try {
				if (DateTime.Now.Subtract(LastSessionRefreshCheck).TotalSeconds < MinSessionTTL) {
					return true;
				}

				bool? isLoggedIn = await IsLoggedIn().ConfigureAwait(false);
				if (isLoggedIn.GetValueOrDefault(true)) {
					LastSessionRefreshCheck = DateTime.Now;
					return true;
				} else {
					Bot.ArchiLogger.LogGenericInfo("Refreshing our session!");
					return await Bot.RefreshSession().ConfigureAwait(false);
				}
			} finally {
				SessionSemaphore.Release();
			}
		}

		private async Task<bool> UnlockParentalAccount(string parentalPin) {
			if (string.IsNullOrEmpty(parentalPin)) {
				Bot.ArchiLogger.LogNullError(nameof(parentalPin));
				return false;
			}

			Bot.ArchiLogger.LogGenericInfo("Unlocking parental account...");

			string request = SteamCommunityURL + "/parental/ajaxunlock";
			Dictionary<string, string> data = new Dictionary<string, string>(1) {
				{ "pin", parentalPin }
			};

			bool result = await WebBrowser.UrlPostRetry(request, data, SteamCommunityURL).ConfigureAwait(false);
			if (!result) {
				Bot.ArchiLogger.LogGenericInfo("Failed!");
				return false;
			}

			Bot.ArchiLogger.LogGenericInfo("Success!");
			return true;
		}
	}
}
