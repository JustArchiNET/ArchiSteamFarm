//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// 
//  Copyright 2015-2018 Łukasz "JustArchi" Domeradzki
//  Contact: JustArchi@JustArchi.net
// 
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
// 
//  http://www.apache.org/licenses/LICENSE-2.0
//      
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Json;
using ArchiSteamFarm.Localization;
using Newtonsoft.Json;
using SteamKit2;
using SteamKit2.Discovery;

namespace ArchiSteamFarm {
	internal sealed class Bot : IDisposable {
		internal const ushort CallbackSleep = 500; // In miliseconds
		internal const byte MinPlayingBlockedTTL = 60; // Delay in seconds added when account was occupied during our disconnect, to not disconnect other Steam client session too soon

		private const char DefaultBackgroundKeysRedeemerSeparator = '\t';
		private const byte FamilySharingInactivityMinutes = 5;
		private const byte LoginCooldownInMinutes = 25; // Captcha disappears after around 20 minutes, so we make it 25
		private const uint LoginID = GlobalConfig.DefaultIPCPort; // This must be the same for all ASF bots and all ASF processes
		private const ushort MaxSteamMessageLength = 2048;
		private const byte MaxTwoFactorCodeFailures = 3;
		private const byte MinHeartBeatTTL = GlobalConfig.DefaultConnectionTimeout; // Assume client is responsive for at least that amount of seconds
		private const byte RedeemCooldownInHours = 1; // 1 hour since first redeem attempt

		internal static readonly ConcurrentDictionary<string, Bot> Bots = new ConcurrentDictionary<string, Bot>();

		private static readonly SemaphoreSlim BotsSemaphore = new SemaphoreSlim(1, 1);
		private static readonly SemaphoreSlim GiftsSemaphore = new SemaphoreSlim(1, 1);
		private static readonly SemaphoreSlim LoginSemaphore = new SemaphoreSlim(1, 1);

		private static SteamConfiguration SteamConfiguration;

		internal readonly ArchiLogger ArchiLogger;
		internal readonly ArchiWebHandler ArchiWebHandler;
		internal readonly ConcurrentDictionary<uint, (EPaymentMethod PaymentMethod, DateTime TimeCreated)> OwnedPackageIDs = new ConcurrentDictionary<uint, (EPaymentMethod PaymentMethod, DateTime TimeCreated)>();

		internal bool CanReceiveSteamCards => !IsAccountLimited && !IsAccountLocked;
		internal bool HasMobileAuthenticator => BotDatabase?.MobileAuthenticator != null;
		internal bool IsAccountLimited => AccountFlags.HasFlag(EAccountFlags.LimitedUser) || AccountFlags.HasFlag(EAccountFlags.LimitedUserForce);
		internal bool IsConnectedAndLoggedOn => SteamID != 0;

		[JsonProperty]
		internal bool IsPlayingPossible => !PlayingBlocked && (LibraryLockedBySteamID == 0);

		private readonly ArchiHandler ArchiHandler;
		private readonly BotDatabase BotDatabase;

		[JsonProperty]
		private readonly string BotName;

		private readonly CallbackManager CallbackManager;
		private readonly SemaphoreSlim CallbackSemaphore = new SemaphoreSlim(1, 1);

		[JsonProperty]
		private readonly CardsFarmer CardsFarmer;

		private readonly SemaphoreSlim GamesRedeemerInBackgroundSemaphore = new SemaphoreSlim(1, 1);
		private readonly ConcurrentHashSet<ulong> HandledGifts = new ConcurrentHashSet<ulong>();
		private readonly Timer HeartBeatTimer;
		private readonly SemaphoreSlim InitializationSemaphore = new SemaphoreSlim(1, 1);
		private readonly SemaphoreSlim LootingSemaphore = new SemaphoreSlim(1, 1);
		private readonly SemaphoreSlim PICSSemaphore = new SemaphoreSlim(1, 1);
		private readonly Statistics Statistics;
		private readonly SteamApps SteamApps;
		private readonly SteamClient SteamClient;
		private readonly ConcurrentHashSet<ulong> SteamFamilySharingIDs = new ConcurrentHashSet<ulong>();
		private readonly SteamFriends SteamFriends;
		private readonly SteamUser SteamUser;
		private readonly Trading Trading;

		private string BotPath => Path.Combine(SharedInfo.ConfigDirectory, BotName);
		private string ConfigFilePath => BotPath + SharedInfo.ConfigExtension;
		private string DatabaseFilePath => BotPath + SharedInfo.DatabaseExtension;
		private bool IsAccountLocked => AccountFlags.HasFlag(EAccountFlags.Lockdown);
		private string KeysToRedeemFilePath => BotPath + SharedInfo.KeysExtension;
		private string KeysToRedeemUnusedFilePath => KeysToRedeemFilePath + SharedInfo.KeysUnusedExtension;
		private string KeysToRedeemUsedFilePath => KeysToRedeemFilePath + SharedInfo.KeysUsedExtension;
		private string MobileAuthenticatorFilePath => BotPath + SharedInfo.MobileAuthenticatorExtension;
		private string SentryFilePath => BotPath + SharedInfo.SentryHashExtension;

		[JsonProperty(PropertyName = SharedInfo.UlongCompatibilityStringPrefix + nameof(SteamID))]
		private string SSteamID => SteamID.ToString();

		[JsonProperty]
		private ulong SteamID => SteamClient?.SteamID ?? 0;

		[JsonProperty]
		internal BotConfig BotConfig { get; private set; }

		internal ulong CachedSteamID { get; private set; }

		[JsonProperty]
		internal bool KeepRunning { get; private set; }

		internal bool PlayingWasBlocked { get; private set; }

		[JsonProperty]
		private EAccountFlags AccountFlags;

		private string AuthCode;

		[JsonProperty]
		private string AvatarHash;

		private Timer CardsFarmerResumeTimer;
		private Timer ConnectionFailureTimer;
		private string DeviceID;
		private Timer FamilySharingInactivityTimer;
		private bool FirstTradeSent;
		private Timer GamesRedeemerInBackgroundTimer;
		private byte HeartBeatFailures;
		private uint ItemsCount;
		private EResult LastLogOnResult;
		private DateTime LastLogonSessionReplaced;
		private ulong LibraryLockedBySteamID;
		private bool LootingAllowed = true;
		private bool LootingScheduled;
		private bool PlayingBlocked;
		private Timer PlayingWasBlockedTimer;
		private bool ReconnectOnUserInitiated;
		private Timer SendItemsTimer;
		private bool SkipFirstShutdown;
		private SteamSaleEvent SteamSaleEvent;
		private uint TradesCount;
		private string TwoFactorCode;
		private byte TwoFactorCodeFailures;

		private Bot(string botName, BotConfig botConfig, BotDatabase botDatabase) {
			if (string.IsNullOrEmpty(botName) || (botConfig == null) || (botDatabase == null)) {
				throw new ArgumentNullException(nameof(botName) + " || " + nameof(botConfig) + " || " + nameof(botDatabase));
			}

			if (Bots.ContainsKey(botName)) {
				throw new ArgumentException(string.Format(Strings.ErrorIsInvalid, nameof(botName)));
			}

			BotName = botName;
			BotConfig = botConfig;
			BotDatabase = botDatabase;

			ArchiLogger = new ArchiLogger(botName);

			// Register bot as available for ASF
			if (!Bots.TryAdd(botName, this)) {
				throw new ArgumentException(string.Format(Strings.ErrorIsInvalid, nameof(botName)));
			}

			if (HasMobileAuthenticator) {
				BotDatabase.MobileAuthenticator.Init(this);
			}

			// Initialize
			SteamClient = new SteamClient(SteamConfiguration);

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
			CallbackManager.Subscribe<SteamApps.GuestPassListCallback>(OnGuestPassList);
			CallbackManager.Subscribe<SteamApps.LicenseListCallback>(OnLicenseList);

			SteamFriends = SteamClient.GetHandler<SteamFriends>();
			CallbackManager.Subscribe<SteamFriends.ChatInviteCallback>(OnChatInvite);
			CallbackManager.Subscribe<SteamFriends.ChatMsgCallback>(OnChatMsg);
			CallbackManager.Subscribe<SteamFriends.FriendsListCallback>(OnFriendsList);
			CallbackManager.Subscribe<SteamFriends.FriendMsgCallback>(OnFriendMsg);
			CallbackManager.Subscribe<SteamFriends.FriendMsgEchoCallback>(OnFriendMsgEcho);
			CallbackManager.Subscribe<SteamFriends.FriendMsgHistoryCallback>(OnFriendMsgHistory);
			CallbackManager.Subscribe<SteamFriends.PersonaStateCallback>(OnPersonaState);

			SteamUser = SteamClient.GetHandler<SteamUser>();
			CallbackManager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOff);
			CallbackManager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
			CallbackManager.Subscribe<SteamUser.LoginKeyCallback>(OnLoginKey);
			CallbackManager.Subscribe<SteamUser.UpdateMachineAuthCallback>(OnMachineAuth);

			CallbackManager.Subscribe<ArchiHandler.OfflineMessageCallback>(OnOfflineMessage);
			CallbackManager.Subscribe<ArchiHandler.PlayingSessionStateCallback>(OnPlayingSessionState);
			CallbackManager.Subscribe<ArchiHandler.SharedLibraryLockStatusCallback>(OnSharedLibraryLockStatus);
			CallbackManager.Subscribe<ArchiHandler.UserNotificationsCallback>(OnUserNotifications);
			CallbackManager.Subscribe<ArchiHandler.VanityURLChangedCallback>(OnVanityURLChangedCallback);

			ArchiWebHandler = new ArchiWebHandler(this);
			CardsFarmer = new CardsFarmer(this);
			Trading = new Trading(this);

			if (!Debugging.IsDebugBuild && Program.GlobalConfig.Statistics) {
				Statistics = new Statistics(this);
			}

			InitModules();

			HeartBeatTimer = new Timer(
				async e => await HeartBeat().ConfigureAwait(false),
				null,
				TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(Program.LoadBalancingDelay * Bots.Count), // Delay
				TimeSpan.FromMinutes(1) // Period
			);
		}

		public void Dispose() {
			// Those are objects that are always being created if constructor doesn't throw exception
			CallbackSemaphore.Dispose();
			GamesRedeemerInBackgroundSemaphore.Dispose();
			InitializationSemaphore.Dispose();
			LootingSemaphore.Dispose();
			PICSSemaphore.Dispose();

			// Those are objects that might be null and the check should be in-place
			ArchiWebHandler?.Dispose();
			BotDatabase?.Dispose();
			CardsFarmer?.Dispose();
			CardsFarmerResumeTimer?.Dispose();
			ConnectionFailureTimer?.Dispose();
			FamilySharingInactivityTimer?.Dispose();
			GamesRedeemerInBackgroundTimer?.Dispose();
			HeartBeatTimer?.Dispose();
			PlayingWasBlockedTimer?.Dispose();
			SendItemsTimer?.Dispose();
			Statistics?.Dispose();
			SteamSaleEvent?.Dispose();
			Trading?.Dispose();
		}

		internal async Task<bool> AcceptConfirmations(bool accept, Steam.ConfirmationDetails.EType acceptedType = Steam.ConfirmationDetails.EType.Unknown, ulong acceptedSteamID = 0, IReadOnlyCollection<ulong> acceptedTradeIDs = null) {
			if (!HasMobileAuthenticator) {
				return false;
			}

			HashSet<MobileAuthenticator.Confirmation> confirmations = await BotDatabase.MobileAuthenticator.GetConfirmations(acceptedType).ConfigureAwait(false);
			if ((confirmations == null) || (confirmations.Count == 0)) {
				return true;
			}

			if ((acceptedSteamID == 0) && ((acceptedTradeIDs == null) || (acceptedTradeIDs.Count == 0))) {
				return await BotDatabase.MobileAuthenticator.HandleConfirmations(confirmations, accept).ConfigureAwait(false);
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

			foreach (MobileAuthenticator.Confirmation confirmation in results.Where(details => (details != null) && ((acceptedType != details.Type) || ((acceptedSteamID != 0) && (details.OtherSteamID64 != 0) && (acceptedSteamID != details.OtherSteamID64)) || ((acceptedTradeIDs != null) && (details.TradeOfferID != 0) && !acceptedTradeIDs.Contains(details.TradeOfferID)))).Select(details => details.Confirmation)) {
				confirmations.Remove(confirmation);
				if (confirmations.Count == 0) {
					return true;
				}
			}

			return await BotDatabase.MobileAuthenticator.HandleConfirmations(confirmations, accept).ConfigureAwait(false);
		}

		internal async Task<bool> DeleteAllRelatedFiles() {
			await BotDatabase.MakeReadOnly().ConfigureAwait(false);

			try {
				if (File.Exists(ConfigFilePath)) {
					File.Delete(ConfigFilePath);
				}

				if (File.Exists(DatabaseFilePath)) {
					File.Delete(DatabaseFilePath);
				}

				if (File.Exists(KeysToRedeemFilePath)) {
					File.Delete(KeysToRedeemFilePath);
				}

				if (File.Exists(KeysToRedeemUnusedFilePath)) {
					File.Delete(KeysToRedeemUnusedFilePath);
				}

				if (File.Exists(KeysToRedeemUsedFilePath)) {
					File.Delete(KeysToRedeemUsedFilePath);
				}

				if (File.Exists(MobileAuthenticatorFilePath)) {
					File.Delete(MobileAuthenticatorFilePath);
				}

				if (File.Exists(SentryFilePath)) {
					File.Delete(SentryFilePath);
				}

				return true;
			} catch (Exception e) {
				ArchiLogger.LogGenericException(e);
				return false;
			}
		}

		internal static string FormatBotResponse(string response, string botName) {
			if (string.IsNullOrEmpty(response) || string.IsNullOrEmpty(botName)) {
				ASF.ArchiLogger.LogNullError(nameof(response) + " || " + nameof(botName));
				return null;
			}

			return Environment.NewLine + "<" + botName + "> " + response;
		}

		internal async Task<(uint PlayableAppID, DateTime IgnoredUntil)> GetAppDataForIdling(uint appID, float hoursPlayed, bool allowRecursiveDiscovery = true, bool optimisticDiscovery = true) {
			if ((appID == 0) || (hoursPlayed < 0)) {
				ArchiLogger.LogNullError(nameof(appID) + " || " + nameof(hoursPlayed));
				return (0, DateTime.MaxValue);
			}

			if ((hoursPlayed < CardsFarmer.HoursForRefund) && !BotConfig.IdleRefundableGames) {
				HashSet<uint> packageIDs = Program.GlobalDatabase.GetPackageIDs(appID);
				if (packageIDs == null) {
					return (0, DateTime.MaxValue);
				}

				if (packageIDs.Count > 0) {
					DateTime mostRecent = DateTime.MinValue;

					foreach (uint packageID in packageIDs) {
						if (!OwnedPackageIDs.TryGetValue(packageID, out (EPaymentMethod PaymentMethod, DateTime TimeCreated) packageData)) {
							continue;
						}

						if (IsRefundable(packageData.PaymentMethod) && (packageData.TimeCreated > mostRecent)) {
							mostRecent = packageData.TimeCreated;
						}
					}

					if (mostRecent > DateTime.MinValue) {
						DateTime playableIn = mostRecent.AddDays(CardsFarmer.DaysForRefund);
						if (playableIn > DateTime.UtcNow) {
							return (0, playableIn);
						}
					}
				}
			}

			AsyncJobMultiple<SteamApps.PICSProductInfoCallback>.ResultSet productInfoResultSet = null;

			for (byte i = 0; (i < WebBrowser.MaxTries) && (productInfoResultSet == null) && IsConnectedAndLoggedOn; i++) {
				await PICSSemaphore.WaitAsync().ConfigureAwait(false);

				try {
#pragma warning disable ConfigureAwaitChecker // CAC001
					productInfoResultSet = await SteamApps.PICSGetProductInfo(appID, null, false);
#pragma warning restore ConfigureAwaitChecker // CAC001
				} catch (Exception e) {
					ArchiLogger.LogGenericWarningException(e);
				} finally {
					PICSSemaphore.Release();
				}
			}

			if (productInfoResultSet == null) {
				return (optimisticDiscovery ? appID : 0, DateTime.MinValue);
			}

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
							return (0, DateTime.MaxValue);
						default:
							ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(releaseState), releaseState));
							break;
					}
				}

				string type = commonProductInfo["type"].Value;
				if (string.IsNullOrEmpty(type)) {
					return (appID, DateTime.MinValue);
				}

				// We must convert this to uppercase, since Valve doesn't stick to any convention and we can have a case mismatch
				switch (type.ToUpperInvariant()) {
					// Types that can be idled
					case "APPLICATION":
					case "EPISODE":
					case "GAME":
					case "MOD":
					case "MOVIE":
					case "SERIES":
					case "TOOL":
					case "VIDEO":
						return (appID, DateTime.MinValue);

					// Types that can't be idled
					case "ADVERTISING":
					case "DEMO":
					case "DLC":
					case "GUIDE":
					case "HARDWARE":
						break;
					default:
						ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(type), type));
						break;
				}

				if (!allowRecursiveDiscovery) {
					return (0, DateTime.MinValue);
				}

				string listOfDlc = productInfo["extended"]["listofdlc"].Value;
				if (string.IsNullOrEmpty(listOfDlc)) {
					return (appID, DateTime.MinValue);
				}

				string[] dlcAppIDsTexts = listOfDlc.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

				foreach (string dlcAppIDsText in dlcAppIDsTexts) {
					if (!uint.TryParse(dlcAppIDsText, out uint dlcAppID) || (dlcAppID == 0)) {
						ArchiLogger.LogNullError(nameof(dlcAppID));
						break;
					}

					(uint playableAppID, _) = await GetAppDataForIdling(dlcAppID, hoursPlayed, false, false).ConfigureAwait(false);
					if (playableAppID != 0) {
						return (playableAppID, DateTime.MinValue);
					}
				}

				return (appID, DateTime.MinValue);
			}

			return ((productInfoResultSet.Complete && !productInfoResultSet.Failed) || optimisticDiscovery ? appID : 0, DateTime.MinValue);
		}

		internal static HashSet<Bot> GetBots(string args) {
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
				} else if (botName.StartsWith("r!", StringComparison.OrdinalIgnoreCase)) {
					string botPattern = botName.Substring(2);
					IEnumerable<Bot> regexMatches = Bots.Where(kvp => Regex.Match(kvp.Key, botPattern, RegexOptions.IgnoreCase).Success).Select(kvp => kvp.Value);
					result.UnionWith(regexMatches);
				}

				if (!Bots.TryGetValue(botName, out Bot targetBot)) {
					continue;
				}

				result.Add(targetBot);
			}

			return result;
		}

		internal async Task<HashSet<uint>> GetMarketableAppIDs() => await ArchiWebHandler.GetAppList().ConfigureAwait(false);

		internal async Task<Dictionary<uint, (uint ChangeNumber, HashSet<uint> AppIDs)>> GetPackagesData(IReadOnlyCollection<uint> packageIDs) {
			if ((packageIDs == null) || (packageIDs.Count == 0)) {
				ArchiLogger.LogNullError(nameof(packageIDs));
				return null;
			}

			AsyncJobMultiple<SteamApps.PICSProductInfoCallback>.ResultSet productInfoResultSet = null;

			for (byte i = 0; (i < WebBrowser.MaxTries) && (productInfoResultSet == null) && IsConnectedAndLoggedOn; i++) {
				await PICSSemaphore.WaitAsync().ConfigureAwait(false);

				try {
#pragma warning disable ConfigureAwaitChecker // CAC001
					productInfoResultSet = await SteamApps.PICSGetProductInfo(Enumerable.Empty<uint>(), packageIDs);
#pragma warning restore ConfigureAwaitChecker // CAC001
				} catch (Exception e) {
					ArchiLogger.LogGenericWarningException(e);
				} finally {
					PICSSemaphore.Release();
				}
			}

			if (productInfoResultSet == null) {
				return null;
			}

			Dictionary<uint, (uint ChangeNumber, HashSet<uint> AppIDs)> result = new Dictionary<uint, (uint ChangeNumber, HashSet<uint> AppIDs)>();

			foreach (SteamApps.PICSProductInfoCallback.PICSProductInfo productInfo in productInfoResultSet.Results.SelectMany(productInfoResult => productInfoResult.Packages).Where(productInfoPackages => productInfoPackages.Key != 0).Select(productInfoPackages => productInfoPackages.Value)) {
				if (productInfo.KeyValues == KeyValue.Invalid) {
					ArchiLogger.LogNullError(nameof(productInfo));
					return null;
				}

				(uint ChangeNumber, HashSet<uint> AppIDs) value = (productInfo.ChangeNumber, null);

				try {
					KeyValue appIDs = productInfo.KeyValues["appids"];
					if (appIDs == KeyValue.Invalid) {
						continue;
					}

					value.AppIDs = new HashSet<uint>();

					foreach (string appIDText in appIDs.Children.Select(app => app.Value)) {
						if (!uint.TryParse(appIDText, out uint appID) || (appID == 0)) {
							ArchiLogger.LogNullError(nameof(appID));
							return null;
						}

						value.AppIDs.Add(appID);
					}
				} finally {
					result[productInfo.ID] = value;
				}
			}

			return result;
		}

		internal async Task<byte?> GetTradeHoldDuration(ulong steamID, ulong tradeID) {
			if ((steamID == 0) || (tradeID == 0)) {
				ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(tradeID));
				return null;
			}

			if (SteamFriends.GetFriendRelationship(steamID) == EFriendRelationship.Friend) {
				return await ArchiWebHandler.GetTradeHoldDurationForUser(steamID).ConfigureAwait(false);
			}

			Bot targetBot = Bots.Values.FirstOrDefault(bot => bot.CachedSteamID == steamID);
			if (targetBot != null) {
				string targetTradeToken = await targetBot.ArchiWebHandler.GetTradeToken().ConfigureAwait(false);
				if (!string.IsNullOrEmpty(targetTradeToken)) {
					return await ArchiWebHandler.GetTradeHoldDurationForUser(steamID, targetTradeToken).ConfigureAwait(false);
				}
			}

			return await ArchiWebHandler.GetTradeHoldDurationForTrade(tradeID).ConfigureAwait(false);
		}

		internal async Task IdleGame(CardsFarmer.Game game) {
			if (game == null) {
				ArchiLogger.LogNullError(nameof(game));
				return;
			}

			await ArchiHandler.PlayGames(game.PlayableAppID.ToEnumerable(), BotConfig.CustomGamePlayedWhileFarming).ConfigureAwait(false);
		}

		internal async Task IdleGames(IReadOnlyCollection<CardsFarmer.Game> games) {
			if ((games == null) || (games.Count == 0)) {
				ArchiLogger.LogNullError(nameof(games));
				return;
			}

			await ArchiHandler.PlayGames(games.Select(game => game.PlayableAppID), BotConfig.CustomGamePlayedWhileFarming).ConfigureAwait(false);
		}

		internal async Task ImportKeysToRedeem(string filePath) {
			if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) {
				ArchiLogger.LogNullError(nameof(filePath));
				return;
			}

			try {
				OrderedDictionary gamesToRedeemInBackground = new OrderedDictionary();

				using (StreamReader reader = new StreamReader(filePath)) {
					string line;

					while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null) {
						if (line.Length == 0) {
							continue;
						}

						string[] parsedArgs = line.Split(DefaultBackgroundKeysRedeemerSeparator, StringSplitOptions.RemoveEmptyEntries);
						if (parsedArgs.Length < 2) {
							ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorIsInvalid, line));
							continue;
						}

						string name = parsedArgs[0];
						string key = parsedArgs[parsedArgs.Length - 1];

						gamesToRedeemInBackground[key] = name;
					}
				}

				if (gamesToRedeemInBackground.Count > 0) {
					await ValidateAndAddGamesToRedeemInBackground(gamesToRedeemInBackground).ConfigureAwait(false);
				}

				File.Delete(filePath);
			} catch (Exception e) {
				ArchiLogger.LogGenericException(e);
			}
		}

		internal static async Task InitializeSteamConfiguration(ProtocolTypes protocolTypes, uint cellID, IServerListProvider serverListProvider) {
			if (serverListProvider == null) {
				ASF.ArchiLogger.LogNullError(nameof(serverListProvider));
				return;
			}

			SteamConfiguration = SteamConfiguration.Create(builder => builder.WithProtocolTypes(protocolTypes).WithCellID(cellID).WithServerListProvider(serverListProvider));

			// Ensure that we ask for a list of servers if we don't have any saved servers available
			IEnumerable<ServerRecord> servers = await SteamConfiguration.ServerListProvider.FetchServerListAsync().ConfigureAwait(false);
			if (servers?.Any() != true) {
				ASF.ArchiLogger.LogGenericInfo(string.Format(Strings.Initializing, nameof(SteamDirectory)));

				try {
					await SteamDirectory.LoadAsync(SteamConfiguration).ConfigureAwait(false);
					ASF.ArchiLogger.LogGenericInfo(Strings.Success);
				} catch {
					ASF.ArchiLogger.LogGenericWarning(Strings.BotSteamDirectoryInitializationFailed);
				}
			}
		}

		internal bool IsBlacklistedFromIdling(uint appID) {
			if (appID == 0) {
				ArchiLogger.LogNullError(nameof(appID));
				return false;
			}

			return BotDatabase.IsBlacklistedFromIdling(appID);
		}

		internal bool IsBlacklistedFromTrades(ulong steamID) {
			if (steamID == 0) {
				ArchiLogger.LogNullError(nameof(steamID));
				return false;
			}

			return BotDatabase.IsBlacklistedFromTrades(steamID);
		}

		internal bool IsMaster(ulong steamID) {
			if (steamID == 0) {
				ArchiLogger.LogNullError(nameof(steamID));
				return false;
			}

			return IsOwner(steamID) || (GetSteamUserPermission(steamID) >= BotConfig.EPermission.Master);
		}

		internal bool IsPriorityIdling(uint appID) {
			if (appID == 0) {
				ArchiLogger.LogNullError(nameof(appID));
				return false;
			}

			return BotDatabase.IsPriorityIdling(appID);
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

		internal async Task OnConfigChanged(bool deleted) {
			if (deleted) {
				Destroy();
				return;
			}

			BotConfig botConfig = await BotConfig.Load(ConfigFilePath).ConfigureAwait(false);

			if (botConfig == null) {
				Destroy();
				return;
			}

			if (botConfig == BotConfig) {
				return;
			}

			await InitializationSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				if (botConfig == BotConfig) {
					return;
				}

				Stop(botConfig.Enabled);
				BotConfig = botConfig;

				InitModules();
				InitStart();
			} finally {
				InitializationSemaphore.Release();
			}
		}

		internal async Task OnFarmingFinished(bool farmedSomething) {
			await OnFarmingStopped().ConfigureAwait(false);

			if (farmedSomething || !FirstTradeSent) {
				FirstTradeSent = true;
				await LootIfNeeded().ConfigureAwait(false);
			}

			if (BotConfig.ShutdownOnFarmingFinished) {
				if (farmedSomething || (Program.GlobalConfig.IdleFarmingPeriod == 0)) {
					Stop();
					return;
				}

				if (SkipFirstShutdown) {
					SkipFirstShutdown = false;
				} else {
					Stop();
				}
			}
		}

		internal async Task OnFarmingStopped() => await ResetGamesPlayed().ConfigureAwait(false);

		internal async Task<bool> RefreshSession() {
			if (!IsConnectedAndLoggedOn) {
				return false;
			}

			SteamUser.WebAPIUserNonceCallback callback;

			try {
#pragma warning disable ConfigureAwaitChecker // CAC001
				callback = await SteamUser.RequestWebAPIUserNonce();
#pragma warning restore ConfigureAwaitChecker // CAC001
			} catch (Exception e) {
				ArchiLogger.LogGenericWarningException(e);
				await Connect(true).ConfigureAwait(false);
				return false;
			}

			if (string.IsNullOrEmpty(callback?.Nonce)) {
				await Connect(true).ConfigureAwait(false);
				return false;
			}

			if (await ArchiWebHandler.Init(CachedSteamID, SteamClient.Universe, callback.Nonce, BotConfig.SteamParentalPIN).ConfigureAwait(false)) {
				return true;
			}

			await Connect(true).ConfigureAwait(false);
			return false;
		}

		internal static async Task RegisterBot(string botName) {
			if (string.IsNullOrEmpty(botName)) {
				ASF.ArchiLogger.LogNullError(nameof(botName));
				return;
			}

			if (Bots.ContainsKey(botName)) {
				return;
			}

			string botPath = Path.Combine(SharedInfo.ConfigDirectory, botName);
			string configFilePath = botPath + SharedInfo.ConfigExtension;

			BotConfig botConfig = await BotConfig.Load(botPath + SharedInfo.ConfigExtension).ConfigureAwait(false);
			if (botConfig == null) {
				ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorBotConfigInvalid, configFilePath));
				return;
			}

			if (Debugging.IsUserDebugging) {
				ASF.ArchiLogger.LogGenericDebug(configFilePath + ": " + JsonConvert.SerializeObject(botConfig, Formatting.Indented));
			}

			string databaseFilePath = botPath + SharedInfo.DatabaseExtension;

			BotDatabase botDatabase = await BotDatabase.Load(databaseFilePath).ConfigureAwait(false);
			if (botDatabase == null) {
				ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorDatabaseInvalid, databaseFilePath));
				return;
			}

			if (Debugging.IsUserDebugging) {
				ASF.ArchiLogger.LogGenericDebug(databaseFilePath + ": " + JsonConvert.SerializeObject(botDatabase, Formatting.Indented));
			}

			Bot bot;
			await BotsSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				if (Bots.ContainsKey(botName)) {
					return;
				}

				bot = new Bot(botName, botConfig, botDatabase);
			} finally {
				BotsSemaphore.Release();
			}

			bot.InitStart();
		}

		internal void RequestPersonaStateUpdate() {
			if (!IsConnectedAndLoggedOn) {
				return;
			}

			SteamFriends.RequestFriendInfo(CachedSteamID, EClientPersonaStateFlag.PlayerName | EClientPersonaStateFlag.Presence);
		}

		internal async Task<string> Response(ulong steamID, string message, ulong chatID = 0) {
			if ((steamID == 0) || string.IsNullOrEmpty(message)) {
				ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(message));
				return null;
			}

			if (!string.IsNullOrEmpty(Program.GlobalConfig.CommandPrefix)) {
				if (!message.StartsWith(Program.GlobalConfig.CommandPrefix, StringComparison.Ordinal)) {
					return null;
				}

				message = message.Substring(Program.GlobalConfig.CommandPrefix.Length);
			}

			string[] args = message.Split((char[]) null, StringSplitOptions.RemoveEmptyEntries);

			switch (args.Length) {
				case 0:
					ArchiLogger.LogNullError(nameof(args));
					return null;
				case 1:
					switch (args[0].ToUpperInvariant()) {
						case "2FA":
							return await Response2FA(steamID).ConfigureAwait(false);
						case "2FANO":
							return await Response2FAConfirm(steamID, false).ConfigureAwait(false);
						case "2FAOK":
							return await Response2FAConfirm(steamID, true).ConfigureAwait(false);
						case "BL":
							return ResponseBlacklist(steamID);
						case "EXIT":
							return ResponseExit(steamID);
						case "FARM":
							return await ResponseFarm(steamID).ConfigureAwait(false);
						case "HELP":
							return ResponseHelp(steamID);
						case "IB":
							return ResponseIdleBlacklist(steamID);
						case "IQ":
							return ResponseIdleQueue(steamID);
						case "LEAVE":
							if (chatID != 0) {
								return ResponseLeave(steamID, chatID);
							}

							goto default;
						case "LOOT":
							return await ResponseLoot(steamID).ConfigureAwait(false);
						case "LOOT&":
							return ResponseLootSwitch(steamID);
						case "PASSWORD":
							return ResponsePassword(steamID);
						case "PAUSE":
							return await ResponsePause(steamID, true).ConfigureAwait(false);
						case "PAUSE~":
							return await ResponsePause(steamID, false).ConfigureAwait(false);
						case "REJOINCHAT":
							return ResponseRejoinChat(steamID);
						case "RESUME":
							return ResponseResume(steamID);
						case "RESTART":
							return ResponseRestart(steamID);
						case "SA":
							return await ResponseStatus(steamID, SharedInfo.ASF).ConfigureAwait(false);
						case "START":
							return ResponseStart(steamID);
						case "STATS":
							return ResponseStats(steamID);
						case "STATUS":
							return ResponseStatus(steamID).Response;
						case "STOP":
							return ResponseStop(steamID);
						case "UNPACK":
							return await ResponseUnpackBoosters(steamID).ConfigureAwait(false);
						case "UPDATE":
							return await ResponseUpdate(steamID).ConfigureAwait(false);
						case "VERSION":
							return ResponseVersion(steamID);
						default:
							return ResponseUnknown(steamID);
					}
				default:
					switch (args[0].ToUpperInvariant()) {
						case "2FA":
							return await Response2FA(steamID, Utilities.GetArgsAsText(args, 1, ",")).ConfigureAwait(false);
						case "2FANO":
							return await Response2FAConfirm(steamID, Utilities.GetArgsAsText(args, 1, ","), false).ConfigureAwait(false);
						case "2FAOK":
							return await Response2FAConfirm(steamID, Utilities.GetArgsAsText(args, 1, ","), true).ConfigureAwait(false);
						case "ADDLICENSE":
							if (args.Length > 2) {
								return await ResponseAddLicense(steamID, args[1], Utilities.GetArgsAsText(args, 2, ",")).ConfigureAwait(false);
							}

							return await ResponseAddLicense(steamID, args[1]).ConfigureAwait(false);
						case "BL":
							return await ResponseBlacklist(steamID, Utilities.GetArgsAsText(args, 1, ",")).ConfigureAwait(false);
						case "BLADD":
							if (args.Length > 2) {
								return await ResponseBlacklistAdd(steamID, args[1], Utilities.GetArgsAsText(args, 2, ",")).ConfigureAwait(false);
							}

							return await ResponseBlacklistAdd(steamID, args[1]).ConfigureAwait(false);
						case "BLRM":
							if (args.Length > 2) {
								return await ResponseBlacklistRemove(steamID, args[1], Utilities.GetArgsAsText(args, 2, ",")).ConfigureAwait(false);
							}

							return await ResponseBlacklistRemove(steamID, args[1]).ConfigureAwait(false);
						case "FARM":
							return await ResponseFarm(steamID, Utilities.GetArgsAsText(args, 1, ",")).ConfigureAwait(false);
						case "INPUT":
							if (args.Length > 3) {
								return await ResponseInput(steamID, args[1], args[2], Utilities.GetArgsAsText(message, 3)).ConfigureAwait(false);
							}

							if (args.Length > 2) {
								return ResponseInput(steamID, args[1], args[2]);
							}

							goto default;
						case "IB":
							return await ResponseIdleBlacklist(steamID, Utilities.GetArgsAsText(args, 1, ",")).ConfigureAwait(false);
						case "IBADD":
							if (args.Length > 2) {
								return await ResponseIdleBlacklistAdd(steamID, args[1], Utilities.GetArgsAsText(args, 2, ",")).ConfigureAwait(false);
							}

							return await ResponseIdleBlacklistAdd(steamID, args[1]).ConfigureAwait(false);
						case "IBRM":
							if (args.Length > 2) {
								return await ResponseIdleBlacklistRemove(steamID, args[1], Utilities.GetArgsAsText(args, 2, ",")).ConfigureAwait(false);
							}

							return await ResponseIdleBlacklistRemove(steamID, args[1]).ConfigureAwait(false);
						case "IQ":
							return await ResponseIdleQueue(steamID, Utilities.GetArgsAsText(args, 1, ",")).ConfigureAwait(false);
						case "IQADD":
							if (args.Length > 2) {
								return await ResponseIdleQueueAdd(steamID, args[1], Utilities.GetArgsAsText(args, 2, ",")).ConfigureAwait(false);
							}

							return await ResponseIdleQueueAdd(steamID, args[1]).ConfigureAwait(false);
						case "IQRM":
							if (args.Length > 2) {
								return await ResponseIdleQueueRemove(steamID, args[1], Utilities.GetArgsAsText(args, 2, ",")).ConfigureAwait(false);
							}

							return await ResponseIdleQueueRemove(steamID, args[1]).ConfigureAwait(false);
						case "LEAVE":
							if (chatID > 0) {
								return await ResponseLeave(steamID, Utilities.GetArgsAsText(args, 1, ","), chatID).ConfigureAwait(false);
							}

							goto default;
						case "LOOT":
							return await ResponseLoot(steamID, Utilities.GetArgsAsText(args, 1, ",")).ConfigureAwait(false);
						case "LOOT^":
							if (args.Length > 3) {
								return await ResponseAdvancedLoot(steamID, args[1], args[2], Utilities.GetArgsAsText(args, 3, ",")).ConfigureAwait(false);
							}

							if (args.Length > 2) {
								return await ResponseAdvancedLoot(steamID, args[1], args[2]).ConfigureAwait(false);
							}

							goto default;
						case "LOOT@":
							if (args.Length > 2) {
								return await ResponseLootByRealAppIDs(steamID, args[1], Utilities.GetArgsAsText(args, 2, ",")).ConfigureAwait(false);
							}

							return await ResponseLootByRealAppIDs(steamID, args[1]).ConfigureAwait(false);
						case "LOOT&":
							return await ResponseLootSwitch(steamID, Utilities.GetArgsAsText(args, 1, ",")).ConfigureAwait(false);
						case "NICKNAME":
							if (args.Length > 2) {
								return await ResponseNickname(steamID, args[1], Utilities.GetArgsAsText(message, 2)).ConfigureAwait(false);
							}

							return ResponseNickname(steamID, args[1]);
						case "OA":
							return await ResponseOwns(steamID, SharedInfo.ASF, Utilities.GetArgsAsText(message, 1)).ConfigureAwait(false);
						case "OWNS":
							if (args.Length > 2) {
								return await ResponseOwns(steamID, args[1], Utilities.GetArgsAsText(message, 2)).ConfigureAwait(false);
							}

							return (await ResponseOwns(steamID, args[1]).ConfigureAwait(false)).Response;
						case "PASSWORD":
							return await ResponsePassword(steamID, Utilities.GetArgsAsText(args, 1, ",")).ConfigureAwait(false);
						case "PAUSE":
							return await ResponsePause(steamID, Utilities.GetArgsAsText(args, 1, ","), true).ConfigureAwait(false);
						case "PAUSE~":
							return await ResponsePause(steamID, Utilities.GetArgsAsText(args, 1, ","), false).ConfigureAwait(false);
						case "PAUSE&":
							if (args.Length > 2) {
								return await ResponsePause(steamID, args[1], true, Utilities.GetArgsAsText(args, 2, ",")).ConfigureAwait(false);
							}

							return await ResponsePause(steamID, true, args[1]).ConfigureAwait(false);
						case "PLAY":
							if (args.Length > 2) {
								return await ResponsePlay(steamID, args[1], Utilities.GetArgsAsText(args, 2, ",")).ConfigureAwait(false);
							}

							return await ResponsePlay(steamID, args[1]).ConfigureAwait(false);
						case "PRIVACY":
							if (args.Length > 2) {
								return await ResponsePrivacy(steamID, args[1], Utilities.GetArgsAsText(args, 2, ",")).ConfigureAwait(false);
							}

							return await ResponsePrivacy(steamID, args[1]).ConfigureAwait(false);
						case "R":
						case "REDEEM":
							if (args.Length > 2) {
								return await ResponseRedeem(steamID, args[1], Utilities.GetArgsAsText(args, 2, ",")).ConfigureAwait(false);
							}

							return await ResponseRedeem(steamID, args[1]).ConfigureAwait(false);
						case "R^":
						case "REDEEM^":
							if (args.Length > 3) {
								return await ResponseAdvancedRedeem(steamID, args[1], args[2], Utilities.GetArgsAsText(args, 3, ",")).ConfigureAwait(false);
							}

							if (args.Length > 2) {
								return await ResponseAdvancedRedeem(steamID, args[1], args[2]).ConfigureAwait(false);
							}

							goto default;
						case "REJOINCHAT":
							return await ResponseRejoinChat(steamID, Utilities.GetArgsAsText(args, 1, ",")).ConfigureAwait(false);
						case "RESUME":
							return await ResponseResume(steamID, Utilities.GetArgsAsText(args, 1, ",")).ConfigureAwait(false);
						case "START":
							return await ResponseStart(steamID, Utilities.GetArgsAsText(args, 1, ",")).ConfigureAwait(false);
						case "STATUS":
							return await ResponseStatus(steamID, Utilities.GetArgsAsText(args, 1, ",")).ConfigureAwait(false);
						case "STOP":
							return await ResponseStop(steamID, Utilities.GetArgsAsText(args, 1, ",")).ConfigureAwait(false);
						case "TRANSFER":
							if (args.Length > 3) {
								return await ResponseTransfer(steamID, args[1], args[2], Utilities.GetArgsAsText(args, 3, ",")).ConfigureAwait(false);
							}

							if (args.Length > 2) {
								return await ResponseTransfer(steamID, args[1], args[2]).ConfigureAwait(false);
							}

							goto default;
						case "UNPACK":
							return await ResponseUnpackBoosters(steamID, Utilities.GetArgsAsText(args, 1, ",")).ConfigureAwait(false);
						default:
							return ResponseUnknown(steamID);
					}
			}
		}

		internal async Task SendMessage(ulong steamID, string message) {
			if ((steamID == 0) || string.IsNullOrEmpty(message)) {
				ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(message));
				return;
			}

			if (new SteamID(steamID).IsChatAccount) {
				await SendMessageToChannel(steamID, message).ConfigureAwait(false);
			} else {
				await SendMessageToUser(steamID, message).ConfigureAwait(false);
			}
		}

		internal void Stop(bool skipShutdownEvent = false) {
			if (!KeepRunning) {
				return;
			}

			ArchiLogger.LogGenericInfo(Strings.BotStopping);
			KeepRunning = false;

			if (SteamClient.IsConnected) {
				Disconnect();
			}

			if (!skipShutdownEvent) {
				Utilities.InBackground(Events.OnBotShutdown);
			}
		}

		internal async Task ValidateAndAddGamesToRedeemInBackground(OrderedDictionary gamesToRedeemInBackground) {
			if ((gamesToRedeemInBackground == null) || (gamesToRedeemInBackground.Count == 0)) {
				ArchiLogger.LogNullError(nameof(gamesToRedeemInBackground));
				return;
			}

			HashSet<object> invalidKeys = new HashSet<object>();

			foreach (DictionaryEntry game in gamesToRedeemInBackground) {
				bool invalid = false;

				string key = game.Key as string;
				if (string.IsNullOrEmpty(key)) {
					invalid = true;
					ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorIsInvalid, nameof(key)));
				} else if (!IsValidCdKey(key)) {
					invalid = true;
					ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorIsInvalid, key));
				}

				string name = game.Value as string;
				if (string.IsNullOrEmpty(name)) {
					invalid = true;
					ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorIsInvalid, nameof(name)));
				}

				if (invalid) {
					invalidKeys.Add(game.Key);
				}
			}

			if (invalidKeys.Count > 0) {
				foreach (string invalidKey in invalidKeys) {
					gamesToRedeemInBackground.Remove(invalidKey);
				}

				if (gamesToRedeemInBackground.Count == 0) {
					return;
				}
			}

			await BotDatabase.AddGamesToRedeemInBackground(gamesToRedeemInBackground).ConfigureAwait(false);

			if ((GamesRedeemerInBackgroundTimer == null) && BotDatabase.HasGamesToRedeemInBackground && IsConnectedAndLoggedOn) {
				Utilities.InBackground(RedeemGamesInBackground);
			}
		}

		private async Task CheckFamilySharingInactivity() {
			StopFamilySharingInactivityTimer();

			if (!IsPlayingPossible) {
				return;
			}

			ArchiLogger.LogGenericInfo(Strings.BotAutomaticIdlingPauseTimeout);

			if (!await CardsFarmer.Resume(false).ConfigureAwait(false)) {
				await ResetGamesPlayed().ConfigureAwait(false);
			}
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

			if (!await CardsFarmer.Resume(false).ConfigureAwait(false)) {
				await ResetGamesPlayed().ConfigureAwait(false);
			}
		}

		private async Task Connect(bool force = false) {
			if (!force && (!KeepRunning || SteamClient.IsConnected)) {
				return;
			}

			await LimitLoginRequestsAsync().ConfigureAwait(false);

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
				Utilities.InBackground(() => Stop());
			}

			Bots.TryRemove(BotName, out _);
		}

		private void Disconnect() {
			StopConnectionFailureTimer();
			SteamClient.Disconnect();
		}

		private string FormatBotResponse(string response) {
			if (string.IsNullOrEmpty(response)) {
				ASF.ArchiLogger.LogNullError(nameof(response));
				return null;
			}

			return Environment.NewLine + "<" + BotName + "> " + response;
		}

		private static string FormatStaticResponse(string response) {
			if (string.IsNullOrEmpty(response)) {
				ASF.ArchiLogger.LogNullError(nameof(response));
				return null;
			}

			return Environment.NewLine + response;
		}

		private ulong GetFirstSteamMasterID() => BotConfig.SteamUserPermissions.Where(kv => (kv.Key != 0) && (kv.Value == BotConfig.EPermission.Master)).Select(kv => kv.Key).OrderByDescending(steamID => steamID != CachedSteamID).ThenBy(steamID => steamID).FirstOrDefault();

		private BotConfig.EPermission GetSteamUserPermission(ulong steamID) {
			if (steamID == 0) {
				ArchiLogger.LogNullError(nameof(steamID));
				return BotConfig.EPermission.None;
			}

			return BotConfig.SteamUserPermissions.TryGetValue(steamID, out BotConfig.EPermission permission) ? permission : BotConfig.EPermission.None;
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

			string response = await Response(steamID, message, chatID).ConfigureAwait(false);

			// We respond with null when user is not authorized (and similar)
			if (string.IsNullOrEmpty(response)) {
				return;
			}

			await SendMessage(chatID, response).ConfigureAwait(false);
		}

		private async Task HeartBeat() {
			if (!KeepRunning || !IsConnectedAndLoggedOn || (HeartBeatFailures == byte.MaxValue)) {
				return;
			}

			try {
				if (DateTime.UtcNow.Subtract(ArchiHandler.LastPacketReceived).TotalSeconds > MinHeartBeatTTL) {
#pragma warning disable ConfigureAwaitChecker // CAC001
					await SteamFriends.RequestProfileInfo(SteamClient.SteamID);
#pragma warning restore ConfigureAwaitChecker // CAC001
				}

				HeartBeatFailures = 0;

				if (Statistics != null) {
					Utilities.InBackground(Statistics.OnHeartBeat);
				}
			} catch (Exception e) {
				ArchiLogger.LogGenericDebuggingException(e);

				if (!KeepRunning || !IsConnectedAndLoggedOn || (HeartBeatFailures == byte.MaxValue)) {
					return;
				}

				if (++HeartBeatFailures >= (byte) Math.Ceiling(Program.GlobalConfig.ConnectionTimeout / 10.0)) {
					HeartBeatFailures = byte.MaxValue;
					ArchiLogger.LogGenericWarning(Strings.BotConnectionLost);
					Utilities.InBackground(() => Connect(true));
				}
			}
		}

		private async Task ImportAuthenticator(string maFilePath) {
			if (HasMobileAuthenticator || !File.Exists(maFilePath)) {
				return;
			}

			ArchiLogger.LogGenericInfo(Strings.BotAuthenticatorConverting);

			try {
				MobileAuthenticator authenticator = JsonConvert.DeserializeObject<MobileAuthenticator>(await RuntimeCompatibility.File.ReadAllTextAsync(maFilePath).ConfigureAwait(false));
				await BotDatabase.SetMobileAuthenticator(authenticator).ConfigureAwait(false);
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
						await BotDatabase.SetMobileAuthenticator().ConfigureAwait(false);
						return;
					}

					SetUserInput(ASF.EUserInputType.DeviceID, deviceID);
				}

				await BotDatabase.CorrectMobileAuthenticatorDeviceID(DeviceID).ConfigureAwait(false);
			}

			ArchiLogger.LogGenericInfo(Strings.BotAuthenticatorImportFinished);
		}

		private void InitConnectionFailureTimer() {
			if (ConnectionFailureTimer != null) {
				return;
			}

			ConnectionFailureTimer = new Timer(
				async e => await InitPermanentConnectionFailure().ConfigureAwait(false),
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

			if (requiresPassword && string.IsNullOrEmpty(BotConfig.SteamPassword)) {
				string steamPassword = Program.GetUserInput(ASF.EUserInputType.Password, BotName);
				if (string.IsNullOrEmpty(steamPassword)) {
					return false;
				}

				SetUserInput(ASF.EUserInputType.Password, steamPassword);
			}

			return true;
		}

		private void InitModules() {
			CardsFarmer.SetInitialState(BotConfig.Paused);

			if (SendItemsTimer != null) {
				SendItemsTimer.Dispose();
				SendItemsTimer = null;
			}

			if (BotConfig.SendTradePeriod > 0) {
				ulong steamMasterID = GetFirstSteamMasterID();
				if (steamMasterID != 0) {
					SendItemsTimer = new Timer(
						async e => await ResponseLoot(steamMasterID).ConfigureAwait(false),
						null,
						TimeSpan.FromHours(BotConfig.SendTradePeriod) + TimeSpan.FromSeconds(Program.LoadBalancingDelay * Bots.Count), // Delay
						TimeSpan.FromHours(BotConfig.SendTradePeriod) // Period
					);
				}
			}

			if (SteamSaleEvent != null) {
				SteamSaleEvent.Dispose();
				SteamSaleEvent = null;
			}

			if (BotConfig.AutoSteamSaleEvent) {
				SteamSaleEvent = new SteamSaleEvent(this);
			}
		}

		private async Task InitPermanentConnectionFailure() {
			if (!KeepRunning) {
				return;
			}

			ArchiLogger.LogGenericWarning(Strings.BotHeartBeatFailed);
			Destroy(true);
			await RegisterBot(BotName).ConfigureAwait(false);
		}

		private void InitStart() {
			if (!BotConfig.Enabled) {
				ArchiLogger.LogGenericInfo(Strings.BotInstanceNotStartingBecauseDisabled);
				return;
			}

			// Start
			Utilities.InBackground(Start);
		}

		private static bool IsAllowedToExecuteCommands(ulong steamID) {
			if (steamID == 0) {
				ASF.ArchiLogger.LogNullError(nameof(steamID));
				return false;
			}

			// This should have reference to lowest permission for command execution
			return Bots.Values.Any(bot => bot.IsFamilySharing(steamID));
		}

		private bool IsFamilySharing(ulong steamID) {
			if (steamID == 0) {
				ArchiLogger.LogNullError(nameof(steamID));
				return false;
			}

			return IsOwner(steamID) || SteamFamilySharingIDs.Contains(steamID) || (GetSteamUserPermission(steamID) >= BotConfig.EPermission.FamilySharing);
		}

		private bool IsMasterClanID(ulong steamID) {
			if (steamID == 0) {
				ArchiLogger.LogNullError(nameof(steamID));
				return false;
			}

			return steamID == BotConfig.SteamMasterClanID;
		}

		private bool IsOperator(ulong steamID) {
			if (steamID == 0) {
				ArchiLogger.LogNullError(nameof(steamID));
				return false;
			}

			return IsOwner(steamID) || (GetSteamUserPermission(steamID) >= BotConfig.EPermission.Operator);
		}

		private static bool IsOwner(ulong steamID) {
			if (steamID == 0) {
				ASF.ArchiLogger.LogNullError(nameof(steamID));
				return false;
			}

			return (steamID == Program.GlobalConfig.SteamOwnerID) || (Debugging.IsDebugBuild && (steamID == SharedInfo.ArchiSteamID));
		}

		private static bool IsRefundable(EPaymentMethod method) {
			if (method == EPaymentMethod.None) {
				ASF.ArchiLogger.LogNullError(nameof(method));
				return false;
			}

			switch (method) {
				case EPaymentMethod.ActivationCode:
				case EPaymentMethod.Complimentary: // This is also a flag
				case EPaymentMethod.GuestPass:
				case EPaymentMethod.HardwarePromo:
					return false;
				default:
					if (method.HasFlag(EPaymentMethod.Complimentary)) {
						return false;
					}

					return true;
			}
		}

		private static bool IsValidCdKey(string key) {
			if (string.IsNullOrEmpty(key)) {
				ASF.ArchiLogger.LogNullError(nameof(key));
				return false;
			}

			return Regex.IsMatch(key, @"^[0-9A-Z]{4,7}-[0-9A-Z]{4,7}-[0-9A-Z]{4,7}(?:(?:-[0-9A-Z]{4,7})?(?:-[0-9A-Z]{4,7}))?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
		}

		private void JoinMasterChat() {
			if (!IsConnectedAndLoggedOn || (BotConfig.SteamMasterClanID == 0)) {
				return;
			}

			SteamFriends.JoinChat(BotConfig.SteamMasterClanID);
		}

		private static async Task LimitGiftsRequestsAsync() {
			if (Program.GlobalConfig.GiftsLimiterDelay == 0) {
				return;
			}

			await GiftsSemaphore.WaitAsync().ConfigureAwait(false);
			Utilities.InBackground(
				async () => {
					await Task.Delay(Program.GlobalConfig.GiftsLimiterDelay * 1000).ConfigureAwait(false);
					GiftsSemaphore.Release();
				}
			);
		}

		private static async Task LimitLoginRequestsAsync() {
			if (Program.GlobalConfig.LoginLimiterDelay == 0) {
				return;
			}

			await LoginSemaphore.WaitAsync().ConfigureAwait(false);
			Utilities.InBackground(
				async () => {
					await Task.Delay(Program.GlobalConfig.LoginLimiterDelay * 1000).ConfigureAwait(false);
					LoginSemaphore.Release();
				}
			);
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

			if ((callback.ChatMsgType != EChatEntryType.ChatMsg) || string.IsNullOrWhiteSpace(callback.Message)) {
				return;
			}

			if ((callback.ChatRoomID == null) || (callback.ChatterID == null)) {
				ArchiLogger.LogNullError(nameof(callback.ChatRoomID) + " || " + nameof(callback.ChatterID));
				return;
			}

			ArchiLogger.LogGenericTrace(callback.ChatRoomID.ConvertToUInt64() + "/" + callback.ChatterID.ConvertToUInt64() + ": " + callback.Message);

			if (!IsAllowedToExecuteCommands(callback.ChatterID)) {
				return;
			}

			await HandleMessage(callback.ChatRoomID, callback.ChatterID, callback.Message).ConfigureAwait(false);
		}

		private async void OnConnected(SteamClient.ConnectedCallback callback) {
			if (callback == null) {
				ArchiLogger.LogNullError(nameof(callback));
				return;
			}

			HeartBeatFailures = 0;
			ReconnectOnUserInitiated = false;
			StopConnectionFailureTimer();

			ArchiLogger.LogGenericInfo(Strings.BotConnected);

			if (!KeepRunning) {
				ArchiLogger.LogGenericInfo(Strings.BotDisconnecting);
				Disconnect();
				return;
			}

			byte[] sentryFileHash = null;

			if (File.Exists(SentryFilePath)) {
				try {
					byte[] sentryFileContent = await RuntimeCompatibility.File.ReadAllBytesAsync(SentryFilePath).ConfigureAwait(false);
					sentryFileHash = SteamKit2.CryptoHelper.SHAHash(sentryFileContent);
				} catch (Exception e) {
					ArchiLogger.LogGenericException(e);

					try {
						File.Delete(SentryFilePath);
					} catch {
						// Ignored, we can only try to delete faulted file at best
					}
				}
			}

			string loginKey = null;

			if (BotConfig.UseLoginKeys) {
				loginKey = BotDatabase.LoginKey;

				// Decrypt login key if needed
				if (!string.IsNullOrEmpty(loginKey) && (loginKey.Length > 19) && (BotConfig.PasswordFormat != CryptoHelper.ECryptoMethod.PlainText)) {
					loginKey = CryptoHelper.Decrypt(BotConfig.PasswordFormat, loginKey);
				}
			} else {
				// If we're not using login keys, ensure we don't have any saved
				await BotDatabase.SetLoginKey().ConfigureAwait(false);
			}

			if (!InitLoginAndPassword(string.IsNullOrEmpty(loginKey))) {
				Stop();
				return;
			}

			// Steam login, password - ASCII characters only, can contain spaces

			Regex regex = new Regex(@"[^\u0000-\u007F]+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

			string username = regex.Replace(BotConfig.SteamLogin, "");

			string password = BotConfig.SteamPassword;
			if (!string.IsNullOrEmpty(password)) {
				password = regex.Replace(password, "");
			}

			ArchiLogger.LogGenericInfo(Strings.BotLoggingIn);

			if (string.IsNullOrEmpty(TwoFactorCode) && HasMobileAuthenticator) {
				// In this case, we can also use ASF 2FA for providing 2FA token, even if it's not required
				TwoFactorCode = await BotDatabase.MobileAuthenticator.GenerateToken().ConfigureAwait(false);
			}

			InitConnectionFailureTimer();

			SteamUser.LogOn(
				new SteamUser.LogOnDetails {
					AuthCode = AuthCode,
					CellID = Program.GlobalDatabase.CellID,
					LoginID = LoginID,
					LoginKey = loginKey,
					Password = password,
					SentryFileHash = sentryFileHash,
					ShouldRememberPassword = BotConfig.UseLoginKeys,
					TwoFactorCode = TwoFactorCode,
					Username = username
				}
			);
		}

		private async void OnDisconnected(SteamClient.DisconnectedCallback callback) {
			if (callback == null) {
				ArchiLogger.LogNullError(nameof(callback));
				return;
			}

			EResult lastLogOnResult = LastLogOnResult;
			LastLogOnResult = EResult.Invalid;
			ItemsCount = TradesCount = HeartBeatFailures = 0;
			StopConnectionFailureTimer();
			StopPlayingWasBlockedTimer();

			ArchiLogger.LogGenericInfo(Strings.BotDisconnected);

			ArchiWebHandler.OnDisconnected();
			CardsFarmer.OnDisconnected();
			Trading.OnDisconnected();

			FirstTradeSent = false;
			HandledGifts.Clear();

			// If we initiated disconnect, do not attempt to reconnect
			if (callback.UserInitiated && !ReconnectOnUserInitiated) {
				return;
			}

			switch (lastLogOnResult) {
				case EResult.AccountDisabled:
					// Do not attempt to reconnect, those failures are permanent
					return;
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

					await BotDatabase.SetLoginKey().ConfigureAwait(false);
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
			}

			if (!KeepRunning || SteamClient.IsConnected) {
				return;
			}

			ArchiLogger.LogGenericInfo(Strings.BotReconnecting);
			await Connect().ConfigureAwait(false);
		}

		private async void OnFriendMsg(SteamFriends.FriendMsgCallback callback) {
			if (callback?.Sender == null) {
				ArchiLogger.LogNullError(nameof(callback) + " || " + nameof(callback.Sender));
				return;
			}

			if ((callback.EntryType != EChatEntryType.ChatMsg) || string.IsNullOrWhiteSpace(callback.Message)) {
				return;
			}

			ArchiLogger.LogGenericTrace(callback.Sender.ConvertToUInt64() + ": " + callback.Message);

			// We should never ever get friend message in the first place when we're using FarmOffline
			// But due to Valve's fuckups, everything is possible, and this case must be checked too
			// Additionally, we might even make use of that if user didn't enable HandleOfflineMessages
			if (!IsAllowedToExecuteCommands(callback.Sender) || ((BotConfig.OnlineStatus == EPersonaState.Offline) && BotConfig.HandleOfflineMessages)) {
				return;
			}

			await HandleMessage(callback.Sender, callback.Sender, callback.Message).ConfigureAwait(false);
		}

		private void OnFriendMsgEcho(SteamFriends.FriendMsgEchoCallback callback) {
			if (callback?.Recipient == null) {
				ArchiLogger.LogNullError(nameof(callback) + " || " + nameof(callback.Recipient));
				return;
			}

			if ((callback.EntryType != EChatEntryType.ChatMsg) || string.IsNullOrWhiteSpace(callback.Message)) {
				return;
			}

			ArchiLogger.LogGenericTrace(callback.Recipient.ConvertToUInt64() + ": " + callback.Message);
		}

		private async void OnFriendMsgHistory(SteamFriends.FriendMsgHistoryCallback callback) {
			if ((callback?.Messages == null) || (callback.SteamID == null)) {
				ArchiLogger.LogNullError(nameof(callback) + " || " + nameof(callback.Messages) + " || " + nameof(callback.SteamID));
				return;
			}

			if (callback.Messages.Count == 0) {
				return;
			}

			bool isAllowedToExecuteCommands = IsAllowedToExecuteCommands(callback.SteamID);

			foreach (SteamFriends.FriendMsgHistoryCallback.FriendMessage message in callback.Messages.Where(message => !string.IsNullOrEmpty(message.Message) && message.Unread)) {
				ArchiLogger.LogGenericTrace(message.SteamID.ConvertToUInt64() + ": " + message.Message);

				if (!isAllowedToExecuteCommands || (DateTime.UtcNow.Subtract(message.Timestamp).TotalHours > 1)) {
					continue;
				}

				await HandleMessage(message.SteamID, message.SteamID, message.Message).ConfigureAwait(false);
			}
		}

		private void OnFriendsList(SteamFriends.FriendsListCallback callback) {
			if (callback?.FriendList == null) {
				ArchiLogger.LogNullError(nameof(callback) + " || " + nameof(callback.FriendList));
				return;
			}

			foreach (SteamFriends.FriendsListCallback.Friend friend in callback.FriendList.Where(friend => friend.Relationship == EFriendRelationship.RequestRecipient)) {
				switch (friend.SteamID.AccountType) {
					case EAccountType.Clan when IsMasterClanID(friend.SteamID):
						ArchiHandler.AcknowledgeClanInvite(friend.SteamID, true);
						break;
					case EAccountType.Clan:
						if (BotConfig.BotBehaviour.HasFlag(BotConfig.EBotBehaviour.RejectInvalidGroupInvites)) {
							ArchiHandler.AcknowledgeClanInvite(friend.SteamID, false);
						}

						break;
					default:
						if (IsFamilySharing(friend.SteamID)) {
							SteamFriends.AddFriend(friend.SteamID);
						} else if (BotConfig.BotBehaviour.HasFlag(BotConfig.EBotBehaviour.RejectInvalidFriendInvites)) {
							SteamFriends.RemoveFriend(friend.SteamID);
						}

						break;
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

			// Return early if this update doesn't bring anything new
			if (callback.LicenseList.Count == OwnedPackageIDs.Count) {
				if (callback.LicenseList.All(license => OwnedPackageIDs.ContainsKey(license.PackageID))) {
					// Wait 2 seconds for eventual PlayingSessionStateCallback or SharedLibraryLockStatusCallback
					await Task.Delay(2000).ConfigureAwait(false);

					if (!await CardsFarmer.Resume(false).ConfigureAwait(false)) {
						await ResetGamesPlayed().ConfigureAwait(false);
					}

					return;
				}
			}

			OwnedPackageIDs.Clear();

			bool refreshData = !BotConfig.IdleRefundableGames || BotConfig.FarmingOrders.Contains(BotConfig.EFarmingOrder.RedeemDateTimesAscending) || BotConfig.FarmingOrders.Contains(BotConfig.EFarmingOrder.RedeemDateTimesDescending);
			Dictionary<uint, uint> packagesToRefresh = new Dictionary<uint, uint>();

			foreach (SteamApps.LicenseListCallback.License license in callback.LicenseList.Where(license => license.PackageID != 0)) {
				OwnedPackageIDs[license.PackageID] = (license.PaymentMethod, license.TimeCreated);

				if (!refreshData) {
					continue;
				}

				if (!Program.GlobalDatabase.PackagesData.TryGetValue(license.PackageID, out (uint ChangeNumber, HashSet<uint> _) packageData) || (packageData.ChangeNumber < license.LastChangeNumber)) {
					packagesToRefresh[license.PackageID] = (uint) license.LastChangeNumber;
				}
			}

			if (packagesToRefresh.Count > 0) {
				ArchiLogger.LogGenericInfo(Strings.BotRefreshingPackagesData);
				await Program.GlobalDatabase.RefreshPackages(this, packagesToRefresh).ConfigureAwait(false);
				ArchiLogger.LogGenericInfo(Strings.Done);
			}

			// Wait a second for eventual PlayingSessionStateCallback or SharedLibraryLockStatusCallback
			await Task.Delay(1000).ConfigureAwait(false);

			if (CardsFarmer.Paused) {
				await ResetGamesPlayed().ConfigureAwait(false);
			}

			await CardsFarmer.OnNewGameAdded().ConfigureAwait(false);
		}

		private void OnLoggedOff(SteamUser.LoggedOffCallback callback) {
			if (callback == null) {
				ArchiLogger.LogNullError(nameof(callback));
				return;
			}

			LastLogOnResult = callback.Result;

			ArchiLogger.LogGenericInfo(string.Format(Strings.BotLoggedOff, callback.Result));

			switch (callback.Result) {
				case EResult.LoggedInElsewhere:
					// This result directly indicates that playing was blocked when we got (forcefully) disconnected
					PlayingWasBlocked = true;
					break;
				case EResult.LogonSessionReplaced:
					DateTime now = DateTime.UtcNow;

					if (now.Subtract(LastLogonSessionReplaced).TotalHours < 1) {
						ArchiLogger.LogGenericError(Strings.BotLogonSessionReplaced);
						Stop();
						return;
					}

					LastLogonSessionReplaced = now;
					break;
			}

			ReconnectOnUserInitiated = true;
			SteamClient.Disconnect();
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
				case EResult.AccountDisabled:
					// Those failures are permanent, we should Stop() the bot if any of those happen
					ArchiLogger.LogGenericWarning(string.Format(Strings.BotUnableToLogin, callback.Result, callback.ExtendedResult));
					Stop();
					break;
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
					AccountFlags = callback.AccountFlags;
					CachedSteamID = callback.ClientSteamID;

					ArchiLogger.LogGenericInfo(string.Format(Strings.BotLoggedOn, CachedSteamID + (!string.IsNullOrEmpty(callback.VanityURL) ? "/" + callback.VanityURL : "")));

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

					if (IsAccountLimited) {
						ArchiLogger.LogGenericWarning(Strings.BotAccountLimited);
					}

					if (IsAccountLocked) {
						ArchiLogger.LogGenericWarning(Strings.BotAccountLocked);
					}

					if ((callback.CellID != 0) && (callback.CellID != Program.GlobalDatabase.CellID)) {
						await Program.GlobalDatabase.SetCellID(callback.CellID).ConfigureAwait(false);
					}

					if (!HasMobileAuthenticator) {
						// Support and convert 2FA files
						string maFilePath = Path.Combine(SharedInfo.ConfigDirectory, callback.ClientSteamID.ConvertToUInt64() + ".maFile");
						if (File.Exists(maFilePath)) {
							await ImportAuthenticator(maFilePath).ConfigureAwait(false);
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

					ArchiWebHandler.OnVanityURLChanged(callback.VanityURL);

					if (!await ArchiWebHandler.Init(callback.ClientSteamID, SteamClient.Universe, callback.WebAPIUserNonce, BotConfig.SteamParentalPIN).ConfigureAwait(false)) {
						if (!await RefreshSession().ConfigureAwait(false)) {
							break;
						}
					}

					if ((GamesRedeemerInBackgroundTimer == null) && BotDatabase.HasGamesToRedeemInBackground) {
						Utilities.InBackground(RedeemGamesInBackground);
					}

					ArchiHandler.RequestItemAnnouncements();

					// Sometimes Steam won't send us our own PersonaStateCallback, so request it explicitly
					RequestPersonaStateUpdate();

					// This will pre-cache API key for eventual further usage
					Utilities.InBackground(ArchiWebHandler.HasValidApiKey);

					Utilities.InBackground(InitializeFamilySharing);

					if (Statistics != null) {
						Utilities.InBackground(Statistics.OnLoggedOn);
					}

					if (BotConfig.SteamMasterClanID != 0) {
						Utilities.InBackground(
							async () => {
								await ArchiWebHandler.JoinGroup(BotConfig.SteamMasterClanID).ConfigureAwait(false);
								JoinMasterChat();
							}
						);
					}

					if (BotConfig.OnlineStatus != EPersonaState.Offline) {
						SteamFriends.SetPersonaState(BotConfig.OnlineStatus);
					}

					break;
				case EResult.InvalidPassword:
				case EResult.NoConnection:
				case EResult.PasswordRequiredToKickSession: // Not sure about this one, it seems to be just generic "try again"? #694
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
				default:
					// Unexpected result, shutdown immediately
					ArchiLogger.LogGenericError(string.Format(Strings.BotUnableToLogin, callback.Result, callback.ExtendedResult));
					Stop();
					break;
			}
		}

		private async void OnLoginKey(SteamUser.LoginKeyCallback callback) {
			if (string.IsNullOrEmpty(callback?.LoginKey)) {
				ArchiLogger.LogNullError(nameof(callback) + " || " + nameof(callback.LoginKey));
				return;
			}

			if (!BotConfig.UseLoginKeys) {
				return;
			}

			string loginKey = callback.LoginKey;
			if (BotConfig.PasswordFormat != CryptoHelper.ECryptoMethod.PlainText) {
				loginKey = CryptoHelper.Encrypt(BotConfig.PasswordFormat, loginKey);
			}

			await BotDatabase.SetLoginKey(loginKey).ConfigureAwait(false);
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
				using (FileStream fileStream = File.Open(SentryFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite)) {
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

				try {
					File.Delete(SentryFilePath);
				} catch {
					// Ignored, we can only try to delete faulted file at best
				}

				return;
			}

			// Inform the steam servers that we're accepting this sentry file
			SteamUser.SendMachineAuthResponse(
				new SteamUser.MachineAuthDetails {
					JobID = callback.JobID,
					FileName = callback.FileName,
					BytesWritten = callback.BytesToWrite,
					FileSize = fileSize,
					Offset = callback.Offset,
					Result = EResult.OK,
					LastError = 0,
					OneTimePassword = callback.OneTimePassword,
					SentryFileHash = sentryHash
				}
			);
		}

		private void OnOfflineMessage(ArchiHandler.OfflineMessageCallback callback) {
			if (callback?.SteamIDs == null) {
				ArchiLogger.LogNullError(nameof(callback) + " || " + nameof(callback.SteamIDs));
				return;
			}

			// Ignore event if we don't have any messages considering any of our permitted users
			// This allows us to skip marking offline messages as read when there is no need to ask for them
			if ((callback.OfflineMessagesCount == 0) || (callback.SteamIDs.Count == 0) || !BotConfig.HandleOfflineMessages || !callback.SteamIDs.Any(IsAllowedToExecuteCommands)) {
				return;
			}

			SteamFriends.RequestOfflineMessages();
		}

		private async void OnPersonaState(SteamFriends.PersonaStateCallback callback) {
			if (callback == null) {
				ArchiLogger.LogNullError(nameof(callback));
				return;
			}

			if (callback.FriendID == CachedSteamID) {
				string avatarHash = null;

				if ((callback.AvatarHash != null) && (callback.AvatarHash.Length > 0) && callback.AvatarHash.Any(singleByte => singleByte != 0)) {
					avatarHash = BitConverter.ToString(callback.AvatarHash).Replace("-", "").ToLowerInvariant();

					if (string.IsNullOrEmpty(avatarHash) || avatarHash.All(singleChar => singleChar == '0')) {
						avatarHash = null;
					}
				}

				AvatarHash = avatarHash;

				if (Statistics != null) {
					Utilities.InBackground(() => Statistics.OnPersonaState(callback.Name, avatarHash));
				}
			} else if ((callback.FriendID == LibraryLockedBySteamID) && (callback.GameID == 0)) {
				LibraryLockedBySteamID = 0;
				await CheckOccupationStatus().ConfigureAwait(false);
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

		private async void OnSharedLibraryLockStatus(ArchiHandler.SharedLibraryLockStatusCallback callback) {
			if (callback == null) {
				ArchiLogger.LogNullError(nameof(callback));
				return;
			}

			// Ignore no status updates
			if (LibraryLockedBySteamID == 0) {
				if ((callback.LibraryLockedBySteamID == 0) || (callback.LibraryLockedBySteamID == CachedSteamID)) {
					return;
				}

				LibraryLockedBySteamID = callback.LibraryLockedBySteamID;
			} else {
				if ((callback.LibraryLockedBySteamID != 0) && (callback.LibraryLockedBySteamID != CachedSteamID)) {
					return;
				}

				if (SteamFriends.GetFriendGamePlayed(LibraryLockedBySteamID) != 0) {
					return;
				}

				LibraryLockedBySteamID = 0;
			}

			await CheckOccupationStatus().ConfigureAwait(false);
		}

		private void OnUserNotifications(ArchiHandler.UserNotificationsCallback callback) {
			if (callback == null) {
				ArchiLogger.LogNullError(nameof(callback));
				return;
			}

			if ((callback.Notifications == null) || (callback.Notifications.Count == 0)) {
				return;
			}

			foreach (KeyValuePair<ArchiHandler.UserNotificationsCallback.EUserNotification, uint> notification in callback.Notifications) {
				switch (notification.Key) {
					case ArchiHandler.UserNotificationsCallback.EUserNotification.Items:
						bool newItems = notification.Value > ItemsCount;
						ItemsCount = notification.Value;

						if (newItems) {
							ArchiLogger.LogGenericTrace(nameof(ArchiHandler.UserNotificationsCallback.EUserNotification.Items));
							Utilities.InBackground(CardsFarmer.OnNewItemsNotification);

							if (BotConfig.BotBehaviour.HasFlag(BotConfig.EBotBehaviour.DismissInventoryNotifications)) {
								Utilities.InBackground(ArchiWebHandler.MarkInventory);
							}
						}

						break;
					case ArchiHandler.UserNotificationsCallback.EUserNotification.Trading:
						bool newTrades = notification.Value > TradesCount;
						TradesCount = notification.Value;

						if (newTrades) {
							ArchiLogger.LogGenericTrace(nameof(ArchiHandler.UserNotificationsCallback.EUserNotification.Trading));
							Utilities.InBackground(Trading.OnNewTrade);
						}

						break;
				}
			}
		}

		private void OnVanityURLChangedCallback(ArchiHandler.VanityURLChangedCallback callback) {
			if (callback == null) {
				ArchiLogger.LogNullError(nameof(callback));
				return;
			}

			ArchiWebHandler.OnVanityURLChanged(callback.VanityURL);
		}

		[SuppressMessage("ReSharper", "FunctionComplexityOverflow")]
		private async Task RedeemGamesInBackground() {
			if (!await GamesRedeemerInBackgroundSemaphore.WaitAsync(0).ConfigureAwait(false)) {
				return;
			}

			try {
				if (GamesRedeemerInBackgroundTimer != null) {
					GamesRedeemerInBackgroundTimer.Dispose();
					GamesRedeemerInBackgroundTimer = null;
				}

				ArchiLogger.LogGenericInfo(Strings.Starting);

				while (IsConnectedAndLoggedOn && BotDatabase.HasGamesToRedeemInBackground) {
					(string key, string name) = BotDatabase.GetGameToRedeemInBackground();
					if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(name)) {
						ArchiLogger.LogNullError(nameof(key) + " || " + nameof(name));
						break;
					}

					await LimitGiftsRequestsAsync().ConfigureAwait(false);

					ArchiHandler.PurchaseResponseCallback result = await ArchiHandler.RedeemKey(key).ConfigureAwait(false);
					if (result == null) {
						continue;
					}

					if (result.PurchaseResultDetail == EPurchaseResultDetail.CannotRedeemCodeFromClient) {
						// If it's a wallet code, we try to redeem it first, then handle the inner result as our primary one
						(EResult Result, EPurchaseResultDetail? PurchaseResult)? walletResult = await ArchiWebHandler.RedeemWalletKey(key).ConfigureAwait(false);

						if (walletResult != null) {
							result.Result = walletResult.Value.Result;
							result.PurchaseResultDetail = walletResult.Value.PurchaseResult.GetValueOrDefault(walletResult.Value.Result == EResult.OK ? EPurchaseResultDetail.NoDetail : EPurchaseResultDetail.BadActivationCode); // BadActivationCode is our smart guess in this case
						} else {
							result.Result = EResult.Timeout;
							result.PurchaseResultDetail = EPurchaseResultDetail.Timeout;
						}
					}

					ArchiLogger.LogGenericDebug(string.Format(Strings.BotRedeem, key, result.Result + "/" + result.PurchaseResultDetail));

					bool rateLimited = false;
					bool redeemed = false;

					switch (result.PurchaseResultDetail) {
						case EPurchaseResultDetail.AccountLocked:
						case EPurchaseResultDetail.AlreadyPurchased:
						case EPurchaseResultDetail.CannotRedeemCodeFromClient:
						case EPurchaseResultDetail.DoesNotOwnRequiredApp:
						case EPurchaseResultDetail.RestrictedCountry:
						case EPurchaseResultDetail.Timeout:
							break;
						case EPurchaseResultDetail.BadActivationCode:
						case EPurchaseResultDetail.DuplicateActivationCode:
						case EPurchaseResultDetail.NoDetail: // OK
							redeemed = true;
							break;
						case EPurchaseResultDetail.RateLimited:
							rateLimited = true;
							break;
						default:
							ASF.ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(result.PurchaseResultDetail), result.PurchaseResultDetail));
							break;
					}

					if (rateLimited) {
						break;
					}

					await BotDatabase.RemoveGameToRedeemInBackground(key).ConfigureAwait(false);

					string logEntry = name + DefaultBackgroundKeysRedeemerSeparator + "[" + result.PurchaseResultDetail + "]" + ((result.Items != null) && (result.Items.Count > 0) ? DefaultBackgroundKeysRedeemerSeparator + string.Join("", result.Items) : "") + DefaultBackgroundKeysRedeemerSeparator + key;

					try {
						await RuntimeCompatibility.File.AppendAllTextAsync(redeemed ? KeysToRedeemUsedFilePath : KeysToRedeemUnusedFilePath, logEntry + Environment.NewLine).ConfigureAwait(false);
					} catch (Exception e) {
						ArchiLogger.LogGenericException(e);
						ArchiLogger.LogGenericError(string.Format(Strings.Content, logEntry));
						break;
					}
				}

				if (BotDatabase.HasGamesToRedeemInBackground) {
					ArchiLogger.LogGenericInfo(string.Format(Strings.BotRateLimitExceeded, TimeSpan.FromHours(RedeemCooldownInHours).ToHumanReadable()));

					GamesRedeemerInBackgroundTimer = new Timer(
						async e => await RedeemGamesInBackground().ConfigureAwait(false),
						null,
						TimeSpan.FromHours(RedeemCooldownInHours), // Delay
						Timeout.InfiniteTimeSpan // Period
					);
				}

				ArchiLogger.LogGenericInfo(Strings.Done);
			} finally {
				GamesRedeemerInBackgroundSemaphore.Release();
			}
		}

		private async Task ResetGamesPlayed() {
			if (!IsPlayingPossible || (FamilySharingInactivityTimer != null) || CardsFarmer.NowFarming) {
				return;
			}

			await ArchiHandler.PlayGames(BotConfig.GamesPlayedWhileIdle, BotConfig.CustomGamePlayedWhileIdle).ConfigureAwait(false);
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

			bool result = await AcceptConfirmations(confirm).ConfigureAwait(false);
			return FormatBotResponse(result ? Strings.Success : Strings.WarningFailed);
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

		private async Task<string> ResponseAddLicense(ulong steamID, IReadOnlyCollection<uint> gameIDs) {
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
				await LimitGiftsRequestsAsync().ConfigureAwait(false);

				if (await ArchiWebHandler.AddFreeLicense(gameID).ConfigureAwait(false)) {
					response.Append(FormatBotResponse(string.Format(Strings.BotAddLicenseWithItems, gameID, EResult.OK, "sub/" + gameID)));
					continue;
				}

				SteamApps.FreeLicenseCallback callback;

				try {
#pragma warning disable ConfigureAwaitChecker // CAC001
					callback = await SteamApps.RequestFreeLicense(gameID);
#pragma warning restore ConfigureAwaitChecker // CAC001
				} catch (Exception e) {
					ArchiLogger.LogGenericWarningException(e);
					response.Append(FormatBotResponse(string.Format(Strings.BotAddLicense, gameID, EResult.Timeout)));
					break;
				}

				if (callback == null) {
					response.Append(FormatBotResponse(string.Format(Strings.BotAddLicense, gameID, EResult.Timeout)));
					break;
				}

				response.Append(FormatBotResponse((callback.GrantedApps.Count > 0) || (callback.GrantedPackages.Count > 0) ? string.Format(Strings.BotAddLicenseWithItems, gameID, callback.Result, string.Join(", ", callback.GrantedApps.Select(appID => "app/" + appID).Union(callback.GrantedPackages.Select(subID => "sub/" + subID)))) : string.Format(Strings.BotAddLicense, gameID, callback.Result)));
			}

			return response.Length > 0 ? response.ToString() : null;
		}

		private async Task<string> ResponseAddLicense(ulong steamID, string targetGameIDs) {
			if ((steamID == 0) || string.IsNullOrEmpty(targetGameIDs)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(targetGameIDs));
				return null;
			}

			if (!IsOperator(steamID)) {
				return null;
			}

			if (!IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			string[] gameIDs = targetGameIDs.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

			if (gameIDs.Length == 0) {
				return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(gameIDs)));
			}

			HashSet<uint> gamesToRedeem = new HashSet<uint>();

			foreach (string game in gameIDs) {
				if (!uint.TryParse(game, out uint gameID) || (gameID == 0)) {
					return FormatBotResponse(string.Format(Strings.ErrorParsingObject, nameof(gameID)));
				}

				gamesToRedeem.Add(gameID);
			}

			return await ResponseAddLicense(steamID, gamesToRedeem).ConfigureAwait(false);
		}

		private static async Task<string> ResponseAddLicense(ulong steamID, string botNames, string targetGameIDs) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(targetGameIDs)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(targetGameIDs));
				return null;
			}

			HashSet<Bot> bots = GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(bot => bot.ResponseAddLicense(steamID, targetGameIDs));
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

		private async Task<string> ResponseAdvancedLoot(ulong steamID, string targetAppID, string targetContextID) {
			if ((steamID == 0) || string.IsNullOrEmpty(targetAppID) || string.IsNullOrEmpty(targetContextID)) {
				ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(targetAppID) + " || " + nameof(targetContextID));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			if (!IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			if (!uint.TryParse(targetAppID, out uint appID) || (appID == 0)) {
				return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(appID)));
			}

			if (!byte.TryParse(targetContextID, out byte contextID) || (contextID == 0)) {
				return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(contextID)));
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

			if (targetSteamMasterID == CachedSteamID) {
				return FormatBotResponse(Strings.BotSendingTradeToYourself);
			}

			await LootingSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				HashSet<Steam.Asset> inventory = await ArchiWebHandler.GetInventory(CachedSteamID, appID, contextID, true).ConfigureAwait(false);
				if ((inventory == null) || (inventory.Count == 0)) {
					return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(inventory)));
				}

				if (!await ArchiWebHandler.MarkSentTrades().ConfigureAwait(false)) {
					return FormatBotResponse(Strings.BotLootingFailed);
				}

				if (!await ArchiWebHandler.SendTradeOffer(targetSteamMasterID, inventory, BotConfig.SteamTradeToken).ConfigureAwait(false)) {
					return FormatBotResponse(Strings.BotLootingFailed);
				}

				if (HasMobileAuthenticator) {
					// Give Steam network some time to generate confirmations
					await Task.Delay(3000).ConfigureAwait(false);
					if (!await AcceptConfirmations(true, Steam.ConfirmationDetails.EType.Trade, targetSteamMasterID).ConfigureAwait(false)) {
						return FormatBotResponse(Strings.BotLootingFailed);
					}
				}
			} finally {
				LootingSemaphore.Release();
			}

			return FormatBotResponse(Strings.BotLootingSuccess);
		}

		private static async Task<string> ResponseAdvancedLoot(ulong steamID, string botNames, string appID, string contextID) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(appID) || string.IsNullOrEmpty(contextID)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(appID) + " || " + nameof(contextID));
				return null;
			}

			HashSet<Bot> bots = GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(bot => bot.ResponseAdvancedLoot(steamID, appID, contextID));
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

		private async Task<string> ResponseAdvancedRedeem(ulong steamID, string options, string keys) {
			if ((steamID == 0) || string.IsNullOrEmpty(options) || string.IsNullOrEmpty(keys)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(options) + " || " + nameof(keys));
				return null;
			}

			if (!IsOperator(steamID)) {
				return null;
			}

			string[] flags = options.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

			if (flags.Length == 0) {
				return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(flags)));
			}

			ERedeemFlags redeemFlags = ERedeemFlags.None;

			foreach (string flag in flags) {
				switch (flag.ToUpperInvariant()) {
					case "FD":
						redeemFlags |= ERedeemFlags.ForceDistributing;
						break;
					case "FF":
						redeemFlags |= ERedeemFlags.ForceForwarding;
						break;
					case "FKMG":
						redeemFlags |= ERedeemFlags.ForceKeepMissingGames;
						break;
					case "SD":
						redeemFlags |= ERedeemFlags.SkipDistributing;
						break;
					case "SF":
						redeemFlags |= ERedeemFlags.SkipForwarding;
						break;
					case "SI":
						redeemFlags |= ERedeemFlags.SkipInitial;
						break;
					case "SKMG":
						redeemFlags |= ERedeemFlags.SkipKeepMissingGames;
						break;
					case "V":
						redeemFlags |= ERedeemFlags.Validate;
						break;
					default:
						return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, flag));
				}
			}

			return await ResponseRedeem(steamID, keys, redeemFlags).ConfigureAwait(false);
		}

		private static async Task<string> ResponseAdvancedRedeem(ulong steamID, string botNames, string options, string keys) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(options) || string.IsNullOrEmpty(keys)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(options) + " || " + nameof(keys));
				return null;
			}

			HashSet<Bot> bots = GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(bot => bot.ResponseAdvancedRedeem(steamID, options, keys));
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
			if (steamID == 0) {
				ArchiLogger.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			IReadOnlyCollection<ulong> blacklist = BotDatabase.GetBlacklistedFromTradesSteamIDs();
			return FormatBotResponse(blacklist.Count > 0 ? string.Join(", ", blacklist) : string.Format(Strings.ErrorIsEmpty, nameof(blacklist)));
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

		private async Task<string> ResponseBlacklistAdd(ulong steamID, string targetSteamIDs) {
			if ((steamID == 0) || string.IsNullOrEmpty(targetSteamIDs)) {
				ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(targetSteamIDs));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			string[] targets = targetSteamIDs.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

			if (targets.Length == 0) {
				return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(targets)));
			}

			HashSet<ulong> targetIDs = new HashSet<ulong>();

			foreach (string target in targets) {
				if (!ulong.TryParse(target, out ulong targetID) || (targetID == 0)) {
					return FormatBotResponse(string.Format(Strings.ErrorParsingObject, nameof(targetID)));
				}

				targetIDs.Add(targetID);
			}

			await BotDatabase.AddBlacklistedFromTradesSteamIDs(targetIDs).ConfigureAwait(false);
			return FormatBotResponse(Strings.Done);
		}

		private static async Task<string> ResponseBlacklistAdd(ulong steamID, string botNames, string targetSteamIDs) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(targetSteamIDs)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(targetSteamIDs));
				return null;
			}

			HashSet<Bot> bots = GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(bot => bot.ResponseBlacklistAdd(steamID, targetSteamIDs));
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

		private async Task<string> ResponseBlacklistRemove(ulong steamID, string targetSteamIDs) {
			if ((steamID == 0) || string.IsNullOrEmpty(targetSteamIDs)) {
				ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(targetSteamIDs));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			string[] targets = targetSteamIDs.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

			if (targets.Length == 0) {
				return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(targets)));
			}

			HashSet<ulong> targetIDs = new HashSet<ulong>();

			foreach (string target in targets) {
				if (!ulong.TryParse(target, out ulong targetID) || (targetID == 0)) {
					return FormatBotResponse(string.Format(Strings.ErrorParsingObject, nameof(targetID)));
				}

				targetIDs.Add(targetID);
			}

			await BotDatabase.RemoveBlacklistedFromTradesSteamIDs(targetIDs).ConfigureAwait(false);
			return FormatBotResponse(Strings.Done);
		}

		private static async Task<string> ResponseBlacklistRemove(ulong steamID, string botNames, string targetSteamIDs) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(targetSteamIDs)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(targetSteamIDs));
				return null;
			}

			HashSet<Bot> bots = GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(bot => bot.ResponseBlacklistRemove(steamID, targetSteamIDs));
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

		private static string ResponseExit(ulong steamID) {
			if (steamID == 0) {
				ASF.ArchiLogger.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsOwner(steamID)) {
				return null;
			}

			// Schedule the task after some time so user can receive response
			Utilities.InBackground(
				async () => {
					await Task.Delay(1000).ConfigureAwait(false);
					await Program.Exit().ConfigureAwait(false);
				}
			);

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

			if (CardsFarmer.NowFarming) {
				await CardsFarmer.StopFarming().ConfigureAwait(false);
			}

			Utilities.InBackground(CardsFarmer.StartFarming);
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
			if (steamID == 0) {
				ArchiLogger.LogNullError(nameof(steamID));
				return null;
			}

			return IsFamilySharing(steamID) ? FormatBotResponse("https://github.com/" + SharedInfo.GithubRepo + "/wiki/Commands") : null;
		}

		private string ResponseIdleBlacklist(ulong steamID) {
			if (steamID == 0) {
				ArchiLogger.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			IReadOnlyCollection<uint> idleBlacklist = BotDatabase.GetIdlingBlacklistedAppIDs();
			return FormatBotResponse(idleBlacklist.Count > 0 ? string.Join(", ", idleBlacklist) : string.Format(Strings.ErrorIsEmpty, nameof(idleBlacklist)));
		}

		private static async Task<string> ResponseIdleBlacklist(ulong steamID, string botNames) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames));
				return null;
			}

			HashSet<Bot> bots = GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(bot => Task.Run(() => bot.ResponseIdleBlacklist(steamID)));
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

		private async Task<string> ResponseIdleBlacklistAdd(ulong steamID, string targetAppIDs) {
			if ((steamID == 0) || string.IsNullOrEmpty(targetAppIDs)) {
				ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(targetAppIDs));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			string[] targets = targetAppIDs.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

			if (targets.Length == 0) {
				return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(targets)));
			}

			HashSet<uint> appIDs = new HashSet<uint>();

			foreach (string target in targets) {
				if (!uint.TryParse(target, out uint appID) || (appID == 0)) {
					return FormatBotResponse(string.Format(Strings.ErrorParsingObject, nameof(appID)));
				}

				appIDs.Add(appID);
			}

			await BotDatabase.AddIdlingBlacklistedAppIDs(appIDs).ConfigureAwait(false);
			return FormatBotResponse(Strings.Done);
		}

		private static async Task<string> ResponseIdleBlacklistAdd(ulong steamID, string botNames, string targetAppIDs) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(targetAppIDs)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(targetAppIDs));
				return null;
			}

			HashSet<Bot> bots = GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(bot => bot.ResponseIdleBlacklistAdd(steamID, targetAppIDs));
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

		private async Task<string> ResponseIdleBlacklistRemove(ulong steamID, string targetAppIDs) {
			if ((steamID == 0) || string.IsNullOrEmpty(targetAppIDs)) {
				ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(targetAppIDs));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			string[] targets = targetAppIDs.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

			if (targets.Length == 0) {
				return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(targets)));
			}

			HashSet<uint> appIDs = new HashSet<uint>();

			foreach (string target in targets) {
				if (!uint.TryParse(target, out uint appID) || (appID == 0)) {
					return FormatBotResponse(string.Format(Strings.ErrorParsingObject, nameof(appID)));
				}

				appIDs.Add(appID);
			}

			await BotDatabase.RemoveIdlingBlacklistedAppIDs(appIDs).ConfigureAwait(false);
			return FormatBotResponse(Strings.Done);
		}

		private static async Task<string> ResponseIdleBlacklistRemove(ulong steamID, string botNames, string targetAppIDs) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(targetAppIDs)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(targetAppIDs));
				return null;
			}

			HashSet<Bot> bots = GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(bot => bot.ResponseIdleBlacklistRemove(steamID, targetAppIDs));
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

		private string ResponseIdleQueue(ulong steamID) {
			if (steamID == 0) {
				ArchiLogger.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			IReadOnlyCollection<uint> idleQueue = BotDatabase.GetIdlingPriorityAppIDs();
			return FormatBotResponse(idleQueue.Count > 0 ? string.Join(", ", idleQueue) : string.Format(Strings.ErrorIsEmpty, nameof(idleQueue)));
		}

		private static async Task<string> ResponseIdleQueue(ulong steamID, string botNames) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames));
				return null;
			}

			HashSet<Bot> bots = GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(bot => Task.Run(() => bot.ResponseIdleQueue(steamID)));
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

		private async Task<string> ResponseIdleQueueAdd(ulong steamID, string targetAppIDs) {
			if ((steamID == 0) || string.IsNullOrEmpty(targetAppIDs)) {
				ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(targetAppIDs));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			string[] targets = targetAppIDs.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

			if (targets.Length == 0) {
				return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(targets)));
			}

			HashSet<uint> appIDs = new HashSet<uint>();

			foreach (string target in targets) {
				if (!uint.TryParse(target, out uint appID) || (appID == 0)) {
					return FormatBotResponse(string.Format(Strings.ErrorParsingObject, nameof(appID)));
				}

				appIDs.Add(appID);
			}

			await BotDatabase.AddIdlingPriorityAppIDs(appIDs).ConfigureAwait(false);
			return FormatBotResponse(Strings.Done);
		}

		private static async Task<string> ResponseIdleQueueAdd(ulong steamID, string botNames, string targetAppIDs) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(targetAppIDs)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(targetAppIDs));
				return null;
			}

			HashSet<Bot> bots = GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(bot => bot.ResponseIdleQueueAdd(steamID, targetAppIDs));
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

		private async Task<string> ResponseIdleQueueRemove(ulong steamID, string targetAppIDs) {
			if ((steamID == 0) || string.IsNullOrEmpty(targetAppIDs)) {
				ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(targetAppIDs));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			string[] targets = targetAppIDs.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

			if (targets.Length == 0) {
				return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(targets)));
			}

			HashSet<uint> appIDs = new HashSet<uint>();

			foreach (string target in targets) {
				if (!uint.TryParse(target, out uint appID) || (appID == 0)) {
					return FormatBotResponse(string.Format(Strings.ErrorParsingObject, nameof(appID)));
				}

				appIDs.Add(appID);
			}

			await BotDatabase.RemoveIdlingPriorityAppIDs(appIDs).ConfigureAwait(false);
			return FormatBotResponse(Strings.Done);
		}

		private static async Task<string> ResponseIdleQueueRemove(ulong steamID, string botNames, string targetAppIDs) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(targetAppIDs)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(targetAppIDs));
				return null;
			}

			HashSet<Bot> bots = GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(bot => bot.ResponseIdleQueueRemove(steamID, targetAppIDs));
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

			if (!Enum.TryParse(propertyName, true, out ASF.EUserInputType inputType) || (inputType == ASF.EUserInputType.Unknown) || !Enum.IsDefined(typeof(ASF.EUserInputType), inputType)) {
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

		private string ResponseLeave(ulong steamID, ulong chatID) {
			if ((steamID == 0) || (chatID == 0)) {
				ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(chatID));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			if (!IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			// Schedule the task after some time so user can receive response
			Utilities.InBackground(
				async () => {
					await Task.Delay(1000).ConfigureAwait(false);
					SteamFriends.LeaveChat(chatID);
				}
			);

			return FormatBotResponse(Strings.Done);
		}

		private static async Task<string> ResponseLeave(ulong steamID, string botNames, ulong chatID) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames) || (chatID == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(chatID));
				return null;
			}

			HashSet<Bot> bots = GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(bot => Task.Run(() => bot.ResponseLeave(steamID, chatID)));
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

			if (targetSteamMasterID == CachedSteamID) {
				return FormatBotResponse(Strings.BotSendingTradeToYourself);
			}

			lock (LootingSemaphore) {
				if (LootingScheduled) {
					return FormatBotResponse(Strings.Done);
				}

				LootingScheduled = true;
			}

			await LootingSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				lock (LootingSemaphore) {
					LootingScheduled = false;
				}

				HashSet<Steam.Asset> inventory = await ArchiWebHandler.GetInventory(CachedSteamID, tradable: true, wantedTypes: BotConfig.LootableTypes).ConfigureAwait(false);
				if ((inventory == null) || (inventory.Count == 0)) {
					return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(inventory)));
				}

				if (!await ArchiWebHandler.MarkSentTrades().ConfigureAwait(false)) {
					return FormatBotResponse(Strings.BotLootingFailed);
				}

				if (!await ArchiWebHandler.SendTradeOffer(targetSteamMasterID, inventory, BotConfig.SteamTradeToken).ConfigureAwait(false)) {
					return FormatBotResponse(Strings.BotLootingFailed);
				}

				if (HasMobileAuthenticator) {
					// Give Steam network some time to generate confirmations
					await Task.Delay(3000).ConfigureAwait(false);
					if (!await AcceptConfirmations(true, Steam.ConfirmationDetails.EType.Trade, targetSteamMasterID).ConfigureAwait(false)) {
						return FormatBotResponse(Strings.BotLootingFailed);
					}
				}
			} finally {
				LootingSemaphore.Release();
			}

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

		private async Task<string> ResponseLootByRealAppIDs(ulong steamID, string targetRealAppIDs) {
			if ((steamID == 0) || string.IsNullOrEmpty(targetRealAppIDs)) {
				ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(targetRealAppIDs));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			if (!IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			string[] appIDTexts = targetRealAppIDs.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

			if (appIDTexts.Length == 0) {
				return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(appIDTexts)));
			}

			HashSet<uint> realAppIDs = new HashSet<uint>();

			foreach (string appIDText in appIDTexts) {
				if (!uint.TryParse(appIDText, out uint appID) || (appID == 0)) {
					return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(appID)));
				}

				realAppIDs.Add(appID);
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

			if (targetSteamMasterID == CachedSteamID) {
				return FormatBotResponse(Strings.BotSendingTradeToYourself);
			}

			await LootingSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				HashSet<Steam.Asset> inventory = await ArchiWebHandler.GetInventory(CachedSteamID, tradable: true, wantedRealAppIDs: realAppIDs).ConfigureAwait(false);
				if ((inventory == null) || (inventory.Count == 0)) {
					return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(inventory)));
				}

				if (!await ArchiWebHandler.MarkSentTrades().ConfigureAwait(false)) {
					return FormatBotResponse(Strings.BotLootingFailed);
				}

				if (!await ArchiWebHandler.SendTradeOffer(targetSteamMasterID, inventory, BotConfig.SteamTradeToken).ConfigureAwait(false)) {
					return FormatBotResponse(Strings.BotLootingFailed);
				}

				if (HasMobileAuthenticator) {
					// Give Steam network some time to generate confirmations
					await Task.Delay(3000).ConfigureAwait(false);
					if (!await AcceptConfirmations(true, Steam.ConfirmationDetails.EType.Trade, targetSteamMasterID).ConfigureAwait(false)) {
						return FormatBotResponse(Strings.BotLootingFailed);
					}
				}
			} finally {
				LootingSemaphore.Release();
			}

			return FormatBotResponse(Strings.BotLootingSuccess);
		}

		private static async Task<string> ResponseLootByRealAppIDs(ulong steamID, string botNames, string realappID) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(realappID)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(realappID));
				return null;
			}

			HashSet<Bot> bots = GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(bot => bot.ResponseLootByRealAppIDs(steamID, realappID));
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

		private string ResponseNickname(ulong steamID, string nickname) {
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

			SteamFriends.SetPersonaName(nickname);
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

			IEnumerable<Task<string>> tasks = bots.Select(bot => Task.Run(() => bot.ResponseNickname(steamID, nickname)));
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

		private async Task<(string Response, HashSet<uint> OwnedGameIDs)> ResponseOwns(ulong steamID, string query) {
			if ((steamID == 0) || string.IsNullOrEmpty(query)) {
				ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(query));
				return (null, null);
			}

			if (!IsOperator(steamID)) {
				return (null, null);
			}

			if (!IsConnectedAndLoggedOn) {
				return (FormatBotResponse(Strings.BotNotConnected), null);
			}

			await LimitGiftsRequestsAsync().ConfigureAwait(false);

			Dictionary<uint, string> ownedGames = await ArchiWebHandler.HasValidApiKey().ConfigureAwait(false) ? await ArchiWebHandler.GetOwnedGames(CachedSteamID).ConfigureAwait(false) : await ArchiWebHandler.GetMyOwnedGames().ConfigureAwait(false);
			if ((ownedGames == null) || (ownedGames.Count == 0)) {
				return (FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(ownedGames))), null);
			}

			StringBuilder response = new StringBuilder();
			HashSet<uint> ownedGameIDs = new HashSet<uint>();

			if (query.Equals("*")) {
				foreach (KeyValuePair<uint, string> ownedGame in ownedGames) {
					ownedGameIDs.Add(ownedGame.Key);
					response.Append(FormatBotResponse(string.Format(Strings.BotOwnedAlreadyWithName, ownedGame.Key, ownedGame.Value)));
				}
			} else {
				string[] games = query.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

				if (games.Length == 0) {
					return (FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(games))), null);
				}

				foreach (string game in games) {
					// Check if this is gameID
					if (uint.TryParse(game, out uint gameID) && (gameID != 0)) {
						if (OwnedPackageIDs.ContainsKey(gameID)) {
							ownedGameIDs.Add(gameID);
							response.Append(FormatBotResponse(string.Format(Strings.BotOwnedAlready, gameID)));
							continue;
						}

						if (ownedGames.TryGetValue(gameID, out string ownedName)) {
							ownedGameIDs.Add(gameID);
							response.Append(FormatBotResponse(string.Format(Strings.BotOwnedAlreadyWithName, gameID, ownedName)));
						} else {
							response.Append(FormatBotResponse(string.Format(Strings.BotNotOwnedYet, gameID)));
						}

						continue;
					}

					// This is a string, so check our entire library
					foreach (KeyValuePair<uint, string> ownedGame in ownedGames.Where(ownedGame => ownedGame.Value.IndexOf(game, StringComparison.OrdinalIgnoreCase) >= 0)) {
						ownedGameIDs.Add(ownedGame.Key);
						response.Append(FormatBotResponse(string.Format(Strings.BotOwnedAlreadyWithName, ownedGame.Key, ownedGame.Value)));
					}
				}
			}

			return (response.Length > 0 ? response.ToString() : FormatBotResponse(string.Format(Strings.BotNotOwnedYet, query)), ownedGameIDs);
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

			IEnumerable<Task<(string Response, HashSet<uint> OwnedGameIDs)>> tasks = bots.Select(bot => bot.ResponseOwns(steamID, query));
			ICollection<(string Response, HashSet<uint> OwnedGameIDs)> results;

			switch (Program.GlobalConfig.OptimizationMode) {
				case GlobalConfig.EOptimizationMode.MinMemoryUsage:
					results = new List<(string Response, HashSet<uint> OwnedGameIDs)>(bots.Count);
					foreach (Task<(string Response, HashSet<uint> OwnedGameIDs)> task in tasks) {
						results.Add(await task.ConfigureAwait(false));
					}

					break;
				default:
					results = await Task.WhenAll(tasks).ConfigureAwait(false);
					break;
			}

			List<(string Response, HashSet<uint> OwnedGameIDs)> validResults = new List<(string Response, HashSet<uint> OwnedGameIDs)>(results.Where(result => !string.IsNullOrEmpty(result.Response)));
			if (validResults.Count == 0) {
				return null;
			}

			Dictionary<uint, ushort> ownedGameCounts = new Dictionary<uint, ushort>();
			foreach (uint gameID in validResults.Where(validResult => (validResult.OwnedGameIDs != null) && (validResult.OwnedGameIDs.Count > 0)).SelectMany(validResult => validResult.OwnedGameIDs)) {
				ownedGameCounts[gameID] = ownedGameCounts.TryGetValue(gameID, out ushort count) ? ++count : (ushort) 1;
			}

			IEnumerable<string> extraResponses = ownedGameCounts.Select(kv => FormatStaticResponse(string.Format(Strings.BotOwnsOverviewPerGame, kv.Value, validResults.Count, kv.Key)));
			return string.Join("", validResults.Select(result => result.Response).Concat(extraResponses));
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
				return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(BotConfig.SteamPassword)));
			}

			string response = FormatBotResponse(string.Format(Strings.BotEncryptedPassword, CryptoHelper.ECryptoMethod.AES, CryptoHelper.Encrypt(CryptoHelper.ECryptoMethod.AES, BotConfig.SteamPassword))) + FormatBotResponse(string.Format(Strings.BotEncryptedPassword, CryptoHelper.ECryptoMethod.ProtectedDataForCurrentUser, CryptoHelper.Encrypt(CryptoHelper.ECryptoMethod.ProtectedDataForCurrentUser, BotConfig.SteamPassword)));
			return response;
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

		private async Task<string> ResponsePause(ulong steamID, bool sticky, string timeout = null) {
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

			ushort resumeInSeconds = 0;

			if (sticky && !string.IsNullOrEmpty(timeout)) {
				if (!ushort.TryParse(timeout, out resumeInSeconds) || (resumeInSeconds == 0)) {
					return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(timeout)));
				}
			}

			await CardsFarmer.Pause(sticky).ConfigureAwait(false);

			if (!sticky && (BotConfig.GamesPlayedWhileIdle.Count > 0)) {
				// We want to let family sharing users access our library, and in this case we must also stop GamesPlayedWhileIdle
				// We add extra delay because OnFarmingStopped() also executes PlayGames()
				// Despite of proper order on our end, Steam network might not respect it
				await Task.Delay(CallbackSleep).ConfigureAwait(false);
				await ArchiHandler.PlayGames(Enumerable.Empty<uint>(), BotConfig.CustomGamePlayedWhileIdle).ConfigureAwait(false);
			}

			if (resumeInSeconds > 0) {
				if (CardsFarmerResumeTimer != null) {
					CardsFarmerResumeTimer.Dispose();
					CardsFarmerResumeTimer = null;
				}

				CardsFarmerResumeTimer = new Timer(
					e => ResponseResume(steamID),
					null,
					TimeSpan.FromSeconds(resumeInSeconds), // Delay
					Timeout.InfiniteTimeSpan // Period
				);
			}

			if (IsOperator(steamID)) {
				return FormatBotResponse(Strings.BotAutomaticIdlingNowPaused);
			}

			StartFamilySharingInactivityTimer();
			return FormatBotResponse(string.Format(Strings.BotAutomaticIdlingPausedWithCountdown, TimeSpan.FromMinutes(FamilySharingInactivityMinutes).ToHumanReadable()));
		}

		private static async Task<string> ResponsePause(ulong steamID, string botNames, bool sticky, string timeout = null) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames));
				return null;
			}

			HashSet<Bot> bots = GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(bot => bot.ResponsePause(steamID, sticky, timeout));
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

		private async Task<string> ResponsePlay(ulong steamID, IEnumerable<uint> gameIDs, string gameName = null) {
			if ((steamID == 0) || (gameIDs == null)) {
				ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(gameIDs));
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

			await ArchiHandler.PlayGames(gameIDs, gameName).ConfigureAwait(false);
			return FormatBotResponse(Strings.Done);
		}

		private async Task<string> ResponsePlay(ulong steamID, string targetGameIDs) {
			if ((steamID == 0) || string.IsNullOrEmpty(targetGameIDs)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(targetGameIDs));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			if (!IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			string[] games = targetGameIDs.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

			if (games.Length == 0) {
				return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(games)));
			}

			HashSet<uint> gamesToPlay = new HashSet<uint>();
			StringBuilder gameName = new StringBuilder();

			foreach (string game in games) {
				if (!uint.TryParse(game, out uint gameID) || (gameID == 0)) {
					gameName.Append((gameName.Length > 0 ? " " : "") + game);
					continue;
				}

				if (gamesToPlay.Count >= ArchiHandler.MaxGamesPlayedConcurrently) {
					continue;
				}

				gamesToPlay.Add(gameID);
			}

			return await ResponsePlay(steamID, gamesToPlay, gameName.ToString()).ConfigureAwait(false);
		}

		private static async Task<string> ResponsePlay(ulong steamID, string botNames, string targetGameIDs) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(targetGameIDs)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(targetGameIDs));
				return null;
			}

			HashSet<Bot> bots = GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(bot => bot.ResponsePlay(steamID, targetGameIDs));
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

		private async Task<string> ResponsePrivacy(ulong steamID, string privacySettingsText) {
			if ((steamID == 0) || string.IsNullOrEmpty(privacySettingsText)) {
				ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(privacySettingsText));
				return null;
			}

			if (!IsMaster(steamID)) {
				return null;
			}

			if (!IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			string[] privacySettingsArgs = privacySettingsText.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

			if (privacySettingsArgs.Length == 0) {
				return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(privacySettingsArgs)));
			}

			// There are only 6 privacy settings
			if (privacySettingsArgs.Length > 6) {
				return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(privacySettingsArgs)));
			}

			Steam.UserPrivacy.PrivacySettings.EPrivacySetting profile = Steam.UserPrivacy.PrivacySettings.EPrivacySetting.Private;
			Steam.UserPrivacy.PrivacySettings.EPrivacySetting ownedGames = Steam.UserPrivacy.PrivacySettings.EPrivacySetting.Private;
			Steam.UserPrivacy.PrivacySettings.EPrivacySetting playtime = Steam.UserPrivacy.PrivacySettings.EPrivacySetting.Private;
			Steam.UserPrivacy.PrivacySettings.EPrivacySetting inventory = Steam.UserPrivacy.PrivacySettings.EPrivacySetting.Private;
			Steam.UserPrivacy.PrivacySettings.EPrivacySetting inventoryGifts = Steam.UserPrivacy.PrivacySettings.EPrivacySetting.Private;
			Steam.UserPrivacy.ECommentPermission comments = Steam.UserPrivacy.ECommentPermission.Private;

			// Converting digits to enum
			for (byte index = 0; index < privacySettingsArgs.Length; index++) {
				if (!Enum.TryParse(privacySettingsArgs[index], true, out Steam.UserPrivacy.PrivacySettings.EPrivacySetting privacySetting) || (privacySetting == Steam.UserPrivacy.PrivacySettings.EPrivacySetting.Unknown) || !Enum.IsDefined(typeof(Steam.UserPrivacy.PrivacySettings.EPrivacySetting), privacySetting)) {
					return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(privacySettingsArgs)));
				}

				// Child setting can't be less restrictive than its parent
				switch (index) {
					case 0: // Profile
						profile = privacySetting;
						break;
					case 1: // OwnedGames, child of Profile
						if (profile < privacySetting) {
							return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(ownedGames)));
						}

						ownedGames = privacySetting;
						break;
					case 2: // Playtime, child of OwnedGames
						if (ownedGames < privacySetting) {
							return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(playtime)));
						}

						playtime = privacySetting;
						break;
					case 3: // Inventory, child of Profile
						if (profile < privacySetting) {
							return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(inventory)));
						}

						inventory = privacySetting;
						break;
					case 4: // InventoryGifts, child of Inventory
						if (inventory < privacySetting) {
							return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(inventoryGifts)));
						}

						inventoryGifts = privacySetting;
						break;
					case 5: // Comments, child of Profile
						if (profile < privacySetting) {
							return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(comments)));
						}

						// Comments use different numbers than everything else, but we want to have this command consistent for end-user, so we'll map them
						switch (privacySetting) {
							case Steam.UserPrivacy.PrivacySettings.EPrivacySetting.FriendsOnly:
								comments = Steam.UserPrivacy.ECommentPermission.FriendsOnly;
								break;
							case Steam.UserPrivacy.PrivacySettings.EPrivacySetting.Private:
								comments = Steam.UserPrivacy.ECommentPermission.Private;
								break;
							case Steam.UserPrivacy.PrivacySettings.EPrivacySetting.Public:
								comments = Steam.UserPrivacy.ECommentPermission.Public;
								break;
							default:
								ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(privacySetting), privacySetting));
								return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(privacySetting)));
						}

						break;
					default:
						ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(index), index));
						return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(index)));
				}
			}

			Steam.UserPrivacy userPrivacy = new Steam.UserPrivacy(new Steam.UserPrivacy.PrivacySettings(profile, ownedGames, playtime, inventory, inventoryGifts), comments);
			return FormatBotResponse(await ArchiWebHandler.ChangePrivacySettings(userPrivacy).ConfigureAwait(false) ? Strings.Success : Strings.WarningFailed);
		}

		private async Task<string> ResponsePrivacy(ulong steamID, string botNames, string privacySettingsText) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(privacySettingsText)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(privacySettingsText));
				return null;
			}

			HashSet<Bot> bots = GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(bot => bot.ResponsePrivacy(steamID, privacySettingsText));
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
		private async Task<string> ResponseRedeem(ulong steamID, string keysText, ERedeemFlags redeemFlags = ERedeemFlags.None) {
			if ((steamID == 0) || string.IsNullOrEmpty(keysText)) {
				ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(keysText));
				return null;
			}

			if (!IsOperator(steamID)) {
				return null;
			}

			if (!IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			string[] keys = keysText.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

			if (keys.Length == 0) {
				return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(keys)));
			}

			bool forward = !redeemFlags.HasFlag(ERedeemFlags.SkipForwarding) && (redeemFlags.HasFlag(ERedeemFlags.ForceForwarding) || BotConfig.RedeemingPreferences.HasFlag(BotConfig.ERedeemingPreferences.Forwarding));
			bool distribute = !redeemFlags.HasFlag(ERedeemFlags.SkipDistributing) && (redeemFlags.HasFlag(ERedeemFlags.ForceDistributing) || BotConfig.RedeemingPreferences.HasFlag(BotConfig.ERedeemingPreferences.Distributing));
			bool keepMissingGames = !redeemFlags.HasFlag(ERedeemFlags.SkipKeepMissingGames) && (redeemFlags.HasFlag(ERedeemFlags.ForceKeepMissingGames) || BotConfig.RedeemingPreferences.HasFlag(BotConfig.ERedeemingPreferences.KeepMissingGames));

			HashSet<string> pendingKeys = keys.ToHashSet();
			HashSet<string> unusedKeys = pendingKeys.ToHashSet();

			StringBuilder response = new StringBuilder();

			using (HashSet<string>.Enumerator keysEnumerator = pendingKeys.GetEnumerator()) {
				HashSet<Bot> rateLimitedBots = new HashSet<Bot>();
				string key = keysEnumerator.MoveNext() ? keysEnumerator.Current : null; // Initial key

				while (!string.IsNullOrEmpty(key)) {
					string startingKey = key;

					using (IEnumerator<Bot> botsEnumerator = Bots.Where(bot => (bot.Value != this) && !rateLimitedBots.Contains(bot.Value) && bot.Value.IsConnectedAndLoggedOn && bot.Value.IsOperator(steamID)).OrderBy(bot => bot.Key).Select(bot => bot.Value).GetEnumerator()) {
						Bot currentBot = this;

						while (!string.IsNullOrEmpty(key) && (currentBot != null)) {
							if (redeemFlags.HasFlag(ERedeemFlags.Validate) && !IsValidCdKey(key)) {
								key = keysEnumerator.MoveNext() ? keysEnumerator.Current : null; // Next key
								continue; // Keep current bot
							}

							if ((currentBot == this) && (redeemFlags.HasFlag(ERedeemFlags.SkipInitial) || rateLimitedBots.Contains(currentBot))) {
								currentBot = null; // Either bot will be changed, or loop aborted
							} else {
								await LimitGiftsRequestsAsync().ConfigureAwait(false);

								ArchiHandler.PurchaseResponseCallback result = await currentBot.ArchiHandler.RedeemKey(key).ConfigureAwait(false);
								if (result == null) {
									response.Append(FormatBotResponse(string.Format(Strings.BotRedeem, key, EPurchaseResultDetail.Timeout), currentBot.BotName));
									currentBot = null; // Either bot will be changed, or loop aborted
								} else {
									if (result.PurchaseResultDetail == EPurchaseResultDetail.CannotRedeemCodeFromClient) {
										// If it's a wallet code, we try to redeem it first, then handle the inner result as our primary one
										(EResult Result, EPurchaseResultDetail? PurchaseResult)? walletResult = await currentBot.ArchiWebHandler.RedeemWalletKey(key).ConfigureAwait(false);

										if (walletResult != null) {
											result.Result = walletResult.Value.Result;
											result.PurchaseResultDetail = walletResult.Value.PurchaseResult.GetValueOrDefault(walletResult.Value.Result == EResult.OK ? EPurchaseResultDetail.NoDetail : EPurchaseResultDetail.BadActivationCode); // BadActivationCode is our smart guess in this case
										} else {
											result.Result = EResult.Timeout;
											result.PurchaseResultDetail = EPurchaseResultDetail.Timeout;
										}
									}

									switch (result.PurchaseResultDetail) {
										case EPurchaseResultDetail.BadActivationCode:
										case EPurchaseResultDetail.CannotRedeemCodeFromClient:
										case EPurchaseResultDetail.DuplicateActivationCode:
										case EPurchaseResultDetail.NoDetail: // OK
										case EPurchaseResultDetail.Timeout:
											if ((result.Items != null) && (result.Items.Count > 0)) {
												response.Append(FormatBotResponse(string.Format(Strings.BotRedeemWithItems, key, result.Result + "/" + result.PurchaseResultDetail, string.Join("", result.Items)), currentBot.BotName));
											} else {
												response.Append(FormatBotResponse(string.Format(Strings.BotRedeem, key, result.Result + "/" + result.PurchaseResultDetail), currentBot.BotName));
											}

											if ((result.Result != EResult.Timeout) && (result.PurchaseResultDetail != EPurchaseResultDetail.Timeout)) {
												unusedKeys.Remove(key);
											}

											key = keysEnumerator.MoveNext() ? keysEnumerator.Current : null; // Next key

											if (result.PurchaseResultDetail == EPurchaseResultDetail.NoDetail) {
												break; // Next bot (if needed)
											}

											continue; // Keep current bot
										case EPurchaseResultDetail.AccountLocked:
										case EPurchaseResultDetail.AlreadyPurchased:
										case EPurchaseResultDetail.DoesNotOwnRequiredApp:
										case EPurchaseResultDetail.RestrictedCountry:
											if ((result.Items != null) && (result.Items.Count > 0)) {
												response.Append(FormatBotResponse(string.Format(Strings.BotRedeemWithItems, key, result.Result + "/" + result.PurchaseResultDetail, string.Join("", result.Items)), currentBot.BotName));
											} else {
												response.Append(FormatBotResponse(string.Format(Strings.BotRedeem, key, result.Result + "/" + result.PurchaseResultDetail), currentBot.BotName));
											}

											if (!forward || (keepMissingGames && (result.PurchaseResultDetail != EPurchaseResultDetail.AlreadyPurchased))) {
												key = keysEnumerator.MoveNext() ? keysEnumerator.Current : null; // Next key
												break; // Next bot (if needed)
											}

											if (distribute) {
												break; // Next bot, without changing key
											}

											Dictionary<uint, string> items = result.Items ?? new Dictionary<uint, string>();

											bool alreadyHandled = false;
											foreach (Bot innerBot in Bots.Where(bot => (bot.Value != currentBot) && (!redeemFlags.HasFlag(ERedeemFlags.SkipInitial) || (bot.Value != this)) && !rateLimitedBots.Contains(bot.Value) && bot.Value.IsConnectedAndLoggedOn && bot.Value.IsOperator(steamID) && ((items.Count == 0) || items.Keys.Any(packageID => !bot.Value.OwnedPackageIDs.ContainsKey(packageID)))).OrderBy(bot => bot.Key).Select(bot => bot.Value)) {
												await LimitGiftsRequestsAsync().ConfigureAwait(false);

												ArchiHandler.PurchaseResponseCallback otherResult = await innerBot.ArchiHandler.RedeemKey(key).ConfigureAwait(false);
												if (otherResult == null) {
													response.Append(FormatBotResponse(string.Format(Strings.BotRedeem, key, EResult.Timeout + "/" + EPurchaseResultDetail.Timeout), innerBot.BotName));
													continue;
												}

												switch (otherResult.PurchaseResultDetail) {
													case EPurchaseResultDetail.BadActivationCode:
													case EPurchaseResultDetail.DuplicateActivationCode:
													case EPurchaseResultDetail.NoDetail: // OK
														alreadyHandled = true; // This key is already handled, as we either redeemed it or we're sure it's dupe/invalid
														unusedKeys.Remove(key);
														break;
													case EPurchaseResultDetail.RateLimited:
														rateLimitedBots.Add(innerBot);
														break;
												}

												if ((otherResult.Items != null) && (otherResult.Items.Count > 0)) {
													response.Append(FormatBotResponse(string.Format(Strings.BotRedeemWithItems, key, otherResult.Result + "/" + otherResult.PurchaseResultDetail, string.Join("", otherResult.Items)), innerBot.BotName));
												} else {
													response.Append(FormatBotResponse(string.Format(Strings.BotRedeem, key, otherResult.Result + "/" + otherResult.PurchaseResultDetail), innerBot.BotName));
												}

												if (alreadyHandled) {
													break;
												}

												if (otherResult.Items == null) {
													continue;
												}

												foreach (KeyValuePair<uint, string> item in otherResult.Items.Where(item => !items.ContainsKey(item.Key))) {
													items[item.Key] = item.Value;
												}
											}

											key = keysEnumerator.MoveNext() ? keysEnumerator.Current : null; // Next key
											break; // Next bot (if needed)
										case EPurchaseResultDetail.RateLimited:
											rateLimitedBots.Add(currentBot);
											goto case EPurchaseResultDetail.AccountLocked;
										default:
											ASF.ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(result.PurchaseResultDetail), result.PurchaseResultDetail));

											if ((result.Items != null) && (result.Items.Count > 0)) {
												response.Append(FormatBotResponse(string.Format(Strings.BotRedeemWithItems, key, result.Result + "/" + result.PurchaseResultDetail, string.Join("", result.Items)), currentBot.BotName));
											} else {
												response.Append(FormatBotResponse(string.Format(Strings.BotRedeem, key, result.Result + "/" + result.PurchaseResultDetail), currentBot.BotName));
											}

											unusedKeys.Remove(key);

											key = keysEnumerator.MoveNext() ? keysEnumerator.Current : null; // Next key
											break; // Next bot (if needed)
									}
								}
							}

							// We want to change bot in two cases:
							// a) When we have distribution enabled, obviously
							// b) When we're skipping initial bot AND we have forwarding enabled, otherwise we won't get down to other accounts
							if (distribute || (forward && redeemFlags.HasFlag(ERedeemFlags.SkipInitial))) {
								currentBot = botsEnumerator.MoveNext() ? botsEnumerator.Current : null;
							}
						}
					}

					if (key == startingKey) {
						// We ran out of bots to try for this key, so change it to avoid infinite loop
						key = keysEnumerator.MoveNext() ? keysEnumerator.Current : null; // Next key
					}
				}
			}

			if (unusedKeys.Count > 0) {
				response.Append(FormatBotResponse(string.Format(Strings.UnusedKeys, string.Join(", ", unusedKeys))));
			}

			return response.Length > 0 ? response.ToString() : null;
		}

		private static async Task<string> ResponseRedeem(ulong steamID, string botNames, string keys, ERedeemFlags redeemFlags = ERedeemFlags.None) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(keys)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(keys));
				return null;
			}

			HashSet<Bot> bots = GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(bot => bot.ResponseRedeem(steamID, keys, redeemFlags));
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
			Utilities.InBackground(
				async () => {
					await Task.Delay(1000).ConfigureAwait(false);
					await Program.Restart().ConfigureAwait(false);
				}
			);

			return FormatStaticResponse(Strings.Done);
		}

		private string ResponseResume(ulong steamID) {
			if (steamID == 0) {
				ArchiLogger.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsFamilySharing(steamID)) {
				return null;
			}

			if (!IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			if (!CardsFarmer.Paused) {
				return FormatBotResponse(Strings.BotAutomaticIdlingResumedAlready);
			}

			if (CardsFarmerResumeTimer != null) {
				CardsFarmerResumeTimer.Dispose();
				CardsFarmerResumeTimer = null;
			}

			StopFamilySharingInactivityTimer();
			Utilities.InBackground(() => CardsFarmer.Resume(true));
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
			Utilities.InBackground(Start);
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

		private string ResponseStats(ulong steamID) {
			if (steamID == 0) {
				ArchiLogger.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsOwner(steamID)) {
				return null;
			}

			ushort memoryInMegabytes = (ushort) (GC.GetTotalMemory(false) / 1024 / 1024);
			return FormatBotResponse(string.Format(Strings.BotStats, memoryInMegabytes));
		}

		private (string Response, Bot Bot) ResponseStatus(ulong steamID) {
			if (steamID == 0) {
				ArchiLogger.LogNullError(nameof(steamID));
				return (null, this);
			}

			if (!IsFamilySharing(steamID)) {
				return (null, this);
			}

			if (!IsConnectedAndLoggedOn) {
				return (FormatBotResponse(KeepRunning ? Strings.BotStatusConnecting : Strings.BotStatusNotRunning), this);
			}

			if (PlayingBlocked) {
				return (FormatBotResponse(Strings.BotStatusPlayingNotAvailable), this);
			}

			if (CardsFarmer.Paused) {
				return (FormatBotResponse(Strings.BotStatusPaused), this);
			}

			if (IsAccountLimited) {
				return (FormatBotResponse(Strings.BotStatusLimited), this);
			}

			if (IsAccountLocked) {
				return (FormatBotResponse(Strings.BotStatusLocked), this);
			}

			if (!CardsFarmer.NowFarming || (CardsFarmer.CurrentGamesFarming.Count == 0)) {
				return (FormatBotResponse(Strings.BotStatusNotIdling), this);
			}

			if (CardsFarmer.CurrentGamesFarming.Count > 1) {
				return (FormatBotResponse(string.Format(Strings.BotStatusIdlingList, string.Join(", ", CardsFarmer.CurrentGamesFarming.Select(game => game.AppID + " (" + game.GameName + ")")), CardsFarmer.GamesToFarm.Count, CardsFarmer.GamesToFarm.Sum(game => game.CardsRemaining), CardsFarmer.TimeRemaining.ToHumanReadable())), this);
			}

			CardsFarmer.Game soloGame = CardsFarmer.CurrentGamesFarming.First();
			return (FormatBotResponse(string.Format(Strings.BotStatusIdling, soloGame.AppID, soloGame.GameName, soloGame.CardsRemaining, CardsFarmer.GamesToFarm.Count, CardsFarmer.GamesToFarm.Sum(game => game.CardsRemaining), CardsFarmer.TimeRemaining.ToHumanReadable())), this);
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

			IEnumerable<Task<(string Response, Bot Bot)>> tasks = bots.Select(bot => Task.Run(() => bot.ResponseStatus(steamID)));
			ICollection<(string Response, Bot Bot)> results;

			switch (Program.GlobalConfig.OptimizationMode) {
				case GlobalConfig.EOptimizationMode.MinMemoryUsage:
					results = new List<(string Response, Bot Bot)>(bots.Count);
					foreach (Task<(string Response, Bot Bot)> task in tasks) {
						results.Add(await task.ConfigureAwait(false));
					}

					break;
				default:
					results = await Task.WhenAll(tasks).ConfigureAwait(false);
					break;
			}

			List<(string Response, Bot Bot)> validResults = new List<(string Response, Bot Bot)>(results.Where(result => !string.IsNullOrEmpty(result.Response)));
			if (validResults.Count == 0) {
				return null;
			}

			HashSet<Bot> botsRunning = validResults.Where(result => result.Bot.KeepRunning).Select(result => result.Bot).ToHashSet();

			string extraResponse = string.Format(Strings.BotStatusOverview, botsRunning.Count, validResults.Count, botsRunning.Sum(bot => bot.CardsFarmer.GamesToFarm.Count), botsRunning.Sum(bot => bot.CardsFarmer.GamesToFarm.Sum(game => game.CardsRemaining)));
			return string.Join("", validResults.Select(result => result.Response)) + FormatStaticResponse(extraResponse);
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

		private async Task<string> ResponseTransfer(ulong steamID, string mode, string botNameTo) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNameTo) || string.IsNullOrEmpty(mode)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(mode) + " || " + nameof(botNameTo));
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

			if (!Bots.TryGetValue(botNameTo, out Bot targetBot)) {
				return IsOwner(steamID) ? FormatBotResponse(string.Format(Strings.BotNotFound, botNameTo)) : null;
			}

			if (!targetBot.IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			if (targetBot.CachedSteamID == CachedSteamID) {
				return FormatBotResponse(Strings.BotSendingTradeToYourself);
			}

			string[] modes = mode.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

			if (modes.Length == 0) {
				return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(modes)));
			}

			HashSet<Steam.Asset.EType> transferTypes = new HashSet<Steam.Asset.EType>();

			foreach (string singleMode in modes) {
				switch (singleMode.ToUpper()) {
					case "A":
					case "ALL":
						foreach (Steam.Asset.EType type in Enum.GetValues(typeof(Steam.Asset.EType))) {
							transferTypes.Add(type);
						}

						break;
					case "BG":
					case "BACKGROUND":
						transferTypes.Add(Steam.Asset.EType.ProfileBackground);
						break;
					case "BO":
					case "BOOSTER":
						transferTypes.Add(Steam.Asset.EType.BoosterPack);
						break;
					case "C":
					case "CARD":
						transferTypes.Add(Steam.Asset.EType.TradingCard);
						break;
					case "E":
					case "EMOTICON":
						transferTypes.Add(Steam.Asset.EType.Emoticon);
						break;
					case "F":
					case "FOIL":
						transferTypes.Add(Steam.Asset.EType.FoilTradingCard);
						break;
					case "G":
					case "GEMS":
						transferTypes.Add(Steam.Asset.EType.SteamGems);
						break;
					case "U":
					case "UNKNOWN":
						transferTypes.Add(Steam.Asset.EType.Unknown);
						break;
					default:
						return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, mode));
				}
			}

			lock (LootingSemaphore) {
				if (LootingScheduled) {
					return FormatBotResponse(Strings.Done);
				}

				LootingScheduled = true;
			}

			await LootingSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				lock (LootingSemaphore) {
					LootingScheduled = false;
				}

				HashSet<Steam.Asset> inventory = await ArchiWebHandler.GetInventory(CachedSteamID, tradable: true, wantedTypes: transferTypes).ConfigureAwait(false);
				if ((inventory == null) || (inventory.Count == 0)) {
					return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(inventory)));
				}

				string tradeToken = null;

				if (SteamFriends.GetFriendRelationship(targetBot.CachedSteamID) != EFriendRelationship.Friend) {
					tradeToken = await targetBot.ArchiWebHandler.GetTradeToken().ConfigureAwait(false);
					if (string.IsNullOrEmpty(tradeToken)) {
						return FormatBotResponse(Strings.BotLootingFailed);
					}
				}

				if (!await ArchiWebHandler.MarkSentTrades().ConfigureAwait(false)) {
					return FormatBotResponse(Strings.BotLootingFailed);
				}

				if (!await ArchiWebHandler.SendTradeOffer(targetBot.CachedSteamID, inventory, tradeToken).ConfigureAwait(false)) {
					return FormatBotResponse(Strings.BotLootingFailed);
				}

				if (HasMobileAuthenticator) {
					// Give Steam network some time to generate confirmations
					await Task.Delay(3000).ConfigureAwait(false);
					if (!await AcceptConfirmations(true, Steam.ConfirmationDetails.EType.Trade, targetBot.CachedSteamID).ConfigureAwait(false)) {
						return FormatBotResponse(Strings.BotLootingFailed);
					}
				}
			} finally {
				LootingSemaphore.Release();
			}

			return FormatBotResponse(Strings.BotLootingSuccess);
		}

		private static async Task<string> ResponseTransfer(ulong steamID, string botNames, string mode, string botNameTo) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(mode) || string.IsNullOrEmpty(botNameTo)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(mode) + " || " + nameof(botNameTo));
				return null;
			}

			HashSet<Bot> bots = GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(bot => bot.ResponseTransfer(steamID, mode, botNameTo));
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
			if (steamID == 0) {
				ArchiLogger.LogNullError(nameof(steamID));
				return null;
			}

			return IsOperator(steamID) ? FormatBotResponse(Strings.UnknownCommand) : null;
		}

		private async Task<string> ResponseUnpackBoosters(ulong steamID) {
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

			HashSet<Steam.Asset> inventory = await ArchiWebHandler.GetInventory(CachedSteamID, wantedTypes: new HashSet<Steam.Asset.EType> { Steam.Asset.EType.BoosterPack }).ConfigureAwait(false);
			if ((inventory == null) || (inventory.Count == 0)) {
				return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(inventory)));
			}

			// It'd make sense here to actually check return code of ArchiWebHandler.UnpackBooster(), but it lies most of the time | https://github.com/JustArchi/ArchiSteamFarm/issues/704
			// It'd also make sense to run all of this in parallel, but it seems that Steam has a lot of problems with inventory-related parallel requests | https://steamcommunity.com/groups/ascfarm/discussions/1/3559414588264550284/
			foreach (Steam.Asset item in inventory) {
				await ArchiWebHandler.UnpackBooster(item.RealAppID, item.AssetID).ConfigureAwait(false);
			}

			return FormatBotResponse(Strings.Done);
		}

		private static async Task<string> ResponseUnpackBoosters(ulong steamID, string botNames) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames));
				return null;
			}

			HashSet<Bot> bots = GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(bot => bot.ResponseUnpackBoosters(steamID));
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

		private static async Task<string> ResponseUpdate(ulong steamID) {
			if (steamID == 0) {
				ASF.ArchiLogger.LogNullError(nameof(steamID));
				return null;
			}

			if (!IsOwner(steamID)) {
				return null;
			}

			Version version = await ASF.CheckAndUpdateProgram(true).ConfigureAwait(false);
			return FormatStaticResponse(version != null ? (version > SharedInfo.Version ? Strings.Success : Strings.Done) : Strings.WarningFailed);
		}

		private string ResponseVersion(ulong steamID) {
			if (steamID == 0) {
				ArchiLogger.LogNullError(nameof(steamID));
				return null;
			}

			return IsOperator(steamID) ? FormatBotResponse(string.Format(Strings.BotVersion, SharedInfo.ASF, SharedInfo.Version)) : null;
		}

		private async Task SendMessageToChannel(ulong steamID, string message) {
			if ((steamID == 0) || string.IsNullOrEmpty(message)) {
				ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(message));
				return;
			}

			if (!IsConnectedAndLoggedOn) {
				return;
			}

			ArchiLogger.LogGenericTrace(steamID + "/" + CachedSteamID + ": " + message);

			for (int i = 0; i < message.Length; i += MaxSteamMessageLength - 2) {
				if (i > 0) {
					await Task.Delay(CallbackSleep).ConfigureAwait(false);
				}

				string messagePart = (i > 0 ? "…" : "") + message.Substring(i, Math.Min(MaxSteamMessageLength - 2, message.Length - i)) + (MaxSteamMessageLength - 2 < message.Length - i ? "…" : "");
				SteamFriends.SendChatRoomMessage(steamID, EChatEntryType.ChatMsg, messagePart);
			}
		}

		private async Task SendMessageToUser(ulong steamID, string message) {
			if ((steamID == 0) || string.IsNullOrEmpty(message)) {
				ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(message));
				return;
			}

			if (!IsConnectedAndLoggedOn) {
				return;
			}

			ArchiLogger.LogGenericTrace(steamID + "/" + CachedSteamID + ": " + message);

			for (int i = 0; i < message.Length; i += MaxSteamMessageLength - 2) {
				if (i > 0) {
					await Task.Delay(CallbackSleep).ConfigureAwait(false);
				}

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
				default:
					ASF.ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(inputType), inputType));
					break;
			}
		}

		private async Task Start() {
			if (!KeepRunning) {
				KeepRunning = true;
				Utilities.InBackground(HandleCallbacks, true);
				ArchiLogger.LogGenericInfo(Strings.Starting);
			}

			// Support and convert 2FA files
			if (!HasMobileAuthenticator && File.Exists(MobileAuthenticatorFilePath)) {
				await ImportAuthenticator(MobileAuthenticatorFilePath).ConfigureAwait(false);
			}

			if (File.Exists(KeysToRedeemFilePath)) {
				await ImportKeysToRedeem(KeysToRedeemFilePath).ConfigureAwait(false);
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
			ForceDistributing = 8,
			SkipDistributing = 16,
			SkipInitial = 32,
			ForceKeepMissingGames = 64,
			SkipKeepMissingGames = 128
		}
	}
}