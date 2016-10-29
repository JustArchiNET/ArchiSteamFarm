using System.Linq;
using System.Threading.Tasks;
using SteamKit2;

namespace ArchiSteamFarm {
	internal static class Events {
		internal static void OnStateUpdated(Bot bot, SteamFriends.PersonaStateCallback callback) {
		}

		internal static async void OnBotShutdown() {
			if (Program.IsWCFRunning || Bot.Bots.Values.Any(bot => bot.KeepRunning)) {
				return;
			}

			Logging.LogGenericInfo("No bots are running, exiting");
			await Task.Delay(5000).ConfigureAwait(false);
			Program.Exit();
		}
	}
}
