using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Accessibility;
using launcher.Core;

namespace launcher.Controls
{
    // 全局低级鼠标钩子：监听双击桌面/任务栏空白处，触发 DoubleClickDetected 事件（呼出主面板）。
    // 从 Form1 抽取，单一职责、封装全部 P/Invoke 与命中判定，避免 Form1 过度膨胀、难以阅读。
    internal sealed class DesktopMouseHook : IDisposable
    {
        // 双击桌面/任务栏空白呼出面板（全局低级鼠标钩子）
        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int GA_ROOT = 2;

        private IntPtr _hookHandle = IntPtr.Zero;
        private LowLevelMouseProc _hookProc; // 必须持有引用，防止被 GC 回收导致回调崩溃
        private int _dblClickTime = 500;      // 双击判定间隔（毫秒），安装时用系统 GetDoubleClickTime
        private Size _dblClickRect = new Size(8, 8); // 双击允许的鼠标位移范围
        private bool _lastDownValid;
        private int _lastDownTime;
        private POINTLL _lastDownPt;
        private readonly Func<bool> _isEnabled;
        private readonly ISynchronizeInvoke _owner;

        public event Action DoubleClickDetected;

        // owner：承载 UI 线程消息循环的控件（主窗体）。触发双击时通过它的 BeginInvoke
        // 异步投递到消息循环，避免在低级鼠标钩子回调里同步操作窗口（会导致面板不显示/钩子发飘）。
        public DesktopMouseHook(ISynchronizeInvoke owner, Func<bool> isEnabled)
        {
            _owner = owner;
            _isEnabled = isEnabled ?? (() => true);
        }

        public void Install()
        {
            try
            {
                Uninstall();
                _dblClickTime = (int)GetDoubleClickTime();
                if (_dblClickTime <= 0) _dblClickTime = 500;
                _dblClickRect = new Size(GetSystemMetrics(36) /*SM_CXDOUBLECLK*/, GetSystemMetrics(37) /*SM_CYDOUBLECLK*/);
                if (_dblClickRect.Width <= 0) _dblClickRect.Width = 4;
                if (_dblClickRect.Height <= 0) _dblClickRect.Height = 4;
                _lastDownValid = false;
                _hookProc = MouseHookCallback;
                _hookHandle = SetWindowsHookEx(WH_MOUSE_LL, _hookProc, IntPtr.Zero, 0);
                if (_hookHandle != IntPtr.Zero) Logger.Log("已启用：双击桌面/任务栏空白呼出面板");
                else Logger.Log("双击呼出面板钩子注册失败(SetWindowsHookEx): " + Marshal.GetLastWin32Error());
            }
            catch (Exception ex) { Logger.Log("安装鼠标钩子异常: " + ex.Message); }
        }

        public void Uninstall()
        {
            if (_hookHandle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
            }
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    int msg = (int)wParam;
                    // 配置开关：dblClickShow=false 时不响应
                    bool enabled = _isEnabled();
                    if (enabled && (msg == WM_LBUTTONDOWN || msg == WM_LBUTTONDBLCLK))
                    {
                        var ms = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                        if (IsDesktopOrTaskbar(ms.pt))
                        {
                            // WM_LBUTTONDBLCLK：仅带 CS_DBLCLKS 样式的窗口（如桌面图标层）会生成，直接命中即双击。
                            // 其它窗口（任务栏、无图标桌面）只发两次 WM_LBUTTONDOWN，用时间+距离判定双击。
                            if (msg == WM_LBUTTONDBLCLK) { _lastDownValid = false; RaiseDoubleClick(); return CallNextHookEx(_hookHandle, nCode, wParam, lParam); }

                            int now = Environment.TickCount;
                            bool withinTime = _lastDownValid && now - _lastDownTime <= _dblClickTime;
                            bool withinSpace = _lastDownValid
                                && Math.Abs(ms.pt.x - _lastDownPt.x) <= _dblClickRect.Width
                                && Math.Abs(ms.pt.y - _lastDownPt.y) <= _dblClickRect.Height;
                            if (withinTime && withinSpace)
                            {
                                _lastDownValid = false;
                                RaiseDoubleClick();
                            }
                            else
                            {
                                _lastDownValid = true;
                                _lastDownTime = now;
                                _lastDownPt = ms.pt;
                            }
                        }
                    }
                }
            }
            catch { }
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        // 与原始实现一致：始终通过 UI 线程的 BeginInvoke 异步触发，
        // 把窗口显隐操作推迟到消息循环的安全点，而非在低级鼠标钩子回调中同步执行。
        private void RaiseDoubleClick()
        {
            if (_owner != null) _owner.BeginInvoke(new Action(() => DoubleClickDetected?.Invoke()), null);
            else DoubleClickDetected?.Invoke();
        }

        // 命中窗口的顶层祖先属于 桌面(Progman/WorkerW) 即视为目标区域。
        // 任务栏不再通过全局鼠标钩子呼出面板，统一由托盘图标左键负责。
        // 桌面有图标时整个桌面被 SysListView32 覆盖：用 MSAA(AccessibleObjectFromPoint) 判断点是否落在图标上，
        // 落在图标/标签上不呼出（双击图标打开软件/文件夹不误触发），落在空隙才呼出。
        private bool IsDesktopOrTaskbar(POINTLL pt)
        {
            IntPtr hwnd = WindowFromPoint(pt);
            if (hwnd == IntPtr.Zero) return false;
            IntPtr root = GetAncestor(hwnd, GA_ROOT);
            string rootCls = ClassNameOf(root);
            if (rootCls == "Progman" || rootCls == "WorkerW")
            {
                // 桌面图标层：点落在图标上不呼出；无图标(壁纸层)或空隙则呼出
                if (ClassNameOf(hwnd) == "SysListView32") return !IsDesktopIconAt(pt);
                return true;
            }
            return false;
        }

        // MSAA 判断点是否落在桌面图标上：AccessibleObjectFromPoint 返回该点最深的可访问对象。
        // pvarChild 为 CHILDID_SELF(0) 表示命中对象本体（空白/壁纸），非 0 子ID 或对象即命中图标等子元素。
        private bool IsDesktopIconAt(POINTLL pt)
        {
            try
            {
                object varChild;
                IAccessible acc;
                int hr = AccessibleObjectFromPoint(new Point(pt.x, pt.y), out acc, out varChild);
                if (hr != 0 || acc == null) return false;
                if (varChild == null) return false;
                if (varChild is int) return (int)varChild != 0; // CHILDID_SELF(0)=空白，>0=命中图标
                return true;                                    // 返回子对象即命中图标
            }
            catch { return false; }
        }

        private string ClassNameOf(IntPtr hwnd)
        {
            var sb = new System.Text.StringBuilder(256);
            return GetClassName(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : string.Empty;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINTLL { public int x; public int y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT { public POINTLL pt; public int mouseData; public int flags; public int time; public IntPtr dwExtraInfo; }
        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINTLL p);
        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, int gaFlags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll")]
        private static extern uint GetDoubleClickTime();
        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);
        [DllImport("oleacc.dll")]
        private static extern int AccessibleObjectFromPoint(Point ptScreen, out IAccessible pacc, out object pvarChild);

        public void Dispose()
        {
            Uninstall();
        }
    }
}
