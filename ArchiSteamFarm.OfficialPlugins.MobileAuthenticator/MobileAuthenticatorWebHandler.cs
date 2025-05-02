// ----------------------------------------------------------------------------------------------
//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// ----------------------------------------------------------------------------------------------
// |
// Copyright 2015-2025 Łukasz "JustArchi" Domeradzki
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
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Integration;
using ArchiSteamFarm.Web;
using SteamKit2;
using SteamKit2.Internal;

namespace ArchiSteamFarm.OfficialPlugins.MobileAuthenticator;

internal static class MobileAuthenticatorWebHandler {
	private const string TwoFactorService = "ITwoFactorService";

	internal static async Task<CTwoFactor_AddAuthenticator_Response?> AddAuthenticator(Bot bot, string deviceID) {
		ArgumentNullException.ThrowIfNull(bot);
		ArgumentException.ThrowIfNullOrEmpty(deviceID);

		if (!bot.IsConnectedAndLoggedOn) {
			return null;
		}

		string? accessToken = bot.AccessToken;

		if (string.IsNullOrEmpty(accessToken)) {
			return null;
		}

		Dictionary<string, object?> arguments = new(5, StringComparer.Ordinal) {
			{ "access_token", accessToken },
			{ "authenticator_time", Utilities.GetUnixTime() },
			{ "authenticator_type", 1 },
			{ "device_identifier", deviceID },
			{ "steamid", bot.SteamID }
		};

		using WebAPI.AsyncInterface twoFactorService = bot.SteamConfiguration.GetAsyncWebAPIInterface(TwoFactorService);

		twoFactorService.Timeout = bot.ArchiWebHandler.WebBrowser.Timeout;

		CTwoFactor_AddAuthenticator_Response? response = null;

		for (byte i = 0; (i < WebBrowser.MaxTries) && (response == null); i++) {
			if ((i > 0) && (ArchiWebHandler.WebLimiterDelay > 0)) {
				await Task.Delay(ArchiWebHandler.WebLimiterDelay).ConfigureAwait(false);
			}

			try {
				// TODO: Move to CallProtobufAsync<TResponse, TRequest> when we update to SK2 3.2.0+ <https://github.com/SteamRE/SteamKit/pull/1537>
				response = await ArchiWebHandler.WebLimitRequest(
					WebAPI.DefaultBaseAddress,

					// ReSharper disable once AccessToDisposedClosure
					async () => await twoFactorService.CallProtobufAsync<CTwoFactor_AddAuthenticator_Response>(HttpMethod.Post, "AddAuthenticator", args: arguments).ConfigureAwait(false)
				).ConfigureAwait(false);
			} catch (TaskCanceledException e) {
				bot.ArchiLogger.LogGenericDebuggingException(e);
			} catch (Exception e) {
				bot.ArchiLogger.LogGenericWarningException(e);
			}
		}

		if (response == null) {
			bot.ArchiLogger.LogGenericWarning(Strings.FormatErrorRequestFailedTooManyTimes(WebBrowser.MaxTries));

			return null;
		}

		return response;
	}

	internal static async Task<CTwoFactor_FinalizeAddAuthenticator_Response?> FinalizeAuthenticator(Bot bot, string activationCode, string authenticatorCode, ulong authenticatorTime) {
		ArgumentNullException.ThrowIfNull(bot);
		ArgumentException.ThrowIfNullOrEmpty(activationCode);
		ArgumentException.ThrowIfNullOrEmpty(authenticatorCode);
		ArgumentOutOfRangeException.ThrowIfZero(authenticatorTime);

		if (!bot.IsConnectedAndLoggedOn) {
			return null;
		}

		string? accessToken = bot.AccessToken;

		if (string.IsNullOrEmpty(accessToken)) {
			return null;
		}

		Dictionary<string, object?> arguments = new(5, StringComparer.Ordinal) {
			{ "access_token", accessToken },
			{ "activation_code", activationCode },
			{ "authenticator_code", authenticatorCode },
			{ "authenticator_time", authenticatorTime },
			{ "steamid", bot.SteamID }
		};

		using WebAPI.AsyncInterface twoFactorService = bot.SteamConfiguration.GetAsyncWebAPIInterface(TwoFactorService);

		twoFactorService.Timeout = bot.ArchiWebHandler.WebBrowser.Timeout;

		CTwoFactor_FinalizeAddAuthenticator_Response? response = null;

		for (byte i = 0; (i < WebBrowser.MaxTries) && (response == null); i++) {
			if ((i > 0) && (ArchiWebHandler.WebLimiterDelay > 0)) {
				await Task.Delay(ArchiWebHandler.WebLimiterDelay).ConfigureAwait(false);
			}

			try {
				// TODO: Move to CallProtobufAsync<TResponse, TRequest> when we update to SK2 3.2.0+ <https://github.com/SteamRE/SteamKit/pull/1537>
				response = await ArchiWebHandler.WebLimitRequest(
					WebAPI.DefaultBaseAddress,

					// ReSharper disable once AccessToDisposedClosure
					async () => await twoFactorService.CallProtobufAsync<CTwoFactor_FinalizeAddAuthenticator_Response>(HttpMethod.Post, "FinalizeAddAuthenticator", args: arguments).ConfigureAwait(false)
				).ConfigureAwait(false);
			} catch (TaskCanceledException e) {
				bot.ArchiLogger.LogGenericDebuggingException(e);
			} catch (Exception e) {
				bot.ArchiLogger.LogGenericWarningException(e);
			}
		}

		if (response == null) {
			bot.ArchiLogger.LogGenericWarning(Strings.FormatErrorRequestFailedTooManyTimes(WebBrowser.MaxTries));

			return null;
		}

		return response;
	}
}
