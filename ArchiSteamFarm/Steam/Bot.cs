// ----------------------------------------------------------------------------------------------
//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// ----------------------------------------------------------------------------------------------
// |
// Copyright 2015-2024 Łukasz "JustArchi" Domeradzki
// Contact: JustArchi@JustArchi.net
// |
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// |
// http://www.apache.org/licenses/LICENSE-2.0
// |
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Dom;
using ArchiSteamFarm.Collections;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers;
using ArchiSteamFarm.Helpers.Json;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.NLog;
using ArchiSteamFarm.Plugins;
using ArchiSteamFarm.Steam.Cards;
using ArchiSteamFarm.Steam.Data;
using ArchiSteamFarm.Steam.Exchange;
using ArchiSteamFarm.Steam.Integration;
using ArchiSteamFarm.Steam.Integration.Callbacks;
using ArchiSteamFarm.Steam.Interaction;
using ArchiSteamFarm.Steam.Security;
using ArchiSteamFarm.Steam.Storage;
using ArchiSteamFarm.Storage;
using ArchiSteamFarm.Web;
using JetBrains.Annotations;
using Microsoft.IdentityModel.JsonWebTokens;
using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.Internal;

namespace ArchiSteamFarm.Steam;

public sealed class Bot : IAsyncDisposable, IDisposable {
	internal const ushort CallbackSleep = 500; // In milliseconds
	internal const byte MinCardsPerBadge = 5;

	private const char DefaultBackgroundKeysRedeemerSeparator = '\t';
	private const byte LoginCooldownInMinutes = 25; // Captcha disappears after around 20 minutes, so we make it 25
	private const uint LoginID = 1242; // This must be the same for all ASF bots and all ASF processes
	private const byte MaxLoginFailures = WebBrowser.MaxTries; // Max login failures in a row before we determine that our credentials are invalid (because Steam wrongly returns those, of course)course)
	private const byte MinimumAccessTokenValidityMinutes = 5;
	private const byte RedeemCooldownInHours = 1; // 1 hour since first redeem attempt, this is a limitation enforced by Steam
	private const byte RegionRestrictionPlayableBlockMonths = 3;

	[PublicAPI]
	public static IReadOnlyDictionary<string, Bot>? BotsReadOnly => Bots;

	internal static ConcurrentDictionary<string, Bot>? Bots { get; private set; }
	internal static StringComparer? BotsComparer { get; private set; }
	internal static EOSType OSType { get; private set; } = EOSType.Unknown;

	private static readonly SemaphoreSlim BotsSemaphore = new(1, 1);

	[JsonIgnore]
	[PublicAPI]
	public Actions Actions { get; }

	[JsonIgnore]
	[PublicAPI]
	public ArchiHandler ArchiHandler { get; }

	[JsonIgnore]
	[PublicAPI]
	public ArchiLogger ArchiLogger { get; }

	[JsonIgnore]
	[PublicAPI]
	public ArchiWebHandler ArchiWebHandler { get; }

	[JsonIgnore]
	[PublicAPI]
	public BotDatabase BotDatabase { get; }

	[JsonInclude]
	[PublicAPI]
	[Required]
	public string BotName { get; }

	[JsonInclude]
	[PublicAPI]
	[Required]
	public CardsFarmer CardsFarmer { get; }

	[JsonIgnore]
	[PublicAPI]
	public Commands Commands { get; }

	[JsonInclude]
	[PublicAPI]
	[Required]
	public uint GamesToRedeemInBackgroundCount => BotDatabase.GamesToRedeemInBackgroundCount;

	[JsonInclude]
	[PublicAPI]
	[Required]
	public bool HasMobileAuthenticator => BotDatabase.MobileAuthenticator != null;

	[JsonIgnore]
	[PublicAPI]
	public bool IsAccountLimited => AccountFlags.HasFlag(EAccountFlags.LimitedUser) || AccountFlags.HasFlag(EAccountFlags.LimitedUserForce);

	[JsonIgnore]
	[PublicAPI]
	public bool IsAccountLocked => AccountFlags.HasFlag(EAccountFlags.Lockdown);

	[JsonInclude]
	[PublicAPI]
	[Required]
	public bool IsConnectedAndLoggedOn => SteamClient.SteamID != null;

	[JsonInclude]
	[PublicAPI]
	[Required]
	public bool IsPlayingPossible => !PlayingBlocked && !LibraryLocked;

	[JsonInclude]
	[PublicAPI]
	public string? PublicIP => SteamClient.PublicIP?.ToString();

	[JsonInclude]
	[JsonPropertyName($"{SharedInfo.UlongCompatibilityStringPrefix}{nameof(SteamID)}")]
	[PublicAPI]
	[Required]
	public string SSteamID => SteamID.ToString(CultureInfo.InvariantCulture);

	[JsonIgnore]
	[PublicAPI]
	public SteamApps SteamApps { get; }

	[JsonIgnore]
	[PublicAPI]
	public SteamConfiguration SteamConfiguration { get; }

	[JsonIgnore]
	[PublicAPI]
	public SteamFriends SteamFriends { get; }

	internal bool CanReceiveSteamCards => !IsAccountLimited && !IsAccountLocked;
	internal bool HasLoginCodeReady => !string.IsNullOrEmpty(TwoFactorCode) || !string.IsNullOrEmpty(AuthCode);

	private readonly CallbackManager CallbackManager;
	private readonly SemaphoreSlim CallbackSemaphore = new(1, 1);
	private readonly SemaphoreSlim GamesRedeemerInBackgroundSemaphore = new(1, 1);
	private readonly Timer HeartBeatTimer;
	private readonly SemaphoreSlim InitializationSemaphore = new(1, 1);
	private readonly SemaphoreSlim MessagingSemaphore = new(1, 1);
	private readonly ConcurrentDictionary<UserNotificationsCallback.EUserNotification, uint> PastNotifications = new();
	private readonly SemaphoreSlim RefreshWebSessionSemaphore = new(1, 1);
	private readonly SemaphoreSlim SendCompleteTypesSemaphore = new(1, 1);
	private readonly SteamClient SteamClient;
	private readonly ConcurrentHashSet<ulong> SteamFamilySharingIDs = [];
	private readonly SteamUser SteamUser;
	private readonly Trading Trading;

	private IEnumerable<(string FilePath, EFileType FileType)> RelatedFiles {
		get {
			foreach (EFileType fileType in Enum.GetValues<EFileType>()) {
				string filePath = GetFilePath(fileType);

				if (string.IsNullOrEmpty(filePath)) {
					ArchiLogger.LogNullError(filePath);

					yield break;
				}

				yield return (filePath, fileType);
			}
		}
	}

	[JsonIgnore]
	[PublicAPI]
	public string? AccessToken {
		get => BackingAccessToken;

		private set {
			AccessTokenValidUntil = null;

			if (string.IsNullOrEmpty(value)) {
				BackingAccessToken = null;

				return;
			}

			if (!Utilities.TryReadJsonWebToken(value, out JsonWebToken? accessToken)) {
				ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, nameof(accessToken)));

				return;
			}

			BackingAccessToken = value;

			if (accessToken.ValidTo > DateTime.MinValue) {
				AccessTokenValidUntil = accessToken.ValidTo;
			}
		}
	}

	[JsonInclude]
	[JsonRequired]
	[PublicAPI]
	[Required]
	public EAccountFlags AccountFlags { get; private set; }

	[JsonInclude]
	[PublicAPI]
	public string? AvatarHash { get; private set; }

	[JsonInclude]
	[JsonRequired]
	[PublicAPI]
	[Required]
	public BotConfig BotConfig { get; private set; }

	[JsonInclude]
	[JsonRequired]
	[PublicAPI]
	[Required]
	public bool KeepRunning { get; private set; }

	[JsonInclude]
	[PublicAPI]
	public string? Nickname { get; private set; }

	[JsonIgnore]
	[PublicAPI]
	public FrozenDictionary<uint, (EPaymentMethod PaymentMethod, DateTime TimeCreated)> OwnedPackageIDs { get; private set; } = FrozenDictionary<uint, (EPaymentMethod PaymentMethod, DateTime TimeCreated)>.Empty;

	[JsonInclude]
	[JsonRequired]
	[PublicAPI]
	[Required]
	public ASF.EUserInputType RequiredInput { get; private set; }

	[JsonInclude]
	[JsonRequired]
	[PublicAPI]
	[Required]
	public ulong SteamID { get; private set; }

	[JsonInclude]
	[JsonRequired]
	[PublicAPI]
	[Required]
	public long WalletBalance { get; private set; }

	[JsonInclude]
	[JsonRequired]
	[PublicAPI]
	[Required]
	public long WalletBalanceDelayed { get; private set; }

	[JsonInclude]
	[JsonRequired]
	[PublicAPI]
	[Required]
	public ECurrencyCode WalletCurrency { get; private set; }

	internal byte HeartBeatFailures { get; private set; }
	internal bool PlayingBlocked { get; private set; }
	internal bool PlayingWasBlocked { get; private set; }

	private DateTime? AccessTokenValidUntil;
	private string? AuthCode;

	private string? BackingAccessToken;
	private Timer? ConnectionFailureTimer;
	private bool FirstTradeSent;
	private Timer? GamesRedeemerInBackgroundTimer;
	private string? IPCountryCode;
	private EResult LastLogOnResult;
	private DateTime LastLogonSessionReplaced;
	private bool LibraryLocked;
	private byte LoginFailures;
	private ulong MasterChatGroupID;
	private Timer? PlayingWasBlockedTimer;
	private bool ReconnectOnUserInitiated;
	private string? RefreshToken;
	private Timer? RefreshTokensTimer;
	private bool SendCompleteTypesScheduled;
	private Timer? SendItemsTimer;
	private bool SteamParentalActive;
	private SteamSaleEvent? SteamSaleEvent;
	private Timer? TradeCheckTimer;
	private string? TwoFactorCode;

	private Bot(string botName, BotConfig botConfig, BotDatabase botDatabase) {
		ArgumentException.ThrowIfNullOrEmpty(botName);
		ArgumentNullException.ThrowIfNull(botConfig);
		ArgumentNullException.ThrowIfNull(botDatabase);

		if (ASF.GlobalDatabase == null) {
			throw new InvalidOperationException(nameof(ASF.GlobalDatabase));
		}

		BotName = botName;
		BotConfig = botConfig;
		BotDatabase = botDatabase;

		ArchiLogger = new ArchiLogger(botName);

		BotDatabase.MobileAuthenticator?.Init(this);

		ArchiWebHandler = new ArchiWebHandler(this);

		SteamConfiguration = SteamConfiguration.Create(
			builder => {
				builder.WithCellID(ASF.GlobalDatabase.CellID);
				builder.WithHttpClientFactory(ArchiWebHandler.GenerateDisposableHttpClient);
				builder.WithProtocolTypes(ASF.GlobalConfig?.SteamProtocols ?? GlobalConfig.DefaultSteamProtocols);
				builder.WithServerListProvider(ASF.GlobalDatabase.ServerListProvider);

				IMachineInfoProvider? customMachineInfoProvider = PluginsCore.GetCustomMachineInfoProvider(this).Result;

				if (customMachineInfoProvider != null) {
					builder.WithMachineInfoProvider(customMachineInfoProvider);
				}
			}
		);

		// Initialize
		SteamClient = new SteamClient(SteamConfiguration, botName);

		if (Debugging.IsDebugConfigured && Directory.Exists(ASF.DebugDirectory)) {
			string debugListenerPath = Path.Combine(ASF.DebugDirectory, botName);

			try {
				Directory.CreateDirectory(debugListenerPath);

				SteamClient.DebugNetworkListener = new NetHookNetworkListener(debugListenerPath, SteamClient);
			} catch (Exception e) {
				ArchiLogger.LogGenericException(e);
			}
		}

		ArchiHandler = new ArchiHandler(ArchiLogger, SteamClient.GetHandler<SteamUnifiedMessages>() ?? throw new InvalidOperationException(nameof(SteamUnifiedMessages)));
		SteamClient.AddHandler(ArchiHandler);

		CallbackManager = new CallbackManager(SteamClient);
		CallbackManager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
		CallbackManager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);

		SteamApps = SteamClient.GetHandler<SteamApps>() ?? throw new InvalidOperationException(nameof(SteamApps));
		CallbackManager.Subscribe<SteamApps.GuestPassListCallback>(OnGuestPassList);
		CallbackManager.Subscribe<SteamApps.LicenseListCallback>(OnLicenseList);

		SteamFriends = SteamClient.GetHandler<SteamFriends>() ?? throw new InvalidOperationException(nameof(SteamFriends));
		CallbackManager.Subscribe<SteamFriends.FriendsListCallback>(OnFriendsList);
		CallbackManager.Subscribe<SteamFriends.PersonaStateCallback>(OnPersonaState);

		CallbackManager.Subscribe<SteamUnifiedMessages.ServiceMethodNotification>(OnServiceMethod);

		SteamUser = SteamClient.GetHandler<SteamUser>() ?? throw new InvalidOperationException(nameof(SteamUser));
		CallbackManager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOff);
		CallbackManager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
		CallbackManager.Subscribe<SteamUser.PlayingSessionStateCallback>(OnPlayingSessionState);
		CallbackManager.Subscribe<SteamUser.VanityURLChangedCallback>(OnVanityURLChangedCallback);
		CallbackManager.Subscribe<SteamUser.WalletInfoCallback>(OnWalletInfo);

		CallbackManager.Subscribe<SharedLibraryLockStatusCallback>(OnSharedLibraryLockStatus);
		CallbackManager.Subscribe<UserNotificationsCallback>(OnUserNotifications);

		Actions = new Actions(this);
		CardsFarmer = new CardsFarmer(this);
		Commands = new Commands(this);
		Trading = new Trading(this);

		HeartBeatTimer = new Timer(
			HeartBeat,
			null,
			TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(ASF.LoadBalancingDelay * Bots?.Count ?? 0), // Delay
			TimeSpan.FromMinutes(1) // Period
		);
	}

	public void Dispose() {
		// Those are objects that are always being created if constructor doesn't throw exception
		ArchiWebHandler.Dispose();
		BotDatabase.Dispose();
		CallbackSemaphore.Dispose();
		GamesRedeemerInBackgroundSemaphore.Dispose();
		InitializationSemaphore.Dispose();
		MessagingSemaphore.Dispose();
		RefreshWebSessionSemaphore.Dispose();
		SendCompleteTypesSemaphore.Dispose();
		Trading.Dispose();

		Actions.Dispose();
		CardsFarmer.Dispose();
		HeartBeatTimer.Dispose();

		// Those are objects that might be null and the check should be in-place
		ConnectionFailureTimer?.Dispose();
		GamesRedeemerInBackgroundTimer?.Dispose();
		PlayingWasBlockedTimer?.Dispose();
		RefreshTokensTimer?.Dispose();
		SendItemsTimer?.Dispose();
		SteamSaleEvent?.Dispose();
		TradeCheckTimer?.Dispose();
	}

	public async ValueTask DisposeAsync() {
		// Those are objects that are always being created if constructor doesn't throw exception
		ArchiWebHandler.Dispose();
		BotDatabase.Dispose();
		CallbackSemaphore.Dispose();
		GamesRedeemerInBackgroundSemaphore.Dispose();
		InitializationSemaphore.Dispose();
		MessagingSemaphore.Dispose();
		RefreshWebSessionSemaphore.Dispose();
		SendCompleteTypesSemaphore.Dispose();
		Trading.Dispose();

		await Actions.DisposeAsync().ConfigureAwait(false);
		await CardsFarmer.DisposeAsync().ConfigureAwait(false);
		await HeartBeatTimer.DisposeAsync().ConfigureAwait(false);

		// Those are objects that might be null and the check should be in-place
		if (ConnectionFailureTimer != null) {
			await ConnectionFailureTimer.DisposeAsync().ConfigureAwait(false);
		}

		if (GamesRedeemerInBackgroundTimer != null) {
			await GamesRedeemerInBackgroundTimer.DisposeAsync().ConfigureAwait(false);
		}

		if (PlayingWasBlockedTimer != null) {
			await PlayingWasBlockedTimer.DisposeAsync().ConfigureAwait(false);
		}

		if (RefreshTokensTimer != null) {
			await RefreshTokensTimer.DisposeAsync().ConfigureAwait(false);
		}

		if (SendItemsTimer != null) {
			await SendItemsTimer.DisposeAsync().ConfigureAwait(false);
		}

		if (SteamSaleEvent != null) {
			await SteamSaleEvent.DisposeAsync().ConfigureAwait(false);
		}

		if (TradeCheckTimer != null) {
			await TradeCheckTimer.DisposeAsync().ConfigureAwait(false);
		}
	}

	[PublicAPI]
	public async Task<bool> DeleteAllRelatedFiles() {
		await BotDatabase.MakeReadOnly().ConfigureAwait(false);

		foreach (string filePath in RelatedFiles.Select(static file => file.FilePath).Where(File.Exists)) {
			try {
				File.Delete(filePath);
			} catch (Exception e) {
				ArchiLogger.LogGenericException(e);

				return false;
			}
		}

		return true;
	}

	[PublicAPI]
	public EAccess GetAccess(ulong steamID) {
		if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		if (ASF.IsOwner(steamID)) {
			return EAccess.Owner;
		}

		EAccess familySharingAccess = SteamFamilySharingIDs.Contains(steamID) ? EAccess.FamilySharing : EAccess.None;

		if (!BotConfig.SteamUserPermissions.TryGetValue(steamID, out BotConfig.EAccess permission)) {
			return familySharingAccess;
		}

		switch (permission) {
			case BotConfig.EAccess.None:
				return EAccess.None;
			case BotConfig.EAccess.FamilySharing:
				return EAccess.FamilySharing;
			case BotConfig.EAccess.Operator:
				return EAccess.Operator;
			case BotConfig.EAccess.Master:
				return EAccess.Master;
			default:
				ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(permission), permission));

				return familySharingAccess;
		}
	}

	[PublicAPI]
	public static Bot? GetBot(string botName) {
		ArgumentException.ThrowIfNullOrEmpty(botName);

		if (Bots == null) {
			throw new InvalidOperationException(nameof(Bots));
		}

		if (Bots.TryGetValue(botName, out Bot? targetBot)) {
			return targetBot;
		}

		if (!ulong.TryParse(botName, out ulong steamID) || (steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
			return null;
		}

		return Bots.Values.FirstOrDefault(bot => bot.SteamID == steamID);
	}

	[PublicAPI]
	public static HashSet<Bot>? GetBots(string args) {
		ArgumentException.ThrowIfNullOrEmpty(args);

		if (Bots == null) {
			throw new InvalidOperationException(nameof(Bots));
		}

		if (BotsComparer == null) {
			throw new InvalidOperationException(nameof(BotsComparer));
		}

		string[] botNames = args.Split(SharedInfo.ListElementSeparators, StringSplitOptions.RemoveEmptyEntries);

		HashSet<Bot> result = [];

		foreach (string botName in botNames) {
			switch (botName.ToUpperInvariant()) {
				case "@ALL":
				case SharedInfo.ASF:
					// We can return the result right away, as all bots have been matched already
					return Bots.OrderBy(static bot => bot.Key, BotsComparer).Select(static bot => bot.Value).ToHashSet();
				case "@FARMING":
					IEnumerable<Bot> farmingBots = Bots.Where(static bot => bot.Value.CardsFarmer.NowFarming).OrderBy(static bot => bot.Key, BotsComparer).Select(static bot => bot.Value);
					result.UnionWith(farmingBots);

					continue;
				case "@IDLE":
					IEnumerable<Bot> idleBots = Bots.Where(static bot => !bot.Value.CardsFarmer.NowFarming).OrderBy(static bot => bot.Key, BotsComparer).Select(static bot => bot.Value);
					result.UnionWith(idleBots);

					continue;
				case "@OFFLINE":
					IEnumerable<Bot> offlineBots = Bots.Where(static bot => !bot.Value.IsConnectedAndLoggedOn).OrderBy(static bot => bot.Key, BotsComparer).Select(static bot => bot.Value);
					result.UnionWith(offlineBots);

					continue;
				case "@ONLINE":
					IEnumerable<Bot> onlineBots = Bots.Where(static bot => bot.Value.IsConnectedAndLoggedOn).OrderBy(static bot => bot.Key, BotsComparer).Select(static bot => bot.Value);
					result.UnionWith(onlineBots);

					continue;
			}

			if ((botName.Length > 2) && SharedInfo.RangeIndicators.Any(rangeIndicator => botName.Contains(rangeIndicator, StringComparison.Ordinal))) {
				string[] botRange = botName.Split(SharedInfo.RangeIndicators, StringSplitOptions.RemoveEmptyEntries);

				Bot? firstBot = GetBot(botRange[0]);

				if (firstBot != null) {
					switch (botRange.Length) {
						case 1:
							// Either bot.. or ..bot
							IEnumerable<Bot> query = Bots.OrderBy(static bot => bot.Key, BotsComparer).Select(static bot => bot.Value);

							query = botName.StartsWith("..", StringComparison.Ordinal) ? query.TakeWhile(bot => bot != firstBot) : query.SkipWhile(bot => bot != firstBot);

							foreach (Bot bot in query) {
								result.Add(bot);
							}

							result.Add(firstBot);

							continue;
						case 2:
							// firstBot..lastBot
							Bot? lastBot = GetBot(botRange[1]);

							if ((lastBot != null) && (BotsComparer.Compare(firstBot.BotName, lastBot.BotName) <= 0)) {
								foreach (Bot bot in Bots.OrderBy(static bot => bot.Key, BotsComparer).Select(static bot => bot.Value).SkipWhile(bot => bot != firstBot).TakeWhile(bot => bot != lastBot)) {
									result.Add(bot);
								}

								result.Add(lastBot);

								continue;
							}

							break;
					}
				}
			}

			if (botName.StartsWith("r!", StringComparison.OrdinalIgnoreCase)) {
				string botsPattern = botName[2..];

				RegexOptions botsRegex = RegexOptions.None;

				if ((BotsComparer == StringComparer.InvariantCulture) || (BotsComparer == StringComparer.Ordinal)) {
					botsRegex |= RegexOptions.CultureInvariant;
				} else if ((BotsComparer == StringComparer.InvariantCultureIgnoreCase) || (BotsComparer == StringComparer.OrdinalIgnoreCase)) {
					botsRegex |= RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;
				}

				Regex regex;

				try {
#pragma warning disable CA3012 // We're aware of a potential denial of service here, this is why we limit maximum matching time to a sane value
					regex = new Regex(botsPattern, botsRegex, TimeSpan.FromSeconds(1));
#pragma warning restore CA3012 // We're aware of a potential denial of service here, this is why we limit maximum matching time to a sane value
				} catch (ArgumentException e) {
					ASF.ArchiLogger.LogGenericWarningException(e);

					return null;
				}

				try {
					IEnumerable<Bot> regexMatches = Bots.Where(kvp => regex.IsMatch(kvp.Key)).Select(static kvp => kvp.Value);

					result.UnionWith(regexMatches);
				} catch (RegexMatchTimeoutException e) {
					ASF.ArchiLogger.LogGenericException(e);
				}

				continue;
			}

			Bot? singleBot = GetBot(botName);

			if (singleBot == null) {
				continue;
			}

			result.Add(singleBot);
		}

		return result;
	}

	[PublicAPI]
	public static string GetFilePath(string botName, EFileType fileType) {
		ArgumentException.ThrowIfNullOrEmpty(botName);

		if (!Enum.IsDefined(fileType)) {
			throw new InvalidEnumArgumentException(nameof(fileType), (int) fileType, typeof(EFileType));
		}

		string botPath = Path.Combine(SharedInfo.ConfigDirectory, botName);

		return fileType switch {
			EFileType.Config => $"{botPath}{SharedInfo.JsonConfigExtension}",
			EFileType.Database => $"{botPath}{SharedInfo.DatabaseExtension}",
			EFileType.KeysToRedeem => $"{botPath}{SharedInfo.KeysExtension}",
			EFileType.KeysToRedeemUnused => $"{botPath}{SharedInfo.KeysExtension}{SharedInfo.KeysUnusedExtension}",
			EFileType.KeysToRedeemUsed => $"{botPath}{SharedInfo.KeysExtension}{SharedInfo.KeysUsedExtension}",
			EFileType.MobileAuthenticator => $"{botPath}{SharedInfo.MobileAuthenticatorExtension}",
			_ => throw new InvalidOperationException(nameof(fileType))
		};
	}

	[PublicAPI]
	public string GetFilePath(EFileType fileType) {
		if (!Enum.IsDefined(fileType)) {
			throw new InvalidEnumArgumentException(nameof(fileType), (int) fileType, typeof(EFileType));
		}

		return GetFilePath(BotName, fileType);
	}

	[PublicAPI]
	public T? GetHandler<T>() where T : ClientMsgHandler => SteamClient.GetHandler<T>();

	[PublicAPI]
	public static HashSet<Asset> GetItemsForFullSets(IReadOnlyCollection<Asset> inventory, IReadOnlyDictionary<(uint RealAppID, EAssetType Type, EAssetRarity Rarity), (uint SetsToExtract, byte ItemsPerSet)> amountsToExtract, ushort maxItems = Trading.MaxItemsPerTrade) {
		if ((inventory == null) || (inventory.Count == 0)) {
			throw new ArgumentNullException(nameof(inventory));
		}

		if ((amountsToExtract == null) || (amountsToExtract.Count == 0)) {
			throw new ArgumentNullException(nameof(amountsToExtract));
		}

		ArgumentOutOfRangeException.ThrowIfLessThan(maxItems, MinCardsPerBadge);

		HashSet<Asset> result = [];
		Dictionary<(uint RealAppID, EAssetType Type, EAssetRarity Rarity), Dictionary<ulong, HashSet<Asset>>> itemsPerClassIDPerSet = inventory.GroupBy(static item => (item.RealAppID, item.Type, item.Rarity)).ToDictionary(static grouping => grouping.Key, static grouping => grouping.GroupBy(static item => item.ClassID).ToDictionary(static group => group.Key, static group => group.ToHashSet()));

		foreach (((uint RealAppID, EAssetType Type, EAssetRarity Rarity) set, (uint setsToExtract, byte itemsPerSet)) in amountsToExtract.OrderBy(static kv => kv.Value.ItemsPerSet)) {
			if (!itemsPerClassIDPerSet.TryGetValue(set, out Dictionary<ulong, HashSet<Asset>>? itemsPerClassID)) {
				continue;
			}

			if (itemsPerSet < itemsPerClassID.Count) {
				throw new InvalidOperationException($"{nameof(itemsPerSet)} < {nameof(itemsPerClassID)}");
			}

			if (itemsPerSet > itemsPerClassID.Count) {
				continue;
			}

			ushort maxSetsAllowed = (ushort) ((maxItems - result.Count) / itemsPerSet);
			ushort realSetsToExtract = (ushort) Math.Min(setsToExtract, maxSetsAllowed);

			if (realSetsToExtract == 0) {
				break;
			}

			foreach (HashSet<Asset> itemsOfClass in itemsPerClassID.Values) {
				ushort classRemaining = realSetsToExtract;

				foreach (Asset item in itemsOfClass.TakeWhile(_ => classRemaining > 0)) {
					if (classRemaining >= item.Amount) {
						result.Add(item);

						classRemaining -= (ushort) item.Amount;
					} else {
						Asset itemToSend = item.DeepClone();
						itemToSend.Amount = classRemaining;
						result.Add(itemToSend);

						classRemaining = 0;
					}
				}
			}
		}

		return result;
	}

	[PublicAPI]
	public async Task<HashSet<uint>?> GetPossiblyCompletedBadgeAppIDs() {
		using IDocument? badgePage = await ArchiWebHandler.GetBadgePage(1).ConfigureAwait(false);

		if (badgePage == null) {
			ArchiLogger.LogGenericWarning(Strings.WarningCouldNotCheckBadges);

			return null;
		}

		byte maxPages = 1;
		INode? htmlNode = badgePage.SelectSingleNode("(//a[@class='pagelink'])[last()]");

		if (htmlNode != null) {
			string lastPage = htmlNode.TextContent;

			if (string.IsNullOrEmpty(lastPage)) {
				ArchiLogger.LogNullError(lastPage);

				return null;
			}

			if (!byte.TryParse(lastPage, out maxPages) || (maxPages == 0)) {
				ArchiLogger.LogNullError(maxPages);

				return null;
			}
		}

		HashSet<uint>? firstPageResult = GetPossiblyCompletedBadgeAppIDs(badgePage);

		if (firstPageResult == null) {
			return null;
		}

		if (maxPages == 1) {
			return firstPageResult;
		}

		switch (ASF.GlobalConfig?.OptimizationMode) {
			case GlobalConfig.EOptimizationMode.MinMemoryUsage:
				for (byte page = 2; page <= maxPages; page++) {
					HashSet<uint>? pageIDs = await GetPossiblyCompletedBadgeAppIDs(page).ConfigureAwait(false);

					if (pageIDs == null) {
						return null;
					}

					firstPageResult.UnionWith(pageIDs);
				}

				return firstPageResult;
			default:
				HashSet<Task<HashSet<uint>?>> tasks = new(maxPages - 1);

				for (byte page = 2; page <= maxPages; page++) {
					// ReSharper disable once InlineTemporaryVariable - we need a copy of variable being passed when in for loops, as loop will proceed before our task is launched
					byte currentPage = page;
					tasks.Add(GetPossiblyCompletedBadgeAppIDs(currentPage));
				}

				IList<HashSet<uint>?> results = await Utilities.InParallel(tasks).ConfigureAwait(false);

				foreach (HashSet<uint>? result in results) {
					if (result == null) {
						return null;
					}

					firstPageResult.UnionWith(result);
				}

				return firstPageResult;
		}
	}

	[PublicAPI]
	public async Task<byte?> GetTradeHoldDuration(ulong steamID, ulong tradeID) {
		if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		ArgumentOutOfRangeException.ThrowIfZero(tradeID);

		if (Bots == null) {
			throw new InvalidOperationException(nameof(Bots));
		}

		if (SteamFriends.GetFriendRelationship(steamID) == EFriendRelationship.Friend) {
			byte? tradeHoldDuration = await ArchiWebHandler.GetCombinedTradeHoldDurationAgainstUser(steamID).ConfigureAwait(false);

			if (tradeHoldDuration.HasValue) {
				return tradeHoldDuration;
			}
		}

		Bot? targetBot = Bots.Values.FirstOrDefault(bot => bot.SteamID == steamID);

		if (targetBot?.IsConnectedAndLoggedOn == true) {
			string? targetTradeToken = await targetBot.ArchiHandler.GetTradeToken().ConfigureAwait(false);

			if (!string.IsNullOrEmpty(targetTradeToken)) {
				byte? tradeHoldDuration = await ArchiWebHandler.GetCombinedTradeHoldDurationAgainstUser(steamID, targetTradeToken).ConfigureAwait(false);

				if (tradeHoldDuration.HasValue) {
					return tradeHoldDuration;
				}
			}
		}

		return await ArchiWebHandler.GetTradeHoldDurationForTrade(tradeID).ConfigureAwait(false);
	}

	[PublicAPI]
	public async Task<Dictionary<uint, byte>?> LoadCardsPerSet(IReadOnlyCollection<uint> appIDs) {
		if ((appIDs == null) || (appIDs.Count == 0)) {
			throw new ArgumentNullException(nameof(appIDs));
		}

		IReadOnlySet<uint> uniqueAppIDs = appIDs as IReadOnlySet<uint> ?? appIDs.ToHashSet();

		switch (ASF.GlobalConfig?.OptimizationMode) {
			case GlobalConfig.EOptimizationMode.MinMemoryUsage:
				Dictionary<uint, byte> result = new(uniqueAppIDs.Count);

				foreach (uint appID in uniqueAppIDs) {
					byte cardCount = await ArchiWebHandler.GetCardCountForGame(appID).ConfigureAwait(false);

					if (cardCount == 0) {
						return null;
					}

					result.Add(appID, cardCount);
				}

				return result;
			default:
				IEnumerable<Task<(uint AppID, byte Cards)>> tasks = uniqueAppIDs.Select(async appID => (AppID: appID, Cards: await ArchiWebHandler.GetCardCountForGame(appID).ConfigureAwait(false)));
				IList<(uint AppID, byte Cards)> results = await Utilities.InParallel(tasks).ConfigureAwait(false);

				return results.All(static tuple => tuple.Cards > 0) ? results.ToDictionary(static res => res.AppID, static res => res.Cards) : null;
		}
	}

	[PublicAPI]
	public async Task<bool> SendMessage(ulong steamID, string message) {
		if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		ArgumentException.ThrowIfNullOrEmpty(message);

		if (!IsConnectedAndLoggedOn) {
			return false;
		}

		ArchiLogger.LogChatMessage(true, message, steamID: steamID);

		string? steamMessagePrefix = ASF.GlobalConfig != null ? ASF.GlobalConfig.SteamMessagePrefix : GlobalConfig.DefaultSteamMessagePrefix;

		await foreach (string messagePart in SteamChatMessage.GetMessageParts(message, steamMessagePrefix, IsAccountLimited).ConfigureAwait(false)) {
			if (!await SendMessagePart(steamID, messagePart).ConfigureAwait(false)) {
				ArchiLogger.LogGenericWarning(Strings.WarningFailed);

				return false;
			}
		}

		return true;
	}

	[PublicAPI]
	public async Task<bool> SendMessage(ulong chatGroupID, ulong chatID, string message) {
		ArgumentOutOfRangeException.ThrowIfZero(chatGroupID);
		ArgumentOutOfRangeException.ThrowIfZero(chatID);
		ArgumentException.ThrowIfNullOrEmpty(message);

		if (!IsConnectedAndLoggedOn) {
			return false;
		}

		ArchiLogger.LogChatMessage(true, message, chatGroupID, chatID);

		string? steamMessagePrefix = ASF.GlobalConfig != null ? ASF.GlobalConfig.SteamMessagePrefix : GlobalConfig.DefaultSteamMessagePrefix;

		await foreach (string messagePart in SteamChatMessage.GetMessageParts(message, steamMessagePrefix, IsAccountLimited).ConfigureAwait(false)) {
			if (!await SendMessagePart(chatID, messagePart, chatGroupID).ConfigureAwait(false)) {
				ArchiLogger.LogGenericWarning(Strings.WarningFailed);

				return false;
			}
		}

		return true;
	}

	[PublicAPI]
	public bool SetUserInput(ASF.EUserInputType inputType, string inputValue) {
		if ((inputType == ASF.EUserInputType.None) || !Enum.IsDefined(inputType)) {
			throw new InvalidEnumArgumentException(nameof(inputType), (int) inputType, typeof(ASF.EUserInputType));
		}

		ArgumentException.ThrowIfNullOrEmpty(inputValue);

		// This switch should cover ONLY bot properties
		switch (inputType) {
			case ASF.EUserInputType.DeviceConfirmation:
				// Nothing to do for us
				break;
			case ASF.EUserInputType.Login:
				BotConfig.SteamLogin = inputValue;

				// Do not allow saving this account credential
				BotConfig.IsSteamLoginSet = false;

				break;
			case ASF.EUserInputType.Password:
				BotConfig.SteamPassword = inputValue;

				// Do not allow saving this account credential
				BotConfig.IsSteamPasswordSet = false;

				// If by any chance user has wrongly configured password format, we reset it back to plaintext
				BotConfig.PasswordFormat = ArchiCryptoHelper.ECryptoMethod.PlainText;

				break;
			case ASF.EUserInputType.SteamGuard:
				if (inputValue.Length != 5) {
					return false;
				}

				AuthCode = inputValue;

				break;
			case ASF.EUserInputType.SteamParentalCode:
				if ((inputValue.Length != BotConfig.SteamParentalCodeLength) || inputValue.Any(static character => character is < '0' or > '9')) {
					return false;
				}

				BotConfig.SteamParentalCode = inputValue;

				// Do not allow saving this account credential
				BotConfig.IsSteamParentalCodeSet = false;

				break;
			case ASF.EUserInputType.TwoFactorAuthentication:
				switch (inputValue.Length) {
					case MobileAuthenticator.BackupCodeDigits:
					case MobileAuthenticator.CodeDigits:
						break;
					default:
						return false;
				}

				inputValue = inputValue.ToUpperInvariant();

				if (inputValue.Any(static character => !MobileAuthenticator.CodeCharacters.Contains(character))) {
					return false;
				}

				TwoFactorCode = inputValue;

				break;
			default:
				throw new InvalidOperationException(nameof(inputType));
		}

		if (RequiredInput == inputType) {
			RequiredInput = ASF.EUserInputType.None;
		}

		return true;
	}

	internal void AddGamesToRedeemInBackground(IOrderedDictionary gamesToRedeemInBackground) {
		if ((gamesToRedeemInBackground == null) || (gamesToRedeemInBackground.Count == 0)) {
			throw new ArgumentNullException(nameof(gamesToRedeemInBackground));
		}

		BotDatabase.AddGamesToRedeemInBackground(gamesToRedeemInBackground);

		if ((GamesRedeemerInBackgroundTimer == null) && BotDatabase.HasGamesToRedeemInBackground && IsConnectedAndLoggedOn) {
			Utilities.InBackground(() => RedeemGamesInBackground());
		}
	}

	internal async Task CheckOccupationStatus() {
		StopPlayingWasBlockedTimer();

		if (!IsPlayingPossible) {
			PlayingWasBlocked = true;
			ArchiLogger.LogGenericInfo(Strings.BotAccountOccupied);

			return;
		}

		if (PlayingWasBlocked && (PlayingWasBlockedTimer == null)) {
			InitPlayingWasBlockedTimer();
		}

		ArchiLogger.LogGenericInfo(Strings.BotAccountFree);

		if (!await CardsFarmer.Resume(false).ConfigureAwait(false)) {
			await ResetGamesPlayed().ConfigureAwait(false);
		}
	}

	internal bool DeleteRedeemedKeysFiles() {
		string unusedKeysFilePath = GetFilePath(EFileType.KeysToRedeemUnused);

		if (string.IsNullOrEmpty(unusedKeysFilePath)) {
			ASF.ArchiLogger.LogNullError(unusedKeysFilePath);

			return false;
		}

		if (File.Exists(unusedKeysFilePath)) {
			try {
				File.Delete(unusedKeysFilePath);
			} catch (Exception e) {
				ArchiLogger.LogGenericException(e);

				return false;
			}
		}

		string usedKeysFilePath = GetFilePath(EFileType.KeysToRedeemUsed);

		if (string.IsNullOrEmpty(usedKeysFilePath)) {
			ASF.ArchiLogger.LogNullError(usedKeysFilePath);

			return false;
		}

		if (File.Exists(usedKeysFilePath)) {
			try {
				File.Delete(usedKeysFilePath);
			} catch (Exception e) {
				ArchiLogger.LogGenericException(e);

				return false;
			}
		}

		return true;
	}

	internal static string FormatBotResponse(string response, string botName) {
		ArgumentException.ThrowIfNullOrEmpty(response);
		ArgumentException.ThrowIfNullOrEmpty(botName);

		return $"{Environment.NewLine}<{botName}> {response}";
	}

	internal async Task<(uint PlayableAppID, DateTime IgnoredUntil, bool IgnoredGlobally)> GetAppDataForIdling(uint appID, float hoursPlayed, bool allowRecursiveDiscovery = true, bool optimisticDiscovery = true) {
		ArgumentOutOfRangeException.ThrowIfZero(appID);
		ArgumentOutOfRangeException.ThrowIfNegative(hoursPlayed);

		HashSet<uint>? packageIDs = ASF.GlobalDatabase?.GetPackageIDs(appID, OwnedPackageIDs.Keys);

		if ((packageIDs == null) || (packageIDs.Count == 0)) {
			return (0, DateTime.MaxValue, true);
		}

		if ((hoursPlayed < CardsFarmer.HoursForRefund) && BotConfig.FarmingPreferences.HasFlag(BotConfig.EFarmingPreferences.SkipRefundableGames)) {
			DateTime mostRecent = DateTime.MinValue;

			foreach (uint packageID in packageIDs) {
				if (!OwnedPackageIDs.TryGetValue(packageID, out (EPaymentMethod PaymentMethod, DateTime TimeCreated) packageData)) {
					continue;
				}

				if ((packageData.PaymentMethod > EPaymentMethod.None) && IsRefundable(packageData.PaymentMethod) && (packageData.TimeCreated > mostRecent)) {
					mostRecent = packageData.TimeCreated;
				}
			}

			if (mostRecent > DateTime.MinValue) {
				DateTime playableIn = mostRecent.AddDays(CardsFarmer.DaysForRefund);

				if (playableIn > DateTime.UtcNow) {
					return (0, playableIn, false);
				}
			}
		}

		// Check region restrictions
		if (!string.IsNullOrEmpty(IPCountryCode)) {
			DateTime? regionRestrictedUntil = null;

			DateTime safePlayableBefore = DateTime.UtcNow.AddMonths(-RegionRestrictionPlayableBlockMonths);

			foreach (uint packageID in packageIDs) {
				if (!OwnedPackageIDs.TryGetValue(packageID, out (EPaymentMethod PaymentMethod, DateTime TimeCreated) ownedPackageData)) {
					// We don't own that packageID, keep checking
					continue;
				}

				if (ownedPackageData.TimeCreated < safePlayableBefore) {
					// Our package is older than required, this is playable
					regionRestrictedUntil = null;

					break;
				}

				// We've got a package that was activated recently, we should check if we have any playable restrictions on it
				if ((ASF.GlobalDatabase == null) || !ASF.GlobalDatabase.PackagesDataReadOnly.TryGetValue(packageID, out PackageData? packageData)) {
					// No information about that package, try again later
					return (0, DateTime.MaxValue, true);
				}

				if ((packageData.ProhibitRunInCountries == null) || packageData.ProhibitRunInCountries.IsEmpty) {
					// No restrictions, we're good to go
					regionRestrictedUntil = null;

					break;
				}

				if (packageData.ProhibitRunInCountries.Contains(IPCountryCode)) {
					// We are restricted by this package, we can only be saved by another package that is not restricted
					DateTime regionRestrictedUntilPackage = ownedPackageData.TimeCreated.AddMonths(RegionRestrictionPlayableBlockMonths);

					if (!regionRestrictedUntil.HasValue || (regionRestrictedUntilPackage < regionRestrictedUntil.Value)) {
						regionRestrictedUntil = regionRestrictedUntilPackage;
					}
				}
			}

			if (regionRestrictedUntil.HasValue) {
				// We can't play this game for now
				ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningRegionRestrictedPackage, appID, IPCountryCode, regionRestrictedUntil.Value));

				return (0, regionRestrictedUntil.Value, false);
			}
		}

		SteamApps.PICSTokensCallback? tokenCallback = null;

		for (byte i = 0; (i < WebBrowser.MaxTries) && (tokenCallback == null) && IsConnectedAndLoggedOn; i++) {
			try {
				tokenCallback = await SteamApps.PICSGetAccessTokens(appID, null).ToLongRunningTask().ConfigureAwait(false);
			} catch (Exception e) {
				ArchiLogger.LogGenericWarningException(e);
			}
		}

		if (tokenCallback == null) {
			return (optimisticDiscovery ? appID : 0, DateTime.MinValue, true);
		}

		SteamApps.PICSRequest request = new(appID, tokenCallback.AppTokens.GetValueOrDefault(appID));

		AsyncJobMultiple<SteamApps.PICSProductInfoCallback>.ResultSet? productInfoResultSet = null;

		for (byte i = 0; (i < WebBrowser.MaxTries) && (productInfoResultSet == null) && IsConnectedAndLoggedOn; i++) {
			try {
				productInfoResultSet = await SteamApps.PICSGetProductInfo(request.ToEnumerable(), []).ToLongRunningTask().ConfigureAwait(false);
			} catch (Exception e) {
				ArchiLogger.LogGenericWarningException(e);
			}
		}

		if (productInfoResultSet?.Results == null) {
			return (optimisticDiscovery ? appID : 0, DateTime.MinValue, true);
		}

		foreach (Dictionary<uint, SteamApps.PICSProductInfoCallback.PICSProductInfo> productInfoApps in productInfoResultSet.Results.Select(static result => result.Apps)) {
			if (!productInfoApps.TryGetValue(appID, out SteamApps.PICSProductInfoCallback.PICSProductInfo? productInfoApp)) {
				continue;
			}

			KeyValue productInfo = productInfoApp.KeyValues;

			if (productInfo == KeyValue.Invalid) {
				ArchiLogger.LogNullError(productInfo);

				break;
			}

			KeyValue commonProductInfo = productInfo["common"];

			if (commonProductInfo == KeyValue.Invalid) {
				continue;
			}

			string? releaseState = commonProductInfo["ReleaseState"].AsString();

			if (!string.IsNullOrEmpty(releaseState)) {
				// We must convert this to uppercase, since Valve doesn't stick to any convention and we can have a case mismatch
				switch (releaseState.ToUpperInvariant()) {
					case "RELEASED":
						break;
					case "PRELOADONLY" or "PRERELEASE":
						return (0, DateTime.MaxValue, true);
					default:
						ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(releaseState), releaseState));

						break;
				}
			}

			string? type = commonProductInfo["type"].AsString();

			if (string.IsNullOrEmpty(type)) {
				return (appID, DateTime.MinValue, true);
			}

			// We must convert this to uppercase, since Valve doesn't stick to any convention and we can have a case mismatch
			switch (type.ToUpperInvariant()) {
				case "APPLICATION" or "EPISODE" or "GAME" or "MOD" or "MOVIE" or "SERIES" or "TOOL" or "VIDEO":
					// Types that can be idled
					return (appID, DateTime.MinValue, true);
				case "ADVERTISING" or "DEMO" or "DLC" or "GUIDE" or "HARDWARE" or "MUSIC":
					// Types that can't be idled
					break;
				default:
					ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(type), type));

					break;
			}

			if (!allowRecursiveDiscovery) {
				return (0, DateTime.MinValue, true);
			}

			string? listOfDlc = productInfo["extended"]["listofdlc"].AsString();

			if (string.IsNullOrEmpty(listOfDlc)) {
				return (appID, DateTime.MinValue, true);
			}

			string[] dlcAppIDsTexts = listOfDlc.Split(SharedInfo.ListElementSeparators, StringSplitOptions.RemoveEmptyEntries);

			foreach (string dlcAppIDsText in dlcAppIDsTexts) {
				if (!uint.TryParse(dlcAppIDsText, out uint dlcAppID) || (dlcAppID == 0)) {
					ArchiLogger.LogNullError(dlcAppID);

					break;
				}

				(uint playableAppID, _, _) = await GetAppDataForIdling(dlcAppID, hoursPlayed, false, false).ConfigureAwait(false);

				if (playableAppID != 0) {
					return (playableAppID, DateTime.MinValue, true);
				}
			}

			return (appID, DateTime.MinValue, true);
		}

		return (productInfoResultSet is { Complete: true, Failed: false } || optimisticDiscovery ? appID : 0, DateTime.MinValue, true);
	}

	internal static Bot? GetDefaultBot() {
		if ((Bots == null) || Bots.IsEmpty) {
			return null;
		}

		if (!string.IsNullOrEmpty(ASF.GlobalConfig?.DefaultBot) && Bots.TryGetValue(ASF.GlobalConfig.DefaultBot, out Bot? targetBot)) {
			return targetBot;
		}

		return Bots.OrderBy(static bot => bot.Key, BotsComparer).Select(static bot => bot.Value).FirstOrDefault();
	}

	internal Task<HashSet<uint>?> GetMarketableAppIDs() => ArchiWebHandler.GetAppList();

	internal async Task<Dictionary<uint, PackageData>?> GetPackagesData(IReadOnlyCollection<uint> packageIDs) {
		if ((packageIDs == null) || (packageIDs.Count == 0)) {
			throw new ArgumentNullException(nameof(packageIDs));
		}

		if (ASF.GlobalDatabase == null) {
			throw new InvalidOperationException(nameof(ASF.GlobalDatabase));
		}

		HashSet<SteamApps.PICSRequest> packageRequests = [];

		foreach (uint packageID in packageIDs) {
			if (!ASF.GlobalDatabase.PackageAccessTokensReadOnly.TryGetValue(packageID, out ulong packageAccessToken)) {
				continue;
			}

			packageRequests.Add(new SteamApps.PICSRequest(packageID, packageAccessToken));
		}

		if (packageRequests.Count == 0) {
			return new Dictionary<uint, PackageData>(0);
		}

		AsyncJobMultiple<SteamApps.PICSProductInfoCallback>.ResultSet? productInfoResultSet = null;

		for (byte i = 0; (i < WebBrowser.MaxTries) && (productInfoResultSet == null) && IsConnectedAndLoggedOn; i++) {
			try {
				productInfoResultSet = await SteamApps.PICSGetProductInfo([], packageRequests).ToLongRunningTask().ConfigureAwait(false);
			} catch (Exception e) {
				ArchiLogger.LogGenericWarningException(e);
			}
		}

		if (productInfoResultSet?.Results == null) {
			return null;
		}

		DateTime validUntil = DateTime.UtcNow.AddDays(7);

		Dictionary<uint, PackageData> result = new();

		foreach (SteamApps.PICSProductInfoCallback.PICSProductInfo productInfo in productInfoResultSet.Results.SelectMany(static productInfoResult => productInfoResult.Packages).Where(static productInfoPackages => productInfoPackages.Key != 0).Select(static productInfoPackages => productInfoPackages.Value)) {
			if (productInfo.KeyValues == KeyValue.Invalid) {
				ArchiLogger.LogNullError(productInfo);

				continue;
			}

			uint changeNumber = productInfo.ChangeNumber;

			HashSet<uint>? appIDs = null;

			KeyValue appIDsKv = productInfo.KeyValues["appids"];

			if (appIDsKv != KeyValue.Invalid) {
				appIDs = new HashSet<uint>(appIDsKv.Children.Count);

				foreach (string? appIDText in appIDsKv.Children.Select(static app => app.Value)) {
					if (!uint.TryParse(appIDText, out uint appID) || (appID == 0)) {
						ArchiLogger.LogNullError(appID);

						continue;
					}

					appIDs.Add(appID);
				}
			}

			string[]? prohibitRunInCountries = null;

			string? prohibitRunInCountriesText = productInfo.KeyValues["extended"]["prohibitrunincountries"].AsString();

			if (!string.IsNullOrEmpty(prohibitRunInCountriesText)) {
				prohibitRunInCountries = prohibitRunInCountriesText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			}

			result[productInfo.ID] = new PackageData(changeNumber, validUntil, appIDs?.ToImmutableHashSet(), prohibitRunInCountries?.ToImmutableHashSet(StringComparer.Ordinal));
		}

		return result;
	}

	internal async Task<(Dictionary<string, string>? UnusedKeys, Dictionary<string, string>? UsedKeys)> GetUsedAndUnusedKeys() {
		string[] files = [GetFilePath(EFileType.KeysToRedeemUnused), GetFilePath(EFileType.KeysToRedeemUsed)];

		IList<Dictionary<string, string>?> results = await Utilities.InParallel(files.Select(GetKeysFromFile)).ConfigureAwait(false);

		return (results[0], results[1]);
	}

	internal async Task<bool?> HasPublicInventory() {
		if (!IsConnectedAndLoggedOn) {
			return null;
		}

		CPrivacySettings? privacySettings = await ArchiHandler.GetPrivacySettings().ConfigureAwait(false);

		if (privacySettings == null) {
			ArchiLogger.LogGenericWarning(Strings.WarningFailed);

			return null;
		}

		return ((ArchiHandler.EPrivacySetting) privacySettings.privacy_state == ArchiHandler.EPrivacySetting.Public) && ((ArchiHandler.EPrivacySetting) privacySettings.privacy_state_inventory == ArchiHandler.EPrivacySetting.Public);
	}

	internal async Task IdleGame(Game game) {
		ArgumentNullException.ThrowIfNull(game);

		string? gameName = null;

		if (!string.IsNullOrEmpty(BotConfig.CustomGamePlayedWhileFarming)) {
			gameName = string.Format(CultureInfo.CurrentCulture, BotConfig.CustomGamePlayedWhileFarming, game.AppID, game.GameName);
		}

		await ArchiHandler.PlayGames(new HashSet<uint>(1) { game.PlayableAppID }, gameName).ConfigureAwait(false);
	}

	internal async Task IdleGames(IReadOnlyCollection<Game> games) {
		if ((games == null) || (games.Count == 0)) {
			throw new ArgumentNullException(nameof(games));
		}

		string? gameName = null;

		if (!string.IsNullOrEmpty(BotConfig.CustomGamePlayedWhileFarming)) {
			gameName = string.Format(CultureInfo.CurrentCulture, BotConfig.CustomGamePlayedWhileFarming, string.Join(", ", games.Select(static game => game.AppID)), string.Join(", ", games.Select(static game => game.GameName)));
		}

		await ArchiHandler.PlayGames(games.Select(static game => game.PlayableAppID).ToHashSet(), gameName).ConfigureAwait(false);
	}

	internal async Task ImportKeysToRedeem(string filePath) {
		ArgumentException.ThrowIfNullOrEmpty(filePath);

		if (!File.Exists(filePath)) {
			throw new FileNotFoundException(nameof(filePath), filePath);
		}

		try {
			OrderedDictionary gamesToRedeemInBackground = new();

			using (StreamReader reader = new(filePath)) {
				while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line) {
					if (line.Length == 0) {
						continue;
					}

					// Valid formats:
					// Key (name will be the same as key and replaced from redemption result, if possible)
					// Name + Key (user provides both, if name is equal to key, above logic is used, otherwise name is kept)
					// Name + <Ignored> + Key (BGR output format, we include extra properties in the middle, those are ignored during import)
					string[] parsedArgs = line.Split(DefaultBackgroundKeysRedeemerSeparator, StringSplitOptions.RemoveEmptyEntries);

					if (parsedArgs.Length < 1) {
						ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, line));

						continue;
					}

					string name = parsedArgs[0];
					string key = parsedArgs[^1];

					gamesToRedeemInBackground[key] = name;
				}
			}

			if (gamesToRedeemInBackground.Count > 0) {
				IOrderedDictionary validGamesToRedeemInBackground = ValidateGamesToRedeemInBackground(gamesToRedeemInBackground);

				if (validGamesToRedeemInBackground.Count > 0) {
					AddGamesToRedeemInBackground(validGamesToRedeemInBackground);
				}
			}

			File.Delete(filePath);
		} catch (Exception e) {
			ArchiLogger.LogGenericException(e);
		}
	}

	internal static void Init(StringComparer botsComparer) {
		ArgumentNullException.ThrowIfNull(botsComparer);

		if (Bots != null) {
			throw new InvalidOperationException(nameof(Bots));
		}

		BotsComparer = botsComparer;
		Bots = new ConcurrentDictionary<string, Bot>(botsComparer);
	}

	internal bool IsBlacklistedFromIdling(uint appID) {
		ArgumentOutOfRangeException.ThrowIfZero(appID);

		return BotDatabase.FarmingBlacklistAppIDs.Contains(appID);
	}

	internal bool IsBlacklistedFromTrades(ulong steamID) {
		if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		return BotDatabase.TradingBlacklistSteamIDs.Contains(steamID);
	}

	internal bool IsPriorityIdling(uint appID) {
		ArgumentOutOfRangeException.ThrowIfZero(appID);

		return BotDatabase.FarmingPriorityQueueAppIDs.Contains(appID);
	}

	internal async Task OnConfigChanged(bool deleted) {
		if (deleted) {
			await Destroy().ConfigureAwait(false);

			return;
		}

		string configFile = GetFilePath(EFileType.Config);

		if (string.IsNullOrEmpty(configFile)) {
			throw new InvalidOperationException(nameof(configFile));
		}

		(BotConfig? botConfig, _) = await BotConfig.Load(configFile).ConfigureAwait(false);

		if (botConfig == null) {
			// Invalid config file, we allow user to fix it without destroying the bot right away
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

			// Skip shutdown event as we're actually reinitializing the bot, not fully stopping it
			Stop(true);

			BotConfig = botConfig;

			await InitModules().ConfigureAwait(false);
			InitStart();
		} finally {
			InitializationSemaphore.Release();
		}
	}

	internal async Task OnFarmingFinished(bool farmedSomething) {
		await OnFarmingStopped().ConfigureAwait(false);

		if (BotConfig.FarmingPreferences.HasFlag(BotConfig.EFarmingPreferences.SendOnFarmingFinished) && (BotConfig.LootableTypes.Count > 0) && (farmedSomething || !FirstTradeSent)) {
			FirstTradeSent = true;

			await Actions.SendInventory(filterFunction: item => BotConfig.LootableTypes.Contains(item.Type)).ConfigureAwait(false);
		}

		if (BotConfig.FarmingPreferences.HasFlag(BotConfig.EFarmingPreferences.ShutdownOnFarmingFinished)) {
			Stop();
		}

		await PluginsCore.OnBotFarmingFinished(this, farmedSomething).ConfigureAwait(false);
	}

	internal async Task OnFarmingStopped() {
		await ResetGamesPlayed().ConfigureAwait(false);
		await PluginsCore.OnBotFarmingStopped(this).ConfigureAwait(false);
	}

	internal async Task<bool> RefreshWebSession(bool force = false) {
		if (!IsConnectedAndLoggedOn) {
			return false;
		}

		await RefreshWebSessionSemaphore.WaitAsync().ConfigureAwait(false);

		try {
			if (!IsConnectedAndLoggedOn) {
				return false;
			}

			DateTime minimumValidUntil = DateTime.UtcNow.AddMinutes(MinimumAccessTokenValidityMinutes);

			if (!force && !string.IsNullOrEmpty(AccessToken) && (!AccessTokenValidUntil.HasValue || (AccessTokenValidUntil.Value >= minimumValidUntil))) {
				// We can use the tokens we already have
				if (await ArchiWebHandler.Init(SteamID, SteamClient.Universe, AccessToken, SteamParentalActive ? BotConfig.SteamParentalCode : null).ConfigureAwait(false)) {
					InitRefreshTokensTimer(AccessTokenValidUntil ?? minimumValidUntil);

					return true;
				}
			}

			// We need to refresh our session, access token is no longer valid
			BotDatabase.AccessToken = AccessToken = null;

			if (string.IsNullOrEmpty(RefreshToken)) {
				// Without refresh token we can't get fresh access tokens, relog needed
				await Connect(true).ConfigureAwait(false);

				return false;
			}

			AccessTokenGenerateResult response;

			try {
				response = await SteamClient.Authentication.GenerateAccessTokenForAppAsync(SteamID, RefreshToken, true).ConfigureAwait(false);
			} catch (Exception e) {
				// The request has failed, in almost all cases this means our refresh token is no longer valid, relog needed
				ArchiLogger.LogGenericWarningException(e);

				BotDatabase.RefreshToken = RefreshToken = null;

				await Connect(true).ConfigureAwait(false);

				return false;
			}

			if (string.IsNullOrEmpty(response.AccessToken)) {
				// The request has failed, in almost all cases this means our refresh token is no longer valid, relog needed
				BotDatabase.RefreshToken = RefreshToken = null;

				ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, nameof(SteamClient.Authentication.GenerateAccessTokenForAppAsync)));

				await Connect(true).ConfigureAwait(false);

				return false;
			}

			UpdateTokens(response.AccessToken, response.RefreshToken);

			if (await ArchiWebHandler.Init(SteamID, SteamClient.Universe, response.AccessToken, SteamParentalActive ? BotConfig.SteamParentalCode : null).ConfigureAwait(false)) {
				InitRefreshTokensTimer(AccessTokenValidUntil ?? minimumValidUntil);

				return true;
			}

			// We got the tokens, but failed to authorize? Purge them just to be sure and reconnect
			BotDatabase.AccessToken = AccessToken = null;

			await Connect(true).ConfigureAwait(false);

			return false;
		} finally {
			RefreshWebSessionSemaphore.Release();
		}
	}

	internal static async Task RegisterBot(string botName) {
		ArgumentException.ThrowIfNullOrEmpty(botName);

		if (Bots == null) {
			throw new InvalidOperationException(nameof(Bots));
		}

		if (Bots.ContainsKey(botName)) {
			return;
		}

		string configFilePath = GetFilePath(botName, EFileType.Config);

		if (string.IsNullOrEmpty(configFilePath)) {
			ASF.ArchiLogger.LogNullError(configFilePath);

			return;
		}

		(BotConfig? botConfig, string? latestJson) = await BotConfig.Load(configFilePath).ConfigureAwait(false);

		if (botConfig == null) {
			ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.ErrorBotConfigInvalid, configFilePath));

			return;
		}

		if (Debugging.IsDebugConfigured) {
			ASF.ArchiLogger.LogGenericDebug($"{configFilePath}: {botConfig.ToJsonText(true)}");
		}

		if (!string.IsNullOrEmpty(latestJson)) {
			ASF.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.AutomaticFileMigration, configFilePath));

			await SerializableFile.Write(configFilePath, latestJson).ConfigureAwait(false);

			ASF.ArchiLogger.LogGenericInfo(Strings.Done);
		}

		string databaseFilePath = GetFilePath(botName, EFileType.Database);

		if (string.IsNullOrEmpty(databaseFilePath)) {
			ASF.ArchiLogger.LogNullError(databaseFilePath);

			return;
		}

		BotDatabase? botDatabase = await BotDatabase.CreateOrLoad(databaseFilePath).ConfigureAwait(false);

		if (botDatabase == null) {
			ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.ErrorDatabaseInvalid, databaseFilePath));

			return;
		}

		if (Debugging.IsDebugConfigured) {
			ASF.ArchiLogger.LogGenericDebug($"{databaseFilePath}: {botDatabase.ToJsonText(true)}");
		}

		botDatabase.PerformMaintenance();

		Bot bot;

		await BotsSemaphore.WaitAsync().ConfigureAwait(false);

		try {
			if (Bots.ContainsKey(botName)) {
				return;
			}

			bot = new Bot(botName, botConfig, botDatabase);

			if (!Bots.TryAdd(botName, bot)) {
				ASF.ArchiLogger.LogNullError(bot);

				await bot.DisposeAsync().ConfigureAwait(false);

				return;
			}
		} finally {
			BotsSemaphore.Release();
		}

		await PluginsCore.OnBotInit(bot).ConfigureAwait(false);

		HashSet<ClientMsgHandler>? customHandlers = await PluginsCore.OnBotSteamHandlersInit(bot).ConfigureAwait(false);

		if (customHandlers?.Count > 0) {
			foreach (ClientMsgHandler customHandler in customHandlers) {
				bot.SteamClient.AddHandler(customHandler);
			}
		}

		await PluginsCore.OnBotSteamCallbacksInit(bot, bot.CallbackManager).ConfigureAwait(false);

		await bot.InitModules().ConfigureAwait(false);

		bot.InitStart();
	}

	internal (bool Success, string? Message) RemoveAuthenticator() {
		MobileAuthenticator? authenticator = BotDatabase.MobileAuthenticator;

		if (authenticator == null) {
			return (false, Strings.BotNoASFAuthenticator);
		}

		BotDatabase.MobileAuthenticator = null;
		authenticator.Dispose();

		return (true, null);
	}

	internal async Task<bool> Rename(string newBotName) {
		ArgumentException.ThrowIfNullOrEmpty(newBotName);

		if (Bots == null) {
			throw new InvalidOperationException(nameof(Bots));
		}

		if (!ASF.IsValidBotName(newBotName) || Bots.ContainsKey(newBotName)) {
			return false;
		}

		if (KeepRunning) {
			Stop(true);
		}

		await BotDatabase.MakeReadOnly().ConfigureAwait(false);

		// We handle the config file last as it'll trigger new bot creation
		foreach ((string filePath, EFileType fileType) in RelatedFiles.Where(static file => File.Exists(file.FilePath)).OrderByDescending(static file => file.FileType != EFileType.Config)) {
			string newFilePath = GetFilePath(newBotName, fileType);

			if (string.IsNullOrEmpty(newFilePath)) {
				ArchiLogger.LogNullError(newFilePath);

				return false;
			}

			try {
				File.Move(filePath, newFilePath);
			} catch (Exception e) {
				ArchiLogger.LogGenericException(e);

				return false;
			}
		}

		return true;
	}

	internal async Task<string?> RequestInput(ASF.EUserInputType inputType, bool previousCodeWasIncorrect) {
		if ((inputType == ASF.EUserInputType.None) || !Enum.IsDefined(inputType)) {
			throw new InvalidEnumArgumentException(nameof(inputType), (int) inputType, typeof(ASF.EUserInputType));
		}

		switch (inputType) {
			case ASF.EUserInputType.SteamGuard when !string.IsNullOrEmpty(AuthCode):
				string? savedAuthCode = AuthCode;

				AuthCode = null;

				return savedAuthCode;
			case ASF.EUserInputType.TwoFactorAuthentication when !string.IsNullOrEmpty(TwoFactorCode):
				string? savedTwoFactorCode = TwoFactorCode;

				TwoFactorCode = null;

				return savedTwoFactorCode;
			case ASF.EUserInputType.TwoFactorAuthentication when BotDatabase.MobileAuthenticator != null:
				if (previousCodeWasIncorrect) {
					// There is a possibility that our cached time is no longer appropriate, so we should reset the cache in this case in order to fetch it upon the next login attempt
					// Yes, this might as well be just invalid 2FA credentials, but we can't be sure about that, and we have LoginFailures designed to verify that for us
					await MobileAuthenticator.ResetSteamTimeDifference().ConfigureAwait(false);
				}

				string? generatedTwoFactorCode = await BotDatabase.MobileAuthenticator.GenerateToken().ConfigureAwait(false);

				if (!string.IsNullOrEmpty(generatedTwoFactorCode)) {
					return generatedTwoFactorCode;
				}

				break;
		}

		RequiredInput = inputType;

		string? input = await Logging.GetUserInput(inputType, BotName).ConfigureAwait(false);

		if (string.IsNullOrEmpty(input) || !SetUserInput(inputType, input)) {
			ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, nameof(input)));

			Stop();

			return null;
		}

		// We keep user input set in case we need to use it again due to disconnection, OnLoggedOn() will reset it for us
		return input;
	}

	internal void RequestPersonaStateUpdate() {
		if (!IsConnectedAndLoggedOn) {
			return;
		}

		SteamFriends.RequestFriendInfo(SteamID, EClientPersonaStateFlag.PlayerName | EClientPersonaStateFlag.Presence);
	}

	internal void ResetPersonaState() {
		if (BotConfig.OnlineStatus == EPersonaState.Offline) {
			return;
		}

		SteamFriends.SetPersonaState(BotConfig.OnlineStatus);

		if (BotConfig.OnlineFlags > 0) {
			ArchiHandler.SetPersonaState(BotConfig.OnlineStatus, BotConfig.OnlineFlags);
		}
	}

	internal async Task<bool> SendTypingMessage(ulong steamID) {
		if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		if (!IsConnectedAndLoggedOn) {
			return false;
		}

		return await ArchiHandler.SendTypingStatus(steamID).ConfigureAwait(false) == EResult.OK;
	}

	internal async Task Start() {
		if (KeepRunning) {
			return;
		}

		KeepRunning = true;
		Utilities.InBackground(HandleCallbacks, true);
		ArchiLogger.LogGenericInfo(Strings.Starting);

		// Support and convert 2FA files
		if (!HasMobileAuthenticator) {
			string mobileAuthenticatorFilePath = GetFilePath(EFileType.MobileAuthenticator);

			if (string.IsNullOrEmpty(mobileAuthenticatorFilePath)) {
				ArchiLogger.LogNullError(mobileAuthenticatorFilePath);

				return;
			}

			if (File.Exists(mobileAuthenticatorFilePath)) {
				await ImportAuthenticatorFromFile(mobileAuthenticatorFilePath).ConfigureAwait(false);
			}
		}

		string keysToRedeemFilePath = GetFilePath(EFileType.KeysToRedeem);

		if (string.IsNullOrEmpty(keysToRedeemFilePath)) {
			ArchiLogger.LogNullError(keysToRedeemFilePath);

			return;
		}

		if (File.Exists(keysToRedeemFilePath)) {
			await ImportKeysToRedeem(keysToRedeemFilePath).ConfigureAwait(false);
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

	internal bool TryImportAuthenticator(MobileAuthenticator authenticator) {
		ArgumentNullException.ThrowIfNull(authenticator);

		if (HasMobileAuthenticator) {
			return false;
		}

		authenticator.Init(this);
		BotDatabase.MobileAuthenticator = authenticator;

		ArchiLogger.LogGenericInfo(Strings.BotAuthenticatorImportFinished);

		return true;
	}

	internal static IOrderedDictionary ValidateGamesToRedeemInBackground(IOrderedDictionary gamesToRedeemInBackground) {
		if ((gamesToRedeemInBackground == null) || (gamesToRedeemInBackground.Count == 0)) {
			throw new ArgumentNullException(nameof(gamesToRedeemInBackground));
		}

		HashSet<object> invalidKeys = gamesToRedeemInBackground.Cast<DictionaryEntry>().Where(static game => !BotDatabase.IsValidGameToRedeemInBackground(game)).Select(static game => game.Key).ToHashSet();

		foreach (object invalidKey in invalidKeys) {
			gamesToRedeemInBackground.Remove(invalidKey);
		}

		return gamesToRedeemInBackground;
	}

	private async Task Connect(bool force = false) {
		if (!force && (!KeepRunning || SteamClient.IsConnected)) {
			return;
		}

		await LimitLoginRequestsAsync().ConfigureAwait(false);

		if (!force && (!KeepRunning || SteamClient.IsConnected)) {
			return;
		}

		LastLogOnResult = EResult.Invalid;
		ReconnectOnUserInitiated = false;

		ArchiLogger.LogGenericInfo(Strings.BotConnecting);
		InitConnectionFailureTimer();
		SteamClient.Connect();
	}

	private async Task Destroy(bool force = false) {
		if (Bots == null) {
			throw new InvalidOperationException(nameof(Bots));
		}

		if (KeepRunning) {
			if (!force) {
				Stop();
			} else {
				// Stop() will most likely block due to connection freeze, don't wait for it
				Utilities.InBackground(() => Stop());
			}
		}

		Bots.TryRemove(BotName, out _);
		await PluginsCore.OnBotDestroy(this).ConfigureAwait(false);
	}

	private void Disconnect() {
		StopConnectionFailureTimer();

		LastLogOnResult = EResult.OK;
		ReconnectOnUserInitiated = false;

		SteamClient.Disconnect();
	}

	private async Task<Dictionary<string, string>?> GetKeysFromFile(string filePath) {
		ArgumentException.ThrowIfNullOrEmpty(filePath);

		if (!File.Exists(filePath)) {
			return new Dictionary<string, string>(0, StringComparer.Ordinal);
		}

		Dictionary<string, string> keys = new(StringComparer.Ordinal);

		try {
			using StreamReader reader = new(filePath);

			while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line) {
				if (line.Length == 0) {
					continue;
				}

				string[] parsedArgs = line.Split(DefaultBackgroundKeysRedeemerSeparator, StringSplitOptions.RemoveEmptyEntries);

				if (parsedArgs.Length < 3) {
					ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, line));

					continue;
				}

				string key = parsedArgs[^1];

				if (!Utilities.IsValidCdKey(key)) {
					ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, key));

					continue;
				}

				string name = parsedArgs[0];
				keys[key] = name;
			}
		} catch (Exception e) {
			ArchiLogger.LogGenericException(e);

			return null;
		}

		return keys;
	}

	private async Task<HashSet<uint>?> GetPossiblyCompletedBadgeAppIDs(byte page) {
		ArgumentOutOfRangeException.ThrowIfZero(page);

		using IDocument? badgePage = await ArchiWebHandler.GetBadgePage(page).ConfigureAwait(false);

		if (badgePage == null) {
			ArchiLogger.LogGenericWarning(Strings.WarningCouldNotCheckBadges);

			return null;
		}

		return GetPossiblyCompletedBadgeAppIDs(badgePage);
	}

	private HashSet<uint>? GetPossiblyCompletedBadgeAppIDs(IDocument badgePage) {
		ArgumentNullException.ThrowIfNull(badgePage);

		// We select badges that are ready to craft, as well as those that are already crafted to a maximum level, as those will not display with a craft button
		// Level 5 is maximum level for card badges according to https://steamcommunity.com/tradingcards/faq
		IEnumerable<IAttr> linkElements = badgePage.SelectNodes<IAttr>("//a[@class='badge_craft_button']/@href | //div[@class='badges_sheet']/div[contains(@class, 'badge_row') and .//div[@class='badge_info_description']/div[contains(text(), 'Level 5')]]/a[@class='badge_row_overlay']/@href");

		HashSet<uint> result = [];

		foreach (string badgeUri in linkElements.Select(static htmlNode => htmlNode.Value)) {
			if (string.IsNullOrEmpty(badgeUri)) {
				ArchiLogger.LogNullError(badgeUri);

				return null;
			}

			// URIs to foil badges are the same as for normal badges except they end with "?border=1"
			string appIDText = badgeUri.Split('?', StringSplitOptions.RemoveEmptyEntries)[0].Split('/', StringSplitOptions.RemoveEmptyEntries)[^1];

			if (!uint.TryParse(appIDText, out uint appID) || (appID == 0)) {
				ArchiLogger.LogNullError(appID);

				return null;
			}

			result.Add(appID);
		}

		return result;
	}

	private async Task HandleCallbacks() {
		if (!await CallbackSemaphore.WaitAsync(CallbackSleep).ConfigureAwait(false)) {
			if (Debugging.IsUserDebugging) {
				ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, nameof(CallbackSemaphore)));
			}

			return;
		}

		try {
			TimeSpan timeSpan = TimeSpan.FromMilliseconds(CallbackSleep);

			while (KeepRunning || SteamClient.IsConnected) {
				CallbackManager.RunWaitAllCallbacks(timeSpan);
			}
		} catch (Exception e) {
			ArchiLogger.LogGenericException(e);
		} finally {
			CallbackSemaphore.Release();
		}
	}

	private async Task HandleLoginResult(EResult result, EResult extendedResult) {
		if (!Enum.IsDefined(result)) {
			throw new InvalidEnumArgumentException(nameof(result), (int) result, typeof(EResult));
		}

		if (!Enum.IsDefined(extendedResult)) {
			throw new InvalidEnumArgumentException(nameof(extendedResult), (int) extendedResult, typeof(EResult));
		}

		// Keep LastLogOnResult for OnDisconnected()
		LastLogOnResult = result > EResult.OK ? result : EResult.Invalid;

		HeartBeatFailures = 0;
		StopConnectionFailureTimer();

		switch (result) {
			case EResult.AccountDisabled:
				// Those failures are permanent, we should Stop() the bot if any of those happen
				ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.BotUnableToLogin, result, extendedResult));

				Stop();

				break;
			case EResult.AccessDenied when string.IsNullOrEmpty(RefreshToken) && (++LoginFailures >= MaxLoginFailures):
			case EResult.InvalidPassword when string.IsNullOrEmpty(RefreshToken) && (++LoginFailures >= MaxLoginFailures):
				// Likely permanently wrong account credentials
				LoginFailures = 0;

				ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.BotInvalidPasswordDuringLogin, MaxLoginFailures));

				Stop();

				break;
			case EResult.AccountLoginDeniedNeedTwoFactor when HasMobileAuthenticator && (++LoginFailures >= MaxLoginFailures):
			case EResult.TwoFactorCodeMismatch when HasMobileAuthenticator && (++LoginFailures >= MaxLoginFailures):
				// Likely permanently wrong 2FA credentials that provide automatic TwoFactorAuthentication input
				LoginFailures = 0;

				ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.BotInvalidAuthenticatorDuringLogin, MaxLoginFailures));

				Stop();

				break;
			case EResult.AccountLoginDeniedNeedTwoFactor when HasMobileAuthenticator:
			case EResult.TwoFactorCodeMismatch when HasMobileAuthenticator:
				// Automatic TwoFactorAuthentication input provided
				ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.BotUnableToLogin, result, extendedResult));

				// There is a possibility that our cached time is no longer appropriate, so we should reset the cache in this case in order to fetch it upon the next login attempt
				// Yes, this might as well be just invalid 2FA credentials, but we can't be sure about that, and we have LoginFailures designed to verify that for us
				await MobileAuthenticator.ResetSteamTimeDifference().ConfigureAwait(false);

				break;
			case EResult.AccountLogonDenied:
			case EResult.InvalidLoginAuthCode:
				// SteamGuard input required
				RequiredInput = ASF.EUserInputType.SteamGuard;

				string? authCode = await Logging.GetUserInput(ASF.EUserInputType.SteamGuard, BotName).ConfigureAwait(false);

				if (string.IsNullOrEmpty(authCode) || !SetUserInput(ASF.EUserInputType.SteamGuard, authCode)) {
					ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, nameof(authCode)));

					Stop();
				}

				break;
			case EResult.AccountLoginDeniedNeedTwoFactor:
			case EResult.TwoFactorCodeMismatch:
				// TwoFactorAuthentication input required
				RequiredInput = ASF.EUserInputType.TwoFactorAuthentication;

				string? twoFactorCode = await Logging.GetUserInput(ASF.EUserInputType.TwoFactorAuthentication, BotName).ConfigureAwait(false);

				if (string.IsNullOrEmpty(twoFactorCode) || !SetUserInput(ASF.EUserInputType.TwoFactorAuthentication, twoFactorCode)) {
					ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, nameof(twoFactorCode)));

					Stop();
				}

				break;
			case EResult.AccessDenied: // Usually means refresh token is no longer authorized to use, otherwise just try again
			case EResult.AccountLoginDeniedThrottle: // Rate-limiting
			case EResult.AlreadyLoggedInElsewhere: // No clue, we might need to handle it differenty but it's so rare it's unknown for now why it happens
			case EResult.Busy: // No clue, might be some internal gateway timeout, just try again
			case EResult.DuplicateRequest: // This will happen if user reacts to popup and tries to use the code afterwards, we have the code saved in ASF, we just need to try again
			case EResult.Expired: // Usually means refresh token is no longer authorized to use, otherwise just try again
			case EResult.FileNotFound: // User denied approval despite telling us that they accepted it, just try again
			case EResult.InvalidPassword: // Usually means refresh token is no longer authorized to use, otherwise just try again
			case EResult.NoConnection: // Usually network issues
			case EResult.PasswordRequiredToKickSession: // Not sure about this one, it seems to be just generic "try again"? #694
			case EResult.RateLimitExceeded: // Rate-limiting
			case EResult.ServiceUnavailable: // Usually Steam maintenance
			case EResult.Timeout: // Usually network issues
			case EResult.TryAnotherCM: // Usually Steam maintenance
				// Generic retry pattern against common/expected problems
				ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.BotUnableToLogin, result, extendedResult));

				break;
			case EResult.OK:
				// Login succeeded
				break;
			default:
				// Unexpected result, shutdown immediately
				ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(result), result));
				ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.BotUnableToLogin, result, extendedResult));
				Stop();

				break;
		}
	}

	private async void HeartBeat(object? state = null) {
		if (!KeepRunning || !IsConnectedAndLoggedOn || (HeartBeatFailures == byte.MaxValue)) {
			return;
		}

		byte connectionTimeout = ASF.GlobalConfig?.ConnectionTimeout ?? GlobalConfig.DefaultConnectionTimeout;

		try {
			if (DateTime.UtcNow.Subtract(ArchiHandler.LastPacketReceived).TotalSeconds > connectionTimeout) {
				await SteamFriends.RequestProfileInfo(SteamID).ToLongRunningTask().ConfigureAwait(false);
			}

			HeartBeatFailures = 0;
		} catch (Exception e) {
			ArchiLogger.LogGenericDebuggingException(e);

			if (!KeepRunning || !IsConnectedAndLoggedOn || (HeartBeatFailures == byte.MaxValue)) {
				return;
			}

			if (++HeartBeatFailures >= (byte) Math.Ceiling(connectionTimeout / 10.0)) {
				HeartBeatFailures = byte.MaxValue;
				ArchiLogger.LogGenericWarning(Strings.BotConnectionLost);
				Utilities.InBackground(() => Connect(true));
			}
		}
	}

	private async Task ImportAuthenticatorFromFile(string maFilePath) {
		if (HasMobileAuthenticator || !File.Exists(maFilePath)) {
			return;
		}

		ArchiLogger.LogGenericInfo(Strings.BotAuthenticatorConverting);

		try {
			string json = await File.ReadAllTextAsync(maFilePath).ConfigureAwait(false);

			if (string.IsNullOrEmpty(json)) {
				ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(json)));

				return;
			}

			MobileAuthenticator? authenticator = json.ToJsonObject<MobileAuthenticator>();

			if (authenticator == null) {
				ArchiLogger.LogNullError(authenticator);

				return;
			}

			if (!TryImportAuthenticator(authenticator)) {
				return;
			}

			File.Delete(maFilePath);
		} catch (Exception e) {
			ArchiLogger.LogGenericException(e);
		}
	}

	private void InitConnectionFailureTimer() {
		if (ConnectionFailureTimer != null) {
			return;
		}

		byte connectionTimeout = ASF.GlobalConfig?.ConnectionTimeout ?? GlobalConfig.DefaultConnectionTimeout;

		ConnectionFailureTimer = new Timer(
			InitPermanentConnectionFailure,
			null,
			TimeSpan.FromMinutes(Math.Ceiling(connectionTimeout / 30.0)), // Delay
			Timeout.InfiniteTimeSpan // Period
		);
	}

	private async Task InitializeFamilySharing() {
		// TODO: Old call should be removed eventually when Steam stops supporting both systems at once
		Task<HashSet<ulong>?> oldFamilySharingSteamIDsTask = ArchiWebHandler.GetFamilySharingSteamIDs();

		HashSet<ulong>? steamIDs = await ArchiHandler.GetFamilyGroupSteamIDs().ConfigureAwait(false);
		HashSet<ulong>? oldSteamIDs = await oldFamilySharingSteamIDsTask.ConfigureAwait(false);

		if ((steamIDs == null) && (oldSteamIDs == null)) {
			return;
		}

		SteamFamilySharingIDs.Clear();

		if (steamIDs is { Count: > 0 }) {
			SteamFamilySharingIDs.UnionWith(steamIDs);
		}

		if (oldSteamIDs is { Count: > 0 }) {
			SteamFamilySharingIDs.UnionWith(oldSteamIDs);
		}
	}

	private async Task<bool> InitLoginAndPassword(bool requiresPassword) {
		if (string.IsNullOrEmpty(BotConfig.SteamLogin)) {
			RequiredInput = ASF.EUserInputType.Login;

			string? steamLogin = await Logging.GetUserInput(ASF.EUserInputType.Login, BotName).ConfigureAwait(false);

			if (string.IsNullOrEmpty(steamLogin) || !SetUserInput(ASF.EUserInputType.Login, steamLogin)) {
				ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, nameof(steamLogin)));

				return false;
			}
		}

		if (requiresPassword) {
			string? decryptedSteamPassword = await BotConfig.GetDecryptedSteamPassword().ConfigureAwait(false);

			if (string.IsNullOrEmpty(decryptedSteamPassword)) {
				RequiredInput = ASF.EUserInputType.Password;

				string? steamPassword = await Logging.GetUserInput(ASF.EUserInputType.Password, BotName).ConfigureAwait(false);

				if (string.IsNullOrEmpty(steamPassword) || !SetUserInput(ASF.EUserInputType.Password, steamPassword)) {
					ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, nameof(steamPassword)));

					return false;
				}
			}
		}

		return true;
	}

	private async Task InitModules() {
		if (Bots == null) {
			throw new InvalidOperationException(nameof(Bots));
		}

		AccountFlags = EAccountFlags.NormalUser;
		AvatarHash = IPCountryCode = Nickname = null;
		MasterChatGroupID = 0;
		RequiredInput = ASF.EUserInputType.None;
		WalletBalance = 0;
		WalletCurrency = ECurrencyCode.Invalid;

		string? accessTokenText = BotDatabase.AccessToken;
		string? refreshTokenText = BotDatabase.RefreshToken;

		if (BotConfig.PasswordFormat.HasTransformation()) {
			if (!string.IsNullOrEmpty(accessTokenText)) {
				accessTokenText = await ArchiCryptoHelper.Decrypt(BotConfig.PasswordFormat, accessTokenText).ConfigureAwait(false);
			}

			if (!string.IsNullOrEmpty(refreshTokenText)) {
				refreshTokenText = await ArchiCryptoHelper.Decrypt(BotConfig.PasswordFormat, refreshTokenText).ConfigureAwait(false);
			}
		}

		if (!string.IsNullOrEmpty(accessTokenText) && Utilities.TryReadJsonWebToken(accessTokenText, out JsonWebToken? accessToken) && ((accessToken.ValidTo == DateTime.MinValue) || (accessToken.ValidTo >= DateTime.UtcNow))) {
			AccessToken = accessTokenText;
		} else {
			AccessToken = null;
		}

		if (!string.IsNullOrEmpty(refreshTokenText) && Utilities.TryReadJsonWebToken(refreshTokenText, out JsonWebToken? refreshToken) && ((refreshToken.ValidTo == DateTime.MinValue) || (refreshToken.ValidTo >= DateTime.UtcNow))) {
			RefreshToken = refreshTokenText;
		} else {
			RefreshToken = null;
		}

		CardsFarmer.SetInitialState(BotConfig.FarmingPreferences.HasFlag(BotConfig.EFarmingPreferences.FarmingPausedByDefault));

		if (SendItemsTimer != null) {
			await SendItemsTimer.DisposeAsync().ConfigureAwait(false);

			SendItemsTimer = null;
		}

		if (SteamSaleEvent != null) {
			await SteamSaleEvent.DisposeAsync().ConfigureAwait(false);

			SteamSaleEvent = null;
		}

		if (TradeCheckTimer != null) {
			await TradeCheckTimer.DisposeAsync().ConfigureAwait(false);

			TradeCheckTimer = null;
		}

		if (BotConfig is { SendTradePeriod: > 0, LootableTypes.Count: > 0 } && BotConfig.SteamUserPermissions.Values.Any(static permission => permission >= BotConfig.EAccess.Master)) {
			SendItemsTimer = new Timer(
				OnSendItemsTimer,
				null,
				TimeSpan.FromHours(BotConfig.SendTradePeriod) + TimeSpan.FromSeconds(ASF.LoadBalancingDelay * Bots.Count), // Delay
				TimeSpan.FromHours(BotConfig.SendTradePeriod) // Period
			);
		}

		if (BotConfig.FarmingPreferences.HasFlag(BotConfig.EFarmingPreferences.AutoSteamSaleEvent)) {
			SteamSaleEvent = new SteamSaleEvent(this);
		}

		if (BotConfig.TradeCheckPeriod > 0) {
			TradeCheckTimer = new Timer(
				OnTradeCheckTimer,
				null,
				TimeSpan.FromMinutes(BotConfig.TradeCheckPeriod) + TimeSpan.FromSeconds(ASF.LoadBalancingDelay * Bots.Count), // Delay
				TimeSpan.FromMinutes(BotConfig.TradeCheckPeriod) // Period
			);
		}

		BotDatabase.MobileAuthenticator?.OnInitModules();

		await PluginsCore.OnBotInitModules(this, BotConfig.AdditionalProperties).ConfigureAwait(false);
	}

	private async void InitPermanentConnectionFailure(object? state = null) {
		if (!KeepRunning) {
			return;
		}

		ArchiLogger.LogGenericWarning(Strings.BotHeartBeatFailed);
		await Destroy(true).ConfigureAwait(false);
		await RegisterBot(BotName).ConfigureAwait(false);
	}

	private void InitPlayingWasBlockedTimer() {
		if (PlayingWasBlockedTimer != null) {
			return;
		}

		byte minFarmingDelayAfterBlock = ASF.GlobalConfig?.MinFarmingDelayAfterBlock ?? GlobalConfig.DefaultMinFarmingDelayAfterBlock;

		PlayingWasBlockedTimer = new Timer(
			ResetPlayingWasBlockedWithTimer,
			null,
			TimeSpan.FromSeconds(minFarmingDelayAfterBlock), // Delay
			Timeout.InfiniteTimeSpan // Period
		);
	}

	private void InitRefreshTokensTimer(DateTime validUntil) {
		ArgumentOutOfRangeException.ThrowIfEqual(validUntil, DateTime.MinValue);

		if (validUntil == DateTime.MaxValue) {
			// OK, tokens do not require refreshing
			StopRefreshTokensTimer();

			return;
		}

		TimeSpan delay = validUntil - DateTime.UtcNow;

		// Start refreshing token before it's invalid
		if (delay.TotalMinutes > MinimumAccessTokenValidityMinutes) {
			delay -= TimeSpan.FromMinutes(MinimumAccessTokenValidityMinutes);
		} else {
			delay = TimeSpan.Zero;
		}

		// Timer can accept only dueTimes up to 2^32 - 2
		uint dueTime = (uint) Math.Min(uint.MaxValue - 1, (ulong) delay.TotalMilliseconds);

		if (RefreshTokensTimer == null) {
			RefreshTokensTimer = new Timer(
				OnRefreshTokensTimer,
				null,
				TimeSpan.FromMilliseconds(dueTime), // Delay
				TimeSpan.FromMinutes(1) // Period
			);
		} else {
			RefreshTokensTimer.Change(TimeSpan.FromMilliseconds(dueTime), TimeSpan.FromMinutes(1));
		}
	}

	private void InitStart() {
		if (!BotConfig.Enabled) {
			ArchiLogger.LogGenericWarning(Strings.BotInstanceNotStartingBecauseDisabled);

			return;
		}

		// Start
		Utilities.InBackground(Start);
	}

	private bool IsMasterClanID(ulong steamID) {
		if ((steamID == 0) || !new SteamID(steamID).IsClanAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		return steamID == BotConfig.SteamMasterClanID;
	}

	private static bool IsRefundable(EPaymentMethod paymentMethod) {
		if (paymentMethod == EPaymentMethod.None) {
			throw new ArgumentOutOfRangeException(nameof(paymentMethod));
		}

#pragma warning disable CA2248 // This is actually a fair warning, EPaymentMethod is not a flags enum on itself, but there is nothing we can do about Steam using it like that here
		return paymentMethod switch {
			EPaymentMethod.ActivationCode => false,
			EPaymentMethod.Complimentary => false,
			EPaymentMethod.HardwarePromo => false,
			_ => !paymentMethod.HasFlag(EPaymentMethod.Complimentary) // Complimentary can also be a flag
		};
#pragma warning restore CA2248 // This is actually a fair warning, EPaymentMethod is not a flags enum on itself, but there is nothing we can do about Steam using it like that here
	}

	private async Task JoinMasterChatGroupID() {
		if ((BotConfig.SteamMasterClanID == 0) || IsAccountLimited) {
			return;
		}

		if (MasterChatGroupID == 0) {
			CClanChatRooms_GetClanChatRoomInfo_Response? clanChatRoomInfo = await ArchiHandler.GetClanChatRoomInfo(BotConfig.SteamMasterClanID).ConfigureAwait(false);

			if ((clanChatRoomInfo == null) || (clanChatRoomInfo.chat_group_summary.chat_group_id == 0)) {
				return;
			}

			MasterChatGroupID = clanChatRoomInfo.chat_group_summary.chat_group_id;
		}

		HashSet<ulong>? chatGroupIDs = await ArchiHandler.GetMyChatGroupIDs().ConfigureAwait(false);

		if (chatGroupIDs?.Contains(MasterChatGroupID) != false) {
			return;
		}

		if (!await ArchiHandler.JoinChatRoomGroup(MasterChatGroupID).ConfigureAwait(false)) {
			ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, nameof(ArchiHandler.JoinChatRoomGroup)));
		}
	}

	private static async Task LimitLoginRequestsAsync() {
		if (ASF.LoginSemaphore == null) {
			ASF.ArchiLogger.LogNullError(ASF.LoginSemaphore);

			return;
		}

		if (ASF.LoginRateLimitingSemaphore == null) {
			ASF.ArchiLogger.LogNullError(ASF.LoginRateLimitingSemaphore);

			return;
		}

		byte loginLimiterDelay = ASF.GlobalConfig?.LoginLimiterDelay ?? GlobalConfig.DefaultLoginLimiterDelay;

		if (loginLimiterDelay == 0) {
			await ASF.LoginRateLimitingSemaphore.WaitAsync().ConfigureAwait(false);
			ASF.LoginRateLimitingSemaphore.Release();

			return;
		}

		await ASF.LoginSemaphore.WaitAsync().ConfigureAwait(false);

		try {
			await ASF.LoginRateLimitingSemaphore.WaitAsync().ConfigureAwait(false);
			ASF.LoginRateLimitingSemaphore.Release();
		} finally {
			Utilities.InBackground(
				async () => {
					await Task.Delay(loginLimiterDelay * 1000).ConfigureAwait(false);
					ASF.LoginSemaphore.Release();
				}
			);
		}
	}

	private async void OnConnected(SteamClient.ConnectedCallback callback) {
		ArgumentNullException.ThrowIfNull(callback);

		HeartBeatFailures = 0;
		ReconnectOnUserInitiated = false;
		StopConnectionFailureTimer();

		ArchiLogger.LogGenericInfo(Strings.BotConnected);

		if (!KeepRunning) {
			ArchiLogger.LogGenericInfo(Strings.BotDisconnecting);
			Disconnect();

			return;
		}

		if (!await InitLoginAndPassword(string.IsNullOrEmpty(RefreshToken)).ConfigureAwait(false)) {
			Stop();

			return;
		}

		if (string.IsNullOrEmpty(BotConfig.SteamLogin)) {
			throw new InvalidOperationException(nameof(BotConfig.SteamLogin));
		}

		// Steam login and password fields can contain ASCII characters only, including spaces
		string username = GeneratedRegexes.NonAscii().Replace(BotConfig.SteamLogin, "");

		if (string.IsNullOrEmpty(username)) {
			ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, nameof(BotConfig.SteamLogin)));

			Stop();

			return;
		}

		string? password = await BotConfig.GetDecryptedSteamPassword().ConfigureAwait(false);

		if (!string.IsNullOrEmpty(password)) {
			password = GeneratedRegexes.NonAscii().Replace(password, "");

			if (string.IsNullOrEmpty(password)) {
				ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, nameof(BotConfig.SteamPassword)));

				Stop();

				return;
			}

			// Steam artificially cuts passwords to first 64 characters
			if (password.Length > 64) {
				password = password[..64];
			}
		}

		if (!SteamClient.IsConnected) {
			// Possible if user spent too much time entering password, try again after reconnect
			return;
		}

		ArchiLogger.LogGenericInfo(Strings.BotLoggingIn);

		InitConnectionFailureTimer();

		if (string.IsNullOrEmpty(RefreshToken)) {
			AuthPollResult pollResult;

			try {
				using CancellationTokenSource authCancellationTokenSource = new();

				CredentialsAuthSession authSession = await SteamClient.Authentication.BeginAuthSessionViaCredentialsAsync(
					new AuthSessionDetails {
						Authenticator = new BotCredentialsProvider(this, authCancellationTokenSource),
						DeviceFriendlyName = SharedInfo.PublicIdentifier,
						GuardData = BotConfig.UseLoginKeys ? BotDatabase.SteamGuardData : null,
						IsPersistentSession = true,
						Password = password,
						Username = username
					}
				).ConfigureAwait(false);

				pollResult = await authSession.PollingWaitForResultAsync(authCancellationTokenSource.Token).ConfigureAwait(false);
			} catch (AuthenticationException e) {
				ArchiLogger.LogGenericWarningException(e);

				await HandleLoginResult(e.Result, e.Result).ConfigureAwait(false);

				ReconnectOnUserInitiated = true;
				SteamClient.Disconnect();

				return;
			} catch (OperationCanceledException) {
				// This is okay, we already took care of that and can ignore it here
				return;
			}

			if (!string.IsNullOrEmpty(pollResult.NewGuardData) && BotConfig.UseLoginKeys) {
				BotDatabase.SteamGuardData = pollResult.NewGuardData;
			}

			if (string.IsNullOrEmpty(pollResult.AccessToken)) {
				// The fuck is this?
				ArchiLogger.LogNullError(pollResult.AccessToken);

				ReconnectOnUserInitiated = true;
				SteamClient.Disconnect();

				return;
			}

			if (string.IsNullOrEmpty(pollResult.RefreshToken)) {
				// The fuck is that?
				ArchiLogger.LogNullError(pollResult.RefreshToken);

				ReconnectOnUserInitiated = true;
				SteamClient.Disconnect();

				return;
			}

			UpdateTokens(pollResult.AccessToken, pollResult.RefreshToken);
		}

		SteamUser.LogOnDetails logOnDetails = new() {
			AccessToken = RefreshToken,
			CellID = ASF.GlobalDatabase?.CellID,
			ClientLanguage = CultureInfo.CurrentCulture.ToSteamClientLanguage(),
			LoginID = LoginID,
			ShouldRememberPassword = BotConfig.UseLoginKeys,
			Username = username
		};

		if (OSType == EOSType.Unknown) {
			OSType = logOnDetails.ClientOSType;
		}

		SteamUser.LogOn(logOnDetails);
	}

	private async void OnDisconnected(SteamClient.DisconnectedCallback callback) {
		ArgumentNullException.ThrowIfNull(callback);

		if (ASF.LoginRateLimitingSemaphore == null) {
			throw new InvalidOperationException(nameof(ASF.LoginRateLimitingSemaphore));
		}

		HeartBeatFailures = 0;
		StopConnectionFailureTimer();
		StopPlayingWasBlockedTimer();
		StopRefreshTokensTimer();

		ArchiLogger.LogGenericInfo(Strings.BotDisconnected);

		PastNotifications.Clear();

		Actions.OnDisconnected();
		ArchiWebHandler.OnDisconnected();
		CardsFarmer.OnDisconnected();
		Trading.OnDisconnected();

		FirstTradeSent = false;
		OwnedPackageIDs = FrozenDictionary<uint, (EPaymentMethod PaymentMethod, DateTime TimeCreated)>.Empty;

		EResult lastLogOnResult = LastLogOnResult;

		for (byte i = 0; (i < WebBrowser.MaxTries) && (lastLogOnResult == EResult.Invalid); i++) {
			await Task.Delay(200).ConfigureAwait(false);

			lastLogOnResult = LastLogOnResult;
		}

		LastLogOnResult = EResult.Invalid;

		await PluginsCore.OnBotDisconnected(this, lastLogOnResult).ConfigureAwait(false);

		// If we initiated disconnect, do not attempt to reconnect
		if (callback.UserInitiated && !ReconnectOnUserInitiated) {
			return;
		}

		switch (lastLogOnResult) {
			case EResult.AccountDisabled:
				// Do not attempt to reconnect, those failures are permanent
				return;
			case EResult.AccessDenied when !string.IsNullOrEmpty(RefreshToken):
			case EResult.Expired when !string.IsNullOrEmpty(RefreshToken):
			case EResult.InvalidPassword when !string.IsNullOrEmpty(RefreshToken):
				// We can retry immediately
				BotDatabase.RefreshToken = RefreshToken = null;
				ArchiLogger.LogGenericInfo(Strings.BotRemovedExpiredLoginKey);

				break;
			case EResult.AccessDenied:
			case EResult.AccountLoginDeniedThrottle:
			case EResult.RateLimitExceeded:
				ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.BotRateLimitExceeded, TimeSpan.FromMinutes(LoginCooldownInMinutes).ToHumanReadable()));

				if (!await ASF.LoginRateLimitingSemaphore.WaitAsync(1000 * WebBrowser.MaxTries).ConfigureAwait(false)) {
					break;
				}

				try {
					await Task.Delay(LoginCooldownInMinutes * 60 * 1000).ConfigureAwait(false);
				} finally {
					ASF.LoginRateLimitingSemaphore.Release();
				}

				break;
			default:
				// Generic delay before retrying
				await Task.Delay(5000).ConfigureAwait(false);

				break;
		}

		if (!KeepRunning || SteamClient.IsConnected) {
			return;
		}

		// Wait with reconnection until we're done with the prompt, not earlier
		while (RequiredInput != ASF.EUserInputType.None) {
			await Task.Delay(1000).ConfigureAwait(false);

			if (!KeepRunning || SteamClient.IsConnected) {
				return;
			}
		}

		ArchiLogger.LogGenericInfo(Strings.BotReconnecting);
		await Connect().ConfigureAwait(false);
	}

	private async void OnFriendsList(SteamFriends.FriendsListCallback callback) {
		ArgumentNullException.ThrowIfNull(callback);
		ArgumentNullException.ThrowIfNull(callback.FriendList);

		foreach (SteamFriends.FriendsListCallback.Friend friend in callback.FriendList.Where(static friend => friend.Relationship == EFriendRelationship.RequestRecipient)) {
			switch (friend.SteamID.AccountType) {
				case EAccountType.Clan when IsMasterClanID(friend.SteamID):
					ArchiLogger.LogInvite(friend.SteamID, true);

					ArchiHandler.AcknowledgeClanInvite(friend.SteamID, true);
					await JoinMasterChatGroupID().ConfigureAwait(false);

					break;
				case EAccountType.Clan:
					bool acceptGroupRequest = await PluginsCore.OnBotFriendRequest(this, friend.SteamID).ConfigureAwait(false);

					if (acceptGroupRequest) {
						ArchiLogger.LogInvite(friend.SteamID, true);

						ArchiHandler.AcknowledgeClanInvite(friend.SteamID, true);
						await JoinMasterChatGroupID().ConfigureAwait(false);

						break;
					}

					if (BotConfig.BotBehaviour.HasFlag(BotConfig.EBotBehaviour.RejectInvalidGroupInvites)) {
						ArchiLogger.LogInvite(friend.SteamID, false);

						ArchiHandler.AcknowledgeClanInvite(friend.SteamID, false);

						break;
					}

					ArchiLogger.LogInvite(friend.SteamID);

					break;
				default:
					if (GetAccess(friend.SteamID) >= EAccess.FamilySharing) {
						ArchiLogger.LogInvite(friend.SteamID, true);

						if (!await ArchiHandler.AddFriend(friend.SteamID).ConfigureAwait(false)) {
							ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, nameof(ArchiHandler.AddFriend)));
						}

						break;
					}

					bool acceptFriendRequest = await PluginsCore.OnBotFriendRequest(this, friend.SteamID).ConfigureAwait(false);

					if (acceptFriendRequest) {
						ArchiLogger.LogInvite(friend.SteamID, true);

						if (!await ArchiHandler.AddFriend(friend.SteamID).ConfigureAwait(false)) {
							ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, nameof(ArchiHandler.AddFriend)));
						}

						break;
					}

					if (BotConfig.BotBehaviour.HasFlag(BotConfig.EBotBehaviour.RejectInvalidFriendInvites)) {
						ArchiLogger.LogInvite(friend.SteamID, false);

						if (!await ArchiHandler.RemoveFriend(friend.SteamID).ConfigureAwait(false)) {
							ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, nameof(ArchiHandler.RemoveFriend)));
						}

						break;
					}

					ArchiLogger.LogInvite(friend.SteamID);

					break;
			}
		}
	}

	private async void OnGuestPassList(SteamApps.GuestPassListCallback callback) {
		ArgumentNullException.ThrowIfNull(callback);
		ArgumentNullException.ThrowIfNull(callback.GuestPasses);

		if ((callback.CountGuestPassesToRedeem == 0) || (callback.GuestPasses.Count == 0) || !BotConfig.AcceptGifts) {
			return;
		}

		HashSet<ulong> guestPassIDs = callback.GuestPasses.Select(static guestPass => guestPass["gid"].AsUnsignedLong()).Where(static gid => gid != 0).ToHashSet();

		if (guestPassIDs.Count == 0) {
			return;
		}

		await Actions.AcceptGuestPasses(guestPassIDs).ConfigureAwait(false);
	}

	private async Task OnIncomingChatMessage(CChatRoom_IncomingChatMessage_Notification notification) {
		ArgumentNullException.ThrowIfNull(notification);

		if (notification.chat_group_id == 0) {
			ArchiLogger.LogNullError(notification.chat_group_id);

			return;
		}

		if (notification.chat_id == 0) {
			ArchiLogger.LogNullError(notification.chat_id);

			return;
		}

		if (notification.steamid_sender == 0) {
			ArchiLogger.LogNullError(notification.steamid_sender);

			return;
		}

		// Under normal circumstances, timestamp must always be greater than 0, but Steam already proved that it's capable of going against the logic
		if ((notification.steamid_sender != SteamID) && (notification.timestamp > 0)) {
			if (ShouldAckChatMessage(notification.steamid_sender)) {
				Utilities.InBackground(() => ArchiHandler.AckChatMessage(notification.chat_group_id, notification.chat_id, notification.timestamp));
			}
		}

		string message;

		// Prefer to use message without bbcode, but only if it's available
		if (!string.IsNullOrEmpty(notification.message_no_bbcode)) {
			message = notification.message_no_bbcode;
		} else if (!string.IsNullOrEmpty(notification.message)) {
			message = SteamChatMessage.Unescape(notification.message);
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

		await Commands.HandleMessage(notification.chat_group_id, notification.chat_id, notification.steamid_sender, message).ConfigureAwait(false);
	}

	private async Task OnIncomingMessage(CFriendMessages_IncomingMessage_Notification notification) {
		ArgumentNullException.ThrowIfNull(notification);

		if (notification.steamid_friend == 0) {
			ArchiLogger.LogNullError(notification.steamid_friend);

			return;
		}

		if ((EChatEntryType) notification.chat_entry_type != EChatEntryType.ChatMsg) {
			return;
		}

		// Under normal circumstances, timestamp must always be greater than 0, but Steam already proved that it's capable of going against the logic
		if (notification is { local_echo: false, rtime32_server_timestamp: > 0 }) {
			if (ShouldAckChatMessage(notification.steamid_friend)) {
				Utilities.InBackground(() => ArchiHandler.AckMessage(notification.steamid_friend, notification.rtime32_server_timestamp));
			}
		}

		string message;

		// Prefer to use message without bbcode, but only if it's available
		if (!string.IsNullOrEmpty(notification.message_no_bbcode)) {
			message = notification.message_no_bbcode;
		} else if (!string.IsNullOrEmpty(notification.message)) {
			message = SteamChatMessage.Unescape(notification.message);
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

		await Commands.HandleMessage(notification.steamid_friend, message).ConfigureAwait(false);
	}

	private void OnInventoryChanged() {
		Utilities.InBackground(CardsFarmer.OnNewItemsNotification);

		if (BotConfig.BotBehaviour.HasFlag(BotConfig.EBotBehaviour.DismissInventoryNotifications)) {
			Utilities.InBackground(ArchiWebHandler.MarkInventory);
		}

		if (BotConfig.CompleteTypesToSend.Count > 0) {
			Utilities.InBackground(SendCompletedSets);
		}
	}

	private async void OnLicenseList(SteamApps.LicenseListCallback callback) {
		ArgumentNullException.ThrowIfNull(callback);
		ArgumentNullException.ThrowIfNull(callback.LicenseList);

		if (ASF.GlobalDatabase == null) {
			throw new InvalidOperationException(nameof(ASF.GlobalDatabase));
		}

		if (callback.LicenseList.Count == 0) {
			ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(callback.LicenseList)));

			return;
		}

		// Wait a short time for eventual LastChangeNumber initialization
		for (byte i = 0; (i < WebBrowser.MaxTries) && !SteamPICSChanges.LiveUpdate; i++) {
			await Task.Delay(1000).ConfigureAwait(false);
		}

		Commands.OnNewLicenseList();

		Dictionary<uint, (EPaymentMethod PaymentMethod, DateTime TimeCreated)> ownedPackageIDs = new();

		Dictionary<uint, ulong> packageAccessTokens = new();
		Dictionary<uint, uint> packagesToRefresh = new();

		bool hasNewEntries = false;

		foreach (SteamApps.LicenseListCallback.License license in callback.LicenseList.GroupBy(static license => license.PackageID, static (_, licenses) => licenses.OrderByDescending(static license => license.TimeCreated).First())) {
			ownedPackageIDs[license.PackageID] = (license.PaymentMethod, license.TimeCreated);

			if (!OwnedPackageIDs.ContainsKey(license.PackageID)) {
				hasNewEntries = true;
			}

			if (!ASF.GlobalDatabase.PackageAccessTokensReadOnly.TryGetValue(license.PackageID, out ulong packageAccessToken) || (packageAccessToken != license.AccessToken)) {
				packageAccessTokens[license.PackageID] = license.AccessToken;

				// Package is always due to refresh with access token change
				packagesToRefresh[license.PackageID] = (uint) license.LastChangeNumber;
			} else if (!ASF.GlobalDatabase.PackagesDataReadOnly.TryGetValue(license.PackageID, out PackageData? packageData) || (packageData.ChangeNumber < license.LastChangeNumber)) {
				packagesToRefresh[license.PackageID] = (uint) license.LastChangeNumber;
			}
		}

		OwnedPackageIDs = ownedPackageIDs.ToFrozenDictionary();

		if (packageAccessTokens.Count > 0) {
			ASF.GlobalDatabase.RefreshPackageAccessTokens(packageAccessTokens);
		}

		if (packagesToRefresh.Count > 0) {
			// Since Steam spams with this call, display message on info level only if refresh takes longer time
			ArchiLogger.LogGenericTrace(Strings.BotRefreshingPackagesData);

			bool displayFinish = false;

			Task refreshTask = ASF.GlobalDatabase.RefreshPackages(this, packagesToRefresh);

			try {
				await refreshTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
			} catch (TimeoutException) {
				ArchiLogger.LogGenericInfo(Strings.BotRefreshingPackagesData);

				displayFinish = true;
			}

			if (await Task.WhenAny(refreshTask, Task.Delay(5000)).ConfigureAwait(false) != refreshTask) {
				ArchiLogger.LogGenericInfo(Strings.BotRefreshingPackagesData);

				displayFinish = true;
			}

			await refreshTask.ConfigureAwait(false);

			if (displayFinish) {
				ArchiLogger.LogGenericInfo(Strings.Done);
			}

			ArchiLogger.LogGenericTrace(Strings.Done);
		}

		if (hasNewEntries) {
			await CardsFarmer.OnNewGameAdded().ConfigureAwait(false);
		}
	}

	private void OnLoggedOff(SteamUser.LoggedOffCallback callback) {
		ArgumentNullException.ThrowIfNull(callback);

		// Keep LastLogOnResult for OnDisconnected()
		LastLogOnResult = callback.Result > EResult.OK ? callback.Result : EResult.Invalid;

		ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.BotLoggedOff, callback.Result));

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
		ArgumentNullException.ThrowIfNull(callback);

		// Always reset one-time-only access tokens when we get OnLoggedOn() response
		AuthCode = TwoFactorCode = null;

		await HandleLoginResult(callback.Result, callback.ExtendedResult).ConfigureAwait(false);

		if (callback.Result != EResult.OK) {
			return;
		}

		AccountFlags = callback.AccountFlags;
		IPCountryCode = callback.IPCountryCode;
		SteamID = callback.ClientSteamID ?? throw new InvalidOperationException(nameof(callback.ClientSteamID));

		ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.BotLoggedOn, $"{SteamID}{(!string.IsNullOrEmpty(callback.VanityURL) ? $"/{callback.VanityURL}" : "")}"));

		// Old status for these doesn't matter, we'll update them if needed
		LoginFailures = 0;
		LibraryLocked = PlayingBlocked = false;

		if (PlayingWasBlocked && (PlayingWasBlockedTimer == null)) {
			InitPlayingWasBlockedTimer();
		}

		if (IsAccountLimited) {
			ArchiLogger.LogGenericWarning(Strings.BotAccountLimited);
		}

		if (IsAccountLocked) {
			ArchiLogger.LogGenericWarning(Strings.BotAccountLocked);
		}

		if ((callback.CellID != 0) && (ASF.GlobalDatabase != null) && (callback.CellID != ASF.GlobalDatabase.CellID)) {
			ASF.GlobalDatabase.CellID = callback.CellID;
		}

		// Handle steamID-based maFile
		if (!HasMobileAuthenticator) {
			string maFilePath = Path.Combine(SharedInfo.ConfigDirectory, $"{SteamID}{SharedInfo.MobileAuthenticatorExtension}");

			if (File.Exists(maFilePath)) {
				await ImportAuthenticatorFromFile(maFilePath).ConfigureAwait(false);
			}
		}

		if (callback.ParentalSettings != null) {
			(SteamParentalActive, string? steamParentalCode) = ValidateSteamParental(callback.ParentalSettings, BotConfig.SteamParentalCode, Program.SteamParentalGeneration);

			if (SteamParentalActive) {
				// Steam parental enabled
				if (!string.IsNullOrEmpty(steamParentalCode)) {
					// We were able to automatically generate it, potentially with help of the config
					if (BotConfig.SteamParentalCode != steamParentalCode) {
						if (!SetUserInput(ASF.EUserInputType.SteamParentalCode, steamParentalCode)) {
							ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, nameof(steamParentalCode)));

							Stop();

							return;
						}
					}
				} else {
					// We failed to generate the pin ourselves, ask the user
					RequiredInput = ASF.EUserInputType.SteamParentalCode;

					steamParentalCode = await Logging.GetUserInput(ASF.EUserInputType.SteamParentalCode, BotName).ConfigureAwait(false);

					if (string.IsNullOrEmpty(steamParentalCode) || !SetUserInput(ASF.EUserInputType.SteamParentalCode, steamParentalCode)) {
						ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, nameof(steamParentalCode)));

						Stop();

						return;
					}
				}
			}
		} else {
			// Steam parental disabled
			SteamParentalActive = false;
		}

		ArchiWebHandler.OnVanityURLChanged(callback.VanityURL);

		// Establish web session
		if (!await RefreshWebSession().ConfigureAwait(false)) {
			return;
		}

		if ((GamesRedeemerInBackgroundTimer == null) && BotDatabase.HasGamesToRedeemInBackground) {
			Utilities.InBackground(() => RedeemGamesInBackground());
		}

		ArchiHandler.SetCurrentMode(BotConfig.UserInterfaceMode);
		ArchiHandler.RequestItemAnnouncements();

		// Sometimes Steam won't send us our own PersonaStateCallback, so request it explicitly
		RequestPersonaStateUpdate();

		Utilities.InBackground(InitializeFamilySharing);

		ResetPersonaState();

		if (BotConfig.SteamMasterClanID != 0) {
			Utilities.InBackground(
				async () => {
					if (!await ArchiWebHandler.JoinGroup(BotConfig.SteamMasterClanID).ConfigureAwait(false)) {
						ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, nameof(ArchiWebHandler.JoinGroup)));
					}

					await JoinMasterChatGroupID().ConfigureAwait(false);
				}
			);
		}

		if (BotConfig.RemoteCommunication.HasFlag(BotConfig.ERemoteCommunication.SteamGroup)) {
			Utilities.InBackground(() => ArchiWebHandler.JoinGroup(SharedInfo.ASFGroupSteamID));
		}

		if (CardsFarmer.Paused) {
			// Emit initial game playing status in this case
			Utilities.InBackground(ResetGamesPlayed);
		}

		SteamPICSChanges.OnBotLoggedOn();

		await PluginsCore.OnBotLoggedOn(this).ConfigureAwait(false);
	}

	private async void OnPersonaState(SteamFriends.PersonaStateCallback callback) {
		ArgumentNullException.ThrowIfNull(callback);

		if (callback.FriendID != SteamID) {
			return;
		}

		// Empty name should be converted to null, this is actually lack of value, but it's transmitted as empty in protobufs
		Nickname = !string.IsNullOrEmpty(callback.Name) ? callback.Name : null;

		string? avatarHash = null;

		if ((callback.AvatarHash?.Length > 0) && callback.AvatarHash.Any(static singleByte => singleByte > 0)) {
#pragma warning disable CA1308 // False positive, we're intentionally converting this part to lowercase and it's not used for any security decisions based on the result of the normalization
			avatarHash = Convert.ToHexString(callback.AvatarHash).ToLowerInvariant();
#pragma warning restore CA1308 // False positive, we're intentionally converting this part to lowercase and it's not used for any security decisions based on the result of the normalization

			if (string.IsNullOrEmpty(avatarHash) || avatarHash.All(static singleChar => singleChar == '0')) {
				avatarHash = null;
			}
		}

		AvatarHash = avatarHash;

		await PluginsCore.OnSelfPersonaState(this, callback, Nickname, AvatarHash).ConfigureAwait(false);
	}

	private async void OnPlayingSessionState(SteamUser.PlayingSessionStateCallback callback) {
		ArgumentNullException.ThrowIfNull(callback);

		if (callback.PlayingBlocked == PlayingBlocked) {
			return; // No status update, we're not interested
		}

		PlayingBlocked = callback.PlayingBlocked;
		await CheckOccupationStatus().ConfigureAwait(false);
	}

	private async void OnRefreshTokensTimer(object? state = null) {
		DateTime accessTokenValidUntil = AccessTokenValidUntil.GetValueOrDefault();

		if ((accessTokenValidUntil > DateTime.MinValue) && (accessTokenValidUntil > DateTime.UtcNow.AddMinutes(MinimumAccessTokenValidityMinutes + 1))) {
			// We don't need to refresh just yet
			InitRefreshTokensTimer(accessTokenValidUntil);

			return;
		}

		await RefreshWebSession().ConfigureAwait(false);
	}

	private async void OnSendItemsTimer(object? state = null) => await Actions.SendInventory(filterFunction: item => BotConfig.LootableTypes.Contains(item.Type)).ConfigureAwait(false);

	private async void OnServiceMethod(SteamUnifiedMessages.ServiceMethodNotification notification) {
		ArgumentNullException.ThrowIfNull(notification);

		switch (notification.MethodName) {
			case "ChatRoomClient.NotifyIncomingChatMessage#1":
				await OnIncomingChatMessage((CChatRoom_IncomingChatMessage_Notification) notification.Body).ConfigureAwait(false);

				break;
			case "FriendMessagesClient.IncomingMessage#1":
				await OnIncomingMessage((CFriendMessages_IncomingMessage_Notification) notification.Body).ConfigureAwait(false);

				break;
		}
	}

	private async void OnSharedLibraryLockStatus(SharedLibraryLockStatusCallback callback) {
		ArgumentNullException.ThrowIfNull(callback);

		// Ignore no status updates
		if (LibraryLocked) {
			if ((callback.LibraryLockedBySteamID != 0) && (callback.LibraryLockedBySteamID != SteamID)) {
				return;
			}

			LibraryLocked = false;
		} else {
			if ((callback.LibraryLockedBySteamID == 0) || (callback.LibraryLockedBySteamID == SteamID)) {
				return;
			}

			LibraryLocked = true;
		}

		await CheckOccupationStatus().ConfigureAwait(false);
	}

	private void OnTradeCheckTimer(object? state = null) {
		if (IsConnectedAndLoggedOn) {
			Utilities.InBackground(Trading.OnNewTrade);
		}
	}

	private void OnUserNotifications(UserNotificationsCallback callback) {
		ArgumentNullException.ThrowIfNull(callback);
		ArgumentNullException.ThrowIfNull(callback.Notifications);

		if (callback.Notifications.Count == 0) {
			return;
		}

		HashSet<UserNotificationsCallback.EUserNotification> newPluginNotifications = [];

		foreach ((UserNotificationsCallback.EUserNotification notification, uint count) in callback.Notifications) {
			bool newNotification;

			if (count > 0) {
				newNotification = !PastNotifications.TryGetValue(notification, out uint previousCount) || (count > previousCount);
				PastNotifications[notification] = count;

				if (newNotification) {
					newPluginNotifications.Add(notification);
				}
			} else {
				newNotification = false;
				PastNotifications.TryRemove(notification, out _);
			}

			ArchiLogger.LogGenericTrace($"{notification} = {count}");

			switch (notification) {
				case UserNotificationsCallback.EUserNotification.Gifts when newNotification && BotConfig.AcceptGifts:
					Utilities.InBackground(Actions.AcceptDigitalGiftCards);

					break;
				case UserNotificationsCallback.EUserNotification.Items when newNotification:
					OnInventoryChanged();

					break;
				case UserNotificationsCallback.EUserNotification.Trading when newNotification:
					if ((TradeCheckTimer != null) && (BotConfig.TradeCheckPeriod > 0)) {
						TradeCheckTimer.Change(TimeSpan.FromMinutes(BotConfig.TradeCheckPeriod), TimeSpan.FromMinutes(BotConfig.TradeCheckPeriod));
					}

					Utilities.InBackground(Trading.OnNewTrade);

					break;
			}
		}

		if (newPluginNotifications.Count > 0) {
			Utilities.InBackground(() => PluginsCore.OnBotUserNotifications(this, newPluginNotifications));
		}
	}

	private void OnVanityURLChangedCallback(SteamUser.VanityURLChangedCallback callback) {
		ArgumentNullException.ThrowIfNull(callback);

		ArchiWebHandler.OnVanityURLChanged(callback.VanityURL);
	}

	private void OnWalletInfo(SteamUser.WalletInfoCallback callback) {
		ArgumentNullException.ThrowIfNull(callback);

		WalletBalance = callback.LongBalance;
		WalletBalanceDelayed = callback.LongBalanceDelayed;
		WalletCurrency = callback.Currency;
	}

	private async void RedeemGamesInBackground(object? state = null) {
		if (!await GamesRedeemerInBackgroundSemaphore.WaitAsync(0).ConfigureAwait(false)) {
			return;
		}

		try {
			if (GamesRedeemerInBackgroundTimer != null) {
				await GamesRedeemerInBackgroundTimer.DisposeAsync().ConfigureAwait(false);

				GamesRedeemerInBackgroundTimer = null;
			}

			ArchiLogger.LogGenericInfo(Strings.Starting);

			bool assumeWalletKeyOnBadActivationCode = BotConfig.RedeemingPreferences.HasFlag(BotConfig.ERedeemingPreferences.AssumeWalletKeyOnBadActivationCode);

			while (IsConnectedAndLoggedOn && BotDatabase.HasGamesToRedeemInBackground) {
				(string? key, string? name) = BotDatabase.GetGameToRedeemInBackground();

				if (string.IsNullOrEmpty(key)) {
					ArchiLogger.LogNullError(key);

					break;
				}

				if (string.IsNullOrEmpty(name)) {
					ArchiLogger.LogNullError(name);

					break;
				}

				CStore_RegisterCDKey_Response? response = await Actions.RedeemKey(key).ConfigureAwait(false);

				if (response == null) {
					continue;
				}

				EResult result = (EResult) response.purchase_receipt_info.purchase_status;
				EPurchaseResultDetail purchaseResultDetail = (EPurchaseResultDetail) response.purchase_result_details;

				string? balanceText = null;

				if ((purchaseResultDetail == EPurchaseResultDetail.CannotRedeemCodeFromClient) || ((purchaseResultDetail == EPurchaseResultDetail.BadActivationCode) && assumeWalletKeyOnBadActivationCode)) {
					// If it's a wallet code, we try to redeem it first, then handle the inner result as our primary one
					(EResult Result, EPurchaseResultDetail? PurchaseResult, string? BalanceText)? walletResult = await ArchiWebHandler.RedeemWalletKey(key).ConfigureAwait(false);

					if (walletResult != null) {
						result = walletResult.Value.Result;
						purchaseResultDetail = walletResult.Value.PurchaseResult.GetValueOrDefault(walletResult.Value.Result == EResult.OK ? EPurchaseResultDetail.NoDetail : EPurchaseResultDetail.BadActivationCode); // BadActivationCode is our smart guess in this case
						balanceText = walletResult.Value.BalanceText;
					} else {
						result = EResult.Timeout;
						purchaseResultDetail = EPurchaseResultDetail.Timeout;
					}
				}

				Dictionary<uint, string>? items = response.purchase_receipt_info.line_items.Count > 0 ? response.purchase_receipt_info.line_items.ToDictionary(static lineItem => lineItem.packageid, static lineItem => lineItem.line_item_description) : null;

				ArchiLogger.LogGenericDebug(items?.Count > 0 ? string.Format(CultureInfo.CurrentCulture, Strings.BotRedeemWithItems, key, $"{result}/{purchaseResultDetail}{(!string.IsNullOrEmpty(balanceText) ? $"/{balanceText}" : "")}", string.Join(", ", items)) : string.Format(CultureInfo.CurrentCulture, Strings.BotRedeem, key, $"{result}/{purchaseResultDetail}{(!string.IsNullOrEmpty(balanceText) ? $"/{balanceText}" : "")}"));

				bool rateLimited = false;
				bool redeemed = false;

				switch (purchaseResultDetail) {
					case EPurchaseResultDetail.AccountLocked:
					case EPurchaseResultDetail.AlreadyPurchased:
					case EPurchaseResultDetail.CannotRedeemCodeFromClient:
					case EPurchaseResultDetail.DoesNotOwnRequiredApp:
					case EPurchaseResultDetail.NoWallet:
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
						ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(purchaseResultDetail), purchaseResultDetail));

						break;
				}

				if (rateLimited) {
					break;
				}

				BotDatabase.RemoveGameToRedeemInBackground(key);

				// If user omitted the name or intentionally provided the same name as key, replace it with the Steam result
				if (name.Equals(key, StringComparison.OrdinalIgnoreCase) && (items?.Count > 0)) {
					name = string.Join(", ", items.Values);
				}

				string logEntry = $"{name}{DefaultBackgroundKeysRedeemerSeparator}[{purchaseResultDetail}]{(items?.Count > 0 ? $"{DefaultBackgroundKeysRedeemerSeparator}{string.Join(", ", items)}" : "")}{DefaultBackgroundKeysRedeemerSeparator}{key}";

				string filePath = GetFilePath(redeemed ? EFileType.KeysToRedeemUsed : EFileType.KeysToRedeemUnused);

				if (string.IsNullOrEmpty(filePath)) {
					ArchiLogger.LogNullError(filePath);

					return;
				}

				try {
					await File.AppendAllTextAsync(filePath, $"{logEntry}{Environment.NewLine}").ConfigureAwait(false);
				} catch (Exception e) {
					ArchiLogger.LogGenericException(e);
					ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.Content, logEntry));

					break;
				}
			}

			if (IsConnectedAndLoggedOn && BotDatabase.HasGamesToRedeemInBackground) {
				ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.BotRateLimitExceeded, TimeSpan.FromHours(RedeemCooldownInHours).ToHumanReadable()));

				GamesRedeemerInBackgroundTimer = new Timer(
					RedeemGamesInBackground,
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
		if (!IsConnectedAndLoggedOn || CardsFarmer.NowFarming) {
			return;
		}

		if (BotConfig.GamesPlayedWhileIdle.Count > 0) {
			if (!IsPlayingPossible) {
				return;
			}

			// This function might be executed before PlayingSessionStateCallback/SharedLibraryLockStatusCallback, ensure proper delay in this case
			await Task.Delay(2000).ConfigureAwait(false);

			if (!IsConnectedAndLoggedOn || CardsFarmer.NowFarming || !IsPlayingPossible) {
				return;
			}

			if (PlayingWasBlocked) {
				byte minFarmingDelayAfterBlock = ASF.GlobalConfig?.MinFarmingDelayAfterBlock ?? GlobalConfig.DefaultMinFarmingDelayAfterBlock;

				if (minFarmingDelayAfterBlock > 0) {
					for (byte i = 0; (i < minFarmingDelayAfterBlock) && IsConnectedAndLoggedOn && !CardsFarmer.NowFarming && IsPlayingPossible && PlayingWasBlocked; i++) {
						await Task.Delay(1000).ConfigureAwait(false);
					}

					if (!IsConnectedAndLoggedOn || CardsFarmer.NowFarming || !IsPlayingPossible) {
						return;
					}
				}
			}

			ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.BotIdlingSelectedGames, nameof(BotConfig.GamesPlayedWhileIdle), string.Join(", ", BotConfig.GamesPlayedWhileIdle)));
		}

		await ArchiHandler.PlayGames(BotConfig.GamesPlayedWhileIdle, BotConfig.CustomGamePlayedWhileIdle).ConfigureAwait(false);
	}

	private void ResetPlayingWasBlockedWithTimer(object? state = null) {
		PlayingWasBlocked = false;
		StopPlayingWasBlockedTimer();
	}

	private async Task SendCompletedSets() {
		// ReSharper disable once SuspiciousLockOverSynchronizationPrimitive - this is not a mistake, we need extra synchronization, and we can re-use the semaphore object for that
		lock (SendCompleteTypesSemaphore) {
			if (SendCompleteTypesScheduled) {
				return;
			}

			SendCompleteTypesScheduled = true;
		}

		await SendCompleteTypesSemaphore.WaitAsync().ConfigureAwait(false);

		try {
			using (await Actions.GetTradingLock().ConfigureAwait(false)) {
				// ReSharper disable once SuspiciousLockOverSynchronizationPrimitive - this is not a mistake, we need extra synchronization, and we can re-use the semaphore object for that
				lock (SendCompleteTypesSemaphore) {
					SendCompleteTypesScheduled = false;
				}

				HashSet<uint>? appIDs = await GetPossiblyCompletedBadgeAppIDs().ConfigureAwait(false);

				if ((appIDs == null) || (appIDs.Count == 0)) {
					return;
				}

				HashSet<Asset> inventory;

				try {
					inventory = await ArchiHandler.GetMyInventoryAsync(tradableOnly: true)
						.Where(item => appIDs.Contains(item.RealAppID) && BotConfig.CompleteTypesToSend.Contains(item.Type))
						.ToHashSetAsync()
						.ConfigureAwait(false);
				} catch (TimeoutException e) {
					ArchiLogger.LogGenericWarningException(e);

					return;
				} catch (Exception e) {
					ArchiLogger.LogGenericException(e);

					return;
				}

				if (inventory.Count == 0) {
					ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(inventory)));

					return;
				}

				Dictionary<(uint RealAppID, EAssetType Type, EAssetRarity Rarity), List<uint>> inventorySets = Trading.GetInventorySets(inventory);

				// Filter appIDs that can't possibly be completed due to having less cards than smallest badges possible
				appIDs.IntersectWith(inventorySets.Where(static kv => kv.Value.Count >= MinCardsPerBadge).Select(static kv => kv.Key.RealAppID));

				if (appIDs.Count == 0) {
					return;
				}

				Dictionary<uint, byte>? cardsCountPerAppID = await LoadCardsPerSet(appIDs).ConfigureAwait(false);

				if (cardsCountPerAppID == null) {
					return;
				}

				Dictionary<(uint RealAppID, EAssetType Type, EAssetRarity Rarity), (uint Sets, byte CardsPerSet)> itemsToTakePerInventorySet = new();

				foreach (((uint RealAppID, EAssetType Type, EAssetRarity Rarity) key, List<uint> amounts) in inventorySets.Where(set => appIDs.Contains(set.Key.RealAppID))) {
					if (!cardsCountPerAppID.TryGetValue(key.RealAppID, out byte cardsCount) || (cardsCount == 0)) {
						throw new InvalidOperationException(nameof(cardsCount));
					}

					if (amounts.Count < cardsCount) {
						// Filter results that can't be completed due to not having enough cards available (now that we know how much exactly)
						continue;
					}

					uint minimumOwnedAmount = amounts[0];

					if (minimumOwnedAmount == 0) {
						throw new InvalidOperationException(nameof(minimumOwnedAmount));
					}

					itemsToTakePerInventorySet[key] = (minimumOwnedAmount, cardsCount);
				}

				if (itemsToTakePerInventorySet.Count == 0) {
					return;
				}

				HashSet<Asset> result = GetItemsForFullSets(inventory, itemsToTakePerInventorySet);

				if (result.Count > 0) {
					await Actions.SendInventory(result).ConfigureAwait(false);
				}
			}
		} finally {
			SendCompleteTypesSemaphore.Release();
		}
	}

	private async Task<bool> SendMessagePart(ulong steamID, string messagePart, ulong chatGroupID = 0) {
		if ((steamID == 0) || ((chatGroupID == 0) && !new SteamID(steamID).IsIndividualAccount)) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		ArgumentException.ThrowIfNullOrEmpty(messagePart);

		if (!IsConnectedAndLoggedOn) {
			return false;
		}

		await MessagingSemaphore.WaitAsync().ConfigureAwait(false);

		try {
			for (byte i = 0; (i < WebBrowser.MaxTries) && IsConnectedAndLoggedOn; i++) {
				EResult result;

				if (chatGroupID == 0) {
					result = await ArchiHandler.SendMessage(steamID, messagePart).ConfigureAwait(false);
				} else {
					result = await ArchiHandler.SendMessage(chatGroupID, steamID, messagePart).ConfigureAwait(false);
				}

				switch (result) {
					case EResult.Blocked:
						// No point in retrying, those failures are permanent
						ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, result));

						return false;
					case EResult.Busy:
					case EResult.Fail:
					case EResult.LimitExceeded:
					case EResult.RateLimitExceeded:
					case EResult.ServiceUnavailable:
					case EResult.Timeout:
						await Task.Delay(5000).ConfigureAwait(false);

						continue;
					case EResult.OK:
						return true;
					default:
						ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(result), result));

						return false;
				}
			}

			return false;
		} finally {
			MessagingSemaphore.Release();
		}
	}

	private bool ShouldAckChatMessage(ulong steamID) {
		if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		if (Bots == null) {
			throw new InvalidOperationException(nameof(Bots));
		}

		if (BotConfig.BotBehaviour.HasFlag(BotConfig.EBotBehaviour.MarkReceivedMessagesAsRead)) {
			return true;
		}

		return BotConfig.BotBehaviour.HasFlag(BotConfig.EBotBehaviour.MarkBotMessagesAsRead) && Bots.Values.Any(bot => bot.SteamID == steamID);
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

	private void StopRefreshTokensTimer() {
		if (RefreshTokensTimer == null) {
			return;
		}

		RefreshTokensTimer.Dispose();
		RefreshTokensTimer = null;
	}

	private void UpdateTokens(string accessToken, string? refreshToken = null) {
		ArgumentException.ThrowIfNullOrEmpty(accessToken);

		AccessToken = accessToken;

		if (!string.IsNullOrEmpty(refreshToken)) {
			RefreshToken = refreshToken;
		}

		if (BotConfig.UseLoginKeys) {
			if (BotConfig.PasswordFormat.HasTransformation()) {
				BotDatabase.AccessToken = ArchiCryptoHelper.Encrypt(BotConfig.PasswordFormat, accessToken);

				if (!string.IsNullOrEmpty(refreshToken)) {
					BotDatabase.RefreshToken = ArchiCryptoHelper.Encrypt(BotConfig.PasswordFormat, refreshToken);
				}
			} else {
				BotDatabase.AccessToken = accessToken;

				if (!string.IsNullOrEmpty(refreshToken)) {
					BotDatabase.RefreshToken = refreshToken;
				}
			}
		}
	}

	private (bool IsSteamParentalEnabled, string? SteamParentalCode) ValidateSteamParental(ParentalSettings settings, string? steamParentalCode = null, bool allowGeneration = true) {
		ArgumentNullException.ThrowIfNull(settings);

		if (!settings.is_enabled || (settings.passwordhash == null)) {
			return (false, null);
		}

		if (settings.passwordhash.Length > byte.MaxValue) {
			throw new ArgumentOutOfRangeException(nameof(settings));
		}

		ArchiCryptoHelper.EHashingMethod steamParentalHashingMethod;

		switch (settings.passwordhashtype) {
			case 4:
				steamParentalHashingMethod = ArchiCryptoHelper.EHashingMethod.Pbkdf2;

				break;
			case 6:
				steamParentalHashingMethod = ArchiCryptoHelper.EHashingMethod.SCrypt;

				break;
			default:
				ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(settings.passwordhashtype), settings.passwordhashtype));

				return (true, null);
		}

		if (!string.IsNullOrEmpty(steamParentalCode)) {
			byte i = 0;

			byte[] password = new byte[steamParentalCode.Length];

			foreach (char character in steamParentalCode.TakeWhile(static character => character is >= '0' and <= '9')) {
				password[i++] = (byte) character;
			}

			if (i >= steamParentalCode.Length) {
				byte[] passwordHash = ArchiCryptoHelper.Hash(password, settings.salt, (byte) settings.passwordhash.Length, steamParentalHashingMethod);

				if (passwordHash.SequenceEqual(settings.passwordhash)) {
					return (true, steamParentalCode);
				}
			}
		}

		if (!allowGeneration) {
			return (true, null);
		}

		ArchiLogger.LogGenericInfo(Strings.BotGeneratingSteamParentalCode);

		steamParentalCode = ArchiCryptoHelper.RecoverSteamParentalCode(settings.passwordhash, settings.salt, steamParentalHashingMethod);

		ArchiLogger.LogGenericInfo(Strings.Done);

		return (true, steamParentalCode);
	}

	public enum EFileType : byte {
		Config,
		Database,
		KeysToRedeem,
		KeysToRedeemUnused,
		KeysToRedeemUsed,
		MobileAuthenticator
	}
}
