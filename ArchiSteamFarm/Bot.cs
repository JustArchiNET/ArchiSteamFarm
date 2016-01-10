/*
    _                _      _  ____   _                           _____
   / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
  / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
 / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
/_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|

 Copyright 2015 Łukasz "JustArchi" Domeradzki
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
using SteamAuth;
using SteamKit2;
using SteamKit2.Internal;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Xml;
using System.Text;

namespace ArchiSteamFarm {
	internal sealed class Bot {
		private const ulong ArchiSCFarmGroup = 103582791440160998;
		private const ushort CallbackSleep = 500; // In miliseconds

		private static readonly uint LoginID = MsgClientLogon.ObfuscationMask; // This must be the same for all ASF bots and all ASF processes

		internal static readonly ConcurrentDictionary<string, Bot> Bots = new ConcurrentDictionary<string, Bot>();
		internal static readonly HashSet<uint> GlobalBlacklist = new HashSet<uint> { 303700, 335590, 368020, 425280 };

		private readonly string ConfigFile, LoginKeyFile, MobileAuthenticatorFile, SentryFile;

		internal readonly string BotName;
		internal readonly ArchiHandler ArchiHandler;
		internal readonly ArchiWebHandler ArchiWebHandler;
		internal readonly CallbackManager CallbackManager;
		internal readonly CardsFarmer CardsFarmer;
		internal readonly SteamClient SteamClient;
		internal readonly SteamFriends SteamFriends;
		internal readonly SteamUser SteamUser;
		internal readonly Trading Trading;

		private bool KeepRunning = true;
		private bool InvalidPassword = false;
		private bool LoggedInElsewhere = false;
		private string AuthCode, LoginKey, TwoFactorAuth;

		internal SteamGuardAccount SteamGuardAccount { get; private set; }

		// Config variables
		internal bool Enabled { get; private set; } = false;
		internal string SteamLogin { get; private set; } = "null";
		internal string SteamPassword { get; private set; } = "null";
		internal string SteamNickname { get; private set; } = "null";
		internal string SteamApiKey { get; private set; } = "null";
		internal string SteamParentalPIN { get; private set; } = "0";
		internal ulong SteamMasterID { get; private set; } = 0;
		internal ulong SteamMasterClanID { get; private set; } = 0;
		internal bool CardDropsRestricted { get; private set; } = false;
		internal bool FarmOffline { get; private set; } = false;
		internal bool HandleOfflineMessages { get; private set; } = false;
		internal bool UseAsfAsMobileAuthenticator { get; private set; } = false;
		internal bool ShutdownOnFarmingFinished { get; private set; } = false;
		internal HashSet<uint> Blacklist { get; private set; } = new HashSet<uint>();
		internal bool Statistics { get; private set; } = true;

		private static bool IsValidCdKey(string key) {
			if (string.IsNullOrEmpty(key)) {
				return false;
			}

			if (key.Length != 17 && key.Length != 29) {
				return false;
			}

			for (byte i = 5; i < key.Length; i += 6) {
				if (key[i] != '-') {
					return false;
				}
			}

			return true;
		}

		internal static string GetAnyBotName() {
			foreach (string botName in Bots.Keys) {
				return botName;
			}

			return null;
		}

		internal static int GetRunningBotsCount() {
			return Bots.Count;
		}

		internal static async Task ShutdownAllBots() {
			List<Task> tasks = new List<Task>();
			foreach (Bot bot in Bots.Values) {
				tasks.Add(Task.Run(async () => await bot.Shutdown().ConfigureAwait(false)));
			}
			await Task.WhenAll(tasks).ConfigureAwait(false);
		}

		internal Bot(string botName) {
			if (Bots.ContainsKey(botName)) {
				return;
			}

			BotName = botName;

			ConfigFile = Path.Combine(Program.ConfigDirectory, BotName + ".xml");
			LoginKeyFile = Path.Combine(Program.ConfigDirectory, BotName + ".key");
			MobileAuthenticatorFile = Path.Combine(Program.ConfigDirectory, BotName + ".auth");
			SentryFile = Path.Combine(Program.ConfigDirectory, BotName + ".bin");

			if (!ReadConfig()) {
				return;
			}

			if (!Enabled) {
				return;
			}

			Bots.AddOrUpdate(BotName, this, (key, value) => this);

			// Initialize
			SteamClient = new SteamClient();

			ArchiHandler = new ArchiHandler();
			SteamClient.AddHandler(ArchiHandler);

			CallbackManager = new CallbackManager(SteamClient);
			CallbackManager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
			CallbackManager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);

			SteamFriends = SteamClient.GetHandler<SteamFriends>();
			CallbackManager.Subscribe<SteamFriends.ChatInviteCallback>(OnChatInvite);
			CallbackManager.Subscribe<SteamFriends.ChatMsgCallback>(OnChatMsg);
			CallbackManager.Subscribe<SteamFriends.FriendsListCallback>(OnFriendsList);
			CallbackManager.Subscribe<SteamFriends.FriendMsgCallback>(OnFriendMsg);
			CallbackManager.Subscribe<SteamFriends.FriendMsgHistoryCallback>(OnFriendMsgHistory);

			if (UseAsfAsMobileAuthenticator && File.Exists(MobileAuthenticatorFile)) {
				SteamGuardAccount = JsonConvert.DeserializeObject<SteamGuardAccount>(File.ReadAllText(MobileAuthenticatorFile));
			}

			SteamUser = SteamClient.GetHandler<SteamUser>();
			CallbackManager.Subscribe<SteamUser.AccountInfoCallback>(OnAccountInfo);
			CallbackManager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOff);
			CallbackManager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
			CallbackManager.Subscribe<SteamUser.LoginKeyCallback>(OnLoginKey);
			CallbackManager.Subscribe<SteamUser.UpdateMachineAuthCallback>(OnMachineAuth);

			CallbackManager.Subscribe<ArchiHandler.NotificationsCallback>(OnNotifications);
			CallbackManager.Subscribe<ArchiHandler.OfflineMessageCallback>(OnOfflineMessage);
			CallbackManager.Subscribe<ArchiHandler.PurchaseResponseCallback>(OnPurchaseResponse);

			ArchiWebHandler = new ArchiWebHandler(this, SteamApiKey);
			CardsFarmer = new CardsFarmer(this);
			Trading = new Trading(this);

			// Before attempting to connect, initialize our list of CMs
			SteamDirectory.Initialize().Wait();

			// Start
			var handleCallbacks = Task.Run(() => HandleCallbacks());
			var start = Task.Run(async () => await Start().ConfigureAwait(false));
		}

		internal async Task AcceptAllConfirmations() {
			if (SteamGuardAccount == null) {
				return;
			}

			await SteamGuardAccount.RefreshSessionAsync().ConfigureAwait(false);

			try {
				foreach (Confirmation confirmation in await SteamGuardAccount.FetchConfirmationsAsync().ConfigureAwait(false)) {
					if (SteamGuardAccount.AcceptConfirmation(confirmation)) {
						Logging.LogGenericInfo(BotName, "Accepting confirmation: Success!");
					} else {
						Logging.LogGenericWarning(BotName, "Accepting confirmation: Failed!");
					}
				}
			} catch (SteamGuardAccount.WGTokenInvalidException) {
				Logging.LogGenericWarning(BotName, "Accepting confirmation: Failed!");
				Logging.LogGenericWarning(BotName, "Confirmation could not be accepted because of invalid token exception");
				Logging.LogGenericWarning(BotName, "If issue persists, consider removing and readding ASF 2FA");
			}
		}

		private bool LinkMobileAuthenticator() {
			if (SteamGuardAccount != null) {
				return false;
			}

			Logging.LogGenericNotice(BotName, "Linking new ASF MobileAuthenticator...");
			UserLogin userLogin = new UserLogin(SteamLogin, SteamPassword);
			LoginResult loginResult;
			while ((loginResult = userLogin.DoLogin()) != LoginResult.LoginOkay) {
				switch (loginResult) {
					case LoginResult.NeedEmail:
						userLogin.EmailCode = Program.GetUserInput(BotName, Program.EUserInputType.SteamGuard);
						break;
					default:
						Logging.LogGenericError(BotName, "Unhandled situation: " + loginResult);
						return false;
				}
			}

			AuthenticatorLinker authenticatorLinker = new AuthenticatorLinker(userLogin.Session);

			AuthenticatorLinker.LinkResult linkResult;
			while ((linkResult = authenticatorLinker.AddAuthenticator()) != AuthenticatorLinker.LinkResult.AwaitingFinalization) {
				switch (linkResult) {
					case AuthenticatorLinker.LinkResult.MustProvidePhoneNumber:
						authenticatorLinker.PhoneNumber = Program.GetUserInput(BotName, Program.EUserInputType.PhoneNumber);
						break;
					default:
						Logging.LogGenericError(BotName, "Unhandled situation: " + linkResult);
						return false;
				}
			}

			SteamGuardAccount = authenticatorLinker.LinkedAccount;

			try {
				File.WriteAllText(MobileAuthenticatorFile, JsonConvert.SerializeObject(SteamGuardAccount));
			} catch (Exception e) {
				Logging.LogGenericException(BotName, e);
				return false;
			}

			AuthenticatorLinker.FinalizeResult finalizeResult = authenticatorLinker.FinalizeAddAuthenticator(Program.GetUserInput(BotName, Program.EUserInputType.SMS));
			if (finalizeResult != AuthenticatorLinker.FinalizeResult.Success) {
				Logging.LogGenericError(BotName, "Unhandled situation: " + finalizeResult);
				DelinkMobileAuthenticator();
				return false;
			}

			Logging.LogGenericInfo(BotName, "Successfully linked ASF as new mobile authenticator for this account!");
			Program.GetUserInput(BotName, Program.EUserInputType.RevocationCode, SteamGuardAccount.RevocationCode);
			return true;
		}

		private bool DelinkMobileAuthenticator() {
			if (SteamGuardAccount == null) {
				return false;
			}

			bool result = SteamGuardAccount.DeactivateAuthenticator();
			SteamGuardAccount = null;
			File.Delete(MobileAuthenticatorFile);

			return result;
		}

		private bool ReadConfig() {
			if (!File.Exists(ConfigFile)) {
				return false;
			}

			try {
				using (XmlReader reader = XmlReader.Create(ConfigFile)) {
					while (reader.Read()) {
						if (reader.NodeType != XmlNodeType.Element) {
							continue;
						}

						string key = reader.Name;
						if (string.IsNullOrEmpty(key)) {
							continue;
						}

						string value = reader.GetAttribute("value");
						if (string.IsNullOrEmpty(value)) {
							continue;
						}

						switch (key) {
							case "Enabled":
								Enabled = bool.Parse(value);
								break;
							case "SteamLogin":
								SteamLogin = value;
								break;
							case "SteamPassword":
								SteamPassword = value;
								break;
							case "SteamNickname":
								SteamNickname = value;
								break;
							case "SteamApiKey":
								SteamApiKey = value;
								break;
							case "SteamParentalPIN":
								SteamParentalPIN = value;
								break;
							case "SteamMasterID":
								SteamMasterID = ulong.Parse(value);
								break;
							case "SteamMasterClanID":
								SteamMasterClanID = ulong.Parse(value);
								break;
							case "UseAsfAsMobileAuthenticator":
								UseAsfAsMobileAuthenticator = bool.Parse(value);
								break;
							case "CardDropsRestricted":
								CardDropsRestricted = bool.Parse(value);
								break;
							case "FarmOffline":
								FarmOffline = bool.Parse(value);
								break;
							case "HandleOfflineMessages":
								HandleOfflineMessages = bool.Parse(value);
								break;
							case "ShutdownOnFarmingFinished":
								ShutdownOnFarmingFinished = bool.Parse(value);
								break;
							case "Blacklist":
								Blacklist.Clear();
								foreach (string appID in value.Split(',')) {
									Blacklist.Add(uint.Parse(appID));
								}
								break;
							case "Statistics":
								Statistics = bool.Parse(value);
								break;
							default:
								Logging.LogGenericWarning(BotName, "Unrecognized config value: " + key + "=" + value);
								break;
						}
					}
				}
			} catch (Exception e) {
				Logging.LogGenericException(BotName, e);
				Logging.LogGenericError(BotName, "Your config for this bot instance is invalid, it won't run!");
				return false;
			}

			return true;
		}

		internal async Task Restart() {
			await Stop().ConfigureAwait(false);
			await Start().ConfigureAwait(false);
		}

		internal async Task Start() {
			if (SteamClient.IsConnected) {
				return;
			}

			Logging.LogGenericInfo(BotName, "Starting...");

			// 2FA tokens are expiring soon, use limiter only when we don't have any pending
			if (TwoFactorAuth == null) {
				await Program.LimitSteamRequestsAsync().ConfigureAwait(false);
			}

			SteamClient.Connect();
		}

		internal async Task Stop() {
			if (!SteamClient.IsConnected) {
				return;
			}

			await Utilities.SleepAsync(0); // TODO: This is here only to make VS happy, for now

			Logging.LogGenericInfo(BotName, "Stopping...");

			SteamClient.Disconnect();
		}

		internal async Task Shutdown() {
			KeepRunning = false;
			await Stop().ConfigureAwait(false);
			Bot bot;
			Bots.TryRemove(BotName, out bot);
			Program.OnBotShutdown();
		}

		internal static async Task<bool> Shutdown(string botName) {
			if (string.IsNullOrEmpty(botName)) {
				return false;
			}

			Bot bot;
			if (!Bots.TryGetValue(botName, out bot)) {
				return false;
			}

			await bot.Shutdown().ConfigureAwait(false);
			return true;
		}

		internal async Task OnFarmingFinished() {
			if (ShutdownOnFarmingFinished) {
				await Shutdown().ConfigureAwait(false);
			}
		}

		private void HandleCallbacks() {
			TimeSpan timeSpan = TimeSpan.FromMilliseconds(CallbackSleep);
			while (KeepRunning) {
				CallbackManager.RunWaitCallbacks(timeSpan);
			}
		}

		private void SendMessage(ulong steamID, string message) {
			if (steamID == 0 || string.IsNullOrEmpty(message)) {
				return;
			}

			// TODO: I really need something better
			if (steamID < 110300000000000000) {
				SteamFriends.SendChatMessage(steamID, EChatEntryType.ChatMsg, message);
			} else {
				SteamFriends.SendChatRoomMessage(steamID, EChatEntryType.ChatMsg, message);
			}
		}

		internal static string ResponseStatus(string botName) {
			if (string.IsNullOrEmpty(botName)) {
				return null;
			}

			Bot bot;
			if (!Bots.TryGetValue(botName, out bot)) {
				return "Couldn't find any bot named " + botName + "!";
			}

			return bot.ResponseStatus();
		}

		internal string ResponseStatus() {
			if (CardsFarmer.CurrentGamesFarming.Count > 0) {
				return "Bot " + BotName + " is currently farming appIDs: " + string.Join(", ", CardsFarmer.CurrentGamesFarming) + " and has a total of " + CardsFarmer.GamesToFarm.Count + " games left to farm.";
			} else {
				return "Bot " + BotName + " is not farming.";
			}
		}

		internal static string ResponseStatusAll() {
			StringBuilder result = new StringBuilder(Environment.NewLine);
			foreach (Bot bot in Bots.Values) {
				result.Append(bot.ResponseStatus() + Environment.NewLine);
			}

			result.Append("Currently " + Bots.Count + " bots are running.");
			return result.ToString();
		}

		internal static string Response2FA(string botName) {
			if (string.IsNullOrEmpty(botName)) {
				return null;
			}

			Bot bot;
			if (!Bots.TryGetValue(botName, out bot)) {
				return "Couldn't find any bot named " + botName + "!";
			}

			if (bot.SteamGuardAccount == null) {
				return "That bot doesn't have ASF 2FA enabled!";
			}

			long timeLeft = 30 - TimeAligner.GetSteamTime() % 30;
			return "2FA Token: " + bot.SteamGuardAccount.GenerateSteamGuardCode() + " (expires in " + timeLeft + " seconds)";
		}

		internal static string Response2FAOff(string botName) {
			if (string.IsNullOrEmpty(botName)) {
				return null;
			}

			Bot bot;
			if (!Bots.TryGetValue(botName, out bot)) {
				return "Couldn't find any bot named " + botName + "!";
			}

			if (bot.SteamGuardAccount == null) {
				return "That bot doesn't have ASF 2FA enabled!";
			}

			if (bot.DelinkMobileAuthenticator()) {
				return "Done! Bot is no longer using ASF 2FA";
			} else {
				return "Something went wrong during delinking mobile authenticator!";
			}
		}

		internal static async Task<string> ResponseRedeem(string botName, string key) {
			if (string.IsNullOrEmpty(botName) || string.IsNullOrEmpty(key)) {
				return null;
			}

			Bot bot;
			if (!Bots.TryGetValue(botName, out bot)) {
				return "Couldn't find any bot named " + botName + "!";
			}

			ArchiHandler.PurchaseResponseCallback result;
			try {
				result = await bot.ArchiHandler.RedeemKey(key);
			} catch (Exception e) {
				Logging.LogGenericException(botName, e);
				return null;
			}

			if (result == null) {
				return null;
			}

			var purchaseResult = result.PurchaseResult;
			var items = result.Items;

			return botName+" status: "+purchaseResult + " | Items: " + string.Join("", items);
		}

		internal static string ResponseStart(string botName) {
			if (string.IsNullOrEmpty(botName)) {
				return null;
			}

			if (Bots.ContainsKey(botName)) {
				return "That bot instance is already running!";
			}

			new Bot(botName);
			if (Bots.ContainsKey(botName)) {
				return "Done!";
			} else {
				return "That bot instance failed to start, make sure that XML config exists and bot is active!";
			}
		}

		internal static async Task<string> ResponseStop(string botName) {
			if (string.IsNullOrEmpty(botName)) {
				return null;
			}

			Bot bot;
			if (!Bots.TryGetValue(botName, out bot)) {
				return "That bot instance is already inactive!";
			}

			if (await Shutdown(botName).ConfigureAwait(false)) {
				return "Done!";
			} else {
				return "That bot instance failed to shutdown!";
			}
		}

		private async Task<string> ResponseMultiActivate(string[] gamekeys) {
			string results="";
			int i = 0;
			foreach (var curbot in Bots) {
				results+=await ResponseRedeem(curbot.Key,gamekeys[i].Trim()).ConfigureAwait(false)+"\n";
				i++;
				if (gamekeys.Length <= i)
					break;
			}

			return results;

		}

		internal async Task<string> HandleMessage(string message) {
			if (string.IsNullOrEmpty(message)) {
				return null;
			}
			if (message.Contains("\n")) {
				string[] args = message.Split('\n');
				if (args[0].Contains("-")) {
					return await ResponseMultiActivate(args);
				}
			}      

			if (IsValidCdKey(message)) {
				return await ResponseRedeem(BotName, message).ConfigureAwait(false);
			}

			if (!message.Contains(" ")) {
				switch (message) {
					case "!2fa":
						return Response2FA(BotName);
					case "!2faoff":
						return Response2FAOff(BotName);
					case "!exit":
						await ShutdownAllBots().ConfigureAwait(false);
						return "Done";
					case "!restart":
						await Program.Restart().ConfigureAwait(false);
						return "Done";
					case "!status":
						return ResponseStatus();
					case "!statusall":
						return ResponseStatusAll();
					case "!stop":
						return await ResponseStop(BotName).ConfigureAwait(false);
					default:
						return "Unrecognized command: " + message;
				}
			} else {
				string[] args = message.Split(' ');
				switch (args[0]) {
					case "!2fa":
						return Response2FA(args[1]);
					case "!2faoff":
						return Response2FAOff(args[1]);
					case "!redeem":
						string botName;
						string key;
						if (args.Length > 2) {
							botName = args[1];
							key = args[2];
						} else {
							botName = BotName;
							key = args[1];
						}
						return await ResponseRedeem(botName, key).ConfigureAwait(false);
					case "!start":
						return ResponseStart(args[1]);
					case "!stop":
						return await ResponseStop(args[1]).ConfigureAwait(false);
					case "!status":
						return ResponseStatus(args[1]);
					default:
						return "Unrecognized command: " + args[0];
				}
			}
		}

		private async Task HandleMessage(ulong steamID, string message) {
			if (steamID == 0 || string.IsNullOrEmpty(message)) {
				return;
			}

			SendMessage(steamID, await HandleMessage(message).ConfigureAwait(false));
		}

		private void OnConnected(SteamClient.ConnectedCallback callback) {
			if (callback == null) {
				return;
			}

			if (callback.Result != EResult.OK) {
				Logging.LogGenericError(BotName, "Unable to connect to Steam: " + callback.Result);
				return;
			}

			Logging.LogGenericInfo(BotName, "Connected to Steam!");

			if (File.Exists(LoginKeyFile)) {
				LoginKey = File.ReadAllText(LoginKeyFile);
			}

			byte[] sentryHash = null;
			if (File.Exists(SentryFile)) {
				byte[] sentryFileContent = File.ReadAllBytes(SentryFile);
				sentryHash = CryptoHelper.SHAHash(sentryFileContent);
			}

			if (SteamLogin.Equals("null")) {
				SteamLogin = Program.GetUserInput(BotName, Program.EUserInputType.Login);
			}

			if (SteamPassword.Equals("null") && string.IsNullOrEmpty(LoginKey)) {
				SteamPassword = Program.GetUserInput(BotName, Program.EUserInputType.Password);
			}

			SteamUser.LogOn(new SteamUser.LogOnDetails {
				Username = SteamLogin,
				Password = SteamPassword,
				AuthCode = AuthCode,
				LoginID = LoginID,
				LoginKey = LoginKey,
				TwoFactorCode = TwoFactorAuth,
				SentryFileHash = sentryHash,
				ShouldRememberPassword = true
			});
		}

		private async void OnDisconnected(SteamClient.DisconnectedCallback callback) {
			if (callback == null) {
				return;
			}

			Logging.LogGenericInfo(BotName, "Disconnected from Steam!");
			await CardsFarmer.StopFarming().ConfigureAwait(false);

			if (!KeepRunning) {
				return;
			}

			// If we initiated disconnect, do not attempt to reconnect
			if (callback.UserInitiated) {
				return;
			}

			if (InvalidPassword) {
				InvalidPassword = false;
				if (!string.IsNullOrEmpty(LoginKey)) { // InvalidPassword means usually that login key has expired, if we used it
					LoginKey = null;
					File.Delete(LoginKeyFile);
					Logging.LogGenericInfo(BotName, "Removed expired login key");
				} else { // If we didn't use login key, InvalidPassword usually means we got captcha or other network-based throttling
					Logging.LogGenericInfo(BotName, "Will retry after 25 minutes...");
					await Utilities.SleepAsync(25 * 60 * 1000).ConfigureAwait(false); // Captcha disappears after around 20 minutes, so we make it 25
				}
			} else if (LoggedInElsewhere) {
				LoggedInElsewhere = false;
				Logging.LogGenericWarning(BotName, "Account is being used elsewhere, will try reconnecting in 30 minutes...");
				await Utilities.SleepAsync(30 * 60 * 1000).ConfigureAwait(false);
			}

			Logging.LogGenericInfo(BotName, "Reconnecting...");

			// 2FA tokens are expiring soon, use limiter only when we don't have any pending
			if (TwoFactorAuth == null) {
				await Program.LimitSteamRequestsAsync().ConfigureAwait(false);
			}

			SteamClient.Connect();
		}

		private void OnChatInvite(SteamFriends.ChatInviteCallback callback) {
			if (callback == null) {
				return;
			}

			ulong steamID = callback.PatronID;
			if (steamID != SteamMasterID) {
				return;
			}

			SteamFriends.JoinChat(callback.ChatRoomID);
		}

		private async void OnChatMsg(SteamFriends.ChatMsgCallback callback) {
			if (callback == null) {
				return;
			}

			if (callback.ChatMsgType != EChatEntryType.ChatMsg) {
				return;
			}

			ulong steamID = callback.ChatterID;
			if (steamID != SteamMasterID) {
				return;
			}

			await HandleMessage(callback.ChatRoomID, callback.Message).ConfigureAwait(false);
		}

		private void OnFriendsList(SteamFriends.FriendsListCallback callback) {
			if (callback == null) {
				return;
			}

			foreach (var friend in callback.FriendList) {
				if (friend.Relationship != EFriendRelationship.RequestRecipient) {
					continue;
				}

				SteamID steamID = friend.SteamID;
				switch (steamID.AccountType) {
					case EAccountType.Clan:
						// TODO: Accept clan invites from master?
						break;
					default:
						if (steamID == SteamMasterID) {
							SteamFriends.AddFriend(steamID);
						}
						break;
				}
			}
		}

		private async void OnFriendMsg(SteamFriends.FriendMsgCallback callback) {
			if (callback == null) {
				return;
			}

			if (callback.EntryType != EChatEntryType.ChatMsg) {
				return;
			}

			ulong steamID = callback.Sender;
			if (steamID != SteamMasterID) {
				return;
			}

			await HandleMessage(steamID, callback.Message).ConfigureAwait(false);
		}

		private async void OnFriendMsgHistory(SteamFriends.FriendMsgHistoryCallback callback) {
			if (callback == null) {
				return;
			}

			if (callback.Result != EResult.OK) {
				return;
			}

			ulong steamID = callback.SteamID;

			if (steamID != SteamMasterID) {
				return;
			}

			var messages = callback.Messages;
			if (messages.Count == 0) {
				return;
			}

			// Get last message
			var lastMessage = messages[messages.Count - 1];

			// If message is read already, return
			if (!lastMessage.Unread) {
				return;
			}

			// If message is too old, return
			if (DateTime.UtcNow.Subtract(lastMessage.Timestamp).TotalMinutes > 1) {
				return;
			}

			// Handle the message
			await HandleMessage(steamID, lastMessage.Message).ConfigureAwait(false);
		}

		private void OnAccountInfo(SteamUser.AccountInfoCallback callback) {
			if (callback == null) {
				return;
			}

			if (!FarmOffline) {
				SteamFriends.SetPersonaState(EPersonaState.Online);
			}
		}

		private void OnLoggedOff(SteamUser.LoggedOffCallback callback) {
			if (callback == null) {
				return;
			}

			Logging.LogGenericInfo(BotName, "Logged off of Steam: " + callback.Result);

			switch (callback.Result) {
				case EResult.AlreadyLoggedInElsewhere:
				case EResult.LoggedInElsewhere:
				case EResult.LogonSessionReplaced:
					LoggedInElsewhere = true;
					break;
			}
		}

		private async void OnLoggedOn(SteamUser.LoggedOnCallback callback) {
			if (callback == null) {
				return;
			}

			EResult result = callback.Result;
			switch (result) {
				case EResult.AccountLogonDenied:
					AuthCode = Program.GetUserInput(SteamLogin, Program.EUserInputType.SteamGuard);
					break;
				case EResult.AccountLoginDeniedNeedTwoFactor:
					if (SteamGuardAccount == null) {
						TwoFactorAuth = Program.GetUserInput(SteamLogin, Program.EUserInputType.TwoFactorAuthentication);
					} else {
						TwoFactorAuth = SteamGuardAccount.GenerateSteamGuardCode();
					}
					break;
				case EResult.InvalidPassword:
					InvalidPassword = true;
					Logging.LogGenericWarning(BotName, "Unable to login to Steam: " + result);
					break;
				case EResult.OK:
					Logging.LogGenericInfo(BotName, "Successfully logged on!");

					if (UseAsfAsMobileAuthenticator && TwoFactorAuth == null && SteamGuardAccount == null) {
						LinkMobileAuthenticator();
					}

					// Reset one-time-only access tokens
					AuthCode = null;
					TwoFactorAuth = null;

					if (!SteamNickname.Equals("null")) {
						await SteamFriends.SetPersonaName(SteamNickname);
					}

					if (SteamParentalPIN.Equals("null")) {
						SteamParentalPIN = Program.GetUserInput(BotName, Program.EUserInputType.SteamParentalPIN);
					}

					if (!await ArchiWebHandler.Init(SteamClient, callback.WebAPIUserNonce, callback.VanityURL, SteamParentalPIN).ConfigureAwait(false)) {
						await Restart().ConfigureAwait(false);
						return;
					}

					if (SteamMasterClanID != 0) {
						await ArchiWebHandler.JoinClan(SteamMasterClanID).ConfigureAwait(false);
						SteamFriends.JoinChat(SteamMasterClanID);
					}

					if (Statistics) {
						await ArchiWebHandler.JoinClan(ArchiSCFarmGroup).ConfigureAwait(false);
						SteamFriends.JoinChat(ArchiSCFarmGroup);
					}

					Trading.CheckTrades();

					await CardsFarmer.StartFarming().ConfigureAwait(false);
					break;
				case EResult.NoConnection:
				case EResult.ServiceUnavailable:
				case EResult.Timeout:
				case EResult.TryAnotherCM:
					Logging.LogGenericWarning(BotName, "Unable to login to Steam: " + result);
					break;
				default: // Unexpected result, shutdown immediately
					Logging.LogGenericWarning(BotName, "Unable to login to Steam: " + result);
					await Shutdown().ConfigureAwait(false);
					break;
			}
		}

		private void OnLoginKey(SteamUser.LoginKeyCallback callback) {
			if (callback == null) {
				return;
			}

			File.WriteAllText(LoginKeyFile, callback.LoginKey);
			SteamUser.AcceptNewLoginKey(callback);
		}

		private void OnMachineAuth(SteamUser.UpdateMachineAuthCallback callback) {
			if (callback == null) {
				return;
			}

			int fileSize;
			byte[] sentryHash;

			using (FileStream fileStream = File.Open(SentryFile, FileMode.OpenOrCreate, FileAccess.ReadWrite)) {
				fileStream.Seek(callback.Offset, SeekOrigin.Begin);
				fileStream.Write(callback.Data, 0, callback.BytesToWrite);
				fileSize = (int) fileStream.Length;

				fileStream.Seek(0, SeekOrigin.Begin);
				using (SHA1CryptoServiceProvider sha = new SHA1CryptoServiceProvider()) {
					sentryHash = sha.ComputeHash(fileStream);
				}
			}


			// Inform the steam servers that we're accepting this sentry file
			SteamUser.SendMachineAuthResponse(new SteamUser.MachineAuthDetails {
				JobID = callback.JobID,
				FileName = callback.FileName,
				BytesWritten = callback.BytesToWrite,
				FileSize = fileSize,
				Offset = callback.Offset,
				Result = EResult.OK,
				LastError = 0,
				OneTimePassword = callback.OneTimePassword,
				SentryFileHash = sentryHash,
			});
		}

		private void OnNotifications(ArchiHandler.NotificationsCallback callback) {
			if (callback == null) {
				return;
			}

			bool checkTrades = false;
			foreach (var notification in callback.Notifications) {
				switch (notification.NotificationType) {
					case ArchiHandler.NotificationsCallback.Notification.ENotificationType.Trading:
						checkTrades = true;
						break;
				}
			}

			if (checkTrades) {
				Trading.CheckTrades();
			}
		}

		private void OnOfflineMessage(ArchiHandler.OfflineMessageCallback callback) {
			if (callback == null) {
				return;
			}

			if (!HandleOfflineMessages) {
				return;
			}

			SteamFriends.RequestOfflineMessages();
		}

		private async void OnPurchaseResponse(ArchiHandler.PurchaseResponseCallback callback) {
			if (callback == null) {
				return;
			}

			var purchaseResult = callback.PurchaseResult;
			if (purchaseResult == ArchiHandler.PurchaseResponseCallback.EPurchaseResult.OK) {
				// We will restart CF module to recalculate current status and decide about new optimal approach
				await CardsFarmer.RestartFarming().ConfigureAwait(false);
			}
		}
	}
}
