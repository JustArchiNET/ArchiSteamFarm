using ArchiSteamFarm.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ArchiSteamFarm {
	internal sealed class Commands {
		//Because this dictionary maps to async functions we have to make sync functions async with lambdas; e.g. VERSION
		//Because for some commands there exist versions with AND without parameters we have to use lambdas to adapt function-interfaces sometimes; e.g. STOP
		private static readonly Dictionary<(string Command, byte ArgumentCount), Func<Bot, ulong, string[], Task<string>>> CommandDictionary = new Dictionary<(string Command, byte arguments), Func<Bot, ulong, string[], Task<string>>>() {
			{ ("EXIT", 0), async (bot, steamID, args) => await Task.Run(() => ResponseExit(steamID)).ConfigureAwait(false) },
			{ ("STOP", 0), async (bot, steamID, args) => await Task.Run(() => ResponseStop(bot, steamID)).ConfigureAwait(false) },
			{ ("UPDATE", 0), ResponseUpdate },
			{ ("VERSION", 0), async (bot, steamID, args) => await Task.Run(() => ResponseVersion(bot, steamID)).ConfigureAwait(false) },

			{ ("STOP", 1), async (bot, steamID, args) => await ResponseStop(steamID, Utilities.GetArgsAsText(args, 0, ",")).ConfigureAwait(false) }
		};

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

			string[] messageParts = message.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

			if (messageParts.Length == 0) {
				ASF.ArchiLogger.LogNullError(nameof(messageParts));
				return null;
			}

			string command = messageParts[0];
			string[] arguments = new string[messageParts.Length - 1];
			Array.Copy(messageParts, 1, arguments, 0, arguments.Length);

			if (CommandDictionary.TryGetValue((command.ToUpperInvariant(), (byte)arguments.Length), out Func<Bot, ulong, string[], Task<string>> func)) {
				string response = await func(bot, steamID, arguments).ConfigureAwait(false);

				return response;
			}

			return null;
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

		private static async Task<string> ResponseStop(ulong steamID, string botNames) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames));
				return null;
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);
			if (bots == null || bots.Count == 0) {
				return Bot.IsOwner(steamID) ? FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<Task<string>> tasks = bots.Select(bot => Task.Run(() => ResponseStop(bot, steamID)));
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

		private static async Task<string> ResponseUpdate(Bot bot, ulong steamID, string[] args) {
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
			if (steamID == 0) {
				ASF.ArchiLogger.LogNullError(nameof(steamID));
				return null;
			}

			if (bot.IsOperator(steamID)) {
				return FormatBotResponse(bot, string.Format(Strings.BotVersion, SharedInfo.ASF, SharedInfo.Version));
			}

			return null;
		}

		/*
		
		private static async Task<string> Response(Bot bot, ulong steamID, string[] args) {

		}
		//*/
	}
}
