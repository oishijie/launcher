using System;
using System.Drawing;
using System.Windows.Forms;

namespace launcher.Core
{
    /// <summary>
    /// 无边框窗口的通用「窗口装饰行为」：命中测试（拖动 / 缩放）与整窗拖动。
    ///
    /// 为什么要有这个类：全项目有 5 个 FormBorderStyle.None 的窗口，每个都自己写了一份
    /// WM_NCHITTEST，而且写得不完全一样 ——
    ///   · 主面板：整个客户区默认 HTCAPTION（图标网格的间隙也能拖），边缘另给缩放区；
    ///   · 便签：只把「标题栏高以内」当拖动区，边缘给缩放区；
    ///   · 设置面板 / 两个对话框：只把标题栏当拖动区，且用了一个魔数 56 当作右边界，
    ///     并且完全不支持边缘缩放（也就是说连"这窗口能不能拉大"都要逐个去翻代码才知道）。
    ///   · 取坐标的方式也有两套：三个窗口用 Cursor.Position，两个对话框用 WM_NCHITTEST
    ///     的 LParam。后者才是对的 —— LParam 是系统实际做命中测试的那个点，
    ///     而 Cursor.Position 在拖动/消息排队时可能已经漂到别处。
    ///
    /// 这里把「边缘缩放区」「标题栏判定」「整窗拖动」三件事各收成一个纯函数，
    /// 各窗口只声明自己要不要缩放、标题栏多高，行为差异就只剩这一句话。
    /// </summary>
    internal static class WindowChrome
    {
        /// <summary>
        /// 从 WM_NCHITTEST 的 LParam 取出命中点（屏幕坐标）并转成客户区坐标。
        /// 用 LParam 而非 Cursor.Position：前者是系统真正做命中测试的那个点，
        /// 后者在消息排队/拖动过程中可能已经漂走，会把"拖窗口"判成"拖边缘"。
        /// </summary>
        public static Point HitPoint(Form form, IntPtr lParam)
        {
            if (form == null) return Point.Empty;
            int lp = lParam.ToInt32();
            // 低 16 位 = X，高 16 位 = Y，均为**有符号** —— 副屏在主屏左侧时 X 为负，
            // 必须先转 short 再补符号，否则副屏上拖动会算到屏幕外。
            int sx = (short)(lp & 0xFFFF);
            int sy = (short)((lp >> 16) & 0xFFFF);
            return form.PointToClient(new Point(sx, sy));
        }

        /// <summary>
        /// 边缘 / 四角的缩放命中区。命中返回对应的 HT* 常量，未命中返回 0
        /// （0 不是合法的命中码，调用方据此判断"这一击不归缩放管"）。
        /// </summary>
        public static int ResizeHit(Point p, Size size)
        {
            const int g = NativeMethods.ResizeGrip;
            if (size.Width <= g * 2 || size.Height <= g * 2) return 0;   // 窗口太小，整个都是边缘 → 干脆不给缩放

            bool left = p.X <= g, right = p.X >= size.Width - g;
            bool top = p.Y <= g, bottom = p.Y >= size.Height - g;

            if (top && left) return NativeMethods.HTTOPLEFT;
            if (top && right) return NativeMethods.HTTOPRIGHT;
            if (bottom && left) return NativeMethods.HTBOTTOMLEFT;
            if (bottom && right) return NativeMethods.HTBOTTOMRIGHT;
            if (left) return NativeMethods.HTLEFT;
            if (right) return NativeMethods.HTRIGHT;
            if (top) return NativeMethods.HTTOP;
            if (bottom) return NativeMethods.HTBOTTOM;
            return 0;
        }

        /// <summary>
        /// 命中点是否落在标题栏（可拖动区）。
        /// 不需要传"右边界"：标题栏右侧的按钮是**子控件**，鼠标落在它们身上时
        /// WM_NCHITTEST 由子控件自己应答，根本不会走到宿主窗口的 WndProc ——
        /// 所以"别把按钮区当拖动区"这件事是系统替我们保证的，不必手写坐标判断
        /// （原先两个对话框那个 `X &lt; Width - 56` 的魔数正源于此误解）。
        /// 下限取 -1 而非 0：窗口顶边那一像素常被用于缩放，命中时不该同时算作标题栏。
        /// </summary>
        public static bool IsCaption(Point p, int titleBarH)
        {
            return p.Y >= 0 && p.Y < titleBarH;
        }

        /// <summary>
        /// 发起整窗拖动。等价于"按住标题栏拖动"，但绕开了 HitTest ——
        /// 用于没有标题栏可抓的场景（例如主面板的图标网格间隙、或某个自定义标题 Label）。
        /// </summary>
        public static void StartDrag(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return;
            try
            {
                NativeMethods.ReleaseCapture();
                NativeMethods.SendMessage(handle, NativeMethods.WM_NCLBUTTONDOWN,
                    (IntPtr)NativeMethods.HTCAPTION, IntPtr.Zero);
            }
            catch { /* 拖动失败不该影响主流程 */ }
        }
    }
}
