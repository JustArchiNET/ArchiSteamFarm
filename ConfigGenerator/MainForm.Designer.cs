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
			this.MenuPanel = new System.Windows.Forms.MenuStrip();
			this.FileMenu = new System.Windows.Forms.ToolStripMenuItem();
			this.FileMenuHelp = new System.Windows.Forms.ToolStripMenuItem();
			this.FileMenuExit = new System.Windows.Forms.ToolStripMenuItem();
			this.MainTab = new System.Windows.Forms.TabControl();
			this.MenuPanel.SuspendLayout();
			this.SuspendLayout();
			// 
			// MenuPanel
			// 
			this.MenuPanel.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.FileMenu});
			this.MenuPanel.Location = new System.Drawing.Point(0, 0);
			this.MenuPanel.Name = "MenuPanel";
			this.MenuPanel.Padding = new System.Windows.Forms.Padding(8, 3, 0, 3);
			this.MenuPanel.Size = new System.Drawing.Size(780, 25);
			this.MenuPanel.TabIndex = 0;
			this.MenuPanel.Text = "menuStrip1";
			// 
			// FileMenu
			// 
			this.FileMenu.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.FileMenuHelp,
            this.FileMenuExit});
			this.FileMenu.Name = "FileMenu";
			this.FileMenu.Size = new System.Drawing.Size(37, 19);
			this.FileMenu.Text = "File";
			// 
			// FileMenuHelp
			// 
			this.FileMenuHelp.Name = "FileMenuHelp";
			this.FileMenuHelp.Size = new System.Drawing.Size(152, 22);
			this.FileMenuHelp.Text = "Help";
			this.FileMenuHelp.Click += new System.EventHandler(this.FileMenuHelp_Click);
			// 
			// FileMenuExit
			// 
			this.FileMenuExit.Name = "FileMenuExit";
			this.FileMenuExit.Size = new System.Drawing.Size(152, 22);
			this.FileMenuExit.Text = "Exit";
			this.FileMenuExit.Click += new System.EventHandler(this.FileMenuExit_Click);
			// 
			// MainTab
			// 
			this.MainTab.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this.MainTab.HotTrack = true;
			this.MainTab.Location = new System.Drawing.Point(16, 33);
			this.MainTab.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
			this.MainTab.Multiline = true;
			this.MainTab.Name = "MainTab";
			this.MainTab.SelectedIndex = 0;
			this.MainTab.Size = new System.Drawing.Size(748, 509);
			this.MainTab.TabIndex = 1;
			this.MainTab.Selected += new System.Windows.Forms.TabControlEventHandler(this.MainTab_Selected);
			this.MainTab.Deselecting += new System.Windows.Forms.TabControlCancelEventHandler(this.MainTab_Deselecting);
			// 
			// MainForm
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.AutoScroll = true;
			this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.ClientSize = new System.Drawing.Size(780, 557);
			this.Controls.Add(this.MainTab);
			this.Controls.Add(this.MenuPanel);
			this.DoubleBuffered = true;
			this.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(238)));
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.Fixed3D;
			this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
			this.MainMenuStrip = this.MenuPanel;
			this.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
			this.Name = "MainForm";
			this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
			this.Text = "ASF Config Generator";
			this.Load += new System.EventHandler(this.MainForm_Load);
			this.MenuPanel.ResumeLayout(false);
			this.MenuPanel.PerformLayout();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.MenuStrip MenuPanel;
		private System.Windows.Forms.TabControl MainTab;
		private System.Windows.Forms.ToolStripMenuItem FileMenu;
		private System.Windows.Forms.ToolStripMenuItem FileMenuHelp;
		private System.Windows.Forms.ToolStripMenuItem FileMenuExit;
	}
}

