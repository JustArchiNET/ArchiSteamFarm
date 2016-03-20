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
			this.MenuPanel = new System.Windows.Forms.MenuStrip();
			this.FileMenu = new System.Windows.Forms.ToolStripMenuItem();
			this.FileMenuHelp = new System.Windows.Forms.ToolStripMenuItem();
			this.BotMenu = new System.Windows.Forms.ToolStripMenuItem();
			this.BotMenuNew = new System.Windows.Forms.ToolStripMenuItem();
			this.BotMenuDelete = new System.Windows.Forms.ToolStripMenuItem();
			this.MainTab = new System.Windows.Forms.TabControl();
			this.FileMenuExit = new System.Windows.Forms.ToolStripMenuItem();
			this.MenuPanel.SuspendLayout();
			this.SuspendLayout();
			// 
			// MenuPanel
			// 
			this.MenuPanel.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.FileMenu,
            this.BotMenu});
			this.MenuPanel.Location = new System.Drawing.Point(0, 0);
			this.MenuPanel.Name = "MenuPanel";
			this.MenuPanel.Size = new System.Drawing.Size(784, 24);
			this.MenuPanel.TabIndex = 0;
			this.MenuPanel.Text = "menuStrip1";
			// 
			// FileMenu
			// 
			this.FileMenu.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.FileMenuHelp,
            this.FileMenuExit});
			this.FileMenu.Name = "FileMenu";
			this.FileMenu.Size = new System.Drawing.Size(37, 20);
			this.FileMenu.Text = "File";
			// 
			// FileMenuHelp
			// 
			this.FileMenuHelp.Name = "FileMenuHelp";
			this.FileMenuHelp.Size = new System.Drawing.Size(152, 22);
			this.FileMenuHelp.Text = "Help";
			this.FileMenuHelp.Click += new System.EventHandler(this.FileMenuHelp_Click);
			// 
			// BotMenu
			// 
			this.BotMenu.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.BotMenuNew,
            this.BotMenuDelete});
			this.BotMenu.Name = "BotMenu";
			this.BotMenu.Size = new System.Drawing.Size(37, 20);
			this.BotMenu.Text = "Bot";
			// 
			// BotMenuNew
			// 
			this.BotMenuNew.Name = "BotMenuNew";
			this.BotMenuNew.Size = new System.Drawing.Size(107, 22);
			this.BotMenuNew.Text = "New";
			this.BotMenuNew.Click += new System.EventHandler(this.BotMenuNew_Click);
			// 
			// BotMenuDelete
			// 
			this.BotMenuDelete.Name = "BotMenuDelete";
			this.BotMenuDelete.Size = new System.Drawing.Size(107, 22);
			this.BotMenuDelete.Text = "Delete";
			this.BotMenuDelete.Click += new System.EventHandler(this.BotMenuDelete_Click);
			// 
			// MainTab
			// 
			this.MainTab.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this.MainTab.Location = new System.Drawing.Point(12, 27);
			this.MainTab.Name = "MainTab";
			this.MainTab.SelectedIndex = 0;
			this.MainTab.Size = new System.Drawing.Size(760, 522);
			this.MainTab.TabIndex = 1;
			// 
			// FileMenuExit
			// 
			this.FileMenuExit.Name = "FileMenuExit";
			this.FileMenuExit.Size = new System.Drawing.Size(152, 22);
			this.FileMenuExit.Text = "Exit";
			this.FileMenuExit.Click += new System.EventHandler(this.FileMenuExit_Click);
			// 
			// MainForm
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(784, 561);
			this.Controls.Add(this.MainTab);
			this.Controls.Add(this.MenuPanel);
			this.MainMenuStrip = this.MenuPanel;
			this.Name = "MainForm";
			this.Text = "Form1";
			this.Load += new System.EventHandler(this.MainForm_Load);
			this.MenuPanel.ResumeLayout(false);
			this.MenuPanel.PerformLayout();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.MenuStrip MenuPanel;
		private System.Windows.Forms.ToolStripMenuItem BotMenu;
		private System.Windows.Forms.ToolStripMenuItem BotMenuNew;
		private System.Windows.Forms.ToolStripMenuItem BotMenuDelete;
		private System.Windows.Forms.TabControl MainTab;
		private System.Windows.Forms.ToolStripMenuItem FileMenu;
		private System.Windows.Forms.ToolStripMenuItem FileMenuHelp;
		private System.Windows.Forms.ToolStripMenuItem FileMenuExit;
	}
}

