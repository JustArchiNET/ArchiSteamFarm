//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2023 Łukasz "JustArchi" Domeradzki
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

using System.Diagnostics.CodeAnalysis;
using ArchiSteamFarm.Steam.Data;
using Newtonsoft.Json;

namespace ArchiSteamFarm.OfficialPlugins.ItemsMatcher.Data;

#pragma warning disable CA1812 // False positive, the class is used during json deserialization
[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
internal sealed class SetPart {
#pragma warning disable CS0649 // False positive, the field is used during json deserialization
	[JsonProperty("c", Required = Required.Always)]
	internal readonly ulong ClassID;
#pragma warning restore CS0649 // False positive, the field is used during json deserialization

#pragma warning disable CS0649 // False positive, the field is used during json deserialization
	[JsonProperty("r", Required = Required.Always)]
	internal readonly Asset.ERarity Rarity;
#pragma warning restore CS0649 // False positive, the field is used during json deserialization

#pragma warning disable CS0649 // False positive, the field is used during json deserialization
	[JsonProperty("e", Required = Required.Always)]
	internal readonly uint RealAppID;
#pragma warning restore CS0649 // False positive, the field is used during json deserialization

#pragma warning disable CS0649 // False positive, the field is used during json deserialization
	[JsonProperty("p", Required = Required.Always)]
	internal readonly Asset.EType Type;
#pragma warning restore CS0649 // False positive, the field is used during json deserialization

	[JsonConstructor]
	private SetPart() { }
}
#pragma warning restore CA1812 // False positive, the class is used during json deserialization
