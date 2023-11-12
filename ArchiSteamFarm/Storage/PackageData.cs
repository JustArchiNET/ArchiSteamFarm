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

using System;
using System.Collections.Immutable;
using JetBrains.Annotations;
using Newtonsoft.Json;

namespace ArchiSteamFarm.Storage;

public sealed class PackageData {
	[JsonProperty]
	public ImmutableHashSet<uint>? AppIDs { get; private set; }

	[JsonProperty(Required = Required.Always)]
	public uint ChangeNumber { get; private set; }

	[JsonProperty]
	public ImmutableHashSet<string>? ProhibitRunInCountries { get; private set; }

	[JsonProperty(Required = Required.Always)]
	public DateTime ValidUntil { get; private set; }

	internal PackageData(uint changeNumber, DateTime validUntil, ImmutableHashSet<uint>? appIDs = null, ImmutableHashSet<string>? prohibitRunInCountries = null) {
		ArgumentOutOfRangeException.ThrowIfZero(changeNumber);
		ArgumentOutOfRangeException.ThrowIfEqual(validUntil, DateTime.MinValue);

		ChangeNumber = changeNumber;
		ValidUntil = validUntil;
		AppIDs = appIDs;
		ProhibitRunInCountries = prohibitRunInCountries;
	}

	[JsonConstructor]
	private PackageData() { }

	[UsedImplicitly]
	public bool ShouldSerializeAppIDs() => AppIDs is { IsEmpty: false };

	[UsedImplicitly]
	public bool ShouldSerializeProhibitRunInCountries() => ProhibitRunInCountries is { IsEmpty: false };
}
