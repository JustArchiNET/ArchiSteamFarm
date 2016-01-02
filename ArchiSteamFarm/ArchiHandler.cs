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
using System.Collections.Generic;
using System.IO;

namespace ArchiSteamFarm {
	internal sealed class ArchiHandler : ClientMsgHandler {
		/*
		  ____        _  _  _                   _
		 / ___| __ _ | || || |__    __ _   ___ | | __ ___
		| |    / _` || || || '_ \  / _` | / __|| |/ // __|
		| |___| (_| || || || |_) || (_| || (__ |   < \__ \
		 \____|\__,_||_||_||_.__/  \__,_| \___||_|\_\|___/

		*/

		internal sealed class NotificationsCallback : CallbackMsg {
			internal class Notification {
				internal enum ENotificationType {
					Unknown = 0,
					Trading = 1,
				}

				internal ENotificationType NotificationType { get; set; }
			}

			internal List<Notification> Notifications { get; private set; }

			internal NotificationsCallback(JobID jobID, CMsgClientUserNotifications msg) {
				JobID = jobID;

				if (msg == null) {
					return;
				}

				Notifications = new List<Notification>();
				foreach (var notification in msg.notifications) {
					Notifications.Add(new Notification {
						NotificationType = (Notification.ENotificationType) notification.user_notification_type
					});
				}
			}
		}

		internal sealed class OfflineMessageCallback : CallbackMsg {
			internal uint OfflineMessages { get; private set; }
			internal List<uint> Users { get; private set; }

			internal OfflineMessageCallback(JobID jobID, CMsgClientOfflineMessageNotification msg) {
				JobID = jobID;

				if (msg == null) {
					return;
				}

				OfflineMessages = msg.offline_messages;
				Users = msg.friends_with_offline_messages;
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
			internal KeyValue ReceiptInfo { get; private set; }
			internal Dictionary<uint, string> Items { get; private set; }

			internal PurchaseResponseCallback(JobID jobID, CMsgClientPurchaseResponse msg) {
				JobID = jobID;

				if (msg == null) {
					return;
				}

				ReceiptInfo = new KeyValue();
				Items = new Dictionary<uint, string>();

				Result = (EResult) msg.eresult;
				PurchaseResult = (EPurchaseResult) msg.purchase_result_details;

				using (MemoryStream ms = new MemoryStream(msg.purchase_receipt_info)) {
					if (!ReceiptInfo.TryReadAsBinary(ms)) {
						return;
					}

					foreach (KeyValue lineItem in ReceiptInfo["lineitems"].Children) {
						Items.Add((uint) lineItem["PackageID"].AsUnsignedLong(), lineItem["ItemDescription"].AsString());
					}
				}
			}
		}

		/*
		 __  __        _    _                 _
		|  \/  |  ___ | |_ | |__    ___    __| | ___
		| |\/| | / _ \| __|| '_ \  / _ \  / _` |/ __|
		| |  | ||  __/| |_ | | | || (_) || (_| |\__ \
		|_|  |_| \___| \__||_| |_| \___/  \__,_||___/

		*/

		internal void AcceptClanInvite(ulong clanID) {
			if (clanID == 0) {
				return;
			}

			var request = new ClientMsg<CMsgClientClanInviteAction>((int) EMsg.ClientAcknowledgeClanInvite);
			request.Body.GroupID = clanID;
			request.Body.AcceptInvite = true;

			Client.Send(request);
		}

		internal void DeclineClanInvite(ulong clanID) {
			if (clanID == 0) {
				return;
			}

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

		internal AsyncJob<PurchaseResponseCallback> RedeemKey(string key) {
			if (string.IsNullOrEmpty(key)) {
				return null;
			}

			var request = new ClientMsgProtobuf<CMsgClientRegisterKey>(EMsg.ClientRegisterKey);
			request.SourceJobID = Client.GetNextJobID();
			request.Body.key = key;

			Client.Send(request);
			return new AsyncJob<PurchaseResponseCallback>(Client, request.SourceJobID);
		}

		/*
		 _   _                    _  _
		| | | |  __ _  _ __    __| || |  ___  _ __  ___
		| |_| | / _` || '_ \  / _` || | / _ \| '__|/ __|
		|  _  || (_| || | | || (_| || ||  __/| |   \__ \
		|_| |_| \__,_||_| |_| \__,_||_| \___||_|   |___/

		*/

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
			if (response == null) {
				return;
			}

			Client.PostCallback(new OfflineMessageCallback(packetMsg.TargetJobID, response.Body));
		}

		private void HandlePurchaseResponse(IPacketMsg packetMsg) {
			if (packetMsg == null) {
				return;
			}

			var response = new ClientMsgProtobuf<CMsgClientPurchaseResponse>(packetMsg);
			if (response == null) {
				return;
			}

			Client.PostCallback(new PurchaseResponseCallback(packetMsg.TargetJobID, response.Body));
		}

		private void HandleUserNotifications(IPacketMsg packetMsg) {
			if (packetMsg == null) {
				return;
			}

			var response = new ClientMsgProtobuf<CMsgClientUserNotifications>(packetMsg);
			if (response == null) {
				return;
			}

			Client.PostCallback(new NotificationsCallback(packetMsg.TargetJobID, response.Body));
		}
	}
}