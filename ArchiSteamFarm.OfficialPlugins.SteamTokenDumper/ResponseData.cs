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

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;

namespace ArchiSteamFarm.OfficialPlugins.SteamTokenDumper {
#pragma warning disable CA1812 // False positive, the class is used during json deserialization
	[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
	internal sealed class ResponseData {
#pragma warning disable CS0649 // False positive, the field is used during json deserialization
		[JsonProperty(PropertyName = "data", Required = Required.DisallowNull)]
		internal readonly InternalData? Data;
#pragma warning restore CS0649 // False positive, the field is used during json deserialization

#pragma warning disable CS0649 // False positive, the field is used during json deserialization
		[JsonProperty(PropertyName = "success", Required = Required.Always)]
		internal readonly bool Success;
#pragma warning restore CS0649 // False positive, the field is used during json deserialization

		[JsonConstructor]
		private ResponseData() { }

		internal sealed class InternalData {
			[JsonProperty(PropertyName = "new_apps", Required = Required.Always)]
			internal readonly ImmutableHashSet<uint> NewApps = ImmutableHashSet<uint>.Empty;

			[JsonProperty(PropertyName = "new_depots", Required = Required.Always)]
			internal readonly ImmutableHashSet<uint> NewDepots = ImmutableHashSet<uint>.Empty;

			[JsonProperty(PropertyName = "new_subs", Required = Required.Always)]
			internal readonly ImmutableHashSet<uint> NewPackages = ImmutableHashSet<uint>.Empty;

			[JsonProperty(PropertyName = "verified_apps", Required = Required.Always)]
			internal readonly ImmutableHashSet<uint> VerifiedApps = ImmutableHashSet<uint>.Empty;

			[JsonProperty(PropertyName = "verified_depots", Required = Required.Always)]
			internal readonly ImmutableHashSet<uint> VerifiedDepots = ImmutableHashSet<uint>.Empty;

			[JsonProperty(PropertyName = "verified_subs", Required = Required.Always)]
			internal readonly ImmutableHashSet<uint> VerifiedPackages = ImmutableHashSet<uint>.Empty;

			[JsonConstructor]
			private InternalData() { }
		}
	}
#pragma warning restore CA1812 // False positive, the class is used during json deserialization
}
