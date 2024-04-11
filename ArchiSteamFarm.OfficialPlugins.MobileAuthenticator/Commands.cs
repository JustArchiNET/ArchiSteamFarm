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
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers.Json;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Steam;
using SteamKit2;
using SteamKit2.Internal;

namespace ArchiSteamFarm.OfficialPlugins.MobileAuthenticator;

internal static class Commands {
	internal static async Task<string?> OnBotCommand(Bot bot, EAccess access, string message, string[] args, ulong steamID = 0) {
		ArgumentNullException.ThrowIfNull(bot);

		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(message);

		if ((args == null) || (args.Length == 0)) {
			throw new ArgumentNullException(nameof(args));
		}

		if ((steamID != 0) && !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		switch (args.Length) {
			case 1:
				switch (args[0].ToUpperInvariant()) {
					case "2FAFINALIZEDFORCE":
						return await ResponseTwoFactorFinalized(access, bot).ConfigureAwait(false);
					case "2FAINIT":
						return await ResponseTwoFactorInit(access, bot).ConfigureAwait(false);
				}

				break;
			default:
				switch (args[0].ToUpperInvariant()) {
					case "2FAFINALIZE" when args.Length > 2:
						return await ResponseTwoFactorFinalize(access, args[1], Utilities.GetArgsAsText(message, 2), steamID).ConfigureAwait(false);
					case "2FAFINALIZE":
						return await ResponseTwoFactorFinalize(access, bot, args[1]).ConfigureAwait(false);
					case "2FAFINALIZED" when args.Length > 2:
						return await ResponseTwoFactorFinalized(access, args[1], Utilities.GetArgsAsText(message, 2), steamID).ConfigureAwait(false);
					case "2FAFINALIZED":
						return await ResponseTwoFactorFinalized(access, bot, args[1]).ConfigureAwait(false);
					case "2FAFINALIZEDFORCE":
						return await ResponseTwoFactorFinalized(access, Utilities.GetArgsAsText(args, 1, ","), steamID: steamID).ConfigureAwait(false);
					case "2FAINIT":
						return await ResponseTwoFactorInit(access, Utilities.GetArgsAsText(args, 1, ","), steamID).ConfigureAwait(false);
				}

				break;
		}

		return null;
	}

	private static async Task<string?> ResponseTwoFactorFinalize(EAccess access, Bot bot, string activationCode) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentNullException.ThrowIfNull(bot);
		ArgumentException.ThrowIfNullOrEmpty(activationCode);

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

		Steam.Security.MobileAuthenticator? mobileAuthenticator = json.ToJsonObject<Steam.Security.MobileAuthenticator>();

		if (mobileAuthenticator == null) {
			return bot.Commands.FormatBotResponse(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(json)));
		}

		mobileAuthenticator.Init(bot);

		MobileAuthenticatorHandler? mobileAuthenticatorHandler = bot.GetHandler<MobileAuthenticatorHandler>();

		if (mobileAuthenticatorHandler == null) {
			throw new InvalidOperationException(nameof(mobileAuthenticatorHandler));
		}

		ulong steamTime = await mobileAuthenticator.GetSteamTime().ConfigureAwait(false);

		string? code = mobileAuthenticator.GenerateTokenForTime(steamTime);

		if (string.IsNullOrEmpty(code)) {
			return bot.Commands.FormatBotResponse(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, nameof(mobileAuthenticator.GenerateTokenForTime)));
		}

		CTwoFactor_FinalizeAddAuthenticator_Response? response = await mobileAuthenticatorHandler.FinalizeAuthenticator(bot.SteamID, activationCode, code, steamTime).ConfigureAwait(false);

		if (response == null) {
			return bot.Commands.FormatBotResponse(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, nameof(mobileAuthenticatorHandler.FinalizeAuthenticator)));
		}

		if (!response.success) {
			EResult result = (EResult) response.status;

			return bot.Commands.FormatBotResponse(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, result));
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

	private static async Task<string?> ResponseTwoFactorFinalize(EAccess access, string botNames, string activationCode, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);
		ArgumentException.ThrowIfNullOrEmpty(activationCode);

		if ((steamID != 0) && !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? Steam.Interaction.Commands.FormatStaticResponse(string.Format(CultureInfo.CurrentCulture, Strings.BotNotFound, botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => ResponseTwoFactorFinalize(Steam.Interaction.Commands.GetProxyAccess(bot, access, steamID), bot, activationCode))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private static async Task<string?> ResponseTwoFactorFinalized(EAccess access, Bot bot, string? activationCode = null) {
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

		Steam.Security.MobileAuthenticator? mobileAuthenticator = json.ToJsonObject<Steam.Security.MobileAuthenticator>();

		if (mobileAuthenticator == null) {
			return bot.Commands.FormatBotResponse(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(json)));
		}

		mobileAuthenticator.Init(bot);

		if (!string.IsNullOrEmpty(activationCode)) {
			string? generatedCode = await mobileAuthenticator.GenerateToken().ConfigureAwait(false);

			if (generatedCode != activationCode) {
				return bot.Commands.FormatBotResponse(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, $"{generatedCode} != {activationCode}"));
			}
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

	private static async Task<string?> ResponseTwoFactorFinalized(EAccess access, string botNames, string? activationCode = null, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);

		if ((steamID != 0) && !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? Steam.Interaction.Commands.FormatStaticResponse(string.Format(CultureInfo.CurrentCulture, Strings.BotNotFound, botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => ResponseTwoFactorFinalized(Steam.Interaction.Commands.GetProxyAccess(bot, access, steamID), bot, activationCode))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

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

		MaFileData maFileData = new(response, bot.SteamID, deviceID);

		string maFilePendingPath = $"{bot.GetFilePath(Bot.EFileType.MobileAuthenticator)}.PENDING";
		string json = maFileData.ToJsonText(true);

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

		ArgumentException.ThrowIfNullOrEmpty(botNames);

		if ((steamID != 0) && !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? Steam.Interaction.Commands.FormatStaticResponse(string.Format(CultureInfo.CurrentCulture, Strings.BotNotFound, botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => ResponseTwoFactorInit(Steam.Interaction.Commands.GetProxyAccess(bot, access, steamID), bot))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}
}
