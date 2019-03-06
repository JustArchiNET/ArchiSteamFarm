//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2019 Łukasz "JustArchi" Domeradzki
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
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ArchiSteamFarm.Json;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Plugins;
using JetBrains.Annotations;
using SteamKit2;

namespace ArchiSteamFarm {
	public sealed class Commands {
		private const ushort SteamTypingStatusDelay = 10000; // Steam client broadcasts typing status each 10 seconds

		private readonly Bot Bot;
		private readonly Dictionary<uint, string> CachedGamesOwned = new Dictionary<uint, string>();

		internal Commands([NotNull] Bot bot) => Bot = bot ?? throw new ArgumentNullException(nameof(bot));

		[PublicAPI]
		public static string FormatBotResponse(string response, string botName) {
			if (string.IsNullOrEmpty(response) || string.IsNullOrEmpty(botName)) {
				ASF.ArchiLogger.LogNullError(nameof(response) + " || " + nameof(botName));

				return null;
			}

			return Environment.NewLine + "<" + botName + "> " + response;
		}

		[PublicAPI]
		public string FormatBotResponse(string response) {
			if (string.IsNullOrEmpty(response)) {
				ASF.ArchiLogger.LogNullError(nameof(response));

				return null;
			}

			return "<" + Bot.BotName + "> " + response;
		}

		[PublicAPI]
		public static string FormatStaticResponse(string response) {
			if (string.IsNullOrEmpty(response)) {
				ASF.ArchiLogger.LogNullError(nameof(response));

				return null;
			}

			return "<" + SharedInfo.ASF + "> " + response;
		}

		[PublicAPI]
		public async Task<string> Response(ulong steamID, string message) {
			if ((steamID == 0) || string.IsNullOrEmpty(message)) {
				Bot.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(message));

				return null;
			}

			string[] args = message.Split((char[]) null, StringSplitOptions.RemoveEmptyEntries);

			switch (args.Length) {
				case 0:
					Bot.ArchiLogger.LogNullError(nameof(args));

					return null;
				case 1:

					switch (args[0].ToUpperInvariant()) {
						case "2FA":

							return await Response2FA(steamID).ConfigureAwait(false);
						case "2FANO":

							return await Response2FAConfirm(steamID, false).ConfigureAwait(false);
						case "2FAOK":

							return await Response2FAConfirm(steamID, true).ConfigureAwait(false);
						case "BALANCE":

							return ResponseWalletBalance(steamID);
						case "BGR":

							return ResponseBackgroundGamesRedeemer(steamID);
						case "BL":

							return ResponseBlacklist(steamID);
						case "EXIT":

							return ResponseExit(steamID);
						case "FARM":

							return await ResponseFarm(steamID).ConfigureAwait(false);
						case "HELP":

							return ResponseHelp(steamID);
						case "IB":

							return ResponseIdleBlacklist(steamID);
						case "IQ":

							return ResponseIdleQueue(steamID);
						case "LEVEL":

							return await ResponseLevel(steamID).ConfigureAwait(false);
						case "LOOT":

							return await ResponseLoot(steamID).ConfigureAwait(false);
						case "PASSWORD":

							return ResponsePassword(steamID);
						case "PAUSE":

							return await ResponsePause(steamID, true).ConfigureAwait(false);
						case "PAUSE~":

							return await ResponsePause(steamID, false).ConfigureAwait(false);
						case "RESUME":

							return ResponseResume(steamID);
						case "RESTART":

							return ResponseRestart(steamID);
						case "SA":

							return await ResponseStatus(steamID, SharedInfo.ASF).ConfigureAwait(false);
						case "START":

							return ResponseStart(steamID);
						case "STATS":

							return ResponseStats(steamID);
						case "STATUS":

							return ResponseStatus(steamID).Response;
						case "STOP":

							return ResponseStop(steamID);
						case "UNPACK":

							return await ResponseUnpackBoosters(steamID).ConfigureAwait(false);
						case "UPDATE":

							return await ResponseUpdate(steamID).ConfigureAwait(false);
						case "VERSION":

							return ResponseVersion(steamID);
						default:
							string pluginsResponse = await Core.OnBotCommand(Bot, steamID, message, args).ConfigureAwait(false);

							return !string.IsNullOrEmpty(pluginsResponse) ? pluginsResponse : ResponseUnknown(steamID);
					}
				default:

					switch (args[0].ToUpperInvariant()) {
						case "2FA":

							return await Response2FA(steamID, Utilities.GetArgsAsText(args, 1, ",")).ConfigureAwait(false);
						case "2FANO":

							return await Response2FAConfirm(steamID, Utilities.GetArgsAsText(args, 1, ","), false).ConfigureAwait(false);
						case "2FAOK":

							return await Response2FAConfirm(steamID, Utilities.GetArgsAsText(args, 1, ","), true).ConfigureAwait(false);
						case "ADDLICENSE" when args.Length > 2:

							return await ResponseAddLicense(steamID, args[1], Utilities.GetArgsAsText(args, 2, ",")).ConfigureAwait(false);
						case "ADDLICENSE":

							return await ResponseAddLicense(steamID, args[1]).ConfigureAwait(false);
						case "BALANCE":

							return await ResponseWalletBalance(steamID, Utilities.GetArgsAsText(args, 1, ",")).ConfigureAwait(false);
						case "BGR":

							return await ResponseBackgroundGamesRedeemer(steamID, Utilities.GetArgsAsText(args, 1, ",")).ConfigureAwait(false);
						case "BL":

							return await ResponseBlacklist(steamID, Utilities.GetArgsAsText(args, 1, ",")).ConfigureAwait(false);
						case "BLADD" when args.Length > 2:

							return await ResponseBlacklistAdd(steamID, args[1], Utilities.GetArgsAsText(args, 2, ",")).ConfigureAwait(false);
						case "BLADD":

							return await ResponseBlacklistAdd(steamID, args[1]).ConfigureAwait(false);
						case "BLRM" when args.Length > 2:

							return await ResponseBlacklistRemove(steamID, args[1], Utilities.GetArgsAsText(args, 2, ",")).ConfigureAwait(false);
						case "BLRM":

							return await ResponseBlacklistRemove(steamID, args[1]).ConfigureAwait(false);
						case "FARM":

							return await ResponseFarm(steamID, Utilities.GetArgsAsText(args, 1, ",")).ConfigureAwait(false);
						case "INPUT" when args.Length > 3:

							return await ResponseInput(steamID, args[1], args[2], Utilities.GetArgsAsText(message, 3)).ConfigureAwait(false);
						case "INPUT" when args.Length > 2:

							return ResponseInput(steamID, args[1], args[2]);
						case "IB":

							return await ResponseIdleBlacklist(steamID, Utilities.GetArgsAsText(args, 1, ",")).ConfigureAwait(false);
						case "IBADD" when args.Length > 2:

							return await ResponseIdleBlacklistAdd(steamID, args[1], Utilities.GetArgsAsText(args, 2, ",")).ConfigureAwait(false);
						case "IBADD":

							return await ResponseIdleBlacklistAdd(steamID, args[1]).ConfigureAwait(false);
						case "IBRM" when args.Length > 2:

							return await ResponseIdleBlacklistRemove(steamID, args[1], Utilities.GetArgsAsText(args, 2, ",")).ConfigureAwait(false);
						case "IBRM":

							return await ResponseIdleBlacklistRemove(steamID, args[1]).ConfigureAwait(false);
						case "IQ":

							return await ResponseIdleQueue(steamID, Utilities.GetArgsAsText(args, 1, ",")).ConfigureAwait(false);
						case "IQADD" when args.Length > 2:

							return await ResponseIdleQueueAdd(steamID, args[1], Utilities.GetArgsAsText(args, 2, ",")).ConfigureAwait(false);
						case "IQADD":

							return await ResponseIdleQueueAdd(steamID, args[1]).ConfigureAwait(false);
						case "IQRM" when args.Length > 2:

							return await ResponseIdleQueueRemove(steamID, args[1], Utilities.GetArgsAsText(args, 2, ",")).ConfigureAwait(false);
						case "IQRM":

							return await ResponseIdleQueueRemove(steamID, args[1]).ConfigureAwait(false);
						case "LEVEL":

							return await ResponseLevel(steamID, Utilities.GetArgsAsText(args, 1, ",")).ConfigureAwait(false);
						case "LOOT":

							return await ResponseLoot(steamID, Utilities.GetArgsAsText(args, 1, ",")).ConfigureAwait(false);
						case "LOOT^" when args.Length > 3:

							return await ResponseAdvancedLoot(steamID, args[1], args[2], Utilities.GetArgsAsText(message, 3)).ConfigureAwait(false);
						case "LOOT^" when args.Length > 2:

							return await ResponseAdvancedLoot(steamID, args[1], args[2]).ConfigureAwait(false);
						case "LOOT@" when args.Length > 2:

							return await ResponseLootByRealAppIDs(steamID, args[1], Utilities.GetArgsAsText(args, 2, ",")).ConfigureAwait(false);
						case "LOOT@":

							return await ResponseLootByRealAppIDs(steamID, args[1]).ConfigureAwait(false);
						case "NICKNAME" when args.Length > 2:

							return await ResponseNickname(steamID, args[1], Utilities.GetArgsAsText(message, 2)).ConfigureAwait(false);
						case "NICKNAME":

							return ResponseNickname(steamID, args[1]);
						case "OA":

							return await ResponseOwns(steamID, SharedInfo.ASF, Utilities.GetArgsAsText(message, 1)).ConfigureAwait(false);
						case "OWNS" when args.Length > 2:

							return await ResponseOwns(steamID, args[1], Utilities.GetArgsAsText(message, 2)).ConfigureAwait(false);
						case "OWNS":

							return (await ResponseOwns(steamID, args[1]).ConfigureAwait(false)).Response;
						case "PASSWORD":

							return await ResponsePassword(steamID, Utilities.GetArgsAsText(args, 1, ",")).ConfigureAwait(false);
						case "PAUSE":

							return await ResponsePause(steamID, Utilities.GetArgsAsText(args, 1, ","), true).ConfigureAwait(false);
						case "PAUSE~":

							return await ResponsePause(steamID, Utilities.GetArgsAsText(args, 1, ","), false).ConfigureAwait(false);
						case "PAUSE&" when args.Length > 2:

							return await ResponsePause(steamID, args[1], true, Utilities.GetArgsAsText(message, 2)).ConfigureAwait(false);
						case "PAUSE&":

							return await ResponsePause(steamID, true, args[1]).ConfigureAwait(false);
						case "PLAY" when args.Length > 2:

							return await ResponsePlay(steamID, args[1], Utilities.GetArgsAsText(message, 2)).ConfigureAwait(false);
						case "PLAY":

							return await ResponsePlay(steamID, args[1]).ConfigureAwait(false);
						case "PRIVACY" when args.Length > 2:

							return await ResponsePrivacy(steamID, args[1], Utilities.GetArgsAsText(args, 2, ",")).ConfigureAwait(false);
						case "PRIVACY":

							return await ResponsePrivacy(steamID, args[1]).ConfigureAwait(false);
						case "R" when args.Length > 2:
						case "REDEEM" when args.Length > 2:

							return await ResponseRedeem(steamID, args[1], Utilities.GetArgsAsText(args, 2, ",")).ConfigureAwait(false);
						case "R":
						case "REDEEM":

							return await ResponseRedeem(steamID, args[1]).ConfigureAwait(false);
						case "R^" when args.Length > 3:
						case "REDEEM^" when args.Length > 3:

							return await ResponseAdvancedRedeem(steamID, args[1], args[2], Utilities.GetArgsAsText(args, 3, ",")).ConfigureAwait(false);
						case "R^" when args.Length > 2:
						case "REDEEM^" when args.Length > 2:

							return await ResponseAdvancedRedeem(steamID, args[1], args[2]).ConfigureAwait(false);
						case "RESUME":

							return await ResponseResume(steamID, Utilities.GetArgsAsText(args, 1, ",")).ConfigureAwait(false);
						case "START":

							return await ResponseStart(steamID, Utilities.GetArgsAsText(args, 1, ",")).ConfigureAwait(false);
						case "STATUS":

							return await ResponseStatus(steamID, Utilities.GetArgsAsText(args, 1, ",")).ConfigureAwait(false);
						case "STOP":

							return await ResponseStop(steamID, Utilities.GetArgsAsText(args, 1, ",")).ConfigureAwait(false);
						case "TRANSFER" when args.Length > 2:

							return await ResponseTransfer(steamID, args[1], Utilities.GetArgsAsText(message, 2)).ConfigureAwait(false);
						case "TRANSFER":

							return await ResponseTransfer(steamID, args[1]).ConfigureAwait(false);
						case "TRANSFER^" when args.Length > 4:

							return await ResponseAdvancedTransfer(steamID, args[1], args[2], args[3], Utilities.GetArgsAsText(message, 4)).ConfigureAwait(false);
						case "TRANSFER^" when args.Length > 3:

							return await ResponseAdvancedTransfer(steamID, args[1], args[2], args[3]).ConfigureAwait(false);
						case "TRANSFER@" when args.Length > 3:

							return await ResponseTransferByRealAppIDs(steamID, args[1], args[2], Utilities.GetArgsAsText(message, 3)).ConfigureAwait(false);
						case "TRANSFER@" when args.Length > 2:

							return await ResponseTransferByRealAppIDs(steamID, args[1], args[2]).ConfigureAwait(false);
						case "UNPACK":

							return await ResponseUnpackBoosters(steamID, Utilities.GetArgsAsText(args, 1, ",")).ConfigureAwait(false);
						default:
							string pluginsResponse = await Core.OnBotCommand(Bot, steamID, message, args).ConfigureAwait(false);

							return !string.IsNullOrEmpty(pluginsResponse) ? pluginsResponse : ResponseUnknown(steamID);
					}
			}
		}

		internal async Task HandleMessage(ulong steamID, string message) {
			if ((steamID == 0) || string.IsNullOrEmpty(message)) {
				Bot.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(message));

				return;
			}

			if (!string.IsNullOrEmpty(ASF.GlobalConfig.CommandPrefix)) {
				if (!message.StartsWith(ASF.GlobalConfig.CommandPrefix, StringComparison.OrdinalIgnoreCase)) {
					string pluginsResponse = await Core.OnBotMessage(Bot, steamID, message).ConfigureAwait(false);

					if (!string.IsNullOrEmpty(pluginsResponse)) {
						await Bot.SendMessage(steamID, pluginsResponse).ConfigureAwait(false);
					}

					return;
				}

				message = message.Substring(ASF.GlobalConfig.CommandPrefix.Length);
			}

			Task<string> responseTask = Response(steamID, message);

			bool feedback = Bot.HasPermission(steamID, BotConfig.EPermission.FamilySharing);

			if (feedback && !responseTask.IsCompleted) {
				await Bot.SendTypingMessage(steamID).ConfigureAwait(false);

				while (!responseTask.IsCompleted && (await Task.WhenAny(responseTask, Task.Delay(SteamTypingStatusDelay)).ConfigureAwait(false) != responseTask)) {
					await Bot.SendTypingMessage(steamID).ConfigureAwait(false);
				}
			}

			string response = await responseTask.ConfigureAwait(false);

			if (string.IsNullOrEmpty(response)) {
				if (!feedback) {
					return;
				}

				Bot.ArchiLogger.LogNullError(nameof(response));
				response = FormatBotResponse(Strings.UnknownCommand);
			}

			await Bot.SendMessage(steamID, response).ConfigureAwait(false);
		}

		internal async Task HandleMessage(ulong chatGroupID, ulong chatID, ulong steamID, string message) {
			if ((chatGroupID == 0) || (chatID == 0) || (steamID == 0) || string.IsNullOrEmpty(message)) {
				Bot.ArchiLogger.LogNullError(nameof(chatGroupID) + " || " + nameof(chatID) + " || " + nameof(steamID) + " || " + nameof(message));

				return;
			}

			if (!string.IsNullOrEmpty(ASF.GlobalConfig.CommandPrefix)) {
				if (!message.StartsWith(ASF.GlobalConfig.CommandPrefix, StringComparison.OrdinalIgnoreCase)) {
					string pluginsResponse = await Core.OnBotMessage(Bot, steamID, message).ConfigureAwait(false);

					if (!string.IsNullOrEmpty(pluginsResponse)) {
						await Bot.SendMessage(chatGroupID, chatID, pluginsResponse).ConfigureAwait(false);
					}

					return;
				}

				message = message.Substring(ASF.GlobalConfig.CommandPrefix.Length);
			}

			Task<string> responseTask = Response(steamID, message);

			bool feedback = Bot.HasPermission(steamID, BotConfig.EPermission.FamilySharing);

			if (feedback && !responseTask.IsCompleted) {
				string pleaseWaitMessage = FormatBotResponse(Strings.PleaseWait);

				await Bot.SendMessage(chatGroupID, chatID, pleaseWaitMessage).ConfigureAwait(false);

				while (!responseTask.IsCompleted && (await Task.WhenAny(responseTask, Task.Delay(SteamTypingStatusDelay)).ConfigureAwait(false) != responseTask)) {
					await Bot.SendMessage(chatGroupID, chatID, pleaseWaitMessage).ConfigureAwait(false);
				}
			}

			string response = await responseTask.ConfigureAwait(false);

			if (string.IsNullOrEmpty(response)) {
				if (!feedback) {
					return;
				}

				Bot.ArchiLogger.LogNullError(nameof(response));
				response = FormatBotResponse(Strings.UnknownCommand);
			}

			await Bot.SendMessage(chatGroupID, chatID, response).ConfigureAwait(false);
		}

		internal void OnNewLicenseList() {
			lock (CachedGamesOwned) {
				CachedGamesOwned.Clear();
				CachedGamesOwned.TrimExcess();
			}
		}

		private async Task<string> Response2FA(ulong steamID) {
			if (steamID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(steamID));

				return null;
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			(bool success, string token, string message) = await Bot.Actions.GenerateTwoFactorAuthenticationToken().ConfigureAwait(false);

			return FormatBotResponse(success ? string.Format(Strings.BotAuthenticatorToken, token) : string.Format(Strings.WarningFailedWithError, message));
		}

		[ItemCanBeNull]
		private static async Task<string> Response2FA(ulong steamID, string botNames) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames));

				return null;
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.Response2FA(steamID))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private async Task<string> Response2FAConfirm(ulong steamID, bool confirm) {
			if (steamID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(steamID));

				return null;
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			if (!Bot.IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			if (!Bot.HasMobileAuthenticator) {
				return FormatBotResponse(Strings.BotNoASFAuthenticator);
			}

			(bool success, string message) = await Bot.Actions.HandleTwoFactorAuthenticationConfirmations(confirm).ConfigureAwait(false);

			return FormatBotResponse(success ? message : string.Format(Strings.WarningFailedWithError, message));
		}

		[ItemCanBeNull]
		private static async Task<string> Response2FAConfirm(ulong steamID, string botNames, bool confirm) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames));

				return null;
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.Response2FAConfirm(steamID, confirm))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private async Task<string> ResponseAddLicense(ulong steamID, IReadOnlyCollection<uint> gameIDs) {
			if ((steamID == 0) || (gameIDs == null) || (gameIDs.Count == 0)) {
				Bot.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(gameIDs));

				return null;
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Operator)) {
				return null;
			}

			if (!Bot.IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			StringBuilder response = new StringBuilder();

			foreach (uint gameID in gameIDs) {
				if (await Bot.ArchiWebHandler.AddFreeLicense(gameID).ConfigureAwait(false)) {
					response.AppendLine(FormatBotResponse(string.Format(Strings.BotAddLicenseWithItems, gameID, EResult.OK, "sub/" + gameID)));

					continue;
				}

				SteamApps.FreeLicenseCallback callback;

				try {
					callback = await Bot.SteamApps.RequestFreeLicense(gameID);
				} catch (Exception e) {
					Bot.ArchiLogger.LogGenericWarningException(e);
					response.AppendLine(FormatBotResponse(string.Format(Strings.BotAddLicense, gameID, EResult.Timeout)));

					break;
				}

				if (callback == null) {
					response.AppendLine(FormatBotResponse(string.Format(Strings.BotAddLicense, gameID, EResult.Timeout)));

					break;
				}

				response.AppendLine(FormatBotResponse((callback.GrantedApps.Count > 0) || (callback.GrantedPackages.Count > 0) ? string.Format(Strings.BotAddLicenseWithItems, gameID, callback.Result, string.Join(", ", callback.GrantedApps.Select(appID => "app/" + appID).Union(callback.GrantedPackages.Select(subID => "sub/" + subID)))) : string.Format(Strings.BotAddLicense, gameID, callback.Result)));
			}

			return response.Length > 0 ? response.ToString() : null;
		}

		private async Task<string> ResponseAddLicense(ulong steamID, string targetGameIDs) {
			if ((steamID == 0) || string.IsNullOrEmpty(targetGameIDs)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(targetGameIDs));

				return null;
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Operator)) {
				return null;
			}

			if (!Bot.IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			string[] gameIDs = targetGameIDs.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

			if (gameIDs.Length == 0) {
				return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(gameIDs)));
			}

			HashSet<uint> gamesToRedeem = new HashSet<uint>();

			foreach (string game in gameIDs) {
				if (!uint.TryParse(game, out uint gameID) || (gameID == 0)) {
					return FormatBotResponse(string.Format(Strings.ErrorParsingObject, nameof(gameID)));
				}

				gamesToRedeem.Add(gameID);
			}

			return await ResponseAddLicense(steamID, gamesToRedeem).ConfigureAwait(false);
		}

		[ItemCanBeNull]
		private static async Task<string> ResponseAddLicense(ulong steamID, string botNames, string targetGameIDs) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(targetGameIDs)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(targetGameIDs));

				return null;
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseAddLicense(steamID, targetGameIDs))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private async Task<string> ResponseAdvancedLoot(ulong steamID, string targetAppID, string targetContextID) {
			if ((steamID == 0) || string.IsNullOrEmpty(targetAppID) || string.IsNullOrEmpty(targetContextID)) {
				Bot.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(targetAppID) + " || " + nameof(targetContextID));

				return null;
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			if (!Bot.IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			if (!uint.TryParse(targetAppID, out uint appID) || (appID == 0)) {
				return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(appID)));
			}

			if (!ulong.TryParse(targetContextID, out ulong contextID) || (contextID == 0)) {
				return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(contextID)));
			}

			(bool success, string message) = await Bot.Actions.SendTradeOffer(appID, contextID).ConfigureAwait(false);

			return FormatBotResponse(success ? message : string.Format(Strings.WarningFailedWithError, message));
		}

		[ItemCanBeNull]
		private static async Task<string> ResponseAdvancedLoot(ulong steamID, string botNames, string appID, string contextID) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(appID) || string.IsNullOrEmpty(contextID)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(appID) + " || " + nameof(contextID));

				return null;
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseAdvancedLoot(steamID, appID, contextID))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private async Task<string> ResponseAdvancedRedeem(ulong steamID, string options, string keys) {
			if ((steamID == 0) || string.IsNullOrEmpty(options) || string.IsNullOrEmpty(keys)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(options) + " || " + nameof(keys));

				return null;
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Operator)) {
				return null;
			}

			string[] flags = options.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

			if (flags.Length == 0) {
				return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(flags)));
			}

			ERedeemFlags redeemFlags = ERedeemFlags.None;

			foreach (string flag in flags) {
				switch (flag.ToUpperInvariant()) {
					case "FD":
						redeemFlags |= ERedeemFlags.ForceDistributing;

						break;
					case "FF":
						redeemFlags |= ERedeemFlags.ForceForwarding;

						break;
					case "FKMG":
						redeemFlags |= ERedeemFlags.ForceKeepMissingGames;

						break;
					case "SD":
						redeemFlags |= ERedeemFlags.SkipDistributing;

						break;
					case "SF":
						redeemFlags |= ERedeemFlags.SkipForwarding;

						break;
					case "SI":
						redeemFlags |= ERedeemFlags.SkipInitial;

						break;
					case "SKMG":
						redeemFlags |= ERedeemFlags.SkipKeepMissingGames;

						break;
					case "V":
						redeemFlags |= ERedeemFlags.Validate;

						break;
					default:

						return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, flag));
				}
			}

			return await ResponseRedeem(steamID, keys, redeemFlags).ConfigureAwait(false);
		}

		[ItemCanBeNull]
		private static async Task<string> ResponseAdvancedRedeem(ulong steamID, string botNames, string options, string keys) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(options) || string.IsNullOrEmpty(keys)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(options) + " || " + nameof(keys));

				return null;
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseAdvancedRedeem(steamID, options, keys))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private async Task<string> ResponseAdvancedTransfer(ulong steamID, uint appID, ulong contextID, Bot targetBot) {
			if ((steamID == 0) || (appID == 0) || (contextID == 0) || (targetBot == null)) {
				Bot.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(appID) + " || " + nameof(contextID) + " || " + nameof(targetBot));

				return null;
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			if (!Bot.IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			if (!targetBot.IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.TargetBotNotConnected);
			}

			(bool success, string message) = await Bot.Actions.SendTradeOffer(appID, contextID, targetBot.SteamID).ConfigureAwait(false);

			return FormatBotResponse(success ? message : string.Format(Strings.WarningFailedWithError, message));
		}

		private async Task<string> ResponseAdvancedTransfer(ulong steamID, string targetAppID, string targetContextID, string botNameTo) {
			if ((steamID == 0) || string.IsNullOrEmpty(targetAppID) || string.IsNullOrEmpty(targetContextID) || string.IsNullOrEmpty(botNameTo)) {
				Bot.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(targetAppID) + " || " + nameof(targetContextID) + " || " + nameof(botNameTo));

				return null;
			}

			Bot targetBot = Bot.GetBot(botNameTo);

			if (targetBot == null) {
				return ASF.IsOwner(steamID) ? FormatBotResponse(string.Format(Strings.BotNotFound, botNameTo)) : null;
			}

			if (!uint.TryParse(targetAppID, out uint appID) || (appID == 0)) {
				return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(appID)));
			}

			if (!ulong.TryParse(targetContextID, out ulong contextID) || (contextID == 0)) {
				return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(contextID)));
			}

			return await ResponseAdvancedTransfer(steamID, appID, contextID, targetBot).ConfigureAwait(false);
		}

		private static async Task<string> ResponseAdvancedTransfer(ulong steamID, string botNames, string targetAppID, string targetContextID, string botNameTo) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(targetAppID) || string.IsNullOrEmpty(targetContextID) || string.IsNullOrEmpty(botNameTo)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(targetAppID) + " || " + nameof(targetContextID) + " || " + nameof(botNameTo));

				return null;
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			if (!uint.TryParse(targetAppID, out uint appID) || (appID == 0)) {
				return FormatStaticResponse(string.Format(Strings.ErrorIsInvalid, nameof(appID)));
			}

			if (!ulong.TryParse(targetContextID, out ulong contextID) || (contextID == 0)) {
				return FormatStaticResponse(string.Format(Strings.ErrorIsInvalid, nameof(contextID)));
			}

			Bot targetBot = Bot.GetBot(botNameTo);

			if (targetBot == null) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNameTo)) : null;
			}

			IList<string> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseAdvancedTransfer(steamID, appID, contextID, targetBot))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private string ResponseBackgroundGamesRedeemer(ulong steamID) {
			if (steamID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(steamID));

				return null;
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			uint count = Bot.GamesToRedeemInBackgroundCount;

			return FormatBotResponse(string.Format(Strings.BotGamesToRedeemInBackgroundCount, count));
		}

		[ItemCanBeNull]
		private static async Task<string> ResponseBackgroundGamesRedeemer(ulong steamID, string botNames) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames));

				return null;
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseBackgroundGamesRedeemer(steamID)))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private string ResponseBlacklist(ulong steamID) {
			if (steamID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(steamID));

				return null;
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			IReadOnlyCollection<ulong> blacklist = Bot.BotDatabase.GetBlacklistedFromTradesSteamIDs();

			return FormatBotResponse(blacklist.Count > 0 ? string.Join(", ", blacklist) : string.Format(Strings.ErrorIsEmpty, nameof(blacklist)));
		}

		[ItemCanBeNull]
		private static async Task<string> ResponseBlacklist(ulong steamID, string botNames) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames));

				return null;
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseBlacklist(steamID)))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private async Task<string> ResponseBlacklistAdd(ulong steamID, string targetSteamIDs) {
			if ((steamID == 0) || string.IsNullOrEmpty(targetSteamIDs)) {
				Bot.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(targetSteamIDs));

				return null;
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			string[] targets = targetSteamIDs.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

			if (targets.Length == 0) {
				return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(targets)));
			}

			HashSet<ulong> targetIDs = new HashSet<ulong>();

			foreach (string target in targets) {
				if (!ulong.TryParse(target, out ulong targetID) || (targetID == 0)) {
					return FormatBotResponse(string.Format(Strings.ErrorParsingObject, nameof(targetID)));
				}

				targetIDs.Add(targetID);
			}

			await Bot.BotDatabase.AddBlacklistedFromTradesSteamIDs(targetIDs).ConfigureAwait(false);

			return FormatBotResponse(Strings.Done);
		}

		[ItemCanBeNull]
		private static async Task<string> ResponseBlacklistAdd(ulong steamID, string botNames, string targetSteamIDs) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(targetSteamIDs)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(targetSteamIDs));

				return null;
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseBlacklistAdd(steamID, targetSteamIDs))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private async Task<string> ResponseBlacklistRemove(ulong steamID, string targetSteamIDs) {
			if ((steamID == 0) || string.IsNullOrEmpty(targetSteamIDs)) {
				Bot.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(targetSteamIDs));

				return null;
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			string[] targets = targetSteamIDs.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

			if (targets.Length == 0) {
				return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(targets)));
			}

			HashSet<ulong> targetIDs = new HashSet<ulong>();

			foreach (string target in targets) {
				if (!ulong.TryParse(target, out ulong targetID) || (targetID == 0)) {
					return FormatBotResponse(string.Format(Strings.ErrorParsingObject, nameof(targetID)));
				}

				targetIDs.Add(targetID);
			}

			await Bot.BotDatabase.RemoveBlacklistedFromTradesSteamIDs(targetIDs).ConfigureAwait(false);

			return FormatBotResponse(Strings.Done);
		}

		[ItemCanBeNull]
		private static async Task<string> ResponseBlacklistRemove(ulong steamID, string botNames, string targetSteamIDs) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(targetSteamIDs)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(targetSteamIDs));

				return null;
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseBlacklistRemove(steamID, targetSteamIDs))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private static string ResponseExit(ulong steamID) {
			if (steamID == 0) {
				ASF.ArchiLogger.LogNullError(nameof(steamID));

				return null;
			}

			if (!ASF.IsOwner(steamID)) {
				return null;
			}

			(bool success, string message) = Actions.Exit();

			return FormatStaticResponse(success ? message : string.Format(Strings.WarningFailedWithError, message));
		}

		private async Task<string> ResponseFarm(ulong steamID) {
			if (steamID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(steamID));

				return null;
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			if (!Bot.IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			if (Bot.CardsFarmer.NowFarming) {
				await Bot.CardsFarmer.StopFarming().ConfigureAwait(false);
			}

			Utilities.InBackground(Bot.CardsFarmer.StartFarming);

			return FormatBotResponse(Strings.Done);
		}

		[ItemCanBeNull]
		private static async Task<string> ResponseFarm(ulong steamID, string botNames) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames));

				return null;
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseFarm(steamID))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private string ResponseHelp(ulong steamID) {
			if (steamID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(steamID));

				return null;
			}

			return Bot.HasPermission(steamID, BotConfig.EPermission.FamilySharing) ? FormatBotResponse(SharedInfo.ProjectURL + "/wiki/Commands") : null;
		}

		private string ResponseIdleBlacklist(ulong steamID) {
			if (steamID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(steamID));

				return null;
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			IReadOnlyCollection<uint> idleBlacklist = Bot.BotDatabase.GetIdlingBlacklistedAppIDs();

			return FormatBotResponse(idleBlacklist.Count > 0 ? string.Join(", ", idleBlacklist) : string.Format(Strings.ErrorIsEmpty, nameof(idleBlacklist)));
		}

		[ItemCanBeNull]
		private static async Task<string> ResponseIdleBlacklist(ulong steamID, string botNames) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames));

				return null;
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseIdleBlacklist(steamID)))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private async Task<string> ResponseIdleBlacklistAdd(ulong steamID, string targetAppIDs) {
			if ((steamID == 0) || string.IsNullOrEmpty(targetAppIDs)) {
				Bot.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(targetAppIDs));

				return null;
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			string[] targets = targetAppIDs.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

			if (targets.Length == 0) {
				return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(targets)));
			}

			HashSet<uint> appIDs = new HashSet<uint>();

			foreach (string target in targets) {
				if (!uint.TryParse(target, out uint appID) || (appID == 0)) {
					return FormatBotResponse(string.Format(Strings.ErrorParsingObject, nameof(appID)));
				}

				appIDs.Add(appID);
			}

			await Bot.BotDatabase.AddIdlingBlacklistedAppIDs(appIDs).ConfigureAwait(false);

			return FormatBotResponse(Strings.Done);
		}

		[ItemCanBeNull]
		private static async Task<string> ResponseIdleBlacklistAdd(ulong steamID, string botNames, string targetAppIDs) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(targetAppIDs)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(targetAppIDs));

				return null;
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseIdleBlacklistAdd(steamID, targetAppIDs))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private async Task<string> ResponseIdleBlacklistRemove(ulong steamID, string targetAppIDs) {
			if ((steamID == 0) || string.IsNullOrEmpty(targetAppIDs)) {
				Bot.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(targetAppIDs));

				return null;
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			string[] targets = targetAppIDs.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

			if (targets.Length == 0) {
				return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(targets)));
			}

			HashSet<uint> appIDs = new HashSet<uint>();

			foreach (string target in targets) {
				if (!uint.TryParse(target, out uint appID) || (appID == 0)) {
					return FormatBotResponse(string.Format(Strings.ErrorParsingObject, nameof(appID)));
				}

				appIDs.Add(appID);
			}

			await Bot.BotDatabase.RemoveIdlingBlacklistedAppIDs(appIDs).ConfigureAwait(false);

			return FormatBotResponse(Strings.Done);
		}

		[ItemCanBeNull]
		private static async Task<string> ResponseIdleBlacklistRemove(ulong steamID, string botNames, string targetAppIDs) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(targetAppIDs)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(targetAppIDs));

				return null;
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseIdleBlacklistRemove(steamID, targetAppIDs))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private string ResponseIdleQueue(ulong steamID) {
			if (steamID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(steamID));

				return null;
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			IReadOnlyCollection<uint> idleQueue = Bot.BotDatabase.GetIdlingPriorityAppIDs();

			return FormatBotResponse(idleQueue.Count > 0 ? string.Join(", ", idleQueue) : string.Format(Strings.ErrorIsEmpty, nameof(idleQueue)));
		}

		[ItemCanBeNull]
		private static async Task<string> ResponseIdleQueue(ulong steamID, string botNames) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames));

				return null;
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseIdleQueue(steamID)))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private async Task<string> ResponseIdleQueueAdd(ulong steamID, string targetAppIDs) {
			if ((steamID == 0) || string.IsNullOrEmpty(targetAppIDs)) {
				Bot.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(targetAppIDs));

				return null;
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			string[] targets = targetAppIDs.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

			if (targets.Length == 0) {
				return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(targets)));
			}

			HashSet<uint> appIDs = new HashSet<uint>();

			foreach (string target in targets) {
				if (!uint.TryParse(target, out uint appID) || (appID == 0)) {
					return FormatBotResponse(string.Format(Strings.ErrorParsingObject, nameof(appID)));
				}

				appIDs.Add(appID);
			}

			await Bot.BotDatabase.AddIdlingPriorityAppIDs(appIDs).ConfigureAwait(false);

			return FormatBotResponse(Strings.Done);
		}

		[ItemCanBeNull]
		private static async Task<string> ResponseIdleQueueAdd(ulong steamID, string botNames, string targetAppIDs) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(targetAppIDs)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(targetAppIDs));

				return null;
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseIdleQueueAdd(steamID, targetAppIDs))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private async Task<string> ResponseIdleQueueRemove(ulong steamID, string targetAppIDs) {
			if ((steamID == 0) || string.IsNullOrEmpty(targetAppIDs)) {
				Bot.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(targetAppIDs));

				return null;
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			string[] targets = targetAppIDs.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

			if (targets.Length == 0) {
				return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(targets)));
			}

			HashSet<uint> appIDs = new HashSet<uint>();

			foreach (string target in targets) {
				if (!uint.TryParse(target, out uint appID) || (appID == 0)) {
					return FormatBotResponse(string.Format(Strings.ErrorParsingObject, nameof(appID)));
				}

				appIDs.Add(appID);
			}

			await Bot.BotDatabase.RemoveIdlingPriorityAppIDs(appIDs).ConfigureAwait(false);

			return FormatBotResponse(Strings.Done);
		}

		[ItemCanBeNull]
		private static async Task<string> ResponseIdleQueueRemove(ulong steamID, string botNames, string targetAppIDs) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(targetAppIDs)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(targetAppIDs));

				return null;
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseIdleQueueRemove(steamID, targetAppIDs))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private string ResponseInput(ulong steamID, string propertyName, string inputValue) {
			if ((steamID == 0) || string.IsNullOrEmpty(propertyName) || string.IsNullOrEmpty(inputValue)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(propertyName) + " || " + nameof(inputValue));

				return null;
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			if (!ASF.GlobalConfig.Headless) {
				return FormatBotResponse(Strings.ErrorFunctionOnlyInHeadlessMode);
			}

			if (!Enum.TryParse(propertyName, true, out ASF.EUserInputType inputType) || (inputType == ASF.EUserInputType.Unknown) || !Enum.IsDefined(typeof(ASF.EUserInputType), inputType)) {
				return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(inputType)));
			}

			Bot.SetUserInput(inputType, inputValue);

			return FormatBotResponse(Strings.Done);
		}

		[ItemCanBeNull]
		private static async Task<string> ResponseInput(ulong steamID, string botNames, string propertyName, string inputValue) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(propertyName) || string.IsNullOrEmpty(inputValue)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(propertyName) + " || " + nameof(inputValue));

				return null;
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseInput(steamID, propertyName, inputValue)))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private async Task<string> ResponseLevel(ulong steamID) {
			if (steamID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(steamID));

				return null;
			}

			if (!Bot.IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			uint? level = await Bot.ArchiHandler.GetLevel().ConfigureAwait(false);

			return FormatBotResponse(level.HasValue ? string.Format(Strings.BotLevel, level.Value) : Strings.WarningFailed);
		}

		[ItemCanBeNull]
		private static async Task<string> ResponseLevel(ulong steamID, string botNames) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames));

				return null;
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseLevel(steamID))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private async Task<string> ResponseLoot(ulong steamID) {
			if (steamID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(steamID));

				return null;
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			if (!Bot.IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			if (Bot.BotConfig.LootableTypes.Count == 0) {
				return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(Bot.BotConfig.LootableTypes)));
			}

			(bool success, string message) = await Bot.Actions.SendTradeOffer(wantedTypes: Bot.BotConfig.LootableTypes).ConfigureAwait(false);

			return FormatBotResponse(success ? message : string.Format(Strings.WarningFailedWithError, message));
		}

		[ItemCanBeNull]
		private static async Task<string> ResponseLoot(ulong steamID, string botNames) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames));

				return null;
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseLoot(steamID))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private async Task<string> ResponseLootByRealAppIDs(ulong steamID, string realAppIDsText) {
			if ((steamID == 0) || string.IsNullOrEmpty(realAppIDsText)) {
				Bot.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(realAppIDsText));

				return null;
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			if (!Bot.IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			if (Bot.BotConfig.LootableTypes.Count == 0) {
				return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(Bot.BotConfig.LootableTypes)));
			}

			string[] appIDTexts = realAppIDsText.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

			if (appIDTexts.Length == 0) {
				return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(appIDTexts)));
			}

			HashSet<uint> realAppIDs = new HashSet<uint>();

			foreach (string appIDText in appIDTexts) {
				if (!uint.TryParse(appIDText, out uint appID) || (appID == 0)) {
					return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(appID)));
				}

				realAppIDs.Add(appID);
			}

			(bool success, string message) = await Bot.Actions.SendTradeOffer(wantedTypes: Bot.BotConfig.LootableTypes, wantedRealAppIDs: realAppIDs).ConfigureAwait(false);

			return FormatBotResponse(success ? message : string.Format(Strings.WarningFailedWithError, message));
		}

		[ItemCanBeNull]
		private static async Task<string> ResponseLootByRealAppIDs(ulong steamID, string botNames, string realAppIDsText) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(realAppIDsText)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(realAppIDsText));

				return null;
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseLootByRealAppIDs(steamID, realAppIDsText))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private string ResponseNickname(ulong steamID, string nickname) {
			if ((steamID == 0) || string.IsNullOrEmpty(nickname)) {
				Bot.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(nickname));

				return null;
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			if (!Bot.IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			Bot.SteamFriends.SetPersonaName(nickname);

			return FormatBotResponse(Strings.Done);
		}

		[ItemCanBeNull]
		private static async Task<string> ResponseNickname(ulong steamID, string botNames, string nickname) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(nickname)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(nickname));

				return null;
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseNickname(steamID, nickname)))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private async Task<(string Response, HashSet<uint> OwnedGameIDs)> ResponseOwns(ulong steamID, string query) {
			if ((steamID == 0) || string.IsNullOrEmpty(query)) {
				Bot.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(query));

				return (null, null);
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Operator)) {
				return (null, null);
			}

			if (!Bot.IsConnectedAndLoggedOn) {
				return (FormatBotResponse(Strings.BotNotConnected), null);
			}

			Dictionary<uint, string> ownedGames = null;

			lock (CachedGamesOwned) {
				if (CachedGamesOwned.Count > 0) {
					ownedGames = new Dictionary<uint, string>(CachedGamesOwned);
				}
			}

			if (ownedGames == null) {
				ownedGames = await Bot.ArchiWebHandler.HasValidApiKey().ConfigureAwait(false) ? await Bot.ArchiWebHandler.GetOwnedGames(Bot.SteamID).ConfigureAwait(false) : await Bot.ArchiWebHandler.GetMyOwnedGames().ConfigureAwait(false);

				if ((ownedGames == null) || (ownedGames.Count == 0)) {
					return (FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(ownedGames))), null);
				}

				lock (CachedGamesOwned) {
					if (CachedGamesOwned.Count == 0) {
						foreach ((uint appID, string gameName) in ownedGames) {
							CachedGamesOwned[appID] = gameName;
						}

						CachedGamesOwned.TrimExcess();
					}
				}
			}

			StringBuilder response = new StringBuilder();
			HashSet<uint> ownedGameIDs = new HashSet<uint>();

			if (query.Equals("*")) {
				foreach ((uint appID, string gameName) in ownedGames) {
					ownedGameIDs.Add(appID);
					response.AppendLine(FormatBotResponse(string.Format(Strings.BotOwnedAlreadyWithName, appID, gameName)));
				}
			} else {
				string[] games = query.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

				if (games.Length == 0) {
					return (FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(games))), null);
				}

				foreach (string game in games) {
					// Check if this is gameID
					if (uint.TryParse(game, out uint gameID) && (gameID != 0)) {
						if (Bot.OwnedPackageIDs.ContainsKey(gameID)) {
							ownedGameIDs.Add(gameID);
							response.AppendLine(FormatBotResponse(string.Format(Strings.BotOwnedAlready, gameID)));

							continue;
						}

						if (ownedGames.TryGetValue(gameID, out string ownedName)) {
							ownedGameIDs.Add(gameID);
							response.AppendLine(FormatBotResponse(string.Format(Strings.BotOwnedAlreadyWithName, gameID, ownedName)));
						} else {
							response.AppendLine(FormatBotResponse(string.Format(Strings.BotNotOwnedYet, gameID)));
						}

						continue;
					}

					// This is a string, so check our entire library
					foreach ((uint appID, string gameName) in ownedGames.Where(ownedGame => ownedGame.Value.IndexOf(game, StringComparison.OrdinalIgnoreCase) >= 0)) {
						ownedGameIDs.Add(appID);
						response.AppendLine(FormatBotResponse(string.Format(Strings.BotOwnedAlreadyWithName, appID, gameName)));
					}
				}
			}

			return (response.Length > 0 ? response.ToString() : FormatBotResponse(string.Format(Strings.BotNotOwnedYet, query)), ownedGameIDs);
		}

		[ItemCanBeNull]
		private static async Task<string> ResponseOwns(ulong steamID, string botNames, string query) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(query)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(query));

				return null;
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<(string Response, HashSet<uint> OwnedGameIDs)> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseOwns(steamID, query))).ConfigureAwait(false);

			List<(string Response, HashSet<uint> OwnedGameIDs)> validResults = new List<(string Response, HashSet<uint> OwnedGameIDs)>(results.Where(result => !string.IsNullOrEmpty(result.Response)));

			if (validResults.Count == 0) {
				return null;
			}

			Dictionary<uint, ushort> ownedGameCounts = new Dictionary<uint, ushort>();

			foreach (uint gameID in validResults.Where(validResult => (validResult.OwnedGameIDs != null) && (validResult.OwnedGameIDs.Count > 0)).SelectMany(validResult => validResult.OwnedGameIDs)) {
				ownedGameCounts[gameID] = ownedGameCounts.TryGetValue(gameID, out ushort count) ? ++count : (ushort) 1;
			}

			IEnumerable<string> extraResponses = ownedGameCounts.Select(kv => FormatStaticResponse(string.Format(Strings.BotOwnsOverviewPerGame, kv.Value, validResults.Count, kv.Key)));

			return string.Join(Environment.NewLine, validResults.Select(result => result.Response).Concat(extraResponses));
		}

		private string ResponsePassword(ulong steamID) {
			if (steamID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(steamID));

				return null;
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			if (string.IsNullOrEmpty(Bot.BotConfig.DecryptedSteamPassword)) {
				return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(BotConfig.DecryptedSteamPassword)));
			}

			Dictionary<ArchiCryptoHelper.ECryptoMethod, string> encryptedPasswords = new Dictionary<ArchiCryptoHelper.ECryptoMethod, string>(2) {
				{ ArchiCryptoHelper.ECryptoMethod.AES, ArchiCryptoHelper.Encrypt(ArchiCryptoHelper.ECryptoMethod.AES, Bot.BotConfig.DecryptedSteamPassword) },
				{ ArchiCryptoHelper.ECryptoMethod.ProtectedDataForCurrentUser, ArchiCryptoHelper.Encrypt(ArchiCryptoHelper.ECryptoMethod.ProtectedDataForCurrentUser, Bot.BotConfig.DecryptedSteamPassword) }
			};

			return FormatBotResponse(string.Join(", ", encryptedPasswords.Where(kv => !string.IsNullOrEmpty(kv.Value)).Select(kv => string.Format(Strings.BotEncryptedPassword, kv.Key, kv.Value))));
		}

		[ItemCanBeNull]
		private static async Task<string> ResponsePassword(ulong steamID, string botNames) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames));

				return null;
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponsePassword(steamID)))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private async Task<string> ResponsePause(ulong steamID, bool permanent, string resumeInSecondsText = null) {
			if (steamID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(steamID));

				return null;
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.FamilySharing)) {
				return null;
			}

			if (permanent && !Bot.HasPermission(steamID, BotConfig.EPermission.Operator)) {
				return FormatBotResponse(Strings.ErrorAccessDenied);
			}

			ushort resumeInSeconds = 0;

			if (!string.IsNullOrEmpty(resumeInSecondsText) && (!ushort.TryParse(resumeInSecondsText, out resumeInSeconds) || (resumeInSeconds == 0))) {
				return string.Format(Strings.ErrorIsInvalid, nameof(resumeInSecondsText));
			}

			(bool success, string message) = await Bot.Actions.Pause(permanent, resumeInSeconds).ConfigureAwait(false);

			return FormatBotResponse(success ? message : string.Format(Strings.WarningFailedWithError, message));
		}

		[ItemCanBeNull]
		private static async Task<string> ResponsePause(ulong steamID, string botNames, bool permanent, string resumeInSecondsText = null) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames));

				return null;
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponsePause(steamID, permanent, resumeInSecondsText))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private async Task<string> ResponsePlay(ulong steamID, IEnumerable<uint> gameIDs, string gameName = null) {
			if ((steamID == 0) || (gameIDs == null)) {
				Bot.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(gameIDs));

				return null;
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			if (!Bot.IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			if (!Bot.CardsFarmer.Paused) {
				await Bot.CardsFarmer.Pause(false).ConfigureAwait(false);
			}

			await Bot.ArchiHandler.PlayGames(gameIDs, gameName).ConfigureAwait(false);

			return FormatBotResponse(Strings.Done);
		}

		private async Task<string> ResponsePlay(ulong steamID, string targetGameIDs) {
			if ((steamID == 0) || string.IsNullOrEmpty(targetGameIDs)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(targetGameIDs));

				return null;
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			if (!Bot.IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			string[] games = targetGameIDs.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

			if (games.Length == 0) {
				return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(games)));
			}

			HashSet<uint> gamesToPlay = new HashSet<uint>();
			StringBuilder gameName = new StringBuilder();

			foreach (string game in games) {
				if (!uint.TryParse(game, out uint gameID) || (gameID == 0)) {
					gameName.Append((gameName.Length > 0 ? " " : "") + game);

					continue;
				}

				if (gamesToPlay.Count >= ArchiHandler.MaxGamesPlayedConcurrently) {
					continue;
				}

				gamesToPlay.Add(gameID);
			}

			return await ResponsePlay(steamID, gamesToPlay, gameName.ToString()).ConfigureAwait(false);
		}

		[ItemCanBeNull]
		private static async Task<string> ResponsePlay(ulong steamID, string botNames, string targetGameIDs) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(targetGameIDs)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(targetGameIDs));

				return null;
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponsePlay(steamID, targetGameIDs))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private async Task<string> ResponsePrivacy(ulong steamID, string privacySettingsText) {
			if ((steamID == 0) || string.IsNullOrEmpty(privacySettingsText)) {
				Bot.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(privacySettingsText));

				return null;
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			if (!Bot.IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			string[] privacySettingsArgs = privacySettingsText.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

			if (privacySettingsArgs.Length == 0) {
				return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(privacySettingsArgs)));
			}

			// There are only 7 privacy settings
			if (privacySettingsArgs.Length > 7) {
				return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(privacySettingsArgs)));
			}

			Steam.UserPrivacy.PrivacySettings.EPrivacySetting profile = Steam.UserPrivacy.PrivacySettings.EPrivacySetting.Private;
			Steam.UserPrivacy.PrivacySettings.EPrivacySetting ownedGames = Steam.UserPrivacy.PrivacySettings.EPrivacySetting.Private;
			Steam.UserPrivacy.PrivacySettings.EPrivacySetting playtime = Steam.UserPrivacy.PrivacySettings.EPrivacySetting.Private;
			Steam.UserPrivacy.PrivacySettings.EPrivacySetting friendsList = Steam.UserPrivacy.PrivacySettings.EPrivacySetting.Private;
			Steam.UserPrivacy.PrivacySettings.EPrivacySetting inventory = Steam.UserPrivacy.PrivacySettings.EPrivacySetting.Private;
			Steam.UserPrivacy.PrivacySettings.EPrivacySetting inventoryGifts = Steam.UserPrivacy.PrivacySettings.EPrivacySetting.Private;
			Steam.UserPrivacy.ECommentPermission comments = Steam.UserPrivacy.ECommentPermission.Private;

			// Converting digits to enum
			for (byte index = 0; index < privacySettingsArgs.Length; index++) {
				if (!Enum.TryParse(privacySettingsArgs[index], true, out Steam.UserPrivacy.PrivacySettings.EPrivacySetting privacySetting) || (privacySetting == Steam.UserPrivacy.PrivacySettings.EPrivacySetting.Unknown) || !Enum.IsDefined(typeof(Steam.UserPrivacy.PrivacySettings.EPrivacySetting), privacySetting)) {
					return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(privacySettingsArgs)));
				}

				// Child setting can't be less restrictive than its parent
				switch (index) {
					case 0: // Profile
						profile = privacySetting;

						break;
					case 1: // OwnedGames, child of Profile

						if (profile < privacySetting) {
							return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(ownedGames)));
						}

						ownedGames = privacySetting;

						break;
					case 2: // Playtime, child of OwnedGames

						if (ownedGames < privacySetting) {
							return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(playtime)));
						}

						playtime = privacySetting;

						break;
					case 3: // FriendsList, child of Profile

						if (profile < privacySetting) {
							return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(ownedGames)));
						}

						friendsList = privacySetting;

						break;
					case 4: // Inventory, child of Profile

						if (profile < privacySetting) {
							return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(inventory)));
						}

						inventory = privacySetting;

						break;
					case 5: // InventoryGifts, child of Inventory

						if (inventory < privacySetting) {
							return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(inventoryGifts)));
						}

						inventoryGifts = privacySetting;

						break;
					case 6: // Comments, child of Profile

						if (profile < privacySetting) {
							return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(comments)));
						}

						// Comments use different numbers than everything else, but we want to have this command consistent for end-user, so we'll map them
						switch (privacySetting) {
							case Steam.UserPrivacy.PrivacySettings.EPrivacySetting.FriendsOnly:
								comments = Steam.UserPrivacy.ECommentPermission.FriendsOnly;

								break;
							case Steam.UserPrivacy.PrivacySettings.EPrivacySetting.Private:
								comments = Steam.UserPrivacy.ECommentPermission.Private;

								break;
							case Steam.UserPrivacy.PrivacySettings.EPrivacySetting.Public:
								comments = Steam.UserPrivacy.ECommentPermission.Public;

								break;
							default:
								Bot.ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(privacySetting), privacySetting));

								return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(privacySetting)));
						}

						break;
					default:
						Bot.ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(index), index));

						return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(index)));
				}
			}

			Steam.UserPrivacy userPrivacy = new Steam.UserPrivacy(new Steam.UserPrivacy.PrivacySettings(profile, ownedGames, playtime, friendsList, inventory, inventoryGifts), comments);

			return FormatBotResponse(await Bot.ArchiWebHandler.ChangePrivacySettings(userPrivacy).ConfigureAwait(false) ? Strings.Success : Strings.WarningFailed);
		}

		[ItemCanBeNull]
		private static async Task<string> ResponsePrivacy(ulong steamID, string botNames, string privacySettingsText) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(privacySettingsText)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(privacySettingsText));

				return null;
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponsePrivacy(steamID, privacySettingsText))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		[SuppressMessage("ReSharper", "FunctionComplexityOverflow")]
		private async Task<string> ResponseRedeem(ulong steamID, string keysText, ERedeemFlags redeemFlags = ERedeemFlags.None) {
			if ((steamID == 0) || string.IsNullOrEmpty(keysText)) {
				Bot.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(keysText));

				return null;
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Operator)) {
				return null;
			}

			if (!Bot.IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			string[] keys = keysText.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

			if (keys.Length == 0) {
				return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(keys)));
			}

			bool forward = !redeemFlags.HasFlag(ERedeemFlags.SkipForwarding) && (redeemFlags.HasFlag(ERedeemFlags.ForceForwarding) || Bot.BotConfig.RedeemingPreferences.HasFlag(BotConfig.ERedeemingPreferences.Forwarding));
			bool distribute = !redeemFlags.HasFlag(ERedeemFlags.SkipDistributing) && (redeemFlags.HasFlag(ERedeemFlags.ForceDistributing) || Bot.BotConfig.RedeemingPreferences.HasFlag(BotConfig.ERedeemingPreferences.Distributing));
			bool keepMissingGames = !redeemFlags.HasFlag(ERedeemFlags.SkipKeepMissingGames) && (redeemFlags.HasFlag(ERedeemFlags.ForceKeepMissingGames) || Bot.BotConfig.RedeemingPreferences.HasFlag(BotConfig.ERedeemingPreferences.KeepMissingGames));

			HashSet<string> pendingKeys = keys.ToHashSet();
			HashSet<string> unusedKeys = pendingKeys.ToHashSet();

			HashSet<Bot> rateLimitedBots = new HashSet<Bot>();

			StringBuilder response = new StringBuilder();

			using (HashSet<string>.Enumerator keysEnumerator = pendingKeys.GetEnumerator()) {
				string key = keysEnumerator.MoveNext() ? keysEnumerator.Current : null; // Initial key

				while (!string.IsNullOrEmpty(key)) {
					string startingKey = key;

					using (IEnumerator<Bot> botsEnumerator = Bot.Bots.Where(bot => (bot.Value != Bot) && !rateLimitedBots.Contains(bot.Value) && bot.Value.IsConnectedAndLoggedOn && bot.Value.Commands.Bot.HasPermission(steamID, BotConfig.EPermission.Operator)).OrderByDescending(bot => Bot.BotsComparer.Compare(bot.Key, Bot.BotName)).ThenBy(bot => bot.Key, Bot.BotsComparer).Select(bot => bot.Value).GetEnumerator()) {
						Bot currentBot = Bot;

						while (!string.IsNullOrEmpty(key) && (currentBot != null)) {
							if (redeemFlags.HasFlag(ERedeemFlags.Validate) && !Utilities.IsValidCdKey(key)) {
								key = keysEnumerator.MoveNext() ? keysEnumerator.Current : null; // Next key

								continue; // Keep current bot
							}

							if ((currentBot == Bot) && (redeemFlags.HasFlag(ERedeemFlags.SkipInitial) || rateLimitedBots.Contains(currentBot))) {
								currentBot = null; // Either bot will be changed, or loop aborted
							} else {
								ArchiHandler.PurchaseResponseCallback result = await currentBot.Actions.RedeemKey(key).ConfigureAwait(false);

								if (result == null) {
									response.AppendLine(FormatBotResponse(string.Format(Strings.BotRedeem, key, EPurchaseResultDetail.Timeout), currentBot.BotName));
									currentBot = null; // Either bot will be changed, or loop aborted
								} else {
									if ((result.PurchaseResultDetail == EPurchaseResultDetail.CannotRedeemCodeFromClient) && (Bot.WalletCurrency != ECurrencyCode.Invalid)) {
										// If it's a wallet code, we try to redeem it first, then handle the inner result as our primary one
										(EResult Result, EPurchaseResultDetail? PurchaseResult)? walletResult = await currentBot.ArchiWebHandler.RedeemWalletKey(key).ConfigureAwait(false);

										if (walletResult != null) {
											result.Result = walletResult.Value.Result;
											result.PurchaseResultDetail = walletResult.Value.PurchaseResult.GetValueOrDefault(walletResult.Value.Result == EResult.OK ? EPurchaseResultDetail.NoDetail : EPurchaseResultDetail.BadActivationCode); // BadActivationCode is our smart guess in this case
										} else {
											result.Result = EResult.Timeout;
											result.PurchaseResultDetail = EPurchaseResultDetail.Timeout;
										}
									}

									switch (result.PurchaseResultDetail) {
										case EPurchaseResultDetail.BadActivationCode:
										case EPurchaseResultDetail.CannotRedeemCodeFromClient:
										case EPurchaseResultDetail.DuplicateActivationCode:
										case EPurchaseResultDetail.NoDetail: // OK
										case EPurchaseResultDetail.Timeout:

											if ((result.Items != null) && (result.Items.Count > 0)) {
												response.AppendLine(FormatBotResponse(string.Format(Strings.BotRedeemWithItems, key, result.Result + "/" + result.PurchaseResultDetail, string.Join(", ", result.Items)), currentBot.BotName));
											} else {
												response.AppendLine(FormatBotResponse(string.Format(Strings.BotRedeem, key, result.Result + "/" + result.PurchaseResultDetail), currentBot.BotName));
											}

											if ((result.Result != EResult.Timeout) && (result.PurchaseResultDetail != EPurchaseResultDetail.Timeout)) {
												unusedKeys.Remove(key);
											}

											key = keysEnumerator.MoveNext() ? keysEnumerator.Current : null; // Next key

											if (result.PurchaseResultDetail == EPurchaseResultDetail.NoDetail) {
												break; // Next bot (if needed)
											}

											continue; // Keep current bot
										case EPurchaseResultDetail.AccountLocked:
										case EPurchaseResultDetail.AlreadyPurchased:
										case EPurchaseResultDetail.DoesNotOwnRequiredApp:
										case EPurchaseResultDetail.RestrictedCountry:

											if ((result.Items != null) && (result.Items.Count > 0)) {
												response.AppendLine(FormatBotResponse(string.Format(Strings.BotRedeemWithItems, key, result.Result + "/" + result.PurchaseResultDetail, string.Join(", ", result.Items)), currentBot.BotName));
											} else {
												response.AppendLine(FormatBotResponse(string.Format(Strings.BotRedeem, key, result.Result + "/" + result.PurchaseResultDetail), currentBot.BotName));
											}

											if (!forward || (keepMissingGames && (result.PurchaseResultDetail != EPurchaseResultDetail.AlreadyPurchased))) {
												key = keysEnumerator.MoveNext() ? keysEnumerator.Current : null; // Next key

												break; // Next bot (if needed)
											}

											if (distribute) {
												break; // Next bot, without changing key
											}

											Dictionary<uint, string> items = result.Items ?? new Dictionary<uint, string>();

											bool alreadyHandled = false;

											foreach (Bot innerBot in Bot.Bots.Where(bot => (bot.Value != currentBot) && (!redeemFlags.HasFlag(ERedeemFlags.SkipInitial) || (bot.Value != Bot)) && !rateLimitedBots.Contains(bot.Value) && bot.Value.IsConnectedAndLoggedOn && bot.Value.Commands.Bot.HasPermission(steamID, BotConfig.EPermission.Operator) && ((items.Count == 0) || items.Keys.Any(packageID => !bot.Value.OwnedPackageIDs.ContainsKey(packageID)))).OrderBy(bot => bot.Key, Bot.BotsComparer).Select(bot => bot.Value)) {
												ArchiHandler.PurchaseResponseCallback otherResult = await innerBot.Actions.RedeemKey(key).ConfigureAwait(false);

												if (otherResult == null) {
													response.AppendLine(FormatBotResponse(string.Format(Strings.BotRedeem, key, EResult.Timeout + "/" + EPurchaseResultDetail.Timeout), innerBot.BotName));

													continue;
												}

												switch (otherResult.PurchaseResultDetail) {
													case EPurchaseResultDetail.BadActivationCode:
													case EPurchaseResultDetail.DuplicateActivationCode:
													case EPurchaseResultDetail.NoDetail: // OK
														alreadyHandled = true; // This key is already handled, as we either redeemed it or we're sure it's dupe/invalid
														unusedKeys.Remove(key);

														break;
													case EPurchaseResultDetail.RateLimited:
														rateLimitedBots.Add(innerBot);

														break;
												}

												if ((otherResult.Items != null) && (otherResult.Items.Count > 0)) {
													response.AppendLine(FormatBotResponse(string.Format(Strings.BotRedeemWithItems, key, otherResult.Result + "/" + otherResult.PurchaseResultDetail, string.Join(", ", otherResult.Items)), innerBot.BotName));
												} else {
													response.AppendLine(FormatBotResponse(string.Format(Strings.BotRedeem, key, otherResult.Result + "/" + otherResult.PurchaseResultDetail), innerBot.BotName));
												}

												if (alreadyHandled) {
													break;
												}

												if (otherResult.Items == null) {
													continue;
												}

												foreach ((uint packageID, string packageName) in otherResult.Items.Where(item => !items.ContainsKey(item.Key))) {
													items[packageID] = packageName;
												}
											}

											key = keysEnumerator.MoveNext() ? keysEnumerator.Current : null; // Next key

											break; // Next bot (if needed)
										case EPurchaseResultDetail.RateLimited:
											rateLimitedBots.Add(currentBot);
											goto case EPurchaseResultDetail.AccountLocked;
										default:
											ASF.ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(result.PurchaseResultDetail), result.PurchaseResultDetail));

											if ((result.Items != null) && (result.Items.Count > 0)) {
												response.AppendLine(FormatBotResponse(string.Format(Strings.BotRedeemWithItems, key, result.Result + "/" + result.PurchaseResultDetail, string.Join(", ", result.Items)), currentBot.BotName));
											} else {
												response.AppendLine(FormatBotResponse(string.Format(Strings.BotRedeem, key, result.Result + "/" + result.PurchaseResultDetail), currentBot.BotName));
											}

											unusedKeys.Remove(key);

											key = keysEnumerator.MoveNext() ? keysEnumerator.Current : null; // Next key

											break; // Next bot (if needed)
									}
								}
							}

							// We want to change bot in two cases:
							// a) When we have distribution enabled, obviously
							// b) When we're skipping initial bot AND we have forwarding enabled, otherwise we won't get down to other accounts
							if (distribute || (forward && redeemFlags.HasFlag(ERedeemFlags.SkipInitial))) {
								currentBot = botsEnumerator.MoveNext() ? botsEnumerator.Current : null;
							}
						}
					}

					if (key == startingKey) {
						// We ran out of bots to try for this key, so change it to avoid infinite loop
						key = keysEnumerator.MoveNext() ? keysEnumerator.Current : null; // Next key
					}
				}
			}

			if (unusedKeys.Count > 0) {
				response.AppendLine(FormatBotResponse(string.Format(Strings.UnusedKeys, string.Join(", ", unusedKeys))));
			}

			return response.Length > 0 ? response.ToString() : null;
		}

		[ItemCanBeNull]
		private static async Task<string> ResponseRedeem(ulong steamID, string botNames, string keys, ERedeemFlags redeemFlags = ERedeemFlags.None) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(keys)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(keys));

				return null;
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseRedeem(steamID, keys, redeemFlags))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private static string ResponseRestart(ulong steamID) {
			if (steamID == 0) {
				ASF.ArchiLogger.LogNullError(nameof(steamID));

				return null;
			}

			if (!ASF.IsOwner(steamID)) {
				return null;
			}

			(bool success, string message) = Actions.Restart();

			return FormatStaticResponse(success ? message : string.Format(Strings.WarningFailedWithError, message));
		}

		private string ResponseResume(ulong steamID) {
			if (steamID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(steamID));

				return null;
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.FamilySharing)) {
				return null;
			}

			(bool success, string message) = Bot.Actions.Resume();

			return FormatBotResponse(success ? message : string.Format(Strings.WarningFailedWithError, message));
		}

		[ItemCanBeNull]
		private static async Task<string> ResponseResume(ulong steamID, string botNames) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames));

				return null;
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseResume(steamID)))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private string ResponseStart(ulong steamID) {
			if (steamID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(steamID));

				return null;
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			(bool success, string message) = Bot.Actions.Start();

			return FormatBotResponse(success ? message : string.Format(Strings.WarningFailedWithError, message));
		}

		[ItemCanBeNull]
		private static async Task<string> ResponseStart(ulong steamID, string botNames) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames));

				return null;
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseStart(steamID)))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private string ResponseStats(ulong steamID) {
			if (steamID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(steamID));

				return null;
			}

			if (!ASF.IsOwner(steamID)) {
				return null;
			}

			ushort memoryInMegabytes = (ushort) (GC.GetTotalMemory(false) / 1024 / 1024);

			return FormatBotResponse(string.Format(Strings.BotStats, memoryInMegabytes));
		}

		private (string Response, Bot Bot) ResponseStatus(ulong steamID) {
			if (steamID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(steamID));

				return (null, Bot);
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.FamilySharing)) {
				return (null, Bot);
			}

			if (!Bot.IsConnectedAndLoggedOn) {
				return (FormatBotResponse(Bot.KeepRunning ? Strings.BotStatusConnecting : Strings.BotStatusNotRunning), Bot);
			}

			if (Bot.PlayingBlocked) {
				return (FormatBotResponse(Strings.BotStatusPlayingNotAvailable), Bot);
			}

			if (Bot.CardsFarmer.Paused) {
				return (FormatBotResponse(Strings.BotStatusPaused), Bot);
			}

			if (Bot.IsAccountLimited) {
				return (FormatBotResponse(Strings.BotStatusLimited), Bot);
			}

			if (Bot.IsAccountLocked) {
				return (FormatBotResponse(Strings.BotStatusLocked), Bot);
			}

			if (!Bot.CardsFarmer.NowFarming || (Bot.CardsFarmer.CurrentGamesFarming.Count == 0)) {
				return (FormatBotResponse(Strings.BotStatusNotIdling), Bot);
			}

			if (Bot.CardsFarmer.CurrentGamesFarming.Count > 1) {
				return (FormatBotResponse(string.Format(Strings.BotStatusIdlingList, string.Join(", ", Bot.CardsFarmer.CurrentGamesFarming.Select(game => game.AppID + " (" + game.GameName + ")")), Bot.CardsFarmer.GamesToFarm.Count, Bot.CardsFarmer.GamesToFarm.Sum(game => game.CardsRemaining), Bot.CardsFarmer.TimeRemaining.ToHumanReadable())), Bot);
			}

			CardsFarmer.Game soloGame = Bot.CardsFarmer.CurrentGamesFarming.First();

			return (FormatBotResponse(string.Format(Strings.BotStatusIdling, soloGame.AppID, soloGame.GameName, soloGame.CardsRemaining, Bot.CardsFarmer.GamesToFarm.Count, Bot.CardsFarmer.GamesToFarm.Sum(game => game.CardsRemaining), Bot.CardsFarmer.TimeRemaining.ToHumanReadable())), Bot);
		}

		[ItemCanBeNull]
		private static async Task<string> ResponseStatus(ulong steamID, string botNames) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames));

				return null;
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<(string Response, Bot Bot)> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseStatus(steamID)))).ConfigureAwait(false);

			List<(string Response, Bot Bot)> validResults = new List<(string Response, Bot Bot)>(results.Where(result => !string.IsNullOrEmpty(result.Response)));

			if (validResults.Count == 0) {
				return null;
			}

			HashSet<Bot> botsRunning = validResults.Where(result => result.Bot.KeepRunning).Select(result => result.Bot).ToHashSet();

			string extraResponse = string.Format(Strings.BotStatusOverview, botsRunning.Count, validResults.Count, botsRunning.Sum(bot => bot.CardsFarmer.GamesToFarm.Count), botsRunning.Sum(bot => bot.CardsFarmer.GamesToFarm.Sum(game => game.CardsRemaining)));

			return string.Join(Environment.NewLine, validResults.Select(result => result.Response).Union(extraResponse.ToEnumerable()));
		}

		private string ResponseStop(ulong steamID) {
			if (steamID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(steamID));

				return null;
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			(bool success, string message) = Bot.Actions.Stop();

			return FormatBotResponse(success ? message : string.Format(Strings.WarningFailedWithError, message));
		}

		[ItemCanBeNull]
		private static async Task<string> ResponseStop(ulong steamID, string botNames) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames));

				return null;
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseStop(steamID)))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private async Task<string> ResponseTransfer(ulong steamID, string botNameTo) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNameTo)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNameTo));

				return null;
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			if (!Bot.IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			if (Bot.BotConfig.TransferableTypes.Count == 0) {
				return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(Bot.BotConfig.TransferableTypes)));
			}

			Bot targetBot = Bot.GetBot(botNameTo);

			if (targetBot == null) {
				return ASF.IsOwner(steamID) ? FormatBotResponse(string.Format(Strings.BotNotFound, botNameTo)) : null;
			}

			if (!targetBot.IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.TargetBotNotConnected);
			}

			if (targetBot.SteamID == Bot.SteamID) {
				return FormatBotResponse(Strings.BotSendingTradeToYourself);
			}

			(bool success, string message) = await Bot.Actions.SendTradeOffer(targetSteamID: targetBot.SteamID, wantedTypes: Bot.BotConfig.TransferableTypes).ConfigureAwait(false);

			return FormatBotResponse(success ? message : string.Format(Strings.WarningFailedWithError, message));
		}

		[ItemCanBeNull]
		private static async Task<string> ResponseTransfer(ulong steamID, string botNames, string botNameTo) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(botNameTo)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(botNameTo));

				return null;
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseTransfer(steamID, botNameTo))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private async Task<string> ResponseTransferByRealAppIDs(ulong steamID, IReadOnlyCollection<uint> realAppIDs, Bot targetBot) {
			if ((steamID == 0) || (realAppIDs == null) || (realAppIDs.Count == 0) || (targetBot == null)) {
				Bot.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(realAppIDs) + " || " + nameof(targetBot));

				return null;
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			if (!Bot.IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			if (Bot.BotConfig.TransferableTypes.Count == 0) {
				return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(Bot.BotConfig.TransferableTypes)));
			}

			if (!targetBot.IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.TargetBotNotConnected);
			}

			if (targetBot.SteamID == Bot.SteamID) {
				return FormatBotResponse(Strings.BotSendingTradeToYourself);
			}

			(bool success, string message) = await Bot.Actions.SendTradeOffer(targetSteamID: targetBot.SteamID, wantedTypes: Bot.BotConfig.TransferableTypes, wantedRealAppIDs: realAppIDs).ConfigureAwait(false);

			return FormatBotResponse(success ? message : string.Format(Strings.WarningFailedWithError, message));
		}

		private async Task<string> ResponseTransferByRealAppIDs(ulong steamID, string realAppIDsText, string botNameTo) {
			if ((steamID == 0) || string.IsNullOrEmpty(realAppIDsText) || string.IsNullOrEmpty(botNameTo)) {
				Bot.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(realAppIDsText) + " || " + nameof(botNameTo));

				return null;
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			Bot targetBot = Bot.GetBot(botNameTo);

			if (targetBot == null) {
				return ASF.IsOwner(steamID) ? FormatBotResponse(string.Format(Strings.BotNotFound, botNameTo)) : null;
			}

			string[] appIDTexts = realAppIDsText.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

			if (appIDTexts.Length == 0) {
				return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(appIDTexts)));
			}

			HashSet<uint> realAppIDs = new HashSet<uint>();

			foreach (string appIDText in appIDTexts) {
				if (!uint.TryParse(appIDText, out uint appID) || (appID == 0)) {
					return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(appID)));
				}

				realAppIDs.Add(appID);
			}

			return await ResponseTransferByRealAppIDs(steamID, realAppIDs, targetBot).ConfigureAwait(false);
		}

		private static async Task<string> ResponseTransferByRealAppIDs(ulong steamID, string botNames, string realAppIDsText, string botNameTo) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(realAppIDsText) || string.IsNullOrEmpty(botNameTo)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(realAppIDsText) + " || " + nameof(botNameTo));

				return null;
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			string[] appIDTexts = realAppIDsText.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

			if (appIDTexts.Length == 0) {
				return FormatStaticResponse(string.Format(Strings.ErrorIsEmpty, nameof(appIDTexts)));
			}

			HashSet<uint> realAppIDs = new HashSet<uint>();

			foreach (string appIDText in appIDTexts) {
				if (!uint.TryParse(appIDText, out uint appID) || (appID == 0)) {
					return FormatStaticResponse(string.Format(Strings.ErrorIsInvalid, nameof(appID)));
				}

				realAppIDs.Add(appID);
			}

			Bot targetBot = Bot.GetBot(botNameTo);

			if (targetBot == null) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNameTo)) : null;
			}

			IList<string> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseTransferByRealAppIDs(steamID, realAppIDs, targetBot))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private string ResponseUnknown(ulong steamID) {
			if (steamID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(steamID));

				return null;
			}

			return Bot.HasPermission(steamID, BotConfig.EPermission.Operator) ? FormatBotResponse(Strings.UnknownCommand) : null;
		}

		private async Task<string> ResponseUnpackBoosters(ulong steamID) {
			if (steamID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(steamID));

				return null;
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			if (!Bot.IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			HashSet<Steam.Asset> inventory = await Bot.ArchiWebHandler.GetInventory(Bot.SteamID, wantedTypes: new HashSet<Steam.Asset.EType> { Steam.Asset.EType.BoosterPack }).ConfigureAwait(false);

			if ((inventory == null) || (inventory.Count == 0)) {
				return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(inventory)));
			}

			// It'd make sense here to actually check return code of ArchiWebHandler.UnpackBooster(), but it lies most of the time | https://github.com/JustArchi/ArchiSteamFarm/issues/704
			// It'd also make sense to run all of this in parallel, but it seems that Steam has a lot of problems with inventory-related parallel requests | https://steamcommunity.com/groups/ascfarm/discussions/1/3559414588264550284/
			foreach (Steam.Asset item in inventory) {
				await Bot.ArchiWebHandler.UnpackBooster(item.RealAppID, item.AssetID).ConfigureAwait(false);
			}

			return FormatBotResponse(Strings.Done);
		}

		[ItemCanBeNull]
		private static async Task<string> ResponseUnpackBoosters(ulong steamID, string botNames) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames));

				return null;
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseUnpackBoosters(steamID))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private static async Task<string> ResponseUpdate(ulong steamID) {
			if (steamID == 0) {
				ASF.ArchiLogger.LogNullError(nameof(steamID));

				return null;
			}

			if (!ASF.IsOwner(steamID)) {
				return null;
			}

			(bool success, string message, Version version) = await Actions.Update().ConfigureAwait(false);

			return FormatStaticResponse((success ? Strings.Success : Strings.WarningFailed) + (!string.IsNullOrEmpty(message) ? " " + message : version != null ? " " + version : ""));
		}

		private string ResponseVersion(ulong steamID) {
			if (steamID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(steamID));

				return null;
			}

			return Bot.HasPermission(steamID, BotConfig.EPermission.Operator) ? FormatBotResponse(string.Format(Strings.BotVersion, SharedInfo.ASF, SharedInfo.Version)) : null;
		}

		private string ResponseWalletBalance(ulong steamID) {
			if (steamID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(steamID));

				return null;
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			return !Bot.IsConnectedAndLoggedOn ? FormatBotResponse(Strings.BotNotConnected) : FormatBotResponse(Bot.WalletCurrency != ECurrencyCode.Invalid ? string.Format(Strings.BotWalletBalance, Bot.WalletBalance / 100.0, Bot.WalletCurrency.ToString()) : Strings.BotHasNoWallet);
		}

		[ItemCanBeNull]
		private static async Task<string> ResponseWalletBalance(ulong steamID, string botNames) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames));

				return null;
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseWalletBalance(steamID)))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		[Flags]
		private enum ERedeemFlags : byte {
			None = 0,
			Validate = 1,
			ForceForwarding = 2,
			SkipForwarding = 4,
			ForceDistributing = 8,
			SkipDistributing = 16,
			SkipInitial = 32,
			ForceKeepMissingGames = 64,
			SkipKeepMissingGames = 128
		}
	}
}
