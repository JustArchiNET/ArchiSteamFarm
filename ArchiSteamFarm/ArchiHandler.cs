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
		internal sealed class OfflineMessageCallback : CallbackMsg {
			internal uint OfflineMessages { get; private set; }
			internal List<uint> Users { get; private set; }
			internal OfflineMessageCallback(CMsgClientOfflineMessageNotification body) {
				OfflineMessages = body.offline_messages;
				Users = body.friends_with_offline_messages;
			}
		}

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

			var response = new ClientMsgProtobuf<CMsgClientOfflineMessageNotification>(packetMsg);
			Client.PostCallback(new OfflineMessageCallback(response.Body));
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
	}
}