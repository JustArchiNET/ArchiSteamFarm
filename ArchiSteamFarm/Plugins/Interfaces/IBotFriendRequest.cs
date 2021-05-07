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

using System.Threading.Tasks;
using ArchiSteamFarm.Steam;
using JetBrains.Annotations;

namespace ArchiSteamFarm.Plugins.Interfaces {
	[PublicAPI]
	public interface IBotFriendRequest : IPlugin {
		/// <summary>
		///     ASF will call this method for unhandled (ignored and rejected) friend requests and Steam group invites received by the bot.
		/// </summary>
		/// <param name="bot">Bot object related to this callback.</param>
		/// <param name="steamID">64-bit Steam identificator of the user that sent a friend request, or a group that the bot has been invited to.</param>
		/// <returns>True if the request should be accepted as part of this plugin, false otherwise.</returns>
		Task<bool> OnBotFriendRequest(Bot bot, ulong steamID);
	}
}
