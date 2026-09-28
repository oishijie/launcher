using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using launcher.Controls;

namespace launcher.Core
{
    internal static class Program
    {
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main()
        {
            // 单实例保护：已存在实例则激活其窗口并退出新进程，避免误开多个 launcher
            bool createdNew;
            using (var mutex = new Mutex(true, "LauncherSingleInstance_worldoi", out createdNew))
            {
                if (!createdNew)
                {
                    ActivateExistingInstance();
                    return;
                }

                // 全局未捕获异常 -> 写 launcher.log（避免“打不开却毫无提示”）
                Application.ThreadException += (s, e) =>
                {
                    try { Logger.Log("未处理的 UI 线程异常: " + e.Exception); } catch { }
                    try { MessageBox.Show("程序遇到错误，详情已记录到 launcher.log：\n" + e.Exception.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { }
                };
                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                {
                    try { Logger.Log("未处理的异常(将导致退出): " + (e.ExceptionObject as Exception)); } catch { }
                    try { MessageBox.Show("程序遇到严重错误，详情已记录到 launcher.log：\n" + ((e.ExceptionObject as Exception)?.Message ?? e.ExceptionObject?.ToString()), "严重错误", MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { }
                };

                // Everything64.dll 随附在 everything\ 子目录里，而 P/Invoke 默认只搜 exe 同级目录，
                // 这里把该子目录加进 DLL 搜索路径，DllImport("Everything64.dll") 才找得到。
                // 找不到也不影响启动 —— 这时全盘搜索会自动降级到 es.exe。
                try
                {
                    string dllDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "everything");
                    if (System.IO.Directory.Exists(dllDir)) SetDllDirectory(dllDir);
                }
                catch { }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new Launcher());
            }
        }

        // 找到已运行的 launcher 进程，将其主窗口恢复到前台
        private static void ActivateExistingInstance()
        {
            try
            {
                int self = Process.GetCurrentProcess().Id;
                foreach (var p in Process.GetProcessesByName("launcher"))
                {
                    if (p.Id == self) continue;
                    IntPtr hwnd = p.MainWindowHandle;
                    if (hwnd != IntPtr.Zero)
                    {
                        ShowWindow(hwnd, 9); // SW_RESTORE
                        SetForegroundWindow(hwnd);
                        break;
                    }
                }
            }
            catch { }
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetDllDirectory(string lpPathName);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}
