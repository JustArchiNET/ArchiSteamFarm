//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2020 Łukasz "JustArchi" Domeradzki
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
using Newtonsoft.Json;

namespace ArchiSteamFarm.OfficialPlugins.SteamTokenDumper {
	[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
	internal sealed class ResponseData {
#pragma warning disable 649
		[JsonProperty(PropertyName = "data", Required = Required.Always)]
		internal readonly InternalData? Data;
#pragma warning restore 649

#pragma warning disable 649
		[JsonProperty(PropertyName = "success", Required = Required.Always)]
		internal readonly bool Success;
#pragma warning restore 649

		[JsonConstructor]
		private ResponseData() { }

		internal sealed class InternalData {
#pragma warning disable 649
			[JsonProperty(PropertyName = "new_apps", Required = Required.Always)]
			internal readonly uint NewAppsCount;
#pragma warning restore 649

#pragma warning disable 649
			[JsonProperty(PropertyName = "new_depots", Required = Required.Always)]
			internal readonly uint NewDepotsCount;
#pragma warning restore 649

#pragma warning disable 649
			[JsonProperty(PropertyName = "new_subs", Required = Required.Always)]
			internal readonly uint NewSubsCount;
#pragma warning restore 649

			[JsonConstructor]
			private InternalData() { }
		}
	}
}
