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

using SteamKit2;
using SteamKit2.Internal;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;

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
			internal sealed class Notification {
				internal enum ENotificationType : uint {
					Unknown = 0,
					Trading = 1,
					// Only custom below, different than ones available as user_notification_type
					Items = 514
				}

				internal ENotificationType NotificationType { get; set; }
			}

			internal readonly List<Notification> Notifications;

			internal NotificationsCallback(JobID jobID, CMsgClientUserNotifications msg) {
				JobID = jobID;

				if (msg == null || msg.notifications == null) {
					return;
				}

				Notifications = new List<Notification>(msg.notifications.Count);
				foreach (var notification in msg.notifications) {
					Notifications.Add(new Notification {
						NotificationType = (Notification.ENotificationType) notification.user_notification_type
					});
				}
			}

			internal NotificationsCallback(JobID jobID, CMsgClientItemAnnouncements msg) {
				JobID = jobID;

				if (msg == null) {
					return;
				}

				if (msg.count_new_items > 0) {
					Notifications = new List<Notification>(1) {
						new Notification { NotificationType = Notification.ENotificationType.Items }
					};
				}
			}
		}

		internal sealed class OfflineMessageCallback : CallbackMsg {
			internal readonly uint OfflineMessages;
			internal readonly List<uint> Users;

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
				RegionLocked = 13,
				InvalidKey = 14,
				DuplicatedKey = 15,
				BaseGameRequired = 24,
				OnCooldown = 53
			}

			internal readonly EResult Result;
			internal readonly EPurchaseResult PurchaseResult;
			internal readonly KeyValue ReceiptInfo;
			internal readonly Dictionary<uint, string> Items;

			internal PurchaseResponseCallback(JobID jobID, CMsgClientPurchaseResponse msg) {
				JobID = jobID;

				if (msg == null) {
					return;
				}

				Result = (EResult) msg.eresult;
				PurchaseResult = (EPurchaseResult) msg.purchase_result_details;

				ReceiptInfo = new KeyValue();
				using (MemoryStream ms = new MemoryStream(msg.purchase_receipt_info)) {
					if (!ReceiptInfo.TryReadAsBinary(ms)) {
						return;
					}

					List<KeyValue> lineItems = ReceiptInfo["lineitems"].Children;
					Items = new Dictionary<uint, string>(lineItems.Count);

					foreach (KeyValue lineItem in lineItems) {
						uint appID = (uint) lineItem["PackageID"].AsUnsignedLong();
						string gameName = lineItem["ItemDescription"].AsString();
						gameName = WebUtility.UrlDecode(gameName); // Apparently steam expects client to decode sent HTML
						Items.Add(appID, gameName);
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
			if (clanID == 0 || !Client.IsConnected) {
				return;
			}

			var request = new ClientMsg<CMsgClientClanInviteAction>((int) EMsg.ClientAcknowledgeClanInvite);
			request.Body.GroupID = clanID;
			request.Body.AcceptInvite = true;

			Client.Send(request);
		}

		internal void DeclineClanInvite(ulong clanID) {
			if (clanID == 0 || !Client.IsConnected) {
				return;
			}

			var request = new ClientMsg<CMsgClientClanInviteAction>((int) EMsg.ClientAcknowledgeClanInvite);
			request.Body.GroupID = clanID;
			request.Body.AcceptInvite = false;

			Client.Send(request);
		}

		internal void PlayGame(string gameName) {
			if (!Client.IsConnected) {
				return;
			}

			var request = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayed);

			var gamePlayed = new CMsgClientGamesPlayed.GamePlayed();
			if (!string.IsNullOrEmpty(gameName)) {
				gamePlayed.game_id = new GameID() {
					AppType = GameID.GameType.Shortcut,
					ModID = uint.MaxValue
				};
				gamePlayed.game_extra_info = gameName;
			}

			request.Body.games_played.Add(gamePlayed);

			Client.Send(request);
		}

		internal void PlayGames(params uint[] gameIDs) {
			if (!Client.IsConnected) {
				return;
			}

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
			if (gameIDs == null || !Client.IsConnected) {
				return;
			}

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

		internal async Task<PurchaseResponseCallback> RedeemKey(string key) {
			if (string.IsNullOrEmpty(key) || !Client.IsConnected) {
				return null;
			}

			var request = new ClientMsgProtobuf<CMsgClientRegisterKey>(EMsg.ClientRegisterKey) {
				SourceJobID = Client.GetNextJobID()
			};

			request.Body.key = key;

			Client.Send(request);
			try {
				return await new AsyncJob<PurchaseResponseCallback>(Client, request.SourceJobID);
			} catch (Exception e) {
				Logging.LogGenericException(e);
				return null;
			}
		}

		/*
		 _   _                    _  _
		| | | |  __ _  _ __    __| || |  ___  _ __  ___
		| |_| | / _` || '_ \  / _` || | / _ \| '__|/ __|
		|  _  || (_| || | | || (_| || ||  __/| |   \__ \
		|_| |_| \__,_||_| |_| \__,_||_| \___||_|   |___/

		*/

		public override void HandleMsg(IPacketMsg packetMsg) {
			if (packetMsg == null) {
				return;
			}

			switch (packetMsg.MsgType) {
				case EMsg.ClientFSOfflineMessageNotification:
					HandleFSOfflineMessageNotification(packetMsg);
					break;
				case EMsg.ClientItemAnnouncements:
					HandleItemAnnouncements(packetMsg);
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
			Client.PostCallback(new OfflineMessageCallback(packetMsg.TargetJobID, response.Body));
		}

		private void HandleItemAnnouncements(IPacketMsg packetMsg) {
			if (packetMsg == null) {
				return;
			}

			var response = new ClientMsgProtobuf<CMsgClientItemAnnouncements>(packetMsg);
			Client.PostCallback(new NotificationsCallback(packetMsg.TargetJobID, response.Body));
		}

		private void HandlePurchaseResponse(IPacketMsg packetMsg) {
			if (packetMsg == null) {
				return;
			}

			var response = new ClientMsgProtobuf<CMsgClientPurchaseResponse>(packetMsg);
			Client.PostCallback(new PurchaseResponseCallback(packetMsg.TargetJobID, response.Body));
		}

		private void HandleUserNotifications(IPacketMsg packetMsg) {
			if (packetMsg == null) {
				return;
			}

			var response = new ClientMsgProtobuf<CMsgClientUserNotifications>(packetMsg);
			Client.PostCallback(new NotificationsCallback(packetMsg.TargetJobID, response.Body));
		}
	}
}