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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Text;

namespace ArchiSteamFarm {
	internal sealed class Bot {
		private const ulong ArchiSCFarmGroup = 103582791440160998;
		private const ushort CallbackSleep = 500; // In miliseconds

		internal static readonly Dictionary<string, Bot> Bots = new Dictionary<string, Bot>();

		private static readonly uint LoginID = MsgClientLogon.ObfuscationMask; // This must be the same for all ASF bots and all ASF processes

		private readonly string SentryFile;
		private readonly Timer SendItemsTimer;

		internal readonly string BotName;
		internal readonly ArchiHandler ArchiHandler;
		internal readonly ArchiWebHandler ArchiWebHandler;
		internal readonly BotConfig BotConfig;
		internal readonly BotDatabase BotDatabase;
		internal readonly SteamClient SteamClient;

		private readonly CallbackManager CallbackManager;
		private readonly CardsFarmer CardsFarmer;
		private readonly SteamApps SteamApps;
		private readonly SteamFriends SteamFriends;
		private readonly SteamUser SteamUser;
		private readonly Trading Trading;

		internal bool KeepRunning { get; private set; } = false;

		private bool InvalidPassword = false;
		private bool LoggedInElsewhere = false;
		private string AuthCode, TwoFactorAuth;

		internal static string GetAnyBotName() {
			foreach (string botName in Bots.Keys) {
				return botName;
			}

			return null;
		}

		internal static async Task RefreshCMs(uint cellID) {
			bool initialized = false;
			for (byte i = 0; i < 3 && !initialized; i++) {
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
				Logging.LogGenericWarning("Failed to initialize list of CMs after 3 tries, ASF will use built-in SK2 list, it may take a while to connect");
			}
		}

		private static bool IsValidCdKey(string key) {
			if (string.IsNullOrEmpty(key)) {
				return false;
			}

			// Steam keys are offered in many formats: https://support.steampowered.com/kb_article.php?ref=7480-WUSF-3601
			// It's pointless to implement them all, so we'll just do a simple check if key is supposed to be valid
			// Every valid key, apart from Prey one has at least two dashes
			return Utilities.GetCharCountInString(key, '-') >= 2;
		}

		internal Bot(string botName) {
			if (string.IsNullOrEmpty(botName)) {
				return;
			}

			BotName = botName;

			string botPath = Path.Combine(Program.ConfigDirectory, botName);

			// CONVERSION START
			if (File.Exists(botPath + ".xml")) {
				BotConfig = BotConfig.LoadOldFormat(botPath + ".xml");
				if (BotConfig == null) {
					return;
				}

				if (BotConfig.Convert(botPath + ".json")) {
					try {
						File.Delete(botPath + ".xml");
					} catch (Exception e) {
						Logging.LogGenericException(e, botName);
						return;
					}
				}
			}
			// CONVERSION END

			BotConfig = BotConfig.Load(botPath + ".json");
			if (BotConfig == null) {
				Logging.LogGenericError("Your config for this bot instance is invalid, it won't run!", botName);
				return;
			}

			// CONVERSION START
			if (File.Exists(botPath + ".key")) {
				BotDatabase = BotDatabase.Load(botPath + ".db");
				try {
					BotDatabase.LoginKey = File.ReadAllText(botPath + ".key");
					File.Delete(botPath + ".key");
				} catch (Exception e) {
					Logging.LogGenericException(e, BotName);
				}
			}
			if (File.Exists(botPath + ".auth")) {
				BotDatabase = BotDatabase.Load(botPath + ".db");
				try {
					BotDatabase.SteamGuardAccount = JsonConvert.DeserializeObject<SteamGuardAccount>(File.ReadAllText(botPath + ".auth"));
					File.Delete(botPath + ".auth");
				} catch (Exception e) {
					Logging.LogGenericException(e, BotName);
				}
			}
			// CONVERSION END

			if (!BotConfig.Enabled) {
				return;
			}

			bool alreadyExists;
			lock (Bots) {
				alreadyExists = Bots.ContainsKey(botName);
				if (!alreadyExists) {
					Bots[botName] = this;
				}
			}

			if (alreadyExists) {
				return;
			}

			BotDatabase = BotDatabase.Load(botPath + ".db");
			SentryFile = botPath + ".bin";

			// Support and convert SDA files
			if (BotDatabase.SteamGuardAccount == null && File.Exists(botPath + ".maFile")) {
				Logging.LogGenericInfo("Converting SDA .maFile into ASF format...", botName);
				try {
					BotDatabase.SteamGuardAccount = JsonConvert.DeserializeObject<SteamGuardAccount>(File.ReadAllText(botPath + ".maFile"));
					File.Delete(botPath + ".maFile");
					Logging.LogGenericInfo("Success!", botName);
				} catch (Exception e) {
					Logging.LogGenericException(e, botName);
				}
			}

			// Initialize
			SteamClient = new SteamClient();

			if (Program.GlobalConfig.Debug && !Debugging.NetHookAlreadyInitialized) {
				try {
					if (Directory.Exists(Program.DebugDirectory)) {
						Directory.Delete(Program.DebugDirectory, true);
					}
					Directory.CreateDirectory(Program.DebugDirectory);
					SteamClient.DebugNetworkListener = new NetHookNetworkListener(Program.DebugDirectory);
					Debugging.NetHookAlreadyInitialized = true;
				} catch (Exception e) {
					Logging.LogGenericException(e, botName);
				}
			}

			ArchiHandler = new ArchiHandler();
			SteamClient.AddHandler(ArchiHandler);

			CallbackManager = new CallbackManager(SteamClient);
			CallbackManager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
			CallbackManager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);

			SteamApps = SteamClient.GetHandler<SteamApps>();
			CallbackManager.Subscribe<SteamApps.FreeLicenseCallback>(OnFreeLicense);

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

			CallbackManager.Subscribe<ArchiHandler.NotificationsCallback>(OnNotifications);
			CallbackManager.Subscribe<ArchiHandler.OfflineMessageCallback>(OnOfflineMessage);
			CallbackManager.Subscribe<ArchiHandler.PurchaseResponseCallback>(OnPurchaseResponse);

			ArchiWebHandler = new ArchiWebHandler(this);
			CardsFarmer = new CardsFarmer(this);
			Trading = new Trading(this);

			if (BotConfig.SendTradePeriod > 0 && SendItemsTimer == null) {
				SendItemsTimer = new Timer(
					async e => await ResponseSendTrade().ConfigureAwait(false),
					null,
					TimeSpan.FromHours(BotConfig.SendTradePeriod), // Delay
					TimeSpan.FromHours(BotConfig.SendTradePeriod) // Period
				);
			}

			if (!BotConfig.StartOnLaunch) {
				return;
			}

			// Start
			Start().Wait();
		}

		internal async Task AcceptAllConfirmations() {
			if (BotDatabase.SteamGuardAccount == null) {
				return;
			}

			await BotDatabase.SteamGuardAccount.RefreshSessionAsync().ConfigureAwait(false);

			try {
				foreach (Confirmation confirmation in await BotDatabase.SteamGuardAccount.FetchConfirmationsAsync().ConfigureAwait(false)) {
					if (BotDatabase.SteamGuardAccount.AcceptConfirmation(confirmation)) {
						Logging.LogGenericInfo("Accepting confirmation: Success!", BotName);
					} else {
						Logging.LogGenericWarning("Accepting confirmation: Failed!", BotName);
					}
				}
			} catch (SteamGuardAccount.WGTokenInvalidException) {
				Logging.LogGenericWarning("Accepting confirmation: Failed!", BotName);
				Logging.LogGenericWarning("Confirmation could not be accepted because of invalid token exception", BotName);
				Logging.LogGenericWarning("If issue persists, consider removing and readding ASF 2FA", BotName);
			}
		}

		internal void ResetGamesPlayed() {
			if (BotConfig.GamesPlayedWhileIdle.Contains(0)) {
				ArchiHandler.PlayGames(0);
			} else {
				ArchiHandler.PlayGames(BotConfig.GamesPlayedWhileIdle);
			}
		}

		internal async Task Restart() {
			Stop();
			await Utilities.SleepAsync(500).ConfigureAwait(false);
			await Start().ConfigureAwait(false);
		}

		internal async Task OnFarmingFinished(bool farmedSomething) {
			if (farmedSomething && BotConfig.SendOnFarmingFinished) {
				await ResponseSendTrade().ConfigureAwait(false);
			}
			if (BotConfig.ShutdownOnFarmingFinished) {
				Shutdown();
			}
		}

		internal async Task<string> HandleMessage(string message) {
			if (string.IsNullOrEmpty(message)) {
				return null;
			}

			if (!message.StartsWith("!")) {
				return await ResponseRedeem(BotName, message, true).ConfigureAwait(false);
			}

			if (!message.Contains(" ")) {
				switch (message) {
					case "!2fa":
						return Response2FA();
					case "!2faoff":
						return Response2FAOff();
					case "!exit":
						Program.Exit();
						return null;
					case "!rejoinchat":
						return ResponseRejoinChat();
					case "!restart":
						Program.Restart();
						return "Done";
					case "!status":
						return ResponseStatus();
					case "!statusall":
						return ResponseStatusAll();
					case "!stop":
						return ResponseStop();
					case "!loot":
						return await ResponseSendTrade().ConfigureAwait(false);
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
					case "!addlicense":
						if (args.Length > 2) {
							return await ResponseAddLicense(args[1], args[2]).ConfigureAwait(false);
						} else {
							return await ResponseAddLicense(BotName, args[1]).ConfigureAwait(false);
						}
					case "!play":
						if (args.Length > 2) {
							return await ResponsePlay(args[1], args[2]).ConfigureAwait(false);
						} else {
							return await ResponsePlay(BotName, args[1]).ConfigureAwait(false);
						}
					case "!redeem":
						if (args.Length > 2) {
							return await ResponseRedeem(args[1], args[2], false).ConfigureAwait(false);
						} else {
							return await ResponseRedeem(BotName, args[1], false).ConfigureAwait(false);
						}
					case "!start":
						return await ResponseStart(args[1]).ConfigureAwait(false);
					case "!stop":
						return ResponseStop(args[1]);
					case "!status":
						return ResponseStatus(args[1]);
					case "!loot":
						return await ResponseSendTrade(args[1]).ConfigureAwait(false);
					default:
						return "Unrecognized command: " + args[0];
				}
			}
		}

		private async Task Start() {
			if (SteamClient.IsConnected) {
				return;
			}

			if (!KeepRunning) {
				KeepRunning = true;
				Task.Run(() => HandleCallbacks()).Forget();
			}

			Logging.LogGenericInfo("Starting...", BotName);

			// 2FA tokens are expiring soon, use limiter only when we don't have any pending
			if (TwoFactorAuth == null) {
				await Program.LimitSteamRequestsAsync().ConfigureAwait(false);
			}

			SteamClient.Connect();
		}

		private void Stop() {
			if (!SteamClient.IsConnected) {
				return;
			}

			Logging.LogGenericInfo("Stopping...", BotName);

			SteamClient.Disconnect();
		}

		private void Shutdown() {
			KeepRunning = false;
			Stop();
			Program.OnBotShutdown();
		}

		private string ResponseStatus() {
			if (CardsFarmer.CurrentGamesFarming.Count > 0) {
				return "Bot " + BotName + " is currently farming appIDs: " + string.Join(", ", CardsFarmer.CurrentGamesFarming) + " and has a total of " + CardsFarmer.GamesToFarm.Count + " games left to farm.";
			} else {
				return "Bot " + BotName + " is currently not farming anything.";
			}
		}

		private static string ResponseStatus(string botName) {
			if (string.IsNullOrEmpty(botName)) {
				return null;
			}

			Bot bot;
			if (!Bots.TryGetValue(botName, out bot)) {
				return "Couldn't find any bot named " + botName + "!";
			}

			return bot.ResponseStatus();
		}

		private static string ResponseStatusAll() {
			StringBuilder result = new StringBuilder(Environment.NewLine);

			int totalBotsCount = Bots.Count;
			int runningBotsCount = 0;

			foreach (Bot bot in Bots.Values) {
				result.Append(bot.ResponseStatus() + Environment.NewLine);
				if (bot.KeepRunning) {
					runningBotsCount++;
				}
			}

			result.Append("There are " + totalBotsCount + " bots initialized and " + runningBotsCount + " of them are currently running.");
			return result.ToString();
		}

		private async Task<string> ResponseSendTrade() {
			if (BotConfig.SteamMasterID == 0) {
				return "Trade couldn't be send because SteamMasterID is not defined!";
			}

			await Trading.LimitInventoryRequestsAsync().ConfigureAwait(false);
			List<SteamItem> inventory = await ArchiWebHandler.GetMyTradableInventory().ConfigureAwait(false);

			if (inventory == null || inventory.Count == 0) {
				return "Nothing to send, inventory seems empty!";
			}

			if (await ArchiWebHandler.SendTradeOffer(inventory, BotConfig.SteamMasterID, BotConfig.SteamTradeToken).ConfigureAwait(false)) {
				await AcceptAllConfirmations().ConfigureAwait(false);
				return "Trade offer sent successfully!";
			} else {
				return "Trade offer failed due to error!";
			}
		}

		private static async Task<string> ResponseSendTrade(string botName) {
			if (string.IsNullOrEmpty(botName)) {
				return null;
			}

			Bot bot;
			if (!Bots.TryGetValue(botName, out bot)) {
				return "Couldn't find any bot named " + botName + "!";
			}

			return await bot.ResponseSendTrade().ConfigureAwait(false);
		}

		private string Response2FA() {
			if (BotDatabase.SteamGuardAccount == null) {
				return "That bot doesn't have ASF 2FA enabled!";
			}

			long timeLeft = 30 - TimeAligner.GetSteamTime() % 30;
			return "2FA Token: " + BotDatabase.SteamGuardAccount.GenerateSteamGuardCode() + " (expires in " + timeLeft + " seconds)";
		}

		private static string Response2FA(string botName) {
			if (string.IsNullOrEmpty(botName)) {
				return null;
			}

			Bot bot;
			if (!Bots.TryGetValue(botName, out bot)) {
				return "Couldn't find any bot named " + botName + "!";
			}

			return bot.Response2FA();
		}

		private string Response2FAOff() {
			if (BotDatabase.SteamGuardAccount == null) {
				return "That bot doesn't have ASF 2FA enabled!";
			}

			if (DelinkMobileAuthenticator()) {
				return "Done! Bot is no longer using ASF 2FA";
			} else {
				return "Something went wrong during delinking mobile authenticator!";
			}
		}

		private static string Response2FAOff(string botName) {
			if (string.IsNullOrEmpty(botName)) {
				return null;
			}

			Bot bot;
			if (!Bots.TryGetValue(botName, out bot)) {
				return "Couldn't find any bot named " + botName + "!";
			}

			return bot.Response2FAOff();
		}

		private async Task<string> ResponseRedeem(string message, bool validate) {
			if (string.IsNullOrEmpty(message)) {
				return null;
			}

			StringBuilder response = new StringBuilder();
			using (StringReader reader = new StringReader(message)) {
				string key = reader.ReadLine();
				IEnumerator<Bot> iterator = Bots.Values.GetEnumerator();
				Bot currentBot = this;
				while (key != null) {
					if (currentBot == null) {
						break;
					}

					if (validate && !IsValidCdKey(key)) {
						key = reader.ReadLine();
						continue;
					}

					ArchiHandler.PurchaseResponseCallback result;
					try {
						result = await currentBot.ArchiHandler.RedeemKey(key).ConfigureAwait(false);
					} catch (Exception e) {
						Logging.LogGenericException(e, currentBot.BotName);
						break;
					}

					if (result == null) {
						break;
					}

					var purchaseResult = result.PurchaseResult;
					var items = result.Items;

					switch (purchaseResult) {
						case ArchiHandler.PurchaseResponseCallback.EPurchaseResult.AlreadyOwned:
						case ArchiHandler.PurchaseResponseCallback.EPurchaseResult.BaseGameRequired:
						case ArchiHandler.PurchaseResponseCallback.EPurchaseResult.OnCooldown:
						case ArchiHandler.PurchaseResponseCallback.EPurchaseResult.RegionLocked:
							response.Append(Environment.NewLine + "<" + currentBot.BotName + "> Key: " + key + " | Status: " + purchaseResult + " | Items: " + string.Join("", items));
							if (BotConfig.DistributeKeys) {
								do {
									if (iterator.MoveNext()) {
										currentBot = iterator.Current;
									} else {
										currentBot = null;
									}
								} while (currentBot == this);

								if (!BotConfig.ForwardKeysToOtherBots) {
									key = reader.ReadLine();
								}
								break;
							}

							if (!BotConfig.ForwardKeysToOtherBots) {
								key = reader.ReadLine();
								break;
							}

							bool alreadyHandled = false;
							foreach (Bot bot in Bots.Values) {
								if (alreadyHandled) {
									break;
								}

								if (bot == this) {
									continue;
								}

								ArchiHandler.PurchaseResponseCallback otherResult;
								try {
									otherResult = await bot.ArchiHandler.RedeemKey(key).ConfigureAwait(false);
								} catch (Exception e) {
									Logging.LogGenericException(e, bot.BotName);
									break; // We're done with this key
								}

								if (otherResult == null) {
									break; // We're done with this key
								}

								var otherPurchaseResult = otherResult.PurchaseResult;
								var otherItems = otherResult.Items;

								switch (otherPurchaseResult) {
									case ArchiHandler.PurchaseResponseCallback.EPurchaseResult.OK:
										alreadyHandled = true; // We're done with this key
										response.Append(Environment.NewLine + "<" + bot.BotName + "> Key: " + key + " | Status: " + otherPurchaseResult + " | Items: " + string.Join("", otherItems));
										break;
									case ArchiHandler.PurchaseResponseCallback.EPurchaseResult.DuplicatedKey:
									case ArchiHandler.PurchaseResponseCallback.EPurchaseResult.InvalidKey:
										alreadyHandled = true; // This key doesn't work, don't try to redeem it anymore
										response.Append(Environment.NewLine + "<" + bot.BotName + "> Key: " + key + " | Status: " + otherPurchaseResult + " | Items: " + string.Join("", otherItems));
										break;
									default:
										response.Append(Environment.NewLine + "<" + bot.BotName + "> Key: " + key + " | Status: " + otherPurchaseResult + " | Items: " + string.Join("", otherItems));
										break;
								}
							}
							key = reader.ReadLine();
							break;
						case ArchiHandler.PurchaseResponseCallback.EPurchaseResult.OK:
							response.Append(Environment.NewLine + "<" + currentBot.BotName + "> Key: " + key + " | Status: " + purchaseResult + " | Items: " + string.Join("", items));
							if (BotConfig.DistributeKeys) {
								do {
									if (iterator.MoveNext()) {
										currentBot = iterator.Current;
									} else {
										currentBot = null;
									}
								} while (currentBot == this);
							}
							key = reader.ReadLine();
							break;
						case ArchiHandler.PurchaseResponseCallback.EPurchaseResult.DuplicatedKey:
						case ArchiHandler.PurchaseResponseCallback.EPurchaseResult.InvalidKey:
							response.Append(Environment.NewLine + "<" + currentBot.BotName + "> Key: " + key + " | Status: " + purchaseResult + " | Items: " + string.Join("", items));
							if (BotConfig.DistributeKeys && !BotConfig.ForwardKeysToOtherBots) {
								do {
									if (iterator.MoveNext()) {
										currentBot = iterator.Current;
									} else {
										currentBot = null;
									}
								} while (currentBot == this);
							}
							key = reader.ReadLine();
							break;
					}
				}
			}

			if (response.Length == 0) {
				return null;
			}

			return response.ToString();
		}

		private static async Task<string> ResponseRedeem(string botName, string message, bool validate) {
			if (string.IsNullOrEmpty(botName) || string.IsNullOrEmpty(message)) {
				return null;
			}

			Bot bot;
			if (!Bots.TryGetValue(botName, out bot)) {
				return "Couldn't find any bot named " + botName + "!";
			}

			return await bot.ResponseRedeem(message, validate).ConfigureAwait(false);
		}

		private static string ResponseRejoinChat() {
			foreach (Bot bot in Bots.Values) {
				bot.JoinMasterChat();
			}

			return "Done!";
		}

		private async Task<string> ResponseAddLicense(HashSet<uint> gameIDs) {
			if (gameIDs == null || gameIDs.Count == 0) {
				return null;
			}

			StringBuilder result = new StringBuilder();
			foreach (uint gameID in gameIDs) {
				SteamApps.FreeLicenseCallback callback;
				try {
					callback = await SteamApps.RequestFreeLicense(gameID);
				} catch (Exception e) {
					Logging.LogGenericException(e, BotName);
					continue;
				}

				result.AppendLine("Result: " + callback.Result + " | Granted apps: " + string.Join(", ", callback.GrantedApps) + " " + string.Join(", ", callback.GrantedPackages));
			}

			return result.ToString();
		}

		private static async Task<string> ResponseAddLicense(string botName, string games) {
			if (string.IsNullOrEmpty(botName) || string.IsNullOrEmpty(games)) {
				return null;
			}

			Bot bot;
			if (!Bots.TryGetValue(botName, out bot)) {
				return "Couldn't find any bot named " + botName + "!";
			}

			string[] gameIDs = games.Split(',');

			HashSet<uint> gamesToRedeem = new HashSet<uint>();
			foreach (string game in gameIDs) {
				uint gameID;
				if (!uint.TryParse(game, out gameID)) {
					continue;
				}
				gamesToRedeem.Add(gameID);
			}

			if (gamesToRedeem.Count == 0) {
				return "Couldn't parse any games given!";
			}

			return await bot.ResponseAddLicense(gamesToRedeem).ConfigureAwait(false);
		}

		private async Task<string> ResponsePlay(HashSet<uint> gameIDs) {
			if (gameIDs == null || gameIDs.Count == 0) {
				return null;
			}

			if (gameIDs.Contains(0)) {
				if (await CardsFarmer.SwitchToManualMode(false).ConfigureAwait(false)) {
					ResetGamesPlayed();
				}
			} else {
				await CardsFarmer.SwitchToManualMode(true).ConfigureAwait(false);
				ArchiHandler.PlayGames(gameIDs);
			}

			return "Done!";
		}

		private static async Task<string> ResponsePlay(string botName, string games) {
			if (string.IsNullOrEmpty(botName) || string.IsNullOrEmpty(games)) {
				return null;
			}

			Bot bot;
			if (!Bots.TryGetValue(botName, out bot)) {
				return "Couldn't find any bot named " + botName + "!";
			}

			string[] gameIDs = games.Split(',');

			HashSet<uint> gamesToPlay = new HashSet<uint>();
			foreach (string game in gameIDs) {
				uint gameID;
				if (!uint.TryParse(game, out gameID)) {
					continue;
				}
				gamesToPlay.Add(gameID);
			}

			if (gamesToPlay.Count == 0) {
				return "Couldn't parse any games given!";
			}

			return await bot.ResponsePlay(gamesToPlay).ConfigureAwait(false);
		}

		private async Task<string> ResponseStart() {
			if (KeepRunning) {
				return "That bot instance is already running!";
			}

			await Start().ConfigureAwait(false);
			return "Done!";
		}

		private static async Task<string> ResponseStart(string botName) {
			if (string.IsNullOrEmpty(botName)) {
				return null;
			}

			Bot bot;
			if (!Bots.TryGetValue(botName, out bot)) {
				return "Couldn't find any bot named " + botName + "!";
			}

			return await bot.ResponseStart().ConfigureAwait(false);
		}

		private string ResponseStop() {
			if (!KeepRunning) {
				return "That bot instance is already inactive!";
			}

			Shutdown();
			return "Done!";
		}

		private static string ResponseStop(string botName) {
			if (string.IsNullOrEmpty(botName)) {
				return null;
			}

			Bot bot;
			if (!Bots.TryGetValue(botName, out bot)) {
				return "Couldn't find any bot named " + botName + "!";
			}

			return bot.ResponseStop();
		}

		private void HandleCallbacks() {
			TimeSpan timeSpan = TimeSpan.FromMilliseconds(CallbackSleep);
			while (KeepRunning || SteamClient.IsConnected) {
				CallbackManager.RunWaitCallbacks(timeSpan);
			}
		}

		private async Task HandleMessage(ulong steamID, string message) {
			if (steamID == 0 || string.IsNullOrEmpty(message)) {
				return;
			}

			SendMessage(steamID, await HandleMessage(message).ConfigureAwait(false));
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

		private bool LinkMobileAuthenticator() {
			if (BotDatabase.SteamGuardAccount != null) {
				return false;
			}

			Logging.LogGenericInfo("Linking new ASF MobileAuthenticator...", BotName);
			UserLogin userLogin = new UserLogin(BotConfig.SteamLogin, BotConfig.SteamPassword);
			LoginResult loginResult;
			while ((loginResult = userLogin.DoLogin()) != LoginResult.LoginOkay) {
				switch (loginResult) {
					case LoginResult.NeedEmail:
						userLogin.EmailCode = Program.GetUserInput(BotName, Program.EUserInputType.SteamGuard);
						break;
					default:
						Logging.LogGenericError("Unhandled situation: " + loginResult, BotName);
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
						Logging.LogGenericError("Unhandled situation: " + linkResult, BotName);
						return false;
				}
			}

			BotDatabase.SteamGuardAccount = authenticatorLinker.LinkedAccount;

			AuthenticatorLinker.FinalizeResult finalizeResult = authenticatorLinker.FinalizeAddAuthenticator(Program.GetUserInput(BotName, Program.EUserInputType.SMS));
			if (finalizeResult != AuthenticatorLinker.FinalizeResult.Success) {
				Logging.LogGenericError("Unhandled situation: " + finalizeResult, BotName);
				DelinkMobileAuthenticator();
				return false;
			}

			Logging.LogGenericInfo("Successfully linked ASF as new mobile authenticator for this account!", BotName);
			Program.GetUserInput(BotName, Program.EUserInputType.RevocationCode, BotDatabase.SteamGuardAccount.RevocationCode);
			return true;
		}

		private bool DelinkMobileAuthenticator() {
			if (BotDatabase.SteamGuardAccount == null) {
				return false;
			}

			bool result = BotDatabase.SteamGuardAccount.DeactivateAuthenticator();

			if (result) {
				BotDatabase.SteamGuardAccount = null;
			}

			return result;
		}

		private void JoinMasterChat() {
			if (BotConfig.SteamMasterClanID == 0) {
				return;
			}

			SteamFriends.JoinChat(BotConfig.SteamMasterClanID);
		}

		private void OnConnected(SteamClient.ConnectedCallback callback) {
			if (callback == null) {
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

			if (string.IsNullOrEmpty(BotConfig.SteamLogin)) {
				BotConfig.SteamLogin = Program.GetUserInput(BotName, Program.EUserInputType.Login);
			}

			if (string.IsNullOrEmpty(BotConfig.SteamPassword) && string.IsNullOrEmpty(BotDatabase.LoginKey)) {
				BotConfig.SteamPassword = Program.GetUserInput(BotName, Program.EUserInputType.Password);
			}

			SteamUser.LogOn(new SteamUser.LogOnDetails {
				Username = BotConfig.SteamLogin,
				Password = BotConfig.SteamPassword,
				AuthCode = AuthCode,
				LoginID = LoginID,
				LoginKey = BotDatabase.LoginKey,
				TwoFactorCode = TwoFactorAuth,
				SentryFileHash = sentryHash,
				ShouldRememberPassword = true
			});
		}

		private async void OnDisconnected(SteamClient.DisconnectedCallback callback) {
			if (callback == null) {
				return;
			}

			Logging.LogGenericInfo("Disconnected from Steam!", BotName);
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
				if (!string.IsNullOrEmpty(BotDatabase.LoginKey)) { // InvalidPassword means usually that login key has expired, if we used it
					BotDatabase.LoginKey = null;
					Logging.LogGenericInfo("Removed expired login key", BotName);
				} else { // If we didn't use login key, InvalidPassword usually means we got captcha or other network-based throttling
					Logging.LogGenericInfo("Will retry after 25 minutes...", BotName);
					await Utilities.SleepAsync(25 * 60 * 1000).ConfigureAwait(false); // Captcha disappears after around 20 minutes, so we make it 25
				}
			} else if (LoggedInElsewhere) {
				LoggedInElsewhere = false;
				Logging.LogGenericWarning("Account is being used elsewhere, will try reconnecting in 30 minutes...", BotName);
				await Utilities.SleepAsync(30 * 60 * 1000).ConfigureAwait(false);
			}

			Logging.LogGenericInfo("Reconnecting...", BotName);

			// 2FA tokens are expiring soon, use limiter only when we don't have any pending
			if (TwoFactorAuth == null) {
				await Program.LimitSteamRequestsAsync().ConfigureAwait(false);
			}

			SteamClient.Connect();
		}

		private void OnFreeLicense(SteamApps.FreeLicenseCallback callback) {
			if (callback == null) {
				return;
			}
		}

		private void OnChatInvite(SteamFriends.ChatInviteCallback callback) {
			if (callback == null) {
				return;
			}

			if (callback.PatronID != BotConfig.SteamMasterID) {
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

			if (callback.ChatterID != BotConfig.SteamMasterID) {
				return;
			}

			switch (callback.Message) {
				case "!leave":
					SteamFriends.LeaveChat(callback.ChatRoomID);
					break;
				default:
					await HandleMessage(callback.ChatRoomID, callback.Message).ConfigureAwait(false);
					break;
			}
		}

		private void OnFriendsList(SteamFriends.FriendsListCallback callback) {
			if (callback == null) {
				return;
			}

			foreach (var friend in callback.FriendList) {
				if (friend.Relationship != EFriendRelationship.RequestRecipient) {
					continue;
				}

				switch (friend.SteamID.AccountType) {
					case EAccountType.Clan:
						// TODO: Accept clan invites from master?
						break;
					default:
						if (friend.SteamID == BotConfig.SteamMasterID) {
							SteamFriends.AddFriend(friend.SteamID);
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

			if (callback.Sender != BotConfig.SteamMasterID) {
				return;
			}

			await HandleMessage(callback.Sender, callback.Message).ConfigureAwait(false);
		}

		private async void OnFriendMsgHistory(SteamFriends.FriendMsgHistoryCallback callback) {
			if (callback == null) {
				return;
			}

			if (callback.Result != EResult.OK) {
				return;
			}

			if (callback.SteamID != BotConfig.SteamMasterID) {
				return;
			}

			if (callback.Messages.Count == 0) {
				return;
			}

			// Get last message
			var lastMessage = callback.Messages[callback.Messages.Count - 1];

			// If message is read already, return
			if (!lastMessage.Unread) {
				return;
			}

			// If message is too old, return
			if (DateTime.UtcNow.Subtract(lastMessage.Timestamp).TotalMinutes > 1) {
				return;
			}

			// Handle the message
			await HandleMessage(callback.SteamID, lastMessage.Message).ConfigureAwait(false);
		}

		private void OnAccountInfo(SteamUser.AccountInfoCallback callback) {
			if (callback == null) {
				return;
			}

			if (!BotConfig.FarmOffline) {
				SteamFriends.SetPersonaState(EPersonaState.Online);
			}
		}

		private void OnLoggedOff(SteamUser.LoggedOffCallback callback) {
			if (callback == null) {
				return;
			}

			Logging.LogGenericInfo("Logged off of Steam: " + callback.Result, BotName);

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

			switch (callback.Result) {
				case EResult.AccountLogonDenied:
					AuthCode = Program.GetUserInput(BotConfig.SteamLogin, Program.EUserInputType.SteamGuard);
					break;
				case EResult.AccountLoginDeniedNeedTwoFactor:
					if (BotDatabase.SteamGuardAccount == null) {
						TwoFactorAuth = Program.GetUserInput(BotConfig.SteamLogin, Program.EUserInputType.TwoFactorAuthentication);
					} else {
						TwoFactorAuth = BotDatabase.SteamGuardAccount.GenerateSteamGuardCode();
					}
					break;
				case EResult.InvalidPassword:
					InvalidPassword = true;
					Logging.LogGenericWarning("Unable to login to Steam: " + callback.Result, BotName);
					break;
				case EResult.OK:
					Logging.LogGenericInfo("Successfully logged on!", BotName);

					if (callback.CellID != 0) {
						Program.GlobalDatabase.CellID = callback.CellID;
					}

					// Support and convert SDA files
					string maFilePath = Path.Combine(Program.ConfigDirectory, callback.ClientSteamID.ConvertToUInt64() + ".maFile");
					if (BotDatabase.SteamGuardAccount == null && File.Exists(maFilePath)) {
						Logging.LogGenericInfo("Converting SDA .maFile into ASF format...", BotName);
						try {
							BotDatabase.SteamGuardAccount = JsonConvert.DeserializeObject<SteamGuardAccount>(File.ReadAllText(maFilePath));
							File.Delete(maFilePath);
							Logging.LogGenericInfo("Success!", BotName);
						} catch (Exception e) {
							Logging.LogGenericException(e, BotName);
						}
					}

					if (BotConfig.UseAsfAsMobileAuthenticator && TwoFactorAuth == null && BotDatabase.SteamGuardAccount == null) {
						LinkMobileAuthenticator();
					}

					// Reset one-time-only access tokens
					AuthCode = null;
					TwoFactorAuth = null;

					ResetGamesPlayed();

					if (string.IsNullOrEmpty(BotConfig.SteamParentalPIN)) {
						BotConfig.SteamParentalPIN = Program.GetUserInput(BotName, Program.EUserInputType.SteamParentalPIN);
					}

					if (!await ArchiWebHandler.Init(SteamClient, callback.WebAPIUserNonce, BotConfig.SteamParentalPIN).ConfigureAwait(false)) {
						await Restart().ConfigureAwait(false);
						return;
					}

					if (BotConfig.SteamMasterClanID != 0) {
						await ArchiWebHandler.JoinClan(BotConfig.SteamMasterClanID).ConfigureAwait(false);
						JoinMasterChat();
					}

					if (BotConfig.Statistics) {
						await ArchiWebHandler.JoinClan(ArchiSCFarmGroup).ConfigureAwait(false);
						SteamFriends.JoinChat(ArchiSCFarmGroup);
					}

					Trading.CheckTrades();

					Task.Run(async () => await CardsFarmer.StartFarming().ConfigureAwait(false)).Forget();
					break;
				case EResult.NoConnection:
				case EResult.ServiceUnavailable:
				case EResult.Timeout:
				case EResult.TryAnotherCM:
					Logging.LogGenericWarning("Unable to login to Steam: " + callback.Result, BotName);
					break;
				default: // Unexpected result, shutdown immediately
					Logging.LogGenericWarning("Unable to login to Steam: " + callback.Result, BotName);
					Shutdown();
					break;
			}
		}

		private void OnLoginKey(SteamUser.LoginKeyCallback callback) {
			if (callback == null) {
				return;
			}

			BotDatabase.LoginKey = callback.LoginKey;
			SteamUser.AcceptNewLoginKey(callback);
		}

		private void OnMachineAuth(SteamUser.UpdateMachineAuthCallback callback) {
			if (callback == null) {
				return;
			}

			try {
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
			} catch (Exception e) {
				Logging.LogGenericException(e, BotName);
			}
		}

		private async void OnNotifications(ArchiHandler.NotificationsCallback callback) {
			if (callback == null || callback.Notifications == null) {
				return;
			}

			bool checkTrades = false;
			bool markInventory = false;
			foreach (var notification in callback.Notifications) {
				switch (notification.NotificationType) {
					case ArchiHandler.NotificationsCallback.Notification.ENotificationType.Items:
						markInventory = true;
						break;
					case ArchiHandler.NotificationsCallback.Notification.ENotificationType.Trading:
						checkTrades = true;
						break;
				}
			}

			if (checkTrades) {
				Trading.CheckTrades();
			}

			if (markInventory && BotConfig.DismissInventoryNotifications) {
				await ArchiWebHandler.MarkInventory().ConfigureAwait(false);
			}
		}

		private void OnOfflineMessage(ArchiHandler.OfflineMessageCallback callback) {
			if (callback == null) {
				return;
			}

			if (!BotConfig.HandleOfflineMessages) {
				return;
			}

			SteamFriends.RequestOfflineMessages();
		}

		private async void OnPurchaseResponse(ArchiHandler.PurchaseResponseCallback callback) {
			if (callback == null) {
				return;
			}

			if (callback.PurchaseResult == ArchiHandler.PurchaseResponseCallback.EPurchaseResult.OK) {
				// We will restart CF module to recalculate current status and decide about new optimal approach
				await CardsFarmer.RestartFarming().ConfigureAwait(false);
			}
		}
	}
}
