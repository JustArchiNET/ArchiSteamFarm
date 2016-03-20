using System.IO;
using System.Windows.Forms;

namespace ConfigGenerator {
	internal sealed class GlobalConfigPage : TabPage {

		internal GlobalConfig GlobalConfig { get; private set; }

		private EnhancedPropertyGrid EnhancedPropertyGrid;

		internal GlobalConfigPage(string filePath) : base() {
			if (string.IsNullOrEmpty(filePath)) {
				return;
			}

			GlobalConfig = GlobalConfig.Load(filePath);
			if (GlobalConfig == null) {
				Logging.LogNullError("GlobalConfig");
				return;
			}

			Text = Path.GetFileNameWithoutExtension(filePath);

			EnhancedPropertyGrid = new EnhancedPropertyGrid(GlobalConfig);
			Controls.Add(EnhancedPropertyGrid);

			Panel panel = new Panel() {
				Height = 20,
				Dock = DockStyle.Bottom,
			};

			panel.Controls.Add(new Button() {
				Dock = DockStyle.Left,
				Text = "Load"
			});

			panel.Controls.Add(new Button() {
				Dock = DockStyle.Right,
				Text = "Save"
			});

			Controls.Add(panel);
		}

		private void InitializeComponent() {
			this.SuspendLayout();
			this.ResumeLayout(false);

		}
	}
}
