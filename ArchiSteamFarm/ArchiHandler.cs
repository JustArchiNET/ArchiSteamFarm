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

namespace ArchiSteamFarm {
	internal sealed class ArchiHandler : ClientMsgHandler {

		internal sealed class PurchaseResponseCallback : CallbackMsg {
			internal enum EPurchaseResult {
				Unknown = -1,
				OK = 0,
				AlreadyOwned = 9,
				InvalidKey = 14,
				DuplicatedKey = 15,
				OnCooldown = 53
			}

			internal EResult Result { get; private set; }
			internal EPurchaseResult PurchaseResult { get; private set; }
			internal int ErrorCode { get; private set; }
			internal byte[] ReceiptInfo { get; private set; }

			internal PurchaseResponseCallback(CMsgClientPurchaseResponse body) {
				Result = (EResult) body.eresult;
				ErrorCode = body.purchase_result_details;
				ReceiptInfo = body.purchase_receipt_info;
				PurchaseResult = (EPurchaseResult) ErrorCode;
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

		internal void PlayGames(params ulong[] gameIDs) {
			var request = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayed);
			foreach (ulong gameID in gameIDs) {
				if (gameID != 0) {
					request.Body.games_played.Add(new CMsgClientGamesPlayed.GamePlayed {
						game_id = new GameID(gameID),
					});
				}
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
			if (packetMsg != null) {
				switch (packetMsg.MsgType) {
					case EMsg.ClientPurchaseResponse:
						HandlePurchaseResponse(packetMsg);
						break;
					case EMsg.ClientUserNotifications:
						HandleUserNotifications(packetMsg);
						break;
				}
			}
		}

		private void HandlePurchaseResponse(IPacketMsg packetMsg) {
			var response = new ClientMsgProtobuf<CMsgClientPurchaseResponse>(packetMsg);
			Client.PostCallback(new PurchaseResponseCallback(response.Body));
		}

		private void HandleUserNotifications(IPacketMsg packetMsg) {
			var response = new ClientMsgProtobuf<CMsgClientUserNotifications>(packetMsg);
			foreach (var notification in response.Body.notifications) {
				Client.PostCallback(new NotificationCallback(notification));
			}
		}
	}
}