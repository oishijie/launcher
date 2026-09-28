using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using launcher.Core;

namespace launcher.Controls
{
    // 设置面板需要主窗体才能执行的动作（导出 / 导入 / 清缓存 / 分页），由调用方注入，
    // 避免 SettingsForm 反向依赖 Launcher 具体类型。
    public class SettingsActions
    {
        public Action OpenConfigFile;
        public Action ExportConfig;
        public Action ImportConfig;
        public Action ClearIconCache;
        public Func<int> AddPage;            // 返回新页索引（<0 表示失败）
        public Action<int> RenamePage;       // 传入页索引
        public Action<int> RemovePage;       // 传入页索引
        public Action<int> PreviewOpacity;   // 拖动不透明度滑块时的实时预览
    }

    /// <summary>
    /// 统一设置面板（左侧竖排标签 + 无边框窗口 + 全自绘控件 + 卡片分组内容）。
    ///
    /// 本轮重构（观感）：
    ///   ① 内容从"控件裸铺在页面上"改为 **卡片分组** —— 一组设置一张圆角卡片，
    ///      组内行与行之间一条细线。之前区分分组靠标题下划线，线横穿整页、很碎；
    ///      现在卡片本身就是分组的可视边界。
    ///   ② 所有间距 / 尺寸取自 UiMetrics 栅格（原来是 44/48/42/44 四种隐式行距混用）。
    ///   ③ 左右内边距统一为 28（原来标题 24 / 内容 32 / 按钮距右 8 三套并存）。
    ///   ④ 窗口 620×460 → 660×500，给卡片和呼吸留出空间。
    ///   ⑤ 底部按钮条独立成区（横线分隔 + 右对齐到内容栅格），不再直接悬在内容下方。
    ///
    /// 行为约定不变：控件不直接改 ConfigStore —— 点「确定」时由 ApplyToStore() 统一写回，
    /// 「取消」由调用方 _store.Load() 回滚。唯一例外是「页面」页的增删改，它是即时生效的独立操作。
    /// </summary>
    public class SettingsForm : Form
    {
        // ===== 布局常量（全部取自 UiMetrics，不再出现裸数字）=====
        private const int BORDER = 1;                                     // 1px 自绘边框内缩
        private const int CornerRadius = UiMetrics.RadiusWindow;
        private const int WIN_W = UiMetrics.SettingsW;                    // 660
        private const int WIN_H = UiMetrics.SettingsH;                    // 500
        private const int TITLE_H = UiMetrics.TitleBarH;                  // 36，与主面板 / 便签同高（原为 50）
        private const int FOOTER_H = UiMetrics.FooterH;                   // 56
        private const int SIDE_W = UiMetrics.SettingsSideW;               // 148
        private const int PAD_L = UiMetrics.SettingsPadL;                 // 28（左右对称）
        private const int PAD_TOP = UiMetrics.SettingsPadTop;             // 20
        private const int ROW_H = UiMetrics.RowH;                         // 48，全页面统一
        private const int CARD_PAD = UiMetrics.CardRadiusPad;             // 12
        private const int CONTENT_W = WIN_W - BORDER * 2 - SIDE_W;        // 510
        private const int CONTENT_H = WIN_H - TITLE_H - FOOTER_H;         // 394
        private const int CARD_W = CONTENT_W - PAD_L * 2;                 // 454
        private const int CARD_RIGHT = CARD_W - CARD_PAD;                 // 卡片内右对齐基准
        private const int FIRST_CARD_Y = PAD_TOP + 34;                    // 分组标题(26) + 间距(8) 之后
        private const int CARD_GAP = UiMetrics.L;                         // 卡片间距 16

        private readonly ConfigStore _store;
        private readonly ThemePalette _palette;
        private readonly SettingsActions _actions;

        private SideTabBar _tabBar;
        private Panel _content;
        private Panel[] _pages;

        // 常规
        private ToggleSwitch _tgAutoStart, _tgAutoHide, _tgDblClick, _tgEdgeHide, _tgEverything;
        private Label _lblGeneralHint;   // 常规页说明区（末行会被「检测」刷新为 Everything 实时状态）
        // 外观
        private SegmentedControl _segTheme;
        private FlatSlider _slOpacity;
        private Label _lblOpacityValue;
        private NumberBox _nbIconSize, _nbColumns, _nbSlotCount;
        // 快捷键
        private FlatTextBox _txMain, _txNotes, _txTheme, _txUndo, _txPrev, _txNext;
        // 页面
        private FlatListBox _lstPages;
        // 数据
        private Label _lblAbout;

        public SettingsForm(ConfigStore store, ThemePalette palette, SettingsActions actions)
        {
            _store = store;
            _palette = palette;
            _actions = actions ?? new SettingsActions();

            Text = "设置";
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            KeyPreview = true;
            DoubleBuffered = true;
            Font = UiFonts.Base;
            BackColor = _palette.PanelBack;
            _corners.BorderColor = _palette.WindowBorder;   // 窗口外沿 1px 描边
            ForeColor = _palette.TextPrimary;
            ClientSize = new Size(WIN_W, WIN_H);

            // 左侧标签栏（选中态 = 圆角块 + 左侧标识条，见 SideTabBar）
            _tabBar = new SideTabBar(_palette)
            {
                Location = new Point(BORDER, TITLE_H),
                Size = new Size(SIDE_W, WIN_H - TITLE_H - FOOTER_H),
                Tabs = new[] { "常规", "外观", "快捷键", "页面", "数据" }
            };
            _tabBar.SelectedIndexChanged += (s, e) => ShowPage(_tabBar.SelectedIndex);

            // 内容区（每页一个 Panel，靠 Visible 切换，彻底避开 TabControl 的系统边框）
            _content = new Panel
            {
                Location = new Point(BORDER + SIDE_W, TITLE_H),
                Size = new Size(CONTENT_W, CONTENT_H),
                BackColor = _palette.PanelBack
            };
            _pages = new[]
            {
                BuildGeneralPage(),
                BuildAppearancePage(),
                BuildHotkeyPage(),
                BuildPagesPage(),
                BuildDataPage()
            };
            foreach (var pg in _pages)
            {
                pg.Location = new Point(0, 0);
                pg.Size = _content.Size;
                pg.BackColor = _palette.PanelBack;
                _content.Controls.Add(pg);
            }

            // ===== 标题栏 =====
            var lblTitle = new Label
            {
                Text = "设置",
                AutoSize = false,
                Location = new Point(PAD_L, 0),
                Size = new Size(CONTENT_W, TITLE_H),
                BackColor = Color.Transparent,
                ForeColor = _palette.TextPrimary,
                Font = UiFonts.Title,
                TextAlign = ContentAlignment.MiddleLeft
            };
            // 关闭键统一走 IconGlyphButton + Danger（悬停红底白叉），与主面板 / 便签同款。
            // 原先这里是 TitleBarButton —— 一个只服务于"对话框关闭键"的独立控件，
            // 结果同一个 × 在设置面板悬停变红、在主面板只是变亮。
            var btnClose = new IconGlyphButton(_palette, GlyphKind.Close, p => p.PanelBack)
            {
                Danger = true,
                Location = new Point(WIN_W - BORDER - UiMetrics.S - UiMetrics.ChromeBtnW,
                                     (TITLE_H - UiMetrics.ChromeBtnH) / 2)
            };
            btnClose.Click += (s, e) => CancelAndClose();

            // ===== 底部按钮条（独立成区，右对齐到内容栅格）=====
            const int btnW = 88, btnH = UiMetrics.ButtonHBig;
            int btnY = WIN_H - FOOTER_H + (FOOTER_H - btnH) / 2;
            int okX = WIN_W - BORDER - PAD_L - btnW;
            var btnOk = new FlatButton(_palette, "确定")
            {
                Location = new Point(okX, btnY),
                Size = new Size(btnW, btnH),
                Primary = true
            };
            var btnCancel = new FlatButton(_palette, "取消")
            {
                Location = new Point(okX - UiMetrics.M - btnW, btnY),
                Size = new Size(btnW, btnH)
            };
            btnCancel.Click += (s, e) => CancelAndClose();
            btnOk.Click += (s, e) =>
            {
                ApplyToStore();
                DialogResult = DialogResult.OK;
                Close();
            };

            Controls.Add(_tabBar);
            Controls.Add(_content);
            Controls.Add(lblTitle);
            Controls.Add(btnClose);
            Controls.Add(btnCancel);
            Controls.Add(btnOk);

            ShowPage(0);
        }

        private void CancelAndClose()
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void ShowPage(int index)
        {
            if (_pages == null || index < 0 || index >= _pages.Length) return;
            for (int i = 0; i < _pages.Length; i++) _pages[i].Visible = (i == index);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { CancelAndClose(); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            UiDraw.Setup(g);
            // 窗口外沿那圈线统一交给 WindowCornerState 的描边（见 PaintBackground）：
            // 那里的半径与 Region（真正的裁剪边界）严格同源，不会像这里各画一份那样
            // 在高 DPI 下错开半像素而显出"双线"。此处只留内部分区线。
            using (var pen = new Pen(_palette.BorderCol, 1f))
            {
                // 标题栏下沿 / 底部按钮条上沿：两条功能分区的边界线
                g.DrawLine(pen, BORDER, TITLE_H - 1, Width - BORDER - 1, TITLE_H - 1);
                g.DrawLine(pen, BORDER, WIN_H - FOOTER_H, Width - BORDER - 1, WIN_H - FOOTER_H);
            }
        }

        // CS_DROPSHADOW 已移除：它是矩形投影，与 Region 圆角裁剪冲突（四角留方角残影）。
        // 三个窗口统一不要阴影，原因见 WindowCorners.cs 顶部说明。

        private bool? _dwmCorners;             // DWM 原生圆角可用性，首次探测后缓存
        private Size _regionFor = Size.Empty;  // 已按哪个尺寸裁过 Region，避免 resize 时重复重建
        private readonly WindowCornerState _corners = new WindowCornerState(CornerRadius); // 圆角抗锯齿（采样桌面底色）

        // 背景自绘：先铺从窗口外沿采来的桌面底色，再叠抗锯齿圆角，
        // 让 Region 的硬裁边落在底色上，消掉 Win10 的圆角锯齿（详见 WindowCorners）。
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (IsHandleCreated) _corners.PaintBackground(this, e.Graphics);
            else base.OnPaintBackground(e);
        }

        // 套窗口圆角：Win11+ 走 DWM 原生（抗锯齿、投影正常）；Win10- 退回 Region 裁剪
        // （边缘无抗锯齿，是 Win10 上的唯一途径）。详见 Form1.ApplyRoundCorners。
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyCorners();
        }

        private void ApplyCorners()
        {
            if (!IsHandleCreated) return;
            if (_dwmCorners == null)
            {
                try { _dwmCorners = NativeMethods.EnableRoundCorners(Handle); }
                catch { _dwmCorners = false; }
            }

            if (_dwmCorners == true)
            {
                // Region 会盖过 DWM 圆角，白白把抗锯齿换成锯齿，必须清掉
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

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplyCorners();
        }

        // 无边框窗口：把标题栏区域伪装成系统标题栏，从而支持拖动。
        // 设置面板是固定尺寸对话框，**不提供边缘缩放**（故不调 WindowChrome.ResizeHit）。
        // 判定与主面板 / 便签 / 其余对话框共用 WindowChrome。
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg != NativeMethods.WM_NCHITTEST || (int)m.Result != NativeMethods.HTCLIENT) return;

            var p = WindowChrome.HitPoint(this, m.LParam);
            if (WindowChrome.IsCaption(p, TITLE_H)) m.Result = (IntPtr)NativeMethods.HTCAPTION;
        }

        // ===== 页面构建 =====

        private Panel NewPage()
        {
            return new Panel { BackColor = _palette.PanelBack };
        }

        /// <summary>页面顶部的分组标题（小字 + 弱色 + 无下划线）。</summary>
        private SectionTitle PageSection(Panel page, string text)
        {
            var st = new SectionTitle(_palette, text)
            {
                Location = new Point(PAD_L, PAD_TOP),
                Size = new Size(CARD_W, 26)
            };
            page.Controls.Add(st);
            return st;
        }

        /// <summary>新建一张内容卡片（无标题，分组标题由 PageSection 在卡片上方承担）。</summary>
        private SectionCard NewCard(Panel page, int y, int w, int h)
        {
            var card = new SectionCard(_palette)
            {
                Location = new Point(PAD_L, y),
                Size = new Size(w, h)
            };
            page.Controls.Add(card);
            return card;
        }

        /// <summary>卡片行内使用的标准行高（含上下自然留白）。</summary>
        private static int RowsHeight(int rowCount)
        {
            return CARD_PAD * 2 + rowCount * ROW_H;
        }

        private Label MakeLabel(string text, int x, int y, int w, Font font, Color color)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                Location = new Point(x, y),
                Size = new Size(w, 20),
                BackColor = Color.Transparent,
                ForeColor = color,
                Font = font,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        /// <summary>行内标签：高度撑满整行并垂直居中，文字自然对齐到行中线。</summary>
        private Label MakeRowLabel(string text, int x, int y, int w)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                Location = new Point(x, y),
                Size = new Size(w, ROW_H),
                BackColor = Color.Transparent,
                ForeColor = _palette.TextPrimary,
                Font = UiFonts.Body,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private Label MakeHint(string text, int x, int y, int w, int h)
        {
            return new Label
            {
                Text = text,
                // ⚠️ AutoSize 必须保持 false。WinForms 的 Label 没有 WordWrap 属性
                // （那是 WPF TextBlock 的），它只在 AutoSize=false 时自动折行；
                // 一旦 AutoSize=true，文字会按自身宽度无限横向延伸，
                // 超出面板的部分被裁掉且**不报任何错**。高度按行数给足，超出即被裁。
                AutoSize = false,
                Location = new Point(x, y),
                Size = new Size(w, h),
                BackColor = Color.Transparent,
                ForeColor = _palette.TextHint,
                Font = UiFonts.Small,
                TextAlign = ContentAlignment.TopLeft
            };
        }

        // ------------------------------------------------------------------
        //  常规
        // ------------------------------------------------------------------
        private Panel BuildGeneralPage()
        {
            var page = NewPage();
            PageSection(page, "启动与交互");

            const int rows = 5;
            var card = NewCard(page, FIRST_CARD_Y, CARD_W, RowsHeight(rows));
            for (int i = 1; i < rows; i++) card.AddDivider(CARD_PAD + i * ROW_H);

            _tgAutoStart = AddToggleRow(card, "开机自动启动", 0);
            try { _tgAutoStart.Checked = AutoStartManager.IsEnabled(); } catch { }

            _tgAutoHide = AddToggleRow(card, "点击窗口外自动隐藏主面板", 1);
            _tgAutoHide.Checked = _store.Layout.AutoHideEffective;

            _tgDblClick = AddToggleRow(card, "双击桌面 / 任务栏空白处呼出面板", 2);
            _tgDblClick.Checked = _store.Layout.DblClickShowEffective;

            _tgEdgeHide = AddToggleRow(card, "贴边自动隐藏", 3);
            _tgEdgeHide.Checked = _store.Layout.EdgeAutoHideEffective;

            _tgEverything = AddToggleRow(card, "搜索时同时做 Everything 全盘搜索", 4,
                64, "检测", RunEverythingProbe);
            _tgEverything.Checked = _store.Layout.EverythingSearchEffective;

            int hintY = FIRST_CARD_Y + card.Height + UiMetrics.M;
            _lblGeneralHint = MakeHint(GeneralHintText, PAD_L, hintY, CARD_W, 56);
            page.Controls.Add(_lblGeneralHint);
            return page;
        }

        // 常规页的说明文字。末行是 Everything 的**实时状态**（点「检测」后原地刷新），
        // 因为"我不会用"绝大多数时候等于"我不知道它到底有没有在工作" ——
        // 光看开关是开着的，看不出 Everything 有没有在跑、索引里有没有东西。
        //
        // 三行宽度实测（微软雅黑 8.5pt，PIL 量得）：363 / 392 / 125px，都在 Label 宽度 454px 内。
        // 改文案时务必重新量一遍：一旦某行折行，总行数由 3 变 4，而 Label 高度是按行数给的，
        // 超出部分是**直接裁掉** —— 不报错、不显示省略号，只能靠肉眼发现。
        // （末行会拼接 Probe() 的返回文本，所以那边的措辞也必须控制在一行内。）
        private const string HintHead =
            "「置顶钉住」时不会自动隐藏；贴边后鼠标移到屏幕边缘停一下即可唤出。\n"
            + "全盘搜索：搜索框里直接打字，先匹配本机图标，再追加 Everything 全盘命中。\n"
            + "Everything 状态：";

        private const string GeneralHintText = HintHead + "未检测";

        /// <summary>点「检测」：后台跑一次与真实搜索同链路的自检，把结果写回说明区末行。</summary>
        private void RunEverythingProbe()
        {
            if (_lblGeneralHint == null || _lblGeneralHint.IsDisposed) return;
            _lblGeneralHint.Text = HintHead + "检测中…";

            var t = new System.Threading.Thread(() =>
            {
                string status;
                try { status = EverythingSearch.Probe(); }
                catch (Exception ex) { status = "检测失败：" + ex.Message; }

                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (IsDisposed || _lblGeneralHint == null || _lblGeneralHint.IsDisposed) return;
                        _lblGeneralHint.Text = HintHead + status;
                    }));
                }
                catch { /* 窗口已关，丢弃结果 */ }
            });
            t.IsBackground = true;
            t.Start();
        }

        /// <summary>在卡片内加一行「标签 + 右侧开关」。rowIndex 从 0 起。</summary>
        /// <param name="inlineW">>0 时在开关左边再塞一个同高的行内小按钮（如「检测」）。</param>
        private ToggleSwitch AddToggleRow(Control card, string label, int rowIndex,
            int inlineW = 0, string inlineText = null, Action inlineAction = null)
        {
            int y = CARD_PAD + rowIndex * ROW_H;
            var tg = new ToggleSwitch(_palette)
            {
                Location = new Point(CARD_RIGHT - ToggleSwitch.TrackW, y + (ROW_H - ToggleSwitch.TrackH) / 2)
            };
            card.Controls.Add(tg);

            int labelRight = CARD_RIGHT - ToggleSwitch.TrackW - UiMetrics.L;
            if (inlineW > 0)
            {
                const int btnH = 28;
                var b = new FlatButton(_palette, inlineText)
                {
                    Location = new Point(labelRight - inlineW, y + (ROW_H - btnH) / 2),
                    Size = new Size(inlineW, btnH)
                };
                if (inlineAction != null) b.Click += (s, e) => inlineAction();
                card.Controls.Add(b);
                // 标签宽度必须让出按钮那一段，否则两者在控件层面重叠、按钮点不动
                labelRight -= inlineW + UiMetrics.S;
            }

            card.Controls.Add(MakeRowLabel(label, CARD_PAD, y, labelRight - CARD_PAD));
            return tg;
        }

        // ------------------------------------------------------------------
        //  外观
        // ------------------------------------------------------------------
        private Panel BuildAppearancePage()
        {
            var page = NewPage();
            PageSection(page, "外观");

            // 卡片一：主题 + 不透明度
            var c1 = NewCard(page, FIRST_CARD_Y, CARD_W, RowsHeight(2));
            c1.AddDivider(CARD_PAD + ROW_H);
            c1.Controls.Add(MakeRowLabel("主题", CARD_PAD, 0, 200));
            _segTheme = new SegmentedControl(_palette)
            {
                Location = new Point(CARD_RIGHT - 180, CARD_PAD + (ROW_H - UiMetrics.ControlH) / 2),
                Size = new Size(180, UiMetrics.ControlH),
                Items = new[] { "深色", "浅色" }
            };
            _segTheme.SelectedIndex = (_store.Theme == ThemeMode.Light) ? 1 : 0;
            c1.Controls.Add(_segTheme);

            int y2 = CARD_PAD + ROW_H;
            c1.Controls.Add(MakeRowLabel("窗口不透明度", CARD_PAD, y2, 200));
            const int valueW = 40, sliderW = 160;
            _slOpacity = new FlatSlider(_palette)
            {
                Location = new Point(CARD_RIGHT - valueW - UiMetrics.S - sliderW, y2 + (ROW_H - 28) / 2),
                Size = new Size(sliderW, 28),
                Minimum = 30,
                Maximum = 100,
                Value = Clamp(_store.Layout.Opacity, 30, 100)
            };
            _lblOpacityValue = MakeLabel(_slOpacity.Value + "%", CARD_RIGHT - valueW, y2 + (ROW_H - 20) / 2, valueW, UiFonts.Body, _palette.TextPrimary);
            _lblOpacityValue.TextAlign = ContentAlignment.MiddleRight;
            _slOpacity.ValueChanged += (s, e) =>
            {
                _lblOpacityValue.Text = _slOpacity.Value + "%";
                if (_actions.PreviewOpacity != null) _actions.PreviewOpacity(_slOpacity.Value);
            };
            c1.Controls.Add(_slOpacity);
            c1.Controls.Add(_lblOpacityValue);

            // 卡片二：三个数值项
            int c2Y = FIRST_CARD_Y + c1.Height + CARD_GAP;
            var c2 = NewCard(page, c2Y, CARD_W, RowsHeight(3));
            c2.AddDivider(CARD_PAD + ROW_H);
            c2.AddDivider(CARD_PAD + ROW_H * 2);

            _nbIconSize = AddNumberRow(c2, "图标大小", "16 – 256", 16, 256, _store.Layout.IconSize, 0);
            _nbColumns = AddNumberRow(c2, "每行图标数", "1 – 40", 1, 40, _store.Layout.Columns, 1);
            _nbSlotCount = AddNumberRow(c2, "每页槽位数", "1 – 400", 1, 400, _store.Layout.SlotCount, 2);

            int hintY = c2Y + c2.Height + UiMetrics.M;
            page.Controls.Add(MakeHint("不透明度低于 100% 会启用分层窗口，鼠标扫过图标网格时略卡。性能优先建议保持 100%。",
                PAD_L, hintY, CARD_W, 18));
            return page;
        }

        private NumberBox AddNumberRow(Control card, string label, string range, int min, int max, int value, int rowIndex)
        {
            const int RangeW = 62, BoxW = 96;
            int y = CARD_PAD + rowIndex * ROW_H;
            card.Controls.Add(MakeRowLabel(label, CARD_PAD, y, 200));
            var nb = new NumberBox(_palette)
            {
                Location = new Point(CARD_RIGHT - RangeW - UiMetrics.S - BoxW, y + (ROW_H - UiMetrics.ControlH) / 2),
                Size = new Size(BoxW, UiMetrics.ControlH),
                Minimum = min,
                Maximum = max
            };
            nb.Value = Clamp(value, min, max);
            card.Controls.Add(nb);
            card.Controls.Add(MakeLabel(range, CARD_RIGHT - RangeW, y + (ROW_H - 20) / 2, RangeW, UiFonts.Small, _palette.TextHint));
            return nb;
        }

        // ------------------------------------------------------------------
        //  快捷键
        // ------------------------------------------------------------------
        private Panel BuildHotkeyPage()
        {
            var page = NewPage();
            PageSection(page, "快捷键");
            page.Controls.Add(MakeHint("格式如 Alt+`、Ctrl+Shift+N、A、1（单键直接写字符）。修改后点「确定」生效。",
                PAD_L, PAD_TOP + 26, CARD_W, 18));

            const int rows = 6;
            int cardY = PAD_TOP + 26 + 18 + UiMetrics.M;
            var card = NewCard(page, cardY, CARD_W, RowsHeight(rows));
            for (int i = 1; i < rows; i++) card.AddDivider(CARD_PAD + i * ROW_H);

            _txMain = AddHotkeyRow(card, "主面板", _store.Hotkeys.Main, 0);
            _txNotes = AddHotkeyRow(card, "便签面板", _store.Hotkeys.Notes, 1);
            _txTheme = AddHotkeyRow(card, "切换主题", _store.Hotkeys.Theme, 2);
            _txUndo = AddHotkeyRow(card, "撤销删除", _store.Hotkeys.Undo, 3);
            _txPrev = AddHotkeyRow(card, "上一页", _store.Hotkeys.PrevPage, 4);
            _txNext = AddHotkeyRow(card, "下一页", _store.Hotkeys.NextPage, 5);
            return page;
        }

        private FlatTextBox AddHotkeyRow(Control card, string label, string value, int rowIndex)
        {
            const int BoxW = 220;
            int y = CARD_PAD + rowIndex * ROW_H;
            card.Controls.Add(MakeRowLabel(label, CARD_PAD, y, 200));
            var tb = new FlatTextBox(_palette)
            {
                Location = new Point(CARD_RIGHT - BoxW, y + (ROW_H - UiMetrics.ControlH) / 2),
                Size = new Size(BoxW, UiMetrics.ControlH)
            };
            tb.Value = value;
            card.Controls.Add(tb);
            return tb;
        }

        // ------------------------------------------------------------------
        //  页面
        // ------------------------------------------------------------------
        private Panel BuildPagesPage()
        {
            var page = NewPage();
            PageSection(page, "页面");

            // 左：列表卡片（卡片自身就是列表的边框容器，不再套一层 BorderedPanel ——
            // 两层圆角框叠在一起会显得很重，且中间那条缝会露出页面底色，像没对齐）
            const int cardH = 286;
            const int listW = 300;
            var listCard = NewCard(page, FIRST_CARD_Y, listW, cardH);
            _lstPages = new FlatListBox(_palette)
            {
                Location = new Point(CARD_PAD, CARD_PAD),
                Size = new Size(listW - CARD_PAD * 2, cardH - CARD_PAD * 2)
            };
            listCard.Controls.Add(_lstPages);

            // 右：操作按钮（与列表卡片同高区域，等距排布）
            int bx = PAD_L + listW + CARD_GAP;
            int bw = CARD_W - listW - CARD_GAP;
            const int bh = 40;
            var add = new FlatButton(_palette, "新增一页") { Location = new Point(bx, FIRST_CARD_Y), Size = new Size(bw, bh) };
            var rename = new FlatButton(_palette, "重命名") { Location = new Point(bx, FIRST_CARD_Y + bh + UiMetrics.M), Size = new Size(bw, bh) };
            var remove = new FlatButton(_palette, "删除") { Location = new Point(bx, FIRST_CARD_Y + (bh + UiMetrics.M) * 2), Size = new Size(bw, bh) };
            page.Controls.Add(add);
            page.Controls.Add(rename);
            page.Controls.Add(remove);

            add.Click += (s, e) =>
            {
                if (_actions.AddPage == null) return;
                int idx = _actions.AddPage();
                if (idx >= 0) { RefreshPageList(); _lstPages.SelectedIndex = idx; }
            };
            rename.Click += (s, e) =>
            {
                int idx = _lstPages.SelectedIndex;
                if (idx < 0 || _actions.RenamePage == null) return;
                _actions.RenamePage(idx);
                RefreshPageList();
                if (idx < _lstPages.Items.Count) _lstPages.SelectedIndex = idx;
            };
            remove.Click += (s, e) =>
            {
                int idx = _lstPages.SelectedIndex;
                if (idx < 0 || _actions.RemovePage == null) return;
                _actions.RemovePage(idx);
                RefreshPageList();
                if (_lstPages.Items.Count > 0)
                    _lstPages.SelectedIndex = Math.Min(idx, _lstPages.Items.Count - 1);
            };

            page.Controls.Add(MakeHint("「页面」页的增删改是即时生效的，不受「确定 / 取消」影响。",
                PAD_L, FIRST_CARD_Y + cardH + UiMetrics.M, CARD_W, 18));
            RefreshPageList();
            return page;
        }

        // ------------------------------------------------------------------
        //  数据
        // ------------------------------------------------------------------
        private Panel BuildDataPage()
        {
            var page = NewPage();
            PageSection(page, "数据与信息");

            const int rows = 4;
            var card = NewCard(page, FIRST_CARD_Y, CARD_W, RowsHeight(rows));
            for (int i = 1; i < rows; i++) card.AddDivider(CARD_PAD + i * ROW_H);

            AddActionRow(card, "打开配置文件（launcher.json）", _actions.OpenConfigFile, 0);
            AddActionRow(card, "导出配置备份…", _actions.ExportConfig, 1);
            AddActionRow(card, "导入配置备份…", _actions.ImportConfig, 2);
            AddActionRow(card, "清除图标缓存", _actions.ClearIconCache, 3);

            int hintY = FIRST_CARD_Y + card.Height + UiMetrics.M;
            page.Controls.Add(MakeHint("图标缓存位于程序目录 iconcache/。目标程序换过图标但显示仍旧时，清除一次即可。",
                PAD_L, hintY, CARD_W, 18));

            _lblAbout = new Label
            {
                Text = BuildAboutText(),
                AutoSize = false,
                Location = new Point(PAD_L, hintY + 26),
                Size = new Size(CARD_W, 84),
                BackColor = Color.Transparent,
                ForeColor = _palette.TextHint,
                Font = UiFonts.Small
            };
            page.Controls.Add(_lblAbout);
            return page;
        }

        /// <summary>
        /// 卡片内的「一行一个操作按钮」。按钮不再整行铺满（240 宽在 454 的卡片里会显得
        /// 左重右空），改成行内垂直居中、宽度 240 —— 视觉重量与开启态开关的右对齐形成呼应。
        /// </summary>
        private void AddActionRow(Control card, string text, Action action, int rowIndex)
        {
            const int btnW = 240, btnH = 34;
            int y = CARD_PAD + rowIndex * ROW_H + (ROW_H - btnH) / 2;
            var b = new FlatButton(_palette, text)
            {
                Location = new Point(CARD_PAD, y),
                Size = new Size(btnW, btnH),
                Enabled = action != null
            };
            if (action != null) b.Click += (s, e) => action();
            card.Controls.Add(b);
        }

        private static string BuildAboutText()
        {
            var asm = Assembly.GetExecutingAssembly();
            string version = (asm.GetName().Version ?? new Version(1, 0, 0, 0)).ToString();
            string build = string.Empty;
            try { build = System.IO.File.GetLastWriteTimeUtc(asm.Location).ToString("yyyy-MM-dd"); } catch { }
            return "Launcher — Windows 快速启动器\n"
                 + "版本：" + version + (build.Length > 0 ? "（构建于 " + build + "）" : "") + "\n"
                 + "基于 .NET Framework 4.7.2 / WinForms\n"
                 + "项目主页：https://github.com/worldoi/launcher";
        }

        private void RefreshPageList()
        {
            if (_lstPages == null) return;
            int sel = _lstPages.SelectedIndex;
            _lstPages.Items.Clear();
            foreach (var name in _store.PageNames) _lstPages.Items.Add(name);
            if (sel >= 0 && sel < _lstPages.Items.Count) _lstPages.SelectedIndex = sel;
            else if (_lstPages.Items.Count > 0) _lstPages.SelectedIndex = 0;
        }

        // ===== 写回 =====

        // 点「确定」时把控件值统一写回 ConfigStore（面板内不直接改，便于「取消」回滚）
        private void ApplyToStore()
        {
            var L = _store.Layout;
            L.IconSize = _nbIconSize.Value;
            L.Columns = _nbColumns.Value;
            L.SlotCount = _nbSlotCount.Value;
            L.Opacity = _slOpacity.Value;
            L.AutoHide = _tgAutoHide.Checked;
            L.DblClickShow = _tgDblClick.Checked;
            L.EdgeAutoHide = _tgEdgeHide.Checked;
            L.EverythingSearch = _tgEverything.Checked;
            _store.Theme = (_segTheme.SelectedIndex == 1) ? ThemeMode.Light : ThemeMode.Dark;

            var hk = _store.Hotkeys;
            hk.Main = Clean(_txMain);
            hk.Notes = Clean(_txNotes);
            hk.Theme = Clean(_txTheme);
            hk.Undo = Clean(_txUndo);
            hk.PrevPage = Clean(_txPrev);
            hk.NextPage = Clean(_txNext);

            // 开机自启不属于配置项（写注册表），在这里一并落实
            try { AutoStartManager.SetEnabled(_tgAutoStart.Checked); }
            catch (Exception ex) { Logger.Log("设置面板：切换开机自启失败 " + ex.Message); }
        }

        private static string Clean(FlatTextBox t)
        {
            return t == null || t.Value == null ? string.Empty : t.Value.Trim();
        }

        private static int Clamp(int v, int min, int max)
        {
            return v < min ? min : (v > max ? max : v);
        }
    }
}
