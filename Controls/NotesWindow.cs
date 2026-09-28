using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using launcher.Core;

namespace launcher.Controls
{
    /// <summary>
    /// 便签独立窗口。
    ///
    /// 背景：便签原先内嵌在主面板里（Designer 的 panel2），与启动器共用同一个窗口生命周期，
    /// 而这两件事的用法是冲突的 —— 启动器的习惯是"用完即走"（失焦自动隐藏、点图标就开app），
    /// 便签却是"长时间驻留、随手记两笔"的东西。内嵌时主面板一失焦，便签就跟着被藏起来。
    /// 拆成独立窗口后：有自己的位置/尺寸/置顶状态，主面板隐藏不影响它，可随时从快捷键
    /// 或主面板的便签按钮唤出。
    ///
    /// 存储仍然是 Core/NotesStore（程序目录下 notes/，每条一个 .txt），与旧数据完全兼容，
    /// 不迁移、不改格式。
    /// </summary>
    public class NotesWindow : Form
    {
        // ===== 布局常量（全部取自 UiMetrics，与主面板 / 设置面板同一套节奏）=====
        private const int BarH = UiMetrics.TitleBarH;         // 自绘标题栏高
        private const int SideW = UiMetrics.NotesSideW;       // 左侧便签列表栏宽
        private const int Pad = UiMetrics.M;                  // 内容外边距
        private const int CornerRadius = UiMetrics.RadiusWindow; // 窗口圆角半径

        private ThemePalette _p;
        private readonly NotesStore _notes;
        private Timer _saveTimer;   // 延迟到 BuildUi 初始化（不能 readonly）
        private readonly Font _tabFont = UiFonts.Small;
        private readonly List<FlatButton> _tabs = new List<FlatButton>();

        private ToolTip _tips;
        private bool _suppressSave;
        private bool _previewing;

        private bool? _dwmCorners;             // DWM 原生圆角可用性（首次探测后缓存）
        private Size _regionFor = Size.Empty;  // 已按哪个尺寸裁过 Region
        private readonly WindowCornerState _corners = new WindowCornerState(CornerRadius); // 圆角抗锯齿（采样桌面底色）

        // 背景自绘：先铺从窗口外沿采来的桌面底色，再叠抗锯齿圆角，
        // 让 Region 的硬裁边落在底色上，消掉 Win10 的圆角锯齿（详见 WindowCorners）。
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (IsHandleCreated) _corners.PaintBackground(this, e.Graphics);
            else base.OnPaintBackground(e);
        }

        // ===== 控件 =====
        private Label _title;
        private FlatButton _btnPreview;
        private IconGlyphButton _btnPin, _btnClose;
        private Panel _side;
        private FlatButton _btnAdd;
        private FlowLayoutPanel _list;
        private BorderedPanel _card;
        private TextBox _editor;
        private RichTextBox _preview;

        /// <summary>可见性变化（供主面板同步便签按钮的激活态）。</summary>
        public event EventHandler VisibilityChanged;

        public NotesWindow(ThemePalette palette)
        {
            _p = palette;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = true;
            MinimizeBox = false;
            MaximizeBox = false;
            KeyPreview = true;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.Font;
            AutoScaleDimensions = new SizeF(6F, 12F);
            Font = UiFonts.Base;
            Text = "便签";
            BackColor = palette.PanelBack;
            _corners.BorderColor = palette.WindowBorder;    // 窗口外沿 1px 描边
            ClientSize = new Size(UiMetrics.NotesW, UiMetrics.NotesH);
            MinimumSize = new Size(UiMetrics.NotesMinW, UiMetrics.NotesMinH);

            // 任务栏与 Alt+Tab 用和主程序一致的图标
            try
            {
                string ico = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Launcher.ico");
                if (File.Exists(ico)) Icon = new Icon(ico);
            }
            catch { }

            // 多便签：加载当前便签（目录不存在则自动创建/迁移旧 notes.txt）
            _notes = new NotesStore(AppDomain.CurrentDomain.BaseDirectory);
            _notes.Load();

            BuildUi();
            ApplyPalette(palette);

            _suppressSave = true;
            _editor.Text = _notes.Read(_notes.CurrentName);
            _suppressSave = false;
            RefreshList();

            // 默认落在主屏右下角（便签的常见位置），不遮住主面板
            var wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(Math.Max(wa.Left + 20, wa.Right - Width - 60),
                                 Math.Max(wa.Top + 20, wa.Bottom - Height - 80));
        }

        // ===== 界面构建 =====
        private void BuildUi()
        {
            _title = new Label
            {
                AutoSize = true,
                Text = "便签",
                Font = UiFonts.Title,
                BackColor = Color.Transparent
            };
            _title.Cursor = Cursors.SizeAll;
            _title.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) StartDrag(); };
            Controls.Add(_title);

            _btnPreview = new FlatButton(_p, "预览") { Size = new Size(64, 28), Font = UiFonts.Small };
            _btnPreview.Click += (s, e) => TogglePreview();
            Controls.Add(_btnPreview);

            // 图标按钮必须声明自己铺在哪种底上：便签窗口底是 PanelBack（不是主面板的 FormBack），
            // 不指定的话按钮会显出一块与周围不同色的方块。
            _btnPin = new IconGlyphButton(_p, GlyphKind.Pin, p => p.PanelBack);
            _btnPin.Click += (s, e) => { TopMost = !TopMost; _btnPin.Active = TopMost; };
            Controls.Add(_btnPin);

            _btnClose = new IconGlyphButton(_p, GlyphKind.Close, p => p.PanelBack) { Danger = true };
            _btnClose.Click += (s, e) => Hide();
            Controls.Add(_btnClose);

            // —— 左侧便签列表栏 ——
            _side = new Panel { BackColor = _p.SideBack };
            _btnAdd = new FlatButton(_p, "+") { Dock = DockStyle.Top, Height = 38, Font = UiFonts.Title };
            _btnAdd.Click += (s, e) => AddNote();
            _list = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(UiMetrics.XS, UiMetrics.S, UiMetrics.XS, UiMetrics.S),
                BackColor = _p.SideBack
            };
            _side.Controls.Add(_list);
            _side.Controls.Add(_btnAdd);
            Controls.Add(_side);

            // —— 内容卡片：编辑框与预览框共用同一区域，交替显示 ——
            _card = new BorderedPanel(_p);
            _editor = new TextBox
            {
                Multiline = true,
                BorderStyle = BorderStyle.None,
                ScrollBars = ScrollBars.Vertical,
                AcceptsTab = true,
                Font = UiFonts.Base,
                BackColor = _p.CardBack,
                ForeColor = _p.TextFore
            };
            _preview = new RichTextBox
            {
                BorderStyle = BorderStyle.None,
                ReadOnly = true,
                DetectUrls = false,
                Font = UiFonts.Base,
                BackColor = _p.CardBack,
                ForeColor = _p.TextFore,
                Visible = false
            };
            _preview.LinkClicked += (s, e) => OpenLink(e.LinkText);
            _card.Controls.Add(_editor);
            _card.Controls.Add(_preview);
            Controls.Add(_card);

            _saveTimer = new Timer { Interval = 600 };
            _saveTimer.Tick += (s, e) => { _saveTimer.Stop(); SaveNotesNow(); };
            _editor.TextChanged += (s, e) =>
            {
                if (_suppressSave) return;
                _saveTimer.Stop();
                _saveTimer.Start();
            };

            LayoutUi();
        }

        private void LayoutUi()
        {
            if (_title == null) return;
            int w = ClientSize.Width, h = ClientSize.Height;

            _title.Location = new Point(UiMetrics.L, (BarH - _title.Height) / 2);

            int right = w - UiMetrics.S;
            foreach (var b in new Control[] { _btnClose, _btnPin, _btnPreview })
            {
                b.Location = new Point(right - b.Width, (BarH - b.Height) / 2);
                right -= b.Width + UiMetrics.ChromeBtnGap;   // 与主面板同一间距，原先这里是 S(8)
            }

            int bodyY = BarH;
            int bodyH = Math.Max(0, h - BarH);
            _side.SetBounds(0, bodyY, SideW, bodyH);

            int cw = Math.Max(20, w - SideW - Pad * 2);
            int ch = Math.Max(20, bodyH - Pad * 2);
            _card.SetBounds(SideW + Pad, bodyY + Pad, cw, ch);

            const int inner = UiMetrics.M;   // 卡片内文本区留白
            int iw = Math.Max(10, _card.Width - inner * 2);
            int ih = Math.Max(10, _card.Height - inner * 2);
            _editor.SetBounds(inner, inner, iw, ih);
            _preview.SetBounds(inner, inner, iw, ih);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutUi();
            ApplyRoundCorners();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyRoundCorners();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            VisibilityChanged?.Invoke(this, EventArgs.Empty);
        }

        // CS_DROPSHADOW 已移除：它是矩形投影，与 Region 圆角裁剪冲突（四角留方角残影）。
        // 三个窗口统一不要阴影，原因见 WindowCorners.cs 顶部说明。

        // 无边框窗口的缩放与拖动：边缘走系统缩放命中区，标题栏走 HTCAPTION，
        // 其余区域保持 HTCLIENT（便签是编辑型窗口，点正文不该拖走窗口）。
        // 判定逻辑收在 WindowChrome，与主面板 / 设置 / 对话框共用同一套边界。
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg != NativeMethods.WM_NCHITTEST || (int)m.Result != NativeMethods.HTCLIENT) return;

            var p = WindowChrome.HitPoint(this, m.LParam);
            int hit = WindowChrome.ResizeHit(p, ClientSize);
            if (hit == 0 && WindowChrome.IsCaption(p, BarH)) hit = NativeMethods.HTCAPTION;
            if (hit != 0) m.Result = (IntPtr)hit;
        }

        private void StartDrag() { WindowChrome.StartDrag(Handle); }

        // ===== 圆角（Win11 DWM 优先，Win10 退回 Region 裁剪，与主面板同一策略）=====
        private void ApplyRoundCorners()
        {
            if (!IsHandleCreated) return;
            if (_dwmCorners == null)
            {
                try { _dwmCorners = NativeMethods.EnableRoundCorners(Handle); }
                catch { _dwmCorners = false; }
            }

            if (_dwmCorners == true)
            {
                var stale = Region;
                if (stale != null) { Region = null; stale.Dispose(); }
                _regionFor = Size.Empty;
                return;
            }

            try
            {
                var size = new Size(Width, Height);
                if (_regionFor == size && Region != null) return;
                int r = _corners.Radius(this);
                var old = Region;
                Region = WindowCorners.BuildRegion(size, r);
                if (old != null) old.Dispose();
                _regionFor = size;
            }
            catch { }
        }

        // ===== 主题 =====
        public void ApplyPalette(ThemePalette p)
        {
            if (p == null) return;
            _p = p;
            BackColor = p.PanelBack;
            _corners.BorderColor = p.WindowBorder;
            _title.ForeColor = p.TextPrimary;

            _btnPreview.Palette = p;
            _btnPreview.Active = _previewing;
            _btnPin.Palette = p;
            _btnPin.Active = TopMost;
            _btnClose.Palette = p;
            _btnAdd.Palette = p;

            _side.BackColor = p.SideBack;
            _list.BackColor = p.SideBack;
            _card.Palette = p;
            _editor.BackColor = p.CardBack;
            _editor.ForeColor = p.TextFore;
            _preview.BackColor = p.CardBack;
            _preview.ForeColor = p.TextFore;

            foreach (var t in _tabs) t.Palette = p;
            HighlightCurrent();
            if (_previewing) RenderPreview();

            Invalidate(true);
            ApplyRoundCorners();
        }

        // ===== 便签列表 =====
        private void RefreshList()
        {
            if (_notes == null || _list == null) return;
            try
            {
                if (_tips == null) _tips = new ToolTip();
                foreach (var b in _tabs) { _list.Controls.Remove(b); b.Dispose(); }
                _tabs.Clear();

                foreach (var name in _notes.ListNames())
                {
                    // 摘要：取内容首行非空文字（列宽很窄，最多 3 字）；空便签退回名字首 2 字
                    string content = _notes.Read(name);
                    string firstLine = (content ?? "").Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
                    string summary = string.IsNullOrWhiteSpace(firstLine)
                        ? (name.Length > 2 ? name.Substring(0, 2) : name)
                        : firstLine.Trim();
                    if (summary.Length > 3) summary = summary.Substring(0, 3);

                    string captured = name;
                    var btn = new FlatButton(_p, summary)
                    {
                        Size = new Size(48, 30),
                        Margin = new Padding(2, 0, 2, 2),
                        Font = _tabFont,
                        Tag = captured
                    };
                    _tips.SetToolTip(btn, captured);
                    btn.Click += (s, e) => SwitchNote(captured);
                    btn.MouseUp += (s, e) => { if (e.Button == MouseButtons.Right) ShowTabMenu(captured); };
                    _tabs.Add(btn);
                    _list.Controls.Add(btn);
                }
                HighlightCurrent();
            }
            catch (Exception ex) { Logger.Log("刷新便签列表失败: " + ex.Message); }
        }

        private void HighlightCurrent()
        {
            foreach (var b in _tabs)
                b.Active = string.Equals(b.Tag as string, _notes.CurrentName, StringComparison.Ordinal);
        }

        private void SwitchNote(string name)
        {
            if (_notes == null || name == _notes.CurrentName) return;
            SaveNotesNow();                 // 切走前落盘
            if (_previewing) TogglePreview(); // 预览态先回编辑态，避免内容错位
            if (_notes.SetCurrent(name))
            {
                _suppressSave = true;
                _editor.Text = _notes.Read(name);
                _suppressSave = false;
                HighlightCurrent();
            }
        }

        private void AddNote()
        {
            SaveNotesNow();
            string name = _notes.CreateNote(null);
            if (name == null) return;
            _notes.SetCurrent(name);
            _suppressSave = true;
            _editor.Text = string.Empty;
            _suppressSave = false;
            RefreshList();
            _editor.Focus();
        }

        private void ShowTabMenu(string name)
        {
            using (var menu = new ContextMenuStrip())
            {
                menu.BackColor = _p.MenuBack;
                menu.ForeColor = _p.MenuFore;
                menu.Renderer = new FlatMenuRenderer(_p);

                var rename = new ToolStripMenuItem("重命名");
                rename.Click += (s, e) => RenameNote(name);
                var del = new ToolStripMenuItem("删除");
                del.Click += (s, e) => DeleteNote(name);
                menu.Items.Add(rename);
                menu.Items.Add(del);
                menu.Show(Cursor.Position);
            }
        }

        private void RenameNote(string name)
        {
            using (var dlg = new PageNameDialog("重命名便签", name, _p))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (!_notes.Rename(name, dlg.PageName)) return;
                RefreshList();
                if (string.Equals(_notes.CurrentName, dlg.PageName, StringComparison.Ordinal))
                {
                    _suppressSave = true;
                    _editor.Text = _notes.Read(dlg.PageName);
                    _suppressSave = false;
                }
                SaveNotesNow();
            }
        }

        private void DeleteNote(string name)
        {
            var r = MessageBox.Show("确定删除便签「" + name + "」吗？\n删除后不可恢复。",
                "删除便签", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (r != DialogResult.Yes) return;
            if (!_notes.Delete(name)) return;
            _suppressSave = true;
            _editor.Text = _notes.Read(_notes.CurrentName);
            _suppressSave = false;
            RefreshList();
        }

        // ===== Markdown 预览 =====
        private void TogglePreview()
        {
            try
            {
                if (!_previewing)
                {
                    SaveNotesNow();
                    RenderPreview();
                    _editor.Visible = false;
                    _preview.Visible = true;
                    _previewing = true;
                    _btnPreview.Text = "编辑";
                    _btnPreview.Active = true;
                }
                else
                {
                    _preview.Visible = false;
                    _editor.Visible = true;
                    _previewing = false;
                    _btnPreview.Text = "预览";
                    _btnPreview.Active = false;
                    _editor.Focus();
                }
            }
            catch (Exception ex) { Logger.Log("便签预览切换失败: " + ex.Message); }
        }

        private void RenderPreview()
        {
            bool dark = _p.FormBack.GetBrightness() < 0.5f;
            _preview.Rtf = MarkdownRenderer.ToRtf(
                _editor.Text,
                _p.TextFore,
                dark ? Color.FromArgb(255, 200, 90) : Color.FromArgb(0, 120, 180),
                dark ? Color.FromArgb(90, 170, 255) : Color.FromArgb(0, 102, 204),
                dark ? Color.FromArgb(120, 210, 140) : Color.FromArgb(0, 128, 80),
                dark ? Color.FromArgb(35, 35, 35) : Color.FromArgb(240, 240, 240));
        }

        private void OpenLink(string url)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url)) return;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex) { Logger.Log("打开链接失败: " + ex.Message); }
        }

        // ===== 持久化 =====
        /// <summary>立即落盘（防抖定时器未到期、或主程序退出时调用）。</summary>
        public void SaveNotesNow()
        {
            if (_notes == null || _editor == null) return;
            _saveTimer?.Stop();
            _notes.Write(_notes.CurrentName, _editor.Text ?? string.Empty);
        }

        // 点 × 只隐藏（程序是托盘常驻的，隐藏后内容与位置都还在）；
        // 主程序退出（ApplicationExitCall）时正常销毁，不拦截。
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveNotesNow();
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _saveTimer?.Dispose();
                _tips?.Dispose();
                foreach (var t in _tabs) t.Dispose();
                _tabs.Clear();
                // 注意：_tabFont 现在是 UiFonts 的**静态共享字体**（原先是本窗自己 new 的），
                // 不能在这里 Dispose —— 释放它会连带废掉全进程其他用到同一个 Font 对象的控件
                // （表现为别处文字突然回退成系统默认字体或直接抛异常）。静态共享资源由进程一并回收。
            }
            base.Dispose(disposing);
        }

        // 供外部（主面板）在窗口尚未显示时把焦点交给编辑框
        public void FocusEditor()
        {
            _editor.Focus();
            _editor.SelectionStart = _editor.TextLength;
        }
    }
}
