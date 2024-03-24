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

using System;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.NLog;
using SteamKit2;
using SteamKit2.Internal;

namespace ArchiSteamFarm.OfficialPlugins.MobileAuthenticator;

internal sealed class MobileAuthenticatorHandler : ClientMsgHandler {
	private readonly ArchiLogger ArchiLogger;
	private readonly SteamUnifiedMessages.UnifiedService<ITwoFactor> UnifiedTwoFactorService;

	internal MobileAuthenticatorHandler(ArchiLogger archiLogger, SteamUnifiedMessages steamUnifiedMessages) {
		ArgumentNullException.ThrowIfNull(archiLogger);
		ArgumentNullException.ThrowIfNull(steamUnifiedMessages);

		ArchiLogger = archiLogger;
		UnifiedTwoFactorService = steamUnifiedMessages.CreateService<ITwoFactor>();
	}

	public override void HandleMsg(IPacketMsg packetMsg) => ArgumentNullException.ThrowIfNull(packetMsg);

	internal async Task<CTwoFactor_AddAuthenticator_Response?> AddAuthenticator(ulong steamID, string deviceID) {
		if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		ArgumentException.ThrowIfNullOrEmpty(deviceID);

		if (Client == null) {
			throw new InvalidOperationException(nameof(Client));
		}

		if (!Client.IsConnected) {
			return null;
		}

		CTwoFactor_AddAuthenticator_Request request = new() {
			authenticator_type = 1,
			authenticator_time = Utilities.GetUnixTime(),
			device_identifier = deviceID,
			steamid = steamID
		};

		SteamUnifiedMessages.ServiceMethodResponse response;

		try {
			response = await UnifiedTwoFactorService.SendMessage(x => x.AddAuthenticator(request)).ToLongRunningTask().ConfigureAwait(false);
		} catch (Exception e) {
			ArchiLogger.LogGenericWarningException(e);

			return null;
		}

		if (response.Result != EResult.OK) {
			return null;
		}

		CTwoFactor_AddAuthenticator_Response body = response.GetDeserializedResponse<CTwoFactor_AddAuthenticator_Response>();

		return body;
	}

	internal async Task<CTwoFactor_FinalizeAddAuthenticator_Response?> FinalizeAuthenticator(ulong steamID, string activationCode, string authenticatorCode, ulong authenticatorTime) {
		if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		ArgumentException.ThrowIfNullOrEmpty(activationCode);
		ArgumentException.ThrowIfNullOrEmpty(authenticatorCode);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(authenticatorTime);

		if (Client == null) {
			throw new InvalidOperationException(nameof(Client));
		}

		if (!Client.IsConnected) {
			return null;
		}

		CTwoFactor_FinalizeAddAuthenticator_Request request = new() {
			activation_code = activationCode,
			authenticator_code = authenticatorCode,
			authenticator_time = authenticatorTime,
			steamid = steamID
		};

		SteamUnifiedMessages.ServiceMethodResponse response;

		try {
			response = await UnifiedTwoFactorService.SendMessage(x => x.FinalizeAddAuthenticator(request)).ToLongRunningTask().ConfigureAwait(false);
		} catch (Exception e) {
			ArchiLogger.LogGenericWarningException(e);

			return null;
		}

		if (response.Result != EResult.OK) {
			return null;
		}

		CTwoFactor_FinalizeAddAuthenticator_Response body = response.GetDeserializedResponse<CTwoFactor_FinalizeAddAuthenticator_Response>();

		return body;
	}
}
