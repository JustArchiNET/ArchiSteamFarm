using System.Linq;
using System.Threading.Tasks;
using SteamKit2;

namespace ArchiSteamFarm {
	internal static class Events {
		internal static void OnBotShutdown() {
			if (Program.ShutdownSequenceInitialized || Program.WCF.IsServerRunning() || Bot.Bots.Values.Any(bot => bot.KeepRunning)) {
				return;
			}

			Logging.LogGenericInfo("No bots are running, exiting");
			Task.Delay(5000).Wait();
			Program.Shutdown();
		}

		internal static void OnStateUpdated(Bot bot, SteamFriends.PersonaStateCallback callback) {
		}
	}
}
