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
using SteamAuth;
using SteamKit2;
using SteamKit2.Internal;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Text.RegularExpressions;
using ArchiSteamFarm.JSON;

namespace ArchiSteamFarm {
	internal sealed class Bot {
		private const ulong ArchiSCFarmGroup = 103582791440160998;
		private const ushort CallbackSleep = 500; // In miliseconds
		private const ushort MaxSteamMessageLength = 2048;

		internal static readonly Dictionary<string, Bot> Bots = new Dictionary<string, Bot>();

		private static readonly uint LoginID = MsgClientLogon.ObfuscationMask; // This must be the same for all ASF bots and all ASF processes
		private static readonly SemaphoreSlim LoginSemaphore = new SemaphoreSlim(1);

		internal readonly string BotName;
		internal readonly ArchiHandler ArchiHandler;
		internal readonly ArchiWebHandler ArchiWebHandler;
		internal readonly BotConfig BotConfig;
		internal readonly SteamClient SteamClient;

		private readonly string SentryFile;
		private readonly BotDatabase BotDatabase;
		private readonly CallbackManager CallbackManager;
		private readonly CardsFarmer CardsFarmer;
		private readonly SteamApps SteamApps;
		private readonly SteamFriends SteamFriends;
		private readonly SteamUser SteamUser;
		private readonly Timer AcceptConfirmationsTimer;
		private readonly Timer SendItemsTimer;
		private readonly Trading Trading;

		internal bool KeepRunning { get; private set; }
		internal bool PlayingBlocked { get; private set; }

		private bool FirstTradeSent, InvalidPassword, SkipFirstShutdown;
		private string AuthCode, TwoFactorCode;

		internal static async Task RefreshCMs(uint cellID) {
			bool initialized = false;
			for (byte i = 0; (i < WebBrowser.MaxRetries) && !initialized; i++) {
				try {
					Logging.LogGenericInfo("Refreshing list of CMs...");
					await SteamDirectory.Initialize(cellID).ConfigureAwait(false);
					initialized = true;
				} catch (Exception e) {
					Logging.LogGenericException(e);
					await Utilities.SleepAsync(1000).ConfigureAwait(false);
				}
			}

			if (initialized) {
				Logging.LogGenericInfo("Success!");
			} else {
				Logging.LogGenericWarning("Failed to initialize list of CMs after " + WebBrowser.MaxRetries + " tries, ASF will use built-in SK2 list, it may take a while to connect");
			}
		}

		private static bool IsOwner(ulong steamID) {
			if (steamID != 0) {
				return steamID == Program.GlobalConfig.SteamOwnerID;
			}

			Logging.LogNullError(nameof(steamID));
			return false;
		}

		private static bool IsValidCdKey(string key) {
			if (!string.IsNullOrEmpty(key)) {
				return Regex.IsMatch(key, @"[0-9A-Z]{4,5}-[0-9A-Z]{4,5}-[0-9A-Z]{4,5}-?(?:(?:[0-9A-Z]{4,5}-?)?(?:[0-9A-Z]{4,5}))?");
			}

			Logging.LogNullError(nameof(key));
			return false;
		}

		private static async Task LimitLoginRequestsAsync() {
			await LoginSemaphore.WaitAsync().ConfigureAwait(false);
			Task.Run(async () => {
				await Utilities.SleepAsync(Program.GlobalConfig.LoginLimiterDelay * 1000).ConfigureAwait(false);
				LoginSemaphore.Release();
			}).Forget();
		}

		internal Bot(string botName) {
			if (string.IsNullOrEmpty(botName)) {
				throw new ArgumentNullException(nameof(botName));
			}

			BotName = botName;

			string botPath = Path.Combine(Program.ConfigDirectory, botName);

			BotConfig = BotConfig.Load(botPath + ".json");
			if (BotConfig == null) {
				Logging.LogGenericError("Your bot config is invalid, refusing to start this bot instance!", botName);
				return;
			}

			if (!BotConfig.Enabled) {
				Logging.LogGenericInfo("Not starting this instance because it's disabled in config file", botName);
				return;
			}

			lock (Bots) {
				if (Bots.ContainsKey(botName)) {
					return;
				}

				Bots[botName] = this;
			}

			SentryFile = botPath + ".bin";

			BotDatabase = BotDatabase.Load(botPath + ".db");
			if (BotDatabase == null) {
				Logging.LogGenericError("Bot database could not be loaded, refusing to start this bot instance!", botName);
				return;
			}

			if (BotDatabase.SteamGuardAccount == null) {
				// Support and convert SDA files
				string maFilePath = botPath + ".maFile";
				if (File.Exists(maFilePath)) {
					ImportAuthenticator(maFilePath);
				}
			}

			// Initialize
			SteamClient = new SteamClient(Program.GlobalConfig.SteamProtocol);

			if (Program.GlobalConfig.Debug && !Debugging.NetHookAlreadyInitialized && Directory.Exists(Program.DebugDirectory)) {
				try {
					Debugging.NetHookAlreadyInitialized = true;
					SteamClient.DebugNetworkListener = new NetHookNetworkListener(Program.DebugDirectory);
				} catch (Exception e) {
					Logging.LogGenericException(e, botName);
				}
			}

			ArchiHandler = new ArchiHandler(this);
			SteamClient.AddHandler(ArchiHandler);

			CallbackManager = new CallbackManager(SteamClient);
			CallbackManager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
			CallbackManager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);

			SteamApps = SteamClient.GetHandler<SteamApps>();
			CallbackManager.Subscribe<SteamApps.FreeLicenseCallback>(OnFreeLicense);
			CallbackManager.Subscribe<SteamApps.GuestPassListCallback>(OnGuestPassList);

			SteamFriends = SteamClient.GetHandler<SteamFriends>();
			CallbackManager.Subscribe<SteamFriends.ChatInviteCallback>(OnChatInvite);
			CallbackManager.Subscribe<SteamFriends.ChatMsgCallback>(OnChatMsg);
			CallbackManager.Subscribe<SteamFriends.FriendsListCallback>(OnFriendsList);
			CallbackManager.Subscribe<SteamFriends.FriendMsgCallback>(OnFriendMsg);
			CallbackManager.Subscribe<SteamFriends.FriendMsgHistoryCallback>(OnFriendMsgHistory);

			SteamUser = SteamClient.GetHandler<SteamUser>();
			CallbackManager.Subscribe<SteamUser.AccountInfoCallback>(OnAccountInfo);
			CallbackManager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOff);
			CallbackManager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
			CallbackManager.Subscribe<SteamUser.LoginKeyCallback>(OnLoginKey);
			CallbackManager.Subscribe<SteamUser.UpdateMachineAuthCallback>(OnMachineAuth);
			CallbackManager.Subscribe<SteamUser.WebAPIUserNonceCallback>(OnWebAPIUserNonce);

			CallbackManager.Subscribe<ArchiHandler.NotificationsCallback>(OnNotifications);
			CallbackManager.Subscribe<ArchiHandler.OfflineMessageCallback>(OnOfflineMessage);
			CallbackManager.Subscribe<ArchiHandler.PlayingSessionStateCallback>(OnPlayingSessionState);
			CallbackManager.Subscribe<ArchiHandler.PurchaseResponseCallback>(OnPurchaseResponse);

			ArchiWebHandler = new ArchiWebHandler(this);
			CardsFarmer = new CardsFarmer(this);
			Trading = new Trading(this);

			if ((AcceptConfirmationsTimer == null) && (BotConfig.AcceptConfirmationsPeriod > 0)) {
				AcceptConfirmationsTimer = new Timer(
					async e => await AcceptConfirmations(true).ConfigureAwait(false),
					null,
					TimeSpan.FromMinutes(BotConfig.AcceptConfirmationsPeriod), // Delay
					TimeSpan.FromMinutes(BotConfig.AcceptConfirmationsPeriod) // Period
				);
			}

			if ((SendItemsTimer == null) && (BotConfig.SendTradePeriod > 0)) {
				SendItemsTimer = new Timer(
					async e => await ResponseSendTrade(BotConfig.SteamMasterID).ConfigureAwait(false),
					null,
					TimeSpan.FromHours(BotConfig.SendTradePeriod), // Delay
					TimeSpan.FromHours(BotConfig.SendTradePeriod) // Period
				);
			}

			if (!BotConfig.StartOnLaunch) {
				return;
			}

			// Start
			Start().Forget();
		}

		internal async Task<bool> AcceptConfirmations(bool confirm, Confirmation.ConfirmationType allowedConfirmationType = Confirmation.ConfirmationType.Unknown) {
			if (BotDatabase.SteamGuardAccount == null) {
				return true;
			}

			bool result = false;
			for (byte i = 0; (i < WebBrowser.MaxRetries) && !result; i++) {
				result = true;

				try {
					if (!await BotDatabase.SteamGuardAccount.RefreshSessionAsync().ConfigureAwait(false)) {
						result = false;
						continue;
					}

					Confirmation[] confirmations = await BotDatabase.SteamGuardAccount.FetchConfirmationsAsync().ConfigureAwait(false);
					if (confirmations == null) {
						return true;
					}

					foreach (Confirmation confirmation in confirmations.Where(confirmation => (allowedConfirmationType == Confirmation.ConfirmationType.Unknown) || (confirmation.ConfType == allowedConfirmationType))) {
						if (confirm) {
							if (BotDatabase.SteamGuardAccount.AcceptConfirmation(confirmation)) {
								continue;
							}

							result = false;
							break;
						}

						if (BotDatabase.SteamGuardAccount.DenyConfirmation(confirmation)) {
							continue;
						}

						result = false;
						break;
					}
				} catch (SteamGuardAccount.WGTokenInvalidException) {
					result = false;
				} catch (Exception e) {
					Logging.LogGenericException(e, BotName);
					return false;
				}
			}

			if (result) {
				return true;
			}

			Logging.LogGenericWTF("Could not accept confirmations even after " + WebBrowser.MaxRetries + " tries", BotName);
			return false;
		}

		internal async Task<bool> RefreshSession() {
			if (!SteamClient.IsConnected) {
				return false;
			}

			SteamUser.WebAPIUserNonceCallback callback;

			try {
				callback = await SteamUser.RequestWebAPIUserNonce();
			} catch (Exception e) {
				Logging.LogGenericException(e, BotName);
				Start().Forget();
				return false;
			}

			if ((callback == null) || (callback.Result != EResult.OK) || string.IsNullOrEmpty(callback.Nonce)) {
				Start().Forget();
				return false;
			}

			if (ArchiWebHandler.Init(SteamClient, callback.Nonce, BotConfig.SteamParentalPIN)) {
				return true;
			}

			Start().Forget();
			return false;
		}

		internal async Task OnFarmingFinished(bool farmedSomething) {
			if ((farmedSomething || !FirstTradeSent) && BotConfig.SendOnFarmingFinished) {
				FirstTradeSent = true;
				await ResponseSendTrade(BotConfig.SteamMasterID).ConfigureAwait(false);
			}

			if (BotConfig.ShutdownOnFarmingFinished) {
				if (SkipFirstShutdown) {
					SkipFirstShutdown = false;
				} else {
					Stop();
					return;
				}
			}

			ResetGamesPlayed();
		}

		internal async Task<string> Response(ulong steamID, string message) {
			if ((steamID == 0) || string.IsNullOrEmpty(message)) {
				Logging.LogNullError(nameof(steamID) + " || " + nameof(message), BotName);
				return null;
			}

			if (message[0] != '!') {
				return await ResponseRedeem(steamID, message.Replace(",", Environment.NewLine), true).ConfigureAwait(false);
			}

			if (message.IndexOf(' ') < 0) {
				switch (message) {
					case "!2fa":
						return Response2FA(steamID);
					case "!2fano":
						return await Response2FAConfirm(steamID, false).ConfigureAwait(false);
					case "!2faoff":
						return Response2FAOff(steamID);
					case "!2faok":
						return await Response2FAConfirm(steamID, true).ConfigureAwait(false);
					case "!exit":
						return ResponseExit(steamID);
					case "!farm":
						return ResponseFarm(steamID);
					case "!help":
						return ResponseHelp(steamID);
					case "!loot":
						return await ResponseSendTrade(steamID).ConfigureAwait(false);
					case "!pause":
						return await ResponsePause(steamID).ConfigureAwait(false);
					case "!rejoinchat":
						return ResponseRejoinChat(steamID);
					case "!restart":
						return ResponseRestart(steamID);
					case "!status":
						return ResponseStatus(steamID);
					case "!statusall":
						return ResponseStatusAll(steamID);
					case "!stop":
						return ResponseStop(steamID);
					case "!update":
						return await ResponseUpdate(steamID).ConfigureAwait(false);
					default:
						return ResponseUnknown(steamID);
				}
			}

			string[] args = message.Split((char[]) null, StringSplitOptions.RemoveEmptyEntries);
			switch (args[0]) {
				case "!2fa":
					return Response2FA(steamID, args[1]);
				case "!2fano":
					return await Response2FAConfirm(steamID, args[1], false).ConfigureAwait(false);
				case "!2faoff":
					return Response2FAOff(steamID, args[1]);
				case "!2faok":
					return await Response2FAConfirm(steamID, args[1], true).ConfigureAwait(false);
				case "!addlicense":
					if (args.Length > 2) {
						return await ResponseAddLicense(steamID, args[1], args[2]).ConfigureAwait(false);
					}

					return await ResponseAddLicense(steamID, BotName, args[1]).ConfigureAwait(false);
				case "!farm":
					return ResponseFarm(steamID, args[1]);
				case "!loot":
					return await ResponseSendTrade(steamID, args[1]).ConfigureAwait(false);
				case "!owns":
					if (args.Length > 2) {
						return await ResponseOwns(steamID, args[1], args[2]).ConfigureAwait(false);
					}

					return await ResponseOwns(steamID, BotName, args[1]).ConfigureAwait(false);
				case "!pause":
					return await ResponsePause(steamID, args[1]).ConfigureAwait(false);
				case "!play":
					if (args.Length > 2) {
						return await ResponsePlay(steamID, args[1], args[2]).ConfigureAwait(false);
					}

					return await ResponsePlay(steamID, BotName, args[1]).ConfigureAwait(false);
				case "!redeem":
					if (args.Length > 2) {
						return await ResponseRedeem(steamID, args[1], args[2].Replace(",", Environment.NewLine), false).ConfigureAwait(false);
					}

					return await ResponseRedeem(steamID, BotName, args[1].Replace(",", Environment.NewLine), false).ConfigureAwait(false);
				case "!start":
					return await ResponseStart(steamID, args[1]).ConfigureAwait(false);
				case "!status":
					return ResponseStatus(steamID, args[1]);
				case "!stop":
					return ResponseStop(steamID, args[1]);
				default:
					return ResponseUnknown(steamID);
			}
		}

		private async Task Start() {
			if (!KeepRunning) {
				KeepRunning = true;
				Task.Run(() => HandleCallbacks()).Forget();
			}

			// 2FA tokens are expiring soon, don't use limiter when user is providing one
			if ((TwoFactorCode == null) || (BotDatabase.SteamGuardAccount != null)) {
				await LimitLoginRequestsAsync().ConfigureAwait(false);
			}

			Logging.LogGenericInfo("Starting...", BotName);
			SteamClient.Connect();
		}

		private void Stop() {
			Logging.LogGenericInfo("Stopping...", BotName);
			KeepRunning = false;

			if (SteamClient.IsConnected) {
				SteamClient.Disconnect();
			}

			Program.OnBotShutdown();
		}

		private bool IsMaster(ulong steamID) {
			if (steamID != 0) {
				return (steamID == BotConfig.SteamMasterID) || IsOwner(steamID);
			}

			Logging.LogNullError(nameof(steamID), BotName);
			return false;
		}

		private void ImportAuthenticator(string maFilePath) {
			if ((BotDatabase.SteamGuardAccount != null) || !File.Exists(maFilePath)) {
				return;
			}

			Logging.LogGenericInfo("Converting SDA .maFile into ASF format...", BotName);

			try {
				BotDatabase.SteamGuardAccount = JsonConvert.DeserializeObject<SteamGuardAccount>(File.ReadAllText(maFilePath));
				File.Delete(maFilePath);
				Logging.LogGenericInfo("Success!", BotName);
			} catch (Exception e) {
				Logging.LogGenericException(e, BotName);
				return;
			}

			// If this is SDA file, then we should already have everything ready
			if (BotDatabase.SteamGuardAccount.Session != null) {
				Logging.LogGenericInfo("Successfully finished importing mobile authenticator!", BotName);
				return;
			}

			// But here we're dealing with WinAuth authenticator
			Logging.LogGenericInfo("ASF requires a few more steps to complete authenticator import...", BotName);

			if (!InitializeLoginAndPassword(true)) {
				BotDatabase.SteamGuardAccount = null;
				return;
			}

			UserLogin userLogin = new UserLogin(BotConfig.SteamLogin, BotConfig.SteamPassword);
			LoginResult loginResult;
			while ((loginResult = userLogin.DoLogin()) != LoginResult.LoginOkay) {
				switch (loginResult) {
					case LoginResult.Need2FA:
						userLogin.TwoFactorCode = Program.GetUserInput(Program.EUserInputType.TwoFactorAuthentication, BotName);
						if (string.IsNullOrEmpty(userLogin.TwoFactorCode)) {
							BotDatabase.SteamGuardAccount = null;
							return;
						}

						break;
					default:
						BotDatabase.SteamGuardAccount = null;
						Logging.LogGenericError("Unhandled situation: " + loginResult, BotName);
						return;
				}
			}

			if (userLogin.Session == null) {
				BotDatabase.SteamGuardAccount = null;
				Logging.LogGenericError("Session is invalid, linking can't be completed!", BotName);
				return;
			}

			BotDatabase.SteamGuardAccount.FullyEnrolled = true;
			BotDatabase.SteamGuardAccount.Session = userLogin.Session;

			if (string.IsNullOrEmpty(BotDatabase.SteamGuardAccount.DeviceID)) {
				BotDatabase.SteamGuardAccount.DeviceID = Program.GetUserInput(Program.EUserInputType.DeviceID, BotName);
			}

			BotDatabase.Save();
			Logging.LogGenericInfo("Successfully finished importing mobile authenticator!", BotName);
		}

		private async Task<string> ResponsePause(ulong steamID) {
			if (steamID == 0) {
				Logging.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			if (CardsFarmer.ManualMode) {
				await CardsFarmer.SwitchToManualMode(false).ConfigureAwait(false);
				return "Automatic farming is enabled again!";
			}

			await CardsFarmer.SwitchToManualMode(true).ConfigureAwait(false);
			return "Automatic farming is now stopped!";
		}

		private static async Task<string> ResponsePause(ulong steamID, string botName) {
			if ((steamID == 0) || string.IsNullOrEmpty(botName)) {
				Logging.LogNullError(nameof(steamID) + " || " + nameof(botName));
				return null;
			}

			Bot bot;
			if (Bots.TryGetValue(botName, out bot)) {
				return await bot.ResponsePause(steamID).ConfigureAwait(false);
			}

			if (IsOwner(steamID)) {
				return "Couldn't find any bot named " + botName + "!";
			}

			return null;
		}

		private string ResponseStatus(ulong steamID) {
			if (steamID == 0) {
				Logging.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			if (CardsFarmer.CurrentGamesFarming.Count > 0) {
				return "Bot " + BotName + " is farming appIDs: " + string.Join(", ", CardsFarmer.CurrentGamesFarming) + " and has a total of " + CardsFarmer.GamesToFarm.Count + " games left to farm.";
			}

			if (CardsFarmer.ManualMode) {
				return "Bot " + BotName + " is running in manual mode.";
			}

			if (SteamClient.IsConnected) {
				return "Bot " + BotName + " is not farming anything.";
			}

			if (KeepRunning) {
				return "Bot " + BotName + " is not connected.";
			}

			return "Bot " + BotName + " is not running.";
		}

		private static string ResponseStatus(ulong steamID, string botName) {
			if ((steamID == 0) || string.IsNullOrEmpty(botName)) {
				Logging.LogNullError(nameof(steamID) + " || " + nameof(botName));
				return null;
			}

			Bot bot;
			if (Bots.TryGetValue(botName, out bot)) {
				return bot.ResponseStatus(steamID);
			}

			if (IsOwner(steamID)) {
				return "Couldn't find any bot named " + botName + "!";
			}

			return null;
		}

		private static string ResponseStatusAll(ulong steamID) {
			if (steamID == 0) {
				Logging.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsOwner(steamID)) {
				return null;
			}

			StringBuilder result = new StringBuilder(Environment.NewLine);

			byte runningBotsCount = 0;
			foreach (Bot bot in Bots.Values) {
				result.Append(bot.ResponseStatus(steamID) + Environment.NewLine);
				if (bot.KeepRunning) {
					runningBotsCount++;
				}
			}

			result.Append("There are " + runningBotsCount + "/" + Bots.Count + " bots running.");
			return result.ToString();
		}

		private async Task<string> ResponseSendTrade(ulong steamID) {
			if (steamID == 0) {
				Logging.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			if (BotConfig.SteamMasterID == 0) {
				return "Trade couldn't be send because SteamMasterID is not defined!";
			}

			await Trading.LimitInventoryRequestsAsync().ConfigureAwait(false);
			HashSet<Steam.Item> inventory = await ArchiWebHandler.GetMyTradableInventory().ConfigureAwait(false);

			if ((inventory == null) || (inventory.Count == 0)) {
				return "Nothing to send, inventory seems empty!";
			}

			// Remove from our pending inventory all items that are not steam cards and boosters
			inventory.RemoveWhere(item => (item.Type != Steam.Item.EType.TradingCard) && (item.Type != Steam.Item.EType.FoilTradingCard) && (item.Type != Steam.Item.EType.BoosterPack));
			inventory.TrimExcess();

			if (inventory.Count == 0) {
				return "Nothing to send, inventory seems empty!";
			}

			if (!await ArchiWebHandler.SendTradeOffer(inventory, BotConfig.SteamMasterID, BotConfig.SteamTradeToken).ConfigureAwait(false)) {
				return "Trade offer failed due to error!";
			}

			await AcceptConfirmations(true, Confirmation.ConfirmationType.Trade).ConfigureAwait(false);
			return "Trade offer sent successfully!";
		}

		private static async Task<string> ResponseSendTrade(ulong steamID, string botName) {
			if ((steamID == 0) || string.IsNullOrEmpty(botName)) {
				Logging.LogNullError(nameof(steamID) + " || " + nameof(botName));
				return null;
			}

			Bot bot;
			if (Bots.TryGetValue(botName, out bot)) {
				return await bot.ResponseSendTrade(steamID).ConfigureAwait(false);
			}

			if (IsOwner(steamID)) {
				return "Couldn't find any bot named " + botName + "!";
			}

			return null;
		}

		private string Response2FA(ulong steamID) {
			if (steamID == 0) {
				Logging.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			if (BotDatabase.SteamGuardAccount == null) {
				return "That bot doesn't have ASF 2FA enabled!";
			}

			long timeLeft = 30 - TimeAligner.GetSteamTime() % 30;
			return "2FA Token: " + BotDatabase.SteamGuardAccount.GenerateSteamGuardCode() + " (expires in " + timeLeft + " seconds)";
		}

		private static string Response2FA(ulong steamID, string botName) {
			if ((steamID == 0) || string.IsNullOrEmpty(botName)) {
				Logging.LogNullError(nameof(steamID) + " || " + nameof(botName));
				return null;
			}

			Bot bot;
			if (Bots.TryGetValue(botName, out bot)) {
				return bot.Response2FA(steamID);
			}

			if (IsOwner(steamID)) {
				return "Couldn't find any bot named " + botName + "!";
			}

			return null;
		}

		private string Response2FAOff(ulong steamID) {
			if (steamID == 0) {
				Logging.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			if (BotDatabase.SteamGuardAccount == null) {
				return "That bot doesn't have ASF 2FA enabled!";
			}

			return DelinkMobileAuthenticator() ? "Done! Bot is no longer using ASF 2FA" : "Something went wrong during delinking mobile authenticator!";
		}

		private static string Response2FAOff(ulong steamID, string botName) {
			if ((steamID == 0) || string.IsNullOrEmpty(botName)) {
				Logging.LogNullError(nameof(steamID) + " || " + nameof(botName));
				return null;
			}

			Bot bot;
			if (Bots.TryGetValue(botName, out bot)) {
				return bot.Response2FAOff(steamID);
			}

			if (IsOwner(steamID)) {
				return "Couldn't find any bot named " + botName + "!";
			}

			return null;
		}

		private async Task<string> Response2FAConfirm(ulong steamID, bool confirm) {
			if (steamID == 0) {
				Logging.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			if (BotDatabase.SteamGuardAccount == null) {
				return "That bot doesn't have ASF 2FA enabled!";
			}

			await AcceptConfirmations(confirm).ConfigureAwait(false);
			return "Done!";
		}

		private static async Task<string> Response2FAConfirm(ulong steamID, string botName, bool confirm) {
			if ((steamID == 0) || string.IsNullOrEmpty(botName)) {
				Logging.LogNullError(nameof(steamID) + " || " + nameof(botName));
				return null;
			}

			Bot bot;
			if (Bots.TryGetValue(botName, out bot)) {
				return await bot.Response2FAConfirm(steamID, confirm).ConfigureAwait(false);
			}

			if (IsOwner(steamID)) {
				return "Couldn't find any bot named " + botName + "!";
			}

			return null;
		}

		private static string ResponseExit(ulong steamID) {
			if (steamID == 0) {
				Logging.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsOwner(steamID)) {
				return null;
			}

			// Schedule the task after some time so user can receive response
			Task.Run(async () => {
				await Utilities.SleepAsync(1000).ConfigureAwait(false);
				Program.Exit();
			}).Forget();

			return "Done!";
		}

		private string ResponseFarm(ulong steamID) {
			if (steamID == 0) {
				Logging.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			if (!SteamClient.IsConnected) {
				return "This bot instance is not connected!";
			}

			CardsFarmer.RestartFarming().Forget();
			return "Done!";
		}

		private static string ResponseFarm(ulong steamID, string botName) {
			if ((steamID == 0) || string.IsNullOrEmpty(botName)) {
				Logging.LogNullError(nameof(steamID) + " || " + nameof(botName));
				return null;
			}

			Bot bot;
			if (Bots.TryGetValue(botName, out bot)) {
				return bot.ResponseFarm(steamID);
			}

			if (IsOwner(steamID)) {
				return "Couldn't find any bot named " + botName + "!";
			}

			return null;
		}

		private string ResponseHelp(ulong steamID) {
			if (steamID == 0) {
				Logging.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			return "https://github.com/" + Program.GithubRepo + "/wiki/Commands";
		}

		private async Task<string> ResponseRedeem(ulong steamID, string message, bool validate) {
			if ((steamID == 0) || string.IsNullOrEmpty(message)) {
				Logging.LogNullError(nameof(steamID) + " || " + nameof(message));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			StringBuilder response = new StringBuilder();
			using (StringReader reader = new StringReader(message))
			using (IEnumerator<Bot> iterator = Bots.Values.GetEnumerator()) {
				string key = reader.ReadLine();
				Bot currentBot = this;
				while (!string.IsNullOrEmpty(key) && (currentBot != null)) {
					if (validate && !IsValidCdKey(key)) {
						key = reader.ReadLine(); // Next key
						continue; // Keep current bot
					}

					if (!currentBot.SteamClient.IsConnected) {
						currentBot = null; // Either bot will be changed, or loop aborted
					} else {
						ArchiHandler.PurchaseResponseCallback result = await currentBot.ArchiHandler.RedeemKey(key).ConfigureAwait(false);
						if (result == null) {
							currentBot = null; // Either bot will be changed, or loop aborted
						} else {
							switch (result.PurchaseResult) {
								case ArchiHandler.PurchaseResponseCallback.EPurchaseResult.DuplicatedKey:
								case ArchiHandler.PurchaseResponseCallback.EPurchaseResult.InvalidKey:
								case ArchiHandler.PurchaseResponseCallback.EPurchaseResult.OK:
									response.Append(Environment.NewLine + "<" + currentBot.BotName + "> Key: " + key + " | Status: " + result.PurchaseResult + " | Items: " + string.Join("", result.Items));

									key = reader.ReadLine(); // Next key

									if (result.PurchaseResult == ArchiHandler.PurchaseResponseCallback.EPurchaseResult.OK) {
										break; // Next bot (if needed)
									}

									continue; // Keep current bot
								case ArchiHandler.PurchaseResponseCallback.EPurchaseResult.AlreadyOwned:
								case ArchiHandler.PurchaseResponseCallback.EPurchaseResult.BaseGameRequired:
								case ArchiHandler.PurchaseResponseCallback.EPurchaseResult.OnCooldown:
								case ArchiHandler.PurchaseResponseCallback.EPurchaseResult.RegionLocked:
									response.Append(Environment.NewLine + "<" + currentBot.BotName + "> Key: " + key + " | Status: " + result.PurchaseResult + " | Items: " + string.Join("", result.Items));

									if (!BotConfig.ForwardKeysToOtherBots) {
										key = reader.ReadLine(); // Next key
										break; // Next bot (if needed)
									}

									if (BotConfig.DistributeKeys) {
										break; // Next bot, without changing key
									}

									bool alreadyHandled = false;
									foreach (Bot bot in Bots.Values.Where(bot => (bot != this) && bot.SteamClient.IsConnected)) {
										ArchiHandler.PurchaseResponseCallback otherResult = await bot.ArchiHandler.RedeemKey(key).ConfigureAwait(false);
										if (otherResult == null) {
											continue;
										}

										switch (otherResult.PurchaseResult) {
											case ArchiHandler.PurchaseResponseCallback.EPurchaseResult.DuplicatedKey:
											case ArchiHandler.PurchaseResponseCallback.EPurchaseResult.InvalidKey:
											case ArchiHandler.PurchaseResponseCallback.EPurchaseResult.OK:
												alreadyHandled = true; // This key is already handled, as we either redeemed it or we're sure it's dupe/invalid
												break;
										}

										response.Append(Environment.NewLine + "<" + bot.BotName + "> Key: " + key + " | Status: " + otherResult.PurchaseResult + " | Items: " + string.Join("", otherResult.Items));

										if (alreadyHandled) {
											break;
										}
									}

									key = reader.ReadLine(); // Next key
									break; // Next bot (if needed)
							}
						}
					}

					if (!BotConfig.DistributeKeys) {
						continue;
					}

					do {
						currentBot = iterator.MoveNext() ? iterator.Current : null;
					} while ((currentBot == this) || ((currentBot != null) && !currentBot.SteamClient.IsConnected));
				}
			}

			return response.Length == 0 ? null : response.ToString();
		}

		private static async Task<string> ResponseRedeem(ulong steamID, string botName, string message, bool validate) {
			if ((steamID == 0) || string.IsNullOrEmpty(botName) || string.IsNullOrEmpty(message)) {
				Logging.LogNullError(nameof(steamID) + " || " + nameof(botName) + " || " + nameof(message));
				return null;
			}

			Bot bot;
			if (Bots.TryGetValue(botName, out bot)) {
				return await bot.ResponseRedeem(steamID, message, validate).ConfigureAwait(false);
			}
			if (IsOwner(steamID)) {
				return "Couldn't find any bot named " + botName + "!";
			}

			return null;
		}

		private static string ResponseRejoinChat(ulong steamID) {
			if (steamID == 0) {
				Logging.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsOwner(steamID)) {
				return null;
			}

			foreach (Bot bot in Bots.Values) {
				bot.JoinMasterChat();
			}

			return "Done!";
		}

		private static string ResponseRestart(ulong steamID) {
			if (steamID == 0) {
				Logging.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsOwner(steamID)) {
				return null;
			}

			// Schedule the task after some time so user can receive response
			Task.Run(async () => {
				await Utilities.SleepAsync(1000).ConfigureAwait(false);
				Program.Restart();
			}).Forget();

			return "Done!";
		}

		private async Task<string> ResponseAddLicense(ulong steamID, ICollection<uint> gameIDs) {
			if ((steamID == 0) || (gameIDs == null) || (gameIDs.Count == 0)) {
				Logging.LogNullError(nameof(steamID) + " || " + nameof(gameIDs) + " || " + nameof(gameIDs.Count));
				return null;
			}

			if (!SteamClient.IsConnected || !IsMaster(steamID)) {
				return null;
			}

			StringBuilder result = new StringBuilder();
			foreach (uint gameID in gameIDs) {
				SteamApps.FreeLicenseCallback callback = await SteamApps.RequestFreeLicense(gameID);
				if (callback == null) {
					continue;
				}

				result.AppendLine("Result: " + callback.Result + " | Granted apps: " + string.Join(", ", callback.GrantedApps) + " " + string.Join(", ", callback.GrantedPackages));
			}

			return result.ToString();
		}

		private static async Task<string> ResponseAddLicense(ulong steamID, string botName, string games) {
			if ((steamID == 0) || string.IsNullOrEmpty(botName) || string.IsNullOrEmpty(games)) {
				Logging.LogNullError(nameof(steamID) + " || " + nameof(botName) + " || " + nameof(games));
				return null;
			}

			Bot bot;
			if (!Bots.TryGetValue(botName, out bot)) {
				if (IsOwner(steamID)) {
					return "Couldn't find any bot named " + botName + "!";
				}

				return null;
			}

			string[] gameIDs = games.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

			HashSet<uint> gamesToRedeem = new HashSet<uint>();
			foreach (string game in gameIDs.Where(game => !string.IsNullOrEmpty(game))) {
				uint gameID;
				if (!uint.TryParse(game, out gameID)) {
					continue;
				}

				gamesToRedeem.Add(gameID);
			}

			if (gamesToRedeem.Count == 0) {
				return "Couldn't parse any games given!";
			}

			return await bot.ResponseAddLicense(steamID, gamesToRedeem).ConfigureAwait(false);
		}

		private async Task<string> ResponseOwns(ulong steamID, string query) {
			if ((steamID == 0) || string.IsNullOrEmpty(query)) {
				Logging.LogNullError(nameof(steamID) + " || " + nameof(query));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			Dictionary<uint, string> ownedGames;
			if (!string.IsNullOrEmpty(BotConfig.SteamApiKey)) {
				ownedGames = ArchiWebHandler.GetOwnedGames(SteamClient.SteamID);
			} else {
				ownedGames = await ArchiWebHandler.GetOwnedGames().ConfigureAwait(false);
			}

			if ((ownedGames == null) || (ownedGames.Count == 0)) {
				return "List of owned games is empty!";
			}

			StringBuilder response = new StringBuilder();

			string[] games = query.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
			foreach (string game in games.Where(game => !string.IsNullOrEmpty(game))) {
				// Check if this is appID
				uint appID;
				if (uint.TryParse(game, out appID)) {
					string ownedName;
					if (ownedGames.TryGetValue(appID, out ownedName)) {
						response.Append(Environment.NewLine + "Owned already: " + appID + " | " + ownedName);
					} else {
						response.Append(Environment.NewLine + "Not owned yet: " + appID);
					}

					continue;
				}

				// This is a string, so check our entire library
				foreach (KeyValuePair<uint, string> ownedGame in ownedGames.Where(ownedGame => ownedGame.Value.IndexOf(game, StringComparison.OrdinalIgnoreCase) >= 0)) {
					response.Append(Environment.NewLine + "Owned already: " + ownedGame.Key + " | " + ownedGame.Value);
				}
			}

			if (response.Length > 0) {
				return response.ToString();
			}

			return "Not owned yet: " + query;
		}

		private static async Task<string> ResponseOwns(ulong steamID, string botName, string query) {
			if ((steamID == 0) || string.IsNullOrEmpty(botName) || string.IsNullOrEmpty(query)) {
				Logging.LogNullError(nameof(steamID) + " || " + nameof(botName) + " || " + nameof(query));
				return null;
			}

			Bot bot;
			if (Bots.TryGetValue(botName, out bot)) {
				return await bot.ResponseOwns(steamID, query).ConfigureAwait(false);
			}

			if (IsOwner(steamID)) {
				return "Couldn't find any bot named " + botName + "!";
			}

			return null;
		}

		private async Task<string> ResponsePlay(ulong steamID, HashSet<uint> gameIDs) {
			if ((steamID == 0) || (gameIDs == null) || (gameIDs.Count == 0)) {
				Logging.LogNullError(nameof(steamID) + " || " + nameof(gameIDs) + " || " + nameof(gameIDs.Count));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			if (gameIDs.Contains(0)) {
				if (!CardsFarmer.ManualMode) {
					return "Done!";
				}

				await CardsFarmer.SwitchToManualMode(false).ConfigureAwait(false);
			} else {
				if (!CardsFarmer.ManualMode) {
					await CardsFarmer.SwitchToManualMode(true).ConfigureAwait(false);
				}

				ArchiHandler.PlayGames(gameIDs);
			}

			return "Done!";
		}

		private static async Task<string> ResponsePlay(ulong steamID, string botName, string games) {
			if ((steamID == 0) || string.IsNullOrEmpty(botName) || string.IsNullOrEmpty(games)) {
				Logging.LogNullError(nameof(steamID) + " || " + nameof(botName) + " || " + nameof(games));
				return null;
			}

			Bot bot;
			if (!Bots.TryGetValue(botName, out bot)) {
				if (IsOwner(steamID)) {
					return "Couldn't find any bot named " + botName + "!";
				}

				return null;
			}

			string[] gameIDs = games.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

			HashSet<uint> gamesToPlay = new HashSet<uint>();
			foreach (string game in gameIDs.Where(game => !string.IsNullOrEmpty(game))) {
				uint gameID;
				if (!uint.TryParse(game, out gameID)) {
					continue;
				}

				gamesToPlay.Add(gameID);
			}

			if (gamesToPlay.Count == 0) {
				return "Couldn't parse any games given!";
			}

			return await bot.ResponsePlay(steamID, gamesToPlay).ConfigureAwait(false);
		}

		private async Task<string> ResponseStart(ulong steamID) {
			if (steamID == 0) {
				Logging.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			if (KeepRunning) {
				return "That bot instance is already running!";
			}

			SkipFirstShutdown = true;
			await Start().ConfigureAwait(false);
			return "Done!";
		}

		private static async Task<string> ResponseStart(ulong steamID, string botName) {
			if ((steamID == 0) || string.IsNullOrEmpty(botName)) {
				Logging.LogNullError(nameof(steamID) + " || " + nameof(botName));
				return null;
			}

			Bot bot;
			if (Bots.TryGetValue(botName, out bot)) {
				return await bot.ResponseStart(steamID).ConfigureAwait(false);
			}

			if (IsOwner(steamID)) {
				return "Couldn't find any bot named " + botName + "!";
			}

			return null;
		}

		private string ResponseStop(ulong steamID) {
			if (steamID == 0) {
				Logging.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			if (!KeepRunning) {
				return "That bot instance is already inactive!";
			}

			Stop();
			return "Done!";
		}

		private static string ResponseStop(ulong steamID, string botName) {
			if ((steamID == 0) || string.IsNullOrEmpty(botName)) {
				Logging.LogNullError(nameof(steamID) + " || " + nameof(botName));
				return null;
			}

			Bot bot;
			if (Bots.TryGetValue(botName, out bot)) {
				return bot.ResponseStop(steamID);
			}

			if (IsOwner(steamID)) {
				return "Couldn't find any bot named " + botName + "!";
			}

			return null;
		}

		private string ResponseUnknown(ulong steamID) {
			if (steamID != 0) {
				return !IsMaster(steamID) ? null : "ERROR: Unknown command!";
			}

			Logging.LogNullError(nameof(steamID), BotName);
			return null;
		}

		private static async Task<string> ResponseUpdate(ulong steamID) {
			if (steamID == 0) {
				Logging.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsOwner(steamID)) {
				return null;
			}

			await Program.CheckForUpdate(true).ConfigureAwait(false);
			return "Done!";
		}

		private void HandleCallbacks() {
			TimeSpan timeSpan = TimeSpan.FromMilliseconds(CallbackSleep);
			while (KeepRunning || SteamClient.IsConnected) {
				try {
					CallbackManager.RunWaitCallbacks(timeSpan);
				} catch (Exception e) {
					Logging.LogGenericException(e, BotName);
				}
			}
		}

		private async Task HandleMessage(ulong chatID, ulong steamID, string message) {
			if ((chatID == 0) || (steamID == 0) || string.IsNullOrEmpty(message)) {
				Logging.LogNullError(nameof(chatID) + " || " + nameof(steamID) + " || " + nameof(message), BotName);
				return;
			}

			string response = await Response(steamID, message).ConfigureAwait(false);
			if (string.IsNullOrEmpty(response)) { // We respond with null when user is not authorized (and similar)
				return;
			}

			SendMessage(chatID, response);
		}

		private void SendMessage(ulong steamID, string message) {
			if ((steamID == 0) || string.IsNullOrEmpty(message)) {
				Logging.LogNullError(nameof(steamID) + " || " + nameof(message), BotName);
				return;
			}

			if (new SteamID(steamID).IsChatAccount) {
				SendMessageToChannel(steamID, message);
			} else {
				SendMessageToUser(steamID, message);
			}
		}

		private void SendMessageToChannel(ulong steamID, string message) {
			if ((steamID == 0) || string.IsNullOrEmpty(message)) {
				Logging.LogNullError(nameof(steamID) + " || " + nameof(message), BotName);
				return;
			}

			if (!SteamClient.IsConnected) {
				return;
			}

			for (int i = 0; i < message.Length; i += MaxSteamMessageLength - 6) {
				string messagePart = (i > 0 ? "..." : "") + message.Substring(i, Math.Min(MaxSteamMessageLength - 6, message.Length - i)) + (MaxSteamMessageLength - 6 < message.Length - i ? "..." : "");
				SteamFriends.SendChatRoomMessage(steamID, EChatEntryType.ChatMsg, messagePart);
			}
		}

		private void SendMessageToUser(ulong steamID, string message) {
			if ((steamID == 0) || string.IsNullOrEmpty(message)) {
				Logging.LogNullError(nameof(steamID) + " || " + nameof(message), BotName);
				return;
			}

			if (!SteamClient.IsConnected) {
				return;
			}

			for (int i = 0; i < message.Length; i += MaxSteamMessageLength - 6) {
				string messagePart = (i > 0 ? "..." : "") + message.Substring(i, Math.Min(MaxSteamMessageLength - 6, message.Length - i)) + (MaxSteamMessageLength - 6 < message.Length - i ? "..." : "");
				SteamFriends.SendChatMessage(steamID, EChatEntryType.ChatMsg, messagePart);
			}
		}

		private void LinkMobileAuthenticator() {
			if (BotDatabase.SteamGuardAccount != null) {
				return;
			}

			Logging.LogGenericInfo("Linking new ASF MobileAuthenticator...", BotName);

			if (!InitializeLoginAndPassword(true)) {
				return;
			}

			UserLogin userLogin = new UserLogin(BotConfig.SteamLogin, BotConfig.SteamPassword);
			LoginResult loginResult;
			while ((loginResult = userLogin.DoLogin()) != LoginResult.LoginOkay) {
				switch (loginResult) {
					case LoginResult.NeedEmail:
						userLogin.EmailCode = Program.GetUserInput(Program.EUserInputType.SteamGuard, BotName);
						if (string.IsNullOrEmpty(userLogin.EmailCode)) {
							return;
						}

						break;
					default:
						Logging.LogGenericError("Unhandled situation: " + loginResult, BotName);
						return;
				}
			}

			AuthenticatorLinker authenticatorLinker = new AuthenticatorLinker(userLogin.Session);

			AuthenticatorLinker.LinkResult linkResult;
			while ((linkResult = authenticatorLinker.AddAuthenticator()) != AuthenticatorLinker.LinkResult.AwaitingFinalization) {
				switch (linkResult) {
					case AuthenticatorLinker.LinkResult.MustProvidePhoneNumber:
						authenticatorLinker.PhoneNumber = Program.GetUserInput(Program.EUserInputType.PhoneNumber, BotName);
						if (string.IsNullOrEmpty(authenticatorLinker.PhoneNumber)) {
							return;
						}

						break;
					default:
						Logging.LogGenericError("Unhandled situation: " + linkResult, BotName);
						return;
				}
			}

			BotDatabase.SteamGuardAccount = authenticatorLinker.LinkedAccount;

			string sms = Program.GetUserInput(Program.EUserInputType.SMS, BotName);
			if (string.IsNullOrEmpty(sms)) {
				Logging.LogGenericWarning("Aborted!", BotName);
				DelinkMobileAuthenticator();
				return;
			}

			AuthenticatorLinker.FinalizeResult finalizeResult;
			while ((finalizeResult = authenticatorLinker.FinalizeAddAuthenticator(sms)) != AuthenticatorLinker.FinalizeResult.Success) {
				switch (finalizeResult) {
					case AuthenticatorLinker.FinalizeResult.BadSMSCode:
						sms = Program.GetUserInput(Program.EUserInputType.SMS, BotName);
						if (string.IsNullOrEmpty(sms)) {
							Logging.LogGenericWarning("Aborted!", BotName);
							DelinkMobileAuthenticator();
							return;
						}

						break;
					default:
						Logging.LogGenericError("Unhandled situation: " + finalizeResult, BotName);
						DelinkMobileAuthenticator();
						return;
				}
			}

			// Ensure that we also save changes made by finalization step (if any)
			BotDatabase.Save();

			Logging.LogGenericInfo("Successfully linked ASF as new mobile authenticator for this account!", BotName);
			Program.GetUserInput(Program.EUserInputType.RevocationCode, BotName, BotDatabase.SteamGuardAccount.RevocationCode);
		}

		private bool DelinkMobileAuthenticator() {
			if (BotDatabase.SteamGuardAccount == null) {
				return false;
			}

			// Try to deactivate authenticator, and assume we're safe to remove if it wasn't fully enrolled yet (even if request fails)
			if (!BotDatabase.SteamGuardAccount.DeactivateAuthenticator() && BotDatabase.SteamGuardAccount.FullyEnrolled) {
				return false;
			}

			BotDatabase.SteamGuardAccount = null;
			return true;
		}

		private void JoinMasterChat() {
			if (!SteamClient.IsConnected || (BotConfig.SteamMasterClanID == 0)) {
				return;
			}

			SteamFriends.JoinChat(BotConfig.SteamMasterClanID);
		}

		private bool InitializeLoginAndPassword(bool requiresPassword) {
			if (string.IsNullOrEmpty(BotConfig.SteamLogin)) {
				BotConfig.SteamLogin = Program.GetUserInput(Program.EUserInputType.Login, BotName);
				if (string.IsNullOrEmpty(BotConfig.SteamLogin)) {
					return false;
				}
			}

			if (!string.IsNullOrEmpty(BotConfig.SteamPassword) || (!requiresPassword && !string.IsNullOrEmpty(BotDatabase.LoginKey))) {
				return true;
			}

			BotConfig.SteamPassword = Program.GetUserInput(Program.EUserInputType.Password, BotName);
			return !string.IsNullOrEmpty(BotConfig.SteamPassword);
		}

		private void ResetGamesPlayed() {
			if (PlayingBlocked) {
				return;
			}

			if (!string.IsNullOrEmpty(BotConfig.CustomGamePlayedWhileIdle)) {
				ArchiHandler.PlayGame(BotConfig.CustomGamePlayedWhileIdle);
			} else {
				ArchiHandler.PlayGames(BotConfig.GamesPlayedWhileIdle);
			}
		}

		private void OnConnected(SteamClient.ConnectedCallback callback) {
			if (callback == null) {
				Logging.LogNullError(nameof(callback), BotName);
				return;
			}

			if (callback.Result != EResult.OK) {
				Logging.LogGenericError("Unable to connect to Steam: " + callback.Result, BotName);
				return;
			}

			Logging.LogGenericInfo("Connected to Steam!", BotName);

			if (!KeepRunning) {
				Logging.LogGenericInfo("Disconnecting...", BotName);
				SteamClient.Disconnect();
				return;
			}

			byte[] sentryHash = null;
			if (File.Exists(SentryFile)) {
				try {
					byte[] sentryFileContent = File.ReadAllBytes(SentryFile);
					sentryHash = CryptoHelper.SHAHash(sentryFileContent);
				} catch (Exception e) {
					Logging.LogGenericException(e, BotName);
				}
			}

			if (!InitializeLoginAndPassword(false)) {
				Stop();
				return;
			}

			Logging.LogGenericInfo("Logging in...", BotName);

			// If we have ASF 2FA enabled, we can always provide TwoFactorCode, and save a request
			if (BotDatabase.SteamGuardAccount != null) {
				TwoFactorCode = BotDatabase.SteamGuardAccount.GenerateSteamGuardCode();
			}

			// TODO: Please remove me immediately after https://github.com/SteamRE/SteamKit/issues/254 gets fixed
			if (Program.GlobalConfig.HackIgnoreMachineID) {
				Logging.LogGenericWarning("Using workaround for broken GenerateMachineID()!", BotName);
				ArchiHandler.HackedLogOn(new SteamUser.LogOnDetails {
					Username = BotConfig.SteamLogin,
					Password = BotConfig.SteamPassword,
					AuthCode = AuthCode,
					CellID = Program.GlobalDatabase.CellID,
					LoginID = LoginID,
					LoginKey = BotDatabase.LoginKey,
					TwoFactorCode = TwoFactorCode,
					SentryFileHash = sentryHash,
					ShouldRememberPassword = true
				});
				return;
			}

			SteamUser.LogOn(new SteamUser.LogOnDetails {
				Username = BotConfig.SteamLogin,
				Password = BotConfig.SteamPassword,
				AuthCode = AuthCode,
				CellID = Program.GlobalDatabase.CellID,
				LoginID = LoginID,
				LoginKey = BotDatabase.LoginKey,
				TwoFactorCode = TwoFactorCode,
				SentryFileHash = sentryHash,
				ShouldRememberPassword = true
			});
		}

		private async void OnDisconnected(SteamClient.DisconnectedCallback callback) {
			if (callback == null) {
				Logging.LogNullError(nameof(callback), BotName);
				return;
			}

			Logging.LogGenericInfo("Disconnected from Steam!", BotName);

			FirstTradeSent = false;
			CardsFarmer.StopFarming().Forget();

			// If we initiated disconnect, do not attempt to reconnect
			if (callback.UserInitiated) {
				return;
			}

			if (InvalidPassword) {
				InvalidPassword = false;
				if (!string.IsNullOrEmpty(BotDatabase.LoginKey)) { // InvalidPassword means usually that login key has expired, if we used it
					BotDatabase.LoginKey = null;
					Logging.LogGenericInfo("Removed expired login key", BotName);
				} else { // If we didn't use login key, InvalidPassword usually means we got captcha or other network-based throttling
					Logging.LogGenericInfo("Will retry after 25 minutes...", BotName);
					await Utilities.SleepAsync(25 * 60 * 1000).ConfigureAwait(false); // Captcha disappears after around 20 minutes, so we make it 25
				}
			}

			if (!KeepRunning || SteamClient.IsConnected) {
				return;
			}

			Logging.LogGenericInfo("Reconnecting...", BotName);

			// 2FA tokens are expiring soon, don't use limiter when user is providing one
			if ((TwoFactorCode == null) || (BotDatabase.SteamGuardAccount != null)) {
				await LimitLoginRequestsAsync().ConfigureAwait(false);
			}

			SteamClient.Connect();
		}

		private void OnFreeLicense(SteamApps.FreeLicenseCallback callback) {
			if (callback == null) {
				Logging.LogNullError(nameof(callback), BotName);
			}
		}

		private async void OnGuestPassList(SteamApps.GuestPassListCallback callback) {
			if ((callback == null) || (callback.GuestPasses == null)) {
				Logging.LogNullError(nameof(callback) + " || " + nameof(callback.GuestPasses), BotName);
				return;
			}

			if ((callback.CountGuestPassesToRedeem == 0) || (callback.GuestPasses.Count == 0) || !BotConfig.AcceptGifts) {
				return;
			}

			bool acceptedSomething = false;
			foreach (ulong gid in callback.GuestPasses.Select(guestPass => guestPass["gid"].AsUnsignedLong()).Where(gid => gid != 0)) {
				Logging.LogGenericInfo("Accepting gift: " + gid + "...", BotName);
				if (await ArchiWebHandler.AcceptGift(gid).ConfigureAwait(false)) {
					acceptedSomething = true;
					Logging.LogGenericInfo("Success!", BotName);
				} else {
					Logging.LogGenericInfo("Failed!", BotName);
				}
			}

			if (acceptedSomething) {
				CardsFarmer.RestartFarming().Forget();
			}
		}

		private void OnChatInvite(SteamFriends.ChatInviteCallback callback) {
			if ((callback == null) || (callback.ChatRoomID == null) || (callback.PatronID == null)) {
				Logging.LogNullError(nameof(callback) + " || " + nameof(callback.ChatRoomID) + " || " + nameof(callback.PatronID), BotName);
				return;
			}

			if (!IsMaster(callback.PatronID)) {
				return;
			}

			SteamFriends.JoinChat(callback.ChatRoomID);
		}

		private async void OnChatMsg(SteamFriends.ChatMsgCallback callback) {
			if (callback == null) {
				Logging.LogNullError(nameof(callback), BotName);
				return;
			}

			if (callback.ChatMsgType != EChatEntryType.ChatMsg) {
				return;
			}

			if ((callback.ChatRoomID == null) || (callback.ChatterID == null) || string.IsNullOrEmpty(callback.Message)) {
				Logging.LogNullError(nameof(callback.ChatRoomID) + " || " + nameof(callback.ChatterID) + " || " + nameof(callback.Message), BotName);
				return;
			}

			switch (callback.Message) {
				case "!leave":
					if (!IsMaster(callback.ChatterID)) {
						break;
					}

					SteamFriends.LeaveChat(callback.ChatRoomID);
					break;
				default:
					await HandleMessage(callback.ChatRoomID, callback.ChatterID, callback.Message).ConfigureAwait(false);
					break;
			}
		}

		private void OnFriendsList(SteamFriends.FriendsListCallback callback) {
			if ((callback == null) || (callback.FriendList == null)) {
				Logging.LogNullError(nameof(callback) + " || " + nameof(callback.FriendList), BotName);
				return;
			}

			foreach (SteamFriends.FriendsListCallback.Friend friend in callback.FriendList.Where(friend => friend.Relationship == EFriendRelationship.RequestRecipient)) {
				switch (friend.SteamID.AccountType) {
					case EAccountType.Clan:
						// TODO: Accept clan invites from master?
						break;
					default:
						if (!IsMaster(friend.SteamID)) {
							break;
						}

						SteamFriends.AddFriend(friend.SteamID);
						break;
				}
			}
		}

		private async void OnFriendMsg(SteamFriends.FriendMsgCallback callback) {
			if (callback == null) {
				Logging.LogNullError(nameof(callback), BotName);
				return;
			}

			if (callback.EntryType != EChatEntryType.ChatMsg) {
				return;
			}

			if ((callback.Sender == null) || string.IsNullOrEmpty(callback.Message)) {
				Logging.LogNullError(nameof(callback.Sender) + " || " + nameof(callback.Message), BotName);
				return;
			}

			await HandleMessage(callback.Sender, callback.Sender, callback.Message).ConfigureAwait(false);
		}

		private async void OnFriendMsgHistory(SteamFriends.FriendMsgHistoryCallback callback) {
			if ((callback == null) || (callback.Messages == null) || (callback.SteamID == null)) {
				Logging.LogNullError(nameof(callback) + " || " + nameof(callback.Messages) + " || " + nameof(callback.SteamID), BotName);
				return;
			}

			if ((callback.Messages.Count == 0) || !IsMaster(callback.SteamID)) {
				return;
			}

			// Get last message
			SteamFriends.FriendMsgHistoryCallback.FriendMessage lastMessage = callback.Messages[callback.Messages.Count - 1];

			// If message is read already, return
			if (!lastMessage.Unread) {
				return;
			}

			// If message is too old, return
			if (DateTime.UtcNow.Subtract(lastMessage.Timestamp).TotalMinutes > 1) {
				return;
			}

			// Handle the message
			await HandleMessage(callback.SteamID, callback.SteamID, lastMessage.Message).ConfigureAwait(false);
		}

		private void OnAccountInfo(SteamUser.AccountInfoCallback callback) {
			if (callback == null) {
				Logging.LogNullError(nameof(callback), BotName);
				return;
			}

			if (!BotConfig.FarmOffline) {
				SteamFriends.SetPersonaState(EPersonaState.Online);
			}
		}

		private void OnLoggedOff(SteamUser.LoggedOffCallback callback) {
			if (callback == null) {
				Logging.LogNullError(nameof(callback), BotName);
				return;
			}

			Logging.LogGenericInfo("Logged off of Steam: " + callback.Result, BotName);
		}

		private async void OnLoggedOn(SteamUser.LoggedOnCallback callback) {
			if (callback == null) {
				Logging.LogNullError(nameof(callback), BotName);
				return;
			}

			switch (callback.Result) {
				case EResult.AccountLogonDenied:
					AuthCode = Program.GetUserInput(Program.EUserInputType.SteamGuard, BotName);
					if (string.IsNullOrEmpty(AuthCode)) {
						Stop();
					}

					break;
				case EResult.AccountLoginDeniedNeedTwoFactor:
					if (BotDatabase.SteamGuardAccount == null) {
						TwoFactorCode = Program.GetUserInput(Program.EUserInputType.TwoFactorAuthentication, BotName);
						if (string.IsNullOrEmpty(TwoFactorCode)) {
							Stop();
						}
					} else {
						Logging.LogGenericWarning("2FA code was invalid despite of using ASF 2FA. Invalid authenticator or bad timing?", BotName);
					}

					break;
				case EResult.InvalidPassword:
					InvalidPassword = true;
					Logging.LogGenericWarning("Unable to login to Steam: " + callback.Result, BotName);
					break;
				case EResult.OK:
					Logging.LogGenericInfo("Successfully logged on!", BotName);

					PlayingBlocked = false; // If playing is really blocked, we'll be notified in a callback, old status doesn't matter

					if (callback.CellID != 0) {
						Program.GlobalDatabase.CellID = callback.CellID;
					}

					if (BotDatabase.SteamGuardAccount == null) {
						// Support and convert SDA files
						string maFilePath = Path.Combine(Program.ConfigDirectory, callback.ClientSteamID.ConvertToUInt64() + ".maFile");
						if (File.Exists(maFilePath)) {
							ImportAuthenticator(maFilePath);
						} else if ((TwoFactorCode == null) && BotConfig.UseAsfAsMobileAuthenticator) {
							LinkMobileAuthenticator();
						}
					}

					// Reset one-time-only access tokens
					AuthCode = TwoFactorCode = null;

					if (string.IsNullOrEmpty(BotConfig.SteamParentalPIN)) {
						BotConfig.SteamParentalPIN = Program.GetUserInput(Program.EUserInputType.SteamParentalPIN, BotName);
						if (string.IsNullOrEmpty(BotConfig.SteamParentalPIN)) {
							Stop();
							return;
						}
					}

					if (!ArchiWebHandler.Init(SteamClient, callback.WebAPIUserNonce, BotConfig.SteamParentalPIN)) {
						if (!await RefreshSession().ConfigureAwait(false)) {
							return;
						}
					}

					if (BotConfig.DismissInventoryNotifications) {
						ArchiWebHandler.MarkInventory().Forget();
					}

					if (BotConfig.SteamMasterClanID != 0) {
						Task.Run(async () => {
							await ArchiWebHandler.JoinGroup(BotConfig.SteamMasterClanID).ConfigureAwait(false);
							JoinMasterChat();
						}).Forget();
					}

					if (Program.GlobalConfig.Statistics) {
						Task.Run(async () => {
							await ArchiWebHandler.JoinGroup(ArchiSCFarmGroup).ConfigureAwait(false);
							SteamFriends.JoinChat(ArchiSCFarmGroup);
						}).Forget();
					}

					Task.Run(() => Trading.CheckTrades()).Forget();

					await Utilities.SleepAsync(1000).ConfigureAwait(false); // Wait a second for eventual PlayingSessionStateCallback
					CardsFarmer.StartFarming().Forget();
					break;
				case EResult.NoConnection:
				case EResult.ServiceUnavailable:
				case EResult.Timeout:
				case EResult.TryAnotherCM:
					Logging.LogGenericWarning("Unable to login to Steam: " + callback.Result, BotName);
					break;
				default: // Unexpected result, shutdown immediately
					Logging.LogGenericWarning("Unable to login to Steam: " + callback.Result, BotName);
					Stop();
					break;
			}
		}

		private void OnLoginKey(SteamUser.LoginKeyCallback callback) {
			if ((callback == null) || string.IsNullOrEmpty(callback.LoginKey)) {
				Logging.LogNullError(nameof(callback) + " || " + nameof(callback.LoginKey), BotName);
				return;
			}

			BotDatabase.LoginKey = callback.LoginKey;
			SteamUser.AcceptNewLoginKey(callback);
		}

		private void OnMachineAuth(SteamUser.UpdateMachineAuthCallback callback) {
			if (callback == null) {
				Logging.LogNullError(nameof(callback), BotName);
				return;
			}

			int fileSize;
			byte[] sentryHash;

			try {
				using (FileStream fileStream = File.Open(SentryFile, FileMode.OpenOrCreate, FileAccess.ReadWrite)) {
					fileStream.Seek(callback.Offset, SeekOrigin.Begin);
					fileStream.Write(callback.Data, 0, callback.BytesToWrite);
					fileSize = (int) fileStream.Length;

					fileStream.Seek(0, SeekOrigin.Begin);
					using (SHA1CryptoServiceProvider sha = new SHA1CryptoServiceProvider()) {
						sentryHash = sha.ComputeHash(fileStream);
					}
				}
			} catch (Exception e) {
				Logging.LogGenericException(e, BotName);
				return;
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
				SentryFileHash = sentryHash
			});
		}

		private void OnWebAPIUserNonce(SteamUser.WebAPIUserNonceCallback callback) {
			if (callback == null) {
				Logging.LogNullError(nameof(callback), BotName);
			}
		}

		private async void OnNotifications(ArchiHandler.NotificationsCallback callback) {
			if (callback == null) {
				Logging.LogNullError(nameof(callback), BotName);
				return;
			}

			if ((callback.Notifications == null) || (callback.Notifications.Count == 0)) {
				return;
			}

			bool checkTrades = false;
			bool markInventory = false;
			foreach (ArchiHandler.NotificationsCallback.ENotification notification in callback.Notifications) {
				switch (notification) {
					case ArchiHandler.NotificationsCallback.ENotification.Items:
						markInventory = true;
						break;
					case ArchiHandler.NotificationsCallback.ENotification.Trading:
						checkTrades = true;
						break;
				}
			}

			if (checkTrades) {
				Trading.CheckTrades().Forget();
			}

			if (markInventory && BotConfig.DismissInventoryNotifications) {
				await ArchiWebHandler.MarkInventory().ConfigureAwait(false);
			}
		}

		private void OnOfflineMessage(ArchiHandler.OfflineMessageCallback callback) {
			if (callback == null) {
				Logging.LogNullError(nameof(callback), BotName);
				return;
			}

			if ((callback.OfflineMessagesCount == 0) || !BotConfig.HandleOfflineMessages) {
				return;
			}

			SteamFriends.RequestOfflineMessages();
		}

		private void OnPlayingSessionState(ArchiHandler.PlayingSessionStateCallback callback) {
			if (callback == null) {
				Logging.LogNullError(nameof(callback));
				return;
			}

			if (callback.PlayingBlocked == PlayingBlocked) {
				return; // No status update, we're not interested
			}

			if (callback.PlayingBlocked) {
				PlayingBlocked = true;
				Logging.LogGenericInfo("Account is currently being used, ASF will resume farming when it's free...", BotName);
			} else {
				PlayingBlocked = false;
				Logging.LogGenericInfo("Account is no longer occupied, farming process resumed!", BotName);
				CardsFarmer.StartFarming().Forget();
			}
		}

		private void OnPurchaseResponse(ArchiHandler.PurchaseResponseCallback callback) {
			if (callback == null) {
				Logging.LogNullError(nameof(callback), BotName);
				return;
			}

			if (callback.PurchaseResult == ArchiHandler.PurchaseResponseCallback.EPurchaseResult.OK) {
				// We will restart CF module to recalculate current status and decide about new optimal approach
				CardsFarmer.RestartFarming().Forget();
			}
		}
	}
}
