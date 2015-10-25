using SteamKit2;
using SteamKit2.Internal;

namespace ArchiSteamFarm {
	internal sealed class ArchiHandler : ClientMsgHandler {

		internal sealed class PurchaseResponseCallback : CallbackMsg {
			internal enum EPurchaseResult {
				Unknown,
				OK,
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

				if (Result == EResult.OK) {
					PurchaseResult = EPurchaseResult.OK;
				} else {
					PurchaseResult = (EPurchaseResult) ErrorCode;
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
				uint notificationType = body.user_notification_type;
                switch (notificationType) {
					case 1:
						NotificationType = (ENotificationType) notificationType;
						break;
					default:
						NotificationType = ENotificationType.Unknown;
						break;
                }
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