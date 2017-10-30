namespace dIRCd
{
	partial class DebugWindow
	{
		/// <summary>
		/// Required designer variable.
		/// </summary>
		private System.ComponentModel.IContainer components = null;

		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose(bool disposing)
		{
			if (disposing && (components != null))
			{
				components.Dispose();
			}
			base.Dispose(disposing);
		}

		#region Windows Form Designer generated code

		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			this.components = new System.ComponentModel.Container();
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(DebugWindow));
			this.TrayIcon = new System.Windows.Forms.NotifyIcon(this.components);
			this.output = new System.Windows.Forms.RichTextBox();
			this.Button1 = new System.Windows.Forms.Button();
			this.LogLevelSelector = new System.Windows.Forms.ComboBox();
			this.LogLevelLabel = new System.Windows.Forms.Label();
			this.SuspendLayout();
			// 
			// TrayIcon
			// 
			this.TrayIcon.Icon = ((System.Drawing.Icon)(resources.GetObject("TrayIcon.Icon")));
			this.TrayIcon.Text = "dIRC";
			this.TrayIcon.Visible = true;
			this.TrayIcon.MouseDoubleClick += new System.Windows.Forms.MouseEventHandler(this.TrayIcon_MouseDoubleClick);
			// 
			// output
			// 
			this.output.BackColor = System.Drawing.SystemColors.InactiveCaption;
			this.output.CausesValidation = false;
			this.output.Cursor = System.Windows.Forms.Cursors.Default;
			this.output.Location = new System.Drawing.Point(12, 12);
			this.output.Name = "output";
			this.output.ScrollBars = System.Windows.Forms.RichTextBoxScrollBars.Vertical;
			this.output.Size = new System.Drawing.Size(984, 677);
			this.output.TabIndex = 0;
			this.output.TabStop = false;
			this.output.Text = "";
			// 
			// Button1
			// 
			this.Button1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this.Button1.Location = new System.Drawing.Point(467, 695);
			this.Button1.Name = "Button1";
			this.Button1.Size = new System.Drawing.Size(75, 23);
			this.Button1.TabIndex = 2;
			this.Button1.Text = "stop";
			this.Button1.UseVisualStyleBackColor = true;
			this.Button1.Click += new System.EventHandler(this.Button1_Click);
			// 
			// LogLevelSelector
			// 
			this.LogLevelSelector.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.LogLevelSelector.FormattingEnabled = true;
			this.LogLevelSelector.Items.AddRange(new object[] {
            "Critical",
            "Error",
            "Warning",
            "Info",
            "Verbose",
            "Debug"});
			this.LogLevelSelector.Location = new System.Drawing.Point(875, 697);
			this.LogLevelSelector.MaxDropDownItems = 5;
			this.LogLevelSelector.Name = "LogLevelSelector";
			this.LogLevelSelector.Size = new System.Drawing.Size(121, 21);
			this.LogLevelSelector.TabIndex = 3;
			this.LogLevelSelector.SelectionChangeCommitted += new System.EventHandler(this.LogLevelSelector_SelectionChangeCommitted);
			// 
			// LogLevelLabel
			// 
			this.LogLevelLabel.AutoSize = true;
			this.LogLevelLabel.Location = new System.Drawing.Point(815, 700);
			this.LogLevelLabel.Name = "LogLevelLabel";
			this.LogLevelLabel.Size = new System.Drawing.Size(54, 13);
			this.LogLevelLabel.TabIndex = 4;
			this.LogLevelLabel.Text = "Log Level";
			// 
			// DebugWindow
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(1008, 730);
			this.Controls.Add(this.LogLevelLabel);
			this.Controls.Add(this.LogLevelSelector);
			this.Controls.Add(this.Button1);
			this.Controls.Add(this.output);
			this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
			this.Name = "DebugWindow";
			this.Text = "dIRC";
			this.Shown += new System.EventHandler(this.DebugWindow_Shown);
			this.Resize += new System.EventHandler(this.DebugWindow_Resize);
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.NotifyIcon TrayIcon;
		private System.Windows.Forms.RichTextBox output;
		private System.Windows.Forms.Button Button1;
		private System.Windows.Forms.ComboBox LogLevelSelector;
		private System.Windows.Forms.Label LogLevelLabel;
	}
}

