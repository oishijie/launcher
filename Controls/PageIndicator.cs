using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace launcher.Controls
{
    // 分页指示圆点（胶囊样式）：替代原先散落生成的 panel_dot1..N。
    //
    // 相比旧版的变化：
    //   旧：12px 圆点 + 每个圆点正下方挂一个小号页码数字 —— 密、土、噪声大；
    //   新：未选中为 8px 圆点，选中项拉伸成 22px 胶囊（现代分页控件的主流做法），
    //       页名信息改由悬停提示承载，不再常驻占位。
    //
    // 交互语义完全保持：左键 PageSelected(索引)、右键 PageRenameRequested(索引)。
    public class PageIndicator : Control
    {
        private const int DotSize = 9;   // 未选中圆点直径
        private const int CapW = 22;     // 选中胶囊宽度（同时也是每个条目的固定槽宽，避免选中变化引起布局抖动）
        private const int Gap = 8;
        private const int PadY = 4;      // 上下留白（悬停放大时不裁切）

        private int _count = 1;
        private int _selected;
        private int _hover = -1;
        private Color _active = Color.White;
        private Color _inactive = Color.Gray;
        private List<string> _pageNames = new List<string>();
        private ToolTip _tooltip;

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                if (_selected == value) return;
                _selected = value;
                Invalidate();
            }
        }

        public event EventHandler<int> PageSelected;
        public event EventHandler<int> PageRenameRequested;

        public PageIndicator()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(CapW, DotSize + PadY * 2);
        }

        public void Setup(int count, Color active, Color inactive)
        {
            _count = Math.Max(1, count);
            _active = active;
            _inactive = inactive;
            Size = new Size(_count * CapW + (_count - 1) * Gap, DotSize + PadY * 2);
            Invalidate();
        }

        // 同步页名列表（用于悬停提示），并随页数重建布局
        public void SetPageNames(IList<string> names)
        {
            _pageNames = names == null ? new List<string>() : new List<string>(names);
            if (_pageNames.Count != _count) Setup(_pageNames.Count, _active, _inactive);
            else Invalidate();
        }

        private string PageLabel(int i)
        {
            string name = (i >= 0 && i < _pageNames.Count) ? _pageNames[i] : null;
            string defaultName = "第" + (i + 1) + "页"; // 与 ConfigStore 默认页名一致（无空格）
            if (string.IsNullOrWhiteSpace(name) || name == defaultName)
                return "第 " + (i + 1) + " 页";          // 显示仍带空格，更美观
            return "第 " + (i + 1) + " 页 · " + name;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            UiDraw.Setup(g);
            float cy = Height / 2f;

            for (int i = 0; i < _count; i++)
            {
                int slotX = i * (CapW + Gap);
                if (i == _selected)
                {
                    // 选中：拉伸成胶囊。高度与圆点一致，避免切换时产生"跳一下"的观感
                    var r = new Rectangle(slotX, (int)(cy - DotSize / 2f), CapW, DotSize);
                    using (var path = UiDraw.Round(r, DotSize / 2))
                    using (var b = new SolidBrush(_active))
                        g.FillPath(b, path);
                }
                else
                {
                    // 未选中：圆点，悬停时放大并提亮
                    int d = (i == _hover) ? DotSize + 2 : DotSize;
                    float x = slotX + (CapW - d) / 2f;
                    Color c = (i == _hover) ? UiDraw.Blend(_inactive, _active, 0.45f) : _inactive;
                    using (var b = new SolidBrush(c))
                        g.FillEllipse(b, x, cy - d / 2f, d, d);
                }
            }
        }

        private int HitTest(int x)
        {
            int i = x / (CapW + Gap);
            return (i >= 0 && i < _count) ? i : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int idx = HitTest(e.X);
            if (idx != _hover) { _hover = idx; Invalidate(); }

            if (_tooltip == null) _tooltip = new ToolTip();
            if (idx >= 0) _tooltip.SetToolTip(this, PageLabel(idx));
            else _tooltip.Hide(this);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != -1) { _hover = -1; Invalidate(); }
            if (_tooltip != null) _tooltip.Hide(this);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right)
            {
                int idx = HitTest(e.X);
                if (idx >= 0) PageRenameRequested?.Invoke(this, idx);
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            int idx = HitTest(e.X);
            if (idx >= 0) PageSelected?.Invoke(this, idx);
        }
    }
}
