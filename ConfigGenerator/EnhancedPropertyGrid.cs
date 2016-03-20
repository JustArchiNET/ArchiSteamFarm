using System.Windows.Forms;

namespace ConfigGenerator {
	internal sealed class EnhancedPropertyGrid : PropertyGrid {
		private ASFConfig ASFConfig;
		internal EnhancedPropertyGrid(ASFConfig config) : base() {
			if (config == null) {
				return;
			}

			ASFConfig = config;

			SelectedObject = config;
			Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
			Dock = DockStyle.Fill;
			HelpVisible = false;
			ToolbarVisible = false;
		}

		protected override void OnPropertyValueChanged(PropertyValueChangedEventArgs e) {
			base.OnPropertyValueChanged(e);
			ASFConfig.Save();
		}
	}
}
