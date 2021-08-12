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

#if NETFRAMEWORK
using HashCode = JustArchiNET.Madness.HashCodeMadness.HashCode;
#endif
using System;
using Newtonsoft.Json;

namespace ArchiSteamFarm.Steam.Cards {
	public sealed class Game : IEquatable<Game> {
		[JsonProperty]
		public uint AppID { get; }

		[JsonProperty]
		public string GameName { get; }

		internal readonly byte BadgeLevel;

		[JsonProperty]
		public ushort CardsRemaining { get; internal set; }

		[JsonProperty]
		public float HoursPlayed { get; internal set; }

		internal uint PlayableAppID { get; set; }

		internal Game(uint appID, string gameName, float hoursPlayed, ushort cardsRemaining, byte badgeLevel) {
			AppID = appID > 0 ? appID : throw new ArgumentOutOfRangeException(nameof(appID));
			GameName = !string.IsNullOrEmpty(gameName) ? gameName : throw new ArgumentNullException(nameof(gameName));
			HoursPlayed = hoursPlayed >= 0 ? hoursPlayed : throw new ArgumentOutOfRangeException(nameof(hoursPlayed));
			CardsRemaining = cardsRemaining > 0 ? cardsRemaining : throw new ArgumentOutOfRangeException(nameof(cardsRemaining));
			BadgeLevel = badgeLevel;

			PlayableAppID = appID;
		}

		public bool Equals(Game? other) => (other != null) && (ReferenceEquals(other, this) || ((AppID == other.AppID) && (BadgeLevel == other.BadgeLevel) && (GameName == other.GameName)));
		public override bool Equals(object? obj) => (obj != null) && ((obj == this) || (obj is Game game && Equals(game)));
		public override int GetHashCode() => HashCode.Combine(AppID, BadgeLevel, GameName);
	}
}
