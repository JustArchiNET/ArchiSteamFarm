// ----------------------------------------------------------------------------------------------
//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// ----------------------------------------------------------------------------------------------
// |
// Copyright 2015-2026 Łukasz "JustArchi" Domeradzki
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
using System.Text.Json.Serialization;
using SteamKit2;

namespace ArchiSteamFarm.Steam.Data;

[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
internal sealed class UserPrivacy {
	[JsonInclude]
	[JsonPropertyName("eCommentPermission")]
	[JsonRequired]
	internal ECommentPermission CommentPermission { get; private init; }

	[JsonInclude]
	[JsonPropertyName("PrivacySettings")]
	[JsonRequired]
	internal PrivacySettings Settings { get; private init; } = null!;

	// Constructed from privacy change request
	internal UserPrivacy(PrivacySettings settings, ECommentPermission commentPermission) {
		ArgumentNullException.ThrowIfNull(settings);

		if ((commentPermission == ECommentPermission.Invalid) || !Enum.IsDefined(commentPermission)) {
			throw new InvalidEnumArgumentException(nameof(commentPermission), (int) commentPermission, typeof(ECommentPermission));
		}

		Settings = settings;
		CommentPermission = commentPermission;
	}

	[JsonConstructor]
	private UserPrivacy() { }

	internal sealed class PrivacySettings {
		[JsonInclude]
		[JsonPropertyName("PrivacyFriendsList")]
		[JsonRequired]
		internal ECommunityPrivacy FriendsList { get; private init; }

		[JsonInclude]
		[JsonPropertyName("PrivacyInventory")]
		[JsonRequired]
		internal ECommunityPrivacy Inventory { get; private init; }

		[JsonInclude]
		[JsonPropertyName("PrivacyInventoryGifts")]
		[JsonRequired]
		internal ECommunityPrivacy InventoryGifts { get; private init; }

		[JsonInclude]
		[JsonPropertyName("PrivacyOwnedGames")]
		[JsonRequired]
		internal ECommunityPrivacy OwnedGames { get; private init; }

		[JsonInclude]
		[JsonPropertyName("PrivacyPlaytime")]
		[JsonRequired]
		internal ECommunityPrivacy Playtime { get; private init; }

		[JsonInclude]
		[JsonPropertyName("PrivacyProfile")]
		[JsonRequired]
		internal ECommunityPrivacy Profile { get; private init; }

		// Constructed from privacy change request
		internal PrivacySettings(ECommunityPrivacy profile, ECommunityPrivacy ownedGames, ECommunityPrivacy playtime, ECommunityPrivacy friendsList, ECommunityPrivacy inventory, ECommunityPrivacy inventoryGifts) {
			if ((profile == ECommunityPrivacy.Invalid) || !Enum.IsDefined(profile)) {
				throw new InvalidEnumArgumentException(nameof(profile), (int) profile, typeof(ECommunityPrivacy));
			}

			if ((ownedGames == ECommunityPrivacy.Invalid) || !Enum.IsDefined(ownedGames)) {
				throw new InvalidEnumArgumentException(nameof(ownedGames), (int) ownedGames, typeof(ECommunityPrivacy));
			}

			if ((playtime == ECommunityPrivacy.Invalid) || !Enum.IsDefined(playtime)) {
				throw new InvalidEnumArgumentException(nameof(playtime), (int) playtime, typeof(ECommunityPrivacy));
			}

			if ((friendsList == ECommunityPrivacy.Invalid) || !Enum.IsDefined(friendsList)) {
				throw new InvalidEnumArgumentException(nameof(friendsList), (int) friendsList, typeof(ECommunityPrivacy));
			}

			if ((inventory == ECommunityPrivacy.Invalid) || !Enum.IsDefined(inventory)) {
				throw new InvalidEnumArgumentException(nameof(inventory), (int) inventory, typeof(ECommunityPrivacy));
			}

			if ((inventoryGifts == ECommunityPrivacy.Invalid) || !Enum.IsDefined(inventoryGifts)) {
				throw new InvalidEnumArgumentException(nameof(inventoryGifts), (int) inventoryGifts, typeof(ECommunityPrivacy));
			}

			Profile = profile;
			OwnedGames = ownedGames;
			Playtime = playtime;
			FriendsList = friendsList;
			Inventory = inventory;
			InventoryGifts = inventoryGifts;
		}

		[JsonConstructor]
		private PrivacySettings() { }
	}
}
