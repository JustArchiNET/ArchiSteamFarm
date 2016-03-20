using System.Windows.Forms;

namespace ConfigGenerator {
	internal sealed class EnhancedPropertyGrid : PropertyGrid {
		private GlobalConfig GlobalConfig;
		internal EnhancedPropertyGrid(GlobalConfig globalConfig) : base() {
			if (globalConfig == null) {
				return;
			}

			GlobalConfig = globalConfig;

			SelectedObject = globalConfig;
			Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
			Dock = DockStyle.Fill;
			HelpVisible = false;
			ToolbarVisible = false;
		}
	}
}
