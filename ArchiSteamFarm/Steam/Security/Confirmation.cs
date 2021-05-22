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
using JetBrains.Annotations;
using Newtonsoft.Json;

namespace ArchiSteamFarm.Steam.Security {
	public sealed class Confirmation {
		[JsonProperty(Required = Required.Always)]
		public ulong Creator { get; }

		[JsonProperty(Required = Required.Always)]
		public ulong ID { get; }

		[JsonProperty(Required = Required.Always)]
		public ulong Key { get; }

		[JsonProperty(Required = Required.Always)]
		public EType Type { get; }

		internal Confirmation(ulong id, ulong key, ulong creator, EType type) {
			ID = id > 0 ? id : throw new ArgumentOutOfRangeException(nameof(id));
			Key = key > 0 ? key : throw new ArgumentOutOfRangeException(nameof(key));
			Creator = creator > 0 ? creator : throw new ArgumentOutOfRangeException(nameof(creator));
			Type = Enum.IsDefined(typeof(EType), type) ? type : throw new InvalidEnumArgumentException(nameof(type), (int) type, typeof(EType));
		}

		// REF: Internal documentation
		[PublicAPI]
		public enum EType : byte {
			Unknown,
			Generic,
			Trade,
			Market,

			// We're missing information about definition of number 4 type
			PhoneNumberChange = 5,
			AccountRecovery = 6
		}
	}
}
