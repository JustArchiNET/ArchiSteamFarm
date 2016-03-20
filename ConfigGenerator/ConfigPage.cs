using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ConfigGenerator {
	internal class ConfigPage : TabPage {

		private EnhancedPropertyGrid EnhancedPropertyGrid;
		private Button LoadButton, SaveButton;

		internal ConfigPage(ASFConfig config) : base() {
			if (config == null) {
				return;
			}

			Text = Path.GetFileNameWithoutExtension(config.FilePath);

			EnhancedPropertyGrid = new EnhancedPropertyGrid(config);
			Controls.Add(EnhancedPropertyGrid);

			Panel panel = new Panel() {
				Height = 20,
				Dock = DockStyle.Bottom,
			};

			LoadButton = new Button() {
				Dock = DockStyle.Left,
				Text = "Load"
			};

			panel.Controls.Add(LoadButton);

			SaveButton = new Button() {
				Dock = DockStyle.Right,
				Text = "Save"
			};

			SaveButton.Click += SaveButton_Click;

			panel.Controls.Add(SaveButton);

			Controls.Add(panel);
		}

		private async void SaveButton_Click(object sender, EventArgs e) {
			if (sender == null || e == null) {
				return;
			}

			SaveButton.Enabled = false;

			List<Task> tasks = new List<Task>(ASFConfig.ASFConfigs.Count);
			foreach (ASFConfig config in ASFConfig.ASFConfigs) {
				tasks.Add(Task.Run(() => config.Save()));
				config.Save();
			}
			await Task.WhenAll(tasks);

			SaveButton.Enabled = true;
		}

		private void InitializeComponent() {
			SuspendLayout();
			ResumeLayout(false);
		}
	}
}
