// ----------------------------------------------------------------------------------------------
//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// ----------------------------------------------------------------------------------------------
// |
// Copyright 2015-2024 Łukasz "JustArchi" Domeradzki
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

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace ArchiSteamFarm.IPC.Responses;

public sealed class BotAddLicenseResponse {
	/// <summary>
	///     A collection (set) of apps (appIDs) to ask license for.
	/// </summary>
	[JsonInclude]
	public ImmutableDictionary<uint, AddLicenseResult>? Apps { get; private init; }

	/// <summary>
	///     A collection (set) of packages (subIDs) to ask license for.
	/// </summary>
	[JsonInclude]
	public ImmutableDictionary<uint, AddLicenseResult>? Packages { get; private init; }

	internal BotAddLicenseResponse(IReadOnlyDictionary<uint, AddLicenseResult>? apps, IReadOnlyDictionary<uint, AddLicenseResult>? packages) {
		Apps = apps?.ToImmutableDictionary();
		Packages = packages?.ToImmutableDictionary();
	}
}
