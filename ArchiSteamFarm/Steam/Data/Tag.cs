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
using JetBrains.Annotations;
using Newtonsoft.Json;

namespace ArchiSteamFarm.Steam.Data {
	public sealed class Tag {
		[JsonProperty(PropertyName = "category", Required = Required.Always)]
		[PublicAPI]
		public string Identifier { get; private set; } = "";

		[JsonProperty(PropertyName = "internal_name", Required = Required.Always)]
		[PublicAPI]
		public string Value { get; private set; } = "";

		internal Tag(string identifier, string value) {
			Identifier = !string.IsNullOrEmpty(identifier) ? identifier : throw new ArgumentNullException(nameof(identifier));
			Value = value ?? throw new ArgumentNullException(nameof(value));
		}

		[JsonConstructor]
		private Tag() { }
	}
}
