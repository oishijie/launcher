using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace launcher.Core
{
    /// <summary>
    /// Win10 没有 DWM 原生圆角，只能退回 Region 裁剪；而 Region 是 1 位掩码，
    /// 圆角边缘必然是整像素台阶 —— 这就是"锯齿明显"的根因，靠调半径治不好。
    ///
    /// 这里用「假抗锯齿」把台阶消掉，全程不碰分层窗口（不牺牲重绘性能）：
    ///   1. 从窗口外沿采样屏幕背景色（桌面／壁纸），作为圆角外侧的底色；
    ///   2. 用 GDI+ 抗锯齿把圆角画在这层底色之上 —— 边缘像素自然变成
    ///      「窗口色 × 桌面色」的混合色，等价于半透明抗锯齿；
    ///   3. Region 半径比绘制半径大 1px，让那条硬裁边落在「采样到的桌面色」上，
    ///      于是硬边两侧同色，肉眼看不到边界。
    ///
    /// 采样失败（窗口贴屏边、坐标越界）时底色退化为窗口自身颜色，效果回到改动前，
    /// 不会比原来更差。
    ///
    /// 关于窗口阴影（2026-09-28）：全项目窗口**一律不加** CS_DROPSHADOW。
    /// 该 class style 是窗口类级的**矩形**投影，而这里走 Region 圆角裁剪 —— 投影不跟随
    /// 圆角，四角会留方角残影（圆角越小越明显）。Win10 上"圆角"与"系统投影"互斥：
    /// 要投影就只能用矩形窗口。用户的取舍是保留圆角、不要阴影，故此路径不再挂投影。
    /// </summary>
    internal static class WindowCorners
    {
        private const int SampleMargin = 4;          // 采样点离窗口边缘的距离（像素）
        private const uint CLR_INVALID = 0xFFFFFFFF; // GetPixel 失败返回值

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern uint GetPixel(IntPtr hdc, int nXPos, int nYPos);

        /// <summary>
        /// 采样窗口四角外侧的屏幕像素，取平均值作为圆角外侧的底色。
        /// 全部采样失败时返回 <see cref="Color.Empty"/>。
        /// </summary>
        public static Color SampleBackdrop(Rectangle screenBounds)
        {
            if (screenBounds.Width <= 0 || screenBounds.Height <= 0) return Color.Empty;

            var screen = SystemInformation.VirtualScreen;
            IntPtr dc = IntPtr.Zero;
            try
            {
                dc = GetDC(IntPtr.Zero);
                if (dc == IntPtr.Zero) return Color.Empty;

                int m = SampleMargin;
                int l = screenBounds.Left - m, t = screenBounds.Top - m;
                int r = screenBounds.Right - 1 + m, b = screenBounds.Bottom - 1 + m;
                var pts = new[]
                {
                    new Point(l, t), new Point(r, t),
                    new Point(l, b), new Point(r, b)
                };

                int sumR = 0, sumG = 0, sumB = 0, n = 0;
                foreach (var p in pts)
                {
                    int x = Math.Min(Math.Max(p.X, screen.Left), screen.Right - 1);
                    int y = Math.Min(Math.Max(p.Y, screen.Top), screen.Bottom - 1);
                    // 贴屏幕边缘时坐标会被拉回屏内，若拉回后落在窗口自己身上就没意义了
                    if (screenBounds.Contains(x, y)) continue;

                    uint c = GetPixel(dc, x, y);
                    if (c == CLR_INVALID) continue;
                    sumR += (int)(c & 0xFF);
                    sumG += (int)((c >> 8) & 0xFF);
                    sumB += (int)((c >> 16) & 0xFF);
                    n++;
                }
                if (n == 0) return Color.Empty;
                return Color.FromArgb(sumR / n, sumG / n, sumB / n);
            }
            catch { return Color.Empty; }
            finally { if (dc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, dc); }
        }

        /// <summary>
        /// 把一个 GraphicsPath 填成 size 范围内的圆角矩形（抗锯齿由调用方设置）。
        /// 边界取 size 本身（不是 size-1）：GDI+ 的像素中心在 (x+0.5, y+0.5)，
        /// 用 size 才能和 Region 的扫描线边界对齐。
        /// </summary>
        public static GraphicsPath BuildPath(Size size, int radius)
        {
            var path = new GraphicsPath();
            int d = Math.Max(2, Math.Min(radius, Math.Min(size.Width, size.Height) / 2) * 2);
            path.AddArc(0, 0, d, d, 180, 90);                              // 左上
            path.AddArc(size.Width - d, 0, d, d, 270, 90);                 // 右上
            path.AddArc(size.Width - d, size.Height - d, d, d, 0, 90);     // 右下
            path.AddArc(0, size.Height - d, d, d, 90, 90);                 // 左下
            path.CloseFigure();
            return path;
        }

        /// <summary>
        /// 构造窗口 Region：半径比绘制半径大 1px，把硬裁边推到「采样底色」那一圈上。
        /// </summary>
        public static Region BuildRegion(Size size, int radius)
        {
            return NativeMethods.BuildRoundRegion(size, radius + 1);
        }
    }

    /// <summary>
    /// 单个窗口的圆角绘制状态：缓存采样到的桌面底色。
    /// 每个无边框窗体持有一个实例，在其 OnPaintBackground 里调用 <see cref="PaintBackground"/>。
    /// </summary>
    internal sealed class WindowCornerState
    {
        private readonly int _radiusDip;
        private Color _backdrop = Color.Empty;
        private Rectangle _sampledFor = Rectangle.Empty;

        public WindowCornerState(int radiusDip)
        {
            _radiusDip = radiusDip;
        }

        /// <summary>当前缓存的桌面底色（采样失败为 Color.Empty）。</summary>
        public Color Backdrop { get { return _backdrop; } }

        /// <summary>窗口 1px 外描边色。默认不画；由宿主窗体在换肤时写入。</summary>
        public Color BorderColor = Color.Empty;

        /// <summary>按窗口 DPI 换算出的绘制半径（DIP → 物理像素）。</summary>
        public int Radius(Control c)
        {
            return c == null ? _radiusDip : (int)Math.Round(_radiusDip * (c.DeviceDpi / 96.0));
        }

        // 窗口位置或尺寸变了就重新采样一次。窗口拖动时每次重绘都走这里，
        // 成本是 1 次 GetDC + 4 次 GetPixel（约几十微秒），可以忽略。
        private void Refresh(Form f)
        {
            var b = f.Bounds;
            if (b == _sampledFor) return;
            _sampledFor = b;
            _backdrop = WindowCorners.SampleBackdrop(b);
        }

        /// <summary>
        /// 画窗口背景：先铺桌面底色，再叠抗锯齿圆角。
        /// 局部重绘（Invalidate 只脏一块）时结果同样正确 —— 两层都是确定性绘制。
        /// </summary>
        public void PaintBackground(Form f, Graphics g)
        {
            Refresh(f);

            var size = f.ClientSize;
            if (size.Width <= 0 || size.Height <= 0) return;

            var prevMode = g.SmoothingMode;
            var prevOffset = g.PixelOffsetMode;
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                Color under = _backdrop.IsEmpty ? f.BackColor : _backdrop;
                using (var b = new SolidBrush(under))
                    g.FillRectangle(b, 0, 0, size.Width, size.Height);

                using (var path = WindowCorners.BuildPath(size, Radius(f)))
                using (var brush = new SolidBrush(f.BackColor))
                    g.FillPath(brush, path);

                // 1px 外描边。Region 半径比绘制半径大 1px，所以这圈线不会被硬裁边啃掉；
                // 用 Inset 让描边压在路径内侧，边缘不会半个像素露在裁剪区外。
                if (!BorderColor.IsEmpty)
                {
                    using (var path = WindowCorners.BuildPath(size, Radius(f)))
                    using (var pen = new Pen(BorderColor, UiMetrics.WindowBorderW))
                    {
                        pen.Alignment = PenAlignment.Inset;
                        g.DrawPath(pen, path);
                    }
                }
            }
            catch
            {
                // 绘制失败时退回最朴素的纯色填充，保证窗口不会花屏
                try
                {
                    using (var b = new SolidBrush(f.BackColor))
                        g.FillRectangle(b, 0, 0, size.Width, size.Height);
                }
                catch { }
            }
            finally
            {
                g.SmoothingMode = prevMode;
                g.PixelOffsetMode = prevOffset;
            }
        }
    }
}
