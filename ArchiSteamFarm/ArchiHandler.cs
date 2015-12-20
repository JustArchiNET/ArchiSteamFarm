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

using SteamKit2;
using SteamKit2.Internal;
using System;
using System.Collections.Generic;
using System.IO;

namespace ArchiSteamFarm {
	internal sealed class ArchiHandler : ClientMsgHandler {

		internal sealed class PurchaseResponseCallback : CallbackMsg {
			internal enum EPurchaseResult {
				Unknown = -1,
				OK = 0,
				AlreadyOwned = 9,
				RegionLockedKey = 13,
				InvalidKey = 14,
				DuplicatedKey = 15,
				BaseGameRequired = 24,
				OnCooldown = 53
			}

			internal EResult Result { get; private set; }
			internal EPurchaseResult PurchaseResult { get; private set; }
			internal KeyValue ReceiptInfo { get; private set; } = new KeyValue();
			internal Dictionary<uint, string> Items { get; private set; } = new Dictionary<uint, string>();

			internal PurchaseResponseCallback(CMsgClientPurchaseResponse body) {
				Result = (EResult) body.eresult;
				PurchaseResult = (EPurchaseResult) body.purchase_result_details;

				using (MemoryStream ms = new MemoryStream(body.purchase_receipt_info)) {
					if (!ReceiptInfo.TryReadAsBinary(ms)) {
						return;
					}

					foreach (KeyValue lineItem in ReceiptInfo["lineitems"].Children) {
						Items.Add((uint) lineItem["PackageID"].AsUnsignedLong(), lineItem["ItemDescription"].AsString());
					}
				}
			}
		}

		internal sealed class NotificationCallback : CallbackMsg {
			internal enum ENotificationType {
				Unknown = 0,
				Trading = 1,
			}

			internal ENotificationType NotificationType { get; private set; }

			internal NotificationCallback(CMsgClientUserNotifications.Notification body) {
				NotificationType = (ENotificationType) body.user_notification_type;
			}
		}

		internal void AcceptClanInvite(ulong clanID) {
			var request = new ClientMsg<CMsgClientClanInviteAction>((int) EMsg.ClientAcknowledgeClanInvite);
			request.Body.GroupID = clanID;
			request.Body.AcceptInvite = true;
			Client.Send(request);
		}

		internal void DeclineClanInvite(ulong clanID) {
			var request = new ClientMsg<CMsgClientClanInviteAction>((int) EMsg.ClientAcknowledgeClanInvite);
			request.Body.GroupID = clanID;
			request.Body.AcceptInvite = false;
			Client.Send(request);
		}

		internal void PlayGames(params uint[] gameIDs) {
			var request = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayed);
			foreach (uint gameID in gameIDs) {
				if (gameID == 0) {
					continue;
				}

				request.Body.games_played.Add(new CMsgClientGamesPlayed.GamePlayed {
					game_id = new GameID(gameID),
				});
			}
			Client.Send(request);
		}

		internal void PlayGames(ICollection<uint> gameIDs) {
			var request = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayed);
			foreach (uint gameID in gameIDs) {
				if (gameID == 0) {
					continue;
				}

				request.Body.games_played.Add(new CMsgClientGamesPlayed.GamePlayed {
					game_id = new GameID(gameID),
				});
			}
			Client.Send(request);
		}

		// Will provide result in ClientPurchaseResponse, regardless if success or not
		internal void RedeemKey(string key) {
			var request = new ClientMsgProtobuf<CMsgClientRegisterKey>(EMsg.ClientRegisterKey);
			request.Body.key = key;
			Client.Send(request);
		}

		public sealed override void HandleMsg(IPacketMsg packetMsg) {
			if (packetMsg == null) {
				return;
			}

			switch (packetMsg.MsgType) {
				case EMsg.ClientFSOfflineMessageNotification:
					HandleFSOfflineMessageNotification(packetMsg);
					break;
				case EMsg.ClientPurchaseResponse:
					HandlePurchaseResponse(packetMsg);
					break;
				case EMsg.ClientUserNotifications:
					HandleUserNotifications(packetMsg);
					break;
			}
		}

		private void HandleFSOfflineMessageNotification(IPacketMsg packetMsg) {
			if (packetMsg == null) {
				return;
			}

			// TODO: Handle offline messages?
		}

		private void HandlePurchaseResponse(IPacketMsg packetMsg) {
			if (packetMsg == null) {
				return;
			}

			var response = new ClientMsgProtobuf<CMsgClientPurchaseResponse>(packetMsg);
			Client.PostCallback(new PurchaseResponseCallback(response.Body));
		}

		private void HandleUserNotifications(IPacketMsg packetMsg) {
			if (packetMsg == null) {
				return;
			}

			var response = new ClientMsgProtobuf<CMsgClientUserNotifications>(packetMsg);
			foreach (var notification in response.Body.notifications) {
				Client.PostCallback(new NotificationCallback(notification));
			}
		}

		// TODO: Please remove me entirely once https://github.com/SteamRE/SteamKit/pull/217 gets merged
		internal void HackedLogOn(uint id, SteamUser.LogOnDetails details) {
			if (details == null) {
				throw new ArgumentNullException("details");
			}

			if (string.IsNullOrEmpty(details.Username) || (string.IsNullOrEmpty(details.Password) && string.IsNullOrEmpty(details.LoginKey))) {
				throw new ArgumentException("LogOn requires a username and password to be set in 'details'.");
			}

			if (!string.IsNullOrEmpty(details.LoginKey) && !details.ShouldRememberPassword) {
				// Prevent consumers from screwing this up.
				// If should_remember_password is false, the login_key is ignored server-side.
				// The inverse is not applicable (you can log in with should_remember_password and no login_key).
				throw new ArgumentException("ShouldRememberPassword is required to be set to true in order to use LoginKey.");
			}

			if (!Client.IsConnected) {
				return;
			}

			var logon = new ClientMsgProtobuf<CMsgClientLogon>(EMsg.ClientLogon);

			SteamID steamId = new SteamID(details.AccountID, details.AccountInstance, Client.ConnectedUniverse, EAccountType.Individual);

			logon.ProtoHeader.client_sessionid = 0;
			logon.ProtoHeader.steamid = steamId.ConvertToUInt64();

			logon.Body.obfustucated_private_ip = id;

			logon.Body.account_name = details.Username;
			logon.Body.password = details.Password;
			logon.Body.should_remember_password = details.ShouldRememberPassword;

			logon.Body.protocol_version = MsgClientLogon.CurrentProtocol;
			logon.Body.client_os_type = (uint) details.ClientOSType;
			logon.Body.client_language = details.ClientLanguage;
			logon.Body.cell_id = details.CellID;

			logon.Body.steam2_ticket_request = details.RequestSteam2Ticket;

			logon.Body.client_package_version = 1771;

			// steam guard 
			logon.Body.auth_code = details.AuthCode;
			logon.Body.two_factor_code = details.TwoFactorCode;

			logon.Body.login_key = details.LoginKey;

			logon.Body.sha_sentryfile = details.SentryFileHash;
			logon.Body.eresult_sentryfile = (int) (details.SentryFileHash != null ? EResult.OK : EResult.FileNotFound);

			Client.Send(logon);
		}
	}
}