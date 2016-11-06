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
using SteamKit2;
using SteamKit2.Internal;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Text.RegularExpressions;
using ArchiSteamFarm.JSON;
using SteamKit2.Discovery;

namespace ArchiSteamFarm {
	internal sealed class Bot : IDisposable {
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

		private const ushort CallbackSleep = 500; // In miliseconds
		private const byte FamilySharingInactivityMinutes = 5;
		private const uint LoginID = 0; // This must be the same for all ASF bots and all ASF processes
		private const ushort MaxSteamMessageLength = 2048;

		internal static readonly ConcurrentDictionary<string, Bot> Bots = new ConcurrentDictionary<string, Bot>();

		private static readonly SemaphoreSlim GiftsSemaphore = new SemaphoreSlim(1);
		private static readonly SemaphoreSlim LoginSemaphore = new SemaphoreSlim(1);

		internal readonly string BotName;
		internal readonly ArchiHandler ArchiHandler;
		internal readonly ArchiLogger ArchiLogger;
		internal readonly ArchiWebHandler ArchiWebHandler;

		private readonly string SentryFile;
		private readonly BotDatabase BotDatabase;
		private readonly CallbackManager CallbackManager;

		[JsonProperty]
		private readonly CardsFarmer CardsFarmer;

		private readonly ConcurrentHashSet<ulong> HandledGifts = new ConcurrentHashSet<ulong>();
		private readonly ConcurrentHashSet<ulong> SteamFamilySharingIDs = new ConcurrentHashSet<ulong>();
		private readonly ConcurrentHashSet<uint> OwnedPackageIDs = new ConcurrentHashSet<uint>();
		private readonly SemaphoreSlim CallbackSemaphore = new SemaphoreSlim(1);
		private readonly SemaphoreSlim InitializationSemaphore = new SemaphoreSlim(1);
		private readonly SteamApps SteamApps;
		private readonly SteamClient SteamClient;
		private readonly SteamFriends SteamFriends;
		private readonly SteamUser SteamUser;
		private readonly Timer HeartBeatTimer;
		private readonly Trading Trading;

		[JsonProperty]
		internal bool KeepRunning { get; private set; }

		internal BotConfig BotConfig { get; private set; }

		internal bool HasMobileAuthenticator => BotDatabase.MobileAuthenticator != null;
		internal bool IsConnectedAndLoggedOn => SteamClient.IsConnected && (SteamClient.SteamID != null);
		internal bool IsFarmingPossible => !PlayingBlocked && (LibraryLockedBySteamID == 0);
		internal ulong SteamID => SteamClient.SteamID;

		private bool FirstTradeSent, PlayingBlocked, SkipFirstShutdown;
		private string AuthCode, TwoFactorCode;
		private ulong LibraryLockedBySteamID;
		private EResult LastLogOnResult;
		private Timer AcceptConfirmationsTimer, FamilySharingInactivityTimer, SendItemsTimer;

		internal static string GetAPIStatus() {
			var response = new {
				Bots
			};

			try {
				return JsonConvert.SerializeObject(response);
			} catch (JsonException e) {
				ASF.ArchiLogger.LogGenericException(e);
				return null;
			}
		}

		internal static void InitializeCMs(uint cellID, IServerListProvider serverListProvider) {
			if (serverListProvider == null) {
				ASF.ArchiLogger.LogNullError(nameof(serverListProvider));
				return;
			}

			CMClient.Servers.CellID = cellID;
			CMClient.Servers.ServerListProvider = serverListProvider;
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
				return Regex.IsMatch(key, @"^[0-9A-Z]{4,7}-[0-9A-Z]{4,7}-[0-9A-Z]{4,7}(?:(?:-[0-9A-Z]{4,7})?(?:-[0-9A-Z]{4,7}))?$", RegexOptions.IgnoreCase);
			}

			ASF.ArchiLogger.LogNullError(nameof(key));
			return false;
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

		internal Bot(string botName) {
			if (string.IsNullOrEmpty(botName)) {
				throw new ArgumentNullException(nameof(botName));
			}

			if (Bots.ContainsKey(botName)) {
				throw new ArgumentException("That bot is already defined!");
			}

			BotName = botName;
			ArchiLogger = new ArchiLogger(botName);

			string botPath = Path.Combine(SharedInfo.ConfigDirectory, botName);
			string botConfigFile = botPath + ".json";

			SentryFile = botPath + ".bin";

			BotConfig = BotConfig.Load(botConfigFile);
			if (BotConfig == null) {
				ArchiLogger.LogGenericError("Your bot config is invalid, please verify content of " + botConfigFile + " and try again!");
				return;
			}

			// Register bot as available for ASF
			if (!Bots.TryAdd(botName, this)) {
				throw new ArgumentException("That bot is already defined!");
			}

			string botDatabaseFile = botPath + ".db";

			BotDatabase = BotDatabase.Load(botDatabaseFile);
			if (BotDatabase == null) {
				ArchiLogger.LogGenericError("Bot database could not be loaded, refusing to create this bot instance! In order to recreate it, remove " + botDatabaseFile + " and try again!");
				return;
			}

			if (BotDatabase.MobileAuthenticator != null) {
				BotDatabase.MobileAuthenticator.Init(this);
			} else {
				// Support and convert SDA files
				string maFilePath = botPath + ".maFile";
				if (File.Exists(maFilePath)) {
					ImportAuthenticator(maFilePath);
				}
			}

			// Initialize
			SteamClient = new SteamClient(Program.GlobalConfig.SteamProtocol);

			if (Program.GlobalConfig.Debug && Directory.Exists(SharedInfo.DebugDirectory)) {
				string debugListenerPath = Path.Combine(SharedInfo.DebugDirectory);

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
			CardsFarmer.SetInitialState(BotConfig.Paused);

			Trading = new Trading(this);

			HeartBeatTimer = new Timer(
				async e => await HeartBeat().ConfigureAwait(false),
				null,
				TimeSpan.FromMinutes(1) + TimeSpan.FromMinutes(0.2 * Bots.Count), // Delay
				TimeSpan.FromMinutes(1) // Period
			);

			Initialize().Forget();
		}

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

				CardsFarmer.SetInitialState(BotConfig.Paused);

				if (BotConfig.AcceptConfirmationsPeriod > 0) {
					TimeSpan delay = TimeSpan.FromMinutes(BotConfig.AcceptConfirmationsPeriod) + TimeSpan.FromMinutes(0.2 * Bots.Count);
					TimeSpan period = TimeSpan.FromMinutes(BotConfig.AcceptConfirmationsPeriod);

					if (AcceptConfirmationsTimer == null) {
						AcceptConfirmationsTimer = new Timer(
							async e => await AcceptConfirmations(true).ConfigureAwait(false),
							null,
							delay, // Delay
							period // Period
						);
					} else {
						AcceptConfirmationsTimer.Change(delay, period);
					}
				} else {
					AcceptConfirmationsTimer?.Change(Timeout.Infinite, Timeout.Infinite);
					AcceptConfirmationsTimer?.Dispose();
				}

				if ((BotConfig.SendTradePeriod > 0) && (BotConfig.SteamMasterID != 0)) {
					TimeSpan delay = TimeSpan.FromHours(BotConfig.SendTradePeriod) + TimeSpan.FromMinutes(Bots.Count);
					TimeSpan period = TimeSpan.FromHours(BotConfig.SendTradePeriod);

					if (SendItemsTimer == null) {
						SendItemsTimer = new Timer(
							async e => await ResponseLoot(BotConfig.SteamMasterID).ConfigureAwait(false),
							null,
							delay, // Delay
							period // Period
						);
					} else {
						SendItemsTimer.Change(delay, period);
					}
				} else {
					SendItemsTimer?.Change(Timeout.Infinite, Timeout.Infinite);
					SendItemsTimer?.Dispose();
				}

				await Initialize().ConfigureAwait(false);
			} finally {
				InitializationSemaphore.Release();
			}
		}

		private async Task Initialize() {
			if (!BotConfig.Enabled) {
				ArchiLogger.LogGenericInfo("Not starting this instance because it's disabled in config file");
				return;
			}

			// Start
			await Start().ConfigureAwait(false);
		}

		public void Dispose() {
			// Those are objects that are always being created if constructor doesn't throw exception
			ArchiWebHandler.Dispose();
			CardsFarmer.Dispose();
			HeartBeatTimer.Dispose();
			HandledGifts.Dispose();
			CallbackSemaphore.Dispose();
			InitializationSemaphore.Dispose();
			SteamFamilySharingIDs.Dispose();
			OwnedPackageIDs.Dispose();
			Trading.Dispose();

			// Those are objects that might be null and the check should be in-place
			AcceptConfirmationsTimer?.Dispose();
			FamilySharingInactivityTimer?.Dispose();
			SendItemsTimer?.Dispose();
		}

		internal async Task<bool> AcceptConfirmations(bool accept, Steam.ConfirmationDetails.EType acceptedType = Steam.ConfirmationDetails.EType.Unknown, ulong acceptedSteamID = 0, HashSet<ulong> acceptedTradeIDs = null) {
			if (BotDatabase.MobileAuthenticator == null) {
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

				Steam.ConfirmationDetails[] detailsResults = await Task.WhenAll(confirmations.Select(BotDatabase.MobileAuthenticator.GetConfirmationDetails)).ConfigureAwait(false);

				HashSet<MobileAuthenticator.Confirmation> ignoredConfirmations = new HashSet<MobileAuthenticator.Confirmation>(detailsResults.Where(details => (details != null) && (
					((acceptedSteamID != 0) && (details.OtherSteamID64 != 0) && (acceptedSteamID != details.OtherSteamID64)) ||
					((acceptedTradeIDs != null) && (details.TradeOfferID != 0) && !acceptedTradeIDs.Contains(details.TradeOfferID))
				)).Select(details => details.Confirmation));

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

			if (await ArchiWebHandler.Init(SteamClient.SteamID, SteamClient.ConnectedUniverse, callback.Nonce, BotConfig.SteamParentalPIN).ConfigureAwait(false)) {
				return true;
			}

			await Connect(true).ConfigureAwait(false);
			return false;
		}

		internal void Stop(bool withShutdownEvent = true) {
			if (!KeepRunning) {
				return;
			}

			ArchiLogger.LogGenericInfo("Stopping...");
			KeepRunning = false;

			if (SteamClient.IsConnected) {
				Disconnect();
			}

			if (withShutdownEvent) {
				Events.OnBotShutdown();
			}
		}

		internal async Task LootIfNeeded() {
			if (!BotConfig.SendOnFarmingFinished || (BotConfig.SteamMasterID == 0) || !IsConnectedAndLoggedOn || (BotConfig.SteamMasterID == SteamClient.SteamID)) {
				return;
			}

			await ResponseLoot(BotConfig.SteamMasterID).ConfigureAwait(false);
		}

		internal void OnFarmingStopped() => ResetGamesPlayed();

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
				switch (message.ToUpper()) {
					case "!2FA":
						return await Response2FA(steamID).ConfigureAwait(false);
					case "!2FANO":
						return await Response2FAConfirm(steamID, false).ConfigureAwait(false);
					case "!2FAOK":
						return await Response2FAConfirm(steamID, true).ConfigureAwait(false);
					case "!API":
						return ResponseAPI(steamID);
					case "!EXIT":
						return ResponseExit(steamID);
					case "!FARM":
						return await ResponseFarm(steamID).ConfigureAwait(false);
					case "!HELP":
						return ResponseHelp(steamID);
					case "!LOOT":
						return await ResponseLoot(steamID).ConfigureAwait(false);
					case "!LOOTALL":
						return await ResponseLootAll(steamID).ConfigureAwait(false);
					case "!PASSWORD":
						return ResponsePassword(steamID);
					case "!PAUSE":
						return await ResponsePause(steamID, false).ConfigureAwait(false);
					case "!PAUSE^":
						return await ResponsePause(steamID, true).ConfigureAwait(false);
					case "!REJOINCHAT":
						return ResponseRejoinChat(steamID);
					case "!RESUME":
						return ResponseResume(steamID);
					case "!RESTART":
						return ResponseRestart(steamID);
					case "!STARTALL":
						return ResponseStartAll(steamID);
					case "!STATUS":
						return ResponseStatus(steamID);
					case "!STATUSALL":
						return ResponseStatusAll(steamID);
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
			switch (args[0].ToUpper()) {
				case "!2FA":
					return await Response2FA(steamID, args[1]).ConfigureAwait(false);
				case "!2FANO":
					return await Response2FAConfirm(steamID, args[1], false).ConfigureAwait(false);
				case "!2FAOK":
					return await Response2FAConfirm(steamID, args[1], true).ConfigureAwait(false);
				case "!ADDLICENSE":
					if (args.Length > 2) {
						return await ResponseAddLicense(steamID, args[1], args[2]).ConfigureAwait(false);
					}

					return await ResponseAddLicense(steamID, BotName, args[1]).ConfigureAwait(false);
				case "!FARM":
					return await ResponseFarm(steamID, args[1]).ConfigureAwait(false);
				case "!LOOT":
					return await ResponseLoot(steamID, args[1]).ConfigureAwait(false);
				case "!OWNS":
					if (args.Length > 2) {
						return await ResponseOwns(steamID, args[1], args[2]).ConfigureAwait(false);
					}

					return await ResponseOwns(steamID, BotName, args[1]).ConfigureAwait(false);
				case "!OWNSALL":
					return await ResponseOwnsAll(steamID, args[1]).ConfigureAwait(false);
				case "!PASSWORD":
					return ResponsePassword(steamID, args[1]);
				case "!PAUSE":
					return await ResponsePause(steamID, args[1], false).ConfigureAwait(false);
				case "!PAUSE^":
					return await ResponsePause(steamID, args[1], true).ConfigureAwait(false);
				case "!PLAY":
					if (args.Length > 2) {
						return await ResponsePlay(steamID, args[1], args[2]).ConfigureAwait(false);
					}

					return await ResponsePlay(steamID, BotName, args[1]).ConfigureAwait(false);
				case "!REDEEM":
					if (args.Length > 2) {
						return await ResponseRedeem(steamID, args[1], args[2]).ConfigureAwait(false);
					}

					return await ResponseRedeem(steamID, BotName, args[1]).ConfigureAwait(false);
				case "!REDEEM^":
					if (args.Length > 2) {
						return await ResponseRedeem(steamID, args[1], args[2], ERedeemFlags.SkipForwarding | ERedeemFlags.SkipDistribution).ConfigureAwait(false);
					}

					return await ResponseRedeem(steamID, BotName, args[1], ERedeemFlags.SkipForwarding | ERedeemFlags.SkipDistribution).ConfigureAwait(false);
				case "!REDEEM&":
					if (args.Length > 2) {
						return await ResponseRedeem(steamID, args[1], args[2], ERedeemFlags.ForceForwarding | ERedeemFlags.SkipInitial).ConfigureAwait(false);
					}

					return await ResponseRedeem(steamID, BotName, args[1], ERedeemFlags.ForceForwarding | ERedeemFlags.SkipInitial).ConfigureAwait(false);
				case "!RESUME":
					return ResponseResume(steamID, args[1]);
				case "!START":
					return ResponseStart(steamID, args[1]);
				case "!STATUS":
					return ResponseStatus(steamID, args[1]);
				case "!STOP":
					return ResponseStop(steamID, args[1]);
				default:
					return ResponseUnknown(steamID);
			}
		}

		private void Destroy() {
			Stop();

			Bot ignored;
			Bots.TryRemove(BotName, out ignored);
		}

		private async Task HeartBeat() {
			if (!IsConnectedAndLoggedOn) {
				return;
			}

			try {
				await SteamApps.PICSGetProductInfo(0, null);
			} catch {
				if (!IsConnectedAndLoggedOn) {
					return;
				}

				ArchiLogger.LogGenericWarning("Connection to Steam Network lost, reconnecting...");
				Connect(true).Forget();
			}
		}

		private async Task Connect(bool force = false) {
			if (!force && (!KeepRunning || SteamClient.IsConnected)) {
				return;
			}

			// Use limiter only when user is not providing 2FA token by himself
			if (string.IsNullOrEmpty(TwoFactorCode)) {
				await LimitLoginRequestsAsync().ConfigureAwait(false);

				if (BotDatabase.MobileAuthenticator != null) {
					// In this case, we can also use ASF 2FA for providing 2FA token, even if it's not required
					TwoFactorCode = await BotDatabase.MobileAuthenticator.GenerateToken().ConfigureAwait(false);
				}
			}

			lock (SteamClient) {
				if (!force && (!KeepRunning || SteamClient.IsConnected)) {
					return;
				}

				SteamClient.Connect();
			}
		}

		private void Disconnect() {
			lock (SteamClient) {
				SteamClient.Disconnect();
			}
		}

		private async Task Start() {
			if (!KeepRunning) {
				KeepRunning = true;
				Task.Run(() => HandleCallbacks()).Forget();
				ArchiLogger.LogGenericInfo("Starting...");
			}

			await Connect().ConfigureAwait(false);
		}

		private bool IsMaster(ulong steamID) {
			if (steamID != 0) {
				return (steamID == BotConfig.SteamMasterID) || IsOwner(steamID);
			}

			ArchiLogger.LogNullError(nameof(steamID));
			return false;
		}

		private void CheckOccupationStatus() {
			if (!IsFarmingPossible) {
				ArchiLogger.LogGenericInfo("Account is currently being used, ASF will resume farming when it's free...");
				StopFamilySharingInactivityTimer();
				return;
			}

			ArchiLogger.LogGenericInfo("Account is no longer occupied, farming process resumed!");
			CardsFarmer.Resume(false);
		}

		private void CheckFamilySharingInactivity() {
			if (!IsFarmingPossible) {
				return;
			}

			ArchiLogger.LogGenericInfo("Shared library has not been launched in given time period, farming process resumed!");
			CardsFarmer.Resume(false);
		}

		private void StartFamilySharingInactivityTimer() {
			if (FamilySharingInactivityTimer != null) {
				return;
			}

			FamilySharingInactivityTimer = new Timer(
				e => CheckFamilySharingInactivity(),
				null,
				TimeSpan.FromMinutes(FamilySharingInactivityMinutes), // Delay
				Timeout.InfiniteTimeSpan // Period
			);
		}

		private void StopFamilySharingInactivityTimer() {
			if (FamilySharingInactivityTimer == null) {
				return;
			}

			FamilySharingInactivityTimer.Change(Timeout.Infinite, Timeout.Infinite);
			FamilySharingInactivityTimer.Dispose();
			FamilySharingInactivityTimer = null;
		}

		private async Task InitializeFamilySharing() {
			HashSet<ulong> steamIDs = await ArchiWebHandler.GetFamilySharingSteamIDs().ConfigureAwait(false);
			if ((steamIDs == null) || (steamIDs.Count == 0)) {
				return;
			}

			SteamFamilySharingIDs.ReplaceIfNeededWith(steamIDs);
		}

		private void ImportAuthenticator(string maFilePath) {
			if ((BotDatabase.MobileAuthenticator != null) || !File.Exists(maFilePath)) {
				return;
			}

			ArchiLogger.LogGenericInfo("Converting .maFile into ASF format...");

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
				ArchiLogger.LogGenericWarning("Your DeviceID is incorrect or doesn't exist");
				string deviceID = Program.GetUserInput(SharedInfo.EUserInputType.DeviceID, BotName);
				if (string.IsNullOrEmpty(deviceID)) {
					BotDatabase.MobileAuthenticator = null;
					return;
				}

				BotDatabase.MobileAuthenticator.CorrectDeviceID(deviceID);
				BotDatabase.Save();
			}

			ArchiLogger.LogGenericInfo("Successfully finished importing mobile authenticator!");
		}

		private string ResponsePassword(ulong steamID) {
			if (steamID == 0) {
				ArchiLogger.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			if (string.IsNullOrEmpty(BotConfig.SteamPassword)) {
				return "Can't encrypt null password!";
			}

			return Environment.NewLine +
				"[" + CryptoHelper.ECryptoMethod.AES + "] password: " + CryptoHelper.Encrypt(CryptoHelper.ECryptoMethod.AES, BotConfig.SteamPassword) + Environment.NewLine +
				"[" + CryptoHelper.ECryptoMethod.ProtectedDataForCurrentUser + "] password: " + CryptoHelper.Encrypt(CryptoHelper.ECryptoMethod.ProtectedDataForCurrentUser, BotConfig.SteamPassword);
		}

		private static string ResponsePassword(ulong steamID, string botName) {
			if ((steamID == 0) || string.IsNullOrEmpty(botName)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botName));
				return null;
			}

			Bot bot;
			if (Bots.TryGetValue(botName, out bot)) {
				return bot.ResponsePassword(steamID);
			}

			if (IsOwner(steamID)) {
				return "Couldn't find any bot named " + botName + "!";
			}

			return null;
		}

		private async Task<string> ResponsePause(ulong steamID, bool sticky) {
			if (steamID == 0) {
				ArchiLogger.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsMaster(steamID) && !SteamFamilySharingIDs.Contains(steamID)) {
				return null;
			}

			if (!IsConnectedAndLoggedOn) {
				return "This bot instance is not connected!";
			}

			if (CardsFarmer.Paused) {
				return "Automatic farming is paused already!";
			}

			await CardsFarmer.Pause(sticky).ConfigureAwait(false);

			if (!SteamFamilySharingIDs.Contains(steamID)) {
				return "Automatic farming is now paused!";
			}

			StartFamilySharingInactivityTimer();
			return "Automatic farming is now paused! You have " + FamilySharingInactivityMinutes + " minutes to start a game.";
		}

		private static async Task<string> ResponsePause(ulong steamID, string botName, bool sticky) {
			if ((steamID == 0) || string.IsNullOrEmpty(botName)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botName));
				return null;
			}

			Bot bot;
			if (Bots.TryGetValue(botName, out bot)) {
				return await bot.ResponsePause(steamID, sticky).ConfigureAwait(false);
			}

			if (IsOwner(steamID)) {
				return "Couldn't find any bot named " + botName + "!";
			}

			return null;
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
				return "This bot instance is not connected!";
			}

			if (!CardsFarmer.Paused) {
				return "Automatic farming is resumed already!";
			}

			StopFamilySharingInactivityTimer();
			CardsFarmer.Resume(true);
			return "Automatic farming is now resumed!";
		}

		private static string ResponseResume(ulong steamID, string botName) {
			if ((steamID == 0) || string.IsNullOrEmpty(botName)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botName));
				return null;
			}

			Bot bot;
			if (Bots.TryGetValue(botName, out bot)) {
				return bot.ResponseResume(steamID);
			}

			if (IsOwner(steamID)) {
				return "Couldn't find any bot named " + botName + "!";
			}

			return null;
		}

		private string ResponseStatus(ulong steamID) {
			if (steamID == 0) {
				ArchiLogger.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			if (!IsConnectedAndLoggedOn) {
				if (KeepRunning) {
					return "Bot " + BotName + " is not connected.";
				}

				return "Bot " + BotName + " is not running.";
			}

			if (PlayingBlocked) {
				return "Bot " + BotName + " is currently being used.";
			}

			if (CardsFarmer.Paused) {
				return "Bot " + BotName + " is paused or running in manual mode.";
			}

			if (CardsFarmer.CurrentGamesFarming.Count == 0) {
				return "Bot " + BotName + " is not farming anything.";
			}

			StringBuilder response = new StringBuilder("Bot " + BotName + " is farming ");

			if (CardsFarmer.CurrentGamesFarming.Count == 1) {
				CardsFarmer.Game game = CardsFarmer.CurrentGamesFarming.First();
				response.Append("game " + game.AppID + " (" + game.GameName + ", " + game.CardsRemaining + " card drops remaining)");
			} else {
				response.Append("appIDs " + string.Join(", ", CardsFarmer.CurrentGamesFarming.Select(game => game.AppID)));
			}

			response.Append(" and has a total of " + CardsFarmer.GamesToFarm.Count + " games (" + CardsFarmer.GamesToFarm.Sum(game => game.CardsRemaining) + " cards) left to farm.");
			return response.ToString();
		}

		private static string ResponseStatus(ulong steamID, string botName) {
			if ((steamID == 0) || string.IsNullOrEmpty(botName)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botName));
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
				ASF.ArchiLogger.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsOwner(steamID)) {
				return null;
			}

			HashSet<Bot> botsRunning = new HashSet<Bot>(Bots.Where(bot => bot.Value.KeepRunning).OrderBy(bot => bot.Key).Select(bot => bot.Value));
			IEnumerable<string> statuses = botsRunning.Select(bot => bot.ResponseStatus(steamID));

			return Environment.NewLine +
				string.Join(Environment.NewLine, statuses) + Environment.NewLine +
				"There are " + botsRunning.Count + "/" + Bots.Count + " bots running, with total of " + botsRunning.Sum(bot => bot.CardsFarmer.GamesToFarm.Count) + " games (" + botsRunning.Sum(bot => bot.CardsFarmer.GamesToFarm.Sum(game => game.CardsRemaining)) + " cards) left to farm.";
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
				return "This bot instance is not connected!";
			}

			if (BotConfig.SteamMasterID == 0) {
				return "Trade couldn't be send because SteamMasterID is not defined!";
			}

			if (BotConfig.SteamMasterID == SteamClient.SteamID) {
				return "You can't loot yourself!";
			}

			await Trading.LimitInventoryRequestsAsync().ConfigureAwait(false);

			HashSet<Steam.Item> inventory = await ArchiWebHandler.GetMySteamInventory(true).ConfigureAwait(false);
			if ((inventory == null) || (inventory.Count == 0)) {
				return "Nothing to send, inventory seems empty!";
			}

			// Remove from our pending inventory all items that are not steam cards and boosters
			if (inventory.RemoveWhere(item => (item.Type != Steam.Item.EType.TradingCard) && ((item.Type != Steam.Item.EType.FoilTradingCard) || !BotConfig.IsBotAccount) && (item.Type != Steam.Item.EType.BoosterPack)) > 0) {
				if (inventory.Count == 0) {
					return "Nothing to send, inventory seems empty!";
				}
			}

			if (!await ArchiWebHandler.SendTradeOffer(inventory, BotConfig.SteamMasterID, BotConfig.SteamTradeToken).ConfigureAwait(false)) {
				return "Trade offer failed due to error!";
			}

			await Task.Delay(1000).ConfigureAwait(false); // Sometimes we can be too fast for Steam servers to generate confirmations, wait a short moment
			await AcceptConfirmations(true, Steam.ConfirmationDetails.EType.Trade, BotConfig.SteamMasterID).ConfigureAwait(false);
			return "Trade offer sent successfully!";
		}

		private static async Task<string> ResponseLoot(ulong steamID, string botName) {
			if ((steamID == 0) || string.IsNullOrEmpty(botName)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botName));
				return null;
			}

			Bot bot;
			if (Bots.TryGetValue(botName, out bot)) {
				return await bot.ResponseLoot(steamID).ConfigureAwait(false);
			}

			if (IsOwner(steamID)) {
				return "Couldn't find any bot named " + botName + "!";
			}

			return null;
		}

		private static async Task<string> ResponseLootAll(ulong steamID) {
			if (steamID == 0) {
				ASF.ArchiLogger.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsOwner(steamID)) {
				return null;
			}

			await Task.WhenAll(Bots.Values.Where(bot => bot.IsConnectedAndLoggedOn).Select(bot => bot.ResponseLoot(steamID))).ConfigureAwait(false);
			return "Done!";
		}

		private async Task<string> Response2FA(ulong steamID) {
			if (steamID == 0) {
				ArchiLogger.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			if (BotDatabase.MobileAuthenticator == null) {
				return "That bot doesn't have ASF 2FA enabled!";
			}

			string token = await BotDatabase.MobileAuthenticator.GenerateToken().ConfigureAwait(false);
			if (string.IsNullOrEmpty(token)) {
				return "Error!";
			}

			return "2FA Token: " + token;
		}

		private static async Task<string> Response2FA(ulong steamID, string botName) {
			if ((steamID == 0) || string.IsNullOrEmpty(botName)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botName));
				return null;
			}

			Bot bot;
			if (Bots.TryGetValue(botName, out bot)) {
				return await bot.Response2FA(steamID).ConfigureAwait(false);
			}

			if (IsOwner(steamID)) {
				return "Couldn't find any bot named " + botName + "!";
			}

			return null;
		}

		private async Task<string> Response2FAConfirm(ulong steamID, bool confirm) {
			if (steamID == 0) {
				ArchiLogger.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			if (BotDatabase.MobileAuthenticator == null) {
				return "That bot doesn't have ASF 2FA enabled!";
			}

			if (await AcceptConfirmations(confirm).ConfigureAwait(false)) {
				return "Success!";
			}

			return "Something went wrong!";
		}

		private static async Task<string> Response2FAConfirm(ulong steamID, string botName, bool confirm) {
			if ((steamID == 0) || string.IsNullOrEmpty(botName)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botName));
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

		private static string ResponseAPI(ulong steamID) {
			if (steamID != 0) {
				return !IsOwner(steamID) ? null : GetAPIStatus();
			}

			ASF.ArchiLogger.LogNullError(nameof(steamID));
			return null;
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
				Program.Exit();
			}).Forget();

			return "Done!";
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
				return "This bot instance is not connected!";
			}

			await CardsFarmer.StopFarming().ConfigureAwait(false);
			CardsFarmer.StartFarming().Forget();
			return "Done!";
		}

		private static async Task<string> ResponseFarm(ulong steamID, string botName) {
			if ((steamID == 0) || string.IsNullOrEmpty(botName)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botName));
				return null;
			}

			Bot bot;
			if (Bots.TryGetValue(botName, out bot)) {
				return await bot.ResponseFarm(steamID).ConfigureAwait(false);
			}

			if (IsOwner(steamID)) {
				return "Couldn't find any bot named " + botName + "!";
			}

			return null;
		}

		private string ResponseHelp(ulong steamID) {
			if (steamID == 0) {
				ArchiLogger.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			return "https://github.com/" + SharedInfo.GithubRepo + "/wiki/Commands";
		}

		[SuppressMessage("ReSharper", "FunctionComplexityOverflow")]
		private async Task<string> ResponseRedeem(ulong steamID, string message, ERedeemFlags redeemFlags = ERedeemFlags.None) {
			if ((steamID == 0) || string.IsNullOrEmpty(message)) {
				ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(message));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			bool forward = !redeemFlags.HasFlag(ERedeemFlags.SkipForwarding) && (redeemFlags.HasFlag(ERedeemFlags.ForceForwarding) || BotConfig.ForwardKeysToOtherBots);
			bool distribute = !redeemFlags.HasFlag(ERedeemFlags.SkipDistribution) && (redeemFlags.HasFlag(ERedeemFlags.ForceDistribution) || BotConfig.DistributeKeys);
			message = message.Replace(",", Environment.NewLine);

			StringBuilder response = new StringBuilder();
			using (StringReader reader = new StringReader(message)) {
				using (IEnumerator<Bot> enumerator = Bots.OrderBy(bot => bot.Key).Select(bot => bot.Value).GetEnumerator()) {
					string key = reader.ReadLine();
					Bot currentBot = this;
					while (!string.IsNullOrEmpty(key) && (currentBot != null)) {
						if (redeemFlags.HasFlag(ERedeemFlags.Validate) && !IsValidCdKey(key)) {
							key = reader.ReadLine(); // Next key
							continue; // Keep current bot
						}

						if ((redeemFlags.HasFlag(ERedeemFlags.SkipInitial) && (currentBot == this)) || !currentBot.IsConnectedAndLoggedOn) {
							currentBot = null; // Either bot will be changed, or loop aborted
						} else {
							ArchiHandler.PurchaseResponseCallback result = await currentBot.ArchiHandler.RedeemKey(key).ConfigureAwait(false);
							if (result == null) {
								response.Append(Environment.NewLine + "<" + currentBot.BotName + "> Key: " + key + " | Status: " + ArchiHandler.PurchaseResponseCallback.EPurchaseResult.Timeout);
								currentBot = null; // Either bot will be changed, or loop aborted
							} else {
								switch (result.PurchaseResult) {
									case ArchiHandler.PurchaseResponseCallback.EPurchaseResult.DuplicatedKey:
									case ArchiHandler.PurchaseResponseCallback.EPurchaseResult.InvalidKey:
									case ArchiHandler.PurchaseResponseCallback.EPurchaseResult.OK:
									case ArchiHandler.PurchaseResponseCallback.EPurchaseResult.SteamWalletCode:
									case ArchiHandler.PurchaseResponseCallback.EPurchaseResult.Timeout:
									case ArchiHandler.PurchaseResponseCallback.EPurchaseResult.Unknown:
										if (result.PurchaseResult == ArchiHandler.PurchaseResponseCallback.EPurchaseResult.SteamWalletCode) {
											// If it's a wallet code, try to redeem it, and forward the result
											// The result is final, there is no place for forwarding
											result.PurchaseResult = await currentBot.ArchiWebHandler.RedeemWalletKey(key).ConfigureAwait(false);
										}

										response.Append(Environment.NewLine + "<" + currentBot.BotName + "> Key: " + key + " | Status: " + result.PurchaseResult + ((result.Items != null) && (result.Items.Count > 0) ? " | Items: " + string.Join("", result.Items) : ""));

										key = reader.ReadLine(); // Next key

										if (result.PurchaseResult == ArchiHandler.PurchaseResponseCallback.EPurchaseResult.OK) {
											break; // Next bot (if needed)
										}

										continue; // Keep current bot
									case ArchiHandler.PurchaseResponseCallback.EPurchaseResult.AlreadyOwned:
									case ArchiHandler.PurchaseResponseCallback.EPurchaseResult.BaseGameRequired:
									case ArchiHandler.PurchaseResponseCallback.EPurchaseResult.OnCooldown:
									case ArchiHandler.PurchaseResponseCallback.EPurchaseResult.RegionLocked:
										response.Append(Environment.NewLine + "<" + currentBot.BotName + "> Key: " + key + " | Status: " + result.PurchaseResult + ((result.Items != null) && (result.Items.Count > 0) ? " | Items: " + string.Join("", result.Items) : ""));

										if (!forward) {
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
												response.Append(Environment.NewLine + "<" + bot.BotName + "> Key: " + key + " | Status: Timeout!");
												continue;
											}

											switch (otherResult.PurchaseResult) {
												case ArchiHandler.PurchaseResponseCallback.EPurchaseResult.DuplicatedKey:
												case ArchiHandler.PurchaseResponseCallback.EPurchaseResult.InvalidKey:
												case ArchiHandler.PurchaseResponseCallback.EPurchaseResult.OK:
													alreadyHandled = true; // This key is already handled, as we either redeemed it or we're sure it's dupe/invalid
													break;
											}

											response.Append(Environment.NewLine + "<" + bot.BotName + "> Key: " + key + " | Status: " + otherResult.PurchaseResult + ((otherResult.Items != null) && (otherResult.Items.Count > 0) ? " | Items: " + string.Join("", otherResult.Items) : ""));

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
								}
							}
						}

						if (!distribute && !redeemFlags.HasFlag(ERedeemFlags.SkipInitial)) {
							continue;
						}

						do {
							currentBot = enumerator.MoveNext() ? enumerator.Current : null;
						} while ((currentBot == this) || ((currentBot != null) && !currentBot.IsConnectedAndLoggedOn));
					}
				}
			}

			return response.Length == 0 ? null : response.ToString();
		}

		private static async Task<string> ResponseRedeem(ulong steamID, string botName, string message, ERedeemFlags redeemFlags = ERedeemFlags.None) {
			if ((steamID == 0) || string.IsNullOrEmpty(botName) || string.IsNullOrEmpty(message)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botName) + " || " + nameof(message));
				return null;
			}

			Bot bot;
			if (Bots.TryGetValue(botName, out bot)) {
				return await bot.ResponseRedeem(steamID, message, redeemFlags).ConfigureAwait(false);
			}

			if (IsOwner(steamID)) {
				return "Couldn't find any bot named " + botName + "!";
			}

			return null;
		}

		private static string ResponseRejoinChat(ulong steamID) {
			if (steamID == 0) {
				ASF.ArchiLogger.LogNullError(nameof(steamID));
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
				ASF.ArchiLogger.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsOwner(steamID)) {
				return null;
			}

			// Schedule the task after some time so user can receive response
			Task.Run(async () => {
				await Task.Delay(1000).ConfigureAwait(false);
				Program.Restart();
			}).Forget();

			return "Done!";
		}

		private static string ResponseStartAll(ulong steamID) {
			if (steamID == 0) {
				ASF.ArchiLogger.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsOwner(steamID)) {
				return null;
			}

			foreach (Bot bot in Bots.Values.Where(bot => !bot.KeepRunning)) {
				bot.ResponseStart(steamID);
			}

			return "Done!";
		}

		private async Task<string> ResponseAddLicense(ulong steamID, ICollection<uint> gameIDs) {
			if ((steamID == 0) || (gameIDs == null) || (gameIDs.Count == 0)) {
				ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(gameIDs) + " || " + nameof(gameIDs.Count));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			if (!IsConnectedAndLoggedOn) {
				return "This bot instance is not connected!";
			}

			StringBuilder result = new StringBuilder();
			foreach (uint gameID in gameIDs) {
				SteamApps.FreeLicenseCallback callback = await SteamApps.RequestFreeLicense(gameID);
				if (callback == null) {
					result.AppendLine(Environment.NewLine + "Result: Timeout!");
					break;
				}

				result.AppendLine(Environment.NewLine + "Result: " + callback.Result + " | Granted apps: " + string.Join(", ", callback.GrantedApps) + " " + string.Join(", ", callback.GrantedPackages));
			}

			return result.ToString();
		}

		private static async Task<string> ResponseAddLicense(ulong steamID, string botName, string games) {
			if ((steamID == 0) || string.IsNullOrEmpty(botName) || string.IsNullOrEmpty(games)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botName) + " || " + nameof(games));
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
					return "Couldn't parse games list!";
				}

				gamesToRedeem.Add(gameID);
			}

			if (gamesToRedeem.Count == 0) {
				return "List of games is empty!";
			}

			return await bot.ResponseAddLicense(steamID, gamesToRedeem).ConfigureAwait(false);
		}

		private async Task<string> ResponseOwns(ulong steamID, string query) {
			if ((steamID == 0) || string.IsNullOrEmpty(query)) {
				ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(query));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			if (!IsConnectedAndLoggedOn) {
				return "This bot instance is not connected!";
			}

			Dictionary<uint, string> ownedGames;
			if (!string.IsNullOrEmpty(BotConfig.SteamApiKey)) {
				ownedGames = ArchiWebHandler.GetOwnedGames(SteamClient.SteamID);
			} else {
				ownedGames = await ArchiWebHandler.GetOwnedGames().ConfigureAwait(false);
			}

			if ((ownedGames == null) || (ownedGames.Count == 0)) {
				return Environment.NewLine + "<" + BotName + "> List of owned games is empty!";
			}

			StringBuilder response = new StringBuilder();

			string[] games = query.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
			foreach (string game in games.Where(game => !string.IsNullOrEmpty(game))) {
				// Check if this is appID
				uint appID;
				if (uint.TryParse(game, out appID)) {
					string ownedName;
					if (ownedGames.TryGetValue(appID, out ownedName)) {
						response.Append(Environment.NewLine + "<" + BotName + "> Owned already: " + appID + " | " + ownedName);
					} else {
						response.Append(Environment.NewLine + "<" + BotName + "> Not owned yet: " + appID);
					}

					continue;
				}

				// This is a string, so check our entire library
				foreach (KeyValuePair<uint, string> ownedGame in ownedGames.Where(ownedGame => ownedGame.Value.IndexOf(game, StringComparison.OrdinalIgnoreCase) >= 0)) {
					response.Append(Environment.NewLine + "<" + BotName + "> Owned already: " + ownedGame.Key + " | " + ownedGame.Value);
				}
			}

			if (response.Length > 0) {
				return response.ToString();
			}

			return Environment.NewLine + "<" + BotName + "> Not owned yet: " + query;
		}

		private static async Task<string> ResponseOwns(ulong steamID, string botName, string query) {
			if ((steamID == 0) || string.IsNullOrEmpty(botName) || string.IsNullOrEmpty(query)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botName) + " || " + nameof(query));
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

		private static async Task<string> ResponseOwnsAll(ulong steamID, string query) {
			if ((steamID == 0) || string.IsNullOrEmpty(query)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(query));
				return null;
			}

			if (!IsOwner(steamID)) {
				return null;
			}

			string[] responses = await Task.WhenAll(Bots.Where(bot => bot.Value.IsConnectedAndLoggedOn).OrderBy(bot => bot.Key).Select(bot => bot.Value.ResponseOwns(steamID, query))).ConfigureAwait(false);

			StringBuilder result = new StringBuilder();
			foreach (string response in responses.Where(response => !string.IsNullOrEmpty(response))) {
				result.Append(response);
			}

			return result.Length != 0 ? result.ToString() : null;
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
				return "This bot instance is not connected!";
			}

			if (!CardsFarmer.Paused) {
				await CardsFarmer.Pause(false).ConfigureAwait(false);
			}

			ArchiHandler.PlayGames(gameIDs);
			return "Done!";
		}

		private static async Task<string> ResponsePlay(ulong steamID, string botName, string games) {
			if ((steamID == 0) || string.IsNullOrEmpty(botName) || string.IsNullOrEmpty(games)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botName) + " || " + nameof(games));
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
					return "Couldn't parse games list!";
				}

				gamesToPlay.Add(gameID);

				if (gamesToPlay.Count >= CardsFarmer.MaxGamesPlayedConcurrently) {
					break;
				}
			}

			if (gamesToPlay.Count == 0) {
				return "List of games is empty!";
			}

			return await bot.ResponsePlay(steamID, gamesToPlay).ConfigureAwait(false);
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
				return "That bot instance is already running!";
			}

			SkipFirstShutdown = true;
			Start().Forget();
			return "Done!";
		}

		private static string ResponseStart(ulong steamID, string botName) {
			if ((steamID == 0) || string.IsNullOrEmpty(botName)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botName));
				return null;
			}

			Bot bot;
			if (Bots.TryGetValue(botName, out bot)) {
				return bot.ResponseStart(steamID);
			}

			if (IsOwner(steamID)) {
				return "Couldn't find any bot named " + botName + "!";
			}

			return null;
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
				return "That bot instance is already inactive!";
			}

			Stop();
			return "Done!";
		}

		private static string ResponseStop(ulong steamID, string botName) {
			if ((steamID == 0) || string.IsNullOrEmpty(botName)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botName));
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
			return "Done!";
		}

		private string ResponseVersion(ulong steamID) {
			if (steamID == 0) {
				ArchiLogger.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			return "ASF V" + SharedInfo.Version;
		}

		private void HandleCallbacks() {
			TimeSpan timeSpan = TimeSpan.FromMilliseconds(CallbackSleep);
			while (KeepRunning || SteamClient.IsConnected) {
				if (!CallbackSemaphore.Wait(0)) {
					return;
				}

				try {
					CallbackManager.RunWaitCallbacks(timeSpan);
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
			if (string.IsNullOrEmpty(response)) { // We respond with null when user is not authorized (and similar)
				return;
			}

			SendMessage(chatID, response);
		}

		private void SendMessage(ulong steamID, string message) {
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

		private void SendMessageToChannel(ulong steamID, string message) {
			if ((steamID == 0) || string.IsNullOrEmpty(message)) {
				ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(message));
				return;
			}

			if (!IsConnectedAndLoggedOn) {
				return;
			}

			for (int i = 0; i < message.Length; i += MaxSteamMessageLength - 6) {
				string messagePart = (i > 0 ? "..." : "") + message.Substring(i, Math.Min(MaxSteamMessageLength - 6, message.Length - i)) + (MaxSteamMessageLength - 6 < message.Length - i ? "..." : "");
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

			for (int i = 0; i < message.Length; i += MaxSteamMessageLength - 6) {
				string messagePart = (i > 0 ? "..." : "") + message.Substring(i, Math.Min(MaxSteamMessageLength - 6, message.Length - i)) + (MaxSteamMessageLength - 6 < message.Length - i ? "..." : "");
				SteamFriends.SendChatMessage(steamID, EChatEntryType.ChatMsg, messagePart);
			}
		}

		private void JoinMasterChat() {
			if (!IsConnectedAndLoggedOn || (BotConfig.SteamMasterClanID == 0)) {
				return;
			}

			SteamFriends.JoinChat(BotConfig.SteamMasterClanID);
		}

		private bool InitializeLoginAndPassword(bool requiresPassword) {
			if (string.IsNullOrEmpty(BotConfig.SteamLogin)) {
				BotConfig.SteamLogin = Program.GetUserInput(SharedInfo.EUserInputType.Login, BotName);
				if (string.IsNullOrEmpty(BotConfig.SteamLogin)) {
					return false;
				}
			}

			if (!string.IsNullOrEmpty(BotConfig.SteamPassword) || (!requiresPassword && !string.IsNullOrEmpty(BotDatabase.LoginKey))) {
				return true;
			}

			BotConfig.SteamPassword = Program.GetUserInput(SharedInfo.EUserInputType.Password, BotName);
			return !string.IsNullOrEmpty(BotConfig.SteamPassword);
		}

		private void ResetGamesPlayed() {
			if (PlayingBlocked) {
				return;
			}

			ArchiHandler.PlayGames(BotConfig.GamesPlayedWhileIdle, BotConfig.CustomGamePlayedWhileIdle);
		}

		private void OnConnected(SteamClient.ConnectedCallback callback) {
			if (callback == null) {
				ArchiLogger.LogNullError(nameof(callback));
				return;
			}

			if (callback.Result != EResult.OK) {
				ArchiLogger.LogGenericError("Unable to connect to Steam: " + callback.Result);
				return;
			}

			ArchiLogger.LogGenericInfo("Connected to Steam!");

			if (!KeepRunning) {
				ArchiLogger.LogGenericInfo("Disconnecting...");
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

			if (!InitializeLoginAndPassword(false)) {
				Stop();
				return;
			}

			ArchiLogger.LogGenericInfo("Logging in...");

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
				Username = BotConfig.SteamLogin,
				Password = password,
				AuthCode = AuthCode,
				CellID = Program.GlobalDatabase.CellID,
				LoginID = LoginID,
				LoginKey = loginKey,
				TwoFactorCode = TwoFactorCode,
				SentryFileHash = sentryFileHash,
				ShouldRememberPassword = true
			};

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

			ArchiLogger.LogGenericInfo("Disconnected from Steam!");

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
					ArchiLogger.LogGenericInfo("Removed expired login key");
					break;
				case EResult.NoConnection:
				case EResult.ServiceUnavailable:
				case EResult.Timeout:
				case EResult.TryAnotherCM:
					await Task.Delay(5000).ConfigureAwait(false);
					break;
				case EResult.RateLimitExceeded:
					ArchiLogger.LogGenericInfo("Will retry after 25 minutes...");
					await Task.Delay(25 * 60 * 1000).ConfigureAwait(false); // Captcha disappears after around 20 minutes, so we make it 25
					break;
			}

			if (!KeepRunning || SteamClient.IsConnected) {
				return;
			}

			ArchiLogger.LogGenericInfo("Reconnecting...");
			await Connect().ConfigureAwait(false);
		}

		private void OnFreeLicense(SteamApps.FreeLicenseCallback callback) {
			if (callback == null) {
				ArchiLogger.LogNullError(nameof(callback));
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

				ArchiLogger.LogGenericInfo("Accepting gift: " + gid + "...");
				await LimitGiftsRequestsAsync().ConfigureAwait(false);

				ArchiHandler.RedeemGuestPassResponseCallback response = await ArchiHandler.RedeemGuestPass(gid).ConfigureAwait(false);
				if (response != null) {
					if (response.Result == EResult.OK) {
						ArchiLogger.LogGenericInfo("Success!");
					} else {
						ArchiLogger.LogGenericWarning("Failed with error: " + response.Result);
					}
				} else {
					ArchiLogger.LogGenericWarning("Failed!");
				}
			}
		}

		private async void OnLicenseList(SteamApps.LicenseListCallback callback) {
			if (callback?.LicenseList == null) {
				ArchiLogger.LogNullError(nameof(callback) + " || " + nameof(callback.LicenseList));
				return;
			}

			OwnedPackageIDs.Clear();

			foreach (SteamApps.LicenseListCallback.License license in callback.LicenseList) {
				OwnedPackageIDs.Add(license.PackageID);
			}

			OwnedPackageIDs.TrimExcess();

			await Task.Delay(1000).ConfigureAwait(false); // Wait a second for eventual PlayingSessionStateCallback

			if (!ArchiWebHandler.Ready) {
				for (byte i = 0; (i < Program.GlobalConfig.HttpTimeout) && !ArchiWebHandler.Ready; i++) {
					await Task.Delay(1000).ConfigureAwait(false);
				}

				if (!ArchiWebHandler.Ready) {
					return;
				}
			}

			await CardsFarmer.OnNewGameAdded().ConfigureAwait(false);
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
			if (callback?.FriendList == null) {
				ArchiLogger.LogNullError(nameof(callback) + " || " + nameof(callback.FriendList));
				return;
			}

			foreach (SteamFriends.FriendsListCallback.Friend friend in callback.FriendList.Where(friend => friend.Relationship == EFriendRelationship.RequestRecipient)) {
				if (IsMaster(friend.SteamID)) {
					SteamFriends.AddFriend(friend.SteamID);
				} else if (BotConfig.IsBotAccount) {
					SteamFriends.RemoveFriend(friend.SteamID);
				}
			}
		}

		private async void OnFriendMsg(SteamFriends.FriendMsgCallback callback) {
			if (callback == null) {
				ArchiLogger.LogNullError(nameof(callback));
				return;
			}

			if (callback.EntryType != EChatEntryType.ChatMsg) {
				return;
			}

			if ((callback.Sender == null) || string.IsNullOrEmpty(callback.Message)) {
				ArchiLogger.LogNullError(nameof(callback.Sender) + " || " + nameof(callback.Message));
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
			if (DateTime.UtcNow.Subtract(lastMessage.Timestamp).TotalHours > 1) {
				return;
			}

			// Handle the message
			await HandleMessage(callback.SteamID, callback.SteamID, lastMessage.Message).ConfigureAwait(false);
		}

		private void OnPersonaState(SteamFriends.PersonaStateCallback callback) {
			if (callback == null) {
				ArchiLogger.LogNullError(nameof(callback));
				return;
			}

			if (callback.FriendID == SteamClient.SteamID) {
				Events.OnStateUpdated(this, callback);
			} else if ((callback.FriendID == LibraryLockedBySteamID) && (callback.GameID == 0)) {
				LibraryLockedBySteamID = 0;
				CheckOccupationStatus();
			}
		}

		private void OnAccountInfo(SteamUser.AccountInfoCallback callback) {
			if (callback == null) {
				ArchiLogger.LogNullError(nameof(callback));
				return;
			}

			if (!BotConfig.FarmOffline) {
				SteamFriends.SetPersonaState(EPersonaState.Online);
			}
		}

		private void OnLoggedOff(SteamUser.LoggedOffCallback callback) {
			if (callback == null) {
				ArchiLogger.LogNullError(nameof(callback));
				return;
			}

			ArchiLogger.LogGenericInfo("Logged off of Steam: " + callback.Result);

			switch (callback.Result) {
				case EResult.LogonSessionReplaced:
					ArchiLogger.LogGenericError("This account seems to be used in another ASF instance, which is undefined behaviour, refusing to keep it running!");
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

			switch (callback.Result) {
				case EResult.AccountLogonDenied:
					AuthCode = Program.GetUserInput(SharedInfo.EUserInputType.SteamGuard, BotName);
					if (string.IsNullOrEmpty(AuthCode)) {
						Stop();
					}

					break;
				case EResult.AccountLoginDeniedNeedTwoFactor:
					if (BotDatabase.MobileAuthenticator == null) {
						TwoFactorCode = Program.GetUserInput(SharedInfo.EUserInputType.TwoFactorAuthentication, BotName);
						if (string.IsNullOrEmpty(TwoFactorCode)) {
							Stop();
						}
					} else {
						ArchiLogger.LogGenericWarning("2FA code was invalid despite of using ASF 2FA. Invalid authenticator or bad timing?");
					}

					break;
				case EResult.OK:
					ArchiLogger.LogGenericInfo("Successfully logged on!");

					// Old status for these doesn't matter, we'll be notified in callback if needed
					LibraryLockedBySteamID = 0;
					PlayingBlocked = false;

					if ((callback.CellID != 0) && (Program.GlobalDatabase.CellID != callback.CellID)) {
						Program.GlobalDatabase.CellID = callback.CellID;
					}

					if (BotDatabase.MobileAuthenticator == null) {
						// Support and convert SDA files
						string maFilePath = Path.Combine(SharedInfo.ConfigDirectory, callback.ClientSteamID.ConvertToUInt64() + ".maFile");
						if (File.Exists(maFilePath)) {
							ImportAuthenticator(maFilePath);
						}
					}

					if (string.IsNullOrEmpty(BotConfig.SteamParentalPIN)) {
						BotConfig.SteamParentalPIN = Program.GetUserInput(SharedInfo.EUserInputType.SteamParentalPIN, BotName);
						if (string.IsNullOrEmpty(BotConfig.SteamParentalPIN)) {
							Stop();
							return;
						}
					}

					if (!await ArchiWebHandler.Init(callback.ClientSteamID, SteamClient.ConnectedUniverse, callback.WebAPIUserNonce, BotConfig.SteamParentalPIN).ConfigureAwait(false)) {
						if (!await RefreshSession().ConfigureAwait(false)) {
							return;
						}
					}

					InitializeFamilySharing().Forget();

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
						ArchiWebHandler.JoinGroup(SharedInfo.ASFGroupSteamID).Forget();
					}

					Trading.CheckTrades().Forget();
					break;
				case EResult.InvalidPassword:
				case EResult.NoConnection:
				case EResult.RateLimitExceeded:
				case EResult.ServiceUnavailable:
				case EResult.Timeout:
				case EResult.TryAnotherCM:
				case EResult.TwoFactorCodeMismatch:
					ArchiLogger.LogGenericWarning("Unable to login to Steam: " + callback.Result + " / " + callback.ExtendedResult);
					break;
				default: // Unexpected result, shutdown immediately
					ArchiLogger.LogGenericError("Unable to login to Steam: " + callback.Result + " / " + callback.ExtendedResult);
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

		private void OnWebAPIUserNonce(SteamUser.WebAPIUserNonceCallback callback) {
			if (callback == null) {
				ArchiLogger.LogNullError(nameof(callback));
			}
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
						if (BotConfig.DismissInventoryNotifications) {
							ArchiWebHandler.MarkInventory().Forget();
						}
						break;
					case ArchiHandler.NotificationsCallback.ENotification.Trading:
						Trading.CheckTrades().Forget();
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

		private void OnPlayingSessionState(ArchiHandler.PlayingSessionStateCallback callback) {
			if (callback == null) {
				ArchiLogger.LogNullError(nameof(callback));
				return;
			}

			if (callback.PlayingBlocked == PlayingBlocked) {
				return; // No status update, we're not interested
			}

			PlayingBlocked = callback.PlayingBlocked;
			CheckOccupationStatus();
		}

		private void OnPurchaseResponse(ArchiHandler.PurchaseResponseCallback callback) {
			if (callback == null) {
				ArchiLogger.LogNullError(nameof(callback));
			}
		}

		private void OnSharedLibraryLockStatus(ArchiHandler.SharedLibraryLockStatusCallback callback) {
			if (callback == null) {
				ArchiLogger.LogNullError(nameof(callback));
				return;
			}

			// Ignore no status updates
			if (LibraryLockedBySteamID == 0) {
				if ((callback.LibraryLockedBySteamID == 0) || (callback.LibraryLockedBySteamID == SteamClient.SteamID)) {
					return;
				}

				LibraryLockedBySteamID = callback.LibraryLockedBySteamID;
			} else {
				if ((callback.LibraryLockedBySteamID != 0) && (callback.LibraryLockedBySteamID != SteamClient.SteamID)) {
					return;
				}

				if (SteamFriends.GetFriendGamePlayed(LibraryLockedBySteamID) != 0) {
					return;
				}

				LibraryLockedBySteamID = 0;
			}

			CheckOccupationStatus();
		}
	}
}
