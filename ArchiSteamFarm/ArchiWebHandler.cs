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
				Logging.LogNullError(nameof(hashName));
				return 0;
			}

			int index = hashName.IndexOf('-');
			if (index < 1) {
				return 0;
			}

			uint appID;
			if (uint.TryParse(hashName.Substring(0, index), out appID)) {
				return appID;
			}

			Logging.LogNullError(nameof(appID));
			return 0;
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

		internal ArchiWebHandler(Bot bot) {
			if (bot == null) {
				throw new ArgumentNullException(nameof(bot));
			}

			Bot = bot;

			WebBrowser = new WebBrowser(bot.BotName);
		}

		internal bool Init(SteamClient steamClient, string webAPIUserNonce, string parentalPin) {
			if ((steamClient == null) || string.IsNullOrEmpty(webAPIUserNonce)) {
				Logging.LogNullError(nameof(steamClient) + " || " + nameof(webAPIUserNonce), Bot.BotName);
				return false;
			}

			ulong steamID = steamClient.SteamID;
			if (steamID == 0) {
				return false;
			}

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
				Logging.LogNullError(nameof(authResult), Bot.BotName);
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

		internal async Task<Dictionary<uint, string>> GetOwnedGames() {
			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			string request = SteamCommunityURL + "/my/games/?xml=1";

			XmlDocument response = await WebBrowser.UrlGetToXMLRetry(request).ConfigureAwait(false);
			if (response == null) {
				return null;
			}

			XmlNodeList xmlNodeList = response.SelectNodes("gamesList/games/game");
			if ((xmlNodeList == null) || (xmlNodeList.Count == 0)) {
				return null;
			}

			Dictionary<uint, string> result = new Dictionary<uint, string>(xmlNodeList.Count);
			foreach (XmlNode xmlNode in xmlNodeList) {
				XmlNode appNode = xmlNode.SelectSingleNode("appID");
				if (appNode == null) {
					Logging.LogNullError(nameof(appNode), Bot.BotName);
					continue;
				}

				uint appID;
				if (!uint.TryParse(appNode.InnerText, out appID)) {
					Logging.LogNullError(nameof(appID), Bot.BotName);
					continue;
				}

				XmlNode nameNode = xmlNode.SelectSingleNode("name");
				if (nameNode == null) {
					Logging.LogNullError(nameof(nameNode), Bot.BotName);
					continue;
				}

				result[appID] = nameNode.InnerText;
			}

			return result;
		}

		internal Dictionary<uint, string> GetOwnedGames(ulong steamID) {
			if ((steamID == 0) || string.IsNullOrEmpty(Bot.BotConfig.SteamApiKey)) {
				// TODO: Correct this when Mono 4.4+ will be a latest stable one | https://bugzilla.xamarin.com/show_bug.cgi?id=39455
				Logging.LogNullError("steamID || SteamApiKey", Bot.BotName);
				//Logging.LogNullError(nameof(steamID) + " || " + nameof(Bot.BotConfig.SteamApiKey), Bot.BotName);
				return null;
			}

			KeyValue response = null;
			using (dynamic iPlayerService = WebAPI.GetInterface("IPlayerService", Bot.BotConfig.SteamApiKey)) {
				iPlayerService.Timeout = Timeout;

				for (byte i = 0; (i < WebBrowser.MaxRetries) && (response == null); i++) {
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
				Logging.LogGenericWTF("Request failed even after " + WebBrowser.MaxRetries + " tries", Bot.BotName);
				return null;
			}

			Dictionary<uint, string> result = new Dictionary<uint, string>(response["games"].Children.Count);
			foreach (KeyValue game in response["games"].Children) {
				uint appID = (uint) game["appid"].AsUnsignedLong();
				if (appID == 0) {
					Logging.LogNullError(nameof(appID));
					continue;
				}

				result[appID] = game["name"].Value;
			}

			return result;
		}

		internal HashSet<Steam.TradeOffer> GetTradeOffers() {
			if (string.IsNullOrEmpty(Bot.BotConfig.SteamApiKey)) {
				// TODO: Correct this when Mono 4.4+ will be a latest stable one | https://bugzilla.xamarin.com/show_bug.cgi?id=39455
				Logging.LogNullError("SteamApiKey", Bot.BotName);
				//Logging.LogNullError(nameof(Bot.BotConfig.SteamApiKey), Bot.BotName);
				return null;
			}

			KeyValue response = null;
			using (dynamic iEconService = WebAPI.GetInterface("IEconService", Bot.BotConfig.SteamApiKey)) {
				iEconService.Timeout = Timeout;

				for (byte i = 0; (i < WebBrowser.MaxRetries) && (response == null); i++) {
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

			Dictionary<ulong, Tuple<uint, Steam.Item.EType>> descriptions = new Dictionary<ulong, Tuple<uint, Steam.Item.EType>>();
			foreach (KeyValue description in response["descriptions"].Children) {
				ulong classID = description["classid"].AsUnsignedLong();
				if (classID == 0) {
					Logging.LogNullError(nameof(classID), Bot.BotName);
					continue;
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
					appID = (uint) description["appid"].AsUnsignedLong();
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
				Steam.TradeOffer tradeOffer = new Steam.TradeOffer {
					TradeOfferID = trade["tradeofferid"].AsUnsignedLong(),
					OtherSteamID3 = (uint) trade["accountid_other"].AsUnsignedLong(),
					State = trade["trade_offer_state"].AsEnum<Steam.TradeOffer.ETradeOfferState>()
				};

				foreach (Steam.Item steamItem in trade["items_to_give"].Children.Select(item => new Steam.Item {
					AppID = (uint) item["appid"].AsUnsignedLong(),
					ContextID = item["contextid"].AsUnsignedLong(),
					AssetID = item["assetid"].AsUnsignedLong(),
					ClassID = item["classid"].AsUnsignedLong(),
					InstanceID = item["instanceid"].AsUnsignedLong(),
					Amount = (uint) item["amount"].AsUnsignedLong()
				})) {
					Tuple<uint, Steam.Item.EType> description;
					if (descriptions.TryGetValue(steamItem.ClassID, out description)) {
						steamItem.RealAppID = description.Item1;
						steamItem.Type = description.Item2;
					}

					tradeOffer.ItemsToGive.Add(steamItem);
				}

				foreach (Steam.Item steamItem in trade["items_to_receive"].Children.Select(item => new Steam.Item {
					AppID = (uint) item["appid"].AsUnsignedLong(),
					ContextID = item["contextid"].AsUnsignedLong(),
					AssetID = item["assetid"].AsUnsignedLong(),
					ClassID = item["classid"].AsUnsignedLong(),
					InstanceID = item["instanceid"].AsUnsignedLong(),
					Amount = (uint) item["amount"].AsUnsignedLong()
				})) {
					Tuple<uint, Steam.Item.EType> description;
					if (descriptions.TryGetValue(steamItem.ClassID, out description)) {
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
				// TODO: Correct this when Mono 4.4+ will be a latest stable one | https://bugzilla.xamarin.com/show_bug.cgi?id=39455
				Logging.LogNullError("tradeID || SteamApiKey", Bot.BotName);
				//Logging.LogNullError(nameof(tradeID) + " || " + nameof(Bot.BotConfig.SteamApiKey), Bot.BotName);
				return false;
			}

			KeyValue response = null;
			using (dynamic iEconService = WebAPI.GetInterface("IEconService", Bot.BotConfig.SteamApiKey)) {
				iEconService.Timeout = Timeout;

				for (byte i = 0; (i < WebBrowser.MaxRetries) && (response == null); i++) {
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

			Logging.LogGenericWTF("Request failed even after " + WebBrowser.MaxRetries + " tries", Bot.BotName);
			return false;
		}

		internal async Task<HashSet<Steam.Item>> GetMyInventory(bool tradable) {
			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			HashSet<Steam.Item> result = new HashSet<Steam.Item>();

			uint currentPage = 0;
			while (true) {
				string request = SteamCommunityURL + "/my/inventory/json/" + Steam.Item.SteamAppID + "/" + Steam.Item.SteamContextID + "?trading=" + (tradable ? "1" : "0") + "&start=" + currentPage;

				JObject jObject = await WebBrowser.UrlGetToJObjectRetry(request).ConfigureAwait(false);
				if (jObject == null) {
					return null;
				}

				IEnumerable<JToken> descriptions = jObject.SelectTokens("$.rgDescriptions.*");
				if (descriptions == null) {
					return null; // OK, empty inventory
				}

				Dictionary<ulong, Tuple<uint, Steam.Item.EType>> descriptionMap = new Dictionary<ulong, Tuple<uint, Steam.Item.EType>>();
				foreach (JToken description in descriptions) {
					string classIDString = description["classid"].ToString();
					if (string.IsNullOrEmpty(classIDString)) {
						Logging.LogNullError(nameof(classIDString), Bot.BotName);
						continue;
					}

					ulong classID;
					if (!ulong.TryParse(classIDString, out classID) || (classID == 0)) {
						Logging.LogNullError(nameof(classID), Bot.BotName);
						continue;
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
							continue;
						}

						if (!uint.TryParse(appIDString, out appID)) {
							Logging.LogNullError(nameof(appID), Bot.BotName);
							continue;
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

				foreach (JToken item in items) {
					Steam.Item steamItem;

					try {
						steamItem = item.ToObject<Steam.Item>();
					} catch (JsonException e) {
						Logging.LogGenericException(e, Bot.BotName);
						continue;
					}

					if (steamItem == null) {
						Logging.LogNullError(nameof(steamItem), Bot.BotName);
						continue;
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
				if (!uint.TryParse(jObject["more_start"].ToString(), out nextPage)) {
					Logging.LogNullError(nameof(nextPage), Bot.BotName);
					break;
				}

				if (nextPage <= currentPage) {
					break;
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

				singleTrade.ItemsToGive.Assets.Add(new Steam.Item {
					AppID = Steam.Item.SteamAppID,
					ContextID = Steam.Item.SteamContextID,
					Amount = item.Amount,
					AssetID = item.AssetID
				});

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

			string request = SteamCommunityURL + "/my/badges?p=" + page;

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

			string request = SteamCommunityURL + "/my/gamecards/" + appID;

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
			if (uri == null) {
				return null;
			}

			return !uri.AbsolutePath.StartsWith("/login", StringComparison.Ordinal);
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
