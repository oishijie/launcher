using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using launcher.Core;

namespace launcher.Controls
{
    // ============================================================================
    //  自绘控件库：设置面板专用
    //  原生 WinForms 控件（ComboBox / NumericUpDown / TrackBar / CheckBox）无法自定
    //  义颜色，在深色主题上会露出系统灰与白底，是界面"廉价感"的主要来源。
    //  这里全部改为 UserPaint 自绘，颜色完全由 ThemePalette 控制。
    // ============================================================================

    internal static class UiFonts
    {
        // ============================================================================
        //  字号阶梯
        //
        //  重构前有 6 个字号（8.25 / 8.5 / 9.5 / 10 / 10.5 / 11），层级却读不出来 ——
        //  因为"比旁边大 0.5pt"不构成视觉层级，人眼分辨不出。真正的层级来自
        //  **字重 + 颜色**（Fluent Typography 的核心思路），字号只需分 3~4 档。
        //
        //  现在的分工：字号定"体量"（标题 / 正文 / 辅助），字重与颜色定"轻重"。
        //  中文微软雅黑只有 Regular / Bold 两个字面，正好够用。
        // ============================================================================
        public const string Family = "微软雅黑";

        public static readonly Font Title = new Font(Family, 11F, FontStyle.Bold);   // 窗口标题
        public static readonly Font Body = new Font(Family, 9.5F);                   // 正文（设置项、按钮、编辑区）
        public static readonly Font BodyStrong = new Font(Family, 9.5F, FontStyle.Bold); // 需强调的正文（数值、当前项）
        public static readonly Font Caption = new Font(Family, 8.5F);                // 辅助说明 / 单位 / 范围
        public static readonly Font Tab = new Font(Family, 10F);                     // 侧栏标签（略大，用户明确偏好）
        public static readonly Font TabActive = new Font(Family, 10F, FontStyle.Bold);

        // 旧名别名：全项目引用上百处，改名会牵动一大片；语义已收敛到上面几档。
        public static readonly Font Base = Body;
        public static readonly Font Small = Caption;
        public static readonly Font Section = BodyStrong;
        public static readonly Font Glyph = Body;

        // ============================================================================
        //  系统图标字体
        //
        //  标题栏图标原先由 UiGlyph 用 GDI+ 手绘几何（圆 + 线段 + 多边形）拼出来。
        //  16px 见方的图标里塞 1.5px 描边，坐标又大多落在非整数位置，抗锯齿只能把
        //  笔画化成 2~3 级灰度 —— 放大看就是"边缘发毛、糊成一团"；而且各图标的视觉
        //  重量还对不齐（圆环直径 14px、文件夹 15px、图钉更窄），并排放就是"不整"。
        //
        //  改用 Segoe MDL2 Assets（Win10 1809+ 系统自带）：整组图标由同一个字体设计
        //  师按同一网格绘制，笔画粗细与视觉尺寸天然统一，渲染时走字形轮廓转路径，
        //  小尺寸下比手绘几何干净得多。取不到该字体时自动退回手绘矢量（见 UiGlyph）。
        // ============================================================================
        public const string IconFamily = "Segoe MDL2 Assets";

        /// <summary>系统图标字体是否可用（探测一次，进程内缓存）。</summary>
        public static readonly bool IconFontAvailable = ProbeIconFont();

        private static bool ProbeIconFont()
        {
            try { return new FontFamily(IconFamily).Name.Length > 0; }
            catch { return false; }
        }
    }

    internal static class UiDraw
    {
        public static void Setup(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;
        }

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            if (radius <= 0 || r.Width <= 0 || r.Height <= 0)
            {
                if (r.Width > 0 && r.Height > 0) p.AddRectangle(r);
                return p;
            }
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Rectangle r, int radius, Color c)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (var path = Round(r, radius))
            using (var b = new SolidBrush(c))
                g.FillPath(b, path);
        }

        public static void DrawRound(Graphics g, Rectangle r, int radius, Color c)
        {
            if (r.Width <= 1 || r.Height <= 1) return;
            var rr = new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1);
            using (var path = Round(rr, radius))
            using (var p = new Pen(c, 1f))
                g.DrawPath(p, path);
        }

        public static Color Blend(Color a, Color b, float t)
        {
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t),
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        public static void Text(Graphics g, string s, Font f, Rectangle r, Color c, TextFormatFlags extra)
        {
            TextRenderer.DrawText(g, s, f, r, c, extra | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>左侧竖排标签栏。完全自绘，无系统 TabControl 的 3D 边框。</summary>
    public class SideTabBar : Control
    {
        private ThemePalette _p;
        private string[] _tabs = new string[0];
        private int _selected;
        private int _hover = -1;

        public int ItemHeight = UiMetrics.SideItemH;
        public int TopPad = UiMetrics.S;
        public int SidePad = UiMetrics.S;      // 选中块与侧栏左右边缘的间距
        public int TextIndent = 16;            // 文字相对选中块左沿的缩进

        public SideTabBar(ThemePalette palette)
        {
            _p = palette;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = palette.SideBack;
        }

        public ThemePalette Palette
        {
            get { return _p; }
            set { _p = value; BackColor = value.SideBack; Invalidate(); }
        }

        public string[] Tabs
        {
            get { return _tabs; }
            set { _tabs = value ?? new string[0]; Invalidate(); }
        }

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                int v = value;
                if (v < 0) v = 0;
                if (_tabs.Length > 0 && v > _tabs.Length - 1) v = _tabs.Length - 1;
                if (v == _selected) return;
                _selected = v;
                Invalidate();
                var h = SelectedIndexChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        public event EventHandler SelectedIndexChanged;

        private int HitTest(Point pt)
        {
            if (pt.X < 0 || pt.X > Width) return -1;
            if (pt.Y < TopPad) return -1;
            int i = (pt.Y - TopPad) / ItemHeight;
            if (i < 0 || i >= _tabs.Length) return -1;
            return i;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = HitTest(e.Location);
            if (i != _hover) { _hover = i; Cursor = i >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = -1; Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int i = HitTest(e.Location);
            if (i >= 0) SelectedIndex = i;
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            UiDraw.Setup(g);
            using (var b = new SolidBrush(_p.SideBack)) g.FillRectangle(b, ClientRectangle);

            for (int i = 0; i < _tabs.Length; i++)
            {
                var row = new Rectangle(0, TopPad + i * ItemHeight, Width, ItemHeight);
                bool sel = i == _selected;

                // 选中/悬停块改成「左右内缩的圆角块」：满宽直角填充是系统 TabControl 的语言，
                // 圆角块 + 左侧标识条才是现代侧栏（Fluent NavigationView / Linear / Raycast）的做法。
                var block = new Rectangle(SidePad, row.Y + UiMetrics.XS, Width - SidePad * 2, ItemHeight - UiMetrics.XS * 2);
                // 选中/悬停底改用 alpha 叠加（对齐 WinKit 的 #18000000 / #10000000）：
                // 叠在侧栏底上得到的明度与原实色几乎一样，但换主题时不必再各维护一个色值。
                if (sel) UiDraw.FillRound(g, block, UiMetrics.RadiusControl, _p.OverlaySelected);
                else if (i == _hover) UiDraw.FillRound(g, block, UiMetrics.RadiusControl, _p.OverlayHover);

                // 选中标识：3×16 的圆头**短条**，在行内垂直居中。
                // 不用"通高竖条"——那是系统 TabControl 的语言；短条 + 圆角块底才是
                // Fluent NavigationView / WinKit 的做法，也不会与选中底重复表达同一件事。
                if (sel)
                {
                    int barY = block.Y + (block.Height - UiMetrics.SideBarH) / 2;
                    UiDraw.FillRound(g, new Rectangle(block.X, barY, UiMetrics.SideBarW, UiMetrics.SideBarH),
                        UiMetrics.SideBarRadius, _p.Accent);
                }

                var tr = new Rectangle(block.X + TextIndent, row.Y, block.Width - TextIndent - UiMetrics.S, row.Height);
                UiDraw.Text(g, _tabs[i], sel ? UiFonts.TabActive : UiFonts.Tab, tr,
                    sel ? _p.TextPrimary : _p.TextMuted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }

            // 右边缘分隔线（画在自身内部，才不会被右侧内容区覆盖）
            using (var pen = new Pen(_p.BorderCol, 1f))
                g.DrawLine(pen, Width - 1, 0, Width - 1, Height);
        }
    }

    /// <summary>滑动开关，替代 CheckBox。</summary>
    public class ToggleSwitch : Control
    {
        private ThemePalette _p;
        private bool _checked;
        private bool _hover;

        public const int TrackW = UiMetrics.SwitchW;
        public const int TrackH = UiMetrics.SwitchH;

        public ToggleSwitch(ThemePalette palette)
        {
            _p = palette;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                   | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = new Size(TrackW, TrackH);
            Cursor = Cursors.Hand;
        }

        public ThemePalette Palette
        {
            get { return _p; }
            set { _p = value; Invalidate(); }
        }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                var h = CheckedChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        public event EventHandler CheckedChanged;

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) Checked = !Checked;
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            UiDraw.Setup(g);

            var track = new Rectangle(0, (Height - TrackH) / 2, TrackW, TrackH);
            Color fill;
            if (_checked) fill = _hover ? _p.AccentHover : _p.Accent;
            else fill = _hover ? UiDraw.Blend(_p.ToggleOff, _p.TextMuted, 0.25f) : _p.ToggleOff;

            UiDraw.FillRound(g, track, TrackH / 2, fill);

            int d = UiMetrics.SwitchKnob;
            int inset = UiMetrics.SwitchInset;
            int cx = _checked ? track.Right - inset - d / 2 : track.X + inset + d / 2;
            int cy = track.Y + TrackH / 2;
            var knob = new Rectangle(cx - d / 2, cy - d / 2, d, d);

            // 柔和投影：GDI+ 没有 blur，用「向下偏移 1px + 低 alpha」的暗圆近似
            // WinKit 那个 DropShadowEffect(blur 3, opacity .25)。缺了它，纯白滑块压在
            // 有色的轨道上会显得"贴平"，没有浮起来的层次。
            using (var shadow = new SolidBrush(Color.FromArgb(34, 0, 0, 0)))
                g.FillEllipse(shadow, knob.X, knob.Y + 1, knob.Width, knob.Height);

            using (var b = new SolidBrush(Color.White))
                g.FillEllipse(b, knob);
            // 细描边：纯白滑块压在浅色主题的浅灰轨道上边界会糊，一圈半透明黑把轮廓勾出来
            using (var pen = new Pen(Color.FromArgb(_checked ? 30 : 44, 0, 0, 0), 1f))
                g.DrawEllipse(pen, knob);
        }
    }

    /// <summary>扁平按钮。Primary = true 时使用强调色填充。</summary>
    public class FlatButton : Control
    {
        private ThemePalette _p;
        private bool _hover, _pressed, _active;

        public bool Primary;
        public int Corner = UiMetrics.RadiusControl;

        /// <summary>激活态（如便签"预览中"）：边框与文字转强调色，标识该功能当前已开启。</summary>
        public bool Active
        {
            get { return _active; }
            set { if (_active != value) { _active = value; Invalidate(); } }
        }

        /// <summary>以编程方式触发一次点击（等价于 ButtonBase.PerformClick）。</summary>
        public void PerformClick() { if (Enabled) OnClick(EventArgs.Empty); }

        public FlatButton(ThemePalette palette, string text)
        {
            _p = palette;
            Text = text;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                   | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = UiFonts.Base;
            Size = new Size(84, UiMetrics.ButtonH);
            Cursor = Cursors.Hand;
        }

        public ThemePalette Palette
        {
            get { return _p; }
            set { _p = value; Invalidate(); }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            UiDraw.Setup(g);
            var r = new Rectangle(0, 0, Width, Height);

            Color fill, fore, border = _p.BorderCol;
            bool hasBorder;

            if (Primary)
            {
                fill = _pressed ? UiDraw.Blend(_p.Accent, Color.Black, 0.16f)
                     : (_hover ? _p.AccentHover : _p.Accent);
                fore = Color.White;
                hasBorder = false;
            }
            else
            {
                // 次按钮：底色取「卡片 + 6% 前景」——深色下变亮、浅色下变暗，
                // 两个主题里都保证按钮比卡片"浮"出来一点（之前直接用 CardBack，
                // 按钮放进卡片就与卡片同色，只剩一圈边框，看着像没点得动）。
                fill = UiDraw.Blend(_p.CardBack, _p.TextPrimary, 0.06f);
                if (_pressed) fill = UiDraw.Blend(_p.CardBack, _p.TextPrimary, 0.16f);
                else if (_hover) fill = UiDraw.Blend(_p.CardBack, _p.TextPrimary, 0.11f);
                fore = _p.TextPrimary;
                hasBorder = true;

                if (_active)
                {
                    fill = UiDraw.Blend(_p.CardBack, _p.Accent, (_hover || _pressed) ? 0.30f : 0.20f);
                    fore = _p.Accent;
                    border = _p.Accent;
                }
            }

            if (!Enabled)
            {
                fill = UiDraw.Blend(_p.PanelBack, _p.CardBack, 0.5f);
                fore = _p.TextHint;
                hasBorder = true;
                border = _p.BorderCol;
            }

            UiDraw.FillRound(g, r, Corner, fill);
            if (hasBorder) UiDraw.DrawRound(g, r, Corner, border);
            UiDraw.Text(g, Text, Font, r, fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    /// <summary>分段控件，替代二选一的 ComboBox。</summary>
    public class SegmentedControl : Control
    {
        private ThemePalette _p;
        private string[] _items = new string[0];
        private int _selected;
        private int _hover = -1;

        public int Corner = UiMetrics.RadiusControl;
        public int Pad = 3;

        public SegmentedControl(ThemePalette palette)
        {
            _p = palette;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                   | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = UiFonts.Base;
            Size = new Size(180, 30);
            Cursor = Cursors.Hand;
        }

        public ThemePalette Palette
        {
            get { return _p; }
            set { _p = value; Invalidate(); }
        }

        public string[] Items
        {
            get { return _items; }
            set { _items = value ?? new string[0]; Invalidate(); }
        }

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                int v = value;
                if (v < 0) v = 0;
                if (_items.Length > 0 && v > _items.Length - 1) v = _items.Length - 1;
                if (v == _selected) return;
                _selected = v;
                Invalidate();
                var h = SelectedIndexChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        public event EventHandler SelectedIndexChanged;

        private int HitTest(Point pt)
        {
            if (_items.Length == 0) return -1;
            int segW = (Width - Pad * 2) / _items.Length;
            if (segW <= 0) return -1;
            int i = (pt.X - Pad) / segW;
            if (i < 0 || i >= _items.Length) return -1;
            return i;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = HitTest(e.Location);
            if (i != _hover) { _hover = i; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int i = HitTest(e.Location);
            if (i >= 0) SelectedIndex = i;
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            UiDraw.Setup(g);
            var outer = new Rectangle(0, 0, Width, Height);
            // 外层是「凹槽」：用 ControlBack（比卡片面再区分一档的控件面），刻意不加边框 ——
            // 填充 + 边框双层描边会让分段控件看着像两个叠起来的按钮，反而不像一体式的选择器。
            UiDraw.FillRound(g, outer, Corner, _p.ControlBack);

            if (_items.Length == 0) return;
            int segW = (Width - Pad * 2) / _items.Length;
            int h = Height - Pad * 2;

            for (int i = 0; i < _items.Length; i++)
            {
                var r = new Rectangle(Pad + i * segW, Pad, segW, h);
                bool sel = i == _selected;
                if (sel)
                {
                    UiDraw.FillRound(g, r, Corner - 2, _p.Accent);
                }
                else if (i == _hover)
                {
                    UiDraw.FillRound(g, r, Corner - 2, UiDraw.Blend(_p.CardBack, _p.TextPrimary, 0.08f));
                }
                UiDraw.Text(g, _items[i], Font, r,
                    sel ? Color.White : _p.TextMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }

    /// <summary>自绘滑块，替代 TrackBar（TrackBar 的刻度与滑块颜色不可定制）。</summary>
    public class FlatSlider : Control
    {
        private ThemePalette _p;
        private int _min, _max = 100, _value = 50;
        private bool _dragging, _hover;

        public int TrackH = 4;
        public int ThumbR = 7;

        public FlatSlider(ThemePalette palette)
        {
            _p = palette;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                   | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Height = 28;
            Cursor = Cursors.Hand;
        }

        public ThemePalette Palette
        {
            get { return _p; }
            set { _p = value; Invalidate(); }
        }

        public int Minimum { get { return _min; } set { _min = value; if (_max <= _min) _max = _min + 1; ApplyValue(_value); } }
        public int Maximum { get { return _max; } set { _max = value; if (_max <= _min) _max = _min + 1; ApplyValue(_value); } }

        public int Value
        {
            get { return _value; }
            set { ApplyValue(value); }
        }

        private void ApplyValue(int v)
        {
            if (v < _min) v = _min;
            if (v > _max) v = _max;
            if (v == _value) { Invalidate(); return; }
            _value = v;
            Invalidate();
            var h = ValueChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        public event EventHandler ValueChanged;

        private float Ratio { get { return _max > _min ? (float)(_value - _min) / (_max - _min) : 0f; } }

        private void SetFromX(int x)
        {
            int usable = Width - ThumbR * 2;
            if (usable <= 0) return;
            float t = (float)(x - ThumbR) / usable;
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            ApplyValue(_min + (int)Math.Round(t * (_max - _min)));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { _dragging = true; Capture = true; SetFromX(e.X); }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging) SetFromX(e.X);
            if (!_hover) { _hover = true; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _dragging = false;
            Capture = false;
            Invalidate();
            base.OnMouseUp(e);
        }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            UiDraw.Setup(g);
            int cy = Height / 2;
            int x0 = ThumbR;
            int w = Width - ThumbR * 2;
            if (w <= 0) return;

            var back = new Rectangle(x0, cy - TrackH / 2, w, TrackH);
            UiDraw.FillRound(g, back, TrackH / 2, _p.TrackBack);

            int fw = (int)Math.Round(w * Ratio);
            if (fw > 0)
            {
                var fill = new Rectangle(x0, cy - TrackH / 2, fw, TrackH);
                UiDraw.FillRound(g, fill, TrackH / 2, _p.Accent);
            }

            int cx = x0 + fw;
            int r = _dragging || _hover ? ThumbR : ThumbR - 1;
            using (var b = new SolidBrush(Color.White))
                g.FillEllipse(b, cx - r, cy - r, r * 2, r * 2);
            using (var p = new Pen(Color.FromArgb(48, 0, 0, 0), 1f))
                g.DrawEllipse(p, cx - r, cy - r, r * 2, r * 2);
        }
    }

    /// <summary>数字输入框：外框自绘 + 内嵌无边框 TextBox + 自绘上下箭头。</summary>
    public class NumberBox : Control
    {
        private ThemePalette _p;
        private readonly TextBox _tb;
        private int _min, _max = 100, _value;
        private int _hotZone;      // 0 无 / 1 增 / 2 减
        private bool _focus;
        private bool _syncing;

        private const int ArrowW = 26;

        public NumberBox(ThemePalette palette)
        {
            _p = palette;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                   | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = new Size(96, UiMetrics.ControlH);

            _tb = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = palette.ControlBack,
                ForeColor = palette.TextPrimary,
                Font = UiFonts.Base
            };
            _tb.TextChanged += Tb_TextChanged;
            _tb.LostFocus += Tb_LostFocus;
            _tb.GotFocus += (s, e) => { _focus = true; Invalidate(); };
            _tb.KeyDown += Tb_KeyDown;
            Controls.Add(_tb);
            LayoutInner();
        }

        public ThemePalette Palette
        {
            get { return _p; }
            set
            {
                _p = value;
                _tb.BackColor = value.ControlBack;
                _tb.ForeColor = value.TextPrimary;
                Invalidate();
            }
        }

        public int Minimum { get { return _min; } set { _min = value; if (_max <= _min) _max = _min + 1; Value = _value; } }
        public int Maximum { get { return _max; } set { _max = value; if (_max <= _min) _max = _min + 1; Value = _value; } }

        public int Value
        {
            get { return _value; }
            set
            {
                int v = value;
                if (v < _min) v = _min;
                if (v > _max) v = _max;
                if (v == _value && _tb.Text == v.ToString()) return;
                _value = v;
                _syncing = true;
                _tb.Text = v.ToString();
                _syncing = false;
                Invalidate();
                var h = ValueChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        public event EventHandler ValueChanged;

        protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutInner(); }

        private void LayoutInner()
        {
            if (_tb == null) return;
            int w = Width - 12 - ArrowW;
            if (w < 20) w = 20;
            int h = _tb.Height;
            int y = (Height - h) / 2;
            if (y < 0) y = 0;
            _tb.SetBounds(12, y, w, h);
        }

        private void Tb_TextChanged(object sender, EventArgs e)
        {
            if (_syncing) return;
            int v;
            if (int.TryParse(_tb.Text.Trim(), out v))
            {
                if (v < _min) v = _min;
                if (v > _max) v = _max;
                if (v != _value)
                {
                    _value = v;
                    var h = ValueChanged;
                    if (h != null) h(this, EventArgs.Empty);
                }
            }
        }

        private void Tb_LostFocus(object sender, EventArgs e)
        {
            _focus = false;
            int v;
            if (!int.TryParse(_tb.Text.Trim(), out v)) v = _value;
            Value = v;
            Invalidate();
        }

        private void Tb_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Up) { Value = _value + 1; e.Handled = true; }
            else if (e.KeyCode == Keys.Down) { Value = _value - 1; e.Handled = true; }
        }

        private int ZoneAt(Point pt)
        {
            if (pt.X < Width - ArrowW || pt.X > Width) return 0;
            return pt.Y < Height / 2 ? 1 : 2;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int z = ZoneAt(e.Location);
            if (z != _hotZone) { _hotZone = z; Cursor = z > 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hotZone = 0; Cursor = Cursors.Default; Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int z = ZoneAt(e.Location);
            if (z == 1) Value = _value + 1;
            else if (z == 2) Value = _value - 1;
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            UiDraw.Setup(g);
            var r = new Rectangle(0, 0, Width, Height);

            UiDraw.FillRound(g, r, UiMetrics.RadiusControl, _p.ControlBack);
            UiDraw.DrawRound(g, r, UiMetrics.RadiusControl, _focus ? _p.Accent : _p.BorderCol);

            int cx = Width - ArrowW / 2 - 1;
            int qy = Height / 4;
            int s = 4;

            DrawArrow(g, cx, qy, s, true, _hotZone == 1 || _focus);
            DrawArrow(g, cx, qy * 3, s, false, _hotZone == 2 || _focus);
        }

        private void DrawArrow(Graphics g, int cx, int cy, int s, bool up, bool highlight)
        {
            Color c = highlight ? _p.Accent : _p.TextHint;
            Point[] pts = up
                ? new[] { new Point(cx - s, cy + s / 2), new Point(cx + s, cy + s / 2), new Point(cx, cy - s / 2) }
                : new[] { new Point(cx - s, cy - s / 2), new Point(cx + s, cy - s / 2), new Point(cx, cy + s / 2) };
            using (var b = new SolidBrush(c))
                g.FillPolygon(b, pts);
        }
    }

    /// <summary>单行文本输入框：外框自绘 + 内嵌无边框 TextBox。焦点时边框转为强调色。</summary>
    public class FlatTextBox : Control
    {
        private ThemePalette _p;
        private readonly TextBox _tb;
        private bool _focus, _hover;

        public FlatTextBox(ThemePalette palette)
        {
            _p = palette;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                   | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = new Size(300, UiMetrics.ControlH);

            _tb = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = palette.ControlBack,
                ForeColor = palette.TextPrimary,
                Font = UiFonts.Base
            };
            _tb.GotFocus += (s, e) => { _focus = true; Invalidate(); };
            _tb.LostFocus += (s, e) => { _focus = false; Invalidate(); };
            Controls.Add(_tb);
            LayoutInner();
        }

        public ThemePalette Palette
        {
            get { return _p; }
            set
            {
                _p = value;
                _tb.BackColor = value.ControlBack;
                _tb.ForeColor = value.TextPrimary;
                Invalidate();
            }
        }

        public string Value
        {
            get { return _tb.Text; }
            set { _tb.Text = value ?? string.Empty; }
        }

        /// <summary>
        /// 把键盘焦点交给内部那个真实 TextBox 并（可选）全选内容。
        /// 外壳是纯自绘的 Control、本身不接收输入焦点，直接调 Focus() 是无效操作 ——
        /// 对话框加载完要让用户能立刻打字，必须走这里。
        /// </summary>
        public void FocusInner(bool selectAll = false)
        {
            if (_tb == null) return;
            if (!_tb.Focused) _tb.Focus();
            if (selectAll) _tb.SelectAll();
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutInner(); }

        private void LayoutInner()
        {
            if (_tb == null) return;
            int h = _tb.Height;
            int y = (Height - h) / 2;
            if (y < 0) y = 0;
            _tb.SetBounds(12, y, Math.Max(20, Width - 24), h);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _tb.Focus(); base.OnMouseDown(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            UiDraw.Setup(g);
            var r = new Rectangle(0, 0, Width, Height);
            UiDraw.FillRound(g, r, UiMetrics.RadiusControl, _p.ControlBack);
            UiDraw.DrawRound(g, r, UiMetrics.RadiusControl, _focus ? _p.Accent : (_hover ? UiDraw.Blend(_p.BorderCol, _p.TextPrimary, 0.25f) : _p.BorderCol));
        }
    }

    /// <summary>无边框列表（自绘项），外层配合 BorderedPanel 提供圆角边框。</summary>
    public class FlatListBox : ListBox
    {
        private ThemePalette _p;

        public FlatListBox(ThemePalette palette)
        {
            _p = palette;
            BorderStyle = BorderStyle.None;
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = 34;
            IntegralHeight = false;
            BackColor = palette.CardBack;
            ForeColor = palette.TextPrimary;
            Font = UiFonts.Base;
        }

        public ThemePalette Palette
        {
            get { return _p; }
            set
            {
                _p = value;
                BackColor = value.CardBack;
                ForeColor = value.TextPrimary;
                Invalidate();
            }
        }

        private int _hotIndex = -1;   // 悬停项（ListBox 自己不发 HotLight，得手工追踪）

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = IndexFromPoint(e.Location);
            if (i != _hotIndex) { _hotIndex = i; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (_hotIndex != -1) { _hotIndex = -1; Invalidate(); }
            base.OnMouseLeave(e);
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var g = e.Graphics;
            bool sel = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            bool hot = e.Index == _hotIndex && !sel;

            // 底色：先铺卡片面，再按状态**叠 alpha** —— 与标题栏按钮、侧栏选中完全同一套语义。
            using (var b = new SolidBrush(_p.CardBack))
                g.FillRectangle(b, e.Bounds);

            if (hot)
                using (var b = new SolidBrush(_p.OverlaySubtle))
                    g.FillRectangle(b, e.Bounds);

            // 选中态 = 淡强调底 + 左侧短标识条，而不是整行实心强调色 ——
            // 实心色块在深色主题里过于抢眼（一行亮蓝比整页所有内容都显眼），
            // 淡底 + 短条是 Fluent ListView / VS Code 树列表的一致做法。
            if (sel)
                using (var b = new SolidBrush(UiDraw.Blend(_p.CardBack, _p.Accent, 0.18f)))
                    g.FillRectangle(b, e.Bounds);

            if (sel)
            {
                // 与侧栏选中条同一套语言：圆头短条、行内垂直居中（不是通高直角块）
                int barY = e.Bounds.Y + (e.Bounds.Height - UiMetrics.SideBarH) / 2;
                UiDraw.FillRound(g, new Rectangle(e.Bounds.X, barY, UiMetrics.SideBarW, UiMetrics.SideBarH),
                    UiMetrics.SideBarRadius, _p.Accent);
            }

            var r = new Rectangle(e.Bounds.X + 14, e.Bounds.Y, e.Bounds.Width - 22, e.Bounds.Height);
            UiDraw.Text(g, Items[e.Index] == null ? string.Empty : Items[e.Index].ToString(),
                sel ? UiFonts.BodyStrong : Font, r, _p.TextPrimary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>圆角边框容器，用于包裹列表等需要视觉容器的区域。</summary>
    public class BorderedPanel : Panel
    {
        private ThemePalette _p;
        public int Corner = UiMetrics.RadiusCard;

        public BorderedPanel(ThemePalette palette)
        {
            _p = palette;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = palette.CardBack;
        }

        public ThemePalette Palette
        {
            get { return _p; }
            set { _p = value; BackColor = value.CardBack; Invalidate(); }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var back = Parent != null ? Parent.BackColor : _p.PanelBack;
            using (var b = new SolidBrush(back)) e.Graphics.FillRectangle(b, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            UiDraw.Setup(g);
            var r = new Rectangle(0, 0, Width, Height);
            UiDraw.FillRound(g, r, Corner, _p.CardBack);
            UiDraw.DrawRound(g, r, Corner, _p.BorderCol);
        }
    }

    /// <summary>分组标题：小字 + 弱色 + 无下划线。</summary>
    public class SectionTitle : Control
    {
        private ThemePalette _p;

        public SectionTitle(ThemePalette palette, string text)
        {
            _p = palette;
            Text = text;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                   | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Height = 26;
        }

        public ThemePalette Palette
        {
            get { return _p; }
            set { _p = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            UiDraw.Setup(g);
            // 满宽下划线是 2010 年代 Windows 设置页的写法：那条线横穿整个内容区、把版面切碎，
            // 且与卡片边框叠在一起形成"双层线"。现代设置页（GitHub / Linear / Fluent）
            // 一律改成「半粗字 + 弱色 + 靠上方留白分隔」——分组照样清楚，版面却安静得多。
            UiDraw.Text(g, Text, UiFonts.BodyStrong, new Rectangle(0, 0, Width, Height), _p.TextHint,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }

    /// <summary>
    /// 分组卡片：设置面板的主力容器。
    ///
    /// 借鉴 Fluent 2 的 "settings card" 与 Linear / GitHub Settings 的分组做法 ——
    /// 一组相关设置用一张圆角卡片包起来，组内行与行之间用 1px 细分隔线区分，
    /// 而不是把控件裸铺在页面上（裸铺时"分组"完全靠读者自己猜）。
    ///
    /// 卡片本身就是分组的可视边界，因此不再需要标题下划线那种弱信号。
    /// </summary>
    public class SectionCard : Panel
    {
        private ThemePalette _p;
        private readonly System.Collections.Generic.List<int> _dividerYs =
            new System.Collections.Generic.List<int>();

        /// <summary>卡片内左上角的分组标题；为空则不占标题高度。</summary>
        public string Title;

        /// <summary>标题区高度（有标题时内容需从标题下方开始排）。</summary>
        public int TitleHeight { get { return string.IsNullOrEmpty(Title) ? 0 : UiMetrics.CardTitleH; } }

        public SectionCard(ThemePalette palette, string title = null)
        {
            _p = palette;
            Title = title;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = palette.CardBack;
        }

        public ThemePalette Palette
        {
            get { return _p; }
            set { _p = value; BackColor = value.CardBack; Invalidate(); }
        }

        /// <summary>登记一条行分隔线的 Y 坐标（相对卡片内部）。</summary>
        public void AddDivider(int y)
        {
            _dividerYs.Add(y);
            Invalidate();
        }

        public void ClearDividers()
        {
            _dividerYs.Clear();
            Invalidate();
        }

        // 卡片外沿要透出页面底色，才能看见圆角
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var back = Parent != null ? Parent.BackColor : _p.PanelBack;
            using (var b = new SolidBrush(back)) e.Graphics.FillRectangle(b, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            UiDraw.Setup(g);
            var r = new Rectangle(0, 0, Width, Height);
            UiDraw.FillRound(g, r, UiMetrics.RadiusCard, _p.CardBack);
            UiDraw.DrawRound(g, r, UiMetrics.RadiusCard, _p.BorderCol);

            int pad = UiMetrics.CardRadiusPad;

            if (!string.IsNullOrEmpty(Title))
                UiDraw.Text(g, Title, UiFonts.BodyStrong,
                    new Rectangle(pad, UiMetrics.S, Width - pad * 2, UiMetrics.CardTitleH - UiMetrics.S),
                    _p.TextHint,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            // 行分隔线：比边框更淡（Blend 60%），只起"分成几行"的暗示，不构成第二条视觉主干
            using (var pen = new Pen(UiDraw.Blend(_p.CardBack, _p.BorderCol, 0.6f), 1f))
                foreach (int y in _dividerYs)
                    g.DrawLine(pen, pad, y, Width - pad, y);
        }
    }

    // 这里原有一个 TitleBarButton（只画一个自绘 × 的独立控件），已删除（2026-09-28）。
    // 它的存在让「窗口关闭键」分裂成两种实现：设置面板 / 两个对话框用它（悬停变红），
    // 主面板与便签用 IconGlyphButton（悬停只是变亮）。同一动作两种反馈没有理由。
    // 现在关闭键一律是 IconGlyphButton(Close) { Danger = true }。

    // ============================================================================
    //  主界面 chrome：标题栏图标按钮 / 搜索框 / 菜单渲染器
    //  原先标题栏是 5 个原生 Button，字体分别是 Webdings、Segoe Script 和 emoji，
    //  且 UseVisualStyleBackColor=true 会在深色底上露出系统浅色悬停块 —— 主界面
    //  "廉价感"的主要来源。这里全部换成矢量自绘，颜色由 ThemePalette 控制。
    // ============================================================================

    /// <summary>标题栏图标按钮可用的矢量字形。</summary>
    public enum GlyphKind { Close, Theme, Note, Pin, Folder, Settings, Search, More }

    /// <summary>
    /// 图标绘制。两条路径：
    ///   ① 系统图标字体 Segoe MDL2 Assets（首选）—— 字形由字体设计师按统一网格绘制，
    ///      笔画粗细与视觉尺寸天然一致；转成轮廓路径后按包围盒精确居中，不会像手绘
    ///      几何那样因基线 / 行距留白而视觉偏移。
    ///   ② GDI+ 手绘矢量（兜底）—— 取不到图标字体时使用，见 DrawVector。
    /// </summary>
    internal static class UiGlyph
    {
        // ===== Segoe MDL2 Assets 码点 =====
        // 这些值不是照抄图标表来的 —— 表中 E8B7"Folder"、E718"Pin"、E70B"QuickNote"
        // 用的是**旧版线条画法**（纸页形、箭尾形），摆在标题栏里既不达意也不好看。
        // 下面这组是把候选码点按 32px 渲染成图、逐个看形状挑出来的（见 codex 记录）。
        private const string CP_Close = "\uE8BB";     // ChromeClose：细线 ×，比 E711 更轻
        private const string CP_Theme = "\uE793";     // 太阳 + 半填充圆：正好是"半明半暗"
        private const string CP_Note = "\uE7C3";      // Page：右上折角的页面，比 E70B 干净
        private const string CP_Pin = "\uE840";       // Pinned：斜置经典图钉，辨识度最高
        private const string CP_Folder = "\uED25";    // Folder：唯一一个真正画成文件夹的
        private const string CP_Settings = "\uE713";  // Setting（齿轮）
        private const string CP_Search = "\uE721";    // Search
        private const string CP_More = "\uE712";      // More（横三点）

        private static FontFamily _iconFamily;

        private static FontFamily IconFamily
        {
            get
            {
                if (_iconFamily == null && UiFonts.IconFontAvailable)
                {
                    try { _iconFamily = new FontFamily(UiFonts.IconFamily); }
                    catch { }
                }
                return _iconFamily;
            }
        }

        private static string CodeOf(GlyphKind kind)
        {
            switch (kind)
            {
                case GlyphKind.Close: return CP_Close;
                case GlyphKind.Theme: return CP_Theme;
                case GlyphKind.Note: return CP_Note;
                case GlyphKind.Pin: return CP_Pin;
                case GlyphKind.Folder: return CP_Folder;
                case GlyphKind.Settings: return CP_Settings;
                case GlyphKind.Search: return CP_Search;
                case GlyphKind.More: return CP_More;
                default: return null;
            }
        }

        /// <summary>在 center 处绘制一个视觉边长约 size 的图标。</summary>
        public static void Draw(Graphics g, GlyphKind kind, Point center, int size, Color c)
        {
            if (DrawFromFont(g, kind, center, size, c)) return;
            DrawVector(g, kind, center, size, c);
        }

        /// <summary>
        /// 用系统图标字体绘制。取不到字体、或该码点在此字体里没有轮廓时返回 false（转手绘兜底）。
        /// </summary>
        private static bool DrawFromFont(Graphics g, GlyphKind kind, Point center, int size, Color c)
        {
            FontFamily family = IconFamily;
            if (family == null) return false;

            string code = CodeOf(kind);
            if (string.IsNullOrEmpty(code)) return false;

            StringFormat fmt = null;
            try
            {
                // GenericTypographic：关掉排版用的字距与额外留白，字形边界才等于视觉边界，
                // 后面按 GetBounds 居中才是准的。
                fmt = (StringFormat)StringFormat.GenericTypographic.Clone();
                fmt.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.NoClip;

                using (var path = new GraphicsPath())
                {
                    // origin 传 (0,0)：AddString 的起点是**基线**，直接画会整体偏上。
                    // 这里量出实际轮廓的包围盒，再把它平移到 center ——
                    // 一次解决"基线偏移"与"字体行距留白"两个居中难题。
                    path.AddString(code, family, 0, size, new PointF(0f, 0f), fmt);

                    var b = path.GetBounds();
                    if (b.Width < 0.5f || b.Height < 0.5f) return false;   // 该码点无字形

                    using (var m = new Matrix())
                    {
                        m.Translate(center.X - (b.X + b.Width / 2f),
                                    center.Y - (b.Y + b.Height / 2f));
                        path.Transform(m);
                    }

                    using (var brush = new SolidBrush(c))
                        g.FillPath(brush, path);
                }
                return true;
            }
            catch { return false; }
            finally { if (fmt != null) fmt.Dispose(); }
        }

        /// <summary>手绘矢量字形（无图标字体时的兜底，统一描边的线性风格）。</summary>
        private static void DrawVector(Graphics g, GlyphKind kind, Point center, int size, Color c)
        {
            float cx = center.X, cy = center.Y;
            float s = size / 2f;                        // 半边长
            float w = Math.Max(1.25f, size / 10.7f);    // 线宽随尺寸走（16px → 1.5px）

            using (var pen = new Pen(c, w)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            })
            using (var fill = new SolidBrush(c))
            {
                switch (kind)
                {
                    case GlyphKind.Close:
                        g.DrawLine(pen, cx - s + 1, cy - s + 1, cx + s - 1, cy + s - 1);
                        g.DrawLine(pen, cx + s - 1, cy - s + 1, cx - s + 1, cy + s - 1);
                        break;

                    case GlyphKind.Theme:
                        // 半填充圆：直接对应"深浅色"语义
                        g.DrawEllipse(pen, cx - s + 1, cy - s + 1, s * 2 - 2, s * 2 - 2);
                        using (var path = new GraphicsPath())
                        {
                            path.AddArc(cx - s + 1, cy - s + 1, s * 2 - 2, s * 2 - 2, -90, 180);
                            path.CloseFigure();
                            g.FillPath(fill, path);
                        }
                        break;

                    case GlyphKind.Note:
                        // 便签：圆角方 + 两条内容线
                        var note = new RectangleF(cx - s + 1, cy - s + 1, s * 2 - 2, s * 2 - 2);
                        using (var np = new GraphicsPath())
                        {
                            np.AddArc(note.X, note.Y, 4, 4, 180, 90);
                            np.AddArc(note.Right - 4, note.Y, 4, 4, 270, 90);
                            np.AddArc(note.Right - 4, note.Bottom - 4, 4, 4, 0, 90);
                            np.AddArc(note.X, note.Bottom - 4, 4, 4, 90, 90);
                            np.CloseFigure();
                            g.DrawPath(pen, np);
                        }
                        g.DrawLine(pen, cx - 2.5f, cy - 1f, cx + 2.5f, cy - 1f);
                        g.DrawLine(pen, cx - 2.5f, cy + 2.5f, cx + 0.5f, cy + 2.5f);
                        break;

                    case GlyphKind.Pin:
                        // 图钉：梯形钉帽 + 细针
                        using (var cap = new GraphicsPath())
                        {
                            cap.AddPolygon(new[]
                            {
                                new PointF(cx - 4f, cy - s + 1),
                                new PointF(cx + 4f, cy - s + 1),
                                new PointF(cx + 2f, cy - 1f),
                                new PointF(cx - 2f, cy - 1f)
                            });
                            g.DrawPath(pen, cap);
                        }
                        g.DrawLine(pen, cx, cy - 1f, cx, cy + s - 1);
                        break;

                    case GlyphKind.Folder:
                        using (var fp = new GraphicsPath())
                        {
                            float x0 = cx - s + 0.5f, y0 = cy - s + 2.5f, x1 = cx + s - 0.5f, y1 = cy + s - 1.5f;
                            fp.AddLine(x0, y0 + 2f, x0 + 4f, y0 + 2f);
                            fp.AddLine(x0 + 4f, y0 + 2f, x0 + 5.5f, y0);
                            fp.AddLine(x0 + 5.5f, y0, x1 - 2f, y0);
                            fp.AddArc(x1 - 4f, y0, 4, 4, 270, 90);
                            fp.AddLine(x1, y0 + 2f, x1, y1 - 2f);
                            fp.AddArc(x1 - 4f, y1 - 4f, 4, 4, 0, 90);
                            fp.AddLine(x1 - 2f, y1, x0 + 2f, y1);
                            fp.AddArc(x0, y1 - 4f, 4, 4, 90, 90);
                            fp.AddLine(x0, y1 - 2f, x0, y0 + 2f);
                            fp.CloseFigure();
                            g.DrawPath(pen, fp);
                        }
                        break;

                    case GlyphKind.Settings:
                        // 两条滑杆：比齿轮更现代，且在小尺寸下辨识度更高
                        g.DrawLine(pen, cx - s + 1, cy - 3f, cx + s - 1, cy - 3f);
                        g.FillEllipse(fill, cx - 4.5f, cy - 5f, 4f, 4f);
                        g.DrawLine(pen, cx - s + 1, cy + 3f, cx + s - 1, cy + 3f);
                        g.FillEllipse(fill, cx + 0.5f, cy + 1f, 4f, 4f);
                        break;

                    case GlyphKind.Search:
                        g.DrawEllipse(pen, cx - s + 2, cy - s + 1.5f, 8f, 8f);
                        g.DrawLine(pen, cx + 1.5f, cy + 1.5f, cx + s - 1, cy + s - 1);
                        break;

                    case GlyphKind.More:
                        g.FillEllipse(fill, cx - s + 1.5f, cy - 1.5f, 3f, 3f);
                        g.FillEllipse(fill, cx - 1.5f, cy - 1.5f, 3f, 3f);
                        g.FillEllipse(fill, cx + s - 4.5f, cy - 1.5f, 3f, 3f);
                        break;
                }
            }
        }
    }

    /// <summary>标题栏图标按钮：圆角悬停底 + 矢量字形，替代字体各异的原生 Button。</summary>
    public class IconGlyphButton : Control
    {
        private ThemePalette _p;
        private readonly GlyphKind _kind;
        private bool _hover, _pressed, _active;

        public int Corner = UiMetrics.RadiusControl;

        /// <summary>
        /// 按钮所铺的底色。默认取主题的 FormBack（主面板底）。
        /// 放到底色不同的窗口上（例如便签窗口底为 PanelBack）必须传入对应的取色器，
        /// 否则按钮会在界面上显出一块与周围不同色的方块 —— 这个控件是**不透明**自绘的，
        /// BackColor 就是它真正画出来的底色。
        /// </summary>
        public Func<ThemePalette, Color> SurfacePicker;

        private Color Surface { get { return SurfacePicker != null ? SurfacePicker(_p) : _p.FormBack; } }

        public IconGlyphButton(ThemePalette palette, GlyphKind kind, Func<ThemePalette, Color> surfacePicker = null)
        {
            _p = palette;
            _kind = kind;
            SurfacePicker = surfacePicker;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Surface;
            Size = new Size(UiMetrics.ChromeBtnW, UiMetrics.ChromeBtnH);
            Cursor = Cursors.Hand;
        }

        public ThemePalette Palette
        {
            get { return _p; }
            set { _p = value; BackColor = Surface; Invalidate(); }
        }

        /// <summary>激活态（如已置顶 / 便签面板已展开）：显示强调色底与强调色图标。</summary>
        public bool Active
        {
            get { return _active; }
            set { if (_active != value) { _active = value; Invalidate(); } }
        }

        /// <summary>
        /// 危险操作按钮（窗口关闭键）：悬停铺系统关闭键的红色底、图标转白。
        ///
        /// "关闭键悬停变红"是全平台的肌肉记忆（Windows 标题栏、macOS 红绿灯、各类
        /// 现代 Web 弹窗），用户不需要思考就能确认"这一下会关掉窗口"。
        /// 统一前只有设置面板的 × 是这个行为，主面板与便签的 × 走普通淡叠层 ——
        /// 三个窗口的关闭键长得一样、反应却不一样。现在三个都走这里。
        /// </summary>
        public bool Danger;

        /// <summary>以编程方式触发一次点击（等价于 ButtonBase.PerformClick）。</summary>
        public void PerformClick() { if (Enabled) OnClick(EventArgs.Empty); }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            UiDraw.Setup(g);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);

            // 悬停 / 按下改用 alpha 叠加（WinKit 的做法：悬停 #12、按下 #24 的黑/白叠层）。
            // 比实色好的地方在于**不必为"按钮铺在不同底色上"各配一个实色** ——
            // 叠加色自己会与底下那层混合，浅色主题也自动成立。
            // 唯一的例外是关闭键（Danger）：红底 + 白叉是系统级约定，必须用实色，不能叠加。
            bool dangerHot = Danger && (_hover || _pressed);
            if (_active)
                UiDraw.FillRound(g, r, Corner, _p.IconActiveBack);
            else if (dangerHot)
                UiDraw.FillRound(g, r, Corner,
                    _pressed ? UiDraw.Blend(_p.Danger, Color.Black, 0.15f) : _p.Danger);
            else if (_pressed)
                UiDraw.FillRound(g, r, Corner, _p.OverlayPressed);
            else if (_hover)
                UiDraw.FillRound(g, r, Corner, _p.OverlayHover);

            Color fg = _active ? _p.Accent
                     : dangerHot ? Color.White
                     : (_hover ? _p.IconForeActive : _p.IconFore);
            UiGlyph.Draw(g, _kind, new Point(Width / 2, Height / 2), 16, fg);
        }
    }

    /// <summary>
    /// 圆角搜索框：外壳自绘（圆角底 + 边框 + 放大镜），内部塞一个原生 TextBox。
    /// 刻意不用全自绘输入框 —— IME、光标、选区、拖选、复制粘贴全部要自己实现，
    /// 而原生 TextBox 只去掉边框就能无缝嵌进圆角容器，能力零损失。
    ///
    /// 内核 TextBox 支持**外部注入**（<paramref name="inner"/>）：主面板的搜索框在
    /// Designer 里建好并绑了 TextChanged / Enter / Leave 事件，如果这里再新建一个自己的
    /// Inner，就会变成"外壳管着一个看不见的输入框、用户实际在另一个输入框里打字"——
    /// 表现为：放大镜被文字盖住、获得焦点时边框不变色、点外壳空白处聚焦不上去。
    /// 传入现成的 TextBox 就不存在这个问题，也让这个控件只有一个输入框。
    /// </summary>
    public class RoundSearchBox : Control
    {
        private ThemePalette _p;
        private bool _hover;
        private const int PadL = 30;   // 左侧放大镜占位
        private const int PadR = 10;

        /// <summary>实际接收输入的原生 TextBox（由本控件负责定位与配色）。</summary>
        public readonly TextBox Inner;

        public RoundSearchBox(ThemePalette palette, TextBox inner = null)
        {
            _p = palette;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = palette.FormBack;

            // 外部注入的 TextBox 也要规范化：边框、字体、底色统一由这里说了算，
            // 免得 Designer 里的老设定（位置 0,0 / 固定宽 100）和外壳打架。
            Inner = inner ?? new TextBox();
            Inner.BorderStyle = BorderStyle.None;
            Inner.Font = UiFonts.Base;
            Inner.BackColor = palette.SearchBack;
            Inner.ForeColor = palette.TextFore;

            Inner.GotFocus += (s, e) => Invalidate();
            Inner.LostFocus += (s, e) => Invalidate();
            Inner.TextChanged += (s, e) => Invalidate();
            if (Inner.Parent != this) Controls.Add(Inner);
            Height = UiMetrics.SearchH;
            LayoutInner();
        }

        /// <summary>把焦点转给内部输入框（外壳自身不接收焦点）。</summary>
        public void FocusInner()
        {
            if (Inner != null && !Inner.IsDisposed && !Inner.Focused) Inner.Focus();
        }

        public ThemePalette Palette
        {
            get { return _p; }
            set
            {
                _p = value;
                BackColor = value.FormBack;
                Inner.BackColor = value.SearchBack;
                Inner.ForeColor = value.TextFore;
                Invalidate();
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutInner();
        }

        private void LayoutInner()
        {
            if (Inner == null || Inner.IsDisposed) return;
            int h = Inner.PreferredHeight;
            Inner.SetBounds(PadL, Math.Max(0, (Height - h) / 2), Math.Max(10, Width - PadL - PadR), h);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        // 点击外壳空白处直接聚焦输入框，省得用户非得点中细窄的文字行
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            FocusInner();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            UiDraw.Setup(g);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            UiDraw.FillRound(g, r, UiMetrics.RadiusCard, _p.SearchBack);

            Color border = Inner.Focused ? _p.SearchBorderFocus
                         : (_hover ? UiDraw.Blend(_p.SearchBorder, _p.TextHint, 0.35f) : _p.SearchBorder);
            UiDraw.DrawRound(g, r, UiMetrics.RadiusCard, border);

            Color glyph = Inner.Focused ? _p.Accent : _p.SearchCue;
            UiGlyph.Draw(g, GlyphKind.Search, new Point(17, Height / 2), 15, glyph);
        }
    }

    /// <summary>深色友好的右键菜单配色表（默认 Professional 配色在深色主题上会露白底与渐变）。</summary>
    public class FlatMenuColorTable : ProfessionalColorTable
    {
        private readonly ThemePalette _p;
        public FlatMenuColorTable(ThemePalette p) { _p = p; UseSystemColors = false; }

        public override Color ToolStripDropDownBackground { get { return _p.MenuBack; } }
        public override Color MenuBorder { get { return _p.MenuBorder; } }
        public override Color MenuItemBorder { get { return Color.Transparent; } }
        public override Color MenuItemSelected { get { return _p.MenuHover; } }
        public override Color MenuItemSelectedGradientBegin { get { return _p.MenuHover; } }
        public override Color MenuItemSelectedGradientEnd { get { return _p.MenuHover; } }
        public override Color MenuItemPressedGradientBegin { get { return _p.MenuHover; } }
        public override Color MenuItemPressedGradientMiddle { get { return _p.MenuHover; } }
        public override Color MenuItemPressedGradientEnd { get { return _p.MenuHover; } }
        public override Color ImageMarginGradientBegin { get { return _p.MenuBack; } }
        public override Color ImageMarginGradientMiddle { get { return _p.MenuBack; } }
        public override Color ImageMarginGradientEnd { get { return _p.MenuBack; } }
        public override Color SeparatorDark { get { return _p.MenuBorder; } }
        public override Color SeparatorLight { get { return _p.MenuBorder; } }
    }

    /// <summary>扁平菜单渲染器：去掉系统 3D 边框与渐变，图标列留白也一并收掉。</summary>
    public class FlatMenuRenderer : ToolStripProfessionalRenderer
    {
        private readonly ThemePalette _p;

        public FlatMenuRenderer(ThemePalette palette) : base(new FlatMenuColorTable(palette))
        {
            _p = palette;
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? _p.MenuFore : _p.TextHint;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            // 菜单窗口已被 MenuRounder 裁成圆角，这里也必须画**圆角**边框 ——
            // 画直角矩形的话，四个角会留下四段"断掉"的直线。
            var g = e.Graphics;
            UiDraw.Setup(g);
            var r = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            UiDraw.DrawRound(g, r, UiMetrics.RadiusCard, _p.MenuBorder);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var g = e.Graphics;
            using (var pen = new Pen(_p.MenuBorder))
                g.DrawLine(pen, 8, e.Item.Height / 2, e.Item.Width - 8, e.Item.Height / 2);
        }
    }

    /// <summary>
    /// 把右键菜单裁成圆角。
    ///
    /// 原理：ToolStripDropDown 是**独立的顶层窗口**，所以直接设 Region 就能让窗口本身变圆角；
    /// 而系统的窗口投影是按 Region 生成的，圆角会自动带着阴影，不必自己画。
    /// 这就是 WinKit 的 ContextMenu（CornerRadius 8 + DropShadowEffect）在 WinForms 里的对应做法。
    ///
    /// 尺寸必须等 Opened 才准 —— 菜单高度是按项数自适应算出来的，构造时量到的是错的。
    /// 关闭时把 Region 还回去，免得下次菜单项变了还按旧尺寸裁。
    /// </summary>
    internal static class MenuRounder
    {
        private static readonly System.Collections.Generic.HashSet<ContextMenuStrip> _attached =
            new System.Collections.Generic.HashSet<ContextMenuStrip>();

        public static void Attach(ContextMenuStrip menu)
        {
            // 幂等：换肤会被反复调用，重复挂事件会让 Region 被设很多遍并闪烁
            if (menu == null || !_attached.Add(menu)) return;
            menu.Opened += (s, e) => Apply(menu);
            menu.Closed += (s, e) => Release(menu);
        }

        private static void Apply(ContextMenuStrip menu)
        {
            try
            {
                var sz = menu.Size;
                if (sz.Width <= 4 || sz.Height <= 4) return;
                var old = menu.Region;
                menu.Region = NativeMethods.BuildRoundRegion(sz, UiMetrics.RadiusCard);
                if (old != null) old.Dispose();
            }
            catch { }
        }

        private static void Release(ContextMenuStrip menu)
        {
            try
            {
                var old = menu.Region;
                if (old != null) { menu.Region = null; old.Dispose(); }
            }
            catch { }
        }
    }
}
