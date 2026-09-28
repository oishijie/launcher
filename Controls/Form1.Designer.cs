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
            this.notifyIcon1 = new System.Windows.Forms.NotifyIcon(this.components);
            this.contextMenuStrip1 = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.trayToggleToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.settingsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.exitToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.searchBox = new System.Windows.Forms.TextBox();
            this.contextMenuStrip1.SuspendLayout();
            this.SuspendLayout();
            //
            // notifyIcon1
            // 图标在运行时由 LoadRuntimeAssets() 从 Resources\Launcher.ico 加载
            //
            this.notifyIcon1.BalloonTipIcon = System.Windows.Forms.ToolTipIcon.Info;
            this.notifyIcon1.ContextMenuStrip = this.contextMenuStrip1;
            this.notifyIcon1.Text = "Launcher";
            this.notifyIcon1.Visible = true;
            this.notifyIcon1.MouseClick += new System.Windows.Forms.MouseEventHandler(this.notifyIcon1_MouseClick);
            //
            // contextMenuStrip1
            // 只留必要入口：主题 / 开机自启 / 导入导出 / 分页增删改 / 关于 全部已收进设置面板，
            // 托盘不再重复列一遍 —— 11 项的长菜单本身也不好用。
            //
            this.contextMenuStrip1.Name = "contextMenuStrip1";
            this.contextMenuStrip1.Size = new System.Drawing.Size(160, 76);
            //
            // trayToggleToolStripMenuItem
            //
            this.trayToggleToolStripMenuItem.Name = "trayToggleToolStripMenuItem";
            this.trayToggleToolStripMenuItem.Size = new System.Drawing.Size(159, 22);
            this.trayToggleToolStripMenuItem.Text = "显示/隐藏面板(&H)";
            this.trayToggleToolStripMenuItem.Click += new System.EventHandler(this.trayToggleToolStripMenuItem_Click);
            //
            // settingsToolStripMenuItem
            //
            this.settingsToolStripMenuItem.Name = "settingsToolStripMenuItem";
            this.settingsToolStripMenuItem.Size = new System.Drawing.Size(159, 22);
            this.settingsToolStripMenuItem.Text = "设置(&S)…";
            this.settingsToolStripMenuItem.Click += new System.EventHandler(this.settingsToolStripMenuItem_Click);
            //
            // toolStripSeparator1
            //
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(156, 6);
            //
            // exitToolStripMenuItem
            //
            this.exitToolStripMenuItem.Name = "exitToolStripMenuItem";
            this.exitToolStripMenuItem.Size = new System.Drawing.Size(159, 22);
            this.exitToolStripMenuItem.Text = "退出(&X)";
            this.exitToolStripMenuItem.Click += new System.EventHandler(this.exitToolStripMenuItem_Click);
            //
            // 托盘菜单项
            //
            this.contextMenuStrip1.Items.Add(this.trayToggleToolStripMenuItem);
            this.contextMenuStrip1.Items.Add(this.settingsToolStripMenuItem);
            this.contextMenuStrip1.Items.Add(this.toolStripSeparator1);
            this.contextMenuStrip1.Items.Add(this.exitToolStripMenuItem);
            //
            // searchBox（顶部搜索框内核：外层由 RoundSearchBox 提供圆角外壳）
            // 保留原生 TextBox 而非全自绘 —— IME、光标、选区、拖选、复制粘贴全部零成本可用，
            // 只去掉边框就能无缝嵌进自绘圆角容器。
            //
            this.searchBox.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.searchBox.Font = new System.Drawing.Font("微软雅黑", 9.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.searchBox.Location = new System.Drawing.Point(0, 0);
            this.searchBox.Name = "searchBox";
            this.searchBox.Size = new System.Drawing.Size(100, 21);
            this.searchBox.TabIndex = 50;
            this.searchBox.TabStop = false;
            this.searchBox.Text = "搜图标或文件…";
            this.searchBox.TextChanged += new System.EventHandler(this.SearchBox_TextChanged);
            this.searchBox.Enter += new System.EventHandler(this.SearchBox_Enter);
            this.searchBox.Leave += new System.EventHandler(this.SearchBox_Leave);
            //
            // 标题栏按钮组 / 搜索框外壳 / 分页指示器
            // 均为自绘控件，需要 palette 才能构造，改由 BuildChrome() 在 SetPalette 之后创建，
            // 不放在设计器里（设计器初始化时尚未加载配置与主题）。
            //
            // Launcher
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(30)))), ((int)(((byte)(30)))));
            this.ClientSize = new System.Drawing.Size(550, 374);
            this.KeyPreview = true;
            this.Name = "Launcher";
            // 不设 Opacity：<1 会把窗口变成 WS_EX_LAYERED 分层窗口，所有子控件重绘都要经 DWM
            // alpha 合成，鼠标在图标网格上移动时明显卡顿。窗口不透明度改由设置面板按需调整
            // （见 ApplyOpacity，仅当用户显式设为小于 100% 时才启用分层窗口）。
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Launcher";
            // 默认不置顶：失焦(点击窗口外)时主面板自动隐藏，符合 dock 启动器习惯；
            // 需要常驻可点标题栏图钉按钮临时置顶(此时不自动隐藏)。
            this.TopMost = false;
            this.Load += new System.EventHandler(this.Launcher_Load);
            this.KeyDown += new System.Windows.Forms.KeyEventHandler(this.Launcher_KeyDown);
            this.contextMenuStrip1.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.NotifyIcon notifyIcon1;
        private System.Windows.Forms.ContextMenuStrip contextMenuStrip1;
        private System.Windows.Forms.ToolStripMenuItem trayToggleToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem settingsToolStripMenuItem;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.ToolStripMenuItem exitToolStripMenuItem;
        private System.Windows.Forms.TextBox searchBox;

        // 自绘控件：在 BuildChrome() 中创建（构造函数里 InitializeComponent 之后）
        private launcher.Controls.RoundSearchBox searchBar;
        private launcher.Controls.IconGlyphButton btnSearch;
        private launcher.Controls.IconGlyphButton btnOpenLocation;
        private launcher.Controls.IconGlyphButton btnTheme;
        private launcher.Controls.IconGlyphButton btnNotes;
        private launcher.Controls.IconGlyphButton btnPinTop;
        private launcher.Controls.IconGlyphButton btnSettings;
        private launcher.Controls.IconGlyphButton btnClose;
        // 标题栏原先还有一个品牌名 Label（titleLabel = "Launcher"），已删除（2026-09-28）：
        // 标题栏的宽度本来就紧张，而主面板的整块客户区都能拖动窗口，不需要靠品牌名当把手。
    }
}
