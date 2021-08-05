//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2021 Łukasz "JustArchi" Domeradzki
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

#if NETFRAMEWORK
using JustArchiNET.Madness;
#endif
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Collections;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Steam.Data;
using ArchiSteamFarm.Steam.Exchange;
using ArchiSteamFarm.Steam.Integration;
using ArchiSteamFarm.Steam.Integration.Callbacks;
using ArchiSteamFarm.Steam.Security;
using ArchiSteamFarm.Steam.Storage;
using ArchiSteamFarm.Storage;
using ArchiSteamFarm.Web;
using JetBrains.Annotations;
using SteamKit2;

namespace ArchiSteamFarm.Steam.Interaction {
	public sealed class Actions : IAsyncDisposable {
		private static readonly SemaphoreSlim GiftCardsSemaphore = new(1, 1);

		private readonly Bot Bot;
		private readonly ConcurrentHashSet<ulong> HandledGifts = new();
		private readonly SemaphoreSlim TradingSemaphore = new(1, 1);

#pragma warning disable CA2213 // False positive, .NET Framework can't understand DisposeAsync()
		private Timer? CardsFarmerResumeTimer;
#pragma warning restore CA2213 // False positive, .NET Framework can't understand DisposeAsync()

		private bool ProcessingGiftsScheduled;
		private bool TradingScheduled;

		internal Actions(Bot bot) => Bot = bot ?? throw new ArgumentNullException(nameof(bot));

		public async ValueTask DisposeAsync() {
			// Those are objects that are always being created if constructor doesn't throw exception
			TradingSemaphore.Dispose();

			// Those are objects that might be null and the check should be in-place
			if (CardsFarmerResumeTimer != null) {
				await CardsFarmerResumeTimer.DisposeAsync().ConfigureAwait(false);
			}
		}

		[PublicAPI]
		public static string? Encrypt(ArchiCryptoHelper.ECryptoMethod cryptoMethod, string stringToEncrypt) {
			if (!Enum.IsDefined(typeof(ArchiCryptoHelper.ECryptoMethod), cryptoMethod)) {
				throw new InvalidEnumArgumentException(nameof(cryptoMethod), (int) cryptoMethod, typeof(ArchiCryptoHelper.ECryptoMethod));
			}

			if (string.IsNullOrEmpty(stringToEncrypt)) {
				throw new ArgumentNullException(nameof(stringToEncrypt));
			}

			return ArchiCryptoHelper.Encrypt(cryptoMethod, stringToEncrypt);
		}

		[PublicAPI]
		public static (bool Success, string Message) Exit() {
			// Schedule the task after some time so user can receive response
			Utilities.InBackground(
				async () => {
					await Task.Delay(1000).ConfigureAwait(false);
					await Program.Exit().ConfigureAwait(false);
				}
			);

			return (true, Strings.Done);
		}

		[PublicAPI]
		public async Task<(bool Success, string? Token, string Message)> GenerateTwoFactorAuthenticationToken() {
			if (Bot.BotDatabase.MobileAuthenticator == null) {
				return (false, null, Strings.BotNoASFAuthenticator);
			}

			string? token = await Bot.BotDatabase.MobileAuthenticator.GenerateToken().ConfigureAwait(false);

			return (true, token, Strings.Success);
		}

		[PublicAPI]
		public async Task<IDisposable> GetTradingLock() {
			await TradingSemaphore.WaitAsync().ConfigureAwait(false);

			return new SemaphoreLock(TradingSemaphore);
		}

		[PublicAPI]
		public async Task<(bool Success, IReadOnlyCollection<Confirmation>? HandledConfirmations, string Message)> HandleTwoFactorAuthenticationConfirmations(bool accept, Confirmation.EType? acceptedType = null, IReadOnlyCollection<ulong>? acceptedCreatorIDs = null, bool waitIfNeeded = false) {
			if (Bot.BotDatabase.MobileAuthenticator == null) {
				return (false, null, Strings.BotNoASFAuthenticator);
			}

			if (!Bot.IsConnectedAndLoggedOn) {
				return (false, null, Strings.BotNotConnected);
			}

			Dictionary<ulong, Confirmation>? handledConfirmations = null;

			for (byte i = 0; (i == 0) || ((i < WebBrowser.MaxTries) && waitIfNeeded); i++) {
				if (i > 0) {
					await Task.Delay(1000).ConfigureAwait(false);
				}

				HashSet<Confirmation>? confirmations = await Bot.BotDatabase.MobileAuthenticator.GetConfirmations().ConfigureAwait(false);

				if ((confirmations == null) || (confirmations.Count == 0)) {
					continue;
				}

				if (acceptedType.HasValue) {
					if (confirmations.RemoveWhere(confirmation => confirmation.Type != acceptedType.Value) > 0) {
						if (confirmations.Count == 0) {
							continue;
						}
					}
				}

				if (acceptedCreatorIDs?.Count > 0) {
					if (confirmations.RemoveWhere(confirmation => !acceptedCreatorIDs.Contains(confirmation.Creator)) > 0) {
						if (confirmations.Count == 0) {
							continue;
						}
					}
				}

				if (!await Bot.BotDatabase.MobileAuthenticator.HandleConfirmations(confirmations, accept).ConfigureAwait(false)) {
					return (false, handledConfirmations?.Values, Strings.WarningFailed);
				}

				handledConfirmations ??= new Dictionary<ulong, Confirmation>();

				foreach (Confirmation? confirmation in confirmations) {
					handledConfirmations[confirmation.Creator] = confirmation;
				}

				if (acceptedCreatorIDs?.Count > 0) {
					// Check if those are all that we were expected to confirm
					if ((handledConfirmations.Count >= acceptedCreatorIDs.Count) && acceptedCreatorIDs.All(handledConfirmations.ContainsKey)) {
						return (true, handledConfirmations.Values, string.Format(CultureInfo.CurrentCulture, Strings.BotHandledConfirmations, handledConfirmations.Count));
					}
				}
			}

			bool success = !waitIfNeeded || ((handledConfirmations?.Count > 0) && ((acceptedCreatorIDs == null) || (acceptedCreatorIDs.Count == 0)));

			return (success, handledConfirmations?.Values, success ? string.Format(CultureInfo.CurrentCulture, Strings.BotHandledConfirmations, handledConfirmations?.Count ?? 0) : string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));
		}

		[PublicAPI]
		public static string Hash(ArchiCryptoHelper.EHashingMethod hashingMethod, string stringToHash) {
			if (!Enum.IsDefined(typeof(ArchiCryptoHelper.EHashingMethod), hashingMethod)) {
				throw new InvalidEnumArgumentException(nameof(hashingMethod), (int) hashingMethod, typeof(ArchiCryptoHelper.EHashingMethod));
			}

			if (string.IsNullOrEmpty(stringToHash)) {
				throw new ArgumentNullException(nameof(stringToHash));
			}

			return ArchiCryptoHelper.Hash(hashingMethod, stringToHash);
		}

		[PublicAPI]
		public async Task<(bool Success, string Message)> Pause(bool permanent, ushort resumeInSeconds = 0) {
			if (Bot.CardsFarmer.Paused) {
				return (false, Strings.BotAutomaticIdlingPausedAlready);
			}

			await Bot.CardsFarmer.Pause(permanent).ConfigureAwait(false);

			if (!permanent && (Bot.BotConfig.GamesPlayedWhileIdle.Count > 0)) {
				// We want to let family sharing users access our library, and in this case we must also stop GamesPlayedWhileIdle
				// We add extra delay because OnFarmingStopped() also executes PlayGames()
				// Despite of proper order on our end, Steam network might not respect it
				await Task.Delay(Bot.CallbackSleep).ConfigureAwait(false);
				await Bot.ArchiHandler.PlayGames(Array.Empty<uint>(), Bot.BotConfig.CustomGamePlayedWhileIdle).ConfigureAwait(false);
			}

			if (resumeInSeconds > 0) {
				if (CardsFarmerResumeTimer == null) {
					CardsFarmerResumeTimer = new Timer(
						_ => Resume(),
						null,
						TimeSpan.FromSeconds(resumeInSeconds), // Delay
						Timeout.InfiniteTimeSpan // Period
					);
				} else {
					CardsFarmerResumeTimer.Change(TimeSpan.FromSeconds(resumeInSeconds), Timeout.InfiniteTimeSpan);
				}
			}

			return (true, Strings.BotAutomaticIdlingNowPaused);
		}

		[PublicAPI]
		public async Task<(bool Success, string Message)> Play(IReadOnlyCollection<uint> gameIDs, string? gameName = null) {
			if (gameIDs == null) {
				throw new ArgumentNullException(nameof(gameIDs));
			}

			if (!Bot.IsConnectedAndLoggedOn) {
				return (false, Strings.BotNotConnected);
			}

			if (!Bot.CardsFarmer.Paused) {
				await Bot.CardsFarmer.Pause(true).ConfigureAwait(false);
			}

			await Bot.ArchiHandler.PlayGames(gameIDs, gameName).ConfigureAwait(false);

			return (true, gameIDs.Count > 0 ? string.Format(CultureInfo.CurrentCulture, Strings.BotIdlingSelectedGames, nameof(gameIDs), string.Join(", ", gameIDs)) : Strings.Done);
		}

		[PublicAPI]
		public async Task<PurchaseResponseCallback?> RedeemKey(string key) {
			await LimitGiftsRequestsAsync().ConfigureAwait(false);

			return await Bot.ArchiHandler.RedeemKey(key).ConfigureAwait(false);
		}

		[PublicAPI]
		public static (bool Success, string Message) Restart() {
			if (!Program.RestartAllowed) {
				return (false, "!" + nameof(Program.RestartAllowed));
			}

			// Schedule the task after some time so user can receive response
			Utilities.InBackground(
				async () => {
					await Task.Delay(1000).ConfigureAwait(false);
					await Program.Restart().ConfigureAwait(false);
				}
			);

			return (true, Strings.Done);
		}

		[PublicAPI]
		public (bool Success, string Message) Resume() {
			if (!Bot.CardsFarmer.Paused) {
				return (false, Strings.BotAutomaticIdlingResumedAlready);
			}

			Utilities.InBackground(() => Bot.CardsFarmer.Resume(true));

			return (true, Strings.BotAutomaticIdlingNowResumed);
		}

		[PublicAPI]
		public async Task<(bool Success, string Message)> SendInventory(IReadOnlyCollection<Asset> items, ulong targetSteamID = 0, string? tradeToken = null, ushort itemsPerTrade = Trading.MaxItemsPerTrade) {
			if ((items == null) || (items.Count == 0)) {
				throw new ArgumentNullException(nameof(items));
			}

			if (itemsPerTrade < 2) {
				throw new ArgumentOutOfRangeException(nameof(itemsPerTrade));
			}

			if (!Bot.IsConnectedAndLoggedOn) {
				return (false, Strings.BotNotConnected);
			}

			if (targetSteamID == 0) {
				targetSteamID = GetFirstSteamMasterID();

				if (targetSteamID == 0) {
					return (false, Strings.BotLootingMasterNotDefined);
				}

				if (string.IsNullOrEmpty(tradeToken) && !string.IsNullOrEmpty(Bot.BotConfig.SteamTradeToken)) {
					tradeToken = Bot.BotConfig.SteamTradeToken;
				}
			} else if (!new SteamID(targetSteamID).IsIndividualAccount) {
				throw new ArgumentOutOfRangeException(nameof(targetSteamID));
			}

			if (targetSteamID == Bot.SteamID) {
				return (false, Strings.BotSendingTradeToYourself);
			}

			if (!await Bot.ArchiWebHandler.MarkSentTrades().ConfigureAwait(false)) {
				return (false, Strings.BotLootingFailed);
			}

			if (string.IsNullOrEmpty(tradeToken) && (Bot.SteamFriends.GetFriendRelationship(targetSteamID) != EFriendRelationship.Friend)) {
				Bot? targetBot = Bot.Bots?.Values.FirstOrDefault(bot => bot.SteamID == targetSteamID);

				if (targetBot?.IsConnectedAndLoggedOn == true) {
					tradeToken = await targetBot.ArchiHandler.GetTradeToken().ConfigureAwait(false);
				}
			}

			(bool success, HashSet<ulong>? mobileTradeOfferIDs) = await Bot.ArchiWebHandler.SendTradeOffer(targetSteamID, items, token: tradeToken, itemsPerTrade: itemsPerTrade).ConfigureAwait(false);

			if ((mobileTradeOfferIDs?.Count > 0) && Bot.HasMobileAuthenticator) {
				(bool twoFactorSuccess, _, _) = await HandleTwoFactorAuthenticationConfirmations(true, Confirmation.EType.Trade, mobileTradeOfferIDs, true).ConfigureAwait(false);

				if (!twoFactorSuccess) {
					return (false, Strings.BotLootingFailed);
				}
			}

			return success ? (true, Strings.BotLootingSuccess) : (false, Strings.BotLootingFailed);
		}

		[PublicAPI]
		public async Task<(bool Success, string Message)> SendInventory(uint appID = Asset.SteamAppID, ulong contextID = Asset.SteamCommunityContextID, ulong targetSteamID = 0, string? tradeToken = null, Func<Asset, bool>? filterFunction = null, ushort itemsPerTrade = Trading.MaxItemsPerTrade) {
			if (appID == 0) {
				throw new ArgumentOutOfRangeException(nameof(appID));
			}

			if (contextID == 0) {
				throw new ArgumentOutOfRangeException(nameof(contextID));
			}

			if (!Bot.IsConnectedAndLoggedOn) {
				return (false, Strings.BotNotConnected);
			}

			filterFunction ??= _ => true;

			HashSet<Asset> inventory;

			lock (TradingSemaphore) {
				if (TradingScheduled) {
					return (false, Strings.ErrorAborted);
				}

				TradingScheduled = true;
			}

			await TradingSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				lock (TradingSemaphore) {
					TradingScheduled = false;
				}

				inventory = await Bot.ArchiWebHandler.GetInventoryAsync(appID: appID, contextID: contextID).Where(item => item.Tradable && filterFunction(item)).ToHashSetAsync().ConfigureAwait(false);
			} catch (HttpRequestException e) {
				Bot.ArchiLogger.LogGenericWarningException(e);

				return (false, string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, e.Message));
			} catch (Exception e) {
				Bot.ArchiLogger.LogGenericException(e);

				return (false, string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, e.Message));
			} finally {
				TradingSemaphore.Release();
			}

			if (inventory.Count == 0) {
				return (false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(inventory)));
			}

			return await SendInventory(inventory, targetSteamID, tradeToken, itemsPerTrade).ConfigureAwait(false);
		}

		[PublicAPI]
		public (bool Success, string Message) Start() {
			if (Bot.KeepRunning) {
				return (false, Strings.BotAlreadyRunning);
			}

			Utilities.InBackground(Bot.Start);

			return (true, Strings.Done);
		}

		[PublicAPI]
		public (bool Success, string Message) Stop() {
			if (!Bot.KeepRunning) {
				return (false, Strings.BotAlreadyStopped);
			}

			Bot.Stop();

			return (true, Strings.Done);
		}

		[PublicAPI]
		public static async Task<(bool Success, string? Message, Version? Version)> Update() {
			Version? version = await ASF.Update(true).ConfigureAwait(false);

			if (version == null) {
				return (false, null, null);
			}

			if (SharedInfo.Version >= version) {
				return (false, "V" + SharedInfo.Version + " ≥ V" + version, version);
			}

			Utilities.InBackground(ASF.RestartOrExit);

			return (true, null, version);
		}

		internal async Task AcceptDigitalGiftCards() {
			if (!Bot.IsConnectedAndLoggedOn) {
				return;
			}

			lock (GiftCardsSemaphore) {
				if (ProcessingGiftsScheduled) {
					return;
				}

				ProcessingGiftsScheduled = true;
			}

			await GiftCardsSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				lock (GiftCardsSemaphore) {
					ProcessingGiftsScheduled = false;
				}

				if (!Bot.IsConnectedAndLoggedOn) {
					return;
				}

				HashSet<ulong>? giftCardIDs = await Bot.ArchiWebHandler.GetDigitalGiftCards().ConfigureAwait(false);

				if ((giftCardIDs == null) || (giftCardIDs.Count == 0)) {
					return;
				}

				foreach (ulong giftCardID in giftCardIDs.Where(gid => !HandledGifts.Contains(gid))) {
					HandledGifts.Add(giftCardID);

					Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.BotAcceptingGift, giftCardID));
					await LimitGiftsRequestsAsync().ConfigureAwait(false);

					bool result = await Bot.ArchiWebHandler.AcceptDigitalGiftCard(giftCardID).ConfigureAwait(false);

					if (result) {
						Bot.ArchiLogger.LogGenericInfo(Strings.Success);
					} else {
						Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
					}
				}
			} finally {
				GiftCardsSemaphore.Release();
			}
		}

		internal async Task AcceptGuestPasses(IReadOnlyCollection<ulong> guestPassIDs) {
			if ((guestPassIDs == null) || (guestPassIDs.Count == 0)) {
				throw new ArgumentNullException(nameof(guestPassIDs));
			}

			if (!Bot.IsConnectedAndLoggedOn) {
				return;
			}

			foreach (ulong guestPassID in guestPassIDs.Where(guestPassID => !HandledGifts.Contains(guestPassID))) {
				HandledGifts.Add(guestPassID);

				Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.BotAcceptingGift, guestPassID));
				await LimitGiftsRequestsAsync().ConfigureAwait(false);

				ArchiHandler.RedeemGuestPassResponseCallback? response = await Bot.ArchiHandler.RedeemGuestPass(guestPassID).ConfigureAwait(false);

				if (response != null) {
					if (response.Result == EResult.OK) {
						Bot.ArchiLogger.LogGenericInfo(Strings.Success);
					} else {
						Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, response.Result));
					}
				} else {
					Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				}
			}
		}

		internal void OnDisconnected() => HandledGifts.Clear();

		private ulong GetFirstSteamMasterID() {
			ulong steamMasterID = Bot.BotConfig.SteamUserPermissions.Where(kv => (kv.Key > 0) && (kv.Key != Bot.SteamID) && new SteamID(kv.Key).IsIndividualAccount && (kv.Value == BotConfig.EAccess.Master)).Select(kv => kv.Key).OrderBy(steamID => steamID).FirstOrDefault();

			if (steamMasterID > 0) {
				return steamMasterID;
			}

			ulong steamOwnerID = ASF.GlobalConfig?.SteamOwnerID ?? GlobalConfig.DefaultSteamOwnerID;

			return (steamOwnerID > 0) && new SteamID(steamOwnerID).IsIndividualAccount ? steamOwnerID : 0;
		}

		private static async Task LimitGiftsRequestsAsync() {
			if (ASF.GiftsSemaphore == null) {
				throw new InvalidOperationException(nameof(ASF.GiftsSemaphore));
			}

			byte giftsLimiterDelay = ASF.GlobalConfig?.GiftsLimiterDelay ?? GlobalConfig.DefaultGiftsLimiterDelay;

			if (giftsLimiterDelay == 0) {
				return;
			}

			await ASF.GiftsSemaphore.WaitAsync().ConfigureAwait(false);

			Utilities.InBackground(
				async () => {
					await Task.Delay(giftsLimiterDelay * 1000).ConfigureAwait(false);
					ASF.GiftsSemaphore.Release();
				}
			);
		}
	}
}
