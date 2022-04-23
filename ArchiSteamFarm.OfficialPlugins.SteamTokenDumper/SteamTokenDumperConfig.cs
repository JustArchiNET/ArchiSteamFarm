//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2022 Łukasz "JustArchi" Domeradzki
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
using ArchiSteamFarm.IPC.Integration;
using Newtonsoft.Json;

namespace ArchiSteamFarm.OfficialPlugins.SteamTokenDumper;

[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
public sealed class SteamTokenDumperConfig {
	[JsonProperty(Required = Required.DisallowNull)]
	public bool Enabled { get; internal set; }

	[JsonProperty(Required = Required.DisallowNull)]
	[SwaggerItemsMinMax(MinimumUint = 1, MaximumUint = uint.MaxValue)]
	public ImmutableHashSet<uint> SecretAppIDs { get; private set; } = ImmutableHashSet<uint>.Empty;

	[JsonProperty(Required = Required.DisallowNull)]
	[SwaggerItemsMinMax(MinimumUint = 1, MaximumUint = uint.MaxValue)]
	public ImmutableHashSet<uint> SecretDepotIDs { get; private set; } = ImmutableHashSet<uint>.Empty;

	[JsonProperty(Required = Required.DisallowNull)]
	[SwaggerItemsMinMax(MinimumUint = 1, MaximumUint = uint.MaxValue)]
	public ImmutableHashSet<uint> SecretPackageIDs { get; private set; } = ImmutableHashSet<uint>.Empty;

	[JsonProperty(Required = Required.DisallowNull)]
	public bool SkipAutoGrantPackages { get; private set; } = true;

	[JsonConstructor]
	internal SteamTokenDumperConfig() { }
}
