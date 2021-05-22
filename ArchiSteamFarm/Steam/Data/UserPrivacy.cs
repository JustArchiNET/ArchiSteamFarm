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
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using ArchiSteamFarm.Steam.Integration;
using Newtonsoft.Json;

namespace ArchiSteamFarm.Steam.Data {
	[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
	internal sealed class UserPrivacy {
		[JsonProperty(PropertyName = "eCommentPermission", Required = Required.Always)]
		internal readonly ECommentPermission CommentPermission;

		[JsonProperty(PropertyName = "PrivacySettings", Required = Required.Always)]
		internal readonly PrivacySettings Settings = new();

		// Constructed from privacy change request
		internal UserPrivacy(PrivacySettings settings, ECommentPermission commentPermission) {
			Settings = settings ?? throw new ArgumentNullException(nameof(settings));
			CommentPermission = commentPermission;
		}

		[JsonConstructor]
		private UserPrivacy() { }

		internal sealed class PrivacySettings {
			[JsonProperty(PropertyName = "PrivacyFriendsList", Required = Required.Always)]
			internal readonly ArchiHandler.EPrivacySetting FriendsList;

			[JsonProperty(PropertyName = "PrivacyInventory", Required = Required.Always)]
			internal readonly ArchiHandler.EPrivacySetting Inventory;

			[JsonProperty(PropertyName = "PrivacyInventoryGifts", Required = Required.Always)]
			internal readonly ArchiHandler.EPrivacySetting InventoryGifts;

			[JsonProperty(PropertyName = "PrivacyOwnedGames", Required = Required.Always)]
			internal readonly ArchiHandler.EPrivacySetting OwnedGames;

			[JsonProperty(PropertyName = "PrivacyPlaytime", Required = Required.Always)]
			internal readonly ArchiHandler.EPrivacySetting Playtime;

			[JsonProperty(PropertyName = "PrivacyProfile", Required = Required.Always)]
			internal readonly ArchiHandler.EPrivacySetting Profile;

			// Constructed from privacy change request
			internal PrivacySettings(ArchiHandler.EPrivacySetting profile, ArchiHandler.EPrivacySetting ownedGames, ArchiHandler.EPrivacySetting playtime, ArchiHandler.EPrivacySetting friendsList, ArchiHandler.EPrivacySetting inventory, ArchiHandler.EPrivacySetting inventoryGifts) {
				if ((profile == ArchiHandler.EPrivacySetting.Unknown) || !Enum.IsDefined(typeof(ArchiHandler.EPrivacySetting), profile)) {
					throw new InvalidEnumArgumentException(nameof(profile), (int) profile, typeof(ArchiHandler.EPrivacySetting));
				}

				if ((ownedGames == ArchiHandler.EPrivacySetting.Unknown) || !Enum.IsDefined(typeof(ArchiHandler.EPrivacySetting), ownedGames)) {
					throw new InvalidEnumArgumentException(nameof(ownedGames), (int) ownedGames, typeof(ArchiHandler.EPrivacySetting));
				}

				if ((playtime == ArchiHandler.EPrivacySetting.Unknown) || !Enum.IsDefined(typeof(ArchiHandler.EPrivacySetting), playtime)) {
					throw new InvalidEnumArgumentException(nameof(playtime), (int) playtime, typeof(ArchiHandler.EPrivacySetting));
				}

				if ((friendsList == ArchiHandler.EPrivacySetting.Unknown) || !Enum.IsDefined(typeof(ArchiHandler.EPrivacySetting), friendsList)) {
					throw new InvalidEnumArgumentException(nameof(friendsList), (int) friendsList, typeof(ArchiHandler.EPrivacySetting));
				}

				if ((inventory == ArchiHandler.EPrivacySetting.Unknown) || !Enum.IsDefined(typeof(ArchiHandler.EPrivacySetting), inventory)) {
					throw new InvalidEnumArgumentException(nameof(inventory), (int) inventory, typeof(ArchiHandler.EPrivacySetting));
				}

				if ((inventoryGifts == ArchiHandler.EPrivacySetting.Unknown) || !Enum.IsDefined(typeof(ArchiHandler.EPrivacySetting), inventoryGifts)) {
					throw new InvalidEnumArgumentException(nameof(inventoryGifts), (int) inventoryGifts, typeof(ArchiHandler.EPrivacySetting));
				}

				Profile = profile;
				OwnedGames = ownedGames;
				Playtime = playtime;
				FriendsList = friendsList;
				Inventory = inventory;
				InventoryGifts = inventoryGifts;
			}

			[JsonConstructor]
			internal PrivacySettings() { }
		}

		internal enum ECommentPermission : byte {
			FriendsOnly,
			Public,
			Private
		}
	}
}
