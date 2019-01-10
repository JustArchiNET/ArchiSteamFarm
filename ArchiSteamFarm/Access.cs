//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// 
// Copyright 2015-2019 Łukasz "JustArchi" Domeradzki
// Contact: JustArchi@JustArchi.net
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
// http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using ArchiSteamFarm.Collections;
using JetBrains.Annotations;

namespace ArchiSteamFarm {
	public sealed class Access {
		internal readonly ConcurrentHashSet<ulong> SteamFamilySharingIDs = new ConcurrentHashSet<ulong>();

		private readonly Bot Bot;

		internal Access(Bot bot) => Bot = bot ?? throw new ArgumentNullException(nameof(bot));

		[PublicAPI]
		public bool IsFamilySharing(ulong steamID) {
			if (steamID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(steamID));

				return false;
			}

			return ASF.IsOwner(steamID) || SteamFamilySharingIDs.Contains(steamID) || (GetSteamUserPermission(steamID) >= BotConfig.EPermission.FamilySharing);
		}

		[PublicAPI]
		public bool IsMaster(ulong steamID) {
			if (steamID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(steamID));

				return false;
			}

			return ASF.IsOwner(steamID) || (GetSteamUserPermission(steamID) >= BotConfig.EPermission.Master);
		}

		[PublicAPI]
		public bool IsOperator(ulong steamID) {
			if (steamID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(steamID));

				return false;
			}

			return ASF.IsOwner(steamID) || (GetSteamUserPermission(steamID) >= BotConfig.EPermission.Operator);
		}

		private BotConfig.EPermission GetSteamUserPermission(ulong steamID) {
			if (steamID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(steamID));

				return BotConfig.EPermission.None;
			}

			return Bot.BotConfig.SteamUserPermissions.TryGetValue(steamID, out BotConfig.EPermission permission) ? permission : BotConfig.EPermission.None;
		}
	}
}
