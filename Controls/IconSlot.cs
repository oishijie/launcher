using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using launcher.Core;
using launcher.Models;

namespace launcher.Controls
{
    // 图标槽位用户控件：替代原先在设计器/代码里散落生成的 pictureBox1..N。
    // 自身携带数据(SlotData)、主题(ThemePalette)与渲染(Render)，并把交互收敛为语义事件，
    // 不再依赖 Form 去翻 Dictionary<PictureBox,...>。
    public class IconSlot : PictureBox
    {
        public int SlotIndex;
        public SlotData Data = new SlotData();
        public Size OriginalSize;
        public ThemePalette Theme;

        public event EventHandler OpenRequested;          // 左键点击（打开）
        public event EventHandler OpenLocationRequested;  // 中键（打开所在位置）
        public event EventHandler DeleteRequested;        // 右键菜单「删除」
        public event EventHandler SetIconRequested;       // 右键菜单「设置自定义图标」
        public event EventHandler ClearIconRequested;     // 右键菜单「清除自定义图标」
        public event EventHandler<string> FileDropped;    // 拖入文件 / http(s) 链接
        public event EventHandler<IconSlot> DropTarget;   // 被另一个槽拖入（移动）
        public event EventHandler DragStarted;            // 开始移动拖拽
        public event EventHandler WindowDragRequested;    // 空槽被拖动：请求移动整个面板

private Point? _dragStart;
        private const int DragThreshold = 5;
        private ContextMenuStrip _menu;
        private ToolStripMenuItem _setIconItem;
        private ToolStripMenuItem _clearIconItem;
        private ToolStripMenuItem _deleteItem;
        private ToolTip _tooltip;

        // 拖拽排序占位预览：拖着一个槽在另一个槽上悬停时，目标槽显示"落点"占位框并轻微脉动，
        // 源槽淡化，让用户提前看到交换到哪、而不是松手才瞬移。
        private bool _dragPreviewActive;   // 本槽正作为拖拽落点
        private bool _dragSourceActive;    // 本槽正被拖着（源）
        private DateTime _previewStart = DateTime.MinValue;

        public IconSlot()
        {
            AllowDrop = true;
            MouseDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseUp += OnMouseUp;
            MouseClick += OnMouseClick;
            MouseEnter += OnMouseEnter;
            MouseLeave += OnMouseLeave;
            DragEnter += OnDragEnter;
            DragOver += OnDragOver;
            DragLeave += OnDragLeave;
            DragDrop += OnDragDrop;

            // 右键上下文菜单：空槽禁用「设置/清除图标」与「删除」
            _setIconItem = new ToolStripMenuItem("设置自定义图标...");
            _clearIconItem = new ToolStripMenuItem("清除自定义图标");
            _deleteItem = new ToolStripMenuItem("删除");
            _setIconItem.Click += (s, e) => SetIconRequested?.Invoke(this, EventArgs.Empty);
            _clearIconItem.Click += (s, e) => ClearIconRequested?.Invoke(this, EventArgs.Empty);
            _deleteItem.Click += (s, e) => DeleteRequested?.Invoke(this, EventArgs.Empty);
            _menu = new ContextMenuStrip();
            _menu.Items.Add(_setIconItem);
            _menu.Items.Add(_clearIconItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_deleteItem);
            _menu.Opening += (s, e) =>
            {
                bool empty = Data == null || Data.IsEmpty;
                _setIconItem.Enabled = !empty;
                _deleteItem.Enabled = !empty;
                _clearIconItem.Enabled = !empty && !string.IsNullOrEmpty(Data?.IconPath);
            };
            ContextMenuStrip = _menu;
        }

        public void ApplyTheme(ThemePalette palette)
        {
            Theme = palette;
            BackColor = palette.SlotBack;
        }

        public void Render()
        {
            if (Data == null || Data.IsEmpty) { Image = null; return; }
            Image = IconRenderer.RenderToBitmap(Width, Height, Data.FilePath, Theme, Data.IconPath);
        }

        // 悬停提示：URL 显示友好域名名（见 SearchEngine.UrlLabel），文件显示文件名（不含路径）
        private string TooltipText()
        {
            if (Data == null || Data.IsEmpty) return string.Empty;
            if (UrlUtil.IsHttpUrl(Data.FilePath))
            {
                string friendly = SearchEngine.UrlLabel(Data.FilePath);
                return string.IsNullOrEmpty(friendly) ? Data.FilePath : friendly;
            }
            string label = SearchEngine.SlotLabel(Data);
            return string.IsNullOrEmpty(label) ? Data.FilePath : label;
        }

        private void OnMouseEnter(object sender, EventArgs e)
        {
            string text = TooltipText();
            if (string.IsNullOrEmpty(text)) return;
            if (_tooltip == null) _tooltip = new ToolTip();
            _tooltip.SetToolTip(this, text);
        }

        private void OnMouseLeave(object sender, EventArgs e)
        {
            if (_tooltip != null) _tooltip.Hide(this);
        }

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) _dragStart = e.Location;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragStart.HasValue && e.Button == MouseButtons.Left)
            {
                int dx = Math.Abs(e.X - _dragStart.Value.X);
                int dy = Math.Abs(e.Y - _dragStart.Value.Y);
                if (dx > DragThreshold || dy > DragThreshold)
                {
                    if (!Data.IsEmpty)
                    {
                        _dragSourceActive = true;
                        _previewStart = DateTime.Now;
                        Invalidate();
                        DragStarted?.Invoke(this, EventArgs.Empty);
                        DoDragDrop(this, DragDropEffects.Move);
                        // 拖拽结束（落点 / 取消 / 松开），清掉源高亮与目标占位
                        _dragSourceActive = false;
                        _dragPreviewActive = false;
                        Invalidate();
                    }
                    else
                    {
                        // 空槽拖动 = 移动整个面板（复用窗口 HTCAPTION 手动拖动）
                        WindowDragRequested?.Invoke(this, EventArgs.Empty);
                    }
                    _dragStart = null;
                }
            }
        }

        private void OnMouseUp(object sender, MouseEventArgs e)
        {
            // 不在 MouseUp 触发打开：双击时 Windows 会产生两次 MouseUp，
            // 会误开两个实例。打开改由 OnMouseClick 在 e.Clicks==1（纯单击）时处理。
            _dragStart = null;
        }

        private void OnMouseClick(object sender, MouseEventArgs e)
        {
            // 右键由 ContextMenuStrip 处理（设置/清除图标、删除）
            if (e.Button == MouseButtons.Middle)
            {
                if (!Data.IsEmpty) OpenLocationRequested?.Invoke(this, EventArgs.Empty);
            }
            else if (e.Button == MouseButtons.Left && e.Clicks == 1)
            {
                // 仅纯单击（Clicks==1）打开；双击的第二次 Clicks==2 被忽略，
                // 避免手抖双击一次就触发两次打开。拖拽场景下 WinForms 不会生成 Click，故不影响移动。
                if (!Data.IsEmpty) OpenRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
            }
            else if (e.Data.GetDataPresent(typeof(IconSlot)))
            {
                e.Effect = DragDropEffects.Move;
                // 槽位间拖拽：本槽即潜在落点，开始占位预览
                IconSlot src = e.Data.GetData(typeof(IconSlot)) as IconSlot;
                if (src != null && src != this)
                {
                    _dragPreviewActive = true;
                    _previewStart = DateTime.Now;
                    Invalidate();
                }
            }
            else if (e.Data.GetDataPresent(DataFormats.UnicodeText) || e.Data.GetDataPresent(DataFormats.Text))
            {
                string text = (e.Data.GetData(DataFormats.UnicodeText) ??
                               e.Data.GetData(DataFormats.Text) ?? string.Empty).ToString();
                if (UrlUtil.IsHttpUrl(text))
                {
                    e.Effect = DragDropEffects.Copy;
                }
            }
        }

        // 拖拽持续触发：保持目标允许 Move，并让占位框脉动刷新
        private void OnDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(IconSlot)))
            {
                e.Effect = DragDropEffects.Move;
                if (_dragPreviewActive) Invalidate();
            }
            else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
            }
            else if (e.Data.GetDataPresent(DataFormats.UnicodeText) || e.Data.GetDataPresent(DataFormats.Text))
            {
                string text = (e.Data.GetData(DataFormats.UnicodeText) ??
                               e.Data.GetData(DataFormats.Text) ?? string.Empty).ToString();
                if (UrlUtil.IsHttpUrl(text)) e.Effect = DragDropEffects.Copy;
            }
        }

        // 拖出本槽：取消占位预览
        private void OnDragLeave(object sender, EventArgs e)
        {
            if (_dragPreviewActive)
            {
                _dragPreviewActive = false;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_dragSourceActive)
            {
                // 源槽淡化，示意"已被提起"（松手才会换位）
                using (var brush = new SolidBrush(Color.FromArgb(140, Theme?.SlotBack ?? Color.Black)))
                    e.Graphics.FillRectangle(brush, ClientRectangle);
            }
            if (_dragPreviewActive)
            {
                // 落点占位：虚线框 + 呼吸高亮，脉动随拖动持续刷新
                var accent = Theme?.Highlight ?? Color.DodgerBlue;
                int alpha = 90 + (int)(40 * Math.Sin((DateTime.Now - _previewStart).TotalMilliseconds / 160.0));
                alpha = Math.Max(60, Math.Min(160, alpha));
                using (var brush = new SolidBrush(Color.FromArgb(alpha, accent)))
                    e.Graphics.FillRectangle(brush, 1, 1, Width - 2, Height - 2);
                using (var pen = new Pen(accent, 2f) { DashStyle = DashStyle.Dash })
                    e.Graphics.DrawRectangle(pen, 2, 2, Width - 5, Height - 5);
            }
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0) FileDropped?.Invoke(this, files[0]);
            }
            else if (e.Data.GetDataPresent(typeof(IconSlot)))
            {
                _dragPreviewActive = false;
                Invalidate();
                IconSlot src = e.Data.GetData(typeof(IconSlot)) as IconSlot;
                if (src != null && src != this) DropTarget?.Invoke(this, src);
            }
            else if (e.Data.GetDataPresent(DataFormats.UnicodeText) || e.Data.GetDataPresent(DataFormats.Text))
            {
                string text = (e.Data.GetData(DataFormats.UnicodeText) ??
                               e.Data.GetData(DataFormats.Text) ?? string.Empty).ToString().Trim();
                if (UrlUtil.IsHttpUrl(text)) FileDropped?.Invoke(this, text);
            }
        }
    }
}
