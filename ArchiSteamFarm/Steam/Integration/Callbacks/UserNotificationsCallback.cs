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

using System;
using System.Collections.Generic;
using System.Globalization;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Localization;
using JetBrains.Annotations;
using SteamKit2;
using SteamKit2.Internal;

namespace ArchiSteamFarm.Steam.Integration.Callbacks {
	public sealed class UserNotificationsCallback : CallbackMsg {
		internal readonly Dictionary<EUserNotification, uint> Notifications;

		internal UserNotificationsCallback(JobID jobID, CMsgClientUserNotifications msg) {
			if (jobID == null) {
				throw new ArgumentNullException(nameof(jobID));
			}

			if (msg == null) {
				throw new ArgumentNullException(nameof(msg));
			}

			JobID = jobID;

			// We might get null body here, and that means there are no notifications related to trading
			// TODO: Check if this workaround is still needed
			Notifications = new Dictionary<EUserNotification, uint> { { EUserNotification.Trading, 0 } };

			if (msg.notifications == null) {
				return;
			}

			foreach (CMsgClientUserNotifications.Notification notification in msg.notifications) {
				EUserNotification type = (EUserNotification) notification.user_notification_type;

				switch (type) {
					case EUserNotification.AccountAlerts:
					case EUserNotification.Chat:
					case EUserNotification.Comments:
					case EUserNotification.GameTurns:
					case EUserNotification.Gifts:
					case EUserNotification.HelpRequestReplies:
					case EUserNotification.Invites:
					case EUserNotification.Items:
					case EUserNotification.ModeratorMessages:
					case EUserNotification.Trading:
						break;
					default:
						ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(type), type));

						break;
				}

				Notifications[type] = notification.count;
			}
		}

		internal UserNotificationsCallback(JobID jobID, CMsgClientItemAnnouncements msg) {
			if (jobID == null) {
				throw new ArgumentNullException(nameof(jobID));
			}

			if (msg == null) {
				throw new ArgumentNullException(nameof(msg));
			}

			JobID = jobID;
			Notifications = new Dictionary<EUserNotification, uint>(1) { { EUserNotification.Items, msg.count_new_items } };
		}

		internal UserNotificationsCallback(JobID jobID, CMsgClientCommentNotifications msg) {
			if (jobID == null) {
				throw new ArgumentNullException(nameof(jobID));
			}

			if (msg == null) {
				throw new ArgumentNullException(nameof(msg));
			}

			JobID = jobID;
			Notifications = new Dictionary<EUserNotification, uint>(1) { { EUserNotification.Comments, msg.count_new_comments + msg.count_new_comments_owner + msg.count_new_comments_subscriptions } };
		}

		[PublicAPI]
		public enum EUserNotification : byte {
			Unknown,
			Trading,
			GameTurns,
			ModeratorMessages,
			Comments,
			Items,
			Invites,
			Unknown7, // Unknown type of notification, never seen in the wild
			Gifts,
			Chat,
			HelpRequestReplies,
			AccountAlerts
		}
	}
}
