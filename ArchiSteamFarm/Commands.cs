using ArchiSteamFarm.Json;
using ArchiSteamFarm.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ArchiSteamFarm {
	internal sealed class Commands {
		private static string FormatBotResponse(Bot bot, string response) {
			if (bot == null || string.IsNullOrEmpty(response)) {
				ASF.ArchiLogger.LogNullError(nameof(bot) + " || " + nameof(response));
				return null;
			}

			return "<" + bot.BotName + "> " + response;
		}

		private static string FormatStaticResponse(string response) {
			if (string.IsNullOrEmpty(response)) {
				ASF.ArchiLogger.LogNullError(nameof(response));
				return null;
			}

			return "<" + SharedInfo.ASF + "> " + response;
		}
		
		internal static async Task<string> Parse(Bot bot, ulong steamID, string message) {
			if (bot == null || steamID == 0 || string.IsNullOrEmpty(message)) {
				ASF.ArchiLogger.LogNullError(nameof(bot) + " || " + nameof(steamID) + " || " + nameof(message));
				return null;
			}

			if (!string.IsNullOrEmpty(Program.GlobalConfig.CommandPrefix)) {
				if (!message.StartsWith(Program.GlobalConfig.CommandPrefix, StringComparison.Ordinal)) {
					return null;
				}

				message = message.Substring(Program.GlobalConfig.CommandPrefix.Length);
			}

			string[] args = message.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

			if(args.Length == 0) {
				ASF.ArchiLogger.LogNullError(nameof(args));
				return null;
			}

			switch (args[0].ToUpperInvariant()) {
				case "2FA":
					return await Response2FA(bot, steamID, args).ConfigureAwait(false);
				case "2FANO":
					return await Response2FANO(bot, steamID, args).ConfigureAwait(false);
				case "2FAOK":
					return await Response2FAOK(bot, steamID, args).ConfigureAwait(false);
				case "EXIT":
					return ResponseExit(steamID);
				case "SA":
					return await ResponseSA(bot, steamID).ConfigureAwait(false);
				case "STATUS":
					return await ResponseStatus(bot, steamID, args).ConfigureAwait(false);
				case "STOP":
					return await ResponseStop(bot, steamID, args).ConfigureAwait(false);
				case "UNPACK":
					return await ResponseUnpack(bot, steamID, args).ConfigureAwait(false);
				case "UPDATE":
					return await ResponseUpdate(steamID).ConfigureAwait(false);
				case "VERSION":
					return ResponseVersion(bot, steamID);
				default:
					return ResponseUnknown(bot, steamID);
			}
		}

		private static async Task<string> Response2FA(Bot bot, ulong steamID, string[] args) {
			if (bot == null || steamID == 0 || args == null) {
				ASF.ArchiLogger.LogNullError(nameof(bot) + " || " + nameof(steamID) + " || " + nameof(args));
				return null;
			}

			if (args.Length == 0) {
				return await Response2FA(bot, steamID).ConfigureAwait(false);
			}

			string botNames = Utilities.GetArgsAsText(args, 0, ",");
			HashSet<Bot> bots = Bot.GetBots(botNames);
			if (bots == null || bots.Count == 0) {
				if (Bot.IsOwner(steamID)) {
					return FormatStaticResponse(string.Format(Strings.BotNotFound, botNames));
				}

				return null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(singleBot => Response2FA(singleBot, steamID));
			ICollection<string> results;

			switch (Program.GlobalConfig.OptimizationMode) {
				case GlobalConfig.EOptimizationMode.MinMemoryUsage:
					results = new List<string>(bots.Count);
					foreach(Task<string> currentTask in tasks) {
						results.Add(await currentTask.ConfigureAwait(false));
					}

					break;
				default:
					results = await Task.WhenAll(tasks).ConfigureAwait(false);
					break;
			}

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));
			if (responses.Count > 0) {
				return string.Join(Environment.NewLine, responses);
			}

			return null;
		}

		private static async Task<string> Response2FA(Bot bot, ulong steamID) {
			if (bot == null || steamID == 0) {
				ASF.ArchiLogger.LogNullError(nameof(bot) + " || " + nameof(steamID));
				return null;
			}

			if (!bot.IsMaster(steamID)) {
				return null;
			}

			if (!bot.HasMobileAuthenticator) {
				return FormatBotResponse(bot, Strings.BotNoASFAuthenticator);
			}

			string token = await bot.MobileAuthenticator.GenerateToken().ConfigureAwait(false);
			return FormatBotResponse(bot, !string.IsNullOrEmpty(token) ? string.Format(Strings.BotAuthenticatorToken, token) : Strings.WarningFailed);
		}

		private static async Task<string> Response2FANO(Bot bot, ulong steamID, string[] args) => await Response2FAConfirm(bot, steamID, args, false).ConfigureAwait(false);

		private static async Task<string> Response2FAOK(Bot bot, ulong steamID, string[] args) => await Response2FAConfirm(bot, steamID, args, true).ConfigureAwait(false);

		private static async Task<string> Response2FAConfirm(Bot bot, ulong steamID, string[] args, bool confirm) {
			if (bot == null || steamID == 0 || args == null) {
				ASF.ArchiLogger.LogNullError(nameof(bot) + " || " + nameof(steamID) + " || " + nameof(args));
				return null;
			}

			if (args.Length == 0) {
				return await Response2FAConfirm(bot, steamID, confirm).ConfigureAwait(false);
			}

			string botNames = Utilities.GetArgsAsText(args, 0, ",");
			HashSet<Bot> bots = Bot.GetBots(botNames);
			if (bots == null || bots.Count == 0) {
				if (Bot.IsOwner(steamID)) {
					return FormatStaticResponse(string.Format(Strings.BotNotFound, botNames));
				}

				return null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(singleBot => Response2FAConfirm(singleBot, steamID, confirm));
			ICollection<string> results;

			switch (Program.GlobalConfig.OptimizationMode) {
				case GlobalConfig.EOptimizationMode.MinMemoryUsage:
					results = new List<string>(bots.Count);
					foreach (Task<string> currentTask in tasks) {
						results.Add(await currentTask.ConfigureAwait(false));
					}

					break;
				default:
					results = await Task.WhenAll(tasks).ConfigureAwait(false);
					break;
			}

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));
			if (responses.Count > 0) {
				return string.Join(Environment.NewLine, responses);
			}

			return null;
		}

		private static async Task<string> Response2FAConfirm(Bot bot, ulong steamID, bool confirm) {
			if (bot == null || steamID == 0) {
				ASF.ArchiLogger.LogNullError(nameof(bot) + " || " + nameof(steamID));
				return null;
			}

			if (!bot.IsMaster(steamID)) {
				return null;
			}

			if (!bot.IsConnectedAndLoggedOn) {
				return FormatBotResponse(bot, Strings.BotNotConnected);
			}

			if (!bot.HasMobileAuthenticator) {
				return FormatBotResponse(bot, Strings.BotNoASFAuthenticator);
			}

			if (await bot.AcceptConfirmations(confirm).ConfigureAwait(false)) {
				return FormatBotResponse(bot, Strings.Success);
			}

			return FormatBotResponse(bot, Strings.WarningFailed);
		}

		private static string ResponseExit(ulong steamID) {
			if (steamID == 0) {
				ASF.ArchiLogger.LogNullError(nameof(steamID));
				return null;
			}

			if (!Bot.IsOwner(steamID)) {
				return null;
			}

			Utilities.InBackground(
				async () => {
					await Task.Delay(1000).ConfigureAwait(false);
					await Program.Exit().ConfigureAwait(false);
				}
			);

			return FormatStaticResponse(Strings.Done);
		}

		private static async Task<string> ResponsePassword(Bot bot, ulong steamID, string[] args) {
			if(bot == null || steamID == 0 || args == null) {
				ASF.ArchiLogger.LogNullError(nameof(bot) + " || " + nameof(steamID) + " || " + nameof(args));
				return null;
			}

			if (args.Length == 0) {
				return ResponsePassword(bot, steamID);
			}

			string botNames = Utilities.GetArgsAsText(args, 0, ",");
			HashSet<Bot> bots = Bot.GetBots(botNames);
			if(bots == null || bots.Count == 0) {
				if (Bot.IsOwner(steamID)) {
					return FormatStaticResponse(string.Format(Strings.BotNotFound, botNames));
				}

				return null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(singleBot => Task.Run(() => ResponsePassword(singleBot, steamID)));
			ICollection<string> results;

			switch (Program.GlobalConfig.OptimizationMode) {
				case GlobalConfig.EOptimizationMode.MinMemoryUsage:
					results = new List<string>(bots.Count);
					foreach(Task<string> currentTask in tasks) {
						results.Add(await currentTask.ConfigureAwait(false));
					}

					break;
				default:
					results = await Task.WhenAll(tasks).ConfigureAwait(false);
					break;
			}

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));
			if(responses.Count > 0) {
				return string.Join(Environment.NewLine, responses);
			}

			return null;
		}

		private static string ResponsePassword(Bot bot, ulong steamID) {
			if (bot == null || steamID == 0) {
				ASF.ArchiLogger.LogNullError(nameof(bot) + " || " + nameof(steamID));
				return null;
			}

			if (!bot.IsMaster(steamID)) {
				return null;
			}

			if (string.IsNullOrEmpty(bot.BotConfig.SteamPassword)) {
				return FormatBotResponse(bot, string.Format(Strings.ErrorIsEmpty, nameof(BotConfig.SteamPassword)));
			}

			return FormatBotResponse(bot, string.Format(Strings.BotEncryptedPassword, ArchiCryptoHelper.ECryptoMethod.AES, ArchiCryptoHelper.Encrypt(ArchiCryptoHelper.ECryptoMethod.AES, bot.BotConfig.SteamPassword))) + FormatBotResponse(bot, string.Format(Strings.BotEncryptedPassword, ArchiCryptoHelper.ECryptoMethod.ProtectedDataForCurrentUser, ArchiCryptoHelper.Encrypt(ArchiCryptoHelper.ECryptoMethod.ProtectedDataForCurrentUser, bot.BotConfig.SteamPassword)));
		}

		private static async Task<string> ResponseSA(Bot bot, ulong steamID) => await ResponseStatus(bot, steamID, new string[] { SharedInfo.ASF }).ConfigureAwait(false);

		private static async Task<string> ResponseStatus(Bot bot, ulong steamID, string[] args) {
			if (bot == null || steamID == 0 || args == null) {
				ASF.ArchiLogger.LogNullError(nameof(bot) + " || " + nameof(steamID) + " || " + nameof(args));
				return null;
			}

			if (args.Length == 0) {
				return ResponseStatus(bot, steamID);
			}

			string botNames = Utilities.GetArgsAsText(args, 0, ",");
			HashSet<Bot> bots = Bot.GetBots(botNames);
			if (bots == null || bots.Count == 0) {
				if (Bot.IsOwner(steamID)) {
					return FormatStaticResponse(string.Format(Strings.BotNotFound, botNames));
				}

				return null;
			}

			IEnumerable<(Bot Bot, Task<string> Task)> tasks = bots.Select(singleBot => (singleBot, Task.Run(() => ResponseStatus(singleBot, steamID))));
			ICollection<(Bot Bot, string Response)> results;

			switch (Program.GlobalConfig.OptimizationMode) {
				case GlobalConfig.EOptimizationMode.MinMemoryUsage:
					results = new List<(Bot Bot, string Response)>(bots.Count);
					foreach ((Bot currentBot, Task<string> currentTask) in tasks) {
						results.Add((currentBot, await currentTask.ConfigureAwait(false)));
					}

					break;
				default:
					results = await Task.WhenAll(tasks.Select(async task => (task.Bot, await task.Task.ConfigureAwait(false)))).ConfigureAwait(false);
					break;
			}

			List<(Bot Bot, string Response)> validResults = new List<(Bot Bot, string Response)>(results.Where(result => !string.IsNullOrEmpty(result.Response)));
			if (validResults.Count == 0) {
				return null;
			}

			HashSet<Bot> botsRunning = validResults.Where(result => result.Bot.KeepRunning).Select(result => result.Bot).ToHashSet();

			string extraResponse = string.Format(Strings.BotStatusOverview, botsRunning.Count, validResults.Count, botsRunning.Sum(singleBot => singleBot.CardsFarmer.GamesToFarm.Count), botsRunning.Sum(singleBot => singleBot.CardsFarmer.GamesToFarm.Sum(game => game.CardsRemaining)));
			return string.Join(Environment.NewLine, validResults.Select(result => result.Response).Union(extraResponse.ToEnumerable()));
		}

		private static string ResponseStatus(Bot bot, ulong steamID) {
			if (bot == null || steamID == 0) {
				ASF.ArchiLogger.LogNullError(nameof(bot) + " || " + nameof(steamID));
				return null;
			}

			if (!bot.IsFamilySharing(steamID)) {
				return null;
			}

			if (!bot.IsConnectedAndLoggedOn) {
				return FormatBotResponse(bot, bot.KeepRunning ? Strings.BotStatusConnecting : Strings.BotStatusNotRunning);
			}

			if (bot.PlayingBlocked) {
				return FormatBotResponse(bot, Strings.BotStatusPlayingNotAvailable);
			}

			if (bot.CardsFarmer.Paused) {
				return FormatBotResponse(bot, Strings.BotStatusPaused);
			}

			if (bot.IsAccountLimited) {
				return FormatBotResponse(bot, Strings.BotStatusLimited);
			}

			if (bot.IsAccountLocked) {
				return FormatBotResponse(bot, Strings.BotStatusLocked);
			}

			if (!bot.CardsFarmer.NowFarming || bot.CardsFarmer.CurrentGamesFarming.Count == 0) {
				return FormatBotResponse(bot, Strings.BotStatusNotIdling);
			}

			if (bot.CardsFarmer.CurrentGamesFarming.Count > 1) {
				return FormatBotResponse(bot, string.Format(Strings.BotStatusIdlingList, string.Join(", ", bot.CardsFarmer.CurrentGamesFarming.Select(game => game.AppID + " (" + game.GameName + ")")), bot.CardsFarmer.GamesToFarm.Count, bot.CardsFarmer.GamesToFarm.Sum(game => game.CardsRemaining), bot.CardsFarmer.TimeRemaining.ToHumanReadable()));
			}

			CardsFarmer.Game soloGame = bot.CardsFarmer.CurrentGamesFarming.First();
			return FormatBotResponse(bot, string.Format(Strings.BotStatusIdling, soloGame.AppID, soloGame.GameName, soloGame.CardsRemaining, bot.CardsFarmer.GamesToFarm.Count, bot.CardsFarmer.GamesToFarm.Sum(game => game.CardsRemaining), bot.CardsFarmer.TimeRemaining.ToHumanReadable()));
		}

		private static async Task<string> ResponseStop(Bot bot, ulong steamID, string[] args) {
			if (bot == null || steamID == 0 || args == null) {
				ASF.ArchiLogger.LogNullError(nameof(bot) + " || " + nameof(steamID) + " || " + nameof(args));
				return null;
			}

			if (args.Length == 0) {
				return ResponseStop(bot, steamID);
			}

			string botNames = Utilities.GetArgsAsText(args, 0, ",");
			HashSet<Bot> bots = Bot.GetBots(botNames);
			if (bots == null || bots.Count == 0) {
				if (Bot.IsOwner(steamID)) {
					return FormatStaticResponse(string.Format(Strings.BotNotFound, botNames));
				}

				return null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(singleBot => Task.Run(() => ResponseStop(singleBot, steamID)));
			ICollection<string> results;

			switch (Program.GlobalConfig.OptimizationMode) {
				case GlobalConfig.EOptimizationMode.MinMemoryUsage:
					results = new List<string>(bots.Count);
					foreach (Task<string> task in tasks) {
						results.Add(await task.ConfigureAwait(false));
					}

					break;
				default:
					results = await Task.WhenAll(tasks).ConfigureAwait(false);
					break;
			}

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));
			if (responses.Count > 0) {
				return string.Join(Environment.NewLine, responses);
			}

			return null;
		}

		private static string ResponseStop(Bot bot, ulong steamID) {
			if (bot == null || steamID == 0) {
				ASF.ArchiLogger.LogNullError(nameof(bot) + " || " + nameof(steamID));
				return null;
			}

			if (!bot.IsMaster(steamID)) {
				return null;
			}

			if (!bot.KeepRunning) {
				return FormatBotResponse(bot, Strings.BotAlreadyStopped);
			}

			bot.Stop();
			return FormatBotResponse(bot, Strings.Done);
		}

		private static string ResponseUnknown(Bot bot, ulong steamID) {
			if (bot == null || steamID == 0) {
				ASF.ArchiLogger.LogNullError(nameof(bot) + " || " + nameof(steamID));
				return null;
			}

			if (bot.IsOperator(steamID)) {
				return FormatBotResponse(bot, Strings.UnknownCommand);
			}

			return null;
		}

		private static async Task<string> ResponseUnpack(Bot bot, ulong steamID, string[] args) {
			if (bot == null || steamID == 0 || args == null) {
				ASF.ArchiLogger.LogNullError(nameof(bot) + " || " + nameof(steamID) + " || " + nameof(args));
				return null;
			}

			if (args.Length == 0) {
				return await ResponseUnpack(bot, steamID).ConfigureAwait(false);
			}

			string botNames = Utilities.GetArgsAsText(args, 0, ",");
			HashSet<Bot> bots = Bot.GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				if (Bot.IsOwner(steamID)) {
					return FormatStaticResponse(string.Format(Strings.BotNotFound, botNames));
				}

				return null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(singleBot => ResponseUnpack(singleBot, steamID));
			ICollection<string> results;

			switch (Program.GlobalConfig.OptimizationMode) {
				case GlobalConfig.EOptimizationMode.MinMemoryUsage:
					results = new List<string>(bots.Count);
					foreach (Task<string> task in tasks) {
						results.Add(await task.ConfigureAwait(false));
					}

					break;
				default:
					results = await Task.WhenAll(tasks).ConfigureAwait(false);
					break;
			}

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));
			if(responses.Count > 0) {
				return string.Join(Environment.NewLine, responses);
			}

			return null;
		}

		private static async Task<string> ResponseUnpack(Bot bot, ulong steamID) {
			if (bot == null || steamID == 0) {
				ASF.ArchiLogger.LogNullError(nameof(bot) + " || " + nameof(steamID));
				return null;
			}

			if (!bot.IsMaster(steamID)) {
				return null;
			}

			if (!bot.IsConnectedAndLoggedOn) {
				return FormatBotResponse(bot, Strings.BotNotConnected);
			}

			HashSet<Steam.Asset> inventory = await bot.ArchiWebHandler.GetInventory(bot.CachedSteamID, wantedTypes: new HashSet<Steam.Asset.EType> { Steam.Asset.EType.BoosterPack }).ConfigureAwait(false);
			if (inventory == null || inventory.Count == 0) {
				return FormatBotResponse(bot, string.Format(Strings.ErrorIsEmpty, nameof(inventory)));
			}

			// It'd make sense here to actually check return code of ArchiWebHandler.UnpackBooster(), but it lies most of the time | https://github.com/JustArchi/ArchiSteamFarm/issues/704
			// It'd also make sense to run all of this in parallel, but it seems that Steam has a lot of problems with inventory-related parallel requests | https://steamcommunity.com/groups/ascfarm/discussions/1/3559414588264550284/
			foreach (Steam.Asset item in inventory) {
				await bot.ArchiWebHandler.UnpackBooster(item.RealAppID, item.AssetID).ConfigureAwait(false);
			}

			return FormatBotResponse(bot, Strings.Done);
		}

		private static async Task<string> ResponseUpdate(ulong steamID) {
			if (steamID == 0) {
				ASF.ArchiLogger.LogNullError(nameof(steamID));
				return null;
			}

			if (!Bot.IsOwner(steamID)) {
				return null;
			}

			Version version = await ASF.CheckAndUpdateProgram(true).ConfigureAwait(false);
			return FormatStaticResponse(version != null ? (version > SharedInfo.Version ? Strings.Success : Strings.Done) : Strings.WarningFailed);
		}

		private static string ResponseVersion(Bot bot, ulong steamID) {
			if (bot == null || steamID == 0) {
				ASF.ArchiLogger.LogNullError(nameof(bot) + " || " + nameof(steamID));
				return null;
			}

			if (bot.IsOperator(steamID)) {
				return FormatBotResponse(bot, string.Format(Strings.BotVersion, SharedInfo.ASF, SharedInfo.Version));
			}

			return null;
		}
	}
}

