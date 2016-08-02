namespace GUI {
	sealed partial class BotStatusForm {
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
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(BotStatusForm));
			this.AvatarPictureBox = new System.Windows.Forms.PictureBox();
			((System.ComponentModel.ISupportInitialize)(this.AvatarPictureBox)).BeginInit();
			this.SuspendLayout();
			// 
			// AvatarPictureBox
			// 
			this.AvatarPictureBox.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
			this.AvatarPictureBox.ErrorImage = ((System.Drawing.Image)(resources.GetObject("AvatarPictureBox.ErrorImage")));
			this.AvatarPictureBox.Image = ((System.Drawing.Image)(resources.GetObject("AvatarPictureBox.Image")));
			this.AvatarPictureBox.Location = new System.Drawing.Point(12, 12);
			this.AvatarPictureBox.Name = "AvatarPictureBox";
			this.AvatarPictureBox.Size = new System.Drawing.Size(184, 184);
			this.AvatarPictureBox.TabIndex = 0;
			this.AvatarPictureBox.TabStop = false;
			this.AvatarPictureBox.LoadCompleted += new System.ComponentModel.AsyncCompletedEventHandler(this.AvatarPictureBox_LoadCompleted);
			// 
			// BotStatusForm
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.AutoScroll = true;
			this.ClientSize = new System.Drawing.Size(651, 513);
			this.Controls.Add(this.AvatarPictureBox);
			this.DoubleBuffered = true;
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
			this.Name = "BotStatusForm";
			this.Text = "BotStatusForm";
			((System.ComponentModel.ISupportInitialize)(this.AvatarPictureBox)).EndInit();
			this.ResumeLayout(false);

		}

		#endregion

		internal System.Windows.Forms.PictureBox AvatarPictureBox;
	}
}