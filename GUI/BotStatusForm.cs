using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Windows.Forms;
using ArchiSteamFarm;
using SteamKit2;

namespace GUI {
	internal sealed partial class BotStatusForm : Form {
		internal static readonly ConcurrentDictionary<string, BotStatusForm> BotForms = new ConcurrentDictionary<string, BotStatusForm>();

		private readonly Bot Bot;

		internal BotStatusForm(Bot bot) {
			if (bot == null) {
				throw new ArgumentNullException(nameof(bot));
			}

			Bot = bot;

			BotForms[bot.BotName] = this;

			InitializeComponent();

			Dock = DockStyle.Fill;
		}

		internal void OnStateUpdated(SteamFriends.PersonaStateCallback callback) {
			if (callback == null) {
				Bot.ArchiLogger.LogNullError(nameof(callback));
				return;
			}

			if ((callback.AvatarHash == null) || (callback.AvatarHash.Length == 0)) {
				return;
			}

			string avatarHash = BitConverter.ToString(callback.AvatarHash).Replace("-", "").ToLowerInvariant();
			if (string.IsNullOrEmpty(avatarHash)) {
				return;
			}

			string avatarURL = "https://steamcdn-a.akamaihd.net/steamcommunity/public/images/avatars/" + avatarHash.Substring(0, 2) + "/" + avatarHash + "_full.jpg";
			AvatarPictureBox.ImageLocation = avatarURL;
			AvatarPictureBox.LoadAsync();
		}

		private void AvatarPictureBox_LoadCompleted(object sender, AsyncCompletedEventArgs e) {
			MainForm.UpdateBotAvatar(Bot.BotName, AvatarPictureBox.Image);
		}
	}
}