using System;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace launcher.Core
{
    // 全局日志：写程序目录下的 launcher.log（追加，带时间戳）。自身异常静默，避免二次崩溃。
    // 从 Form1.Launcher.Log 下沉到 Core，消除逻辑层(Controls)对 UI 层(Core)的反向依赖。
    public static class Logger
    {
        public static void Log(string message)
        {
            try
            {
                string dir = Application.StartupPath;
                if (string.IsNullOrEmpty(dir)) dir = AppDomain.CurrentDomain.BaseDirectory;
                string logPath = Path.Combine(dir, "launcher.log");
                string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + message;
                File.AppendAllText(logPath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }
    }
}
