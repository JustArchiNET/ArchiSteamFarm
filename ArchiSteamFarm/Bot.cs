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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.JSON;
using ArchiSteamFarm.Localization;
using Newtonsoft.Json;
using SteamKit2;
using SteamKit2.Internal;

namespace ArchiSteamFarm {
	internal sealed class Bot : IDisposable {
		internal const byte MinPlayingBlockedTTL = 60; // Delay in seconds added when account was occupied during our disconnect, to not disconnect other Steam client session too soon

		private const ushort CallbackSleep = 500; // In miliseconds
		private const byte FamilySharingInactivityMinutes = 5;
		private const byte LoginCooldownInMinutes = 25; // Captcha disappears after around 20 minutes, so we make it 25
		private const uint LoginID = GlobalConfig.DefaultWCFPort; // This must be the same for all ASF bots and all ASF processes
		private const ushort MaxSteamMessageLength = 2048;
		private const byte MaxTwoFactorCodeFailures = 3;
		private const byte MinHeartBeatTTL = GlobalConfig.DefaultConnectionTimeout; // Assume client is responsive for at least that amount of seconds

		internal static readonly ConcurrentDictionary<string, Bot> Bots = new ConcurrentDictionary<string, Bot>();

		private static readonly SemaphoreSlim GiftsSemaphore = new SemaphoreSlim(1);
		private static readonly SemaphoreSlim LoginSemaphore = new SemaphoreSlim(1);

		internal readonly ArchiLogger ArchiLogger;
		internal readonly ArchiWebHandler ArchiWebHandler;
		internal readonly string BotName;

		internal bool CanReceiveSteamCards => !IsAccountLimited && !IsAccountLocked;
		internal bool HasMobileAuthenticator => BotDatabase?.MobileAuthenticator != null;
		internal bool IsConnectedAndLoggedOn => SteamID != 0;
		internal bool IsPlayingPossible => !PlayingBlocked && (LibraryLockedBySteamID == 0);

		[JsonProperty]
		internal ulong SteamID => SteamClient?.SteamID ?? 0;

		private readonly ArchiHandler ArchiHandler;
		private readonly BotDatabase BotDatabase;
		private readonly CallbackManager CallbackManager;
		private readonly SemaphoreSlim CallbackSemaphore = new SemaphoreSlim(1);

		[JsonProperty]
		private readonly CardsFarmer CardsFarmer;

		private readonly ConcurrentHashSet<ulong> HandledGifts = new ConcurrentHashSet<ulong>();
		private readonly Timer HeartBeatTimer;
		private readonly SemaphoreSlim InitializationSemaphore = new SemaphoreSlim(1);
		private readonly ConcurrentHashSet<uint> OwnedPackageIDs = new ConcurrentHashSet<uint>();

		private readonly Statistics Statistics;
		private readonly SteamApps SteamApps;
		private readonly SteamClient SteamClient;
		private readonly ConcurrentHashSet<ulong> SteamFamilySharingIDs = new ConcurrentHashSet<ulong>();
		private readonly SteamFriends SteamFriends;
		//private readonly SteamSaleEvent SteamSaleEvent;
		private readonly SteamUser SteamUser;
		private readonly Trading Trading;

		private string BotPath => Path.Combine(SharedInfo.ConfigDirectory, BotName);
		private bool IsAccountLimited => AccountFlags.HasFlag(EAccountFlags.LimitedUser) || AccountFlags.HasFlag(EAccountFlags.LimitedUserForce);
		private bool IsAccountLocked => AccountFlags.HasFlag(EAccountFlags.Lockdown);
		private string SentryFile => BotPath + ".bin";

		[JsonProperty]
		internal BotConfig BotConfig { get; private set; }

		[JsonProperty]
		internal bool KeepRunning { get; private set; }

		internal bool PlayingWasBlocked { get; private set; }

		[JsonProperty]
		private EAccountFlags AccountFlags;

		private string AuthCode;
		private Timer ConnectionFailureTimer;
		private string DeviceID;
		private Timer FamilySharingInactivityTimer;
		private bool FirstTradeSent;
		private byte HeartBeatFailures;
		private EResult LastLogOnResult;
		private ulong LibraryLockedBySteamID;
		private bool LootingAllowed = true;
		private bool PlayingBlocked;
		private Timer PlayingWasBlockedTimer;
		private Timer SendItemsTimer;
		private bool SkipFirstShutdown;
		private string TwoFactorCode;
		private byte TwoFactorCodeFailures;

		private Bot(string botName) {
			if (string.IsNullOrEmpty(botName)) {
				throw new ArgumentNullException(nameof(botName));
			}

			if (Bots.ContainsKey(botName)) {
				throw new ArgumentException(string.Format(Strings.ErrorIsInvalid, nameof(botName)));
			}

			BotName = botName;
			ArchiLogger = new ArchiLogger(botName);

			string botConfigFile = BotPath + ".json";

			BotConfig = BotConfig.Load(botConfigFile);
			if (BotConfig == null) {
				ArchiLogger.LogGenericError(string.Format(Strings.ErrorBotConfigInvalid, botConfigFile));
				return;
			}

			string botDatabaseFile = BotPath + ".db";

			BotDatabase = BotDatabase.Load(botDatabaseFile);
			if (BotDatabase == null) {
				ArchiLogger.LogGenericError(string.Format(Strings.ErrorDatabaseInvalid, botDatabaseFile));
				return;
			}

			// Register bot as available for ASF
			if (!Bots.TryAdd(botName, this)) {
				throw new ArgumentException(string.Format(Strings.ErrorIsInvalid, nameof(botName)));
			}

			if (HasMobileAuthenticator) {
				BotDatabase.MobileAuthenticator.Init(this);
			}

			// Initialize
			SteamClient = new SteamClient(Program.GlobalConfig.SteamProtocol);

			if (Program.GlobalConfig.Debug && Directory.Exists(SharedInfo.DebugDirectory)) {
				string debugListenerPath = Path.Combine(SharedInfo.DebugDirectory, botName);

				try {
					Directory.CreateDirectory(debugListenerPath);
					SteamClient.DebugNetworkListener = new NetHookNetworkListener(debugListenerPath);
				} catch (Exception e) {
					ArchiLogger.LogGenericException(e);
				}
			}

			ArchiHandler = new ArchiHandler(ArchiLogger);
			SteamClient.AddHandler(ArchiHandler);

			CallbackManager = new CallbackManager(SteamClient);
			CallbackManager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
			CallbackManager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);

			SteamApps = SteamClient.GetHandler<SteamApps>();
			CallbackManager.Subscribe<SteamApps.FreeLicenseCallback>(OnFreeLicense);
			CallbackManager.Subscribe<SteamApps.GuestPassListCallback>(OnGuestPassList);
			CallbackManager.Subscribe<SteamApps.LicenseListCallback>(OnLicenseList);
			CallbackManager.Subscribe<SteamApps.PICSProductInfoCallback>(OnPICSProductInfo);

			SteamFriends = SteamClient.GetHandler<SteamFriends>();
			CallbackManager.Subscribe<SteamFriends.ChatInviteCallback>(OnChatInvite);
			CallbackManager.Subscribe<SteamFriends.ChatMsgCallback>(OnChatMsg);
			CallbackManager.Subscribe<SteamFriends.FriendsListCallback>(OnFriendsList);
			CallbackManager.Subscribe<SteamFriends.FriendMsgCallback>(OnFriendMsg);
			CallbackManager.Subscribe<SteamFriends.FriendMsgHistoryCallback>(OnFriendMsgHistory);
			CallbackManager.Subscribe<SteamFriends.PersonaStateCallback>(OnPersonaState);

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
			CallbackManager.Subscribe<ArchiHandler.SharedLibraryLockStatusCallback>(OnSharedLibraryLockStatus);

			ArchiWebHandler = new ArchiWebHandler(this);
			CardsFarmer = new CardsFarmer(this);
			//SteamSaleEvent = new SteamSaleEvent(this);
			Trading = new Trading(this);

			if (!Debugging.IsDebugBuild && Program.GlobalConfig.Statistics) {
				Statistics = new Statistics(this);
			}

			InitModules();

			HeartBeatTimer = new Timer(
				async e => await HeartBeat().ConfigureAwait(false),
				null,
				TimeSpan.FromMinutes(1) + TimeSpan.FromMinutes(0.2 * Bots.Count), // Delay
				TimeSpan.FromMinutes(1) // Period
			);
		}

		public void Dispose() {
			// Those are objects that are always being created if constructor doesn't throw exception
			ArchiWebHandler.Dispose();
			CardsFarmer.Dispose();
			HeartBeatTimer.Dispose();
			CallbackSemaphore.Dispose();
			InitializationSemaphore.Dispose();
			//SteamSaleEvent.Dispose();
			Trading.Dispose();

			// Those are objects that might be null and the check should be in-place
			ConnectionFailureTimer?.Dispose();
			FamilySharingInactivityTimer?.Dispose();
			SendItemsTimer?.Dispose();
			Statistics?.Dispose();
		}

		internal async Task<bool> AcceptConfirmations(bool accept, Steam.ConfirmationDetails.EType acceptedType = Steam.ConfirmationDetails.EType.Unknown, ulong acceptedSteamID = 0, HashSet<ulong> acceptedTradeIDs = null) {
			if (!HasMobileAuthenticator) {
				return false;
			}

			while (true) {
				HashSet<MobileAuthenticator.Confirmation> confirmations = await BotDatabase.MobileAuthenticator.GetConfirmations().ConfigureAwait(false);
				if ((confirmations == null) || (confirmations.Count == 0)) {
					return true;
				}

				if (acceptedType != Steam.ConfirmationDetails.EType.Unknown) {
					if (confirmations.RemoveWhere(confirmation => (confirmation.Type != acceptedType) && (confirmation.Type != Steam.ConfirmationDetails.EType.Other)) > 0) {
						if (confirmations.Count == 0) {
							return true;
						}
					}
				}

				if ((acceptedSteamID == 0) && ((acceptedTradeIDs == null) || (acceptedTradeIDs.Count == 0))) {
					if (!await BotDatabase.MobileAuthenticator.HandleConfirmations(confirmations, accept).ConfigureAwait(false)) {
						return false;
					}

					continue;
				}

				IEnumerable<Task<Steam.ConfirmationDetails>> tasks = confirmations.Select(BotDatabase.MobileAuthenticator.GetConfirmationDetails);
				ICollection<Steam.ConfirmationDetails> results;

				switch (Program.GlobalConfig.OptimizationMode) {
					case GlobalConfig.EOptimizationMode.MinMemoryUsage:
						results = new List<Steam.ConfirmationDetails>(confirmations.Count);
						foreach (Task<Steam.ConfirmationDetails> task in tasks) {
							results.Add(await task.ConfigureAwait(false));
						}

						break;
					default:
						results = await Task.WhenAll(tasks).ConfigureAwait(false);
						break;
				}

				HashSet<MobileAuthenticator.Confirmation> ignoredConfirmations = new HashSet<MobileAuthenticator.Confirmation>(results.Where(details => (details != null) && (((acceptedSteamID != 0) && (details.OtherSteamID64 != 0) && (acceptedSteamID != details.OtherSteamID64)) || ((acceptedTradeIDs != null) && (details.TradeOfferID != 0) && !acceptedTradeIDs.Contains(details.TradeOfferID)))).Select(details => details.Confirmation));

				if (ignoredConfirmations.Count > 0) {
					confirmations.ExceptWith(ignoredConfirmations);
					if (confirmations.Count == 0) {
						return true;
					}
				}

				if (!await BotDatabase.MobileAuthenticator.HandleConfirmations(confirmations, accept).ConfigureAwait(false)) {
					return false;
				}
			}
		}

		internal static string FormatBotResponse(string response, string botName) {
			if (!string.IsNullOrEmpty(response) && !string.IsNullOrEmpty(botName)) {
				return Environment.NewLine + "<" + botName + "> " + response;
			}

			ASF.ArchiLogger.LogNullError(nameof(response) + " || " + nameof(botName));
			return null;
		}

		internal static string GetAPIStatus(IDictionary<string, Bot> bots) {
			if (bots == null) {
				ASF.ArchiLogger.LogNullError(nameof(bots));
				return null;
			}

			var response = new {
				Bots = bots
			};

			try {
				return JsonConvert.SerializeObject(response);
			} catch (JsonException e) {
				ASF.ArchiLogger.LogGenericException(e);
				return null;
			}
		}

		internal async Task<uint> GetAppIDForIdling(uint appID, bool allowRecursiveDiscovery = true) {
			if (appID == 0) {
				ArchiLogger.LogNullError(nameof(appID));
				return 0;
			}

			AsyncJobMultiple<SteamApps.PICSProductInfoCallback>.ResultSet productInfoResultSet;

			try {
				productInfoResultSet = await SteamApps.PICSGetProductInfo(appID, null, false);
			} catch (Exception e) {
				ArchiLogger.LogGenericException(e);
				return appID;
			}

			// ReSharper disable once LoopCanBePartlyConvertedToQuery - C# 7.0 out can't be used within LINQ query yet | https://github.com/dotnet/roslyn/issues/15619
			foreach (Dictionary<uint, SteamApps.PICSProductInfoCallback.PICSProductInfo> productInfoApps in productInfoResultSet.Results.Select(result => result.Apps)) {
				if (!productInfoApps.TryGetValue(appID, out SteamApps.PICSProductInfoCallback.PICSProductInfo productInfoApp)) {
					continue;
				}

				KeyValue productInfo = productInfoApp.KeyValues;
				if (productInfo == KeyValue.Invalid) {
					ArchiLogger.LogNullError(nameof(productInfo));
					break;
				}

				KeyValue commonProductInfo = productInfo["common"];
				if (commonProductInfo == KeyValue.Invalid) {
					continue;
				}

				string releaseState = commonProductInfo["ReleaseState"].Value;
				if (!string.IsNullOrEmpty(releaseState)) {
					// We must convert this to uppercase, since Valve doesn't stick to any convention and we can have a case mismatch
					switch (releaseState.ToUpperInvariant()) {
						case "RELEASED":
							break;
						case "PRELOADONLY":
						case "PRERELEASE":
							return 0;
						default:
							ArchiLogger.LogGenericWarning(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(releaseState), releaseState));
							break;
					}
				}

				string type = commonProductInfo["type"].Value;
				if (string.IsNullOrEmpty(type)) {
					return appID;
				}

				// We must convert this to uppercase, since Valve doesn't stick to any convention and we can have a case mismatch
				switch (type.ToUpperInvariant()) {
					// Types that can be idled
					case "APPLICATION":
					case "EPISODE":
					case "GAME":
					case "MOVIE":
					case "VIDEO":
						return appID;

					// Types that can't be idled
					case "ADVERTISING":
					case "DEMO":
					case "DLC":
					case "GUIDE":
					case "HARDWARE":
					case "MOD":
					case "SERIES":
						break;
					default:
						ArchiLogger.LogGenericWarning(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(type), type));
						break;
				}

				if (!allowRecursiveDiscovery) {
					return 0;
				}

				string listOfDlc = productInfo["extended"]["listofdlc"].Value;
				if (string.IsNullOrEmpty(listOfDlc)) {
					return appID;
				}

				string[] dlcAppIDsString = listOfDlc.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
				foreach (string dlcAppIDString in dlcAppIDsString) {
					if (!uint.TryParse(dlcAppIDString, out uint dlcAppID) || (dlcAppID == 0)) {
						ArchiLogger.LogNullError(nameof(dlcAppID));
						break;
					}

					dlcAppID = await GetAppIDForIdling(dlcAppID, false).ConfigureAwait(false);
					if (dlcAppID != 0) {
						return dlcAppID;
					}
				}

				return appID;
			}

			return appID;
		}

		internal static async Task InitializeCMs(uint cellID, InMemoryServerListProvider serverListProvider) {
			if (serverListProvider == null) {
				ASF.ArchiLogger.LogNullError(nameof(serverListProvider));
				return;
			}

			CMClient.Servers.CellID = cellID;
			CMClient.Servers.ServerListProvider = serverListProvider;

			// Ensure that we ask for a list of servers if we don't have any saved servers available
			IEnumerable<IPEndPoint> servers = await serverListProvider.FetchServerListAsync().ConfigureAwait(false);
			if (servers?.Any() != true) {
				ASF.ArchiLogger.LogGenericInfo(string.Format(Strings.Initializing, nameof(SteamDirectory)));

				try {
					await SteamDirectory.Initialize(cellID).ConfigureAwait(false);
					ASF.ArchiLogger.LogGenericInfo(Strings.Success);
				} catch {
					ASF.ArchiLogger.LogGenericWarning(Strings.BotSteamDirectoryInitializationFailed);
				}
			}
		}

		internal bool IsBlacklistedFromTrades(ulong steamID) {
			if (steamID != 0) {
				return BotDatabase.IsBlacklistedFromTrades(steamID);
			}

			ArchiLogger.LogNullError(nameof(steamID));
			return false;
		}

		internal bool IsMaster(ulong steamID) {
			if (steamID == 0) {
				ArchiLogger.LogNullError(nameof(steamID));
				return false;
			}

			if (IsOwner(steamID)) {
				return true;
			}

			return GetSteamUserPermission(steamID) >= BotConfig.EPermission.Master;
		}

		internal async Task LootIfNeeded() {
			if (!IsConnectedAndLoggedOn || !BotConfig.SendOnFarmingFinished) {
				return;
			}

			ulong steamMasterID = GetFirstSteamMasterID();
			if (steamMasterID == 0) {
				return;
			}

			await ResponseLoot(steamMasterID).ConfigureAwait(false);
		}

		internal async Task OnFarmingFinished(bool farmedSomething) {
			OnFarmingStopped();

			if (farmedSomething || !FirstTradeSent) {
				FirstTradeSent = true;
				await LootIfNeeded().ConfigureAwait(false);
			}

			if (BotConfig.ShutdownOnFarmingFinished) {
				if (SkipFirstShutdown) {
					SkipFirstShutdown = false;
				} else {
					Stop();
				}
			}
		}

		internal void OnFarmingStopped() => ResetGamesPlayed();

		internal async Task OnNewConfigLoaded(ASF.BotConfigEventArgs args) {
			if (args == null) {
				ArchiLogger.LogNullError(nameof(args));
				return;
			}

			if (args.BotConfig == null) {
				Destroy();
				return;
			}

			if (args.BotConfig == BotConfig) {
				return;
			}

			await InitializationSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				if (args.BotConfig == BotConfig) {
					return;
				}

				Stop(false);
				BotConfig = args.BotConfig;

				InitModules();
				InitStart().Forget();
			} finally {
				InitializationSemaphore.Release();
			}
		}

		internal void PlayGame(uint gameID, string gameName = null) => PlayGames(gameID.ToEnumerable(), gameName);

		internal void PlayGames(IEnumerable<uint> gameIDs, string gameName = null) {
			if (gameIDs == null) {
				ArchiLogger.LogNullError(nameof(gameIDs));
				return;
			}

			ArchiHandler.PlayGames(gameIDs, gameName);
		}

		internal async Task<bool> RefreshSession() {
			if (!IsConnectedAndLoggedOn) {
				return false;
			}

			SteamUser.WebAPIUserNonceCallback callback;

			try {
				callback = await SteamUser.RequestWebAPIUserNonce();
			} catch (Exception e) {
				ArchiLogger.LogGenericException(e);
				await Connect(true).ConfigureAwait(false);
				return false;
			}

			if (string.IsNullOrEmpty(callback?.Nonce)) {
				await Connect(true).ConfigureAwait(false);
				return false;
			}

			if (await ArchiWebHandler.Init(SteamID, SteamClient.ConnectedUniverse, callback.Nonce, BotConfig.SteamParentalPIN).ConfigureAwait(false)) {
				return true;
			}

			await Connect(true).ConfigureAwait(false);
			return false;
		}

		internal static void RegisterBot(string botName) {
			if (string.IsNullOrEmpty(botName)) {
				ASF.ArchiLogger.LogNullError(nameof(botName));
				return;
			}

			Bot bot;

			try {
				bot = new Bot(botName);
			} catch (ArgumentException e) {
				ASF.ArchiLogger.LogGenericException(e);
				return;
			}

			bot.InitStart().Forget();
		}

		internal void RequestPersonaStateUpdate() {
			if (!IsConnectedAndLoggedOn) {
				return;
			}

			SteamFriends.RequestFriendInfo(SteamID, EClientPersonaStateFlag.PlayerName | EClientPersonaStateFlag.Presence);
		}

		internal async Task<string> Response(ulong steamID, string message) {
			if ((steamID == 0) || string.IsNullOrEmpty(message)) {
				ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(message));
				return null;
			}

			if (message[0] != '!') {
				if (!IsMaster(steamID)) {
					return null;
				}

				return await ResponseRedeem(steamID, message, ERedeemFlags.Validate).ConfigureAwait(false);
			}

			if (message.IndexOf(' ') < 0) {
				switch (message.ToUpperInvariant()) {
					case "!2FA":
						return await Response2FA(steamID).ConfigureAwait(false);
					case "!2FANO":
						return await Response2FAConfirm(steamID, false).ConfigureAwait(false);
					case "!2FAOK":
						return await Response2FAConfirm(steamID, true).ConfigureAwait(false);
					case "!API":
						return ResponseAPI(steamID);
					case "!BL":
						return ResponseBlacklist(steamID);
					case "!EXIT":
						return ResponseExit(steamID);
					case "!FARM":
						return await ResponseFarm(steamID).ConfigureAwait(false);
					case "!HELP":
						return ResponseHelp(steamID);
					case "!LOOT":
						return await ResponseLoot(steamID).ConfigureAwait(false);
					case "!LOOT^":
						return ResponseLootSwitch(steamID);
					case "!PASSWORD":
						return ResponsePassword(steamID);
					case "!PAUSE":
						return await ResponsePause(steamID, true).ConfigureAwait(false);
					case "!PAUSE~":
						return await ResponsePause(steamID, false).ConfigureAwait(false);
					case "!REJOINCHAT":
						return ResponseRejoinChat(steamID);
					case "!RESUME":
						return ResponseResume(steamID);
					case "!RESTART":
						return ResponseRestart(steamID);
					case "!SA":
						return await ResponseStatus(steamID, SharedInfo.ASF).ConfigureAwait(false);
					case "!STATUS":
						return ResponseStatus(steamID);
					case "!STOP":
						return ResponseStop(steamID);
					case "!UPDATE":
						return await ResponseUpdate(steamID).ConfigureAwait(false);
					case "!VERSION":
						return ResponseVersion(steamID);
					default:
						return ResponseUnknown(steamID);
				}
			}

			string[] args = message.Split((char[]) null, StringSplitOptions.RemoveEmptyEntries);
			switch (args[0].ToUpperInvariant()) {
				case "!2FA":
					return await Response2FA(steamID, args[1]).ConfigureAwait(false);
				case "!2FANO":
					return await Response2FAConfirm(steamID, args[1], false).ConfigureAwait(false);
				case "!2FAOK":
					return await Response2FAConfirm(steamID, args[1], true).ConfigureAwait(false);
				case "!ADDLICENSE":
					if (args.Length > 2) {
						return await ResponseAddLicense(steamID, args[1], args.GetArgsAsString(2)).ConfigureAwait(false);
					}

					return await ResponseAddLicense(steamID, args[1]).ConfigureAwait(false);
				case "!API":
					return ResponseAPI(steamID, args[1]);
				case "!BL":
					return await ResponseBlacklist(steamID, args[1]).ConfigureAwait(false);
				case "!BLADD":
					if (args.Length > 2) {
						return await ResponseBlacklistAdd(steamID, args[1], args.GetArgsAsString(2)).ConfigureAwait(false);
					}

					return ResponseBlacklistAdd(steamID, args[1]);
				case "!BLRM":
					if (args.Length > 2) {
						return await ResponseBlacklistRemove(steamID, args[1], args.GetArgsAsString(2)).ConfigureAwait(false);
					}

					return ResponseBlacklistRemove(steamID, args[1]);
				case "!FARM":
					return await ResponseFarm(steamID, args[1]).ConfigureAwait(false);
				case "!INPUT":
					if (args.Length > 3) {
						return await ResponseInput(steamID, args[1], args[2], args.GetArgsAsString(3)).ConfigureAwait(false);
					}

					return args.Length == 3 ? ResponseInput(steamID, args[1], args[2]) : ResponseUnknown(steamID);
				case "!LOOT":
					return await ResponseLoot(steamID, args[1]).ConfigureAwait(false);
				case "!LOOT^":
					return await ResponseLootSwitch(steamID, args[1]).ConfigureAwait(false);
				case "!NICKNAME":
					if (args.Length > 2) {
						return await ResponseNickname(steamID, args[1], args.GetArgsAsString(2)).ConfigureAwait(false);
					}

					return await ResponseNickname(steamID, args[1]).ConfigureAwait(false);
				case "!OA":
					return await ResponseOwns(steamID, SharedInfo.ASF, args[1]).ConfigureAwait(false);
				case "!OWNS":
					if (args.Length > 2) {
						return await ResponseOwns(steamID, args[1], args.GetArgsAsString(2)).ConfigureAwait(false);
					}

					return await ResponseOwns(steamID, args[1]).ConfigureAwait(false);
				case "!PASSWORD":
					return await ResponsePassword(steamID, args[1]).ConfigureAwait(false);
				case "!PAUSE":
					return await ResponsePause(steamID, args[1], true).ConfigureAwait(false);
				case "!PAUSE~":
					return await ResponsePause(steamID, args[1], false).ConfigureAwait(false);
				case "!PLAY":
					if (args.Length > 2) {
						return await ResponsePlay(steamID, args[1], args.GetArgsAsString(2)).ConfigureAwait(false);
					}

					return await ResponsePlay(steamID, args[1]).ConfigureAwait(false);
				case "!R":
				case "!REDEEM":
					if (args.Length > 2) {
						return await ResponseRedeem(steamID, args[1], args.GetArgsAsString(2)).ConfigureAwait(false);
					}

					return await ResponseRedeem(steamID, args[1]).ConfigureAwait(false);
				case "!R^":
				case "!REDEEM^":
					if (args.Length > 2) {
						return await ResponseRedeem(steamID, args[1], args.GetArgsAsString(2), ERedeemFlags.SkipForwarding | ERedeemFlags.SkipDistribution).ConfigureAwait(false);
					}

					return await ResponseRedeem(steamID, args[1], ERedeemFlags.SkipForwarding | ERedeemFlags.SkipDistribution).ConfigureAwait(false);
				case "!R&":
				case "!REDEEM&":
					if (args.Length > 2) {
						return await ResponseRedeem(steamID, args[1], args.GetArgsAsString(2), ERedeemFlags.ForceForwarding | ERedeemFlags.SkipInitial).ConfigureAwait(false);
					}

					return await ResponseRedeem(steamID, args[1], ERedeemFlags.ForceForwarding | ERedeemFlags.SkipInitial).ConfigureAwait(false);
				case "!REJOINCHAT":
					return await ResponseRejoinChat(steamID, args[1]).ConfigureAwait(false);
				case "!RESUME":
					return await ResponseResume(steamID, args[1]).ConfigureAwait(false);
				case "!START":
					return await ResponseStart(steamID, args[1]).ConfigureAwait(false);
				case "!STATUS":
					return await ResponseStatus(steamID, args[1]).ConfigureAwait(false);
				case "!STOP":
					return await ResponseStop(steamID, args[1]).ConfigureAwait(false);
				default:
					return ResponseUnknown(steamID);
			}
		}

		internal void SendMessage(ulong steamID, string message) {
			if ((steamID == 0) || string.IsNullOrEmpty(message)) {
				ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(message));
				return;
			}

			if (new SteamID(steamID).IsChatAccount) {
				SendMessageToChannel(steamID, message);
			} else {
				SendMessageToUser(steamID, message);
			}
		}

		internal void Stop(bool withShutdownEvent = true) {
			if (!KeepRunning) {
				return;
			}

			ArchiLogger.LogGenericInfo(Strings.BotStopping);
			KeepRunning = false;

			if (SteamClient.IsConnected) {
				Disconnect();
			}

			if (withShutdownEvent) {
				Events.OnBotShutdown();
			}
		}

		private async Task CheckFamilySharingInactivity() {
			if (!IsPlayingPossible) {
				return;
			}

			ArchiLogger.LogGenericInfo(Strings.BotAutomaticIdlingPauseTimeout);
			StopFamilySharingInactivityTimer();
			await CardsFarmer.Resume(false).ConfigureAwait(false);
		}

		private async Task CheckOccupationStatus() {
			StopPlayingWasBlockedTimer();

			if (!IsPlayingPossible) {
				ArchiLogger.LogGenericInfo(Strings.BotAccountOccupied);
				PlayingWasBlocked = true;
				StopFamilySharingInactivityTimer();
				return;
			}

			ArchiLogger.LogGenericInfo(Strings.BotAccountFree);
			PlayingWasBlocked = false;
			await CardsFarmer.Resume(false).ConfigureAwait(false);
		}

		private async Task Connect(bool force = false) {
			if (!force && (!KeepRunning || SteamClient.IsConnected)) {
				return;
			}

			await LimitLoginRequestsAsync().ConfigureAwait(false);

			if (string.IsNullOrEmpty(TwoFactorCode) && HasMobileAuthenticator) {
				// In this case, we can also use ASF 2FA for providing 2FA token, even if it's not required
				TwoFactorCode = await BotDatabase.MobileAuthenticator.GenerateToken().ConfigureAwait(false);
			}

			if (!force && (!KeepRunning || SteamClient.IsConnected)) {
				return;
			}

			ArchiLogger.LogGenericInfo(Strings.BotConnecting);
			InitConnectionFailureTimer();
			SteamClient.Connect();
		}

		private void Destroy(bool force = false) {
			if (!force) {
				Stop();
			} else {
				// Stop() will most likely block due to fuckup, don't wait for it
				Task.Run(() => Stop()).Forget();
			}

			Bots.TryRemove(BotName, out _);
		}

		private void Disconnect() {
			StopConnectionFailureTimer();
			SteamClient.Disconnect();
		}

		private string FormatBotResponse(string response) {
			if (!string.IsNullOrEmpty(response)) {
				return Environment.NewLine + "<" + BotName + "> " + response;
			}

			ASF.ArchiLogger.LogNullError(nameof(response));
			return null;
		}

		private static string FormatStaticResponse(string response) {
			if (!string.IsNullOrEmpty(response)) {
				return Environment.NewLine + response;
			}

			ASF.ArchiLogger.LogNullError(nameof(response));
			return null;
		}

		private static HashSet<Bot> GetBots(string args) {
			if (string.IsNullOrEmpty(args)) {
				ASF.ArchiLogger.LogNullError(nameof(args));
				return null;
			}

			string[] botNames = args.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

			HashSet<Bot> result = new HashSet<Bot>();
			foreach (string botName in botNames) {
				if (botName.Equals(SharedInfo.ASF, StringComparison.OrdinalIgnoreCase)) {
					foreach (Bot bot in Bots.OrderBy(bot => bot.Key).Select(bot => bot.Value)) {
						result.Add(bot);
					}

					return result;
				}

				if (botName.Contains("..")) {
					string[] botRange = botName.Split(new[] { ".." }, StringSplitOptions.RemoveEmptyEntries);
					if (botRange.Length == 2) {
						if (Bots.TryGetValue(botRange[0], out Bot firstBot) && Bots.TryGetValue(botRange[1], out Bot lastBot)) {
							bool inRange = false;

							foreach (Bot bot in Bots.OrderBy(bot => bot.Key).Select(bot => bot.Value)) {
								if (bot == firstBot) {
									inRange = true;
								} else if (!inRange) {
									continue;
								}

								result.Add(bot);

								if (bot == lastBot) {
									break;
								}
							}

							continue;
						}
					}
				}

				if (!Bots.TryGetValue(botName, out Bot targetBot)) {
					continue;
				}

				result.Add(targetBot);
			}

			return result;
		}

		private ulong GetFirstSteamMasterID() => BotConfig.SteamUserPermissions.Where(kv => (kv.Key != 0) && (kv.Key != SteamID) && (kv.Value == BotConfig.EPermission.Master)).Select(kv => kv.Key).OrderBy(steamID => steamID).FirstOrDefault();

		private BotConfig.EPermission GetSteamUserPermission(ulong steamID) {
			if (steamID != 0) {
				return BotConfig.SteamUserPermissions.TryGetValue(steamID, out BotConfig.EPermission permission) ? permission : BotConfig.EPermission.None;
			}

			ArchiLogger.LogNullError(nameof(steamID));
			return BotConfig.EPermission.None;
		}

		private void HandleCallbacks() {
			TimeSpan timeSpan = TimeSpan.FromMilliseconds(CallbackSleep);
			while (KeepRunning || SteamClient.IsConnected) {
				if (!CallbackSemaphore.Wait(0)) {
					if (Debugging.IsUserDebugging) {
						ArchiLogger.LogGenericDebug(string.Format(Strings.WarningFailedWithError, nameof(CallbackSemaphore)));
					}

					return;
				}

				try {
					CallbackManager.RunWaitAllCallbacks(timeSpan);
				} catch (Exception e) {
					ArchiLogger.LogGenericException(e);
				} finally {
					CallbackSemaphore.Release();
				}
			}
		}

		private async Task HandleMessage(ulong chatID, ulong steamID, string message) {
			if ((chatID == 0) || (steamID == 0) || string.IsNullOrEmpty(message)) {
				ArchiLogger.LogNullError(nameof(chatID) + " || " + nameof(steamID) + " || " + nameof(message));
				return;
			}

			string response = await Response(steamID, message).ConfigureAwait(false);

			// We respond with null when user is not authorized (and similar)
			if (string.IsNullOrEmpty(response)) {
				return;
			}

			SendMessage(chatID, response);
		}

		private async Task HeartBeat() {
			if (!KeepRunning || !IsConnectedAndLoggedOn || (HeartBeatFailures == byte.MaxValue)) {
				return;
			}

			try {
				if (DateTime.UtcNow.Subtract(ArchiHandler.LastPacketReceived).TotalSeconds > MinHeartBeatTTL) {
					await SteamApps.PICSGetProductInfo(0, null);
				}

				HeartBeatFailures = 0;
				Statistics?.OnHeartBeat().Forget();
			} catch (Exception e) {
				if (Debugging.IsUserDebugging) {
					ArchiLogger.LogGenericDebugException(e);
				}

				if (!KeepRunning || !IsConnectedAndLoggedOn || (HeartBeatFailures == byte.MaxValue)) {
					return;
				}

				if (++HeartBeatFailures >= (byte) Math.Ceiling(Program.GlobalConfig.ConnectionTimeout / 10.0)) {
					HeartBeatFailures = byte.MaxValue;
					ArchiLogger.LogGenericWarning(Strings.BotConnectionLost);
					Connect(true).Forget();
				}
			}
		}

		private void ImportAuthenticator(string maFilePath) {
			if (HasMobileAuthenticator || !File.Exists(maFilePath)) {
				return;
			}

			ArchiLogger.LogGenericInfo(Strings.BotAuthenticatorConverting);

			try {
				BotDatabase.MobileAuthenticator = JsonConvert.DeserializeObject<MobileAuthenticator>(File.ReadAllText(maFilePath));
				File.Delete(maFilePath);
			} catch (Exception e) {
				ArchiLogger.LogGenericException(e);
				return;
			}

			if (BotDatabase.MobileAuthenticator == null) {
				ArchiLogger.LogNullError(nameof(BotDatabase.MobileAuthenticator));
				return;
			}

			BotDatabase.MobileAuthenticator.Init(this);

			if (!BotDatabase.MobileAuthenticator.HasCorrectDeviceID) {
				ArchiLogger.LogGenericWarning(Strings.BotAuthenticatorInvalidDeviceID);
				if (string.IsNullOrEmpty(DeviceID)) {
					string deviceID = Program.GetUserInput(ASF.EUserInputType.DeviceID, BotName);
					if (string.IsNullOrEmpty(deviceID)) {
						BotDatabase.MobileAuthenticator = null;
						return;
					}

					SetUserInput(ASF.EUserInputType.DeviceID, deviceID);
				}

				BotDatabase.MobileAuthenticator.CorrectDeviceID(DeviceID);
				BotDatabase.Save();
			}

			ArchiLogger.LogGenericInfo(Strings.BotAuthenticatorImportFinished);
		}

		private void InitConnectionFailureTimer() {
			if (ConnectionFailureTimer != null) {
				return;
			}

			ConnectionFailureTimer = new Timer(
				e => InitPermanentConnectionFailure(),
				null,
				TimeSpan.FromMinutes(Math.Ceiling(Program.GlobalConfig.ConnectionTimeout / 30.0)), // Delay
				Timeout.InfiniteTimeSpan // Period
			);
		}

		private async Task InitializeFamilySharing() {
			HashSet<ulong> steamIDs = await ArchiWebHandler.GetFamilySharingSteamIDs().ConfigureAwait(false);
			if ((steamIDs == null) || (steamIDs.Count == 0)) {
				return;
			}

			SteamFamilySharingIDs.ReplaceIfNeededWith(steamIDs);
		}

		private bool InitLoginAndPassword(bool requiresPassword) {
			if (string.IsNullOrEmpty(BotConfig.SteamLogin)) {
				string steamLogin = Program.GetUserInput(ASF.EUserInputType.Login, BotName);
				if (string.IsNullOrEmpty(steamLogin)) {
					return false;
				}

				SetUserInput(ASF.EUserInputType.Login, steamLogin);
			}

			if (!string.IsNullOrEmpty(BotConfig.SteamPassword) || (!requiresPassword && !string.IsNullOrEmpty(BotDatabase.LoginKey))) {
				return true;
			}

			string steamPassword = Program.GetUserInput(ASF.EUserInputType.Password, BotName);
			if (string.IsNullOrEmpty(steamPassword)) {
				return false;
			}

			SetUserInput(ASF.EUserInputType.Password, steamPassword);
			return true;
		}

		private void InitModules() {
			CardsFarmer.SetInitialState(BotConfig.Paused);

			if (SendItemsTimer != null) {
				SendItemsTimer.Dispose();
				SendItemsTimer = null;
			}

			if (BotConfig.SendTradePeriod == 0) {
				return;
			}

			ulong steamMasterID = BotConfig.SteamUserPermissions.Where(kv => kv.Value == BotConfig.EPermission.Master).Select(kv => kv.Key).FirstOrDefault();
			if (steamMasterID == 0) {
				return;
			}

			TimeSpan delay = TimeSpan.FromHours(BotConfig.SendTradePeriod) + TimeSpan.FromMinutes(Bots.Count);
			TimeSpan period = TimeSpan.FromHours(BotConfig.SendTradePeriod);

			SendItemsTimer = new Timer(
				async e => await ResponseLoot(steamMasterID).ConfigureAwait(false),
				null,
				delay, // Delay
				period // Period
			);
		}

		private void InitPermanentConnectionFailure() {
			if (!KeepRunning) {
				return;
			}

			ArchiLogger.LogGenericError(Strings.BotHeartBeatFailed);
			Destroy(true);
			RegisterBot(BotName);
		}

		private async Task InitStart() {
			if ((BotConfig == null) || (BotDatabase == null)) {
				return;
			}

			if (!BotConfig.Enabled) {
				ArchiLogger.LogGenericInfo(Strings.BotInstanceNotStartingBecauseDisabled);
				return;
			}

			// Start
			await Start().ConfigureAwait(false);
		}

		private bool IsFamilySharing(ulong steamID) {
			if (steamID == 0) {
				ArchiLogger.LogNullError(nameof(steamID));
				return false;
			}

			if (IsOwner(steamID)) {
				return true;
			}

			return SteamFamilySharingIDs.Contains(steamID) || (GetSteamUserPermission(steamID) >= BotConfig.EPermission.FamilySharing);
		}

		private bool IsMasterClanID(ulong steamID) {
			if (steamID != 0) {
				return steamID == BotConfig.SteamMasterClanID;
			}

			ArchiLogger.LogNullError(nameof(steamID));
			return false;
		}

		private bool IsOperator(ulong steamID) {
			if (steamID == 0) {
				ArchiLogger.LogNullError(nameof(steamID));
				return false;
			}

			if (IsOwner(steamID)) {
				return true;
			}

			return GetSteamUserPermission(steamID) >= BotConfig.EPermission.Operator;
		}

		private static bool IsOwner(ulong steamID) {
			if (steamID != 0) {
				return (steamID == Program.GlobalConfig.SteamOwnerID) || (Debugging.IsDebugBuild && (steamID == SharedInfo.ArchiSteamID));
			}

			ASF.ArchiLogger.LogNullError(nameof(steamID));
			return false;
		}

		private static bool IsValidCdKey(string key) {
			if (!string.IsNullOrEmpty(key)) {
				return Regex.IsMatch(key, @"^[0-9A-Z]{4,7}-[0-9A-Z]{4,7}-[0-9A-Z]{4,7}(?:(?:-[0-9A-Z]{4,7})?(?:-[0-9A-Z]{4,7}))?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
			}

			ASF.ArchiLogger.LogNullError(nameof(key));
			return false;
		}

		private void JoinMasterChat() {
			if (!IsConnectedAndLoggedOn || (BotConfig.SteamMasterClanID == 0)) {
				return;
			}

			SteamFriends.JoinChat(BotConfig.SteamMasterClanID);
		}

		private static async Task LimitGiftsRequestsAsync() {
			await GiftsSemaphore.WaitAsync().ConfigureAwait(false);
			Task.Run(async () => {
				await Task.Delay(Program.GlobalConfig.GiftsLimiterDelay * 1000).ConfigureAwait(false);
				GiftsSemaphore.Release();
			}).Forget();
		}

		private static async Task LimitLoginRequestsAsync() {
			await LoginSemaphore.WaitAsync().ConfigureAwait(false);
			Task.Run(async () => {
				await Task.Delay(Program.GlobalConfig.LoginLimiterDelay * 1000).ConfigureAwait(false);
				LoginSemaphore.Release();
			}).Forget();
		}

		private async Task MarkInventoryIfNeeded() {
			if (!BotConfig.DismissInventoryNotifications) {
				return;
			}

			await ArchiWebHandler.MarkInventory().ConfigureAwait(false);
		}

		private async void OnAccountInfo(SteamUser.AccountInfoCallback callback) {
			if (callback == null) {
				ArchiLogger.LogNullError(nameof(callback));
				return;
			}

			if (BotConfig.FarmOffline) {
				return;
			}

			// We can't use SetPersonaState() before SK2 in fact registers our nickname
			// This is pretty rare, but SK2 SteamFriends handler and this handler could execute at the same time
			// So we wait for nickname to be registered (with timeout of 5 tries/seconds)
			string nickname = SteamFriends.GetPersonaName();
			for (byte i = 0; (i < WebBrowser.MaxRetries) && (string.IsNullOrEmpty(nickname) || nickname.Equals("[unassigned]")); i++) {
				await Task.Delay(1000).ConfigureAwait(false);
				nickname = SteamFriends.GetPersonaName();
			}

			if (string.IsNullOrEmpty(nickname) || nickname.Equals("[unassigned]")) {
				ArchiLogger.LogGenericError(string.Format(Strings.ErrorObjectIsNull, nameof(nickname)));
				return;
			}

			try {
				await SteamFriends.SetPersonaState(EPersonaState.Online);
			} catch (Exception e) {
				ArchiLogger.LogGenericException(e);
			}
		}

		private void OnChatInvite(SteamFriends.ChatInviteCallback callback) {
			if ((callback?.ChatRoomID == null) || (callback.PatronID == null)) {
				ArchiLogger.LogNullError(nameof(callback) + " || " + nameof(callback.ChatRoomID) + " || " + nameof(callback.PatronID));
				return;
			}

			if (!IsMaster(callback.PatronID)) {
				return;
			}

			SteamFriends.JoinChat(callback.ChatRoomID);
		}

		private async void OnChatMsg(SteamFriends.ChatMsgCallback callback) {
			if (callback == null) {
				ArchiLogger.LogNullError(nameof(callback));
				return;
			}

			if (callback.ChatMsgType != EChatEntryType.ChatMsg) {
				return;
			}

			if ((callback.ChatRoomID == null) || (callback.ChatterID == null) || string.IsNullOrEmpty(callback.Message)) {
				ArchiLogger.LogNullError(nameof(callback.ChatRoomID) + " || " + nameof(callback.ChatterID) + " || " + nameof(callback.Message));
				return;
			}

			ArchiLogger.LogGenericTrace(callback.ChatRoomID.ConvertToUInt64() + "/" + callback.ChatterID.ConvertToUInt64() + ": " + callback.Message);

			switch (callback.Message.ToUpperInvariant()) {
				case "!LEAVE":
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

		private void OnConnected(SteamClient.ConnectedCallback callback) {
			if (callback == null) {
				ArchiLogger.LogNullError(nameof(callback));
				return;
			}

			HeartBeatFailures = 0;
			StopConnectionFailureTimer();

			if (callback.Result != EResult.OK) {
				ArchiLogger.LogGenericError(string.Format(Strings.BotUnableToConnect, callback.Result));
				return;
			}

			ArchiLogger.LogGenericInfo(Strings.BotConnected);

			if (!KeepRunning) {
				ArchiLogger.LogGenericInfo(Strings.BotDisconnecting);
				Disconnect();
				return;
			}

			byte[] sentryFileHash = null;

			if (File.Exists(SentryFile)) {
				try {
					byte[] sentryFileContent = File.ReadAllBytes(SentryFile);
					sentryFileHash = SteamKit2.CryptoHelper.SHAHash(sentryFileContent);
				} catch (Exception e) {
					ArchiLogger.LogGenericException(e);
				}
			}

			if (!InitLoginAndPassword(false)) {
				Stop();
				return;
			}

			ArchiLogger.LogGenericInfo(Strings.BotLoggingIn);

			string password = BotConfig.SteamPassword;
			if (!string.IsNullOrEmpty(password)) {
				// Steam silently ignores non-ASCII characters in password, we're going to do the same
				// Don't ask me why, I know it's stupid
				password = Regex.Replace(password, @"[^\u0000-\u007F]+", "");
			}

			// Decrypt login key if needed
			string loginKey = BotDatabase.LoginKey;
			if (!string.IsNullOrEmpty(loginKey) && (loginKey.Length > 19)) {
				loginKey = CryptoHelper.Decrypt(BotConfig.PasswordFormat, loginKey);
			}

			SteamUser.LogOnDetails logOnDetails = new SteamUser.LogOnDetails {
				AuthCode = AuthCode,
				CellID = Program.GlobalDatabase.CellID,
				LoginID = LoginID,
				LoginKey = loginKey,
				Password = password,
				SentryFileHash = sentryFileHash,
				ShouldRememberPassword = true,
				TwoFactorCode = TwoFactorCode,
				Username = BotConfig.SteamLogin
			};

			InitConnectionFailureTimer();

			try {
				SteamUser.LogOn(logOnDetails);
			} catch {
				// TODO: Remove me once https://github.com/SteamRE/SteamKit/issues/305 is fixed
				ArchiHandler.LogOnWithoutMachineID(logOnDetails);
			}
		}

		private async void OnDisconnected(SteamClient.DisconnectedCallback callback) {
			if (callback == null) {
				ArchiLogger.LogNullError(nameof(callback));
				return;
			}

			EResult lastLogOnResult = LastLogOnResult;
			LastLogOnResult = EResult.Invalid;
			HeartBeatFailures = 0;
			StopConnectionFailureTimer();
			StopPlayingWasBlockedTimer();

			ArchiLogger.LogGenericInfo(Strings.BotDisconnected);

			ArchiWebHandler.OnDisconnected();
			CardsFarmer.OnDisconnected();
			Trading.OnDisconnected();

			FirstTradeSent = false;
			HandledGifts.ClearAndTrim();

			// If we initiated disconnect, do not attempt to reconnect
			if (callback.UserInitiated) {
				return;
			}

			switch (lastLogOnResult) {
				case EResult.Invalid:
					// Invalid means that we didn't get OnLoggedOn() in the first place, so Steam is down
					// Always reset one-time-only access tokens in this case, as OnLoggedOn() didn't do that for us
					AuthCode = TwoFactorCode = null;
					break;
				case EResult.InvalidPassword:
					// If we didn't use login key, it's nearly always rate limiting
					if (string.IsNullOrEmpty(BotDatabase.LoginKey)) {
						goto case EResult.RateLimitExceeded;
					}

					BotDatabase.LoginKey = null;
					ArchiLogger.LogGenericInfo(Strings.BotRemovedExpiredLoginKey);
					break;
				case EResult.NoConnection:
				case EResult.ServiceUnavailable:
				case EResult.Timeout:
				case EResult.TryAnotherCM:
					await Task.Delay(5000).ConfigureAwait(false);
					break;
				case EResult.RateLimitExceeded:
					ArchiLogger.LogGenericInfo(string.Format(Strings.BotRateLimitExceeded, TimeSpan.FromMinutes(LoginCooldownInMinutes).ToHumanReadable()));
					await Task.Delay(LoginCooldownInMinutes * 60 * 1000).ConfigureAwait(false);
					break;
				case EResult.AccountDisabled:
					// Do not attempt to reconnect, those failures are permanent
					return;
			}

			if (!KeepRunning || SteamClient.IsConnected) {
				return;
			}

			ArchiLogger.LogGenericInfo(Strings.BotReconnecting);
			await Connect().ConfigureAwait(false);
		}

		private void OnFreeLicense(SteamApps.FreeLicenseCallback callback) {
			if (callback == null) {
				ArchiLogger.LogNullError(nameof(callback));
			}
		}

		private async void OnFriendMsg(SteamFriends.FriendMsgCallback callback) {
			if (callback?.Sender == null) {
				ArchiLogger.LogNullError(nameof(callback) + " || " + nameof(callback.Sender));
				return;
			}

			// We should never ever get friend message in the first place when we're using FarmOffline
			// But due to Valve's fuckups, everything is possible, and this case must be checked too
			if ((callback.EntryType != EChatEntryType.ChatMsg) || string.IsNullOrEmpty(callback.Message) || (BotConfig.FarmOffline && BotConfig.HandleOfflineMessages)) {
				return;
			}

			ArchiLogger.LogGenericTrace(callback.Sender.ConvertToUInt64() + ": " + callback.Message);

			await HandleMessage(callback.Sender, callback.Sender, callback.Message).ConfigureAwait(false);
		}

		private async void OnFriendMsgHistory(SteamFriends.FriendMsgHistoryCallback callback) {
			if ((callback?.Messages == null) || (callback.SteamID == null)) {
				ArchiLogger.LogNullError(nameof(callback) + " || " + nameof(callback.Messages) + " || " + nameof(callback.SteamID));
				return;
			}

			if (callback.Messages.Count == 0) {
				return;
			}

			// Get last message
			SteamFriends.FriendMsgHistoryCallback.FriendMessage lastMessage = callback.Messages[callback.Messages.Count - 1];

			// If message is read already, return
			if (!lastMessage.Unread) {
				return;
			}

			// If message is too old, return
			if (DateTime.UtcNow.Subtract(lastMessage.Timestamp).TotalHours > 1) {
				return;
			}

			ArchiLogger.LogGenericTrace(callback.SteamID.ConvertToUInt64() + ": " + lastMessage.Message);

			// Handle the message
			await HandleMessage(callback.SteamID, callback.SteamID, lastMessage.Message).ConfigureAwait(false);
		}

		private void OnFriendsList(SteamFriends.FriendsListCallback callback) {
			if (callback?.FriendList == null) {
				ArchiLogger.LogNullError(nameof(callback) + " || " + nameof(callback.FriendList));
				return;
			}

			foreach (SteamFriends.FriendsListCallback.Friend friend in callback.FriendList.Where(friend => friend.Relationship == EFriendRelationship.RequestRecipient)) {
				if (friend.SteamID.AccountType == EAccountType.Clan) {
					if (IsMasterClanID(friend.SteamID)) {
						ArchiHandler.AcceptClanInvite(friend.SteamID, true);
					} else if (BotConfig.IsBotAccount) {
						ArchiHandler.AcceptClanInvite(friend.SteamID, false);
					}
				} else {
					if (IsFamilySharing(friend.SteamID)) {
						SteamFriends.AddFriend(friend.SteamID);
					} else if (BotConfig.IsBotAccount) {
						SteamFriends.RemoveFriend(friend.SteamID);
					}
				}
			}
		}

		private async void OnGuestPassList(SteamApps.GuestPassListCallback callback) {
			if (callback?.GuestPasses == null) {
				ArchiLogger.LogNullError(nameof(callback) + " || " + nameof(callback.GuestPasses));
				return;
			}

			if ((callback.CountGuestPassesToRedeem == 0) || (callback.GuestPasses.Count == 0) || !BotConfig.AcceptGifts) {
				return;
			}

			foreach (ulong gid in callback.GuestPasses.Select(guestPass => guestPass["gid"].AsUnsignedLong()).Where(gid => (gid != 0) && !HandledGifts.Contains(gid))) {
				HandledGifts.Add(gid);

				ArchiLogger.LogGenericInfo(string.Format(Strings.BotAcceptingGift, gid));
				await LimitGiftsRequestsAsync().ConfigureAwait(false);

				ArchiHandler.RedeemGuestPassResponseCallback response = await ArchiHandler.RedeemGuestPass(gid).ConfigureAwait(false);
				if (response != null) {
					if (response.Result == EResult.OK) {
						ArchiLogger.LogGenericInfo(Strings.Success);
					} else {
						ArchiLogger.LogGenericWarning(string.Format(Strings.WarningFailedWithError, response.Result));
					}
				} else {
					ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				}
			}
		}

		private async void OnLicenseList(SteamApps.LicenseListCallback callback) {
			if (callback?.LicenseList == null) {
				ArchiLogger.LogNullError(nameof(callback) + " || " + nameof(callback.LicenseList));
				return;
			}

			HashSet<uint> ownedPackageIDs = new HashSet<uint>(callback.LicenseList.Select(license => license.PackageID));
			OwnedPackageIDs.ReplaceIfNeededWith(ownedPackageIDs);

			await Task.Delay(1000).ConfigureAwait(false); // Wait a second for eventual PlayingSessionStateCallback or SharedLibraryLockStatusCallback

			// Normally we ResetGamesPlayed() in OnFarmingStopped() but there is no farming event if CardsFarmer module is disabled
			// Therefore, trigger extra ResetGamesPlayed(), but only in this specific case
			if (CardsFarmer.Paused) {
				ResetGamesPlayed();
			}

			// We trigger OnNewGameAdded() anyway, as CardsFarmer has other things to handle regardless of being Paused or not
			await CardsFarmer.OnNewGameAdded().ConfigureAwait(false);
		}

		private void OnLoggedOff(SteamUser.LoggedOffCallback callback) {
			if (callback == null) {
				ArchiLogger.LogNullError(nameof(callback));
				return;
			}

			ArchiLogger.LogGenericInfo(string.Format(Strings.BotLoggedOff, callback.Result));

			switch (callback.Result) {
				case EResult.LogonSessionReplaced:
					ArchiLogger.LogGenericError(Strings.BotLogonSessionReplaced);
					Stop();
					break;
			}
		}

		private async void OnLoggedOn(SteamUser.LoggedOnCallback callback) {
			if (callback == null) {
				ArchiLogger.LogNullError(nameof(callback));
				return;
			}

			// Always reset one-time-only access tokens
			AuthCode = TwoFactorCode = null;

			// Keep LastLogOnResult for OnDisconnected()
			LastLogOnResult = callback.Result;

			HeartBeatFailures = 0;
			StopConnectionFailureTimer();

			switch (callback.Result) {
				case EResult.AccountLogonDenied:
					string authCode = Program.GetUserInput(ASF.EUserInputType.SteamGuard, BotName);
					if (string.IsNullOrEmpty(authCode)) {
						Stop();
						break;
					}

					SetUserInput(ASF.EUserInputType.SteamGuard, authCode);
					break;
				case EResult.AccountLoginDeniedNeedTwoFactor:
					if (!HasMobileAuthenticator) {
						string twoFactorCode = Program.GetUserInput(ASF.EUserInputType.TwoFactorAuthentication, BotName);
						if (string.IsNullOrEmpty(twoFactorCode)) {
							Stop();
							break;
						}

						SetUserInput(ASF.EUserInputType.TwoFactorAuthentication, twoFactorCode);
					}

					break;
				case EResult.OK:
					ArchiLogger.LogGenericInfo(Strings.BotLoggedOn);

					// Old status for these doesn't matter, we'll update them if needed
					LibraryLockedBySteamID = TwoFactorCodeFailures = 0;
					PlayingBlocked = false;

					if (PlayingWasBlocked && (PlayingWasBlockedTimer == null)) {
						PlayingWasBlockedTimer = new Timer(
							e => ResetPlayingWasBlockedWithTimer(),
							null,
							TimeSpan.FromSeconds(MinPlayingBlockedTTL), // Delay
							Timeout.InfiniteTimeSpan // Period
						);
					}

					AccountFlags = callback.AccountFlags;

					if (IsAccountLimited) {
						ArchiLogger.LogGenericWarning(Strings.BotAccountLimited);
					}

					if (IsAccountLocked) {
						ArchiLogger.LogGenericWarning(Strings.BotAccountLocked);
					}

					if ((callback.CellID != 0) && (Program.GlobalDatabase.CellID != callback.CellID)) {
						Program.GlobalDatabase.CellID = callback.CellID;
						CMClient.Servers.CellID = callback.CellID;
					}

					if (!HasMobileAuthenticator) {
						// Support and convert 2FA files
						string maFilePath = Path.Combine(SharedInfo.ConfigDirectory, callback.ClientSteamID.ConvertToUInt64() + ".maFile");
						if (File.Exists(maFilePath)) {
							ImportAuthenticator(maFilePath);
						}
					}

					if (string.IsNullOrEmpty(BotConfig.SteamParentalPIN)) {
						string steamParentalPIN = Program.GetUserInput(ASF.EUserInputType.SteamParentalPIN, BotName);
						if (string.IsNullOrEmpty(steamParentalPIN)) {
							Stop();
							break;
						}

						SetUserInput(ASF.EUserInputType.SteamParentalPIN, steamParentalPIN);
					}

					if (!await ArchiWebHandler.Init(callback.ClientSteamID, SteamClient.ConnectedUniverse, callback.WebAPIUserNonce, BotConfig.SteamParentalPIN).ConfigureAwait(false)) {
						if (!await RefreshSession().ConfigureAwait(false)) {
							break;
						}
					}

					// Sometimes Steam won't send us our own PersonaStateCallback, so request it explicitly
					RequestPersonaStateUpdate();

					InitializeFamilySharing().Forget();
					MarkInventoryIfNeeded().Forget();

					if (BotConfig.SteamMasterClanID != 0) {
						Task.Run(async () => {
							await ArchiWebHandler.JoinGroup(BotConfig.SteamMasterClanID).ConfigureAwait(false);
							JoinMasterChat();
						}).Forget();
					}

					Statistics?.OnLoggedOn().Forget();
					Trading.OnNewTrade().Forget();
					break;
				case EResult.InvalidPassword:
				case EResult.NoConnection:
				case EResult.RateLimitExceeded:
				case EResult.ServiceUnavailable:
				case EResult.Timeout:
				case EResult.TryAnotherCM:
				case EResult.TwoFactorCodeMismatch:
					ArchiLogger.LogGenericWarning(string.Format(Strings.BotUnableToLogin, callback.Result, callback.ExtendedResult));

					if ((callback.Result == EResult.TwoFactorCodeMismatch) && HasMobileAuthenticator) {
						if (++TwoFactorCodeFailures >= MaxTwoFactorCodeFailures) {
							TwoFactorCodeFailures = 0;
							ArchiLogger.LogGenericError(string.Format(Strings.BotInvalidAuthenticatorDuringLogin, MaxTwoFactorCodeFailures));
							Stop();
						}
					}

					break;
				case EResult.AccountDisabled:
					// Those failures are permanent, we should Stop() the bot if any of those happen
					ArchiLogger.LogGenericWarning(string.Format(Strings.BotUnableToLogin, callback.Result, callback.ExtendedResult));
					Stop();
					break;
				default:
					// Unexpected result, shutdown immediately
					ArchiLogger.LogGenericError(string.Format(Strings.BotUnableToLogin, callback.Result, callback.ExtendedResult));
					Stop();
					break;
			}
		}

		private void OnLoginKey(SteamUser.LoginKeyCallback callback) {
			if (string.IsNullOrEmpty(callback?.LoginKey)) {
				ArchiLogger.LogNullError(nameof(callback) + " || " + nameof(callback.LoginKey));
				return;
			}

			string loginKey = callback.LoginKey;
			if (!string.IsNullOrEmpty(loginKey)) {
				loginKey = CryptoHelper.Encrypt(BotConfig.PasswordFormat, loginKey);
			}

			BotDatabase.LoginKey = loginKey;
			SteamUser.AcceptNewLoginKey(callback);
		}

		private void OnMachineAuth(SteamUser.UpdateMachineAuthCallback callback) {
			if (callback == null) {
				ArchiLogger.LogNullError(nameof(callback));
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
				ArchiLogger.LogGenericException(e);
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

		private void OnNotifications(ArchiHandler.NotificationsCallback callback) {
			if (callback == null) {
				ArchiLogger.LogNullError(nameof(callback));
				return;
			}

			if ((callback.Notifications == null) || (callback.Notifications.Count == 0)) {
				return;
			}

			foreach (ArchiHandler.NotificationsCallback.ENotification notification in callback.Notifications) {
				switch (notification) {
					case ArchiHandler.NotificationsCallback.ENotification.Items:
						CardsFarmer.OnNewItemsNotification().Forget();
						MarkInventoryIfNeeded().Forget();
						break;
					case ArchiHandler.NotificationsCallback.ENotification.Trading:
						Trading.OnNewTrade().Forget();
						break;
				}
			}
		}

		private void OnOfflineMessage(ArchiHandler.OfflineMessageCallback callback) {
			if (callback == null) {
				ArchiLogger.LogNullError(nameof(callback));
				return;
			}

			if ((callback.OfflineMessagesCount == 0) || !BotConfig.HandleOfflineMessages) {
				return;
			}

			SteamFriends.RequestOfflineMessages();
		}

		private async void OnPersonaState(SteamFriends.PersonaStateCallback callback) {
			if (callback == null) {
				ArchiLogger.LogNullError(nameof(callback));
				return;
			}

			if (callback.FriendID == SteamID) {
				Events.OnPersonaState(this, callback);
				Statistics?.OnPersonaState(callback).Forget();
			} else if ((callback.FriendID == LibraryLockedBySteamID) && (callback.GameID == 0)) {
				LibraryLockedBySteamID = 0;
				await CheckOccupationStatus().ConfigureAwait(false);
			}
		}

		private void OnPICSProductInfo(SteamApps.PICSProductInfoCallback callback) {
			if (callback == null) {
				ArchiLogger.LogNullError(nameof(callback));
			}
		}

		private async void OnPlayingSessionState(ArchiHandler.PlayingSessionStateCallback callback) {
			if (callback == null) {
				ArchiLogger.LogNullError(nameof(callback));
				return;
			}

			if (callback.PlayingBlocked == PlayingBlocked) {
				return; // No status update, we're not interested
			}

			PlayingBlocked = callback.PlayingBlocked;
			await CheckOccupationStatus().ConfigureAwait(false);
		}

		private void OnPurchaseResponse(ArchiHandler.PurchaseResponseCallback callback) {
			if (callback == null) {
				ArchiLogger.LogNullError(nameof(callback));
			}
		}

		private async void OnSharedLibraryLockStatus(ArchiHandler.SharedLibraryLockStatusCallback callback) {
			if (callback == null) {
				ArchiLogger.LogNullError(nameof(callback));
				return;
			}

			// Ignore no status updates
			if (LibraryLockedBySteamID == 0) {
				if ((callback.LibraryLockedBySteamID == 0) || (callback.LibraryLockedBySteamID == SteamID)) {
					return;
				}

				LibraryLockedBySteamID = callback.LibraryLockedBySteamID;
			} else {
				if ((callback.LibraryLockedBySteamID != 0) && (callback.LibraryLockedBySteamID != SteamID)) {
					return;
				}

				if (SteamFriends.GetFriendGamePlayed(LibraryLockedBySteamID) != 0) {
					return;
				}

				LibraryLockedBySteamID = 0;
			}

			await CheckOccupationStatus().ConfigureAwait(false);
		}

		private void OnWebAPIUserNonce(SteamUser.WebAPIUserNonceCallback callback) {
			if (callback == null) {
				ArchiLogger.LogNullError(nameof(callback));
			}
		}

		private void ResetGamesPlayed() {
			if (!IsPlayingPossible || (FamilySharingInactivityTimer != null)) {
				return;
			}

			ArchiHandler.PlayGames(BotConfig.GamesPlayedWhileIdle, BotConfig.CustomGamePlayedWhileIdle);
		}

		private void ResetPlayingWasBlockedWithTimer() {
			PlayingWasBlocked = false;
			StopPlayingWasBlockedTimer();
		}

		private async Task<string> Response2FA(ulong steamID) {
			if (steamID == 0) {
				ArchiLogger.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			if (!HasMobileAuthenticator) {
				return FormatBotResponse(Strings.BotNoASFAuthenticator);
			}

			string token = await BotDatabase.MobileAuthenticator.GenerateToken().ConfigureAwait(false);
			return FormatBotResponse(!string.IsNullOrEmpty(token) ? string.Format(Strings.BotAuthenticatorToken, token) : Strings.WarningFailed);
		}

		private static async Task<string> Response2FA(ulong steamID, string botNames) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames));
				return null;
			}

			HashSet<Bot> bots = GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(bot => bot.Response2FA(steamID));
			ICollection<string> results;

			switch (Program.GlobalConfig.OptimizationMode) {
				case GlobalConfig.EOptimizationMode.MinMemoryUsage:
					results = new List<string>(bots.Count);
					foreach (Task<string> task in tasks) {
						results.Add(await task.ConfigureAwait(false));
					}

					break;
				default:
					results = await Task.WhenAll(tasks).ConfigureAwait(false);
					break;
			}

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));
			return responses.Count > 0 ? string.Join("", responses) : null;
		}

		private async Task<string> Response2FAConfirm(ulong steamID, bool confirm) {
			if (steamID == 0) {
				ArchiLogger.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			if (!IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			if (!HasMobileAuthenticator) {
				return FormatBotResponse(Strings.BotNoASFAuthenticator);
			}

			if (await AcceptConfirmations(confirm).ConfigureAwait(false)) {
				return FormatBotResponse(Strings.Success);
			}

			return FormatBotResponse(Strings.WarningFailed);
		}

		private static async Task<string> Response2FAConfirm(ulong steamID, string botNames, bool confirm) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames));
				return null;
			}

			HashSet<Bot> bots = GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(bot => bot.Response2FAConfirm(steamID, confirm));
			ICollection<string> results;

			switch (Program.GlobalConfig.OptimizationMode) {
				case GlobalConfig.EOptimizationMode.MinMemoryUsage:
					results = new List<string>(bots.Count);
					foreach (Task<string> task in tasks) {
						results.Add(await task.ConfigureAwait(false));
					}

					break;
				default:
					results = await Task.WhenAll(tasks).ConfigureAwait(false);
					break;
			}

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));
			return responses.Count > 0 ? string.Join("", responses) : null;
		}

		private async Task<string> ResponseAddLicense(ulong steamID, ICollection<uint> gameIDs) {
			if ((steamID == 0) || (gameIDs == null) || (gameIDs.Count == 0)) {
				ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(gameIDs) + " || " + nameof(gameIDs.Count));
				return null;
			}

			if (!IsOperator(steamID)) {
				return null;
			}

			if (!IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			StringBuilder response = new StringBuilder();
			foreach (uint gameID in gameIDs) {
				SteamApps.FreeLicenseCallback callback;

				try {
					callback = await SteamApps.RequestFreeLicense(gameID);
				} catch (Exception e) {
					ArchiLogger.LogGenericException(e);
					response.Append(FormatBotResponse(string.Format(Strings.BotAddLicense, gameID, EResult.Timeout)));
					break;
				}

				if (callback == null) {
					response.Append(FormatBotResponse(string.Format(Strings.BotAddLicense, gameID, EResult.Timeout)));
					break;
				}

				if (callback.GrantedApps.Count > 0) {
					response.Append(FormatBotResponse(string.Format(Strings.BotAddLicenseWithItems, gameID, callback.Result, string.Join(", ", callback.GrantedApps))));
				} else if (callback.GrantedPackages.Count > 0) {
					response.Append(FormatBotResponse(string.Format(Strings.BotAddLicenseWithItems, gameID, callback.Result, string.Join(", ", callback.GrantedPackages))));
				} else if (await ArchiWebHandler.AddFreeLicense(gameID).ConfigureAwait(false)) {
					response.Append(FormatBotResponse(string.Format(Strings.BotAddLicenseWithItems, gameID, EResult.OK, gameID)));
				} else {
					response.Append(FormatBotResponse(string.Format(Strings.BotAddLicense, gameID, EResult.AccessDenied)));
				}
			}

			return response.Length > 0 ? response.ToString() : null;
		}

		private async Task<string> ResponseAddLicense(ulong steamID, string games) {
			if ((steamID == 0) || string.IsNullOrEmpty(games)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(games));
				return null;
			}

			if (!IsOperator(steamID)) {
				return null;
			}

			if (!IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			string[] gameIDs = games.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

			HashSet<uint> gamesToRedeem = new HashSet<uint>();
			foreach (string game in gameIDs) {
				if (!uint.TryParse(game, out uint gameID) || (gameID == 0)) {
					return FormatBotResponse(string.Format(Strings.ErrorParsingObject, nameof(gameID)));
				}

				gamesToRedeem.Add(gameID);
			}

			if (gamesToRedeem.Count == 0) {
				return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(gamesToRedeem)));
			}

			return await ResponseAddLicense(steamID, gamesToRedeem).ConfigureAwait(false);
		}

		private static async Task<string> ResponseAddLicense(ulong steamID, string botNames, string games) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(games)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(games));
				return null;
			}

			HashSet<Bot> bots = GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(bot => bot.ResponseAddLicense(steamID, games));
			ICollection<string> results;

			switch (Program.GlobalConfig.OptimizationMode) {
				case GlobalConfig.EOptimizationMode.MinMemoryUsage:
					results = new List<string>(bots.Count);
					foreach (Task<string> task in tasks) {
						results.Add(await task.ConfigureAwait(false));
					}

					break;
				default:
					results = await Task.WhenAll(tasks).ConfigureAwait(false);
					break;
			}

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));
			return responses.Count > 0 ? string.Join("", responses) : null;
		}

		private string ResponseAPI(ulong steamID) {
			if (steamID != 0) {
				return IsMaster(steamID) ? GetAPIStatus(Bots.Where(kv => kv.Value == this).ToDictionary(kv => kv.Key, kv => kv.Value)) : null;
			}

			ASF.ArchiLogger.LogNullError(nameof(steamID));
			return null;
		}

		private static string ResponseAPI(ulong steamID, string botNames) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames));
				return null;
			}

			HashSet<Bot> bots = GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			return GetAPIStatus(Bots.Where(kv => bots.Contains(kv.Value) && kv.Value.IsMaster(steamID)).ToDictionary(kv => kv.Key, kv => kv.Value));
		}

		private static async Task<string> ResponseBlacklist(ulong steamID, string botNames) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames));
				return null;
			}

			HashSet<Bot> bots = GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(bot => Task.Run(() => bot.ResponseBlacklist(steamID)));
			ICollection<string> results;

			switch (Program.GlobalConfig.OptimizationMode) {
				case GlobalConfig.EOptimizationMode.MinMemoryUsage:
					results = new List<string>(bots.Count);
					foreach (Task<string> task in tasks) {
						results.Add(await task.ConfigureAwait(false));
					}

					break;
				default:
					results = await Task.WhenAll(tasks).ConfigureAwait(false);
					break;
			}

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));
			return responses.Count > 0 ? string.Join("", responses) : null;
		}

		private string ResponseBlacklist(ulong steamID) {
			if (steamID != 0) {
				return IsMaster(steamID) ? FormatBotResponse(string.Join(", ", BotDatabase.GetBlacklistedFromTradesSteamIDs())) : null;
			}

			ArchiLogger.LogNullError(nameof(steamID));
			return null;
		}

		private string ResponseBlacklistAdd(ulong steamID, string targetsText) {
			if ((steamID == 0) || string.IsNullOrEmpty(targetsText)) {
				ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(targetsText));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			string[] targets = targetsText.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

			HashSet<ulong> targetIDs = new HashSet<ulong>();
			foreach (string target in targets) {
				if (!ulong.TryParse(target, out ulong targetID) || (targetID == 0)) {
					return FormatBotResponse(string.Format(Strings.ErrorParsingObject, nameof(targetID)));
				}

				targetIDs.Add(targetID);
			}

			if (targetIDs.Count == 0) {
				return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(targetIDs)));
			}

			BotDatabase.AddBlacklistedFromTradesSteamIDs(targetIDs);
			return FormatBotResponse(Strings.Done);
		}

		private static async Task<string> ResponseBlacklistAdd(ulong steamID, string botNames, string targetsText) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(targetsText)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(targetsText));
				return null;
			}

			HashSet<Bot> bots = GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(bot => Task.Run(() => bot.ResponseBlacklistAdd(steamID, targetsText)));
			ICollection<string> results;

			switch (Program.GlobalConfig.OptimizationMode) {
				case GlobalConfig.EOptimizationMode.MinMemoryUsage:
					results = new List<string>(bots.Count);
					foreach (Task<string> task in tasks) {
						results.Add(await task.ConfigureAwait(false));
					}

					break;
				default:
					results = await Task.WhenAll(tasks).ConfigureAwait(false);
					break;
			}

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));
			return responses.Count > 0 ? string.Join("", responses) : null;
		}

		private static async Task<string> ResponseBlacklistRemove(ulong steamID, string botNames, string targetsText) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(targetsText)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(targetsText));
				return null;
			}

			HashSet<Bot> bots = GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(bot => Task.Run(() => bot.ResponseBlacklistRemove(steamID, targetsText)));
			ICollection<string> results;

			switch (Program.GlobalConfig.OptimizationMode) {
				case GlobalConfig.EOptimizationMode.MinMemoryUsage:
					results = new List<string>(bots.Count);
					foreach (Task<string> task in tasks) {
						results.Add(await task.ConfigureAwait(false));
					}

					break;
				default:
					results = await Task.WhenAll(tasks).ConfigureAwait(false);
					break;
			}

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));
			return responses.Count > 0 ? string.Join("", responses) : null;
		}

		private string ResponseBlacklistRemove(ulong steamID, string targetsText) {
			if ((steamID == 0) || string.IsNullOrEmpty(targetsText)) {
				ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(targetsText));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			string[] targets = targetsText.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

			HashSet<ulong> targetIDs = new HashSet<ulong>();
			foreach (string target in targets) {
				if (!ulong.TryParse(target, out ulong targetID) || (targetID == 0)) {
					return FormatBotResponse(string.Format(Strings.ErrorParsingObject, nameof(targetID)));
				}

				targetIDs.Add(targetID);
			}

			if (targetIDs.Count == 0) {
				return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(targetIDs)));
			}

			BotDatabase.RemoveBlacklistedFromTradesSteamIDs(targetIDs);
			return FormatBotResponse(Strings.Done);
		}

		private static string ResponseExit(ulong steamID) {
			if (steamID == 0) {
				ASF.ArchiLogger.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsOwner(steamID)) {
				return null;
			}

			// Schedule the task after some time so user can receive response
			Task.Run(async () => {
				await Task.Delay(1000).ConfigureAwait(false);
				await Program.Exit().ConfigureAwait(false);
			}).Forget();

			return FormatStaticResponse(Strings.Done);
		}

		private async Task<string> ResponseFarm(ulong steamID) {
			if (steamID == 0) {
				ArchiLogger.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			if (!IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			await CardsFarmer.StopFarming().ConfigureAwait(false);
			CardsFarmer.StartFarming().Forget();

			return FormatBotResponse(Strings.Done);
		}

		private static async Task<string> ResponseFarm(ulong steamID, string botNames) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames));
				return null;
			}

			HashSet<Bot> bots = GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(bot => bot.ResponseFarm(steamID));
			ICollection<string> results;

			switch (Program.GlobalConfig.OptimizationMode) {
				case GlobalConfig.EOptimizationMode.MinMemoryUsage:
					results = new List<string>(bots.Count);
					foreach (Task<string> task in tasks) {
						results.Add(await task.ConfigureAwait(false));
					}

					break;
				default:
					results = await Task.WhenAll(tasks).ConfigureAwait(false);
					break;
			}

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));
			return responses.Count > 0 ? string.Join("", responses) : null;
		}

		private string ResponseHelp(ulong steamID) {
			if (steamID != 0) {
				return IsFamilySharing(steamID) ? FormatBotResponse("https://github.com/" + SharedInfo.GithubRepo + "/wiki/Commands") : null;
			}

			ArchiLogger.LogNullError(nameof(steamID));
			return null;
		}

		private string ResponseInput(ulong steamID, string propertyName, string inputValue) {
			if ((steamID == 0) || string.IsNullOrEmpty(propertyName) || string.IsNullOrEmpty(inputValue)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(propertyName) + " || " + nameof(inputValue));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			if (!Program.GlobalConfig.Headless) {
				return FormatBotResponse(Strings.ErrorFunctionOnlyInHeadlessMode);
			}

			if (!Enum.TryParse(propertyName, true, out ASF.EUserInputType inputType) || (inputType == ASF.EUserInputType.Unknown)) {
				return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(inputType)));
			}

			SetUserInput(inputType, inputValue);
			return FormatBotResponse(Strings.Done);
		}

		private static async Task<string> ResponseInput(ulong steamID, string botNames, string propertyName, string inputValue) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(propertyName) || string.IsNullOrEmpty(inputValue)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(propertyName) + " || " + nameof(inputValue));
				return null;
			}

			HashSet<Bot> bots = GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(bot => Task.Run(() => bot.ResponseInput(steamID, propertyName, inputValue)));
			ICollection<string> results;

			switch (Program.GlobalConfig.OptimizationMode) {
				case GlobalConfig.EOptimizationMode.MinMemoryUsage:
					results = new List<string>(bots.Count);
					foreach (Task<string> task in tasks) {
						results.Add(await task.ConfigureAwait(false));
					}

					break;
				default:
					results = await Task.WhenAll(tasks).ConfigureAwait(false);
					break;
			}

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));
			return responses.Count > 0 ? string.Join("", responses) : null;
		}

		private async Task<string> ResponseLoot(ulong steamID) {
			if (steamID == 0) {
				ArchiLogger.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			if (!IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			if (!LootingAllowed) {
				return FormatBotResponse(Strings.BotLootingTemporarilyDisabled);
			}

			if (BotConfig.LootableTypes.Count == 0) {
				return FormatBotResponse(Strings.BotLootingNoLootableTypes);
			}

			ulong targetSteamMasterID = GetFirstSteamMasterID();
			if (targetSteamMasterID == 0) {
				return FormatBotResponse(Strings.BotLootingMasterNotDefined);
			}

			if (targetSteamMasterID == SteamID) {
				return FormatBotResponse(Strings.BotLootingYourself);
			}

			HashSet<Steam.Item> inventory = await ArchiWebHandler.GetMySteamInventory(true, BotConfig.LootableTypes).ConfigureAwait(false);
			if ((inventory == null) || (inventory.Count == 0)) {
				return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(inventory)));
			}

			if (!await ArchiWebHandler.MarkSentTrades().ConfigureAwait(false)) {
				return FormatBotResponse(Strings.BotLootingFailed);
			}

			if (!await ArchiWebHandler.SendTradeOffer(inventory, targetSteamMasterID, BotConfig.SteamTradeToken).ConfigureAwait(false)) {
				return FormatBotResponse(Strings.BotLootingFailed);
			}

			await Task.Delay(3000).ConfigureAwait(false); // Sometimes we can be too fast for Steam servers to generate confirmations, wait a short moment
			await AcceptConfirmations(true, Steam.ConfirmationDetails.EType.Trade, targetSteamMasterID).ConfigureAwait(false);
			return FormatBotResponse(Strings.BotLootingSuccess);
		}

		private static async Task<string> ResponseLoot(ulong steamID, string botNames) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames));
				return null;
			}

			HashSet<Bot> bots = GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(bot => bot.ResponseLoot(steamID));
			ICollection<string> results;

			switch (Program.GlobalConfig.OptimizationMode) {
				case GlobalConfig.EOptimizationMode.MinMemoryUsage:
					results = new List<string>(bots.Count);
					foreach (Task<string> task in tasks) {
						results.Add(await task.ConfigureAwait(false));
					}

					break;
				default:
					results = await Task.WhenAll(tasks).ConfigureAwait(false);
					break;
			}

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));
			return responses.Count > 0 ? string.Join("", responses) : null;
		}

		private string ResponseLootSwitch(ulong steamID) {
			if (steamID == 0) {
				ArchiLogger.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			LootingAllowed = !LootingAllowed;
			return FormatBotResponse(LootingAllowed ? Strings.BotLootingNowEnabled : Strings.BotLootingNowDisabled);
		}

		private static async Task<string> ResponseLootSwitch(ulong steamID, string botNames) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames));
				return null;
			}

			HashSet<Bot> bots = GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(bot => Task.Run(() => bot.ResponseLootSwitch(steamID)));
			ICollection<string> results;

			switch (Program.GlobalConfig.OptimizationMode) {
				case GlobalConfig.EOptimizationMode.MinMemoryUsage:
					results = new List<string>(bots.Count);
					foreach (Task<string> task in tasks) {
						results.Add(await task.ConfigureAwait(false));
					}

					break;
				default:
					results = await Task.WhenAll(tasks).ConfigureAwait(false);
					break;
			}

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));
			return responses.Count > 0 ? string.Join("", responses) : null;
		}

		private async Task<string> ResponseNickname(ulong steamID, string nickname) {
			if ((steamID == 0) || string.IsNullOrEmpty(nickname)) {
				ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(nickname));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			if (!IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			SteamFriends.PersonaChangeCallback result;

			try {
				result = await SteamFriends.SetPersonaName(nickname);
			} catch (Exception e) {
				ArchiLogger.LogGenericException(e);
				return FormatBotResponse(Strings.WarningFailed);
			}

			if ((result == null) || (result.Result != EResult.OK)) {
				return FormatBotResponse(Strings.WarningFailed);
			}

			return FormatBotResponse(Strings.Done);
		}

		private static async Task<string> ResponseNickname(ulong steamID, string botNames, string nickname) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(nickname)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(nickname));
				return null;
			}

			HashSet<Bot> bots = GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(bot => bot.ResponseNickname(steamID, nickname));
			ICollection<string> results;

			switch (Program.GlobalConfig.OptimizationMode) {
				case GlobalConfig.EOptimizationMode.MinMemoryUsage:
					results = new List<string>(bots.Count);
					foreach (Task<string> task in tasks) {
						results.Add(await task.ConfigureAwait(false));
					}

					break;
				default:
					results = await Task.WhenAll(tasks).ConfigureAwait(false);
					break;
			}

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));
			return responses.Count > 0 ? string.Join("", responses) : null;
		}

		private async Task<string> ResponseOwns(ulong steamID, string query) {
			if ((steamID == 0) || string.IsNullOrEmpty(query)) {
				ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(query));
				return null;
			}

			if (!IsOperator(steamID)) {
				return null;
			}

			if (!IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			Dictionary<uint, string> ownedGames;
			if (await ArchiWebHandler.HasValidApiKey().ConfigureAwait(false)) {
				ownedGames = await ArchiWebHandler.GetOwnedGames(SteamID).ConfigureAwait(false);
			} else {
				ownedGames = await ArchiWebHandler.GetMyOwnedGames().ConfigureAwait(false);
			}

			if ((ownedGames == null) || (ownedGames.Count == 0)) {
				return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(ownedGames)));
			}

			StringBuilder response = new StringBuilder();

			if (query.Equals("*")) {
				foreach (KeyValuePair<uint, string> ownedGame in ownedGames) {
					response.Append(FormatBotResponse(string.Format(Strings.BotOwnedAlreadyWithName, ownedGame.Key, ownedGame.Value)));
				}
			} else {
				string[] games = query.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

				foreach (string game in games) {
					// Check if this is gameID
					if (uint.TryParse(game, out uint gameID) && (gameID != 0)) {
						if (OwnedPackageIDs.Contains(gameID)) {
							response.Append(FormatBotResponse(string.Format(Strings.BotOwnedAlready, gameID)));
							continue;
						}

						response.Append(FormatBotResponse(ownedGames.TryGetValue(gameID, out string ownedName) ? string.Format(Strings.BotOwnedAlreadyWithName, gameID, ownedName) : string.Format(Strings.BotNotOwnedYet, gameID)));

						continue;
					}

					// This is a string, so check our entire library
					foreach (KeyValuePair<uint, string> ownedGame in ownedGames.Where(ownedGame => ownedGame.Value.IndexOf(game, StringComparison.OrdinalIgnoreCase) >= 0)) {
						response.Append(FormatBotResponse(string.Format(Strings.BotOwnedAlreadyWithName, ownedGame.Key, ownedGame.Value)));
					}
				}
			}

			return response.Length > 0 ? response.ToString() : FormatBotResponse(string.Format(Strings.BotNotOwnedYet, query));
		}

		private static async Task<string> ResponseOwns(ulong steamID, string botNames, string query) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(query)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(query));
				return null;
			}

			HashSet<Bot> bots = GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(bot => bot.ResponseOwns(steamID, query));
			ICollection<string> results;

			switch (Program.GlobalConfig.OptimizationMode) {
				case GlobalConfig.EOptimizationMode.MinMemoryUsage:
					results = new List<string>(bots.Count);
					foreach (Task<string> task in tasks) {
						results.Add(await task.ConfigureAwait(false));
					}

					break;
				default:
					results = await Task.WhenAll(tasks).ConfigureAwait(false);
					break;
			}

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));
			return responses.Count > 0 ? string.Join("", responses) : null;
		}

		private string ResponsePassword(ulong steamID) {
			if (steamID == 0) {
				ArchiLogger.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			return !string.IsNullOrEmpty(BotConfig.SteamPassword) ? FormatBotResponse(string.Format(Strings.BotEncryptedPassword, CryptoHelper.ECryptoMethod.AES, CryptoHelper.Encrypt(CryptoHelper.ECryptoMethod.AES, BotConfig.SteamPassword))) + FormatBotResponse(string.Format(Strings.BotEncryptedPassword, CryptoHelper.ECryptoMethod.ProtectedDataForCurrentUser, CryptoHelper.Encrypt(CryptoHelper.ECryptoMethod.ProtectedDataForCurrentUser, BotConfig.SteamPassword))) : FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(BotConfig.SteamPassword)));
		}

		private static async Task<string> ResponsePassword(ulong steamID, string botNames) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames));
				return null;
			}

			HashSet<Bot> bots = GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(bot => Task.Run(() => bot.ResponsePassword(steamID)));
			ICollection<string> results;

			switch (Program.GlobalConfig.OptimizationMode) {
				case GlobalConfig.EOptimizationMode.MinMemoryUsage:
					results = new List<string>(bots.Count);
					foreach (Task<string> task in tasks) {
						results.Add(await task.ConfigureAwait(false));
					}

					break;
				default:
					results = await Task.WhenAll(tasks).ConfigureAwait(false);
					break;
			}

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));
			return responses.Count > 0 ? string.Join("", responses) : null;
		}

		private async Task<string> ResponsePause(ulong steamID, bool sticky) {
			if (steamID == 0) {
				ArchiLogger.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsFamilySharing(steamID)) {
				return null;
			}

			if (sticky && !IsOperator(steamID)) {
				return FormatBotResponse(Strings.ErrorAccessDenied);
			}

			if (!IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			if (CardsFarmer.Paused) {
				return FormatBotResponse(Strings.BotAutomaticIdlingPausedAlready);
			}

			await CardsFarmer.Pause(sticky).ConfigureAwait(false);

			if (IsOperator(steamID)) {
				return FormatBotResponse(Strings.BotAutomaticIdlingNowPaused);
			}

			StartFamilySharingInactivityTimer();
			return FormatBotResponse(string.Format(Strings.BotAutomaticIdlingPausedWithCountdown, TimeSpan.FromMinutes(FamilySharingInactivityMinutes).ToHumanReadable()));
		}

		private static async Task<string> ResponsePause(ulong steamID, string botNames, bool sticky) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames));
				return null;
			}

			HashSet<Bot> bots = GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(bot => bot.ResponsePause(steamID, sticky));
			ICollection<string> results;

			switch (Program.GlobalConfig.OptimizationMode) {
				case GlobalConfig.EOptimizationMode.MinMemoryUsage:
					results = new List<string>(bots.Count);
					foreach (Task<string> task in tasks) {
						results.Add(await task.ConfigureAwait(false));
					}

					break;
				default:
					results = await Task.WhenAll(tasks).ConfigureAwait(false);
					break;
			}

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));
			return responses.Count > 0 ? string.Join("", responses) : null;
		}

		private async Task<string> ResponsePlay(ulong steamID, HashSet<uint> gameIDs) {
			if ((steamID == 0) || (gameIDs == null) || (gameIDs.Count == 0)) {
				ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(gameIDs) + " || " + nameof(gameIDs.Count));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			if (!IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			if (!CardsFarmer.Paused) {
				await CardsFarmer.Pause(false).ConfigureAwait(false);
			}

			ArchiHandler.PlayGames(gameIDs);
			return FormatBotResponse(Strings.Done);
		}

		private async Task<string> ResponsePlay(ulong steamID, string games) {
			if ((steamID == 0) || string.IsNullOrEmpty(games)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(games));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			if (!IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			string[] gameIDs = games.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

			HashSet<uint> gamesToPlay = new HashSet<uint>();
			foreach (string game in gameIDs) {
				if (!uint.TryParse(game, out uint gameID)) {
					return FormatBotResponse(string.Format(Strings.ErrorParsingObject, nameof(gameID)));
				}

				gamesToPlay.Add(gameID);

				if (gamesToPlay.Count >= ArchiHandler.MaxGamesPlayedConcurrently) {
					break;
				}
			}

			if (gamesToPlay.Count == 0) {
				return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, gamesToPlay));
			}

			return await ResponsePlay(steamID, gamesToPlay).ConfigureAwait(false);
		}

		private static async Task<string> ResponsePlay(ulong steamID, string botNames, string games) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(games)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(games));
				return null;
			}

			HashSet<Bot> bots = GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(bot => bot.ResponsePlay(steamID, games));
			ICollection<string> results;

			switch (Program.GlobalConfig.OptimizationMode) {
				case GlobalConfig.EOptimizationMode.MinMemoryUsage:
					results = new List<string>(bots.Count);
					foreach (Task<string> task in tasks) {
						results.Add(await task.ConfigureAwait(false));
					}

					break;
				default:
					results = await Task.WhenAll(tasks).ConfigureAwait(false);
					break;
			}

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));
			return responses.Count > 0 ? string.Join("", responses) : null;
		}

		[SuppressMessage("ReSharper", "FunctionComplexityOverflow")]
		private async Task<string> ResponseRedeem(ulong steamID, string message, ERedeemFlags redeemFlags = ERedeemFlags.None) {
			if ((steamID == 0) || string.IsNullOrEmpty(message)) {
				ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(message));
				return null;
			}

			if (!IsOperator(steamID)) {
				return null;
			}

			if (!IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			bool forward = !redeemFlags.HasFlag(ERedeemFlags.SkipForwarding) && (redeemFlags.HasFlag(ERedeemFlags.ForceForwarding) || BotConfig.RedeemingPreferences.HasFlag(BotConfig.ERedeemingPreferences.Forwarding));
			bool distribute = !redeemFlags.HasFlag(ERedeemFlags.SkipDistribution) && (redeemFlags.HasFlag(ERedeemFlags.ForceDistribution) || BotConfig.RedeemingPreferences.HasFlag(BotConfig.ERedeemingPreferences.Distributing));
			message = message.Replace(",", Environment.NewLine);
			bool keepMissingGames = BotConfig.RedeemingPreferences.HasFlag(BotConfig.ERedeemingPreferences.KeepMissingGames);

			HashSet<string> unusedKeys = new HashSet<string>();
			StringBuilder response = new StringBuilder();

			using (StringReader reader = new StringReader(message)) {
				using (IEnumerator<Bot> enumerator = Bots.Where(bot => bot.Value.IsOperator(steamID)).OrderBy(bot => bot.Key).Select(bot => bot.Value).GetEnumerator()) {
					string key = reader.ReadLine();
					Bot currentBot = this;
					while (!string.IsNullOrEmpty(key) && (currentBot != null)) {
						if (redeemFlags.HasFlag(ERedeemFlags.Validate) && !IsValidCdKey(key)) {
							key = reader.ReadLine(); // Next key
							continue; // Keep current bot
						}

						unusedKeys.Add(key);

						if ((redeemFlags.HasFlag(ERedeemFlags.SkipInitial) && (currentBot == this)) || !currentBot.IsConnectedAndLoggedOn) {
							currentBot = null; // Either bot will be changed, or loop aborted
						} else {
							ArchiHandler.PurchaseResponseCallback result = await currentBot.ArchiHandler.RedeemKey(key).ConfigureAwait(false);
							if (result == null) {
								response.Append(FormatBotResponse(string.Format(Strings.BotRedeem, key, EPurchaseResultDetail.Timeout), currentBot.BotName));
								currentBot = null; // Either bot will be changed, or loop aborted
							} else {
								switch (result.PurchaseResultDetail) {
									case EPurchaseResultDetail.BadActivationCode:
									case EPurchaseResultDetail.CannotRedeemCodeFromClient: // Steam wallet code
									case EPurchaseResultDetail.DuplicateActivationCode:
									case EPurchaseResultDetail.NoDetail: // OK
									case EPurchaseResultDetail.Timeout:
										if (result.PurchaseResultDetail == EPurchaseResultDetail.CannotRedeemCodeFromClient) {
											// If it's a wallet code, try to redeem it, and forward the result
											// The result is final, there is no place for forwarding
											Tuple<EResult, EPurchaseResultDetail?> walletResult = await currentBot.ArchiWebHandler.RedeemWalletKey(key).ConfigureAwait(false);
											if (walletResult != null) {
												result.Result = walletResult.Item1;
												result.PurchaseResultDetail = walletResult.Item2.GetValueOrDefault(walletResult.Item1 == EResult.OK ? EPurchaseResultDetail.NoDetail : EPurchaseResultDetail.CannotRedeemCodeFromClient);
											} else {
												result.Result = EResult.Timeout;
												result.PurchaseResultDetail = EPurchaseResultDetail.Timeout;
											}
										}

										if ((result.Items != null) && (result.Items.Count > 0)) {
											response.Append(FormatBotResponse(string.Format(Strings.BotRedeemWithItems, key, result.Result + "/" + result.PurchaseResultDetail, string.Join("", result.Items)), currentBot.BotName));
										} else {
											response.Append(FormatBotResponse(string.Format(Strings.BotRedeem, key, result.Result + "/" + result.PurchaseResultDetail), currentBot.BotName));
										}

										if ((result.Result != EResult.Timeout) && (result.PurchaseResultDetail != EPurchaseResultDetail.Timeout)) {
											unusedKeys.Remove(key);
										}

										key = reader.ReadLine(); // Next key

										if (result.PurchaseResultDetail == EPurchaseResultDetail.NoDetail) {
											break; // Next bot (if needed)
										}

										continue; // Keep current bot
									case EPurchaseResultDetail.AccountLocked:
									case EPurchaseResultDetail.AlreadyPurchased:
									case EPurchaseResultDetail.DoesNotOwnRequiredApp:
									case EPurchaseResultDetail.RateLimited:
									case EPurchaseResultDetail.RestrictedCountry:
										if ((result.Items != null) && (result.Items.Count > 0)) {
											response.Append(FormatBotResponse(string.Format(Strings.BotRedeemWithItems, key, result.Result + "/" + result.PurchaseResultDetail, string.Join("", result.Items)), currentBot.BotName));
										} else {
											response.Append(FormatBotResponse(string.Format(Strings.BotRedeem, key, result.Result + "/" + result.PurchaseResultDetail), currentBot.BotName));
										}

										if (!forward || (keepMissingGames && (result.PurchaseResultDetail != EPurchaseResultDetail.AlreadyPurchased))) {
											key = reader.ReadLine(); // Next key
											break; // Next bot (if needed)
										}

										if (distribute) {
											break; // Next bot, without changing key
										}

										Dictionary<uint, string> items = result.Items ?? new Dictionary<uint, string>();

										Bot previousBot = currentBot;
										bool alreadyHandled = false;
										foreach (Bot bot in Bots.Where(bot => (bot.Value != previousBot) && (!redeemFlags.HasFlag(ERedeemFlags.SkipInitial) || (bot.Value != this)) && bot.Value.IsConnectedAndLoggedOn && ((items.Count == 0) || items.Keys.Any(packageID => !bot.Value.OwnedPackageIDs.Contains(packageID)))).OrderBy(bot => bot.Key).Select(bot => bot.Value)) {
											ArchiHandler.PurchaseResponseCallback otherResult = await bot.ArchiHandler.RedeemKey(key).ConfigureAwait(false);
											if (otherResult == null) {
												response.Append(FormatBotResponse(string.Format(Strings.BotRedeem, key, EResult.Timeout + "/" + EPurchaseResultDetail.Timeout), bot.BotName));
												continue;
											}

											switch (otherResult.PurchaseResultDetail) {
												case EPurchaseResultDetail.BadActivationCode:
												case EPurchaseResultDetail.DuplicateActivationCode:
												case EPurchaseResultDetail.NoDetail: // OK
													alreadyHandled = true; // This key is already handled, as we either redeemed it or we're sure it's dupe/invalid
													unusedKeys.Remove(key);
													break;
											}

											if ((otherResult.Items != null) && (otherResult.Items.Count > 0)) {
												response.Append(FormatBotResponse(string.Format(Strings.BotRedeemWithItems, key, otherResult.Result + "/" + otherResult.PurchaseResultDetail, string.Join("", otherResult.Items)), bot.BotName));
											} else {
												response.Append(FormatBotResponse(string.Format(Strings.BotRedeem, key, otherResult.Result + "/" + otherResult.PurchaseResultDetail), bot.BotName));
											}

											if (alreadyHandled) {
												break;
											}

											if (otherResult.Items == null) {
												continue;
											}

											foreach (KeyValuePair<uint, string> item in otherResult.Items) {
												items[item.Key] = item.Value;
											}
										}

										key = reader.ReadLine(); // Next key
										break; // Next bot (if needed)
									default:
										ASF.ArchiLogger.LogGenericWarning(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(result.PurchaseResultDetail), result.PurchaseResultDetail));

										if ((result.Items != null) && (result.Items.Count > 0)) {
											response.Append(FormatBotResponse(string.Format(Strings.BotRedeemWithItems, key, result.Result + "/" + result.PurchaseResultDetail, string.Join("", result.Items)), currentBot.BotName));
										} else {
											response.Append(FormatBotResponse(string.Format(Strings.BotRedeem, key, result.Result + "/" + result.PurchaseResultDetail), currentBot.BotName));
										}

										unusedKeys.Remove(key);

										key = reader.ReadLine(); // Next key
										break; // Next bot (if needed)
								}
							}
						}

						if (!distribute && !redeemFlags.HasFlag(ERedeemFlags.SkipInitial)) {
							continue;
						}

						do {
							currentBot = enumerator.MoveNext() ? enumerator.Current : null;
						} while ((currentBot == this) || (currentBot?.IsConnectedAndLoggedOn == false));
					}
				}
			}

			if (unusedKeys.Count > 0) {
				response.Append(FormatBotResponse(string.Format(Strings.UnusedKeys, string.Join(", ", unusedKeys))));
			}

			return response.Length > 0 ? response.ToString() : null;
		}

		private static async Task<string> ResponseRedeem(ulong steamID, string botNames, string message, ERedeemFlags redeemFlags = ERedeemFlags.None) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(message)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(message));
				return null;
			}

			HashSet<Bot> bots = GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(bot => bot.ResponseRedeem(steamID, message, redeemFlags));
			ICollection<string> results;

			switch (Program.GlobalConfig.OptimizationMode) {
				case GlobalConfig.EOptimizationMode.MinMemoryUsage:
					results = new List<string>(bots.Count);
					foreach (Task<string> task in tasks) {
						results.Add(await task.ConfigureAwait(false));
					}

					break;
				default:
					results = await Task.WhenAll(tasks).ConfigureAwait(false);
					break;
			}

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));
			return responses.Count > 0 ? string.Join("", responses) : null;
		}

		private string ResponseRejoinChat(ulong steamID) {
			if (steamID == 0) {
				ArchiLogger.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsOperator(steamID)) {
				return null;
			}

			JoinMasterChat();
			return FormatStaticResponse(Strings.Done);
		}

		private static async Task<string> ResponseRejoinChat(ulong steamID, string botNames) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames));
				return null;
			}

			HashSet<Bot> bots = GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(bot => Task.Run(() => bot.ResponseRejoinChat(steamID)));
			ICollection<string> results;

			switch (Program.GlobalConfig.OptimizationMode) {
				case GlobalConfig.EOptimizationMode.MinMemoryUsage:
					results = new List<string>(bots.Count);
					foreach (Task<string> task in tasks) {
						results.Add(await task.ConfigureAwait(false));
					}

					break;
				default:
					results = await Task.WhenAll(tasks).ConfigureAwait(false);
					break;
			}

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));
			return responses.Count > 0 ? string.Join("", responses) : null;
		}

		private static string ResponseRestart(ulong steamID) {
			if (steamID == 0) {
				ASF.ArchiLogger.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsOwner(steamID)) {
				return null;
			}

			// Schedule the task after some time so user can receive response
			Task.Run(async () => {
				await Task.Delay(1000).ConfigureAwait(false);
				await Program.Restart().ConfigureAwait(false);
			}).Forget();

			return FormatStaticResponse(Strings.Done);
		}

		private string ResponseResume(ulong steamID) {
			if (steamID == 0) {
				ArchiLogger.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsMaster(steamID) && !SteamFamilySharingIDs.Contains(steamID)) {
				return null;
			}

			if (!IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			if (!CardsFarmer.Paused) {
				return FormatBotResponse(Strings.BotAutomaticIdlingResumedAlready);
			}

			StopFamilySharingInactivityTimer();
			CardsFarmer.Resume(true).Forget();
			return FormatBotResponse(Strings.BotAutomaticIdlingNowResumed);
		}

		private static async Task<string> ResponseResume(ulong steamID, string botNames) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames));
				return null;
			}

			HashSet<Bot> bots = GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(bot => Task.Run(() => bot.ResponseResume(steamID)));
			ICollection<string> results;

			switch (Program.GlobalConfig.OptimizationMode) {
				case GlobalConfig.EOptimizationMode.MinMemoryUsage:
					results = new List<string>(bots.Count);
					foreach (Task<string> task in tasks) {
						results.Add(await task.ConfigureAwait(false));
					}

					break;
				default:
					results = await Task.WhenAll(tasks).ConfigureAwait(false);
					break;
			}

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));
			return responses.Count > 0 ? string.Join("", responses) : null;
		}

		private string ResponseStart(ulong steamID) {
			if (steamID == 0) {
				ArchiLogger.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			if (KeepRunning) {
				return FormatBotResponse(Strings.BotAlreadyRunning);
			}

			SkipFirstShutdown = true;
			Start().Forget();
			return FormatBotResponse(Strings.Done);
		}

		private static async Task<string> ResponseStart(ulong steamID, string botNames) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames));
				return null;
			}

			HashSet<Bot> bots = GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(bot => Task.Run(() => bot.ResponseStart(steamID)));
			ICollection<string> results;

			switch (Program.GlobalConfig.OptimizationMode) {
				case GlobalConfig.EOptimizationMode.MinMemoryUsage:
					results = new List<string>(bots.Count);
					foreach (Task<string> task in tasks) {
						results.Add(await task.ConfigureAwait(false));
					}

					break;
				default:
					results = await Task.WhenAll(tasks).ConfigureAwait(false);
					break;
			}

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));
			return responses.Count > 0 ? string.Join("", responses) : null;
		}

		private string ResponseStatus(ulong steamID) {
			if (steamID == 0) {
				ArchiLogger.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsFamilySharing(steamID)) {
				return null;
			}

			if (!IsConnectedAndLoggedOn) {
				return FormatBotResponse(KeepRunning ? Strings.BotStatusConnecting : Strings.BotStatusNotRunning);
			}

			if (PlayingBlocked) {
				return FormatBotResponse(Strings.BotStatusPlayingNotAvailable);
			}

			if (CardsFarmer.Paused) {
				return FormatBotResponse(Strings.BotStatusPaused);
			}

			if (IsAccountLimited) {
				return FormatBotResponse(Strings.BotStatusLimited);
			}

			if (IsAccountLocked) {
				return FormatBotResponse(Strings.BotStatusLocked);
			}

			if (CardsFarmer.CurrentGamesFarming.Count == 0) {
				return FormatBotResponse(Strings.BotStatusNotIdling);
			}

			if (CardsFarmer.CurrentGamesFarming.Count > 1) {
				return FormatBotResponse(string.Format(Strings.BotStatusIdlingList, string.Join(", ", CardsFarmer.CurrentGamesFarming.Select(game => game.AppID)), CardsFarmer.GamesToFarm.Count, CardsFarmer.GamesToFarm.Sum(game => game.CardsRemaining), CardsFarmer.TimeRemaining.ToHumanReadable()));
			}

			CardsFarmer.Game soloGame = CardsFarmer.CurrentGamesFarming.First();
			return FormatBotResponse(string.Format(Strings.BotStatusIdling, soloGame.AppID, soloGame.GameName, soloGame.CardsRemaining, CardsFarmer.GamesToFarm.Count, CardsFarmer.GamesToFarm.Sum(game => game.CardsRemaining), CardsFarmer.TimeRemaining.ToHumanReadable()));
		}

		private static async Task<string> ResponseStatus(ulong steamID, string botNames) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames));
				return null;
			}

			HashSet<Bot> bots = GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(bot => Task.Run(() => bot.ResponseStatus(steamID)));
			ICollection<string> results;

			switch (Program.GlobalConfig.OptimizationMode) {
				case GlobalConfig.EOptimizationMode.MinMemoryUsage:
					results = new List<string>(bots.Count);
					foreach (Task<string> task in tasks) {
						results.Add(await task.ConfigureAwait(false));
					}

					break;
				default:
					results = await Task.WhenAll(tasks).ConfigureAwait(false);
					break;
			}

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));
			if (responses.Count == 0) {
				return null;
			}

			if (responses.Count < Bots.Count) {
				return string.Join("", responses);
			}

			HashSet<Bot> botsRunning = new HashSet<Bot>(Bots.Values.Where(bot => bot.KeepRunning));
			string extraResponse = string.Format(Strings.BotStatusOverview, botsRunning.Count, Bots.Count, botsRunning.Sum(bot => bot.CardsFarmer.GamesToFarm.Count), botsRunning.Sum(bot => bot.CardsFarmer.GamesToFarm.Sum(game => game.CardsRemaining)));

			return string.Join("", responses) + FormatStaticResponse(extraResponse);
		}

		private string ResponseStop(ulong steamID) {
			if (steamID == 0) {
				ArchiLogger.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			if (!KeepRunning) {
				return FormatBotResponse(Strings.BotAlreadyStopped);
			}

			Stop();
			return FormatBotResponse(Strings.Done);
		}

		private static async Task<string> ResponseStop(ulong steamID, string botNames) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames));
				return null;
			}

			HashSet<Bot> bots = GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(bot => Task.Run(() => bot.ResponseStop(steamID)));
			ICollection<string> results;

			switch (Program.GlobalConfig.OptimizationMode) {
				case GlobalConfig.EOptimizationMode.MinMemoryUsage:
					results = new List<string>(bots.Count);
					foreach (Task<string> task in tasks) {
						results.Add(await task.ConfigureAwait(false));
					}

					break;
				default:
					results = await Task.WhenAll(tasks).ConfigureAwait(false);
					break;
			}

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));
			return responses.Count > 0 ? string.Join("", responses) : null;
		}

		private string ResponseUnknown(ulong steamID) {
			if (steamID != 0) {
				return IsOperator(steamID) ? FormatBotResponse(Strings.UnknownCommand) : null;
			}

			ArchiLogger.LogNullError(nameof(steamID));
			return null;
		}

		private static async Task<string> ResponseUpdate(ulong steamID) {
			if (steamID == 0) {
				ASF.ArchiLogger.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsOwner(steamID)) {
				return null;
			}

			await ASF.CheckForUpdate(true).ConfigureAwait(false);
			return FormatStaticResponse(Strings.Done);
		}

		private string ResponseVersion(ulong steamID) {
			if (steamID != 0) {
				return IsOperator(steamID) ? FormatBotResponse(string.Format(Strings.BotVersion, SharedInfo.ASF, SharedInfo.Version)) : null;
			}

			ArchiLogger.LogNullError(nameof(steamID));
			return null;
		}

		private void SendMessageToChannel(ulong steamID, string message) {
			if ((steamID == 0) || string.IsNullOrEmpty(message)) {
				ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(message));
				return;
			}

			if (!IsConnectedAndLoggedOn) {
				return;
			}

			for (int i = 0; i < message.Length; i += MaxSteamMessageLength - 2) {
				string messagePart = (i > 0 ? "…" : "") + message.Substring(i, Math.Min(MaxSteamMessageLength - 2, message.Length - i)) + (MaxSteamMessageLength - 2 < message.Length - i ? "…" : "");
				SteamFriends.SendChatRoomMessage(steamID, EChatEntryType.ChatMsg, messagePart);
			}
		}

		private void SendMessageToUser(ulong steamID, string message) {
			if ((steamID == 0) || string.IsNullOrEmpty(message)) {
				ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(message));
				return;
			}

			if (!IsConnectedAndLoggedOn) {
				return;
			}

			for (int i = 0; i < message.Length; i += MaxSteamMessageLength - 2) {
				string messagePart = (i > 0 ? "…" : "") + message.Substring(i, Math.Min(MaxSteamMessageLength - 2, message.Length - i)) + (MaxSteamMessageLength - 2 < message.Length - i ? "…" : "");
				SteamFriends.SendChatMessage(steamID, EChatEntryType.ChatMsg, messagePart);
			}
		}

		private void SetUserInput(ASF.EUserInputType inputType, string inputValue) {
			if ((inputType == ASF.EUserInputType.Unknown) || string.IsNullOrEmpty(inputValue)) {
				ArchiLogger.LogNullError(nameof(inputType) + " || " + nameof(inputValue));
			}

			// This switch should cover ONLY bot properties
			switch (inputType) {
				case ASF.EUserInputType.DeviceID:
					DeviceID = inputValue;
					break;
				case ASF.EUserInputType.Login:
					if (BotConfig != null) {
						BotConfig.SteamLogin = inputValue;
					}

					break;
				case ASF.EUserInputType.Password:
					if (BotConfig != null) {
						BotConfig.SteamPassword = inputValue;
					}

					break;
				case ASF.EUserInputType.SteamGuard:
					AuthCode = inputValue;
					break;
				case ASF.EUserInputType.SteamParentalPIN:
					if (BotConfig != null) {
						BotConfig.SteamParentalPIN = inputValue;
					}

					break;
				case ASF.EUserInputType.TwoFactorAuthentication:
					TwoFactorCode = inputValue;
					break;
				case ASF.EUserInputType.WCFHostname:
					// We don't handle global ASF properties here
					break;
				default:
					ASF.ArchiLogger.LogGenericWarning(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(inputType), inputType));
					break;
			}
		}

		private async Task Start() {
			if (!KeepRunning) {
				KeepRunning = true;
				Task.Factory.StartNew(HandleCallbacks, TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning).Forget();
				ArchiLogger.LogGenericInfo(Strings.Starting);
			}

			// Support and convert 2FA files
			if (!HasMobileAuthenticator) {
				string maFilePath = BotPath + ".maFile";
				if (File.Exists(maFilePath)) {
					ImportAuthenticator(maFilePath);
				}
			}

			await Connect().ConfigureAwait(false);
		}

		private void StartFamilySharingInactivityTimer() {
			if (FamilySharingInactivityTimer != null) {
				return;
			}

			FamilySharingInactivityTimer = new Timer(
				async e => await CheckFamilySharingInactivity().ConfigureAwait(false),
				null,
				TimeSpan.FromMinutes(FamilySharingInactivityMinutes), // Delay
				Timeout.InfiniteTimeSpan // Period
			);
		}

		private void StopConnectionFailureTimer() {
			if (ConnectionFailureTimer == null) {
				return;
			}

			ConnectionFailureTimer.Dispose();
			ConnectionFailureTimer = null;
		}

		private void StopFamilySharingInactivityTimer() {
			if (FamilySharingInactivityTimer == null) {
				return;
			}

			FamilySharingInactivityTimer.Dispose();
			FamilySharingInactivityTimer = null;
		}

		private void StopPlayingWasBlockedTimer() {
			if (PlayingWasBlockedTimer == null) {
				return;
			}

			PlayingWasBlockedTimer.Dispose();
			PlayingWasBlockedTimer = null;
		}

		[Flags]
		private enum ERedeemFlags : byte {
			None = 0,
			Validate = 1,
			ForceForwarding = 2,
			SkipForwarding = 4,
			ForceDistribution = 8,
			SkipDistribution = 16,
			SkipInitial = 32
		}
	}
}