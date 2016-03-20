using System.Windows.Forms;

namespace ConfigGenerator {
	internal sealed class EnhancedPropertyGrid : PropertyGrid {
		internal EnhancedPropertyGrid(ASFConfig config) : base() {
			if (config == null) {
				return;
			}

			SelectedObject = config;
			Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
			Dock = DockStyle.Fill;
			HelpVisible = false;
			ToolbarVisible = false;
		}
	}
}
