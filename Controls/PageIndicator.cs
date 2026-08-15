using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace launcher.Controls
{
    // 分页指示圆点：替代原先散落生成的 panel_dot1..N。
    // 自己负责绘制与点击命中，发出 PageSelected(页索引) 语义事件。
    // 每个圆点下方绘制页码，悬停显示「第 N 页 · 页名」提示，右键触发 PageRenameRequested 供重命名。
    public class PageIndicator : Control
    {
        private int _count;
        private int _selected;
        private readonly int _dotSize = 12;
        private readonly int _dotGap = 12;
        private Color _active = Color.White;
        private Color _inactive = Color.Gray;
        private List<string> _pageNames = new List<string>();
        private ToolTip _tooltip;

        public int SelectedIndex
        {
            get { return _selected; }
            set { _selected = value; Invalidate(); }
        }

        public event EventHandler<int> PageSelected;
        public event EventHandler<int> PageRenameRequested;

        public void Setup(int count, Color active, Color inactive)
        {
            _count = Math.Max(1, count);
            _active = active;
            _inactive = inactive;
            this.Size = new Size(_count * _dotSize + (_count - 1) * _dotGap, _dotSize + 16);
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
            base.OnPaint(e);
            var numFont = new Font("Microsoft YaHei UI", 7.5f, FontStyle.Regular);
            try
            {
                for (int i = 0; i < _count; i++)
                {
                    int x = i * (_dotSize + _dotGap);
                    using (Brush b = new SolidBrush(i == _selected ? _active : _inactive))
                    {
                        e.Graphics.FillEllipse(b, x + 1, 1, _dotSize, _dotSize);
                    }
                    // 圆点下方页码（深色主题下用浅灰，浅色主题下用深灰，始终可读）
                    using (Brush nb = new SolidBrush(_selected == i ? _active : Color.FromArgb(_inactive.GetBrightness() > 0.5f ? 90 : 170, _inactive)))
                    {
                        string num = (i + 1).ToString();
                        SizeF sz = e.Graphics.MeasureString(num, numFont);
                        e.Graphics.DrawString(num, numFont, nb, x + 1 + (_dotSize - sz.Width) / 2f, _dotSize + 3);
                    }
                }
            }
            finally { numFont.Dispose(); }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int idx = HitTest(e.X);
            if (_tooltip == null) _tooltip = new ToolTip();
            if (idx >= 0 && idx < _count)
                _tooltip.SetToolTip(this, PageLabel(idx));
            else if (idx < 0 || idx >= _count)
                _tooltip.Hide(this);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_tooltip != null) _tooltip.Hide(this);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right)
            {
                int idx = HitTest(e.X);
                if (idx >= 0 && idx < _count) PageRenameRequested?.Invoke(this, idx);
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            int idx = HitTest(e.X);
            if (idx >= 0 && idx < _count) PageSelected?.Invoke(this, idx);
        }

        private int HitTest(int x) => x / (_dotSize + _dotGap);
    }
}
