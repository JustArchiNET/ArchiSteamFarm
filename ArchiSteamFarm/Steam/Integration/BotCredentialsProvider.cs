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
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Localization;
using SteamKit2;
using SteamKit2.Authentication;

namespace ArchiSteamFarm.Steam.Integration;

internal sealed class BotCredentialsProvider : IAuthenticator {
	private const byte MaxLoginFailures = 5;

	private readonly Bot Bot;
	private readonly CancellationTokenSource CancellationTokenSource;

	private byte LoginFailures;

	internal BotCredentialsProvider(Bot bot, CancellationTokenSource cancellationTokenSource) {
		ArgumentNullException.ThrowIfNull(bot);
		ArgumentNullException.ThrowIfNull(cancellationTokenSource);

		Bot = bot;
		CancellationTokenSource = cancellationTokenSource;
	}

	public async Task<bool> AcceptDeviceConfirmationAsync() {
		if (Bot.HasMobileAuthenticator || Bot.HasLoginCodeReady) {
			// We don't want device confirmation under any circumstance, we can provide the code on our own
			return false;
		}

		// Ask the user what he wants
		string input = await ProvideInput(ASF.EUserInputType.DeviceConfirmation, false).ConfigureAwait(false);

		return input.Equals("Y", StringComparison.OrdinalIgnoreCase);
	}

	public async Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect) => await ProvideInput(ASF.EUserInputType.TwoFactorAuthentication, previousCodeWasIncorrect).ConfigureAwait(false);

	public async Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect) => await ProvideInput(ASF.EUserInputType.SteamGuard, previousCodeWasIncorrect).ConfigureAwait(false);

	private async Task<string> ProvideInput(ASF.EUserInputType inputType, bool previousCodeWasIncorrect) {
		if (!Enum.IsDefined(inputType)) {
			throw new InvalidEnumArgumentException(nameof(inputType), (int) inputType, typeof(ASF.EUserInputType));
		}

		if (previousCodeWasIncorrect && (++LoginFailures >= MaxLoginFailures)) {
			EResult reason = inputType == ASF.EUserInputType.TwoFactorAuthentication ? EResult.TwoFactorCodeMismatch : EResult.InvalidLoginAuthCode;

			Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.BotUnableToLogin, reason, reason));

			if (++LoginFailures >= MaxLoginFailures) {
				CancellationTokenSource.Cancel();

				return "";
			}
		}

		string? result = await Bot.RequestInput(inputType, previousCodeWasIncorrect).ConfigureAwait(false);

		if (string.IsNullOrEmpty(result)) {
			CancellationTokenSource.Cancel();
		}

		return result ?? "";
	}
}
