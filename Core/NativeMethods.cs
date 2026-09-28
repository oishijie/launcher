using System;
using System.Runtime.InteropServices;

namespace launcher.Core
{
    // Win32 P/Invoke 声明与常量集中管理（从 Form1 抽取，消除 UI 层对 Win32 API 的直接依赖）
    internal static class NativeMethods
    {
        // 窗口消息常量
        public const int WM_NCHITTEST = 0x84;
        public const int HTCLIENT = 0x1;
        public const int HTCAPTION = 0x2;
        public const int HTLEFT = 10;
        public const int HTRIGHT = 11;
        public const int HTTOP = 12;
        public const int HTTOPLEFT = 13;
        public const int HTTOPRIGHT = 14;
        public const int HTBOTTOM = 15;
        public const int HTBOTTOMLEFT = 16;
        public const int HTBOTTOMRIGHT = 17;
        public const int WM_HOTKEY = 0x0312;
        public const int WM_NCLBUTTONDOWN = 0x00A1;

        // 全局快捷键 ID（主面板/便签/主题切换）
        public const int HOTKEY_ID_MAIN = 1;
        public const int HOTKEY_ID_NOTES = 2;
        public const int HOTKEY_ID_THEME = 3;

        // 修饰键常量
        public const int MOD_CTRL = 0x0002;
        public const int MOD_SHIFT = 0x0004;
        public const int MOD_NOREPEAT = 0x4000;

        // 窗口缩放边缘宽度（像素）
        public const int ResizeGrip = 6;

        // P/Invoke 声明
        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        // ===== Win11 无边框窗口圆角（DWM 原生）=====
        // 不用 Region 裁剪：Region 边缘无抗锯齿，还有锯齿毛边，且会让 DWM 阴影失效。
        // DWM 圆角由系统合成器绘制，抗锯齿、自带投影，且随系统缩放自动适配。
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        /// <summary>
        /// 给无边框窗口套上 Win11 原生圆角。返回 true 表示系统支持并已生效。
        /// Win10 及更早系统上该属性未知，DwmSetWindowAttribute 返回 E_INVALIDARG（非 0），
        /// 借此判定即可，无需做版本探测（Environment.OSVersion 会被 manifest 劫持）。
        /// 注意：窗口一旦设了 WS_EX_LAYERED（不透明度 &lt; 100%），DWM 圆角会失效。
        /// </summary>
        public static bool EnableRoundCorners(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            try
            {
                int pref = DWMWCP_ROUND;
                return DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int)) == 0;
            }
            catch { return false; }
        }

        /// <summary>
        /// 构造圆角矩形 Region —— 供不支持 DWM 圆角的系统（Win10 及更早）兜底裁剪窗口用。
        /// 局限：Region 是 1 位掩码、边缘无抗锯齿，圆角处会有轻微台阶锯齿，这是 Win10 的固有限制，
        /// 没有既抗锯齿又不牺牲性能的第三条路（分层窗口能做到抗锯齿，但会让整个窗口走 DWM alpha 合成，
        /// 代价是重绘性能大幅下降，本项目的启动顿挫修复正是要摆脱分层窗口）。
        /// </summary>
        public static System.Drawing.Region BuildRoundRegion(System.Drawing.Size size, int radius)
        {
            if (size.Width <= 0 || size.Height <= 0) return null;
            int d = Math.Max(2, Math.Min(radius, Math.Min(size.Width, size.Height) / 2) * 2);
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            try
            {
                path.AddArc(0, 0, d, d, 180, 90);                                  // 左上
                path.AddArc(size.Width - d, 0, d, d, 270, 90);                     // 右上
                path.AddArc(size.Width - d, size.Height - d, d, d, 0, 90);         // 右下
                path.AddArc(0, size.Height - d, d, d, 90, 90);                     // 左下
                path.CloseFigure();
                return new System.Drawing.Region(path);
            }
            finally { path.Dispose(); }
        }
    }
}
