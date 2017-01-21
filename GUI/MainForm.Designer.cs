namespace ArchiSteamFarm {
	internal sealed partial class MainForm {
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
			this.components = new System.ComponentModel.Container();
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
			this.BotListView = new System.Windows.Forms.ListView();
			this.menuStrip1 = new System.Windows.Forms.MenuStrip();
			this.MinimizeIcon = new System.Windows.Forms.NotifyIcon(this.components);
			this.LogTextBox = new System.Windows.Forms.RichTextBox();
			this.BotStatusPanel = new System.Windows.Forms.Panel();
			this.AvatarImageList = new System.Windows.Forms.ImageList(this.components);
			this.SuspendLayout();
			// 
			// BotListView
			// 
			this.BotListView.Dock = System.Windows.Forms.DockStyle.Left;
			this.BotListView.GridLines = true;
			this.BotListView.Location = new System.Drawing.Point(0, 24);
			this.BotListView.MultiSelect = false;
			this.BotListView.Name = "BotListView";
			this.BotListView.ShowGroups = false;
			this.BotListView.Size = new System.Drawing.Size(150, 705);
			this.BotListView.TabIndex = 0;
			this.BotListView.UseCompatibleStateImageBehavior = false;
			this.BotListView.View = System.Windows.Forms.View.SmallIcon;
			this.BotListView.SelectedIndexChanged += new System.EventHandler(this.BotListView_SelectedIndexChanged);
			// 
			// menuStrip1
			// 
			this.menuStrip1.Location = new System.Drawing.Point(0, 0);
			this.menuStrip1.Name = "menuStrip1";
			this.menuStrip1.Size = new System.Drawing.Size(1008, 24);
			this.menuStrip1.TabIndex = 1;
			this.menuStrip1.Text = "menuStrip1";
			// 
			// MinimizeIcon
			// 
			this.MinimizeIcon.BalloonTipIcon = System.Windows.Forms.ToolTipIcon.Info;
			this.MinimizeIcon.BalloonTipText = "ASF will keep working in the background...";
			this.MinimizeIcon.BalloonTipTitle = "ASF";
			this.MinimizeIcon.Icon = ((System.Drawing.Icon)(resources.GetObject("MinimizeIcon.Icon")));
			this.MinimizeIcon.Text = "MinimizeIcon";
			this.MinimizeIcon.Visible = true;
			this.MinimizeIcon.DoubleClick += new System.EventHandler(this.MinimizeIcon_DoubleClick);
			// 
			// LogTextBox
			// 
			this.LogTextBox.BackColor = System.Drawing.Color.Black;
			this.LogTextBox.CausesValidation = false;
			this.LogTextBox.Dock = System.Windows.Forms.DockStyle.Bottom;
			this.LogTextBox.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(238)));
			this.LogTextBox.ForeColor = System.Drawing.Color.White;
			this.LogTextBox.Location = new System.Drawing.Point(150, 529);
			this.LogTextBox.Name = "LogTextBox";
			this.LogTextBox.ReadOnly = true;
			this.LogTextBox.Size = new System.Drawing.Size(858, 200);
			this.LogTextBox.TabIndex = 2;
			this.LogTextBox.Text = "";
			// 
			// BotStatusPanel
			// 
			this.BotStatusPanel.Dock = System.Windows.Forms.DockStyle.Top;
			this.BotStatusPanel.Location = new System.Drawing.Point(150, 24);
			this.BotStatusPanel.Name = "BotStatusPanel";
			this.BotStatusPanel.Size = new System.Drawing.Size(858, 496);
			this.BotStatusPanel.TabIndex = 3;
			// 
			// AvatarImageList
			// 
			this.AvatarImageList.ColorDepth = System.Windows.Forms.ColorDepth.Depth24Bit;
			this.AvatarImageList.ImageSize = new System.Drawing.Size(46, 46);
			this.AvatarImageList.TransparentColor = System.Drawing.Color.Transparent;
			// 
			// MainForm
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(1008, 729);
			this.Controls.Add(this.BotStatusPanel);
			this.Controls.Add(this.LogTextBox);
			this.Controls.Add(this.BotListView);
			this.Controls.Add(this.menuStrip1);
			this.DoubleBuffered = true;
			this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
			this.MainMenuStrip = this.menuStrip1;
			this.Name = "MainForm";
			this.Text = "ArchiSteamFarm";
			this.FormClosed += new System.Windows.Forms.FormClosedEventHandler(this.MainForm_FormClosed);
			this.Load += new System.EventHandler(this.MainForm_Load);
			this.Resize += new System.EventHandler(this.MainForm_Resize);
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.ListView BotListView;
		private System.Windows.Forms.MenuStrip menuStrip1;
		private System.Windows.Forms.NotifyIcon MinimizeIcon;
		private System.Windows.Forms.RichTextBox LogTextBox;
		private System.Windows.Forms.Panel BotStatusPanel;
		private System.Windows.Forms.ImageList AvatarImageList;
	}
}

