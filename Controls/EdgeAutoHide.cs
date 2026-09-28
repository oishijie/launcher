using System;
using System.Drawing;
using System.Windows.Forms;

namespace launcher.Controls
{
    /// <summary>
    /// 窗口贴边自动隐藏（借鉴亦安 edge_auto_hide.rs）。
    ///
    /// 行为：把面板拖到屏幕左/右/上边缘附近并停稳 → 自动缩进去，只留一条边；
    /// 鼠标移上去停一小会儿 → 再滑出来。
    ///
    /// 几个刻意的取值（与亦安一致，都是手感调出来的）：
    ///   · 吸附阈值 18px：太大会误吸，太小要怼到屏幕边才认；
    ///   · 露出 12px：不留一条边，缩进去以后鼠标就找不着门了；
    ///   · 悬停 500ms 才弹出：擦边而过的鼠标不该把窗口弹出来；
    ///   · 停稳 280ms 才吸附：拖拽途中路过屏幕边不该触发；
    ///   · 缓动 160ms ease-in-out：太快像跳、太慢像卡。
    ///
    /// 只在窗口可见时跑计时器（16ms ≈ 60fps），不可见就停掉，不留常驻空转。
    /// </summary>
    internal sealed class EdgeAutoHide : IDisposable
    {
        private enum DockEdge { None, Left, Right, Top }

        private const int SnapThreshold = 18;   // 距工作区边缘多少像素内算「贴边」
        private const int VisibleStrip = 12;    // 缩进去后保留的可见条宽
        private const int HotzonePad = 8;       // 唤出热区在露出条外的额外外扩
        private const int HoverDelayMs = 500;   // 鼠标停在热区多久才弹出
        private const int StableMs = 280;       // 窗口停稳多久才允许吸附
        private const int SlideMs = 160;        // 滑入/滑出时长
        private const int WarmupMs = 1200;      // 启动后多久才开始吸附（避免开机就贴边缩进去）

        private readonly Form _form;
        private readonly Timer _timer;          // WinForms Timer：这是纯 UI 交互行为，本就该在 UI 线程跑

        private DockEdge _edge = DockEdge.None;
        private bool _docked;
        private int _restoreX, _restoreY;       // 缩进前的位置，弹回时优先用

        // 动画状态
        private bool _animating;
        private bool _animDocking;
        private int _fromX, _fromY, _toX, _toY;
        private DateTime _animStart;

        // 贴边判定状态
        private Point _lastPos = new Point(int.MinValue, int.MinValue);
        private DateTime _lastMoveAt = DateTime.MinValue;
        private DateTime _hoverSince = DateTime.MinValue;
        private readonly DateTime _bornAt = DateTime.Now;

        public bool Enabled { get; set; }

        /// <summary>
        /// 处于缩进/滑入滑出中。此时主面板**不能**因失焦而隐藏 ——
        /// 否则缩进去留的那条边也被藏了，鼠标再没有东西可以停靠，功能等于废掉。
        /// </summary>
        public bool KeepVisible { get { return _docked || _animating; } }

        public EdgeAutoHide(Form form)
        {
            _form = form;
            _timer = new Timer { Interval = 16 };
            _timer.Tick += OnTick;
        }

        /// <summary>窗口可见时调用。</summary>
        public void Start()
        {
            if (!_timer.Enabled) _timer.Start();
        }

        /// <summary>窗口隐藏时调用（省掉空转）。</summary>
        public void Pause()
        {
            _timer.Stop();
            _hoverSince = DateTime.MinValue;
        }

        /// <summary>热键/托盘呼出前调用：还缩着就整窗弹回工作区，不播动画。</summary>
        public void Undock()
        {
            if (!_docked && !_animating) return;
            _animating = false;
            _docked = false;
            _edge = DockEdge.None;
            _hoverSince = DateTime.MinValue;
            _lastPos = new Point(int.MinValue, int.MinValue);
            _lastMoveAt = DateTime.Now;

            // 恢复到吸附前的位置；越界就夹回工作区
            var wa = WorkArea;
            int x = _restoreX, y = _restoreY;
            x = Math.Min(Math.Max(x, wa.Left), Math.Max(wa.Left, wa.Right - _form.Width));
            y = Math.Min(Math.Max(y, wa.Top), Math.Max(wa.Top, wa.Bottom - _form.Height));
            _form.Location = new Point(x, y);
        }

        private Rectangle WorkArea
        {
            get
            {
                try { return Screen.FromControl(_form).WorkingArea; }
                catch { return Screen.PrimaryScreen.WorkingArea; }
            }
        }

        private void OnTick(object sender, EventArgs e)
        {
            try { Tick(); }
            catch { /* 位置/屏幕变化竞态，跳过这一帧即可 */ }
        }

        private void Tick()
        {
            if (_form == null || _form.IsDisposed || !_form.IsHandleCreated) return;

            // 1) 动画优先，播完再说别的
            if (_animating) { StepAnimation(); return; }

            // 2) 开关关掉后，缩着的要弹回来
            if (!Enabled) { if (_docked) Undock(); return; }

            // 3) 窗口不可见/最小化/最大化时不参与
            if (!_form.Visible) return;
            if (_form.WindowState != FormWindowState.Normal) { if (_docked) Undock(); return; }

            // 4) 刚启动的一小段时间不吸附：启动位置本身可能就贴着屏幕边，
            //    不设这个冷静期，程序一开就自己缩进去了。
            if ((DateTime.Now - _bornAt).TotalMilliseconds < WarmupMs) return;

            var cur = _form.Location;

            // 5) 已缩进：只看鼠标有没有进热区
            if (_docked)
            {
                if (CursorInHotzone())
                {
                    if (_hoverSince == DateTime.MinValue) _hoverSince = DateTime.Now;
                    else if ((DateTime.Now - _hoverSince).TotalMilliseconds >= HoverDelayMs)
                        StartSlide(ExpandTarget(), false);
                }
                else _hoverSince = DateTime.MinValue;
                return;
            }
            _hoverSince = DateTime.MinValue;

            // 6) 鼠标压在窗口上（多半正在拖）→ 别动它
            if (CursorOverWindow(cur))
            {
                _lastPos = cur;
                _lastMoveAt = DateTime.Now;
                return;
            }

            // 7) 窗口还在移动 → 刷新「停稳」计时
            if (_lastPos != cur)
            {
                _lastPos = cur;
                _lastMoveAt = DateTime.Now;
                return;
            }

            // 8) 停稳够久才判定，否则拖拽途中路过屏幕边就会误吸
            if ((DateTime.Now - _lastMoveAt).TotalMilliseconds < StableMs) return;

            var edge = DetectEdge(cur);
            if (edge == DockEdge.None) return;

            _edge = edge;
            _restoreX = cur.X;
            _restoreY = cur.Y;
            StartSlide(HiddenTarget(edge, cur), true);
        }

        private DockEdge DetectEdge(Point loc)
        {
            var wa = WorkArea;
            bool nearLeft = loc.X <= wa.Left + SnapThreshold;
            bool nearRight = loc.X + _form.Width >= wa.Right - SnapThreshold;
            bool nearTop = loc.Y <= wa.Top + SnapThreshold;

            if (nearLeft && !nearRight) return DockEdge.Left;
            if (nearRight && !nearLeft) return DockEdge.Right;
            if (nearTop && !nearLeft && !nearRight) return DockEdge.Top;
            return DockEdge.None;
        }

        // 缩进去的目标位置：露出 VisibleStrip 宽度的一条边
        private Point HiddenTarget(DockEdge edge, Point cur)
        {
            var wa = WorkArea;
            switch (edge)
            {
                case DockEdge.Left: return new Point(wa.Left - _form.Width + VisibleStrip, cur.Y);
                case DockEdge.Right: return new Point(wa.Right - VisibleStrip, cur.Y);
                case DockEdge.Top: return new Point(cur.X, wa.Top - _form.Height + VisibleStrip);
                default: return cur;
            }
        }

        // 弹出来的目标位置：整窗回到工作区内，贴边那条轴贴齐边缘
        private Point ExpandTarget()
        {
            var wa = WorkArea;
            int x = _restoreX, y = _restoreY;
            switch (_edge)
            {
                case DockEdge.Left: x = wa.Left; break;
                case DockEdge.Right: x = wa.Right - _form.Width; break;
                case DockEdge.Top: y = wa.Top; break;
            }
            x = Math.Min(Math.Max(x, wa.Left), Math.Max(wa.Left, wa.Right - _form.Width));
            y = Math.Min(Math.Max(y, wa.Top), Math.Max(wa.Top, wa.Bottom - _form.Height));
            return new Point(x, y);
        }

        private void StartSlide(Point target, bool docking)
        {
            var from = _form.Location;
            if (from == target)
            {
                _form.Location = target;
                FinishSlide(docking, target);
                return;
            }
            _fromX = from.X; _fromY = from.Y;
            _toX = target.X; _toY = target.Y;
            _animStart = DateTime.Now;
            _animDocking = docking;
            _animating = true;
        }

        private void StepAnimation()
        {
            double t = (DateTime.Now - _animStart).TotalMilliseconds / SlideMs;
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            double e = EaseInOutCubic(t);
            int x = (int)Math.Round(_fromX + (_toX - _fromX) * e);
            int y = (int)Math.Round(_fromY + (_toY - _fromY) * e);
            _form.Location = new Point(x, y);
            if (t >= 1.0) FinishSlide(_animDocking, new Point(_toX, _toY));
        }

        private void FinishSlide(bool docking, Point final)
        {
            _animating = false;
            _docked = docking;
            if (docking)
            {
                _lastPos = final;
                _lastMoveAt = DateTime.Now;
            }
            else
            {
                _edge = DockEdge.None;
                _lastPos = final;
                _lastMoveAt = DateTime.Now;
            }
            _hoverSince = DateTime.MinValue;
        }

        private static double EaseInOutCubic(double t)
        {
            if (t < 0.5) return 4.0 * t * t * t;
            double u = -2.0 * t + 2.0;
            return 1.0 - (u * u * u) / 2.0;
        }

        private bool CursorOverWindow(Point loc)
        {
            var p = Cursor.Position;
            return p.X >= loc.X && p.X < loc.X + _form.Width
                && p.Y >= loc.Y && p.Y < loc.Y + _form.Height;
        }

        // 缩进态的「唤出门」：只认露出条那一条窄带，擦不着也就算了
        private bool CursorInHotzone()
        {
            var p = Cursor.Position;
            var wa = WorkArea;
            int winL = _form.Left, winT = _form.Top;
            int winR = winL + _form.Width, winB = winT + _form.Height;
            int y0 = winT - HotzonePad, y1 = winB + HotzonePad;
            int x0 = winL - HotzonePad, x1 = winR + HotzonePad;

            switch (_edge)
            {
                case DockEdge.Left:
                    return p.X >= wa.Left - HotzonePad
                        && p.X <= wa.Left + VisibleStrip + HotzonePad
                        && p.Y >= y0 && p.Y <= y1;
                case DockEdge.Right:
                    return p.X >= wa.Right - VisibleStrip - HotzonePad
                        && p.X <= wa.Right + HotzonePad
                        && p.Y >= y0 && p.Y <= y1;
                case DockEdge.Top:
                    return p.Y >= wa.Top - HotzonePad
                        && p.Y <= wa.Top + VisibleStrip + HotzonePad
                        && p.X >= x0 && p.X <= x1;
                default:
                    return false;
            }
        }

        public void Dispose()
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Tick -= OnTick;
                _timer.Dispose();
            }
        }
    }
}
