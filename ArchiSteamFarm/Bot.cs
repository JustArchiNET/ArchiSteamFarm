//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// 
// Copyright 2015-2018 Łukasz "JustArchi" Domeradzki
// Contact: JustArchi@JustArchi.net
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
// http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Collections;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.NLog;
using Newtonsoft.Json;
using SteamKit2;
using SteamKit2.Discovery;
using SteamKit2.Unified.Internal;

namespace ArchiSteamFarm {
	public sealed class Bot : IDisposable {
		internal const ushort CallbackSleep = 500; // In milliseconds
		internal const ushort MaxMessagePrefixLength = MaxMessageLength - ReservedMessageLength - 2; // 2 for a minimum of 2 characters (escape one and real one)
		internal const byte MinPlayingBlockedTTL = 60; // Delay in seconds added when account was occupied during our disconnect, to not disconnect other Steam client session too soon

		private const char DefaultBackgroundKeysRedeemerSeparator = '\t';
		private const byte LoginCooldownInMinutes = 25; // Captcha disappears after around 20 minutes, so we make it 25
		private const uint LoginID = 1242; // This must be the same for all ASF bots and all ASF processes
		private const ushort MaxMessageLength = 5000; // This is a limitation enforced by Steam
		private const byte MaxTwoFactorCodeFailures = 3;
		private const byte RedeemCooldownInHours = 1; // 1 hour since first redeem attempt, this is a limitation enforced by Steam
		private const byte ReservedMessageLength = 2; // 2 for 2x optional …

		internal static readonly ConcurrentDictionary<string, Bot> Bots = new ConcurrentDictionary<string, Bot>();

		private static readonly SemaphoreSlim BotsSemaphore = new SemaphoreSlim(1, 1);
		private static readonly SemaphoreSlim LoginSemaphore = new SemaphoreSlim(1, 1);

		private static SteamConfiguration SteamConfiguration;

		internal readonly Actions Actions;
		internal readonly ArchiHandler ArchiHandler;
		internal readonly ArchiLogger ArchiLogger;
		internal readonly ArchiWebHandler ArchiWebHandler;
		internal readonly BotDatabase BotDatabase;

		[JsonProperty]
		internal readonly string BotName;

		[JsonProperty]
		internal readonly CardsFarmer CardsFarmer;

		internal readonly Commands Commands;
		internal readonly ConcurrentDictionary<uint, (EPaymentMethod PaymentMethod, DateTime TimeCreated)> OwnedPackageIDs = new ConcurrentDictionary<uint, (EPaymentMethod PaymentMethod, DateTime TimeCreated)>();
		internal readonly SteamApps SteamApps;
		internal readonly SteamFriends SteamFriends;

		internal bool CanReceiveSteamCards => !IsAccountLimited && !IsAccountLocked;
		internal bool HasMobileAuthenticator => BotDatabase?.MobileAuthenticator != null;
		internal bool IsAccountLimited => AccountFlags.HasFlag(EAccountFlags.LimitedUser) || AccountFlags.HasFlag(EAccountFlags.LimitedUserForce);
		internal bool IsAccountLocked => AccountFlags.HasFlag(EAccountFlags.Lockdown);

		[JsonProperty]
		internal bool IsConnectedAndLoggedOn => SteamClient?.SteamID != null;

		[JsonProperty]
		internal bool IsPlayingPossible => !PlayingBlocked && (LibraryLockedBySteamID == 0);

		private readonly CallbackManager CallbackManager;
		private readonly SemaphoreSlim CallbackSemaphore = new SemaphoreSlim(1, 1);
		private readonly SemaphoreSlim GamesRedeemerInBackgroundSemaphore = new SemaphoreSlim(1, 1);
		private readonly Timer HeartBeatTimer;
		private readonly SemaphoreSlim InitializationSemaphore = new SemaphoreSlim(1, 1);
		private readonly SemaphoreSlim MessagingSemaphore = new SemaphoreSlim(1, 1);
		private readonly SemaphoreSlim PICSSemaphore = new SemaphoreSlim(1, 1);
		private readonly Statistics Statistics;
		private readonly SteamClient SteamClient;
		private readonly ConcurrentHashSet<ulong> SteamFamilySharingIDs = new ConcurrentHashSet<ulong>();
		private readonly SteamUser SteamUser;
		private readonly Trading Trading;

		private string BotPath => Path.Combine(SharedInfo.ConfigDirectory, BotName);
		private string ConfigFilePath => BotPath + SharedInfo.ConfigExtension;
		private string DatabaseFilePath => BotPath + SharedInfo.DatabaseExtension;
		private string KeysToRedeemFilePath => BotPath + SharedInfo.KeysExtension;
		private string KeysToRedeemUnusedFilePath => KeysToRedeemFilePath + SharedInfo.KeysUnusedExtension;
		private string KeysToRedeemUsedFilePath => KeysToRedeemFilePath + SharedInfo.KeysUsedExtension;
		private string MobileAuthenticatorFilePath => BotPath + SharedInfo.MobileAuthenticatorExtension;
		private string SentryFilePath => BotPath + SharedInfo.SentryHashExtension;

		[JsonProperty(PropertyName = SharedInfo.UlongCompatibilityStringPrefix + nameof(SteamID))]
		private string SSteamID => SteamID.ToString();

		[JsonProperty]
		internal BotConfig BotConfig { get; private set; }

		[JsonProperty]
		internal bool KeepRunning { get; private set; }

		internal bool PlayingBlocked { get; private set; }
		internal bool PlayingWasBlocked { get; private set; }
		internal ulong SteamID { get; private set; }

		[JsonProperty]
		private EAccountFlags AccountFlags;

		private string AuthCode;

		[JsonProperty]
		private string AvatarHash;

		private Timer ConnectionFailureTimer;
		private string DeviceID;
		private bool FirstTradeSent;
		private Timer GamesRedeemerInBackgroundTimer;
		private uint GiftsCount;
		private byte HeartBeatFailures;
		private uint ItemsCount;
		private EResult LastLogOnResult;
		private DateTime LastLogonSessionReplaced;
		private ulong LibraryLockedBySteamID;
		private ulong MasterChatGroupID;
		private Timer PlayingWasBlockedTimer;
		private bool ReconnectOnUserInitiated;
		private Timer SendItemsTimer;
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

			if (Debugging.IsUserDebugging && Directory.Exists(SharedInfo.DebugDirectory)) {
				string debugListenerPath = Path.Combine(SharedInfo.DebugDirectory, botName);

				try {
					Directory.CreateDirectory(debugListenerPath);
					SteamClient.DebugNetworkListener = new NetHookNetworkListener(debugListenerPath);
				} catch (Exception e) {
					ArchiLogger.LogGenericException(e);
				}
			}

			SteamUnifiedMessages steamUnifiedMessages = SteamClient.GetHandler<SteamUnifiedMessages>();

			ArchiHandler = new ArchiHandler(ArchiLogger, steamUnifiedMessages);
			SteamClient.AddHandler(ArchiHandler);

			CallbackManager = new CallbackManager(SteamClient);
			CallbackManager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
			CallbackManager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);

			SteamApps = SteamClient.GetHandler<SteamApps>();
			CallbackManager.Subscribe<SteamApps.GuestPassListCallback>(OnGuestPassList);
			CallbackManager.Subscribe<SteamApps.LicenseListCallback>(OnLicenseList);

			SteamFriends = SteamClient.GetHandler<SteamFriends>();
			CallbackManager.Subscribe<SteamFriends.FriendsListCallback>(OnFriendsList);
			CallbackManager.Subscribe<SteamFriends.PersonaStateCallback>(OnPersonaState);

			CallbackManager.Subscribe<SteamUnifiedMessages.ServiceMethodNotification>(OnServiceMethod);

			SteamUser = SteamClient.GetHandler<SteamUser>();
			CallbackManager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOff);
			CallbackManager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
			CallbackManager.Subscribe<SteamUser.LoginKeyCallback>(OnLoginKey);
			CallbackManager.Subscribe<SteamUser.UpdateMachineAuthCallback>(OnMachineAuth);

			CallbackManager.Subscribe<ArchiHandler.PlayingSessionStateCallback>(OnPlayingSessionState);
			CallbackManager.Subscribe<ArchiHandler.SharedLibraryLockStatusCallback>(OnSharedLibraryLockStatus);
			CallbackManager.Subscribe<ArchiHandler.UserNotificationsCallback>(OnUserNotifications);
			CallbackManager.Subscribe<ArchiHandler.VanityURLChangedCallback>(OnVanityURLChangedCallback);

			Actions = new Actions(this);
			ArchiWebHandler = new ArchiWebHandler(this);
			CardsFarmer = new CardsFarmer(this);
			Commands = new Commands(this);
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
			Actions.Dispose();
			CallbackSemaphore.Dispose();
			GamesRedeemerInBackgroundSemaphore.Dispose();
			InitializationSemaphore.Dispose();
			MessagingSemaphore.Dispose();
			PICSSemaphore.Dispose();

			// Those are objects that might be null and the check should be in-place
			ArchiWebHandler?.Dispose();
			BotDatabase?.Dispose();
			CardsFarmer?.Dispose();
			ConnectionFailureTimer?.Dispose();
			GamesRedeemerInBackgroundTimer?.Dispose();
			HeartBeatTimer?.Dispose();
			PlayingWasBlockedTimer?.Dispose();
			SendItemsTimer?.Dispose();
			Statistics?.Dispose();
			SteamSaleEvent?.Dispose();
			Trading?.Dispose();
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

				if (!DeleteRedeemedKeysFiles()) {
					return false;
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

		internal bool DeleteRedeemedKeysFiles() {
			try {
				if (File.Exists(KeysToRedeemUnusedFilePath)) {
					File.Delete(KeysToRedeemUnusedFilePath);
				}

				if (File.Exists(KeysToRedeemUsedFilePath)) {
					File.Delete(KeysToRedeemUsedFilePath);
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
					productInfoResultSet = await SteamApps.PICSGetProductInfo(appID, null, false);
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

					try {
						IEnumerable<Bot> regexMatches = Bots.Where(kvp => Regex.IsMatch(kvp.Key, botPattern, RegexOptions.CultureInvariant)).Select(kvp => kvp.Value);
						result.UnionWith(regexMatches);
					} catch (ArgumentException e) {
						ASF.ArchiLogger.LogGenericWarningException(e);
						return null;
					}
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
					productInfoResultSet = await SteamApps.PICSGetProductInfo(Enumerable.Empty<uint>(), packageIDs);
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

		internal BotConfig.EPermission GetSteamUserPermission(ulong steamID) {
			if (steamID == 0) {
				ArchiLogger.LogNullError(nameof(steamID));
				return BotConfig.EPermission.None;
			}

			return BotConfig.SteamUserPermissions.TryGetValue(steamID, out BotConfig.EPermission permission) ? permission : BotConfig.EPermission.None;
		}

		internal async Task<byte?> GetTradeHoldDuration(ulong steamID, ulong tradeID) {
			if ((steamID == 0) || (tradeID == 0)) {
				ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(tradeID));
				return null;
			}

			if (SteamFriends.GetFriendRelationship(steamID) == EFriendRelationship.Friend) {
				return await ArchiWebHandler.GetTradeHoldDurationForUser(steamID).ConfigureAwait(false);
			}

			Bot targetBot = Bots.Values.FirstOrDefault(bot => bot.SteamID == steamID);
			if (targetBot != null) {
				string targetTradeToken = await targetBot.ArchiWebHandler.GetTradeToken().ConfigureAwait(false);
				if (!string.IsNullOrEmpty(targetTradeToken)) {
					return await ArchiWebHandler.GetTradeHoldDurationForUser(steamID, targetTradeToken).ConfigureAwait(false);
				}
			}

			return await ArchiWebHandler.GetTradeHoldDurationForTrade(tradeID).ConfigureAwait(false);
		}

		internal async Task<(Dictionary<string, string> UnusedKeys, Dictionary<string, string> UsedKeys)> GetUsedAndUnusedKeys() {
			IList<Dictionary<string, string>> results = await Utilities.InParallel(new[] { KeysToRedeemUnusedFilePath, KeysToRedeemUsedFilePath }.Select(GetKeysFromFile)).ConfigureAwait(false);
			return (results[0], results[1]);
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

		internal bool IsFamilySharing(ulong steamID) {
			if (steamID == 0) {
				ArchiLogger.LogNullError(nameof(steamID));
				return false;
			}

			return ASF.IsOwner(steamID) || SteamFamilySharingIDs.Contains(steamID) || (GetSteamUserPermission(steamID) >= BotConfig.EPermission.FamilySharing);
		}

		internal bool IsMaster(ulong steamID) {
			if (steamID == 0) {
				ArchiLogger.LogNullError(nameof(steamID));
				return false;
			}

			return ASF.IsOwner(steamID) || (GetSteamUserPermission(steamID) >= BotConfig.EPermission.Master);
		}

		internal bool IsPriorityIdling(uint appID) {
			if (appID == 0) {
				ArchiLogger.LogNullError(nameof(appID));
				return false;
			}

			return BotDatabase.IsPriorityIdling(appID);
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

				if (BotConfig.SendOnFarmingFinished) {
					await Actions.SendTradeOffer(wantedTypes: BotConfig.LootableTypes).ConfigureAwait(false);
				}
			}

			if (BotConfig.ShutdownOnFarmingFinished) {
				if (farmedSomething || (Program.GlobalConfig.IdleFarmingPeriod == 0)) {
					Stop();
					return;
				}

				if (Actions.SkipFirstShutdown) {
					Actions.SkipFirstShutdown = false;
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
				callback = await SteamUser.RequestWebAPIUserNonce();
			} catch (Exception e) {
				ArchiLogger.LogGenericWarningException(e);
				await Connect(true).ConfigureAwait(false);
				return false;
			}

			if (string.IsNullOrEmpty(callback?.Nonce)) {
				await Connect(true).ConfigureAwait(false);
				return false;
			}

			if (await ArchiWebHandler.Init(SteamID, SteamClient.Universe, callback.Nonce, BotConfig.SteamParentalCode).ConfigureAwait(false)) {
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

			BotDatabase botDatabase = await BotDatabase.CreateOrLoad(databaseFilePath).ConfigureAwait(false);
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

			SteamFriends.RequestFriendInfo(SteamID, EClientPersonaStateFlag.PlayerName | EClientPersonaStateFlag.Presence);
		}

		internal async Task<bool> SendMessage(ulong steamID, string message) {
			if ((steamID == 0) || string.IsNullOrEmpty(message)) {
				ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(message));
				return false;
			}

			if (!IsConnectedAndLoggedOn || !new SteamID(steamID).IsIndividualAccount) {
				return false;
			}

			ArchiLogger.LogChatMessage(true, message, steamID: steamID);

			ushort maxMessageLength = (ushort) (MaxMessageLength - ReservedMessageLength - (Program.GlobalConfig.SteamMessagePrefix?.Length ?? 0));

			// We must escape our message prior to sending it
			message = Escape(message);

			for (int i = 0; i < message.Length; i += maxMessageLength) {
				string messagePart = message.Substring(i, Math.Min(maxMessageLength, message.Length - i));

				// If our message is of max length and ends with a single '\' then we can't split it here, it escapes the next character
				if ((messagePart.Length >= maxMessageLength) && (messagePart[messagePart.Length - 1] == '\\') && (messagePart[messagePart.Length - 2] != '\\')) {
					// Instead, we'll cut this message one char short and include the rest in next iteration
					messagePart = messagePart.Remove(messagePart.Length - 1);
					i--;
				}

				messagePart = Program.GlobalConfig.SteamMessagePrefix + (i > 0 ? "…" : "") + messagePart + (maxMessageLength < message.Length - i ? "…" : "");

				await MessagingSemaphore.WaitAsync().ConfigureAwait(false);

				try {
					bool sent = false;

					for (byte j = 0; (j < WebBrowser.MaxTries) && !sent && IsConnectedAndLoggedOn; j++) {
						EResult result = await ArchiHandler.SendMessage(steamID, messagePart).ConfigureAwait(false);

						switch (result) {
							case EResult.OK:
								sent = true;
								break;
							case EResult.RateLimitExceeded:
							case EResult.Timeout:
								await Task.Delay(5000).ConfigureAwait(false);
								continue;
							default:
								ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(result), result));
								return false;
						}
					}

					if (!sent) {
						ArchiLogger.LogGenericWarning(Strings.WarningFailed);
						return false;
					}
				} finally {
					MessagingSemaphore.Release();
				}
			}

			return true;
		}

		internal async Task<bool> SendMessage(ulong chatGroupID, ulong chatID, string message) {
			if ((chatGroupID == 0) || (chatID == 0) || string.IsNullOrEmpty(message)) {
				ArchiLogger.LogNullError(nameof(chatGroupID) + " || " + nameof(chatID) + " || " + nameof(message));
				return false;
			}

			if (!IsConnectedAndLoggedOn) {
				return false;
			}

			ArchiLogger.LogChatMessage(true, message, chatGroupID, chatID);

			ushort maxMessageLength = (ushort) (MaxMessageLength - ReservedMessageLength - (Program.GlobalConfig.SteamMessagePrefix?.Length ?? 0));

			// We must escape our message prior to sending it
			message = Escape(message);

			for (int i = 0; i < message.Length; i += maxMessageLength) {
				string messagePart = message.Substring(i, Math.Min(maxMessageLength, message.Length - i));

				// If our message is of max length and ends with a single '\' then we can't split it here, it escapes the next character
				if ((messagePart.Length >= maxMessageLength) && (messagePart[messagePart.Length - 1] == '\\') && (messagePart[messagePart.Length - 2] != '\\')) {
					// Instead, we'll cut this message one char short and include the rest in next iteration
					messagePart = messagePart.Remove(messagePart.Length - 1);
					i--;
				}

				messagePart = Program.GlobalConfig.SteamMessagePrefix + (i > 0 ? "…" : "") + messagePart + (maxMessageLength < message.Length - i ? "…" : "");

				await MessagingSemaphore.WaitAsync().ConfigureAwait(false);

				try {
					bool sent = false;

					for (byte j = 0; (j < WebBrowser.MaxTries) && !sent && IsConnectedAndLoggedOn; j++) {
						EResult result = await ArchiHandler.SendMessage(chatGroupID, chatID, messagePart).ConfigureAwait(false);

						switch (result) {
							case EResult.OK:
								sent = true;
								break;
							case EResult.RateLimitExceeded:
							case EResult.Timeout:
								await Task.Delay(5000).ConfigureAwait(false);
								continue;
							default:
								ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(result), result));
								return false;
						}
					}

					if (!sent) {
						ArchiLogger.LogGenericWarning(Strings.WarningFailed);
						return false;
					}
				} finally {
					MessagingSemaphore.Release();
				}
			}

			return true;
		}

		internal void SetUserInput(ASF.EUserInputType inputType, string inputValue) {
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
						BotConfig.DecryptedSteamPassword = inputValue;
					}

					break;
				case ASF.EUserInputType.SteamGuard:
					AuthCode = inputValue;
					break;
				case ASF.EUserInputType.SteamParentalCode:
					if (BotConfig != null) {
						BotConfig.SteamParentalCode = inputValue;
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

		internal async Task Start() {
			if (KeepRunning) {
				return;
			}

			KeepRunning = true;
			Utilities.InBackground(HandleCallbacks, true);
			ArchiLogger.LogGenericInfo(Strings.Starting);

			// Support and convert 2FA files
			if (!HasMobileAuthenticator && File.Exists(MobileAuthenticatorFilePath)) {
				await ImportAuthenticator(MobileAuthenticatorFilePath).ConfigureAwait(false);
			}

			if (File.Exists(KeysToRedeemFilePath)) {
				await ImportKeysToRedeem(KeysToRedeemFilePath).ConfigureAwait(false);
			}

			await Connect().ConfigureAwait(false);
		}

		internal void Stop(bool skipShutdownEvent = false) {
			if (!KeepRunning) {
				return;
			}

			KeepRunning = false;
			ArchiLogger.LogGenericInfo(Strings.BotStopping);

			if (SteamClient.IsConnected) {
				Disconnect();
			}

			if (!skipShutdownEvent) {
				Utilities.InBackground(Events.OnBotShutdown);
			}
		}

		internal async Task<bool> ValidateAndAddGamesToRedeemInBackground(OrderedDictionary gamesToRedeemInBackground) {
			if ((gamesToRedeemInBackground == null) || (gamesToRedeemInBackground.Count == 0)) {
				ArchiLogger.LogNullError(nameof(gamesToRedeemInBackground));
				return false;
			}

			HashSet<object> invalidKeys = new HashSet<object>();

			foreach (DictionaryEntry game in gamesToRedeemInBackground) {
				bool invalid = false;

				string key = game.Key as string;
				if (string.IsNullOrEmpty(key)) {
					invalid = true;
					ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorIsInvalid, nameof(key)));
				} else if (!Utilities.IsValidCdKey(key)) {
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
					return false;
				}
			}

			await BotDatabase.AddGamesToRedeemInBackground(gamesToRedeemInBackground).ConfigureAwait(false);

			if ((GamesRedeemerInBackgroundTimer == null) && BotDatabase.HasGamesToRedeemInBackground && IsConnectedAndLoggedOn) {
				Utilities.InBackground(RedeemGamesInBackground);
			}

			return true;
		}

		private async Task CheckOccupationStatus() {
			StopPlayingWasBlockedTimer();

			if (!IsPlayingPossible) {
				ArchiLogger.LogGenericInfo(Strings.BotAccountOccupied);
				PlayingWasBlocked = true;
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
				// Stop() will most likely block due to connection freeze, don't wait for it
				Utilities.InBackground(() => Stop());
			}

			Bots.TryRemove(BotName, out _);
		}

		private void Disconnect() {
			StopConnectionFailureTimer();
			SteamClient.Disconnect();
		}

		private static string Escape(string message) {
			if (string.IsNullOrEmpty(message)) {
				ASF.ArchiLogger.LogNullError(nameof(message));
				return null;
			}

			return message.Replace("\\", "\\\\").Replace("[", "\\[");
		}

		private async Task<Dictionary<string, string>> GetKeysFromFile(string filePath) {
			if (string.IsNullOrEmpty(filePath)) {
				ArchiLogger.LogNullError(nameof(filePath));
				return null;
			}

			if (!File.Exists(filePath)) {
				return new Dictionary<string, string>(0);
			}

			try {
				Dictionary<string, string> keys = new Dictionary<string, string>();

				using (StreamReader reader = new StreamReader(filePath)) {
					string line;

					while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null) {
						if (line.Length == 0) {
							continue;
						}

						string[] parsedArgs = line.Split(DefaultBackgroundKeysRedeemerSeparator, StringSplitOptions.RemoveEmptyEntries);
						if (parsedArgs.Length < 3) {
							ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorIsInvalid, line));
							continue;
						}

						string key = parsedArgs[parsedArgs.Length - 1];
						if (!Utilities.IsValidCdKey(key)) {
							ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorIsInvalid, key));
							continue;
						}

						string name = parsedArgs[0];
						keys[key] = name;
					}
				}

				return keys;
			} catch (Exception e) {
				ArchiLogger.LogGenericException(e);
				return null;
			}
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

		private async Task HandleMessage(ulong chatGroupID, ulong chatID, ulong steamID, string message) {
			if ((chatGroupID == 0) || (chatID == 0) || (steamID == 0) || string.IsNullOrEmpty(message)) {
				ArchiLogger.LogNullError(nameof(chatGroupID) + " || " + nameof(chatID) + " || " + nameof(steamID) + " || " + nameof(message));
				return;
			}

			string response = await Commands.Response(steamID, message).ConfigureAwait(false);

			// We respond with null when user is not authorized (and similar)
			if (string.IsNullOrEmpty(response)) {
				return;
			}

			await SendMessage(chatGroupID, chatID, response).ConfigureAwait(false);
		}

		private async Task HandleMessage(ulong steamID, string message) {
			if ((steamID == 0) || string.IsNullOrEmpty(message)) {
				ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(message));
				return;
			}

			string response = await Commands.Response(steamID, message).ConfigureAwait(false);

			// We respond with null when user is not authorized (and similar)
			if (string.IsNullOrEmpty(response)) {
				return;
			}

			await SendMessage(steamID, response).ConfigureAwait(false);
		}

		private async Task HeartBeat() {
			if (!KeepRunning || !IsConnectedAndLoggedOn || (HeartBeatFailures == byte.MaxValue)) {
				return;
			}

			try {
				if (DateTime.UtcNow.Subtract(ArchiHandler.LastPacketReceived).TotalSeconds > Program.GlobalConfig.ConnectionTimeout) {
					await SteamFriends.RequestProfileInfo(SteamClient.SteamID);
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

			if (requiresPassword && string.IsNullOrEmpty(BotConfig.DecryptedSteamPassword)) {
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

			if ((BotConfig.SendTradePeriod > 0) && BotConfig.SteamUserPermissions.Values.Any(permission => permission >= BotConfig.EPermission.Master)) {
				SendItemsTimer = new Timer(
					async e => await Actions.SendTradeOffer(wantedTypes: BotConfig.LootableTypes).ConfigureAwait(false),
					null,
					TimeSpan.FromHours(BotConfig.SendTradePeriod) + TimeSpan.FromSeconds(Program.LoadBalancingDelay * Bots.Count), // Delay
					TimeSpan.FromHours(BotConfig.SendTradePeriod) // Period
				);
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

		private bool IsMasterClanID(ulong steamID) {
			if (steamID == 0) {
				ArchiLogger.LogNullError(nameof(steamID));
				return false;
			}

			return steamID == BotConfig.SteamMasterClanID;
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

		private async Task JoinMasterChatGroupID() {
			if (BotConfig.SteamMasterClanID == 0) {
				return;
			}

			ulong chatGroupID = await ArchiHandler.GetClanChatGroupID(BotConfig.SteamMasterClanID).ConfigureAwait(false);

			if (chatGroupID == 0) {
				return;
			}

			MasterChatGroupID = chatGroupID;

			HashSet<ulong> chatGroupIDs = await ArchiHandler.GetMyChatGroupIDs().ConfigureAwait(false);

			if (chatGroupIDs?.Contains(chatGroupID) != false) {
				return;
			}

			await ArchiHandler.JoinChatRoomGroup(chatGroupID).ConfigureAwait(false);
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
					sentryFileHash = CryptoHelper.SHAHash(sentryFileContent);
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
				if (!string.IsNullOrEmpty(loginKey) && (loginKey.Length > 19) && (BotConfig.PasswordFormat != ArchiCryptoHelper.ECryptoMethod.PlainText)) {
					loginKey = ArchiCryptoHelper.Decrypt(BotConfig.PasswordFormat, loginKey);
				}
			} else {
				// If we're not using login keys, ensure we don't have any saved
				await BotDatabase.SetLoginKey().ConfigureAwait(false);
			}

			if (!InitLoginAndPassword(string.IsNullOrEmpty(loginKey))) {
				Stop();
				return;
			}

			// Steam login and password fields can contain ASCII characters only, including spaces
			const string nonAsciiPattern = @"[^\u0000-\u007F]+";

			string username = Regex.Replace(BotConfig.SteamLogin, nonAsciiPattern, "", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

			string password = BotConfig.DecryptedSteamPassword;
			if (!string.IsNullOrEmpty(password)) {
				password = Regex.Replace(password, nonAsciiPattern, "", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
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

			Actions.OnDisconnected();
			ArchiWebHandler.OnDisconnected();
			CardsFarmer.OnDisconnected();
			Trading.OnDisconnected();

			FirstTradeSent = false;

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

		private async void OnFriendsList(SteamFriends.FriendsListCallback callback) {
			if (callback?.FriendList == null) {
				ArchiLogger.LogNullError(nameof(callback) + " || " + nameof(callback.FriendList));
				return;
			}

			foreach (SteamFriends.FriendsListCallback.Friend friend in callback.FriendList.Where(friend => friend.Relationship == EFriendRelationship.RequestRecipient)) {
				switch (friend.SteamID.AccountType) {
					case EAccountType.Clan when IsMasterClanID(friend.SteamID):
						ArchiHandler.AcknowledgeClanInvite(friend.SteamID, true);
						await JoinMasterChatGroupID().ConfigureAwait(false);
						break;
					case EAccountType.Clan:
						if (BotConfig.BotBehaviour.HasFlag(BotConfig.EBotBehaviour.RejectInvalidGroupInvites)) {
							ArchiHandler.AcknowledgeClanInvite(friend.SteamID, false);
						}

						break;
					default:
						if (IsFamilySharing(friend.SteamID)) {
							await ArchiHandler.AddFriend(friend.SteamID).ConfigureAwait(false);
						} else if (BotConfig.BotBehaviour.HasFlag(BotConfig.EBotBehaviour.RejectInvalidFriendInvites)) {
							await ArchiHandler.RemoveFriend(friend.SteamID).ConfigureAwait(false);
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

			HashSet<ulong> guestPassIDs = callback.GuestPasses.Select(guestPass => guestPass["gid"].AsUnsignedLong()).Where(gid => gid != 0).ToHashSet();
			if (guestPassIDs.Count == 0) {
				return;
			}

			await Actions.AcceptGuestPasses(guestPassIDs).ConfigureAwait(false);
		}

		private async Task OnIncomingChatMessage(CChatRoom_IncomingChatMessage_Notification notification) {
			if (notification == null) {
				ArchiLogger.LogNullError(nameof(notification));
				return;
			}

			if ((notification.steamid_sender != SteamID) && BotConfig.BotBehaviour.HasFlag(BotConfig.EBotBehaviour.MarkReceivedMessagesAsRead)) {
				Utilities.InBackground(() => ArchiHandler.AckChatMessage(notification.chat_group_id, notification.chat_id, notification.timestamp));
			}

			string message;

			// Prefer to use message without bbcode, but only if it's available
			if (!string.IsNullOrEmpty(notification.message_no_bbcode)) {
				message = notification.message_no_bbcode;
			} else if (!string.IsNullOrEmpty(notification.message)) {
				message = UnEscape(notification.message);
			} else {
				return;
			}

			ArchiLogger.LogChatMessage(false, message, notification.chat_group_id, notification.chat_id, notification.steamid_sender);

			// Steam network broadcasts chat events also when we don't explicitly sign into Steam community
			// We'll explicitly ignore those messages when using offline mode, as it was done in the first version of Steam chat when no messages were broadcasted at all before signing in
			// Handling messages will still work correctly in invisible mode, which is how it should work in the first place
			// This goes in addition to usual logic that ignores irrelevant messages from being parsed further
			if ((notification.chat_group_id != MasterChatGroupID) || (BotConfig.OnlineStatus == EPersonaState.Offline)) {
				return;
			}

			await HandleMessage(notification.chat_group_id, notification.chat_id, notification.steamid_sender, message).ConfigureAwait(false);
		}

		private async Task OnIncomingMessage(CFriendMessages_IncomingMessage_Notification notification) {
			if (notification == null) {
				ArchiLogger.LogNullError(nameof(notification));
				return;
			}

			if ((EChatEntryType) notification.chat_entry_type != EChatEntryType.ChatMsg) {
				return;
			}

			if (!notification.local_echo && BotConfig.BotBehaviour.HasFlag(BotConfig.EBotBehaviour.MarkReceivedMessagesAsRead)) {
				Utilities.InBackground(() => ArchiHandler.AckMessage(notification.steamid_friend, notification.rtime32_server_timestamp));
			}

			string message;

			// Prefer to use message without bbcode, but only if it's available
			if (!string.IsNullOrEmpty(notification.message_no_bbcode)) {
				message = notification.message_no_bbcode;
			} else if (!string.IsNullOrEmpty(notification.message)) {
				message = UnEscape(notification.message);
			} else {
				return;
			}

			ArchiLogger.LogChatMessage(notification.local_echo, message, steamID: notification.steamid_friend);

			// Steam network broadcasts chat events also when we don't explicitly sign into Steam community
			// We'll explicitly ignore those messages when using offline mode, as it was done in the first version of Steam chat when no messages were broadcasted at all before signing in
			// Handling messages will still work correctly in invisible mode, which is how it should work in the first place
			// This goes in addition to usual logic that ignores irrelevant messages from being parsed further
			if (notification.local_echo || (BotConfig.OnlineStatus == EPersonaState.Offline)) {
				return;
			}

			await HandleMessage(notification.steamid_friend, message).ConfigureAwait(false);
		}

		private async void OnLicenseList(SteamApps.LicenseListCallback callback) {
			if (callback?.LicenseList == null) {
				ArchiLogger.LogNullError(nameof(callback) + " || " + nameof(callback.LicenseList));
				return;
			}

			// Return early if this update doesn't bring anything new
			if (callback.LicenseList.Count == OwnedPackageIDs.Count) {
				if (callback.LicenseList.All(license => OwnedPackageIDs.ContainsKey(license.PackageID))) {
					if (!await CardsFarmer.Resume(false).ConfigureAwait(false)) {
						await ResetGamesPlayed().ConfigureAwait(false);
					}

					return;
				}
			}

			Commands.OnNewLicenseList();
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
					SteamID = callback.ClientSteamID;

					ArchiLogger.LogGenericInfo(string.Format(Strings.BotLoggedOn, SteamID + (!string.IsNullOrEmpty(callback.VanityURL) ? "/" + callback.VanityURL : "")));

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

					if (!string.IsNullOrEmpty(BotConfig.SteamParentalCode) && (BotConfig.SteamParentalCode.Length != 4)) {
						string steamParentalCode = Program.GetUserInput(ASF.EUserInputType.SteamParentalCode, BotName);
						if (string.IsNullOrEmpty(steamParentalCode) || (steamParentalCode.Length != 4)) {
							Stop();
							break;
						}

						SetUserInput(ASF.EUserInputType.SteamParentalCode, steamParentalCode);
					}

					ArchiWebHandler.OnVanityURLChanged(callback.VanityURL);

					if (!await ArchiWebHandler.Init(callback.ClientSteamID, SteamClient.Universe, callback.WebAPIUserNonce, BotConfig.SteamParentalCode).ConfigureAwait(false)) {
						if (!await RefreshSession().ConfigureAwait(false)) {
							break;
						}
					}

					// TODO: Until https://github.com/dotnet/corefx/issues/27232 is dealt with, use this fallback as an alternative for keys import
					if (OS.IsUnix && File.Exists(KeysToRedeemFilePath)) {
						await ImportKeysToRedeem(KeysToRedeemFilePath).ConfigureAwait(false);
						await Task.Delay(1000).ConfigureAwait(false);
					}

					if ((GamesRedeemerInBackgroundTimer == null) && BotDatabase.HasGamesToRedeemInBackground) {
						Utilities.InBackground(RedeemGamesInBackground);
					}

					ArchiHandler.SetCurrentMode(2);
					ArchiHandler.RequestItemAnnouncements();

					// Sometimes Steam won't send us our own PersonaStateCallback, so request it explicitly
					RequestPersonaStateUpdate();

					// This will pre-cache API key for eventual further usage
					Utilities.InBackground(ArchiWebHandler.HasValidApiKey);

					Utilities.InBackground(InitializeFamilySharing);

					if (Statistics != null) {
						Utilities.InBackground(Statistics.OnLoggedOn);
					}

					if (BotConfig.OnlineStatus != EPersonaState.Offline) {
						SteamFriends.SetPersonaState(BotConfig.OnlineStatus);
					}

					if (BotConfig.SteamMasterClanID != 0) {
						Utilities.InBackground(
							async () => {
								await ArchiWebHandler.JoinGroup(BotConfig.SteamMasterClanID).ConfigureAwait(false);
								await JoinMasterChatGroupID().ConfigureAwait(false);
							}
						);
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
			if (BotConfig.PasswordFormat != ArchiCryptoHelper.ECryptoMethod.PlainText) {
				loginKey = ArchiCryptoHelper.Encrypt(BotConfig.PasswordFormat, loginKey);
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

		private async void OnPersonaState(SteamFriends.PersonaStateCallback callback) {
			if (callback == null) {
				ArchiLogger.LogNullError(nameof(callback));
				return;
			}

			if (callback.FriendID == SteamID) {
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

		private async void OnServiceMethod(SteamUnifiedMessages.ServiceMethodNotification notification) {
			if (notification == null) {
				ArchiLogger.LogNullError(nameof(notification));
				return;
			}

			switch (notification.MethodName) {
				case "ChatRoomClient.NotifyIncomingChatMessage#1":
					await OnIncomingChatMessage((CChatRoom_IncomingChatMessage_Notification) notification.Body).ConfigureAwait(false);
					break;
				case "FriendMessagesClient.IncomingMessage#1":
					await OnIncomingMessage((CFriendMessages_IncomingMessage_Notification) notification.Body).ConfigureAwait(false);
					break;
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
					case ArchiHandler.UserNotificationsCallback.EUserNotification.Gifts:
						bool newGifts = notification.Value > GiftsCount;
						GiftsCount = notification.Value;

						if (newGifts && BotConfig.AcceptGifts) {
							ArchiLogger.LogGenericTrace(nameof(ArchiHandler.UserNotificationsCallback.EUserNotification.Gifts));
							Utilities.InBackground(Actions.AcceptDigitalGiftCards);
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

					ArchiHandler.PurchaseResponseCallback result = await Actions.RedeemKey(key).ConfigureAwait(false);
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

					string logEntry = name + DefaultBackgroundKeysRedeemerSeparator + "[" + result.PurchaseResultDetail + "]" + ((result.Items != null) && (result.Items.Count > 0) ? DefaultBackgroundKeysRedeemerSeparator + string.Join(", ", result.Items) : "") + DefaultBackgroundKeysRedeemerSeparator + key;

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
			if (!IsPlayingPossible || CardsFarmer.NowFarming) {
				return;
			}

			if (BotConfig.GamesPlayedWhileIdle.Count > 0) {
				// This function might be executed before PlayingSessionStateCallback/SharedLibraryLockStatusCallback, ensure proper delay in this case
				await Task.Delay(2000).ConfigureAwait(false);

				if (!IsPlayingPossible || CardsFarmer.NowFarming) {
					return;
				}
			}

			await ArchiHandler.PlayGames(BotConfig.GamesPlayedWhileIdle, BotConfig.CustomGamePlayedWhileIdle).ConfigureAwait(false);
		}

		private void ResetPlayingWasBlockedWithTimer() {
			PlayingWasBlocked = false;
			StopPlayingWasBlockedTimer();
		}

		private void StopConnectionFailureTimer() {
			if (ConnectionFailureTimer == null) {
				return;
			}

			ConnectionFailureTimer.Dispose();
			ConnectionFailureTimer = null;
		}

		private void StopPlayingWasBlockedTimer() {
			if (PlayingWasBlockedTimer == null) {
				return;
			}

			PlayingWasBlockedTimer.Dispose();
			PlayingWasBlockedTimer = null;
		}

		private static string UnEscape(string message) {
			if (string.IsNullOrEmpty(message)) {
				ASF.ArchiLogger.LogNullError(nameof(message));
				return null;
			}

			return message.Replace("\\[", "[").Replace("\\\\", "\\");
		}
	}
}
