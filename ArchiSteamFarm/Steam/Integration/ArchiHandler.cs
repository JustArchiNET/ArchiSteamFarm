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
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.NLog;
using ArchiSteamFarm.Steam.Data;
using ArchiSteamFarm.Steam.Integration.Callbacks;
using ArchiSteamFarm.Steam.Integration.CMsgs;
using ArchiSteamFarm.Web;
using JetBrains.Annotations;
using SteamKit2;
using SteamKit2.Internal;
using SteamKit2.WebUI.Internal;
using CMsgClientChangeStatus = SteamKit2.Internal.CMsgClientChangeStatus;
using CMsgClientCommentNotifications = SteamKit2.Internal.CMsgClientCommentNotifications;
using CMsgClientGamesPlayed = SteamKit2.Internal.CMsgClientGamesPlayed;
using CMsgClientItemAnnouncements = SteamKit2.Internal.CMsgClientItemAnnouncements;
using CMsgClientRedeemGuestPass = SteamKit2.Internal.CMsgClientRedeemGuestPass;
using CMsgClientRequestItemAnnouncements = SteamKit2.Internal.CMsgClientRequestItemAnnouncements;
using CMsgClientSharedLibraryLockStatus = SteamKit2.Internal.CMsgClientSharedLibraryLockStatus;
using CMsgClientUIMode = SteamKit2.Internal.CMsgClientUIMode;
using CMsgClientUserNotifications = SteamKit2.Internal.CMsgClientUserNotifications;
using EPersonaStateFlag = SteamKit2.EPersonaStateFlag;

namespace ArchiSteamFarm.Steam.Integration;

public sealed class ArchiHandler : ClientMsgHandler {
	internal const byte MaxGamesPlayedConcurrently = 32; // This is limit introduced by Steam Network

	private readonly ArchiLogger ArchiLogger;

	private readonly SteamUnifiedMessages.UnifiedService<IAccountPrivateApps> UnifiedAccountPrivateApps;
	private readonly SteamUnifiedMessages.UnifiedService<IChatRoom> UnifiedChatRoomService;
	private readonly SteamUnifiedMessages.UnifiedService<IClanChatRooms> UnifiedClanChatRoomsService;
	private readonly SteamUnifiedMessages.UnifiedService<ICredentials> UnifiedCredentialsService;
	private readonly SteamUnifiedMessages.UnifiedService<IEcon> UnifiedEconService;
	private readonly SteamUnifiedMessages.UnifiedService<IFamilyGroups> UnifiedFamilyGroups;
	private readonly SteamUnifiedMessages.UnifiedService<IFriendMessages> UnifiedFriendMessagesService;
	private readonly SteamUnifiedMessages.UnifiedService<IPlayer> UnifiedPlayerService;
	private readonly SteamUnifiedMessages.UnifiedService<IStore> UnifiedStoreService;
	private readonly SteamUnifiedMessages.UnifiedService<ITwoFactor> UnifiedTwoFactorService;

	internal DateTime LastPacketReceived { get; private set; }

	internal ArchiHandler(ArchiLogger archiLogger, SteamUnifiedMessages steamUnifiedMessages) {
		ArgumentNullException.ThrowIfNull(archiLogger);
		ArgumentNullException.ThrowIfNull(steamUnifiedMessages);

		ArchiLogger = archiLogger;

		UnifiedAccountPrivateApps = steamUnifiedMessages.CreateService<IAccountPrivateApps>();
		UnifiedChatRoomService = steamUnifiedMessages.CreateService<IChatRoom>();
		UnifiedClanChatRoomsService = steamUnifiedMessages.CreateService<IClanChatRooms>();
		UnifiedCredentialsService = steamUnifiedMessages.CreateService<ICredentials>();
		UnifiedEconService = steamUnifiedMessages.CreateService<IEcon>();
		UnifiedFamilyGroups = steamUnifiedMessages.CreateService<IFamilyGroups>();
		UnifiedFriendMessagesService = steamUnifiedMessages.CreateService<IFriendMessages>();
		UnifiedPlayerService = steamUnifiedMessages.CreateService<IPlayer>();
		UnifiedStoreService = steamUnifiedMessages.CreateService<IStore>();
		UnifiedTwoFactorService = steamUnifiedMessages.CreateService<ITwoFactor>();
	}

	[PublicAPI]
	public async Task<bool> AddFriend(ulong steamID) {
		if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		if (Client == null) {
			throw new InvalidOperationException(nameof(Client));
		}

		if (!Client.IsConnected) {
			return false;
		}

		CPlayer_AddFriend_Request request = new() { steamid = steamID };

		SteamUnifiedMessages.ServiceMethodResponse response;

		try {
			response = await UnifiedPlayerService.SendMessage(x => x.AddFriend(request)).ToLongRunningTask().ConfigureAwait(false);
		} catch (Exception e) {
			ArchiLogger.LogGenericWarningException(e);

			return false;
		}

		return response.Result == EResult.OK;
	}

	[PublicAPI]
	public async Task<CClanChatRooms_GetClanChatRoomInfo_Response?> GetClanChatRoomInfo(ulong steamID) {
		if ((steamID == 0) || !new SteamID(steamID).IsClanAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		if (Client == null) {
			throw new InvalidOperationException(nameof(Client));
		}

		if (!Client.IsConnected) {
			return null;
		}

		CClanChatRooms_GetClanChatRoomInfo_Request request = new() {
			autocreate = true,
			steamid = steamID
		};

		SteamUnifiedMessages.ServiceMethodResponse response;

		try {
			response = await UnifiedClanChatRoomsService.SendMessage(x => x.GetClanChatRoomInfo(request)).ToLongRunningTask().ConfigureAwait(false);
		} catch (Exception e) {
			ArchiLogger.LogGenericWarningException(e);

			return null;
		}

		return response.Result == EResult.OK ? response.GetDeserializedResponse<CClanChatRooms_GetClanChatRoomInfo_Response>() : null;
	}

	[PublicAPI]
	public async Task<CCredentials_LastCredentialChangeTime_Response?> GetCredentialChangeTimeDetails() {
		if (Client == null) {
			throw new InvalidOperationException(nameof(Client));
		}

		if (!Client.IsConnected) {
			return null;
		}

		CCredentials_LastCredentialChangeTime_Request request = new();

		SteamUnifiedMessages.ServiceMethodResponse response;

		try {
			response = await UnifiedCredentialsService.SendMessage(x => x.GetCredentialChangeTimeDetails(request)).ToLongRunningTask().ConfigureAwait(false);
		} catch (Exception e) {
			ArchiLogger.LogGenericWarningException(e);

			return null;
		}

		return response.Result == EResult.OK ? response.GetDeserializedResponse<CCredentials_LastCredentialChangeTime_Response>() : null;
	}

	[PublicAPI]
	public async IAsyncEnumerable<Asset> GetMyInventoryAsync(uint appID = Asset.SteamAppID, ulong contextID = Asset.SteamCommunityContextID, bool tradableOnly = false, bool marketableOnly = false, ushort itemsCountPerRequest = ArchiWebHandler.MaxItemsInSingleInventoryRequest) {
		ArgumentOutOfRangeException.ThrowIfZero(appID);
		ArgumentOutOfRangeException.ThrowIfZero(contextID);
		ArgumentOutOfRangeException.ThrowIfZero(itemsCountPerRequest);

		if (!Client.IsConnected || (Client.SteamID == null)) {
			throw new TimeoutException(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, nameof(Client.IsConnected)));
		}

		ulong steamID = Client.SteamID;

		if (steamID == 0) {
			throw new InvalidOperationException(nameof(Client.SteamID));
		}

		CEcon_GetInventoryItemsWithDescriptions_Request request = new() {
			appid = appID,
			contextid = contextID,

			filters = new CEcon_GetInventoryItemsWithDescriptions_Request.FilterOptions {
				tradable_only = tradableOnly,
				marketable_only = marketableOnly
			},

			get_descriptions = true,
			steamid = steamID,
			count = itemsCountPerRequest
		};

		// We need to store asset IDs to make sure we won't get duplicate items
		HashSet<ulong>? assetIDs = null;

		Dictionary<(ulong ClassID, ulong InstanceID), InventoryDescription>? descriptions = null;

		while (true) {
			SteamUnifiedMessages.ServiceMethodResponse? serviceMethodResponse = null;

			for (byte i = 0; (i < WebBrowser.MaxTries) && (serviceMethodResponse?.Result != EResult.OK) && Client.IsConnected && (Client.SteamID != null); i++) {
				if (i > 0) {
					// It seems 2 seconds is enough to win over DuplicateRequest, so we'll use that for this and also other network-related failures
					await Task.Delay(2000).ConfigureAwait(false);
				}

				try {
					serviceMethodResponse = await UnifiedEconService.SendMessage(x => x.GetInventoryItemsWithDescriptions(request)).ToLongRunningTask().ConfigureAwait(false);
				} catch (Exception e) {
					ArchiLogger.LogGenericWarningException(e);

					continue;
				}

				// Interpret the result and see what we should do about it
				switch (serviceMethodResponse.Result) {
					case EResult.OK:
						// Success, we can continue
						break;
					case EResult.Busy:
					case EResult.DuplicateRequest:
					case EResult.Fail:
					case EResult.RemoteCallFailed:
					case EResult.ServiceUnavailable:
					case EResult.Timeout:
						// Expected failures that we should be able to retry
						continue;
					case EResult.NoMatch:
						// Expected failures that we're not going to retry
						throw new TimeoutException(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, serviceMethodResponse.Result));
					default:
						// Unknown failures, report them and do not retry since we're unsure if we should
						ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(serviceMethodResponse.Result), serviceMethodResponse.Result));

						throw new TimeoutException(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, serviceMethodResponse.Result));
				}
			}

			if (serviceMethodResponse == null) {
				throw new TimeoutException(string.Format(CultureInfo.CurrentCulture, Strings.ErrorObjectIsNull, nameof(serviceMethodResponse)));
			}

			if (serviceMethodResponse.Result != EResult.OK) {
				throw new TimeoutException(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, serviceMethodResponse.Result));
			}

			CEcon_GetInventoryItemsWithDescriptions_Response response = serviceMethodResponse.GetDeserializedResponse<CEcon_GetInventoryItemsWithDescriptions_Response>();

			if ((response.total_inventory_count == 0) || (response.assets.Count == 0)) {
				// Empty inventory
				yield break;
			}

			if (response.descriptions.Count == 0) {
				throw new InvalidOperationException(nameof(response.descriptions));
			}

			if (response.total_inventory_count > Array.MaxLength) {
				throw new InvalidOperationException(nameof(response.total_inventory_count));
			}

			assetIDs ??= new HashSet<ulong>((int) response.total_inventory_count);

			if (descriptions == null) {
				descriptions = new Dictionary<(ulong ClassID, ulong InstanceID), InventoryDescription>();
			} else {
				// We don't need descriptions from the previous request
				descriptions.Clear();
			}

			foreach (CEconItem_Description? description in response.descriptions) {
				if (description.classid == 0) {
					throw new NotSupportedException(string.Format(CultureInfo.CurrentCulture, Strings.ErrorObjectIsNull, nameof(description.classid)));
				}

				(ulong ClassID, ulong InstanceID) key = (description.classid, description.instanceid);

				if (descriptions.ContainsKey(key)) {
					continue;
				}

				descriptions.Add(key, new InventoryDescription(description));
			}

			foreach (CEcon_Asset? asset in response.assets.Where(asset => assetIDs.Add(asset.assetid))) {
				InventoryDescription? description = descriptions.GetValueOrDefault((asset.classid, asset.instanceid));

				// Extra bulletproofing against Steam showing us middle finger
				if ((tradableOnly && (description?.Tradable != true)) || (marketableOnly && (description?.Marketable != true))) {
					continue;
				}

				yield return new Asset(asset, description);
			}

			if (!response.more_items) {
				yield break;
			}

			if (response.last_assetid == 0) {
				throw new NotSupportedException(string.Format(CultureInfo.CurrentCulture, Strings.ErrorObjectIsNull, nameof(response.last_assetid)));
			}

			request.start_assetid = response.last_assetid;
		}
	}

	[PublicAPI]
	public async Task<Dictionary<uint, string>?> GetOwnedGames(ulong steamID) {
		if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		if (Client == null) {
			throw new InvalidOperationException(nameof(Client));
		}

		if (!Client.IsConnected) {
			return null;
		}

		CPlayer_GetOwnedGames_Request request = new() {
			steamid = steamID,
			include_appinfo = true,
			include_free_sub = true,
			include_played_free_games = true,
			skip_unvetted_apps = false
		};

		SteamUnifiedMessages.ServiceMethodResponse response;

		try {
			response = await UnifiedPlayerService.SendMessage(x => x.GetOwnedGames(request)).ToLongRunningTask().ConfigureAwait(false);
		} catch (Exception e) {
			ArchiLogger.LogGenericWarningException(e);

			return null;
		}

		if (response.Result != EResult.OK) {
			return null;
		}

		CPlayer_GetOwnedGames_Response body = response.GetDeserializedResponse<CPlayer_GetOwnedGames_Response>();

		return body.games.ToDictionary(static game => (uint) game.appid, static game => game.name);
	}

	[PublicAPI]
	public async Task<CCredentials_GetSteamGuardDetails_Response?> GetSteamGuardStatus() {
		if (Client == null) {
			throw new InvalidOperationException(nameof(Client));
		}

		if (!Client.IsConnected) {
			return null;
		}

		CCredentials_GetSteamGuardDetails_Request request = new();

		SteamUnifiedMessages.ServiceMethodResponse response;

		try {
			response = await UnifiedCredentialsService.SendMessage(x => x.GetSteamGuardDetails(request)).ToLongRunningTask().ConfigureAwait(false);
		} catch (Exception e) {
			ArchiLogger.LogGenericWarningException(e);

			return null;
		}

		return response.Result == EResult.OK ? response.GetDeserializedResponse<CCredentials_GetSteamGuardDetails_Response>() : null;
	}

	[PublicAPI]
	public async Task<string?> GetTradeToken() {
		if (Client == null) {
			throw new InvalidOperationException(nameof(Client));
		}

		if (!Client.IsConnected) {
			return null;
		}

		CEcon_GetTradeOfferAccessToken_Request request = new();

		SteamUnifiedMessages.ServiceMethodResponse response;

		try {
			response = await UnifiedEconService.SendMessage(x => x.GetTradeOfferAccessToken(request)).ToLongRunningTask().ConfigureAwait(false);
		} catch (Exception e) {
			ArchiLogger.LogGenericWarningException(e);

			return null;
		}

		if (response.Result != EResult.OK) {
			return null;
		}

		CEcon_GetTradeOfferAccessToken_Response body = response.GetDeserializedResponse<CEcon_GetTradeOfferAccessToken_Response>();

		return body.trade_offer_access_token;
	}

	public override void HandleMsg(IPacketMsg packetMsg) {
		ArgumentNullException.ThrowIfNull(packetMsg);

		if (Client == null) {
			throw new InvalidOperationException(nameof(Client));
		}

		LastPacketReceived = DateTime.UtcNow;

		switch (packetMsg.MsgType) {
			case EMsg.ClientCommentNotifications:
				ClientMsgProtobuf<CMsgClientCommentNotifications> commentNotifications = new(packetMsg);
				Client.PostCallback(new UserNotificationsCallback(packetMsg.TargetJobID, commentNotifications.Body));

				break;
			case EMsg.ClientItemAnnouncements:
				ClientMsgProtobuf<CMsgClientItemAnnouncements> itemAnnouncements = new(packetMsg);
				Client.PostCallback(new UserNotificationsCallback(packetMsg.TargetJobID, itemAnnouncements.Body));

				break;
			case EMsg.ClientSharedLibraryLockStatus:
				ClientMsgProtobuf<CMsgClientSharedLibraryLockStatus> sharedLibraryLockStatus = new(packetMsg);
				Client.PostCallback(new SharedLibraryLockStatusCallback(packetMsg.TargetJobID, sharedLibraryLockStatus.Body));

				break;
			case EMsg.ClientUserNotifications:
				ClientMsgProtobuf<CMsgClientUserNotifications> userNotifications = new(packetMsg);
				Client.PostCallback(new UserNotificationsCallback(packetMsg.TargetJobID, userNotifications.Body));

				break;
		}
	}

	[PublicAPI]
	public async Task<bool> JoinChatRoomGroup(ulong chatGroupID) {
		ArgumentOutOfRangeException.ThrowIfZero(chatGroupID);

		if (Client == null) {
			throw new InvalidOperationException(nameof(Client));
		}

		if (!Client.IsConnected) {
			return false;
		}

		CChatRoom_JoinChatRoomGroup_Request request = new() { chat_group_id = chatGroupID };

		SteamUnifiedMessages.ServiceMethodResponse response;

		try {
			response = await UnifiedChatRoomService.SendMessage(x => x.JoinChatRoomGroup(request)).ToLongRunningTask().ConfigureAwait(false);
		} catch (Exception e) {
			ArchiLogger.LogGenericWarningException(e);

			return false;
		}

		return response.Result == EResult.OK;
	}

	[PublicAPI]
	public async Task<bool> LeaveChatRoomGroup(ulong chatGroupID) {
		ArgumentOutOfRangeException.ThrowIfZero(chatGroupID);

		if (Client == null) {
			throw new InvalidOperationException(nameof(Client));
		}

		if (!Client.IsConnected) {
			return false;
		}

		CChatRoom_LeaveChatRoomGroup_Request request = new() { chat_group_id = chatGroupID };

		SteamUnifiedMessages.ServiceMethodResponse response;

		try {
			response = await UnifiedChatRoomService.SendMessage(x => x.LeaveChatRoomGroup(request)).ToLongRunningTask().ConfigureAwait(false);
		} catch (Exception e) {
			ArchiLogger.LogGenericWarningException(e);

			return false;
		}

		return response.Result == EResult.OK;
	}

	[PublicAPI]
	public async Task<bool> RemoveFriend(ulong steamID) {
		if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		if (Client == null) {
			throw new InvalidOperationException(nameof(Client));
		}

		if (!Client.IsConnected) {
			return false;
		}

		CPlayer_RemoveFriend_Request request = new() { steamid = steamID };

		SteamUnifiedMessages.ServiceMethodResponse response;

		try {
			response = await UnifiedPlayerService.SendMessage(x => x.RemoveFriend(request)).ToLongRunningTask().ConfigureAwait(false);
		} catch (Exception e) {
			ArchiLogger.LogGenericWarningException(e);

			return false;
		}

		return response.Result == EResult.OK;
	}

	internal void AckChatMessage(ulong chatGroupID, ulong chatID, uint timestamp) {
		ArgumentOutOfRangeException.ThrowIfZero(chatGroupID);
		ArgumentOutOfRangeException.ThrowIfZero(chatID);
		ArgumentOutOfRangeException.ThrowIfZero(timestamp);

		if (Client == null) {
			throw new InvalidOperationException(nameof(Client));
		}

		if (!Client.IsConnected) {
			return;
		}

		CChatRoom_AckChatMessage_Notification request = new() {
			chat_group_id = chatGroupID,
			chat_id = chatID,
			timestamp = timestamp
		};

		UnifiedChatRoomService.SendNotification(x => x.AckChatMessage(request));
	}

	internal void AckMessage(ulong steamID, uint timestamp) {
		if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		ArgumentOutOfRangeException.ThrowIfZero(timestamp);

		if (Client == null) {
			throw new InvalidOperationException(nameof(Client));
		}

		if (!Client.IsConnected) {
			return;
		}

		CFriendMessages_AckMessage_Notification request = new() {
			steamid_partner = steamID,
			timestamp = timestamp
		};

		UnifiedFriendMessagesService.SendNotification(x => x.AckMessage(request));
	}

	internal void AcknowledgeClanInvite(ulong steamID, bool acceptInvite) {
		if ((steamID == 0) || !new SteamID(steamID).IsClanAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		if (Client == null) {
			throw new InvalidOperationException(nameof(Client));
		}

		if (!Client.IsConnected) {
			return;
		}

		ClientMsg<CMsgClientAcknowledgeClanInvite> request = new() {
			Body = {
				ClanID = steamID,
				AcceptInvite = acceptInvite
			}
		};

		Client.Send(request);
	}

	internal async Task<HashSet<ulong>?> GetFamilyGroupSteamIDs() {
		if (Client == null) {
			throw new InvalidOperationException(nameof(Client));
		}

		if (!Client.IsConnected) {
			return null;
		}

		if (Client.SteamID == null) {
			throw new InvalidOperationException(nameof(Client.SteamID));
		}

		ulong steamID = Client.SteamID;

		CFamilyGroups_GetFamilyGroupForUser_Request request = new() {
			include_family_group_response = true,
			steamid = steamID
		};

		SteamUnifiedMessages.ServiceMethodResponse response;

		try {
			response = await UnifiedFamilyGroups.SendMessage(x => x.GetFamilyGroupForUser(request)).ToLongRunningTask().ConfigureAwait(false);
		} catch (Exception e) {
			ArchiLogger.LogGenericWarningException(e);

			return null;
		}

		if (response.Result != EResult.OK) {
			return null;
		}

		CFamilyGroups_GetFamilyGroupForUser_Response body = response.GetDeserializedResponse<CFamilyGroups_GetFamilyGroupForUser_Response>();

		return body.family_group?.members.Where(member => member.steamid != steamID).Select(static member => member.steamid).ToHashSet() ?? [];
	}

	internal async Task<uint?> GetLevel() {
		if (Client == null) {
			throw new InvalidOperationException(nameof(Client));
		}

		if (!Client.IsConnected) {
			return null;
		}

		CPlayer_GetGameBadgeLevels_Request request = new();
		SteamUnifiedMessages.ServiceMethodResponse response;

		try {
			response = await UnifiedPlayerService.SendMessage(x => x.GetGameBadgeLevels(request)).ToLongRunningTask().ConfigureAwait(false);
		} catch (Exception e) {
			ArchiLogger.LogGenericWarningException(e);

			return null;
		}

		if (response.Result != EResult.OK) {
			return null;
		}

		CPlayer_GetGameBadgeLevels_Response body = response.GetDeserializedResponse<CPlayer_GetGameBadgeLevels_Response>();

		return body.player_level;
	}

	internal async Task<HashSet<ulong>?> GetMyChatGroupIDs() {
		if (Client == null) {
			throw new InvalidOperationException(nameof(Client));
		}

		if (!Client.IsConnected) {
			return null;
		}

		CChatRoom_GetMyChatRoomGroups_Request request = new();

		SteamUnifiedMessages.ServiceMethodResponse response;

		try {
			response = await UnifiedChatRoomService.SendMessage(x => x.GetMyChatRoomGroups(request)).ToLongRunningTask().ConfigureAwait(false);
		} catch (Exception e) {
			ArchiLogger.LogGenericWarningException(e);

			return null;
		}

		if (response.Result != EResult.OK) {
			return null;
		}

		CChatRoom_GetMyChatRoomGroups_Response body = response.GetDeserializedResponse<CChatRoom_GetMyChatRoomGroups_Response>();

		return body.chat_room_groups.Select(static chatRoom => chatRoom.group_summary.chat_group_id).ToHashSet();
	}

	internal async Task<CPrivacySettings?> GetPrivacySettings() {
		if (Client == null) {
			throw new InvalidOperationException(nameof(Client));
		}

		if (!Client.IsConnected) {
			return null;
		}

		CPlayer_GetPrivacySettings_Request request = new();

		SteamUnifiedMessages.ServiceMethodResponse response;

		try {
			response = await UnifiedPlayerService.SendMessage(x => x.GetPrivacySettings(request)).ToLongRunningTask().ConfigureAwait(false);
		} catch (Exception e) {
			ArchiLogger.LogGenericWarningException(e);

			return null;
		}

		if (response.Result != EResult.OK) {
			return null;
		}

		CPlayer_GetPrivacySettings_Response body = response.GetDeserializedResponse<CPlayer_GetPrivacySettings_Response>();

		return body.privacy_settings;
	}

	internal async Task<HashSet<uint>?> GetPrivateAppIDs() {
		if (Client == null) {
			throw new InvalidOperationException(nameof(Client));
		}

		if (!Client.IsConnected) {
			return null;
		}

		CAccountPrivateApps_GetPrivateAppList_Request request = new();

		SteamUnifiedMessages.ServiceMethodResponse response;

		try {
			response = await UnifiedAccountPrivateApps.SendMessage(x => x.GetPrivateAppList(request)).ToLongRunningTask().ConfigureAwait(false);
		} catch (Exception e) {
			ArchiLogger.LogGenericWarningException(e);

			return null;
		}

		if (response.Result != EResult.OK) {
			return null;
		}

		CAccountPrivateApps_GetPrivateAppList_Response body = response.GetDeserializedResponse<CAccountPrivateApps_GetPrivateAppList_Response>();

		return body.private_apps.appids.Select(static appID => (uint) appID).ToHashSet();
	}

	internal async Task<ulong> GetServerTime() {
		if (Client == null) {
			throw new InvalidOperationException(nameof(Client));
		}

		if (!Client.IsConnected) {
			return 0;
		}

		CTwoFactor_Time_Request request = new();

		SteamUnifiedMessages.ServiceMethodResponse response;

		try {
			response = await UnifiedTwoFactorService.SendMessage(x => x.QueryTime(request)).ToLongRunningTask().ConfigureAwait(false);
		} catch (Exception e) {
			ArchiLogger.LogGenericWarningException(e);

			return 0;
		}

		if (response.Result != EResult.OK) {
			return 0;
		}

		CTwoFactor_Time_Response body = response.GetDeserializedResponse<CTwoFactor_Time_Response>();

		return body.server_time;
	}

	internal async Task<string?> GetTwoFactorDeviceIdentifier(ulong steamID) {
		if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		if (Client == null) {
			throw new InvalidOperationException(nameof(Client));
		}

		if (!Client.IsConnected) {
			return null;
		}

		CTwoFactor_Status_Request request = new() {
			steamid = steamID
		};

		SteamUnifiedMessages.ServiceMethodResponse response;

		try {
			response = await UnifiedTwoFactorService.SendMessage(x => x.QueryStatus(request)).ToLongRunningTask().ConfigureAwait(false);
		} catch (Exception e) {
			ArchiLogger.LogGenericWarningException(e);

			return null;
		}

		if (response.Result != EResult.OK) {
			return null;
		}

		CTwoFactor_Status_Response body = response.GetDeserializedResponse<CTwoFactor_Status_Response>();

		return body.device_identifier;
	}

	internal async Task PlayGames(IReadOnlyCollection<uint> gameIDs, string? gameName = null) {
		ArgumentNullException.ThrowIfNull(gameIDs);

		if (Client == null) {
			throw new InvalidOperationException(nameof(Client));
		}

		if (!Client.IsConnected) {
			return;
		}

		ClientMsgProtobuf<CMsgClientGamesPlayed> request = new(EMsg.ClientGamesPlayedWithDataBlob) {
			Body = {
				// Underflow here is to be expected, this is Steam's logic
				client_os_type = unchecked((uint) Bot.OSType)
			}
		};

		if (!string.IsNullOrEmpty(gameName)) {
			// If we have custom name to display, we must workaround the Steam network broken behaviour and send request on clean non-playing session
			// This ensures that custom name will in fact display properly (if it's not omitted due to MaxGamesPlayedConcurrently, that is)
			Client.Send(request);
			await Task.Delay(Bot.CallbackSleep).ConfigureAwait(false);

			request.Body.games_played.Add(
				new CMsgClientGamesPlayed.GamePlayed {
					game_extra_info = gameName,
					game_id = new GameID {
						AppType = GameID.GameType.Shortcut,
						ModID = uint.MaxValue
					}
				}
			);
		}

		if (gameIDs.Count > 0) {
			IReadOnlySet<uint> uniqueGameIDs = gameIDs as IReadOnlySet<uint> ?? gameIDs.ToHashSet();

			foreach (uint gameID in uniqueGameIDs.Where(static gameID => gameID > 0)) {
				if (request.Body.games_played.Count >= MaxGamesPlayedConcurrently) {
					if (string.IsNullOrEmpty(gameName)) {
						throw new ArgumentOutOfRangeException(nameof(gameIDs));
					}

					// Make extra space by ditching custom gameName
					gameName = null;

					request.Body.games_played.RemoveAt(0);
				}

				request.Body.games_played.Add(new CMsgClientGamesPlayed.GamePlayed { game_id = new GameID(gameID) });
			}
		}

		Client.Send(request);
	}

	internal async Task<SteamApps.RedeemGuestPassResponseCallback?> RedeemGuestPass(ulong guestPassID) {
		ArgumentOutOfRangeException.ThrowIfZero(guestPassID);

		if (Client == null) {
			throw new InvalidOperationException(nameof(Client));
		}

		if (!Client.IsConnected) {
			return null;
		}

		ClientMsgProtobuf<CMsgClientRedeemGuestPass> request = new(EMsg.ClientRedeemGuestPass) {
			SourceJobID = Client.GetNextJobID(),
			Body = { guest_pass_id = guestPassID }
		};

		Client.Send(request);

		try {
			return await new AsyncJob<SteamApps.RedeemGuestPassResponseCallback>(Client, request.SourceJobID).ToLongRunningTask().ConfigureAwait(false);
		} catch (Exception e) {
			ArchiLogger.LogGenericException(e);

			return null;
		}
	}

	internal async Task<CStore_RegisterCDKey_Response?> RedeemKey(string key) {
		ArgumentException.ThrowIfNullOrEmpty(key);

		if (Client == null) {
			throw new InvalidOperationException(nameof(Client));
		}

		if (!Client.IsConnected) {
			return null;
		}

		CStore_RegisterCDKey_Request request = new() {
			activation_code = key,
			is_request_from_client = true
		};

		SteamUnifiedMessages.ServiceMethodResponse response;

		try {
			response = await UnifiedStoreService.SendMessage(x => x.RegisterCDKey(request)).ToLongRunningTask().ConfigureAwait(false);
		} catch (Exception e) {
			ArchiLogger.LogGenericWarningException(e);

			return null;
		}

		// We want to deserialize the response even with failed EResult
		return response.GetDeserializedResponse<CStore_RegisterCDKey_Response>();
	}

	internal void RequestItemAnnouncements() {
		if (Client == null) {
			throw new InvalidOperationException(nameof(Client));
		}

		if (!Client.IsConnected) {
			return;
		}

		ClientMsgProtobuf<CMsgClientRequestItemAnnouncements> request = new(EMsg.ClientRequestItemAnnouncements);
		Client.Send(request);
	}

	internal async Task<EResult> SendMessage(ulong steamID, string message) {
		if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		ArgumentException.ThrowIfNullOrEmpty(message);

		if (Client == null) {
			throw new InvalidOperationException(nameof(Client));
		}

		if (!Client.IsConnected) {
			return EResult.NoConnection;
		}

		CFriendMessages_SendMessage_Request request = new() {
			chat_entry_type = (int) EChatEntryType.ChatMsg,
			contains_bbcode = true,
			message = message,
			steamid = steamID
		};

		SteamUnifiedMessages.ServiceMethodResponse response;

		try {
			response = await UnifiedFriendMessagesService.SendMessage(x => x.SendMessage(request)).ToLongRunningTask().ConfigureAwait(false);
		} catch (Exception e) {
			ArchiLogger.LogGenericWarningException(e);

			return EResult.Timeout;
		}

		return response.Result;
	}

	internal async Task<EResult> SendMessage(ulong chatGroupID, ulong chatID, string message) {
		ArgumentOutOfRangeException.ThrowIfZero(chatGroupID);
		ArgumentOutOfRangeException.ThrowIfZero(chatID);
		ArgumentException.ThrowIfNullOrEmpty(message);

		if (Client == null) {
			throw new InvalidOperationException(nameof(Client));
		}

		if (!Client.IsConnected) {
			return EResult.NoConnection;
		}

		CChatRoom_SendChatMessage_Request request = new() {
			chat_group_id = chatGroupID,
			chat_id = chatID,
			message = message
		};

		SteamUnifiedMessages.ServiceMethodResponse response;

		try {
			response = await UnifiedChatRoomService.SendMessage(x => x.SendChatMessage(request)).ToLongRunningTask().ConfigureAwait(false);
		} catch (Exception e) {
			ArchiLogger.LogGenericWarningException(e);

			return EResult.Timeout;
		}

		return response.Result;
	}

	internal async Task<EResult> SendTypingStatus(ulong steamID) {
		if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		if (Client == null) {
			throw new InvalidOperationException(nameof(Client));
		}

		if (!Client.IsConnected) {
			return EResult.NoConnection;
		}

		CFriendMessages_SendMessage_Request request = new() {
			chat_entry_type = (int) EChatEntryType.Typing,
			steamid = steamID
		};

		SteamUnifiedMessages.ServiceMethodResponse response;

		try {
			response = await UnifiedFriendMessagesService.SendMessage(x => x.SendMessage(request)).ToLongRunningTask().ConfigureAwait(false);
		} catch (Exception e) {
			ArchiLogger.LogGenericWarningException(e);

			return EResult.Timeout;
		}

		return response.Result;
	}

	internal void SetCurrentMode(EUserInterfaceMode userInterfaceMode, byte chatMode = 2) {
		if (!Enum.IsDefined(userInterfaceMode)) {
			throw new InvalidEnumArgumentException(nameof(userInterfaceMode), (int) userInterfaceMode, typeof(EUserInterfaceMode));
		}

		ArgumentOutOfRangeException.ThrowIfZero(chatMode);

		if (Client == null) {
			throw new InvalidOperationException(nameof(Client));
		}

		if (!Client.IsConnected) {
			return;
		}

		ClientMsgProtobuf<CMsgClientUIMode> request = new(EMsg.ClientCurrentUIMode) {
			Body = {
				uimode = (uint) userInterfaceMode,
				chat_mode = chatMode
			}
		};

		Client.Send(request);
	}

	internal void SetPersonaState(EPersonaState state, EPersonaStateFlag flags) {
		if (!Enum.IsDefined(state)) {
			throw new InvalidEnumArgumentException(nameof(state), (int) state, typeof(EPersonaState));
		}

		if (flags < 0) {
			throw new InvalidEnumArgumentException(nameof(flags), (int) flags, typeof(EPersonaStateFlag));
		}

		if (Client == null) {
			throw new InvalidOperationException(nameof(Client));
		}

		if (!Client.IsConnected) {
			return;
		}

		ClientMsgProtobuf<CMsgClientChangeStatus> request = new(EMsg.ClientChangeStatus) {
			Body = {
				persona_state = (uint) state,
				persona_state_flags = (uint) flags
			}
		};

		Client.Send(request);
	}

	[PublicAPI]
	public enum EUserInterfaceMode : byte {
		Default = 0,
		BigPicture = 1,
		Mobile = 2
	}

	internal enum EPrivacySetting : byte {
		Unknown,
		Private,
		FriendsOnly,
		Public
	}
}
