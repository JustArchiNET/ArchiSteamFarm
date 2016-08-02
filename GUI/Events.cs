using GUI;
using SteamKit2;

// ReSharper disable once CheckNamespace
namespace ArchiSteamFarm {
	internal static class Events {
		internal static void OnStateUpdated(Bot bot, SteamFriends.PersonaStateCallback callback) {
			if ((bot == null) || (callback == null)) {
				Logging.LogNullError(nameof(bot) + " || " + nameof(callback));
				return;
			}

			BotStatusForm form;
			if (!BotStatusForm.BotForms.TryGetValue(bot.BotName, out form)) {
				return;
			}

			form.OnStateUpdated(callback);
		}
	}
}
