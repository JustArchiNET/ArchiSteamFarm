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
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Xml;

namespace ArchiSteamFarm {
	internal sealed class Bot {
		private const ushort CallbackSleep = 500; // In miliseconds

		private static readonly ConcurrentDictionary<string, Bot> Bots = new ConcurrentDictionary<string, Bot>();

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

		private bool LoggedInElsewhere = false;
		private bool IsRunning = false;
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
		internal HashSet<uint> Blacklist { get; private set; } = new HashSet<uint> { 303700, 335590, 368020, 425280 };
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

			ConfigFile = Path.Combine(Program.ConfigDirectoryPath, BotName + ".xml");
			LoginKeyFile = Path.Combine(Program.ConfigDirectoryPath, BotName + ".key");
			MobileAuthenticatorFile = Path.Combine(Program.ConfigDirectoryPath, BotName + ".auth");
			SentryFile = Path.Combine(Program.ConfigDirectoryPath, BotName + ".bin");

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
			CallbackManager.Subscribe<SteamFriends.FriendsListCallback>(OnFriendsList);
			CallbackManager.Subscribe<SteamFriends.FriendMsgCallback>(OnFriendMsg);

			if (UseAsfAsMobileAuthenticator && File.Exists(MobileAuthenticatorFile)) {
				SteamGuardAccount = JsonConvert.DeserializeObject<SteamGuardAccount>(File.ReadAllText(MobileAuthenticatorFile));
			}

			SteamUser = SteamClient.GetHandler<SteamUser>();
			CallbackManager.Subscribe<SteamUser.AccountInfoCallback>(OnAccountInfo);
			CallbackManager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOff);
			CallbackManager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
			CallbackManager.Subscribe<SteamUser.LoginKeyCallback>(OnLoginKey);
			CallbackManager.Subscribe<SteamUser.UpdateMachineAuthCallback>(OnMachineAuth);

			CallbackManager.Subscribe<ArchiHandler.NotificationCallback>(OnNotification);
			CallbackManager.Subscribe<ArchiHandler.OfflineMessageCallback>(OnOfflineMessage);
			CallbackManager.Subscribe<ArchiHandler.PurchaseResponseCallback>(OnPurchaseResponse);

			ArchiWebHandler = new ArchiWebHandler(this, SteamApiKey);
			CardsFarmer = new CardsFarmer(this);
			Trading = new Trading(this);

			// Start
			var fireAndForget = Task.Run(async () => await Start().ConfigureAwait(false));
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

		internal async Task Start() {
			if (IsRunning) {
				return;
			}

			IsRunning = true;

			Logging.LogGenericInfo(BotName, "Starting...");

			// 2FA tokens are expiring soon, use limiter only when we don't have any pending
			if (TwoFactorAuth == null) {
				await Program.LimitSteamRequestsAsync().ConfigureAwait(false);
			}

			SteamClient.Connect();

			var fireAndForget = Task.Run(() => HandleCallbacks());
		}

		internal async Task Stop() {
			if (!IsRunning) {
				return;
			}

			await CardsFarmer.StopFarming().ConfigureAwait(false);
			IsRunning = false;
			SteamClient.Disconnect();
		}

		internal async Task<bool> Shutdown(string botName = null) {
			Bot bot;

			if (string.IsNullOrEmpty(botName)) {
				bot = this;
			} else {
				if (!Bots.TryGetValue(botName, out bot)) {
					return false;
				}
			}

			await bot.Stop().ConfigureAwait(false);
			Bots.TryRemove(bot.BotName, out bot);

			Program.OnBotShutdown();
			return true;
		}

		internal async Task OnFarmingFinished() {
			if (ShutdownOnFarmingFinished) {
				await Shutdown().ConfigureAwait(false);
			}
		}

		private void HandleCallbacks() {
			TimeSpan timeSpan = TimeSpan.FromMilliseconds(CallbackSleep);
			while (IsRunning) {
				CallbackManager.RunWaitCallbacks(timeSpan);
			}
		}

		private void SendMessageToUser(ulong steamID, string message) {
			if (steamID == 0 || string.IsNullOrEmpty(message)) {
				return;
			}

			SteamFriends.SendChatMessage(steamID, EChatEntryType.ChatMsg, message);
		}


		private void ResponseStatus(ulong steamID, string botName = null) {
			if (steamID == 0) {
				return;
			}

			Bot bot;

			if (string.IsNullOrEmpty(botName)) {
				bot = this;
			} else {
				if (!Bots.TryGetValue(botName, out bot)) {
					SendMessageToUser(steamID, "Couldn't find any bot named " + botName + "!");
					return;
				}
			}

			if (bot.CardsFarmer.CurrentGamesFarming.Count > 0) {
				SendMessageToUser(steamID, "Bot " + bot.BotName + " is currently farming appIDs: " + string.Join(", ", bot.CardsFarmer.CurrentGamesFarming) + " and has a total of " + bot.CardsFarmer.GamesToFarm.Count + " games left to farm");
			}
			SendMessageToUser(steamID, "Currently " + Bots.Count + " bots are running");
		}

		private void Response2FA(ulong steamID, string botName = null) {
			if (steamID == 0) {
				return;
			}

			Bot bot;

			if (string.IsNullOrEmpty(botName)) {
				bot = this;
			} else {
				if (!Bots.TryGetValue(botName, out bot)) {
					SendMessageToUser(steamID, "Couldn't find any bot named " + botName + "!");
					return;
				}
			}

			if (bot.SteamGuardAccount == null) {
				SendMessageToUser(steamID, "That bot doesn't have ASF 2FA enabled!");
				return;
			}

			long timeLeft = 30 - TimeAligner.GetSteamTime() % 30;
			SendMessageToUser(steamID, "2FA Token: " + bot.SteamGuardAccount.GenerateSteamGuardCode() + " (expires in " + timeLeft + " seconds)");
		}

		private void Response2FAOff(ulong steamID, string botName = null) {
			if (steamID == 0) {
				return;
			}

			Bot bot;

			if (string.IsNullOrEmpty(botName)) {
				bot = this;
			} else {
				if (!Bots.TryGetValue(botName, out bot)) {
					SendMessageToUser(steamID, "Couldn't find any bot named " + botName + "!");
					return;
				}
			}

			if (bot.SteamGuardAccount == null) {
				SendMessageToUser(steamID, "That bot doesn't have ASF 2FA enabled!");
				return;
			}

			if (bot.DelinkMobileAuthenticator()) {
				SendMessageToUser(steamID, "Done! Bot is no longer using ASF 2FA");
			} else {
				SendMessageToUser(steamID, "Something went wrong!");
			}
		}

		private void ResponseStart(ulong steamID, string botName) {
			if (steamID == 0 || string.IsNullOrEmpty(botName)) {
				return;
			}

			if (Bots.ContainsKey(botName)) {
				SendMessageToUser(steamID, "That bot instance is already running!");
				return;
			}

			new Bot(botName);
			if (Bots.ContainsKey(botName)) {
				SendMessageToUser(steamID, "Done!");
			} else {
				SendMessageToUser(steamID, "That bot instance failed to start, make sure that XML config exists and bot is active!");
			}
		}

		private async Task ResponseStop(ulong steamID, string botName) {
			if (steamID == 0 || string.IsNullOrEmpty(botName)) {
				return;
			}

			if (!Bots.ContainsKey(botName)) {
				SendMessageToUser(steamID, "That bot instance is already inactive!");
				return;
			}

			if (await Shutdown(botName).ConfigureAwait(false)) {
				SendMessageToUser(steamID, "Done!");
			} else {
				SendMessageToUser(steamID, "That bot instance failed to shutdown!");
			}
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

			// TODO: We should use SteamUser.LogOn with proper LoginID once https://github.com/SteamRE/SteamKit/pull/217 gets merged
			ArchiHandler.HackedLogOn(Program.UniqueID, new SteamUser.LogOnDetails {
				Username = SteamLogin,
				Password = SteamPassword,
				AuthCode = AuthCode,
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

			if (!IsRunning) {
				return;
			}

			await CardsFarmer.StopFarming().ConfigureAwait(false);

			Logging.LogGenericWarning(BotName, "Disconnected from Steam, reconnecting...");

			// 2FA tokens are expiring soon, use limiter only when we don't have any pending
			if (TwoFactorAuth == null) {
				await Program.LimitSteamRequestsAsync().ConfigureAwait(false);
			}

			if (LoggedInElsewhere) {
				LoggedInElsewhere = false;
				Logging.LogGenericWarning(BotName, "Account is being used elsewhere, will try reconnecting in 5 minutes...");
				await Utilities.SleepAsync(5 * 60 * 1000).ConfigureAwait(false);
			}

			SteamClient.Connect();
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

			string message = callback.Message;
			if (string.IsNullOrEmpty(message)) {
				return;
			}

			if (IsValidCdKey(message)) {
				ArchiHandler.RedeemKey(message);
				return;
			}

			if (!message.StartsWith("!")) {
				return;
			}

			if (!message.Contains(" ")) {
				switch (message) {
					case "!2fa":
						Response2FA(steamID);
						break;
					case "!2faoff":
						Response2FAOff(steamID);
						break;
					case "!exit":
						await ShutdownAllBots().ConfigureAwait(false);
						break;
					case "!farm":
						SendMessageToUser(steamID, "Please wait...");
						await CardsFarmer.StartFarming().ConfigureAwait(false);
						SendMessageToUser(steamID, "Done!");
						break;
					case "!restart":
						await Program.Restart().ConfigureAwait(false);
						break;
					case "!status":
						ResponseStatus(steamID);
						break;
					case "!stop":
						await Shutdown().ConfigureAwait(false);
						break;
				}
			} else {
				string[] args = message.Split(' ');
				switch (args[0]) {
					case "!2fa":
						Response2FA(steamID, args[1]);
						break;
					case "!2faoff":
						Response2FAOff(steamID, args[1]);
						break;
					case "!redeem":
						ArchiHandler.RedeemKey(args[1]);
						break;
					case "!start":
						ResponseStart(steamID, args[1]);
						break;
					case "!stop":
						await ResponseStop(steamID, args[1]).ConfigureAwait(false);
						break;
					case "!status":
						ResponseStatus(steamID, args[1]);
						break;
				}
			}
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
					Logging.LogGenericWarning(BotName, "Unable to login to Steam: " + result);
					await Stop().ConfigureAwait(false);

					// InvalidPassword means usually that login key has expired, if we used it
					if (!string.IsNullOrEmpty(LoginKey)) {
						LoginKey = null;
						File.Delete(LoginKeyFile);
						Logging.LogGenericInfo(BotName, "Removed expired login key, reconnecting...");
					} else { // If we didn't use login key, InvalidPassword usually means we got captcha or other network-based throttling
						Logging.LogGenericInfo(BotName, "Will retry after 25 minutes...");
						await Utilities.SleepAsync(25 * 60 * 1000).ConfigureAwait(false); // Captcha disappears after around 20 minutes, so we make it 25
					}

					// After all of that, try again
					await Start().ConfigureAwait(false);
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
						SteamFriends.SetPersonaName(SteamNickname);
					}

					if (SteamParentalPIN.Equals("null")) {
						SteamParentalPIN = Program.GetUserInput(BotName, Program.EUserInputType.SteamParentalPIN);
					}

					await ArchiWebHandler.Init(SteamClient, callback.WebAPIUserNonce, callback.VanityURL, SteamParentalPIN).ConfigureAwait(false);

					if (SteamMasterClanID != 0) {
						await ArchiWebHandler.JoinClan(SteamMasterClanID).ConfigureAwait(false);
						SteamFriends.JoinChat(SteamMasterClanID);
					}

					if (Statistics) {
						await ArchiWebHandler.JoinClan(Program.ArchiSCFarmGroup).ConfigureAwait(false);
						SteamFriends.JoinChat(Program.ArchiSCFarmGroup);
					}

					Trading.CheckTrades();

					await CardsFarmer.StartFarming().ConfigureAwait(false);
					break;
				case EResult.ServiceUnavailable:
				case EResult.Timeout:
				case EResult.TryAnotherCM:
					Logging.LogGenericWarning(BotName, "Unable to login to Steam: " + result + ", retrying...");
					await Stop().ConfigureAwait(false);
					await Start().ConfigureAwait(false);
					break;
				default:
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

			Logging.LogGenericInfo(BotName, "Updating sentryfile...");

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

			Logging.LogGenericInfo(BotName, "Sentryfile updated successfully!");
		}

		private void OnNotification(ArchiHandler.NotificationCallback callback) {
			if (callback == null) {
				return;
			}

			switch (callback.NotificationType) {
				case ArchiHandler.NotificationCallback.ENotificationType.Trading:
					Trading.CheckTrades();
					break;
			}
		}

		private void OnOfflineMessage(ArchiHandler.OfflineMessageCallback callback) {
			if (callback == null) {
				return;
			}

			if (!HandleOfflineMessages) {
				return;
			}

			// TODO: Enable this after SK2 1.7+ gets released
			//SteamFriends.RequestOfflineMessages();
		}

		private async void OnPurchaseResponse(ArchiHandler.PurchaseResponseCallback callback) {
			if (callback == null) {
				return;
			}

			var purchaseResult = callback.PurchaseResult;
			var items = callback.Items;
			SendMessageToUser(SteamMasterID, "Status: " + purchaseResult + " | Items: " + string.Join("", items));

			if (purchaseResult == ArchiHandler.PurchaseResponseCallback.EPurchaseResult.OK) {
				await CardsFarmer.StartFarming().ConfigureAwait(false);
			}
		}
	}
}
