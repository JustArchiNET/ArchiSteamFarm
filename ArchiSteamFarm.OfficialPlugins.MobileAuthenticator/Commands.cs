// ----------------------------------------------------------------------------------------------
//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// ----------------------------------------------------------------------------------------------
// |
// Copyright 2015-2026 Łukasz "JustArchi" Domeradzki
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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers.Json;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Steam;
using SteamKit2;
using SteamKit2.Internal;

namespace ArchiSteamFarm.OfficialPlugins.MobileAuthenticator;

internal static class Commands {
	internal static async Task<string?> OnBotCommand(Bot bot, EBotUserAccess access, string message, string[] args, ulong steamID = 0, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(bot);

		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EBotUserAccess));
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
						return await ResponseTwoFactorFinalized(access, bot, cancellationToken: cancellationToken).ConfigureAwait(false);
					case "2FAINIT":
						return await ResponseTwoFactorInit(access, bot, cancellationToken).ConfigureAwait(false);
				}

				break;
			default:
				switch (args[0].ToUpperInvariant()) {
					case "2FAFINALIZE" when args.Length > 2:
						return await ResponseTwoFactorFinalize(access, args[1], Utilities.GetArgsAsText(message, 2), steamID, cancellationToken).ConfigureAwait(false);
					case "2FAFINALIZE":
						return await ResponseTwoFactorFinalize(access, bot, args[1], cancellationToken).ConfigureAwait(false);
					case "2FAFINALIZED" when args.Length > 2:
						return await ResponseTwoFactorFinalized(access, args[1], Utilities.GetArgsAsText(message, 2), steamID, cancellationToken).ConfigureAwait(false);
					case "2FAFINALIZED":
						return await ResponseTwoFactorFinalized(access, bot, args[1], cancellationToken).ConfigureAwait(false);
					case "2FAFINALIZEDFORCE":
						return await ResponseTwoFactorFinalized(access, Utilities.GetArgsAsText(args, 1, ","), steamID: steamID, cancellationToken: cancellationToken).ConfigureAwait(false);
					case "2FAINIT":
						return await ResponseTwoFactorInit(access, Utilities.GetArgsAsText(args, 1, ","), steamID, cancellationToken).ConfigureAwait(false);
				}

				break;
		}

		return null;
	}

	private static async Task<string?> ResponseTwoFactorFinalize(EBotUserAccess access, Bot bot, string activationCode, CancellationToken cancellationToken = default) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EBotUserAccess));
		}

		ArgumentNullException.ThrowIfNull(bot);
		ArgumentException.ThrowIfNullOrEmpty(activationCode);

		if (access < EBotUserAccess.Master) {
			return access > EBotUserAccess.None ? bot.Commands.FormatBotResponse(Strings.ErrorAccessDenied) : null;
		}

		if (bot.HasMobileAuthenticator) {
			return bot.Commands.FormatBotResponse(Strings.FormatWarningFailedWithError(nameof(bot.HasMobileAuthenticator)));
		}

		if (!bot.IsConnectedAndLoggedOn) {
			return bot.Commands.FormatBotResponse(Strings.BotNotConnected);
		}

		string maFilePath = bot.GetFilePath(EBotFileType.MobileAuthenticator);
		string maFilePendingPath = $"{maFilePath}.PENDING";

		if (!File.Exists(maFilePendingPath)) {
			return bot.Commands.FormatBotResponse(Strings.NothingFound);
		}

		string json;

		try {
			json = await File.ReadAllTextAsync(maFilePendingPath, cancellationToken).ConfigureAwait(false);
		} catch (Exception e) {
			bot.ArchiLogger.LogGenericException(e);

			return bot.Commands.FormatBotResponse(Strings.FormatWarningFailedWithError(e.Message));
		}

		if (string.IsNullOrEmpty(json)) {
			return bot.Commands.FormatBotResponse(Strings.FormatErrorIsEmpty(nameof(json)));
		}

		Steam.Security.MobileAuthenticator? mobileAuthenticator = json.ToJsonObject<Steam.Security.MobileAuthenticator>();

		if (mobileAuthenticator == null) {
			return bot.Commands.FormatBotResponse(Strings.FormatErrorIsEmpty(nameof(json)));
		}

		mobileAuthenticator.Init(bot);

		ulong steamTime = await mobileAuthenticator.GetSteamTime(cancellationToken).ConfigureAwait(false);

		string? code = mobileAuthenticator.GenerateTokenForTime(steamTime);

		if (string.IsNullOrEmpty(code)) {
			return bot.Commands.FormatBotResponse(Strings.FormatWarningFailedWithError(nameof(mobileAuthenticator.GenerateTokenForTime)));
		}

		CTwoFactor_FinalizeAddAuthenticator_Response? response = await MobileAuthenticatorWebHandler.FinalizeAuthenticator(bot, activationCode, code, steamTime, cancellationToken).ConfigureAwait(false);

		if (response == null) {
			return bot.Commands.FormatBotResponse(Strings.FormatWarningFailedWithError(nameof(MobileAuthenticatorWebHandler.FinalizeAuthenticator)));
		}

		if (!response.success) {
			EResult result = (EResult) response.status;

			return bot.Commands.FormatBotResponse(Strings.FormatWarningFailedWithError(result));
		}

		if (!bot.TryImportAuthenticator(mobileAuthenticator)) {
			return bot.Commands.FormatBotResponse(Strings.FormatWarningFailedWithError(nameof(bot.TryImportAuthenticator)));
		}

		string maFileFinishedPath = $"{maFilePath}.NEW";

		try {
			File.Move(maFilePendingPath, maFileFinishedPath, true);
		} catch (Exception e) {
			bot.ArchiLogger.LogGenericException(e);

			return bot.Commands.FormatBotResponse(Strings.FormatWarningFailedWithError(e.Message));
		}

		return bot.Commands.FormatBotResponse(Strings.Done);
	}

	private static async Task<string?> ResponseTwoFactorFinalize(EBotUserAccess access, string botNames, string activationCode, ulong steamID = 0, CancellationToken cancellationToken = default) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EBotUserAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);
		ArgumentException.ThrowIfNullOrEmpty(activationCode);

		if ((steamID != 0) && !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EBotUserAccess.Owner ? Steam.Interaction.Commands.FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => ResponseTwoFactorFinalize(Steam.Interaction.Commands.GetProxyAccess(bot, access, steamID), bot, activationCode, cancellationToken)), cancellationToken).ConfigureAwait(false);

		List<string> responses = [.. results.Where(static result => !string.IsNullOrEmpty(result)).Select(static result => result!)];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private static async Task<string?> ResponseTwoFactorFinalized(EBotUserAccess access, Bot bot, string? activationCode = null, CancellationToken cancellationToken = default) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EBotUserAccess));
		}

		ArgumentNullException.ThrowIfNull(bot);

		if (access < EBotUserAccess.Master) {
			return access > EBotUserAccess.None ? bot.Commands.FormatBotResponse(Strings.ErrorAccessDenied) : null;
		}

		if (bot.HasMobileAuthenticator) {
			return bot.Commands.FormatBotResponse(Strings.FormatWarningFailedWithError(nameof(bot.HasMobileAuthenticator)));
		}

		string maFilePath = bot.GetFilePath(EBotFileType.MobileAuthenticator);
		string maFilePendingPath = $"{maFilePath}.PENDING";

		if (!File.Exists(maFilePendingPath)) {
			return bot.Commands.FormatBotResponse(Strings.NothingFound);
		}

		string json;

		try {
			json = await File.ReadAllTextAsync(maFilePendingPath, cancellationToken).ConfigureAwait(false);
		} catch (Exception e) {
			bot.ArchiLogger.LogGenericException(e);

			return bot.Commands.FormatBotResponse(Strings.FormatWarningFailedWithError(e.Message));
		}

		if (string.IsNullOrEmpty(json)) {
			return bot.Commands.FormatBotResponse(Strings.FormatErrorIsEmpty(nameof(json)));
		}

		Steam.Security.MobileAuthenticator? mobileAuthenticator = json.ToJsonObject<Steam.Security.MobileAuthenticator>();

		if (mobileAuthenticator == null) {
			return bot.Commands.FormatBotResponse(Strings.FormatErrorIsEmpty(nameof(json)));
		}

		mobileAuthenticator.Init(bot);

		if (!string.IsNullOrEmpty(activationCode)) {
			string? generatedCode = await mobileAuthenticator.GenerateToken(cancellationToken).ConfigureAwait(false);

			if (string.IsNullOrEmpty(generatedCode)) {
				return bot.Commands.FormatBotResponse(Strings.FormatWarningFailedWithError(nameof(generatedCode)));
			}

			if (!generatedCode.Equals(activationCode, StringComparison.OrdinalIgnoreCase)) {
				return bot.Commands.FormatBotResponse(Strings.FormatWarningFailedWithError($"{generatedCode} != {activationCode}"));
			}
		}

		if (!bot.TryImportAuthenticator(mobileAuthenticator)) {
			return bot.Commands.FormatBotResponse(Strings.FormatWarningFailedWithError(nameof(bot.TryImportAuthenticator)));
		}

		string maFileFinishedPath = $"{maFilePath}.NEW";

		try {
			File.Move(maFilePendingPath, maFileFinishedPath, true);
		} catch (Exception e) {
			bot.ArchiLogger.LogGenericException(e);

			return bot.Commands.FormatBotResponse(Strings.FormatWarningFailedWithError(e.Message));
		}

		return bot.Commands.FormatBotResponse(Strings.Done);
	}

	private static async Task<string?> ResponseTwoFactorFinalized(EBotUserAccess access, string botNames, string? activationCode = null, ulong steamID = 0, CancellationToken cancellationToken = default) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EBotUserAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);

		if ((steamID != 0) && !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EBotUserAccess.Owner ? Steam.Interaction.Commands.FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => ResponseTwoFactorFinalized(Steam.Interaction.Commands.GetProxyAccess(bot, access, steamID), bot, activationCode, cancellationToken)), cancellationToken).ConfigureAwait(false);

		List<string> responses = [.. results.Where(static result => !string.IsNullOrEmpty(result)).Select(static result => result!)];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private static async Task<string?> ResponseTwoFactorInit(EBotUserAccess access, Bot bot, CancellationToken cancellationToken = default) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EBotUserAccess));
		}

		ArgumentNullException.ThrowIfNull(bot);

		if (access < EBotUserAccess.Master) {
			return access > EBotUserAccess.None ? bot.Commands.FormatBotResponse(Strings.ErrorAccessDenied) : null;
		}

		if (bot.HasMobileAuthenticator) {
			return bot.Commands.FormatBotResponse(Strings.FormatWarningFailedWithError(nameof(bot.HasMobileAuthenticator)));
		}

		if (!bot.IsConnectedAndLoggedOn) {
			return bot.Commands.FormatBotResponse(Strings.BotNotConnected);
		}

		string deviceID = $"android:{Guid.NewGuid()}";

		CTwoFactor_AddAuthenticator_Response? response = await MobileAuthenticatorWebHandler.AddAuthenticator(bot, deviceID, cancellationToken).ConfigureAwait(false);

		if (response == null) {
			return bot.Commands.FormatBotResponse(Strings.FormatWarningFailedWithError(nameof(MobileAuthenticatorWebHandler.AddAuthenticator)));
		}

		EResult result = (EResult) response.status;

		if (result != EResult.OK) {
			return bot.Commands.FormatBotResponse(Strings.FormatWarningFailedWithError(result));
		}

		MaFileData maFileData = new(response, bot.SteamID, deviceID);

		string maFilePendingPath = $"{bot.GetFilePath(EBotFileType.MobileAuthenticator)}.PENDING";
		string json = maFileData.ToJsonText(true);

		try {
			await File.WriteAllTextAsync(maFilePendingPath, json, cancellationToken).ConfigureAwait(false);
		} catch (Exception e) {
			bot.ArchiLogger.LogGenericException(e);

			return bot.Commands.FormatBotResponse(Strings.FormatWarningFailedWithError(e.Message));
		}

		return bot.Commands.FormatBotResponse(Strings.Done);
	}

	private static async Task<string?> ResponseTwoFactorInit(EBotUserAccess access, string botNames, ulong steamID = 0, CancellationToken cancellationToken = default) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EBotUserAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);

		if ((steamID != 0) && !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EBotUserAccess.Owner ? Steam.Interaction.Commands.FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => ResponseTwoFactorInit(Steam.Interaction.Commands.GetProxyAccess(bot, access, steamID), bot, cancellationToken)), cancellationToken).ConfigureAwait(false);

		List<string> responses = [.. results.Where(static result => !string.IsNullOrEmpty(result)).Select(static result => result!)];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}
}
