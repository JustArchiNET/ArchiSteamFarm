using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ArchiSteamFarm {
	internal sealed class Commands
    {
		private static readonly Dictionary<(string Command, byte ArgumentCount), Func<Bot, ulong, string[], Task<string>>> CommandDictionary = new Dictionary<(string Command, byte arguments), Func<Bot, ulong, string[], Task<string>>>() {
			
		};

		internal static async Task<string> Parse(Bot bot, ulong steamID, string message) {
			if(bot == null || steamID == 0 || string.IsNullOrEmpty(message)) {
				ASF.ArchiLogger.LogNullError(nameof(bot) + " || " + nameof(steamID) + " || " + nameof(message));
				return null;
			}

			if (!string.IsNullOrEmpty(Program.GlobalConfig.CommandPrefix)) {
				if(!message.StartsWith(Program.GlobalConfig.CommandPrefix, StringComparison.Ordinal)) {
					return null;
				}

				message = message.Substring(Program.GlobalConfig.CommandPrefix.Length);
			}

			string[] messageParts = message.Split((char[]) null, StringSplitOptions.RemoveEmptyEntries);

			if(messageParts.Length == 0) {
				ASF.ArchiLogger.LogNullError(nameof(messageParts));
				return null;
			}

			string command = messageParts[0];
			string[] arguments = new string[messageParts.Length - 1];
			Array.Copy(messageParts, 1, arguments, 0, arguments.Length);

			if (CommandDictionary.TryGetValue((command.ToUpperInvariant(), (byte) arguments.Length), out Func<Bot, ulong, string[], Task<string>> func)) {
				string response = await func(bot, steamID, arguments).ConfigureAwait(false);
				if(response == null) {
					ASF.ArchiLogger.LogNullError(nameof(response));
					return null;
				}

				return response;
			}

			ASF.ArchiLogger.LogNullError(nameof(func));
			return null;
		}
    }
}
