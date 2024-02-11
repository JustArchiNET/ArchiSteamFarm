//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
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

using System;
using System.Text.Json.Serialization;
using SteamKit2;
using SteamKit2.Internal;

namespace ArchiSteamFarm.OfficialPlugins.MobileAuthenticator;

internal sealed class MaFileData {
	[JsonInclude]
	[JsonPropertyName("account_name")]
	[JsonRequired]
	internal readonly string AccountName;

	[JsonInclude]
	[JsonPropertyName("device_id")]
	[JsonRequired]
	internal readonly string DeviceID;

	[JsonInclude]
	[JsonPropertyName("identity_secret")]
	[JsonRequired]
	internal readonly string IdentitySecret;

	[JsonInclude]
	[JsonPropertyName("revocation_code")]
	[JsonRequired]
	internal readonly string RevocationCode;

	[JsonInclude]
	[JsonPropertyName("secret_1")]
	[JsonRequired]
	internal readonly string Secret1;

	[JsonInclude]
	[JsonPropertyName("serial_number")]
	[JsonRequired]
	internal readonly ulong SerialNumber;

	[JsonInclude]
	[JsonPropertyName("server_time")]
	[JsonRequired]
	internal readonly ulong ServerTime;

	[JsonInclude]
	[JsonRequired]
	internal readonly MaFileSessionData Session;

	[JsonInclude]
	[JsonPropertyName("shared_secret")]
	[JsonRequired]
	internal readonly string SharedSecret;

	[JsonInclude]
	[JsonPropertyName("status")]
	[JsonRequired]
	internal readonly int Status;

	[JsonInclude]
	[JsonPropertyName("token_gid")]
	[JsonRequired]
	internal readonly string TokenGid;

	[JsonInclude]
	[JsonPropertyName("uri")]
	[JsonRequired]
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
