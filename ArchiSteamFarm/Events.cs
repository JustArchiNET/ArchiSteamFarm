using System.Linq;
using SteamKit2;

namespace ArchiSteamFarm {
	internal static class Events {
		internal static void OnStateUpdated(Bot bot, SteamFriends.PersonaStateCallback callback) {
		}

		internal static void OnBotShutdown() {
			if (Program.IsWCFRunning || Bot.Bots.Values.Any(bot => bot.KeepRunning)) {
				return;
			}

			Logging.LogGenericInfo("No bots are running, exiting");
			Program.Exit();
		}
	}
}
