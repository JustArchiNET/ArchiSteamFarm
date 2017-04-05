using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace ArchiSteamFarm {
	internal sealed partial class MainForm : Form {
		private static readonly ConcurrentDictionary<string, int> BotIndexes = new ConcurrentDictionary<string, int>();

		private static MainForm Form;
		private string PreviouslySelectedBotName;

		internal MainForm() {
			Form = this;
			InitializeComponent();
		}

		internal static void UpdateBotAvatar(string botName, Image image) {
			if (string.IsNullOrEmpty(botName) || (image == null)) {
				ASF.ArchiLogger.LogNullError(nameof(botName) + " || " + nameof(image));
				return;
			}

			if (Form == null) {
				return;
			}

			int index;
			if (!BotIndexes.TryGetValue(botName, out index)) {
				return;
			}

			Bitmap resizedImage = ResizeImage(image, Form.AvatarImageList.ImageSize.Width, Form.AvatarImageList.ImageSize.Height);
			if (resizedImage == null) {
				return;
			}

			Form.Invoke((MethodInvoker) (() => {
				Form.AvatarImageList.Images[index] = resizedImage;
				Form.BotListView.Refresh();
			}));
		}

		private void BotListView_SelectedIndexChanged(object sender, EventArgs e) {
			if (!string.IsNullOrEmpty(PreviouslySelectedBotName)) {
				BotStatusForm.BotForms[PreviouslySelectedBotName].Visible = false;
			}

			if (BotListView.SelectedItems.Count == 0) {
				return;
			}

			PreviouslySelectedBotName = BotListView.SelectedItems[0].Text;
			BotStatusForm.BotForms[PreviouslySelectedBotName].Visible = true;
		}

		private async void MainForm_FormClosed(object sender, FormClosedEventArgs e) => await Program.InitShutdownSequence().ConfigureAwait(false);

		private async void MainForm_Load(object sender, EventArgs e) {
			BotListView.LargeImageList = BotListView.SmallImageList = AvatarImageList;

			Program.InitCore();
			Logging.InitFormLogger();
			await Program.InitASF(); // No ConfigureAwait, we need GUI thread

			foreach (KeyValuePair<string, Bot> bot in Bot.Bots) {
				BotStatusForm botStatusForm = new BotStatusForm(bot.Value);

				BotIndexes[bot.Key] = AvatarImageList.Images.Count;

				AvatarImageList.Images.Add(bot.Key, botStatusForm.AvatarPictureBox.Image);

				botStatusForm.TopLevel = false;
				BotStatusPanel.Controls.Add(botStatusForm);

				ListViewItem botListViewItem = new ListViewItem {
					ImageIndex = BotIndexes[bot.Key],
					Text = bot.Key
				};

				BotListView.Items.Add(botListViewItem);
			}

			if (BotListView.Items.Count <= 0) {
				return;
			}

			BotListView.Items[0].Selected = true;
			BotListView.Select();
		}

		private void MainForm_Resize(object sender, EventArgs e) {
			switch (WindowState) {
				case FormWindowState.Minimized:
					MinimizeIcon.Visible = true;
					MinimizeIcon.ShowBalloonTip(5000);
					break;
				case FormWindowState.Normal:
					MinimizeIcon.Visible = false;
					break;
			}
		}

		private void MinimizeIcon_DoubleClick(object sender, EventArgs e) {
			Show();
			WindowState = FormWindowState.Normal;
		}

		private static Bitmap ResizeImage(Image image, int width, int height) {
			if ((image == null) || (width <= 0) || (height <= 0)) {
				ASF.ArchiLogger.LogNullError(nameof(image) + " || " + nameof(width) + " || " + nameof(height));
				return null;
			}

			Rectangle destRect = new Rectangle(0, 0, width, height);
			Bitmap destImage = new Bitmap(width, height);

			destImage.SetResolution(image.HorizontalResolution, image.VerticalResolution);

			using (Graphics graphics = Graphics.FromImage(destImage)) {
				graphics.CompositingMode = CompositingMode.SourceCopy;
				graphics.CompositingQuality = CompositingQuality.HighQuality;
				graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
				graphics.SmoothingMode = SmoothingMode.HighQuality;
				graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

				using (ImageAttributes wrapMode = new ImageAttributes()) {
					wrapMode.SetWrapMode(WrapMode.TileFlipXY);
					graphics.DrawImage(image, destRect, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, wrapMode);
				}
			}

			return destImage;
		}
	}
}