namespace ConfigGenerator {
	partial class MainForm {
		/// <summary>
		/// Required designer variable.
		/// </summary>
		private System.ComponentModel.IContainer components = null;

		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose(bool disposing) {
			if (disposing && (components != null)) {
				components.Dispose();
			}
			base.Dispose(disposing);
		}

		#region Windows Form Designer generated code

		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent() {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            this.MainTab = new System.Windows.Forms.TabControl();
            this.button_update_all_configs = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // MainTab
            // 
            this.MainTab.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.MainTab.Appearance = System.Windows.Forms.TabAppearance.Buttons;
            this.MainTab.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(238)));
            this.MainTab.HotTrack = true;
            this.MainTab.Location = new System.Drawing.Point(14, 48);
            this.MainTab.Margin = new System.Windows.Forms.Padding(4);
            this.MainTab.Multiline = true;
            this.MainTab.Name = "MainTab";
            this.MainTab.SelectedIndex = 0;
            this.MainTab.Size = new System.Drawing.Size(854, 711);
            this.MainTab.SizeMode = System.Windows.Forms.TabSizeMode.Fixed;
            this.MainTab.TabIndex = 1;
            this.MainTab.Selected += new System.Windows.Forms.TabControlEventHandler(this.MainTab_Selected);
            this.MainTab.Deselecting += new System.Windows.Forms.TabControlCancelEventHandler(this.MainTab_Deselecting);
            // 
            // button_update_all_configs
            // 
            this.button_update_all_configs.Location = new System.Drawing.Point(741, 12);
            this.button_update_all_configs.Name = "button_update_all_configs";
            this.button_update_all_configs.Size = new System.Drawing.Size(127, 23);
            this.button_update_all_configs.TabIndex = 2;
            this.button_update_all_configs.Text = "Update all configs";
            this.button_update_all_configs.UseVisualStyleBackColor = true;
            this.button_update_all_configs.Click += new System.EventHandler(this.button_update_all_configs_Click);
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoScroll = true;
            this.ClientSize = new System.Drawing.Size(882, 774);
            this.Controls.Add(this.button_update_all_configs);
            this.Controls.Add(this.MainTab);
            this.Cursor = System.Windows.Forms.Cursors.Default;
            this.DoubleBuffered = true;
            this.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(238)));
            this.HelpButton = true;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Margin = new System.Windows.Forms.Padding(4);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "ASF Config Generator";
            this.HelpButtonClicked += new System.ComponentModel.CancelEventHandler(this.MainForm_HelpButtonClicked);
            this.Load += new System.EventHandler(this.MainForm_Load);
            this.Shown += new System.EventHandler(this.MainForm_Shown);
            this.ResumeLayout(false);

		}

		#endregion
		private System.Windows.Forms.TabControl MainTab;
        private System.Windows.Forms.Button button_update_all_configs;
    }
}

