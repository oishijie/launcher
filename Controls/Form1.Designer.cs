namespace launcher.Controls
{
    partial class Launcher
    {
        /// <summary>
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                if (_animationTimer != null)
                {
                    _animationTimer.Dispose();
                }
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码

        /// <summary>
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Launcher));
            this.btnClose = new System.Windows.Forms.Button();
            this.panel1 = new System.Windows.Forms.Panel();
            this.notifyIcon1 = new System.Windows.Forms.NotifyIcon(this.components);
            this.contextMenuStrip1 = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.exitToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.btnOpenFolder = new System.Windows.Forms.Button();
            this.label2 = new System.Windows.Forms.Label();
            this.btnPinTop = new System.Windows.Forms.Button();
            this.btnNotes = new System.Windows.Forms.Button();
            this.panel2 = new System.Windows.Forms.Panel();
            this.textBox1 = new System.Windows.Forms.TextBox();
            this.rtbNotesPreview = new System.Windows.Forms.RichTextBox();
            this.btnMdToggle = new System.Windows.Forms.Button();
            this.contextMenuStrip1.SuspendLayout();
            this.panel2.SuspendLayout();
            this.SuspendLayout();
            // 
            // btnClose
            // 
            this.btnClose.FlatAppearance.BorderColor = System.Drawing.Color.Black;
            this.btnClose.FlatAppearance.BorderSize = 0;
            this.btnClose.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnClose.Font = new System.Drawing.Font("Webdings", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(2)));
            this.btnClose.ForeColor = System.Drawing.Color.Crimson;
            this.btnClose.Location = new System.Drawing.Point(522, -2);
            this.btnClose.Name = "btnClose";
            this.btnClose.Size = new System.Drawing.Size(30, 30);
            this.btnClose.TabIndex = 33;
            this.btnClose.Text = "x";
            this.btnClose.UseVisualStyleBackColor = true;
            this.btnClose.Click += new System.EventHandler(this.exitbutton_Click);
            // 
            // panel1
            // 
            this.panel1.BackColor = System.Drawing.Color.LightGray;
            this.panel1.Location = new System.Drawing.Point(250, 0);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(55, 1);
            this.panel1.TabIndex = 34;
            // 
            // notifyIcon1
            // 
            this.notifyIcon1.BalloonTipIcon = System.Windows.Forms.ToolTipIcon.Info;
            this.notifyIcon1.ContextMenuStrip = this.contextMenuStrip1;
            this.notifyIcon1.Icon = ((System.Drawing.Icon)(resources.GetObject("notifyIcon1.Icon")));
            this.notifyIcon1.Text = "Launcher";
            this.notifyIcon1.Visible = true;
            this.notifyIcon1.MouseClick += new System.Windows.Forms.MouseEventHandler(this.notifyIcon1_MouseClick);
            // 
            // contextMenuStrip1
            // 
            this.contextMenuStrip1.Name = "contextMenuStrip1";
            this.contextMenuStrip1.Size = new System.Drawing.Size(149, 26);
            // 
            // themeToolStripMenuItem
            // 
            this.themeToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.themeToolStripMenuItem.Name = "themeToolStripMenuItem";
            this.themeToolStripMenuItem.Size = new System.Drawing.Size(148, 22);
            this.themeToolStripMenuItem.Text = "切换深浅色主题(&T)";
            this.themeToolStripMenuItem.Click += new System.EventHandler(this.themeToolStripMenuItem_Click);
            this.settingsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.settingsToolStripMenuItem.Name = "settingsToolStripMenuItem";
            this.settingsToolStripMenuItem.Size = new System.Drawing.Size(148, 22);
            this.settingsToolStripMenuItem.Text = "编辑配置(&E)";
            this.settingsToolStripMenuItem.Click += new System.EventHandler(this.settingsToolStripMenuItem_Click);
            // 
            // autostartToolStripMenuItem
            // 
            this.autostartToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.autostartToolStripMenuItem.Name = "autostartToolStripMenuItem";
            this.autostartToolStripMenuItem.Size = new System.Drawing.Size(148, 22);
            this.autostartToolStripMenuItem.Text = "开机自启(&R)";
            this.autostartToolStripMenuItem.CheckOnClick = true;
            this.autostartToolStripMenuItem.Click += new System.EventHandler(this.autostartToolStripMenuItem_Click);
            // 
            // exportConfigToolStripMenuItem
            // 
            this.exportConfigToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.exportConfigToolStripMenuItem.Name = "exportConfigToolStripMenuItem";
            this.exportConfigToolStripMenuItem.Size = new System.Drawing.Size(148, 22);
            this.exportConfigToolStripMenuItem.Text = "导出配置(&X)";
            this.exportConfigToolStripMenuItem.Click += new System.EventHandler(this.exportConfigToolStripMenuItem_Click);
            // 
            // importConfigToolStripMenuItem
            // 
            this.importConfigToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.importConfigToolStripMenuItem.Name = "importConfigToolStripMenuItem";
            this.importConfigToolStripMenuItem.Size = new System.Drawing.Size(148, 22);
            this.importConfigToolStripMenuItem.Text = "导入配置(&I)";
            this.importConfigToolStripMenuItem.Click += new System.EventHandler(this.importConfigToolStripMenuItem_Click);
            // 
            // aboutToolStripMenuItem
            // 
            this.aboutToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.aboutToolStripMenuItem.Name = "aboutToolStripMenuItem";
            this.aboutToolStripMenuItem.Size = new System.Drawing.Size(148, 22);
            this.aboutToolStripMenuItem.Text = "关于(&A)";
            this.aboutToolStripMenuItem.Click += new System.EventHandler(this.aboutToolStripMenuItem_Click);
            //
            // addPageToolStripMenuItem
            //
            this.addPageToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.addPageToolStripMenuItem.Name = "addPageToolStripMenuItem";
            this.addPageToolStripMenuItem.Size = new System.Drawing.Size(148, 22);
            this.addPageToolStripMenuItem.Text = "新增一页(&P)";
            this.addPageToolStripMenuItem.Click += new System.EventHandler(this.addPageToolStripMenuItem_Click);
            //
            // renamePageToolStripMenuItem
            //
            this.renamePageToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.renamePageToolStripMenuItem.Name = "renamePageToolStripMenuItem";
            this.renamePageToolStripMenuItem.Size = new System.Drawing.Size(148, 22);
            this.renamePageToolStripMenuItem.Text = "重命名当前页(&R)";
            this.renamePageToolStripMenuItem.Click += new System.EventHandler(this.renamePageToolStripMenuItem_Click);
            //
            // removePageToolStripMenuItem
            //
            this.removePageToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.removePageToolStripMenuItem.Name = "removePageToolStripMenuItem";
            this.removePageToolStripMenuItem.Size = new System.Drawing.Size(148, 22);
            this.removePageToolStripMenuItem.Text = "删除当前页(&D)";
            this.removePageToolStripMenuItem.Click += new System.EventHandler(this.removePageToolStripMenuItem_Click);
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(145, 6);
            // 
            // exitToolStripMenuItem
            // 
            this.exitToolStripMenuItem.Name = "exitToolStripMenuItem";
            this.exitToolStripMenuItem.Size = new System.Drawing.Size(148, 22);
            this.exitToolStripMenuItem.Text = "&Quit（退出）";
            this.exitToolStripMenuItem.Click += new System.EventHandler(this.exitToolStripMenuItem_Click);
            // 
            // 托盘菜单项（先加主题切换，再加退出）
            // 
            this.contextMenuStrip1.Items.Add(this.themeToolStripMenuItem);
            this.contextMenuStrip1.Items.Add(this.settingsToolStripMenuItem);
            this.contextMenuStrip1.Items.Add(this.autostartToolStripMenuItem);
            this.contextMenuStrip1.Items.Add(this.exportConfigToolStripMenuItem);
            this.contextMenuStrip1.Items.Add(this.importConfigToolStripMenuItem);
            this.contextMenuStrip1.Items.Add(this.addPageToolStripMenuItem);
            this.contextMenuStrip1.Items.Add(this.renamePageToolStripMenuItem);
            this.contextMenuStrip1.Items.Add(this.removePageToolStripMenuItem);
            this.contextMenuStrip1.Items.Add(this.aboutToolStripMenuItem);
            this.contextMenuStrip1.Items.Add(this.toolStripSeparator1);
            this.contextMenuStrip1.Items.Add(this.exitToolStripMenuItem);
            // 
            // btnOpenFolder
            // 
            this.btnOpenFolder.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnOpenFolder.FlatAppearance.BorderColor = System.Drawing.SystemColors.ActiveCaptionText;
            this.btnOpenFolder.FlatAppearance.BorderSize = 0;
            this.btnOpenFolder.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnOpenFolder.Font = new System.Drawing.Font("Segoe Script", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnOpenFolder.ForeColor = System.Drawing.Color.MediumPurple;
            this.btnOpenFolder.Image = ((System.Drawing.Image)(resources.GetObject("button2.Image")));
            this.btnOpenFolder.Location = new System.Drawing.Point(2, 1);
            this.btnOpenFolder.Name = "btnOpenFolder";
            this.btnOpenFolder.Size = new System.Drawing.Size(23, 22);
            this.btnOpenFolder.TabIndex = 36;
            this.btnOpenFolder.UseVisualStyleBackColor = true;
            this.btnOpenFolder.Click += new System.EventHandler(this.btnOpenFolder_Click);
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.label2.ForeColor = System.Drawing.SystemColors.ActiveBorder;
            this.label2.Location = new System.Drawing.Point(22, 5);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(64, 17);
            this.label2.TabIndex = 37;
            this.label2.Text = "Launcher";
            // 
            // btnPinTop
            // 
            this.btnPinTop.FlatAppearance.BorderColor = System.Drawing.SystemColors.ActiveCaptionText;
            this.btnPinTop.FlatAppearance.BorderSize = 0;
            this.btnPinTop.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnPinTop.Font = new System.Drawing.Font("Segoe Script", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnPinTop.ForeColor = System.Drawing.Color.Yellow;
            this.btnPinTop.Location = new System.Drawing.Point(496, -2);
            this.btnPinTop.Name = "btnPinTop";
            this.btnPinTop.Size = new System.Drawing.Size(30, 30);
            this.btnPinTop.TabIndex = 46;
            this.btnPinTop.Text = "📌 ";
            this.btnPinTop.UseVisualStyleBackColor = true;
            this.btnPinTop.Click += new System.EventHandler(this.btnPinTop_Click);
            // 
            // btnNotes
            // 
            this.btnNotes.FlatAppearance.BorderColor = System.Drawing.SystemColors.ActiveCaptionText;
            this.btnNotes.FlatAppearance.BorderSize = 0;
            this.btnNotes.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnNotes.Font = new System.Drawing.Font("Segoe Script", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnNotes.ForeColor = System.Drawing.Color.Gray;
            this.btnNotes.Location = new System.Drawing.Point(467, -2);
            this.btnNotes.Name = "btnNotes";
            this.btnNotes.Size = new System.Drawing.Size(30, 30);
            this.btnNotes.TabIndex = 47;
            this.btnNotes.Text = "🖍";
            this.btnNotes.UseVisualStyleBackColor = true;
            this.btnNotes.Click += new System.EventHandler(this.btnNotes_Click);
            // 
            // btnTheme (深浅色切换)
            // 
            this.btnTheme = new System.Windows.Forms.Button();
            this.btnTheme.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnTheme.FlatAppearance.BorderColor = System.Drawing.SystemColors.ActiveCaptionText;
            this.btnTheme.FlatAppearance.BorderSize = 0;
            this.btnTheme.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnTheme.Font = new System.Drawing.Font("Segoe UI Symbol", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnTheme.ForeColor = System.Drawing.SystemColors.ControlText;
            this.btnTheme.Location = new System.Drawing.Point(409, -2);
            this.btnTheme.Name = "btnTheme";
            this.btnTheme.Size = new System.Drawing.Size(30, 30);
            this.btnTheme.TabIndex = 49;
            this.btnTheme.Text = "🌓";
            this.btnTheme.UseVisualStyleBackColor = true;
            this.btnTheme.Click += new System.EventHandler(this.btnTheme_Click);
            // 
            // searchBox（顶部搜索框：实时过滤图标，回车打开首个匹配）
            // 
            this.searchBox = new System.Windows.Forms.TextBox();
            this.searchBox.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.searchBox.Location = new System.Drawing.Point(95, 4);
            this.searchBox.Name = "searchBox";
            this.searchBox.Size = new System.Drawing.Size(300, 22);
            this.searchBox.TabIndex = 50;
            this.searchBox.TabStop = false;
            this.searchBox.Text = "搜索…";
            this.searchBox.TextChanged += new System.EventHandler(this.SearchBox_TextChanged);
            this.searchBox.Enter += new System.EventHandler(this.SearchBox_Enter);
            this.searchBox.Leave += new System.EventHandler(this.SearchBox_Leave);
            // 
            // panel2
            // 
            this.panel2.Controls.Add(this.btnMdToggle);
            this.panel2.Controls.Add(this.rtbNotesPreview);
            this.panel2.Controls.Add(this.textBox1);
            this.panel2.Location = new System.Drawing.Point(2, 370);
            this.panel2.Name = "panel2";
            this.panel2.Size = new System.Drawing.Size(552, 334);
            this.panel2.TabIndex = 48;
            this.panel2.Visible = false;
            // 
            // textBox1
            // 
            this.textBox1.BackColor = System.Drawing.Color.Black;
            this.textBox1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.textBox1.Font = new System.Drawing.Font("微软雅黑", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.textBox1.ForeColor = System.Drawing.SystemColors.MenuBar;
            this.textBox1.Location = new System.Drawing.Point(12, 10);
            this.textBox1.Multiline = true;
            this.textBox1.Name = "textBox1";
            this.textBox1.Size = new System.Drawing.Size(526, 314);
            this.textBox1.TabIndex = 0;
            this.textBox1.Text = "便签内容自动保存，关闭软件后不丢失";
            // 
            // rtbNotesPreview
            // 
            this.rtbNotesPreview.BackColor = System.Drawing.Color.Black;
            this.rtbNotesPreview.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.rtbNotesPreview.DetectUrls = false;
            this.rtbNotesPreview.Font = new System.Drawing.Font("微软雅黑", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.rtbNotesPreview.ForeColor = System.Drawing.SystemColors.MenuBar;
            this.rtbNotesPreview.Location = new System.Drawing.Point(12, 10);
            this.rtbNotesPreview.Name = "rtbNotesPreview";
            this.rtbNotesPreview.ReadOnly = true;
            this.rtbNotesPreview.Size = new System.Drawing.Size(526, 314);
            this.rtbNotesPreview.TabIndex = 1;
            this.rtbNotesPreview.Text = "";
            this.rtbNotesPreview.Visible = false;
            this.rtbNotesPreview.LinkClicked += new System.Windows.Forms.LinkClickedEventHandler(this.rtbNotesPreview_LinkClicked);
            // 
            // btnMdToggle
            // 
            this.btnMdToggle.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(40)))), ((int)(((byte)(40)))), ((int)(((byte)(40)))));
            this.btnMdToggle.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(70)))), ((int)(((byte)(70)))), ((int)(((byte)(70)))));
            this.btnMdToggle.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnMdToggle.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnMdToggle.ForeColor = System.Drawing.Color.Silver;
            this.btnMdToggle.Location = new System.Drawing.Point(468, 12);
            this.btnMdToggle.Name = "btnMdToggle";
            this.btnMdToggle.Size = new System.Drawing.Size(68, 26);
            this.btnMdToggle.TabIndex = 2;
            this.btnMdToggle.Text = "预览";
            this.btnMdToggle.UseVisualStyleBackColor = false;
            this.btnMdToggle.Click += new System.EventHandler(this.btnMdToggle_Click);
            // 
            // Launcher
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.Black;
            this.ClientSize = new System.Drawing.Size(550, 374);
            this.Controls.Add(this.panel2);
            this.Controls.Add(this.searchBox);
            this.Controls.Add(this.btnNotes);
            this.Controls.Add(this.btnPinTop);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.btnOpenFolder);
            this.Controls.Add(this.btnClose);
            this.Controls.Add(this.btnTheme);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.KeyPreview = true;
            this.Name = "Launcher";
            this.Opacity = 0.94D;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Launcher";
            // 默认不置顶：失焦(点击窗口外)时主面板自动隐藏，符合 dock 启动器习惯；
            // 需要常驻可点标题栏图钉按钮临时置顶(此时不自动隐藏)。
            this.TopMost = false;
            this.Load += new System.EventHandler(this.Launcher_Load);
            this.KeyDown += new System.Windows.Forms.KeyEventHandler(this.Launcher_KeyDown);
            this.contextMenuStrip1.ResumeLayout(false);
            this.panel2.ResumeLayout(false);
            this.panel2.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button btnClose;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.NotifyIcon notifyIcon1;
        private System.Windows.Forms.ContextMenuStrip contextMenuStrip1;
        private System.Windows.Forms.ToolStripMenuItem exitToolStripMenuItem;
        private System.Windows.Forms.Button btnOpenFolder;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Button btnPinTop;
        private System.Windows.Forms.Button btnNotes;
        private System.Windows.Forms.Button btnTheme;
        private System.Windows.Forms.Panel panel2;
        private System.Windows.Forms.TextBox textBox1;
        private System.Windows.Forms.RichTextBox rtbNotesPreview;
        private System.Windows.Forms.Button btnMdToggle;
        private System.Windows.Forms.TextBox searchBox;
        private System.Windows.Forms.ToolStripMenuItem themeToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem settingsToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem autostartToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem exportConfigToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem importConfigToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem aboutToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem addPageToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem renamePageToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem removePageToolStripMenuItem;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
    }
}
