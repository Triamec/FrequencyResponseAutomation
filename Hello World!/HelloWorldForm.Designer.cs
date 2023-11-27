namespace Triamec.Tam.Samples {
    partial class HelloWorldForm {
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
            System.Windows.Forms.Button enableButton;
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(HelloWorldForm));
            System.Windows.Forms.Button disableButton;
            System.Windows.Forms.MenuStrip menuStrip;
            System.Windows.Forms.ToolStripMenuItem fileToolStripMenuItem;
            System.Windows.Forms.ToolStripMenuItem exitMenuItem;
            System.Windows.Forms.GroupBox motionGroupBox;
            this._measureButton = new System.Windows.Forms.Button();
            this._moveNegativeButton = new System.Windows.Forms.Button();
            this._movePositiveButton = new System.Windows.Forms.Button();
            this._toolTip = new System.Windows.Forms.ToolTip(this.components);
            this._driveGroupBox = new System.Windows.Forms.GroupBox();
            this._timer = new System.Windows.Forms.Timer(this.components);
            enableButton = new System.Windows.Forms.Button();
            disableButton = new System.Windows.Forms.Button();
            menuStrip = new System.Windows.Forms.MenuStrip();
            fileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            exitMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            motionGroupBox = new System.Windows.Forms.GroupBox();
            menuStrip.SuspendLayout();
            motionGroupBox.SuspendLayout();
            this._driveGroupBox.SuspendLayout();
            this.SuspendLayout();
            // 
            // enableButton
            // 
            resources.ApplyResources(enableButton, "enableButton");
            enableButton.Name = "enableButton";
            this._toolTip.SetToolTip(enableButton, resources.GetString("enableButton.ToolTip"));
            enableButton.UseVisualStyleBackColor = true;
            enableButton.Click += new System.EventHandler(this.OnEnableButtonClick);
            // 
            // disableButton
            // 
            resources.ApplyResources(disableButton, "disableButton");
            disableButton.Name = "disableButton";
            this._toolTip.SetToolTip(disableButton, resources.GetString("disableButton.ToolTip"));
            disableButton.UseVisualStyleBackColor = true;
            disableButton.Click += new System.EventHandler(this.OnDisableButtonClick);
            // 
            // menuStrip
            // 
            menuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            fileToolStripMenuItem});
            resources.ApplyResources(menuStrip, "menuStrip");
            menuStrip.Name = "menuStrip";
            // 
            // fileToolStripMenuItem
            // 
            fileToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            exitMenuItem});
            fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            resources.ApplyResources(fileToolStripMenuItem, "fileToolStripMenuItem");
            // 
            // exitMenuItem
            // 
            exitMenuItem.Name = "exitMenuItem";
            resources.ApplyResources(exitMenuItem, "exitMenuItem");
            exitMenuItem.Click += new System.EventHandler(this.OnExitMenuItemClick);
            // 
            // motionGroupBox
            // 
            motionGroupBox.Controls.Add(this._measureButton);
            motionGroupBox.Controls.Add(this._moveNegativeButton);
            motionGroupBox.Controls.Add(this._movePositiveButton);
            resources.ApplyResources(motionGroupBox, "motionGroupBox");
            motionGroupBox.Name = "motionGroupBox";
            motionGroupBox.TabStop = false;
            // 
            // _measureButton
            // 
            resources.ApplyResources(this._measureButton, "_measureButton");
            this._measureButton.Name = "_measureButton";
            this._toolTip.SetToolTip(this._measureButton, resources.GetString("_measureButton.ToolTip"));
            this._measureButton.UseVisualStyleBackColor = true;
            this._measureButton.Click += new System.EventHandler(this.OnMeasureButtonClick);
            // 
            // _moveNegativeButton
            // 
            resources.ApplyResources(this._moveNegativeButton, "_moveNegativeButton");
            this._moveNegativeButton.Name = "_moveNegativeButton";
            this._toolTip.SetToolTip(this._moveNegativeButton, resources.GetString("_moveNegativeButton.ToolTip"));
            this._moveNegativeButton.UseVisualStyleBackColor = true;
            this._moveNegativeButton.Click += new System.EventHandler(this.OnMoveNegativeButtonClick);
            // 
            // _movePositiveButton
            // 
            resources.ApplyResources(this._movePositiveButton, "_movePositiveButton");
            this._movePositiveButton.Name = "_movePositiveButton";
            this._toolTip.SetToolTip(this._movePositiveButton, resources.GetString("_movePositiveButton.ToolTip"));
            this._movePositiveButton.UseVisualStyleBackColor = true;
            this._movePositiveButton.Click += new System.EventHandler(this.OnMovePositiveButtonClick);
            // 
            // _driveGroupBox
            // 
            this._driveGroupBox.Controls.Add(enableButton);
            this._driveGroupBox.Controls.Add(disableButton);
            resources.ApplyResources(this._driveGroupBox, "_driveGroupBox");
            this._driveGroupBox.Name = "_driveGroupBox";
            this._driveGroupBox.TabStop = false;
            // 
            // HelloWorldForm
            // 
            resources.ApplyResources(this, "$this");
            this.Controls.Add(motionGroupBox);
            this.Controls.Add(this._driveGroupBox);
            this.Controls.Add(menuStrip);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MainMenuStrip = menuStrip;
            this.Name = "HelloWorldForm";
            menuStrip.ResumeLayout(false);
            menuStrip.PerformLayout();
            motionGroupBox.ResumeLayout(false);
            this._driveGroupBox.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button _moveNegativeButton;
        private System.Windows.Forms.ToolTip _toolTip;
        private System.Windows.Forms.Button _movePositiveButton;
        private System.Windows.Forms.GroupBox _driveGroupBox;
        private System.Windows.Forms.Timer _timer;
        private System.Windows.Forms.Button _measureButton;
    }
}

