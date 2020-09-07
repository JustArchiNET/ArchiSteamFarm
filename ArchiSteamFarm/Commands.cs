//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2020 Łukasz "JustArchi" Domeradzki
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
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ArchiSteamFarm.Json;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Plugins;
using JetBrains.Annotations;
using SteamKit2;

namespace ArchiSteamFarm {
	public sealed class Commands {
		private const ushort SteamTypingStatusDelay = 10 * 1000; // Steam client broadcasts typing status each 10 seconds

		private readonly Bot Bot;
		private readonly Dictionary<uint, string> CachedGamesOwned = new Dictionary<uint, string>();

		internal Commands(Bot bot) => Bot = bot ?? throw new ArgumentNullException(nameof(bot));

		[PublicAPI]
		public static string FormatBotResponse(string response, string botName) {
			if (string.IsNullOrEmpty(response) || string.IsNullOrEmpty(botName)) {
				throw new ArgumentNullException(nameof(response) + " || " + nameof(botName));
			}

			return Environment.NewLine + "<" + botName + "> " + response;
		}

		[PublicAPI]
		public string FormatBotResponse(string response) {
			if (string.IsNullOrEmpty(response)) {
				throw new ArgumentNullException(nameof(response));
			}

			return "<" + Bot.BotName + "> " + response;
		}

		[PublicAPI]
		public static string FormatStaticResponse(string response) {
			if (string.IsNullOrEmpty(response)) {
				throw new ArgumentNullException(nameof(response));
			}

			return "<" + SharedInfo.ASF + "> " + response;
		}

		[PublicAPI]
		public async Task<string?> Response(ulong steamID, string message) {
			if ((steamID == 0) || string.IsNullOrEmpty(message)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(message));
			}

			string[] args = message.Split(new char[0], StringSplitOptions.RemoveEmptyEntries);

			switch (args.Length) {
				case 0:
					throw new ArgumentOutOfRangeException(nameof(args.Length));
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
						case "RESET":
							return await ResponseReset(steamID).ConfigureAwait(false);
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
							string? pluginsResponse = await PluginsCore.OnBotCommand(Bot, steamID, message, args).ConfigureAwait(false);

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
							return ResponseBlacklistAdd(steamID, args[1]);
						case "BLRM" when args.Length > 2:
							return await ResponseBlacklistRemove(steamID, args[1], Utilities.GetArgsAsText(args, 2, ",")).ConfigureAwait(false);
						case "BLRM":
							return ResponseBlacklistRemove(steamID, args[1]);
						case "ENCRYPT" when args.Length > 2:
							return ResponseEncrypt(steamID, args[1], Utilities.GetArgsAsText(message, 2));
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
							return ResponseIdleBlacklistAdd(steamID, args[1]);
						case "IBRM" when args.Length > 2:
							return await ResponseIdleBlacklistRemove(steamID, args[1], Utilities.GetArgsAsText(args, 2, ",")).ConfigureAwait(false);
						case "IBRM":
							return ResponseIdleBlacklistRemove(steamID, args[1]);
						case "IQ":
							return await ResponseIdleQueue(steamID, Utilities.GetArgsAsText(args, 1, ",")).ConfigureAwait(false);
						case "IQADD" when args.Length > 2:
							return await ResponseIdleQueueAdd(steamID, args[1], Utilities.GetArgsAsText(args, 2, ",")).ConfigureAwait(false);
						case "IQADD":
							return ResponseIdleQueueAdd(steamID, args[1]);
						case "IQRM" when args.Length > 2:
							return await ResponseIdleQueueRemove(steamID, args[1], Utilities.GetArgsAsText(args, 2, ",")).ConfigureAwait(false);
						case "IQRM":
							return ResponseIdleQueueRemove(steamID, args[1]);
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
						case "LOOT%" when args.Length > 2:
							return await ResponseLootByRealAppIDs(steamID, args[1], Utilities.GetArgsAsText(args, 2, ","), true).ConfigureAwait(false);
						case "LOOT%":
							return await ResponseLootByRealAppIDs(steamID, args[1], true).ConfigureAwait(false);
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
						case "RESET":
							return await ResponseReset(steamID, Utilities.GetArgsAsText(args, 1, ",")).ConfigureAwait(false);
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
						case "TRANSFER%" when args.Length > 3:
							return await ResponseTransferByRealAppIDs(steamID, args[1], args[2], Utilities.GetArgsAsText(message, 3), true).ConfigureAwait(false);
						case "TRANSFER%" when args.Length > 2:
							return await ResponseTransferByRealAppIDs(steamID, args[1], args[2], true).ConfigureAwait(false);
						case "UNPACK":
							return await ResponseUnpackBoosters(steamID, Utilities.GetArgsAsText(args, 1, ",")).ConfigureAwait(false);
						default:
							string? pluginsResponse = await PluginsCore.OnBotCommand(Bot, steamID, message, args).ConfigureAwait(false);

							return !string.IsNullOrEmpty(pluginsResponse) ? pluginsResponse : ResponseUnknown(steamID);
					}
			}
		}

		internal async Task HandleMessage(ulong steamID, string message) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(message)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(message));
			}

			string? commandPrefix = ASF.GlobalConfig != null ? ASF.GlobalConfig.CommandPrefix : GlobalConfig.DefaultCommandPrefix;

			if (!string.IsNullOrEmpty(commandPrefix)) {
				if (!message.StartsWith(commandPrefix!, StringComparison.OrdinalIgnoreCase)) {
					string? pluginsResponse = await PluginsCore.OnBotMessage(Bot, steamID, message).ConfigureAwait(false);

					if (!string.IsNullOrEmpty(pluginsResponse)) {
						if (!await Bot.SendMessage(steamID, pluginsResponse!).ConfigureAwait(false)) {
							Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.WarningFailedWithError, nameof(Bot.SendMessage)));
							Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.Content, pluginsResponse));
						}
					}

					return;
				}

				message = message.Substring(commandPrefix!.Length);
			}

			Task<string?> responseTask = Response(steamID, message);

			bool feedback = Bot.HasPermission(steamID, BotConfig.EPermission.FamilySharing);

			if (feedback && !responseTask.IsCompleted) {
				if (!await Bot.SendTypingMessage(steamID).ConfigureAwait(false)) {
					Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.WarningFailedWithError, nameof(Bot.SendTypingMessage)));
				}

				while (!responseTask.IsCompleted && (await Task.WhenAny(responseTask, Task.Delay(SteamTypingStatusDelay)).ConfigureAwait(false) != responseTask)) {
					if (!await Bot.SendTypingMessage(steamID).ConfigureAwait(false)) {
						Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.WarningFailedWithError, nameof(Bot.SendTypingMessage)));
					}
				}
			}

			string? response = await responseTask.ConfigureAwait(false);

			if (string.IsNullOrEmpty(response)) {
				if (!feedback) {
					return;
				}

				Bot.ArchiLogger.LogNullError(nameof(response));
				response = FormatBotResponse(Strings.UnknownCommand);
			}

			if (!await Bot.SendMessage(steamID, response!).ConfigureAwait(false)) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.WarningFailedWithError, nameof(Bot.SendMessage)));
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.Content, response));
			}
		}

		internal async Task HandleMessage(ulong chatGroupID, ulong chatID, ulong steamID, string message) {
			if ((chatGroupID == 0) || (chatID == 0) || (steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(message)) {
				throw new ArgumentNullException(nameof(chatGroupID) + " || " + nameof(chatID) + " || " + nameof(steamID) + " || " + nameof(message));
			}

			string? commandPrefix = ASF.GlobalConfig != null ? ASF.GlobalConfig.CommandPrefix : GlobalConfig.DefaultCommandPrefix;

			if (!string.IsNullOrEmpty(commandPrefix)) {
				if (!message.StartsWith(commandPrefix!, StringComparison.OrdinalIgnoreCase)) {
					string? pluginsResponse = await PluginsCore.OnBotMessage(Bot, steamID, message).ConfigureAwait(false);

					if (!string.IsNullOrEmpty(pluginsResponse)) {
						if (!await Bot.SendMessage(chatGroupID, chatID, pluginsResponse!).ConfigureAwait(false)) {
							Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.WarningFailedWithError, nameof(Bot.SendMessage)));
							Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.Content, pluginsResponse));
						}
					}

					return;
				}

				message = message.Substring(commandPrefix!.Length);
			}

			Task<string?> responseTask = Response(steamID, message);

			bool feedback = Bot.HasPermission(steamID, BotConfig.EPermission.FamilySharing);

			if (feedback && !responseTask.IsCompleted) {
				string pleaseWaitMessage = FormatBotResponse(Strings.PleaseWait);

				if (!await Bot.SendMessage(chatGroupID, chatID, pleaseWaitMessage).ConfigureAwait(false)) {
					Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.WarningFailedWithError, nameof(Bot.SendMessage)));
				}

				while (!responseTask.IsCompleted && (await Task.WhenAny(responseTask, Task.Delay(SteamTypingStatusDelay)).ConfigureAwait(false) != responseTask)) {
					if (!await Bot.SendMessage(chatGroupID, chatID, pleaseWaitMessage).ConfigureAwait(false)) {
						Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.WarningFailedWithError, nameof(Bot.SendMessage)));
					}
				}
			}

			string? response = await responseTask.ConfigureAwait(false);

			if (string.IsNullOrEmpty(response)) {
				if (!feedback) {
					return;
				}

				Bot.ArchiLogger.LogNullError(nameof(response));
				response = FormatBotResponse(Strings.UnknownCommand);
			}

			if (!await Bot.SendMessage(chatGroupID, chatID, response!).ConfigureAwait(false)) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.WarningFailedWithError, nameof(Bot.SendMessage)));
				Bot.ArchiLogger.LogGenericDebug(string.Format(Strings.Content, response));
			}
		}

		internal void OnNewLicenseList() {
			lock (CachedGamesOwned) {
				CachedGamesOwned.Clear();
				CachedGamesOwned.TrimExcess();
			}
		}

		private async Task<Dictionary<uint, string>?> FetchGamesOwned(bool cachedOnly = false) {
			lock (CachedGamesOwned) {
				if (CachedGamesOwned.Count > 0) {
					return new Dictionary<uint, string>(CachedGamesOwned);
				}
			}

			if (cachedOnly) {
				return null;
			}

			bool? hasValidApiKey = await Bot.ArchiWebHandler.HasValidApiKey().ConfigureAwait(false);

			Dictionary<uint, string>? gamesOwned = hasValidApiKey.GetValueOrDefault() ? await Bot.ArchiWebHandler.GetOwnedGames(Bot.SteamID).ConfigureAwait(false) : await Bot.ArchiWebHandler.GetMyOwnedGames().ConfigureAwait(false);

			if ((gamesOwned != null) && (gamesOwned.Count > 0)) {
				lock (CachedGamesOwned) {
					if (CachedGamesOwned.Count == 0) {
						foreach ((uint appID, string gameName) in gamesOwned) {
							CachedGamesOwned[appID] = gameName;
						}

						CachedGamesOwned.TrimExcess();
					}
				}
			}

			return gamesOwned;
		}

		private async Task<string?> Response2FA(ulong steamID) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
				throw new ArgumentNullException(nameof(steamID));
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			(bool success, string? token, string message) = await Bot.Actions.GenerateTwoFactorAuthenticationToken().ConfigureAwait(false);

			return FormatBotResponse(success && !string.IsNullOrEmpty(token) ? string.Format(Strings.BotAuthenticatorToken, token) : string.Format(Strings.WarningFailedWithError, message));
		}

		private static async Task<string?> Response2FA(ulong steamID, string botNames) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(botNames)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(botNames));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.Response2FA(steamID))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result))!);

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private async Task<string?> Response2FAConfirm(ulong steamID, bool confirm) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
				throw new ArgumentNullException(nameof(steamID));
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

		private static async Task<string?> Response2FAConfirm(ulong steamID, string botNames, bool confirm) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(botNames)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(botNames));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.Response2FAConfirm(steamID, confirm))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result))!);

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private async Task<string?> ResponseAddLicense(ulong steamID, string query) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(query)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(query));
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Operator)) {
				return null;
			}

			if (!Bot.IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			StringBuilder response = new StringBuilder();

			string[] entries = query.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

			foreach (string entry in entries) {
				uint gameID;
				string type;

				int index = entry.IndexOf('/');

				if ((index > 0) && (entry.Length > index + 1)) {
					if (!uint.TryParse(entry.Substring(index + 1), out gameID) || (gameID == 0)) {
						response.AppendLine(FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(gameID))));

						continue;
					}

					type = entry.Substring(0, index);
				} else if (uint.TryParse(entry, out gameID) && (gameID > 0)) {
					type = "SUB";
				} else {
					response.AppendLine(FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(gameID))));

					continue;
				}

				switch (type.ToUpperInvariant()) {
					case "A":
					case "APP":
						SteamApps.FreeLicenseCallback callback;

						try {
							callback = await Bot.SteamApps.RequestFreeLicense(gameID).ToLongRunningTask().ConfigureAwait(false);
						} catch (Exception e) {
							Bot.ArchiLogger.LogGenericWarningException(e);
							response.AppendLine(FormatBotResponse(string.Format(Strings.BotAddLicense, "app/" + gameID, EResult.Timeout)));

							break;
						}

						response.AppendLine(FormatBotResponse((callback.GrantedApps.Count > 0) || (callback.GrantedPackages.Count > 0) ? string.Format(Strings.BotAddLicenseWithItems, "app/" + gameID, callback.Result, string.Join(", ", callback.GrantedApps.Select(appID => "app/" + appID).Union(callback.GrantedPackages.Select(subID => "sub/" + subID)))) : string.Format(Strings.BotAddLicense, "app/" + gameID, callback.Result)));

						break;
					default:
						if (!await Bot.ArchiWebHandler.AddFreeLicense(gameID).ConfigureAwait(false)) {
							response.AppendLine(FormatBotResponse(string.Format(Strings.BotAddLicense, "sub/" + gameID, EResult.Fail)));

							continue;
						}

						response.AppendLine(FormatBotResponse(string.Format(Strings.BotAddLicenseWithItems, gameID, EResult.OK, "sub/" + gameID)));

						break;
				}
			}

			return response.Length > 0 ? response.ToString() : null;
		}

		private static async Task<string?> ResponseAddLicense(ulong steamID, string botNames, string query) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(query)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(query));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseAddLicense(steamID, query))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result))!);

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private async Task<string?> ResponseAdvancedLoot(ulong steamID, string targetAppID, string targetContextID) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(targetAppID) || string.IsNullOrEmpty(targetContextID)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(targetAppID) + " || " + nameof(targetContextID));
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

			(bool success, string message) = await Bot.Actions.SendInventory(appID, contextID).ConfigureAwait(false);

			return FormatBotResponse(success ? message : string.Format(Strings.WarningFailedWithError, message));
		}

		private static async Task<string?> ResponseAdvancedLoot(ulong steamID, string botNames, string appID, string contextID) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(appID) || string.IsNullOrEmpty(contextID)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(appID) + " || " + nameof(contextID));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseAdvancedLoot(steamID, appID, contextID))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result))!);

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private async Task<string?> ResponseAdvancedRedeem(ulong steamID, string options, string keys) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(options) || string.IsNullOrEmpty(keys)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(options) + " || " + nameof(keys));
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
					case "FAWK":
					case "FORCEASSUMEWALLETKEY":
						redeemFlags |= ERedeemFlags.ForceAssumeWalletKeyOnBadActivationCode;

						break;
					case "FD":
					case "FORCEDISTRIBUTING":
						redeemFlags |= ERedeemFlags.ForceDistributing;

						break;
					case "FF":
					case "FORCEFORWARDING":
						redeemFlags |= ERedeemFlags.ForceForwarding;

						break;
					case "FKMG":
					case "FORCEKEEPMISSINGGAMES":
						redeemFlags |= ERedeemFlags.ForceKeepMissingGames;

						break;
					case "SAWK":
					case "SKIPASSUMEWALLETKEY":
						redeemFlags |= ERedeemFlags.SkipAssumeWalletKeyOnBadActivationCode;

						break;
					case "SD":
					case "SKIPDISTRIBUTING":
						redeemFlags |= ERedeemFlags.SkipDistributing;

						break;
					case "SF":
					case "SKIPFORWARDING":
						redeemFlags |= ERedeemFlags.SkipForwarding;

						break;
					case "SI":
					case "SKIPINITIAL":
						redeemFlags |= ERedeemFlags.SkipInitial;

						break;
					case "SKMG":
					case "SKIPKEEPMISSINGGAMES":
						redeemFlags |= ERedeemFlags.SkipKeepMissingGames;

						break;
					case "V":
					case "VALIDATE":
						redeemFlags |= ERedeemFlags.Validate;

						break;
					default:
						return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, flag));
				}
			}

			return await ResponseRedeem(steamID, keys, redeemFlags).ConfigureAwait(false);
		}

		private static async Task<string?> ResponseAdvancedRedeem(ulong steamID, string botNames, string options, string keys) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(options) || string.IsNullOrEmpty(keys)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(options) + " || " + nameof(keys));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseAdvancedRedeem(steamID, options, keys))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result))!);

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private async Task<string?> ResponseAdvancedTransfer(ulong steamID, uint appID, ulong contextID, Bot targetBot) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || (appID == 0) || (contextID == 0) || (targetBot == null)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(appID) + " || " + nameof(contextID) + " || " + nameof(targetBot));
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

			(bool success, string message) = await Bot.Actions.SendInventory(appID, contextID, targetBot.SteamID).ConfigureAwait(false);

			return FormatBotResponse(success ? message : string.Format(Strings.WarningFailedWithError, message));
		}

		private async Task<string?> ResponseAdvancedTransfer(ulong steamID, string targetAppID, string targetContextID, string botNameTo) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(targetAppID) || string.IsNullOrEmpty(targetContextID) || string.IsNullOrEmpty(botNameTo)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(targetAppID) + " || " + nameof(targetContextID) + " || " + nameof(botNameTo));
			}

			Bot? targetBot = Bot.GetBot(botNameTo);

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

		private static async Task<string?> ResponseAdvancedTransfer(ulong steamID, string botNames, string targetAppID, string targetContextID, string botNameTo) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(targetAppID) || string.IsNullOrEmpty(targetContextID) || string.IsNullOrEmpty(botNameTo)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(targetAppID) + " || " + nameof(targetContextID) + " || " + nameof(botNameTo));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			if (!uint.TryParse(targetAppID, out uint appID) || (appID == 0)) {
				return FormatStaticResponse(string.Format(Strings.ErrorIsInvalid, nameof(appID)));
			}

			if (!ulong.TryParse(targetContextID, out ulong contextID) || (contextID == 0)) {
				return FormatStaticResponse(string.Format(Strings.ErrorIsInvalid, nameof(contextID)));
			}

			Bot? targetBot = Bot.GetBot(botNameTo);

			if (targetBot == null) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNameTo)) : null;
			}

			IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseAdvancedTransfer(steamID, appID, contextID, targetBot))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result))!);

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private string? ResponseBackgroundGamesRedeemer(ulong steamID) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
				throw new ArgumentNullException(nameof(steamID));
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			uint count = Bot.GamesToRedeemInBackgroundCount;

			return FormatBotResponse(string.Format(Strings.BotGamesToRedeemInBackgroundCount, count));
		}

		private static async Task<string?> ResponseBackgroundGamesRedeemer(ulong steamID, string botNames) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(botNames)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(botNames));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseBackgroundGamesRedeemer(steamID)))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result))!);

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private string? ResponseBlacklist(ulong steamID) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
				throw new ArgumentNullException(nameof(steamID));
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			IReadOnlyCollection<ulong> blacklist = Bot.BotDatabase.GetBlacklistedFromTradesSteamIDs();

			return FormatBotResponse(blacklist.Count > 0 ? string.Join(", ", blacklist) : string.Format(Strings.ErrorIsEmpty, nameof(blacklist)));
		}

		private static async Task<string?> ResponseBlacklist(ulong steamID, string botNames) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(botNames)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(botNames));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseBlacklist(steamID)))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result))!);

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private string? ResponseBlacklistAdd(ulong steamID, string targetSteamIDs) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(targetSteamIDs)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(targetSteamIDs));
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
				if (!ulong.TryParse(target, out ulong targetID) || (targetID == 0) || !new SteamID(targetID).IsIndividualAccount) {
					return FormatBotResponse(string.Format(Strings.ErrorParsingObject, nameof(targetID)));
				}

				targetIDs.Add(targetID);
			}

			Bot.BotDatabase.AddBlacklistedFromTradesSteamIDs(targetIDs);

			return FormatBotResponse(Strings.Done);
		}

		private static async Task<string?> ResponseBlacklistAdd(ulong steamID, string botNames, string targetSteamIDs) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(targetSteamIDs)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(targetSteamIDs));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseBlacklistAdd(steamID, targetSteamIDs)))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result))!);

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private string? ResponseBlacklistRemove(ulong steamID, string targetSteamIDs) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(targetSteamIDs)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(targetSteamIDs));
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
				if (!ulong.TryParse(target, out ulong targetID) || (targetID == 0) || !new SteamID(targetID).IsIndividualAccount) {
					return FormatBotResponse(string.Format(Strings.ErrorParsingObject, nameof(targetID)));
				}

				targetIDs.Add(targetID);
			}

			Bot.BotDatabase.RemoveBlacklistedFromTradesSteamIDs(targetIDs);

			return FormatBotResponse(Strings.Done);
		}

		private static async Task<string?> ResponseBlacklistRemove(ulong steamID, string botNames, string targetSteamIDs) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(targetSteamIDs)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(targetSteamIDs));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseBlacklistRemove(steamID, targetSteamIDs)))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result))!);

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private static string? ResponseEncrypt(ulong steamID, string cryptoMethodText, string stringToEncrypt) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(cryptoMethodText) || string.IsNullOrEmpty(stringToEncrypt)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(cryptoMethodText) + " || " + nameof(stringToEncrypt));
			}

			if (!ASF.IsOwner(steamID)) {
				return null;
			}

			if (!Enum.TryParse(cryptoMethodText, true, out ArchiCryptoHelper.ECryptoMethod cryptoMethod)) {
				return FormatStaticResponse(string.Format(Strings.ErrorIsInvalid, nameof(cryptoMethod)));
			}

			string? encryptedString = Actions.Encrypt(cryptoMethod, stringToEncrypt);

			return FormatStaticResponse(!string.IsNullOrEmpty(encryptedString) ? string.Format(Strings.Result, encryptedString) : Strings.WarningFailed);
		}

		private static string? ResponseExit(ulong steamID) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
				throw new ArgumentNullException(nameof(steamID));
			}

			if (!ASF.IsOwner(steamID)) {
				return null;
			}

			(bool success, string message) = Actions.Exit();

			return FormatStaticResponse(success ? message : string.Format(Strings.WarningFailedWithError, message));
		}

		private async Task<string?> ResponseFarm(ulong steamID) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
				throw new ArgumentNullException(nameof(steamID));
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

		private static async Task<string?> ResponseFarm(ulong steamID, string botNames) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(botNames)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(botNames));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseFarm(steamID))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result))!);

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private string? ResponseHelp(ulong steamID) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
				throw new ArgumentNullException(nameof(steamID));
			}

			return Bot.HasPermission(steamID, BotConfig.EPermission.FamilySharing) ? FormatBotResponse(SharedInfo.ProjectURL + "/wiki/Commands") : null;
		}

		private string? ResponseIdleBlacklist(ulong steamID) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
				throw new ArgumentNullException(nameof(steamID));
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			IReadOnlyCollection<uint> idleBlacklist = Bot.BotDatabase.GetIdlingBlacklistedAppIDs();

			return FormatBotResponse(idleBlacklist.Count > 0 ? string.Join(", ", idleBlacklist) : string.Format(Strings.ErrorIsEmpty, nameof(idleBlacklist)));
		}

		private static async Task<string?> ResponseIdleBlacklist(ulong steamID, string botNames) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(botNames)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(botNames));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseIdleBlacklist(steamID)))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result))!);

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private string? ResponseIdleBlacklistAdd(ulong steamID, string targetAppIDs) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(targetAppIDs)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(targetAppIDs));
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

			Bot.BotDatabase.AddIdlingBlacklistedAppIDs(appIDs);

			if (Bot.CardsFarmer.NowFarming && Bot.CardsFarmer.GamesToFarmReadOnly.Any(game => appIDs.Contains(game.AppID))) {
				Utilities.InBackground(
					async () => {
						await Bot.CardsFarmer.StopFarming().ConfigureAwait(false);
						await Bot.CardsFarmer.StartFarming().ConfigureAwait(false);
					}
				);
			}

			return FormatBotResponse(Strings.Done);
		}

		private static async Task<string?> ResponseIdleBlacklistAdd(ulong steamID, string botNames, string targetAppIDs) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(targetAppIDs)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(targetAppIDs));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseIdleBlacklistAdd(steamID, targetAppIDs)))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result))!);

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private string? ResponseIdleBlacklistRemove(ulong steamID, string targetAppIDs) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(targetAppIDs)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(targetAppIDs));
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

			Bot.BotDatabase.RemoveIdlingBlacklistedAppIDs(appIDs);

			return FormatBotResponse(Strings.Done);
		}

		private static async Task<string?> ResponseIdleBlacklistRemove(ulong steamID, string botNames, string targetAppIDs) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(targetAppIDs)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(targetAppIDs));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseIdleBlacklistRemove(steamID, targetAppIDs)))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result))!);

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private string? ResponseIdleQueue(ulong steamID) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
				throw new ArgumentNullException(nameof(steamID));
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			IReadOnlyCollection<uint> idleQueue = Bot.BotDatabase.GetIdlingPriorityAppIDs();

			return FormatBotResponse(idleQueue.Count > 0 ? string.Join(", ", idleQueue) : string.Format(Strings.ErrorIsEmpty, nameof(idleQueue)));
		}

		private static async Task<string?> ResponseIdleQueue(ulong steamID, string botNames) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(botNames)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(botNames));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseIdleQueue(steamID)))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result))!);

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private string? ResponseIdleQueueAdd(ulong steamID, string targetAppIDs) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(targetAppIDs)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(targetAppIDs));
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

			Bot.BotDatabase.AddIdlingPriorityAppIDs(appIDs);

			return FormatBotResponse(Strings.Done);
		}

		private static async Task<string?> ResponseIdleQueueAdd(ulong steamID, string botNames, string targetAppIDs) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(targetAppIDs)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(targetAppIDs));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseIdleQueueAdd(steamID, targetAppIDs)))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result))!);

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private string? ResponseIdleQueueRemove(ulong steamID, string targetAppIDs) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(targetAppIDs)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(targetAppIDs));
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

			Bot.BotDatabase.RemoveIdlingPriorityAppIDs(appIDs);

			return FormatBotResponse(Strings.Done);
		}

		private static async Task<string?> ResponseIdleQueueRemove(ulong steamID, string botNames, string targetAppIDs) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(targetAppIDs)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(targetAppIDs));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseIdleQueueRemove(steamID, targetAppIDs)))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result))!);

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private string? ResponseInput(ulong steamID, string propertyName, string inputValue) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(propertyName) || string.IsNullOrEmpty(inputValue)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(propertyName) + " || " + nameof(inputValue));
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			bool headless = ASF.GlobalConfig?.Headless ?? GlobalConfig.DefaultHeadless;

			if (!headless) {
				return FormatBotResponse(Strings.ErrorFunctionOnlyInHeadlessMode);
			}

			if (!Enum.TryParse(propertyName, true, out ASF.EUserInputType inputType) || (inputType == ASF.EUserInputType.None) || !Enum.IsDefined(typeof(ASF.EUserInputType), inputType)) {
				return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(inputType)));
			}

			bool result = Bot.SetUserInput(inputType, inputValue);

			return FormatBotResponse(result ? Strings.Done : Strings.WarningFailed);
		}

		private static async Task<string?> ResponseInput(ulong steamID, string botNames, string propertyName, string inputValue) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(propertyName) || string.IsNullOrEmpty(inputValue)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(propertyName) + " || " + nameof(inputValue));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseInput(steamID, propertyName, inputValue)))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result))!);

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private async Task<string?> ResponseLevel(ulong steamID) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
				throw new ArgumentNullException(nameof(steamID));
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			if (!Bot.IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			uint? level = await Bot.ArchiHandler.GetLevel().ConfigureAwait(false);

			return FormatBotResponse(level.HasValue ? string.Format(Strings.BotLevel, level.Value) : Strings.WarningFailed);
		}

		private static async Task<string?> ResponseLevel(ulong steamID, string botNames) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(botNames)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(botNames));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseLevel(steamID))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result))!);

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private async Task<string?> ResponseLoot(ulong steamID) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
				throw new ArgumentNullException(nameof(steamID));
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

			(bool success, string message) = await Bot.Actions.SendInventory(filterFunction: item => Bot.BotConfig.LootableTypes.Contains(item.Type)).ConfigureAwait(false);

			return FormatBotResponse(success ? message : string.Format(Strings.WarningFailedWithError, message));
		}

		private static async Task<string?> ResponseLoot(ulong steamID, string botNames) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(botNames)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(botNames));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseLoot(steamID))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result))!);

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private async Task<string?> ResponseLootByRealAppIDs(ulong steamID, string realAppIDsText, bool exclude = false) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(realAppIDsText)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(realAppIDsText));
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

			(bool success, string message) = await Bot.Actions.SendInventory(filterFunction: item => Bot.BotConfig.LootableTypes.Contains(item.Type) && (exclude ^ realAppIDs.Contains(item.RealAppID))).ConfigureAwait(false);

			return FormatBotResponse(success ? message : string.Format(Strings.WarningFailedWithError, message));
		}

		private static async Task<string?> ResponseLootByRealAppIDs(ulong steamID, string botNames, string realAppIDsText, bool exclude = false) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(realAppIDsText)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(realAppIDsText));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseLootByRealAppIDs(steamID, realAppIDsText, exclude))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result))!);

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private string? ResponseNickname(ulong steamID, string nickname) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(nickname)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(nickname));
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

		private static async Task<string?> ResponseNickname(ulong steamID, string botNames, string nickname) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(nickname)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(nickname));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseNickname(steamID, nickname)))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result))!);

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private async Task<(string? Response, Dictionary<string, string>? OwnedGames)> ResponseOwns(ulong steamID, string query) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(query)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(query));
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Operator)) {
				return (null, null);
			}

			if (!Bot.IsConnectedAndLoggedOn) {
				return (FormatBotResponse(Strings.BotNotConnected), null);
			}

			Dictionary<uint, string>? gamesOwned = await FetchGamesOwned(true).ConfigureAwait(false);

			StringBuilder response = new StringBuilder();
			Dictionary<string, string> result = new Dictionary<string, string>();

			string[] entries = query.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

			foreach (string entry in entries) {
				string game;
				string type;

				int index = entry.IndexOf('/');

				if ((index > 0) && (entry.Length > index + 1)) {
					game = entry.Substring(index + 1);
					type = entry.Substring(0, index);
				} else if (uint.TryParse(entry, out uint appID) && (appID > 0)) {
					game = entry;
					type = "APP";
				} else {
					game = entry;
					type = "NAME";
				}

				switch (type.ToUpperInvariant()) {
					case "A" when uint.TryParse(game, out uint appID) && (appID > 0):
					case "APP" when uint.TryParse(game, out appID) && (appID > 0):
						HashSet<uint>? packageIDs = ASF.GlobalDatabase?.GetPackageIDs(appID, Bot.OwnedPackageIDs.Keys);

						if ((packageIDs != null) && (packageIDs.Count > 0)) {
							if ((gamesOwned != null) && gamesOwned.TryGetValue(appID, out string? cachedGameName)) {
								result["app/" + appID] = cachedGameName;
								response.AppendLine(FormatBotResponse(string.Format(Strings.BotOwnedAlreadyWithName, "app/" + appID, cachedGameName)));
							} else {
								result["app/" + appID] = appID.ToString();
								response.AppendLine(FormatBotResponse(string.Format(Strings.BotOwnedAlready, "app/" + appID)));
							}
						} else {
							if (gamesOwned == null) {
								gamesOwned = await FetchGamesOwned().ConfigureAwait(false);

								if (gamesOwned == null) {
									response.AppendLine(FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(gamesOwned))));

									break;
								}
							}

							if (gamesOwned.TryGetValue(appID, out string? gameName)) {
								result["app/" + appID] = gameName;
								response.AppendLine(FormatBotResponse(string.Format(Strings.BotOwnedAlreadyWithName, "app/" + appID, gameName)));
							} else {
								response.AppendLine(FormatBotResponse(string.Format(Strings.BotNotOwnedYet, "app/" + appID)));
							}
						}

						break;
					case "R":
					case "REGEX":
						Regex regex;

						try {
							regex = new Regex(game);
						} catch (ArgumentException e) {
							Bot.ArchiLogger.LogGenericWarningException(e);
							response.AppendLine(FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(regex))));

							break;
						}

						if (gamesOwned == null) {
							gamesOwned = await FetchGamesOwned().ConfigureAwait(false);

							if (gamesOwned == null) {
								response.AppendLine(FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(gamesOwned))));

								break;
							}
						}

						bool foundWithRegex = false;

						foreach ((uint appID, string gameName) in gamesOwned.Where(gameOwned => regex.IsMatch(gameOwned.Value))) {
							foundWithRegex = true;

							result["app/" + appID] = gameName;
							response.AppendLine(FormatBotResponse(string.Format(Strings.BotOwnedAlreadyWithName, "app/" + appID, gameName)));
						}

						if (!foundWithRegex) {
							response.AppendLine(FormatBotResponse(string.Format(Strings.BotNotOwnedYet, entry)));
						}

						continue;
					case "S" when uint.TryParse(game, out uint packageID) && (packageID > 0):
					case "SUB" when uint.TryParse(game, out packageID) && (packageID > 0):
						if (Bot.OwnedPackageIDs.ContainsKey(packageID)) {
							result["sub/" + packageID] = packageID.ToString();
							response.AppendLine(FormatBotResponse(string.Format(Strings.BotOwnedAlready, "sub/" + packageID)));
						} else {
							response.AppendLine(FormatBotResponse(string.Format(Strings.BotNotOwnedYet, "sub/" + packageID)));
						}

						break;
					default:
						if (gamesOwned == null) {
							gamesOwned = await FetchGamesOwned().ConfigureAwait(false);

							if (gamesOwned == null) {
								response.AppendLine(FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(gamesOwned))));

								break;
							}
						}

						bool foundWithName = false;

						foreach ((uint appID, string gameName) in gamesOwned.Where(gameOwned => gameOwned.Value.IndexOf(game, StringComparison.OrdinalIgnoreCase) >= 0)) {
							foundWithName = true;

							result["app/" + appID] = gameName;
							response.AppendLine(FormatBotResponse(string.Format(Strings.BotOwnedAlreadyWithName, "app/" + appID, gameName)));
						}

						if (!foundWithName) {
							response.AppendLine(FormatBotResponse(string.Format(Strings.BotNotOwnedYet, entry)));
						}

						break;
				}
			}

			return (response.Length > 0 ? response.ToString() : FormatBotResponse(string.Format(Strings.BotNotOwnedYet, query)), result);
		}

		private static async Task<string?> ResponseOwns(ulong steamID, string botNames, string query) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(query)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(query));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<(string? Response, Dictionary<string, string>? OwnedGames)> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseOwns(steamID, query))).ConfigureAwait(false);

			List<(string Response, Dictionary<string, string> OwnedGames)> validResults = new List<(string Response, Dictionary<string, string> OwnedGames)>(results.Where(result => !string.IsNullOrEmpty(result.Response) && (result.OwnedGames != null))!);

			if (validResults.Count == 0) {
				return null;
			}

			Dictionary<string, (ushort Count, string GameName)> ownedGamesStats = new Dictionary<string, (ushort Count, string GameName)>();

			foreach ((string gameID, string gameName) in validResults.Where(validResult => validResult.OwnedGames.Count > 0).SelectMany(validResult => validResult.OwnedGames)) {
				if (ownedGamesStats.TryGetValue(gameID, out (ushort Count, string GameName) ownedGameStats)) {
					ownedGameStats.Count++;
				} else {
					ownedGameStats.Count = 1;
				}

				if (!string.IsNullOrEmpty(gameName)) {
					ownedGameStats.GameName = gameName;
				}

				ownedGamesStats[gameID] = ownedGameStats;
			}

			IEnumerable<string> extraResponses = ownedGamesStats.Select(kv => FormatStaticResponse(string.Format(Strings.BotOwnsOverviewPerGame, kv.Value.Count, validResults.Count, kv.Key + (!string.IsNullOrEmpty(kv.Value.GameName) ? " | " + kv.Value.GameName : ""))));

			return string.Join(Environment.NewLine, validResults.Select(result => result.Response).Concat(extraResponses));
		}

		private string? ResponsePassword(ulong steamID) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
				throw new ArgumentNullException(nameof(steamID));
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			if (string.IsNullOrEmpty(Bot.BotConfig.DecryptedSteamPassword)) {
				return FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(BotConfig.DecryptedSteamPassword)));
			}

			Dictionary<ArchiCryptoHelper.ECryptoMethod, string> encryptedPasswords = new Dictionary<ArchiCryptoHelper.ECryptoMethod, string>(2) {
				{ ArchiCryptoHelper.ECryptoMethod.AES, ArchiCryptoHelper.Encrypt(ArchiCryptoHelper.ECryptoMethod.AES, Bot.BotConfig.DecryptedSteamPassword!) ?? "" },
				{ ArchiCryptoHelper.ECryptoMethod.ProtectedDataForCurrentUser, ArchiCryptoHelper.Encrypt(ArchiCryptoHelper.ECryptoMethod.ProtectedDataForCurrentUser, Bot.BotConfig.DecryptedSteamPassword!) ?? "" }
			};

			return FormatBotResponse(string.Join(", ", encryptedPasswords.Where(kv => !string.IsNullOrEmpty(kv.Value)).Select(kv => string.Format(Strings.BotEncryptedPassword, kv.Key, kv.Value))));
		}

		private static async Task<string?> ResponsePassword(ulong steamID, string botNames) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(botNames)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(botNames));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponsePassword(steamID)))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result))!);

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private async Task<string?> ResponsePause(ulong steamID, bool permanent, string? resumeInSecondsText = null) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
				throw new ArgumentNullException(nameof(steamID));
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

		private static async Task<string?> ResponsePause(ulong steamID, string botNames, bool permanent, string? resumeInSecondsText = null) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(botNames)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(botNames));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponsePause(steamID, permanent, resumeInSecondsText))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result))!);

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private async Task<string?> ResponsePlay(ulong steamID, IReadOnlyCollection<uint> gameIDs, string? gameName = null) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || (gameIDs == null) || (gameIDs.Count > ArchiHandler.MaxGamesPlayedConcurrently)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(gameIDs));
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			if (!Bot.IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			(bool success, string message) = await Bot.Actions.Play(gameIDs, gameName).ConfigureAwait(false);

			return FormatBotResponse(success ? message : string.Format(Strings.WarningFailedWithError, message));
		}

		private async Task<string?> ResponsePlay(ulong steamID, string targetGameIDs) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(targetGameIDs)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(targetGameIDs));
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
					return FormatBotResponse(string.Format(Strings.WarningFailedWithError, nameof(gamesToPlay) + " > " + ArchiHandler.MaxGamesPlayedConcurrently));
				}

				gamesToPlay.Add(gameID);
			}

			return await ResponsePlay(steamID, gamesToPlay, gameName.Length > 0 ? gameName.ToString() : null).ConfigureAwait(false);
		}

		private static async Task<string?> ResponsePlay(ulong steamID, string botNames, string targetGameIDs) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(targetGameIDs)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(targetGameIDs));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponsePlay(steamID, targetGameIDs))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result))!);

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private async Task<string?> ResponsePrivacy(ulong steamID, string privacySettingsText) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(privacySettingsText)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(privacySettingsText));
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

			ArchiHandler.EPrivacySetting profile = ArchiHandler.EPrivacySetting.Private;
			ArchiHandler.EPrivacySetting ownedGames = ArchiHandler.EPrivacySetting.Private;
			ArchiHandler.EPrivacySetting playtime = ArchiHandler.EPrivacySetting.Private;
			ArchiHandler.EPrivacySetting friendsList = ArchiHandler.EPrivacySetting.Private;
			ArchiHandler.EPrivacySetting inventory = ArchiHandler.EPrivacySetting.Private;
			ArchiHandler.EPrivacySetting inventoryGifts = ArchiHandler.EPrivacySetting.Private;
			Steam.UserPrivacy.ECommentPermission comments = Steam.UserPrivacy.ECommentPermission.Private;

			// Converting digits to enum
			for (byte index = 0; index < privacySettingsArgs.Length; index++) {
				if (!Enum.TryParse(privacySettingsArgs[index], true, out ArchiHandler.EPrivacySetting privacySetting) || (privacySetting == ArchiHandler.EPrivacySetting.Unknown) || !Enum.IsDefined(typeof(ArchiHandler.EPrivacySetting), privacySetting)) {
					return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(privacySettingsArgs)));
				}

				// Child setting can't be less restrictive than its parent
				switch (index) {
					case 0:
						// Profile
						profile = privacySetting;

						break;
					case 1:
						// OwnedGames, child of Profile
						if (profile < privacySetting) {
							return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(ownedGames)));
						}

						ownedGames = privacySetting;

						break;
					case 2:
						// Playtime, child of OwnedGames
						if (ownedGames < privacySetting) {
							return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(playtime)));
						}

						playtime = privacySetting;

						break;
					case 3:
						// FriendsList, child of Profile
						if (profile < privacySetting) {
							return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(ownedGames)));
						}

						friendsList = privacySetting;

						break;
					case 4:
						// Inventory, child of Profile
						if (profile < privacySetting) {
							return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(inventory)));
						}

						inventory = privacySetting;

						break;
					case 5:
						// InventoryGifts, child of Inventory
						if (inventory < privacySetting) {
							return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(inventoryGifts)));
						}

						inventoryGifts = privacySetting;

						break;
					case 6:
						// Comments, child of Profile
						if (profile < privacySetting) {
							return FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(comments)));
						}

						// Comments use different numbers than everything else, but we want to have this command consistent for end-user, so we'll map them
						switch (privacySetting) {
							case ArchiHandler.EPrivacySetting.FriendsOnly:
								comments = Steam.UserPrivacy.ECommentPermission.FriendsOnly;

								break;
							case ArchiHandler.EPrivacySetting.Private:
								comments = Steam.UserPrivacy.ECommentPermission.Private;

								break;
							case ArchiHandler.EPrivacySetting.Public:
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

		private static async Task<string?> ResponsePrivacy(ulong steamID, string botNames, string privacySettingsText) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(privacySettingsText)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(privacySettingsText));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponsePrivacy(steamID, privacySettingsText))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result))!);

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private async Task<string?> ResponseRedeem(ulong steamID, string keysText, ERedeemFlags redeemFlags = ERedeemFlags.None) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(keysText) || (Bot.Bots == null)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(keysText) + " || " + nameof(Bot.Bots));
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
			bool assumeWalletKeyOnBadActivationCode = !redeemFlags.HasFlag(ERedeemFlags.SkipAssumeWalletKeyOnBadActivationCode) && (redeemFlags.HasFlag(ERedeemFlags.ForceAssumeWalletKeyOnBadActivationCode) || Bot.BotConfig.RedeemingPreferences.HasFlag(BotConfig.ERedeemingPreferences.AssumeWalletKeyOnBadActivationCode));

			HashSet<string> pendingKeys = keys.ToHashSet(StringComparer.Ordinal);
			HashSet<string> unusedKeys = pendingKeys.ToHashSet(StringComparer.Ordinal);

			HashSet<Bot> rateLimitedBots = new HashSet<Bot>();
			HashSet<Bot> triedBots = new HashSet<Bot>();

			StringBuilder response = new StringBuilder();

			using (HashSet<string>.Enumerator keysEnumerator = pendingKeys.GetEnumerator()) {
				// Initial key
				string? key = keysEnumerator.MoveNext() ? keysEnumerator.Current : null;
				string? previousKey = key;

				while (!string.IsNullOrEmpty(key)) {
					string startingKey = key!;

					using (IEnumerator<Bot> botsEnumerator = Bot.Bots.Where(bot => (bot.Value != Bot) && bot.Value.IsConnectedAndLoggedOn && bot.Value.Commands.Bot.HasPermission(steamID, BotConfig.EPermission.Operator)).OrderByDescending(bot => Bot.BotsComparer?.Compare(bot.Key, Bot.BotName) > 0).ThenBy(bot => bot.Key, Bot.BotsComparer).Select(bot => bot.Value).GetEnumerator()) {
						Bot? currentBot = Bot;

						while (!string.IsNullOrEmpty(key) && (currentBot != null)) {
							if (previousKey != key) {
								triedBots.Clear();
								previousKey = key;
							}

							if (redeemFlags.HasFlag(ERedeemFlags.Validate) && !Utilities.IsValidCdKey(key!)) {
								// Next key
								key = keysEnumerator.MoveNext() ? keysEnumerator.Current : null;

								// Keep current bot
								continue;
							}

							if ((currentBot == Bot) && redeemFlags.HasFlag(ERedeemFlags.SkipInitial)) {
								// Either bot will be changed, or loop aborted
								currentBot = null;
							} else {
								bool skipRequest = triedBots.Contains(currentBot) || rateLimitedBots.Contains(currentBot);

								ArchiHandler.PurchaseResponseCallback? result = skipRequest ? new ArchiHandler.PurchaseResponseCallback(EResult.Fail, EPurchaseResultDetail.CancelledByUser) : await currentBot.Actions.RedeemKey(key!).ConfigureAwait(false);

								if (result == null) {
									response.AppendLine(FormatBotResponse(string.Format(Strings.BotRedeem, key, EPurchaseResultDetail.Timeout), currentBot.BotName));

									// Either bot will be changed, or loop aborted
									currentBot = null;
								} else {
									triedBots.Add(currentBot);

									if (((result.PurchaseResultDetail == EPurchaseResultDetail.CannotRedeemCodeFromClient) || ((result.PurchaseResultDetail == EPurchaseResultDetail.BadActivationCode) && assumeWalletKeyOnBadActivationCode)) && (Bot.WalletCurrency != ECurrencyCode.Invalid)) {
										// If it's a wallet code, we try to redeem it first, then handle the inner result as our primary one
										(EResult Result, EPurchaseResultDetail? PurchaseResult)? walletResult = await currentBot.ArchiWebHandler.RedeemWalletKey(key!).ConfigureAwait(false);

										if (walletResult != null) {
											result.Result = walletResult.Value.Result;

											// BadActivationCode is our smart guess in this case
											result.PurchaseResultDetail = walletResult.Value.PurchaseResult.GetValueOrDefault(walletResult.Value.Result == EResult.OK ? EPurchaseResultDetail.NoDetail : EPurchaseResultDetail.BadActivationCode);
										} else {
											result.Result = EResult.Timeout;
											result.PurchaseResultDetail = EPurchaseResultDetail.Timeout;
										}
									}

									if ((result.Items != null) && (result.Items.Count > 0)) {
										response.AppendLine(FormatBotResponse(string.Format(Strings.BotRedeemWithItems, key, result.Result + "/" + result.PurchaseResultDetail, string.Join(", ", result.Items)), currentBot.BotName));
									} else if (!skipRequest) {
										response.AppendLine(FormatBotResponse(string.Format(Strings.BotRedeem, key, result.Result + "/" + result.PurchaseResultDetail), currentBot.BotName));
									}

									switch (result.PurchaseResultDetail) {
										case EPurchaseResultDetail.BadActivationCode:
										case EPurchaseResultDetail.CannotRedeemCodeFromClient:
										case EPurchaseResultDetail.DuplicateActivationCode:
										case EPurchaseResultDetail.NoDetail: // OK
										case EPurchaseResultDetail.Timeout:
											if ((result.Result != EResult.Timeout) && (result.PurchaseResultDetail != EPurchaseResultDetail.Timeout)) {
												unusedKeys.Remove(key!);
											}

											// Next key
											key = keysEnumerator.MoveNext() ? keysEnumerator.Current : null;

											if (result.PurchaseResultDetail == EPurchaseResultDetail.NoDetail) {
												// Next bot (if needed)
												break;
											}

											// Keep current bot
											continue;
										case EPurchaseResultDetail.AccountLocked:
										case EPurchaseResultDetail.AlreadyPurchased:
										case EPurchaseResultDetail.CancelledByUser:
										case EPurchaseResultDetail.DoesNotOwnRequiredApp:
										case EPurchaseResultDetail.RestrictedCountry:
											if (!forward || (keepMissingGames && (result.PurchaseResultDetail != EPurchaseResultDetail.AlreadyPurchased))) {
												// Next key
												key = keysEnumerator.MoveNext() ? keysEnumerator.Current : null;

												// Next bot (if needed)
												break;
											}

											if (distribute) {
												// Next bot, without changing key
												break;
											}

											Dictionary<uint, string> items = result.Items ?? new Dictionary<uint, string>();

											bool alreadyHandled = false;

											foreach (Bot innerBot in Bot.Bots.Where(bot => (bot.Value != currentBot) && (!redeemFlags.HasFlag(ERedeemFlags.SkipInitial) || (bot.Value != Bot)) && !triedBots.Contains(bot.Value) && !rateLimitedBots.Contains(bot.Value) && bot.Value.IsConnectedAndLoggedOn && bot.Value.Commands.Bot.HasPermission(steamID, BotConfig.EPermission.Operator) && ((items.Count == 0) || items.Keys.Any(packageID => !bot.Value.OwnedPackageIDs.ContainsKey(packageID)))).OrderBy(bot => bot.Key, Bot.BotsComparer).Select(bot => bot.Value)) {
												ArchiHandler.PurchaseResponseCallback? otherResult = await innerBot.Actions.RedeemKey(key!).ConfigureAwait(false);

												if (otherResult == null) {
													response.AppendLine(FormatBotResponse(string.Format(Strings.BotRedeem, key, EResult.Timeout + "/" + EPurchaseResultDetail.Timeout), innerBot.BotName));

													continue;
												}

												triedBots.Add(innerBot);

												switch (otherResult.PurchaseResultDetail) {
													case EPurchaseResultDetail.BadActivationCode:
													case EPurchaseResultDetail.DuplicateActivationCode:
													case EPurchaseResultDetail.NoDetail: // OK
														// This key is already handled, as we either redeemed it or we're sure it's dupe/invalid
														alreadyHandled = true;
														unusedKeys.Remove(key!);

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

											// Next key
											key = keysEnumerator.MoveNext() ? keysEnumerator.Current : null;

											// Next bot (if needed)
											break;
										case EPurchaseResultDetail.RateLimited:
											rateLimitedBots.Add(currentBot);

											goto case EPurchaseResultDetail.CancelledByUser;
										default:
											ASF.ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(result.PurchaseResultDetail), result.PurchaseResultDetail));

											unusedKeys.Remove(key!);

											// Next key
											key = keysEnumerator.MoveNext() ? keysEnumerator.Current : null;

											// Next bot (if needed)
											break;
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
						// We ran out of bots to try for this key, so change it to avoid infinite loop, next key
						key = keysEnumerator.MoveNext() ? keysEnumerator.Current : null;
					}
				}
			}

			if (unusedKeys.Count > 0) {
				response.AppendLine(FormatBotResponse(string.Format(Strings.UnusedKeys, string.Join(", ", unusedKeys))));
			}

			return response.Length > 0 ? response.ToString() : null;
		}

		private static async Task<string?> ResponseRedeem(ulong steamID, string botNames, string keys, ERedeemFlags redeemFlags = ERedeemFlags.None) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(keys)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(keys));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseRedeem(steamID, keys, redeemFlags))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result))!);

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private async Task<string?> ResponseReset(ulong steamID) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
				throw new ArgumentNullException(nameof(steamID));
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			if (!Bot.IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			(bool success, string message) = await Bot.Actions.Play(Enumerable.Empty<uint>(), Bot.BotConfig.CustomGamePlayedWhileIdle).ConfigureAwait(false);

			return FormatBotResponse(success ? message : string.Format(Strings.WarningFailedWithError, message));
		}

		private static async Task<string?> ResponseReset(ulong steamID, string botNames) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(botNames)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(botNames));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseReset(steamID))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result))!);

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private static string? ResponseRestart(ulong steamID) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
				throw new ArgumentNullException(nameof(steamID));
			}

			if (!ASF.IsOwner(steamID)) {
				return null;
			}

			(bool success, string message) = Actions.Restart();

			return FormatStaticResponse(success ? message : string.Format(Strings.WarningFailedWithError, message));
		}

		private string? ResponseResume(ulong steamID) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
				throw new ArgumentNullException(nameof(steamID));
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.FamilySharing)) {
				return null;
			}

			(bool success, string message) = Bot.Actions.Resume();

			return FormatBotResponse(success ? message : string.Format(Strings.WarningFailedWithError, message));
		}

		private static async Task<string?> ResponseResume(ulong steamID, string botNames) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(botNames)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(botNames));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseResume(steamID)))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result))!);

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private string? ResponseStart(ulong steamID) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
				throw new ArgumentNullException(nameof(steamID));
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			(bool success, string message) = Bot.Actions.Start();

			return FormatBotResponse(success ? message : string.Format(Strings.WarningFailedWithError, message));
		}

		private static async Task<string?> ResponseStart(ulong steamID, string botNames) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(botNames)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(botNames));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseStart(steamID)))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result))!);

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private string? ResponseStats(ulong steamID) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
				throw new ArgumentNullException(nameof(steamID));
			}

			if (!ASF.IsOwner(steamID)) {
				return null;
			}

			ushort memoryInMegabytes = (ushort) (GC.GetTotalMemory(false) / 1024 / 1024);
			TimeSpan uptime = DateTime.UtcNow.Subtract(RuntimeCompatibility.ProcessStartTime.ToUniversalTime());

			return FormatBotResponse(string.Format(Strings.BotStats, memoryInMegabytes, uptime.ToHumanReadable()));
		}

		private (string? Response, Bot Bot) ResponseStatus(ulong steamID) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
				throw new ArgumentNullException(nameof(steamID));
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

			if (!Bot.CardsFarmer.NowFarming || (Bot.CardsFarmer.CurrentGamesFarmingReadOnly.Count == 0)) {
				return (FormatBotResponse(Strings.BotStatusNotIdling), Bot);
			}

			if (Bot.CardsFarmer.CurrentGamesFarmingReadOnly.Count > 1) {
				return (FormatBotResponse(string.Format(Strings.BotStatusIdlingList, string.Join(", ", Bot.CardsFarmer.CurrentGamesFarmingReadOnly.Select(game => game.AppID + " (" + game.GameName + ")")), Bot.CardsFarmer.GamesToFarmReadOnly.Count, Bot.CardsFarmer.GamesToFarmReadOnly.Sum(game => game.CardsRemaining), Bot.CardsFarmer.TimeRemaining.ToHumanReadable())), Bot);
			}

			CardsFarmer.Game soloGame = Bot.CardsFarmer.CurrentGamesFarmingReadOnly.First();

			return (FormatBotResponse(string.Format(Strings.BotStatusIdling, soloGame.AppID, soloGame.GameName, soloGame.CardsRemaining, Bot.CardsFarmer.GamesToFarmReadOnly.Count, Bot.CardsFarmer.GamesToFarmReadOnly.Sum(game => game.CardsRemaining), Bot.CardsFarmer.TimeRemaining.ToHumanReadable())), Bot);
		}

		private static async Task<string?> ResponseStatus(ulong steamID, string botNames) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(botNames)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(botNames));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<(string? Response, Bot Bot)> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseStatus(steamID)))).ConfigureAwait(false);

			List<(string Response, Bot Bot)> validResults = new List<(string Response, Bot Bot)>(results.Where(result => !string.IsNullOrEmpty(result.Response))!);

			if (validResults.Count == 0) {
				return null;
			}

			HashSet<Bot> botsRunning = validResults.Where(result => result.Bot.KeepRunning).Select(result => result.Bot).ToHashSet();

			string extraResponse = string.Format(Strings.BotStatusOverview, botsRunning.Count, validResults.Count, botsRunning.Sum(bot => bot.CardsFarmer.GamesToFarmReadOnly.Count), botsRunning.Sum(bot => bot.CardsFarmer.GamesToFarmReadOnly.Sum(game => game.CardsRemaining)));

			return string.Join(Environment.NewLine, validResults.Select(result => result.Response).Union(extraResponse.ToEnumerable()));
		}

		private string? ResponseStop(ulong steamID) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
				throw new ArgumentNullException(nameof(steamID));
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			(bool success, string message) = Bot.Actions.Stop();

			return FormatBotResponse(success ? message : string.Format(Strings.WarningFailedWithError, message));
		}

		private static async Task<string?> ResponseStop(ulong steamID, string botNames) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(botNames)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(botNames));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseStop(steamID)))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result))!);

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private async Task<string?> ResponseTransfer(ulong steamID, string botNameTo) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(botNameTo)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(botNameTo));
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

			Bot? targetBot = Bot.GetBot(botNameTo);

			if (targetBot == null) {
				return ASF.IsOwner(steamID) ? FormatBotResponse(string.Format(Strings.BotNotFound, botNameTo)) : null;
			}

			if (!targetBot.IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.TargetBotNotConnected);
			}

			if (targetBot.SteamID == Bot.SteamID) {
				return FormatBotResponse(Strings.BotSendingTradeToYourself);
			}

			(bool success, string message) = await Bot.Actions.SendInventory(targetSteamID: targetBot.SteamID, filterFunction: item => Bot.BotConfig.TransferableTypes.Contains(item.Type)).ConfigureAwait(false);

			return FormatBotResponse(success ? message : string.Format(Strings.WarningFailedWithError, message));
		}

		private static async Task<string?> ResponseTransfer(ulong steamID, string botNames, string botNameTo) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(botNameTo)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(botNameTo));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseTransfer(steamID, botNameTo))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result))!);

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private async Task<string?> ResponseTransferByRealAppIDs(ulong steamID, IReadOnlyCollection<uint> realAppIDs, Bot targetBot, bool exclude = false) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || (realAppIDs == null) || (realAppIDs.Count == 0) || (targetBot == null)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(realAppIDs) + " || " + nameof(targetBot));
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

			(bool success, string message) = await Bot.Actions.SendInventory(targetSteamID: targetBot.SteamID, filterFunction: item => Bot.BotConfig.TransferableTypes.Contains(item.Type) && (exclude ^ realAppIDs.Contains(item.RealAppID))).ConfigureAwait(false);

			return FormatBotResponse(success ? message : string.Format(Strings.WarningFailedWithError, message));
		}

		private async Task<string?> ResponseTransferByRealAppIDs(ulong steamID, string realAppIDsText, string botNameTo, bool exclude = false) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(realAppIDsText) || string.IsNullOrEmpty(botNameTo)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(realAppIDsText) + " || " + nameof(botNameTo));
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			Bot? targetBot = Bot.GetBot(botNameTo);

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

			return await ResponseTransferByRealAppIDs(steamID, realAppIDs, targetBot, exclude).ConfigureAwait(false);
		}

		private static async Task<string?> ResponseTransferByRealAppIDs(ulong steamID, string botNames, string realAppIDsText, string botNameTo, bool exclude = false) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(botNames) || string.IsNullOrEmpty(realAppIDsText) || string.IsNullOrEmpty(botNameTo)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(botNames) + " || " + nameof(realAppIDsText) + " || " + nameof(botNameTo));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

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

			Bot? targetBot = Bot.GetBot(botNameTo);

			if (targetBot == null) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNameTo)) : null;
			}

			IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseTransferByRealAppIDs(steamID, realAppIDs, targetBot, exclude))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result))!);

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private string? ResponseUnknown(ulong steamID) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
				throw new ArgumentNullException(nameof(steamID));
			}

			return Bot.HasPermission(steamID, BotConfig.EPermission.Operator) ? FormatBotResponse(Strings.UnknownCommand) : null;
		}

		private async Task<string?> ResponseUnpackBoosters(ulong steamID) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
				throw new ArgumentNullException(nameof(steamID));
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			if (!Bot.IsConnectedAndLoggedOn) {
				return FormatBotResponse(Strings.BotNotConnected);
			}

			// It'd make sense here to actually check return code of ArchiWebHandler.UnpackBooster(), but it lies most of the time | https://github.com/JustArchi/ArchiSteamFarm/issues/704
			bool completeSuccess = true;

			// It'd also make sense to run all of this in parallel, but it seems that Steam has a lot of problems with inventory-related parallel requests | https://steamcommunity.com/groups/ascfarm/discussions/1/3559414588264550284/
			try {
				await foreach (Steam.Asset item in Bot.ArchiWebHandler.GetInventoryAsync(Bot.SteamID).Where(item => item.Type == Steam.Asset.EType.BoosterPack).ConfigureAwait(false)) {
					if (!await Bot.ArchiWebHandler.UnpackBooster(item.RealAppID, item.AssetID).ConfigureAwait(false)) {
						completeSuccess = false;
					}
				}
			} catch (HttpRequestException e) {
				Bot.ArchiLogger.LogGenericWarningException(e);

				completeSuccess = false;
			} catch (Exception e) {
				Bot.ArchiLogger.LogGenericException(e);

				completeSuccess = false;
			}

			return FormatBotResponse(completeSuccess ? Strings.Success : Strings.Done);
		}

		private static async Task<string?> ResponseUnpackBoosters(ulong steamID, string botNames) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(botNames)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(botNames));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseUnpackBoosters(steamID))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result))!);

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private static async Task<string?> ResponseUpdate(ulong steamID) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
				throw new ArgumentNullException(nameof(steamID));
			}

			if (!ASF.IsOwner(steamID)) {
				return null;
			}

			(bool success, string? message, Version? version) = await Actions.Update().ConfigureAwait(false);

			return FormatStaticResponse((success ? Strings.Success : Strings.WarningFailed) + (!string.IsNullOrEmpty(message) ? " " + message : version != null ? " " + version : ""));
		}

		private string? ResponseVersion(ulong steamID) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
				throw new ArgumentNullException(nameof(steamID));
			}

			return Bot.HasPermission(steamID, BotConfig.EPermission.Operator) ? FormatBotResponse(string.Format(Strings.BotVersion, SharedInfo.ASF, SharedInfo.Version)) : null;
		}

		private string? ResponseWalletBalance(ulong steamID) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
				throw new ArgumentNullException(nameof(steamID));
			}

			if (!Bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}

			return !Bot.IsConnectedAndLoggedOn ? FormatBotResponse(Strings.BotNotConnected) : FormatBotResponse(Bot.WalletCurrency != ECurrencyCode.Invalid ? string.Format(Strings.BotWalletBalance, Bot.WalletBalance / 100.0, Bot.WalletCurrency.ToString()) : Strings.BotHasNoWallet);
		}

		private static async Task<string?> ResponseWalletBalance(ulong steamID, string botNames) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || string.IsNullOrEmpty(botNames)) {
				throw new ArgumentNullException(nameof(steamID) + " || " + nameof(botNames));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseWalletBalance(steamID)))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result))!);

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		[Flags]
		private enum ERedeemFlags : ushort {
			None = 0,
			Validate = 1,
			ForceForwarding = 2,
			SkipForwarding = 4,
			ForceDistributing = 8,
			SkipDistributing = 16,
			SkipInitial = 32,
			ForceKeepMissingGames = 64,
			SkipKeepMissingGames = 128,
			ForceAssumeWalletKeyOnBadActivationCode = 256,
			SkipAssumeWalletKeyOnBadActivationCode = 512
		}
	}
}
