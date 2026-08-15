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
    }
}
