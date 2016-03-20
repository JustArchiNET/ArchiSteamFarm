using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ConfigGenerator {
	internal class ConfigPage : TabPage {

		internal readonly ASFConfig ASFConfig;

		private EnhancedPropertyGrid EnhancedPropertyGrid;

		internal ConfigPage(ASFConfig config) : base() {
			if (config == null) {
				return;
			}

			ASFConfig = config;

			RefreshText();

			EnhancedPropertyGrid = new EnhancedPropertyGrid(config);
			Controls.Add(EnhancedPropertyGrid);
		}

		internal void RefreshText() {
			Text = Path.GetFileNameWithoutExtension(ASFConfig.FilePath);
		}

		private void InitializeComponent() {
			SuspendLayout();
			ResumeLayout(false);
		}
	}
}
