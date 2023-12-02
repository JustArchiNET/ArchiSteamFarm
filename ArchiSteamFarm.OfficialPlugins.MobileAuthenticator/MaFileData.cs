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
using Newtonsoft.Json;
using SteamKit2;
using SteamKit2.Internal;

namespace ArchiSteamFarm.OfficialPlugins.MobileAuthenticator;

internal sealed class MaFileData {
	[JsonProperty("account_name", Required = Required.Always)]
	internal readonly string AccountName;

	[JsonProperty("device_id", Required = Required.Always)]
	internal readonly string DeviceID;

	[JsonProperty("identity_secret", Required = Required.Always)]
	internal readonly string IdentitySecret;

	[JsonProperty("revocation_code", Required = Required.Always)]
	internal readonly string RevocationCode;

	[JsonProperty("secret_1", Required = Required.Always)]
	internal readonly string Secret1;

	[JsonProperty("serial_number", Required = Required.Always)]
	internal readonly ulong SerialNumber;

	[JsonProperty("server_time", Required = Required.Always)]
	internal readonly ulong ServerTime;

	[JsonProperty(Required = Required.Always)]
	internal readonly MaFileSessionData Session;

	[JsonProperty("shared_secret", Required = Required.Always)]
	internal readonly string SharedSecret;

	[JsonProperty("status", Required = Required.Always)]
	internal readonly int Status;

	[JsonProperty("token_gid", Required = Required.Always)]
	internal readonly string TokenGid;

	[JsonProperty("uri", Required = Required.Always)]
	internal readonly string Uri;

	internal MaFileData(CTwoFactor_AddAuthenticator_Response data, ulong steamID, string deviceID) {
		ArgumentNullException.ThrowIfNull(data);

		if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		ArgumentException.ThrowIfNullOrEmpty(deviceID);

		AccountName = data.account_name;
		DeviceID = deviceID;
		IdentitySecret = Convert.ToBase64String(data.identity_secret);
		RevocationCode = data.revocation_code;
		Secret1 = Convert.ToBase64String(data.secret_1);
		SerialNumber = data.serial_number;
		ServerTime = data.server_time;
		Session = new MaFileSessionData(steamID);
		SharedSecret = Convert.ToBase64String(data.shared_secret);
		Status = data.status;
		TokenGid = data.token_gid;
		Uri = data.uri;
	}
}
