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
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Steam;
using Newtonsoft.Json;
using SteamKit2;
using SteamKit2.Internal;

namespace ArchiSteamFarm.OfficialPlugins.MobileAuthenticator;

internal static class Commands {
	private const byte MaxFinalizationAttempts = 900 / Steam.Security.MobileAuthenticator.CodeInterval;

	internal static async Task<string?> OnBotCommand(Bot bot, EAccess access, string message, string[] args, ulong steamID = 0) {
		ArgumentNullException.ThrowIfNull(bot);

		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		if (string.IsNullOrEmpty(message)) {
			throw new ArgumentNullException(nameof(message));
		}

		if ((args == null) || (args.Length == 0)) {
			throw new ArgumentNullException(nameof(args));
		}

		if ((steamID != 0) && !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		switch (args.Length) {
			case 1:
				switch (args[0].ToUpperInvariant()) {
					case "2FAINIT":
						return await ResponseTwoFactorInit(access, bot).ConfigureAwait(false);
				}

				break;
			default:
				switch (args[0].ToUpperInvariant()) {
					case "2FAINIT":
						return await ResponseTwoFactorInit(access, Utilities.GetArgsAsText(args, 1, ","), steamID).ConfigureAwait(false);
					case "2FASMS" when args.Length > 2:
						return await ResponseTwoFactorFinalize(access, args[1], Utilities.GetArgsAsText(message, 2), steamID).ConfigureAwait(false);
					case "2FASMS":
						return await ResponseTwoFactorFinalize(access, bot, args[1]).ConfigureAwait(false);
				}

				break;
		}

		return null;
	}

	private static async Task<string?> ResponseTwoFactorFinalize(EAccess access, Bot bot, string smsCode) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentNullException.ThrowIfNull(bot);

		if (string.IsNullOrEmpty(smsCode)) {
			throw new ArgumentNullException(nameof(smsCode));
		}

		if (access < EAccess.Master) {
			return access > EAccess.None ? bot.Commands.FormatBotResponse(Strings.ErrorAccessDenied) : null;
		}

		if (bot.HasMobileAuthenticator) {
			return bot.Commands.FormatBotResponse(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, nameof(bot.HasMobileAuthenticator)));
		}

		if (!bot.IsConnectedAndLoggedOn) {
			return bot.Commands.FormatBotResponse(Strings.BotNotConnected);
		}

		string maFilePath = bot.GetFilePath(Bot.EFileType.MobileAuthenticator);
		string maFilePendingPath = $"{maFilePath}.PENDING";

		if (!File.Exists(maFilePendingPath)) {
			return bot.Commands.FormatBotResponse(Strings.NothingFound);
		}

		string json;

		try {
			json = await File.ReadAllTextAsync(maFilePendingPath).ConfigureAwait(false);
		} catch (Exception e) {
			bot.ArchiLogger.LogGenericException(e);

			return bot.Commands.FormatBotResponse(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, e.Message));
		}

		if (string.IsNullOrEmpty(json)) {
			return bot.Commands.FormatBotResponse(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(json)));
		}

		Steam.Security.MobileAuthenticator? mobileAuthenticator = JsonConvert.DeserializeObject<Steam.Security.MobileAuthenticator>(json);

		if (mobileAuthenticator == null) {
			return bot.Commands.FormatBotResponse(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(json)));
		}

		mobileAuthenticator.Init(bot);

		MobileAuthenticatorHandler? mobileAuthenticatorHandler = bot.GetHandler<MobileAuthenticatorHandler>();

		if (mobileAuthenticatorHandler == null) {
			throw new InvalidOperationException(nameof(mobileAuthenticatorHandler));
		}

		ulong steamTime = await mobileAuthenticator.GetSteamTime().ConfigureAwait(false);

		bool successFinalizing = false;

		for (byte i = 0; i < MaxFinalizationAttempts; i++) {
			if (i > 0) {
				steamTime += Steam.Security.MobileAuthenticator.CodeInterval;
			}

			string? code = mobileAuthenticator.GenerateTokenForTime(steamTime);

			if (string.IsNullOrEmpty(code)) {
				return bot.Commands.FormatBotResponse(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, nameof(mobileAuthenticator.GenerateTokenForTime)));
			}

			// ReSharper disable once RedundantSuppressNullableWarningExpression - required for .NET Framework
			CTwoFactor_FinalizeAddAuthenticator_Response? response = await mobileAuthenticatorHandler.FinalizeAuthenticator(bot.SteamID, smsCode, code!, steamTime).ConfigureAwait(false);

			if (response == null) {
				return bot.Commands.FormatBotResponse(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, nameof(mobileAuthenticatorHandler.FinalizeAuthenticator)));
			}

			if (response.want_more) {
				// OK, whatever
				continue;
			}

			if (!response.success) {
				EResult result = (EResult) response.status;

				return bot.Commands.FormatBotResponse(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, result));
			}

			successFinalizing = true;

			break;
		}

		if (!successFinalizing) {
			return bot.Commands.FormatBotResponse(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, MaxFinalizationAttempts));
		}

		if (!bot.TryImportAuthenticator(mobileAuthenticator)) {
			return bot.Commands.FormatBotResponse(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, nameof(bot.TryImportAuthenticator)));
		}

		string maFileFinishedPath = $"{maFilePath}.NEW";

		try {
			File.Move(maFilePendingPath, maFileFinishedPath, true);
		} catch (Exception e) {
			bot.ArchiLogger.LogGenericException(e);

			return bot.Commands.FormatBotResponse(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, e.Message));
		}

		return bot.Commands.FormatBotResponse(Strings.Done);
	}

	private static async Task<string?> ResponseTwoFactorFinalize(EAccess access, string botNames, string smsCode, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		if (string.IsNullOrEmpty(botNames)) {
			throw new ArgumentNullException(nameof(botNames));
		}

		if (string.IsNullOrEmpty(smsCode)) {
			throw new ArgumentNullException(nameof(smsCode));
		}

		if ((steamID != 0) && !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? Steam.Interaction.Commands.FormatStaticResponse(string.Format(CultureInfo.CurrentCulture, Strings.BotNotFound, botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => ResponseTwoFactorFinalize(Steam.Interaction.Commands.GetProxyAccess(bot, access, steamID), bot, smsCode))).ConfigureAwait(false);

		List<string> responses = new(results.Where(static result => !string.IsNullOrEmpty(result))!);

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private static async Task<string?> ResponseTwoFactorInit(EAccess access, Bot bot) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentNullException.ThrowIfNull(bot);

		if (access < EAccess.Master) {
			return access > EAccess.None ? bot.Commands.FormatBotResponse(Strings.ErrorAccessDenied) : null;
		}

		if (bot.HasMobileAuthenticator) {
			return bot.Commands.FormatBotResponse(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, nameof(bot.HasMobileAuthenticator)));
		}

		if (!bot.IsConnectedAndLoggedOn) {
			return bot.Commands.FormatBotResponse(Strings.BotNotConnected);
		}

		MobileAuthenticatorHandler? mobileAuthenticatorHandler = bot.GetHandler<MobileAuthenticatorHandler>();

		if (mobileAuthenticatorHandler == null) {
			throw new InvalidOperationException(nameof(mobileAuthenticatorHandler));
		}

		string deviceID = $"android:{Guid.NewGuid()}";

		CTwoFactor_AddAuthenticator_Response? response = await mobileAuthenticatorHandler.AddAuthenticator(bot.SteamID, deviceID).ConfigureAwait(false);

		if (response == null) {
			return bot.Commands.FormatBotResponse(Strings.WarningFailed);
		}

		EResult result = (EResult) response.status;

		if (result != EResult.OK) {
			return bot.Commands.FormatBotResponse(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, result));
		}

		MaFileData maFileData = new(response, deviceID);

		string maFilePendingPath = $"{bot.GetFilePath(Bot.EFileType.MobileAuthenticator)}.PENDING";
		string json = JsonConvert.SerializeObject(maFileData, Formatting.Indented);

		try {
			await File.WriteAllTextAsync(maFilePendingPath, json).ConfigureAwait(false);
		} catch (Exception e) {
			bot.ArchiLogger.LogGenericException(e);

			return bot.Commands.FormatBotResponse(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, e.Message));
		}

		return bot.Commands.FormatBotResponse(Strings.Done);
	}

	private static async Task<string?> ResponseTwoFactorInit(EAccess access, string botNames, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		if (string.IsNullOrEmpty(botNames)) {
			throw new ArgumentNullException(nameof(botNames));
		}

		if ((steamID != 0) && !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? Steam.Interaction.Commands.FormatStaticResponse(string.Format(CultureInfo.CurrentCulture, Strings.BotNotFound, botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => ResponseTwoFactorInit(Steam.Interaction.Commands.GetProxyAccess(bot, access, steamID), bot))).ConfigureAwait(false);

		List<string> responses = new(results.Where(static result => !string.IsNullOrEmpty(result))!);

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}
}
