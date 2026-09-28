using System;
using System.Drawing;
using System.Windows.Forms;
using launcher.Core;

namespace launcher.Controls
{
    /// <summary>
    /// 添加 / 编辑命令对话框（命令或路径 + 可选显示名称）。
    ///
    /// 与 PageNameDialog 同样从"系统标题栏 + 原生 Flat 按钮"改为自绘无边框窗口：
    /// 系统标题栏在 Win10 下没有暗色版本，Flat 按钮又会露系统浅色悬停块，
    /// 这两处在深色主题里是仅剩的"不像同一个程序"的地方。
    ///
    /// 命令输入仍用 CommandComboBox（带预设下拉），但下拉列表改为 OwnerDraw 自绘配色，
    /// 否则展开时是一整块系统白。
    ///
    /// 对外接口不变：构造签名 + Command / DisplayName 属性保持原样。
    /// </summary>
    public class CommandDialog : Form
    {
        private const int BORDER = 1;
        private const int CornerRadius = UiMetrics.RadiusWindow;
        private const int WIN_W = 440;
        private const int WIN_H = 250;
        private const int TITLE_H = UiMetrics.TitleBarH;   // 36：与主面板 / 设置 / 便签同高（原为 46）
        private const int FOOTER_H = UiMetrics.FooterH;
        private const int PAD = UiMetrics.XL;

        private readonly CommandComboBox _cmdBox;
        private readonly FlatTextBox _nameBox;
        private readonly ThemePalette _palette;
        private readonly WindowCornerState _corners = new WindowCornerState(CornerRadius);
        private bool? _dwmCorners;
        private Size _regionFor = Size.Empty;

        public string Command { get; private set; }
        public string DisplayName { get; private set; }

        public CommandDialog(string title, string initialCmd, string initialName, ThemePalette palette)
        {
            _palette = palette;

            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            KeyPreview = true;
            DoubleBuffered = true;
            Font = UiFonts.Base;
            BackColor = palette.PanelBack;
            _corners.BorderColor = palette.WindowBorder;   // 窗口外沿 1px 描边
            ForeColor = palette.TextPrimary;
            ClientSize = new Size(WIN_W, WIN_H);

            var lblTitle = new Label
            {
                Text = title,
                AutoSize = false,
                Location = new Point(PAD, 0),
                Size = new Size(WIN_W - PAD * 2 - UiMetrics.ChromeBtnW - UiMetrics.S, TITLE_H),
                BackColor = Color.Transparent,
                ForeColor = palette.TextPrimary,
                Font = UiFonts.Title,
                TextAlign = ContentAlignment.MiddleLeft
            };
            // 关闭键统一走 IconGlyphButton + Danger（悬停红底白叉），与其余四个窗口同款
            var btnClose = new IconGlyphButton(palette, GlyphKind.Close, q => q.PanelBack)
            {
                Danger = true,
                Location = new Point(WIN_W - BORDER - UiMetrics.S - UiMetrics.ChromeBtnW, (TITLE_H - UiMetrics.ChromeBtnH) / 2)
            };
            btnClose.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            // —— 命令 / 路径 ——
            var lblCmd = MakeFieldLabel("命令 / 路径", TITLE_H + UiMetrics.S);
            _cmdBox = new CommandComboBox
            {
                Location = new Point(PAD, TITLE_H + UiMetrics.XXXL),
                Width = WIN_W - PAD * 2,
                DropDownWidth = 260,
                BackColor = palette.ControlBack,
                ForeColor = palette.TextPrimary,
                FlatStyle = FlatStyle.Flat
            };
            _cmdBox.Text = initialCmd ?? string.Empty;
            StyleComboDropDown(_cmdBox, palette);
            // 选中预设命令时，若名称框为空则自动填友好名称（如选"此电脑"→名称填"此电脑"）
            _cmdBox.PresetSelected += label => { if (string.IsNullOrEmpty(_nameBox.Value)) _nameBox.Value = label; };

            // —— 显示名称 ——
            int nameLabelY = _cmdBox.Bottom + UiMetrics.M;
            var lblName = MakeFieldLabel("显示名称（可选）", nameLabelY);
            _nameBox = new FlatTextBox(palette)
            {
                Location = new Point(PAD, nameLabelY + UiMetrics.XXL),
                Size = new Size(WIN_W - PAD * 2, UiMetrics.ControlH)
            };
            _nameBox.Value = initialName ?? string.Empty;

            // —— 底部按钮 ——
            const int btnW = 88, btnH = UiMetrics.ButtonHBig;
            int btnY = WIN_H - FOOTER_H + (FOOTER_H - btnH) / 2;
            int okX = WIN_W - BORDER - PAD - btnW;
            var ok = new FlatButton(palette, "确定") { Location = new Point(okX, btnY), Size = new Size(btnW, btnH), Primary = true };
            var cancel = new FlatButton(palette, "取消") { Location = new Point(okX - UiMetrics.M - btnW, btnY), Size = new Size(btnW, btnH) };

            ok.Click += (s, e) =>
            {
                Command = _cmdBox.EffectiveCommand.Trim();
                DisplayName = _nameBox.Value.Trim();
                DialogResult = DialogResult.OK;
                Close();
            };
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            Controls.Add(lblTitle);
            Controls.Add(btnClose);
            Controls.Add(lblCmd);
            Controls.Add(_cmdBox);
            Controls.Add(lblName);
            Controls.Add(_nameBox);
            Controls.Add(ok);
            Controls.Add(cancel);

            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) { cancel.PerformClick(); e.Handled = true; }
            };

            Load += (s, e) => { _cmdBox.SelectAll(); _cmdBox.Focus(); };
        }

        private Label MakeFieldLabel(string text, int y)
        {
            return new Label
            {
                Text = text,
                ForeColor = _palette.TextMuted,
                Font = UiFonts.Body,
                AutoSize = false,
                Location = new Point(PAD, y),
                Size = new Size(WIN_W - PAD * 2, 20),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        /// <summary>把原生 ComboBox 的下拉列表改成 OwnerDraw —— 否则展开时是一整块系统白底，
        /// 在深色主题里格外刺眼（编辑区本身的底色可以设，下拉列表不行，只能自绘）。</summary>
        private static void StyleComboDropDown(ComboBox combo, ThemePalette palette)
        {
            combo.DrawMode = DrawMode.OwnerDrawFixed;
            combo.ItemHeight = 26;
            combo.DrawItem += (s, e) =>
            {
                if (e.Index < 0) return;
                var g = e.Graphics;
                bool sel = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

                using (var b = new SolidBrush(sel ? palette.Accent : palette.CardBack))
                    g.FillRectangle(b, e.Bounds);

                UiDraw.Text(g, combo.GetItemText(combo.Items[e.Index]), UiFonts.Body,
                    new Rectangle(e.Bounds.X + UiMetrics.S, e.Bounds.Y, e.Bounds.Width - UiMetrics.M, e.Bounds.Height),
                    sel ? Color.White : palette.TextPrimary,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
        }

        // CS_DROPSHADOW 已移除：它是矩形投影，与 Region 圆角裁剪冲突（四角留方角残影）。
        // 全项目窗口统一不要阴影，原因见 WindowCorners.cs 顶部说明。

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (IsHandleCreated) _corners.PaintBackground(this, e.Graphics);
            else base.OnPaintBackground(e);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyCorners();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplyCorners();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            UiDraw.Setup(e.Graphics);
            using (var pen = new Pen(_palette.BorderCol, 1f))
            {
                // 窗口外沿那圈线由 WindowCornerState 的描边统一负责（见 PaintBackground），
                // 这里只画内部分区线，避免同一位置画两道线。
                e.Graphics.DrawLine(pen, BORDER, TITLE_H - 1, Width - BORDER - 1, TITLE_H - 1);
                e.Graphics.DrawLine(pen, BORDER, WIN_H - FOOTER_H, Width - BORDER - 1, WIN_H - FOOTER_H);
            }
        }

        // 圆角：Win11 走 DWM，Win10 退回 Region 裁剪（与主面板同一策略）
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

        // 无边框窗口：标题栏区域伪装成系统标题栏，从而支持拖动。
        // 固定尺寸对话框，不提供边缘缩放。判定逻辑与其余四个窗口共用 WindowChrome。
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg != NativeMethods.WM_NCHITTEST || (int)m.Result != NativeMethods.HTCLIENT) return;

            var p = WindowChrome.HitPoint(this, m.LParam);
            if (WindowChrome.IsCaption(p, TITLE_H)) m.Result = (IntPtr)NativeMethods.HTCAPTION;
        }
    }
}
