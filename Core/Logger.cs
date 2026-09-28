using System;
using System.IO;
using System.Text;

namespace launcher.Core
{
    // 全局日志：写程序目录下的 launcher.log（追加，带时间戳）。自身异常静默，避免二次崩溃。
    // 从 Form1.Launcher.Log 下沉到 Core，消除逻辑层(Controls)对 UI 层(Core)的反向依赖。
    public static class Logger
    {
        // 用 AppDomain.CurrentDomain.BaseDirectory（与 ConfigStore 一致）而非 Application.StartupPath。
        // 此前用 Application.StartupPath 在部分环境下返回异常值，导致 launcher.log 写不成功、
        // 诊断能力丧失（"打不开却无提示"）。BaseDirectory 在进程启动时即确定，行为稳定。
        public static void Log(string message)
        {
            try
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                string logPath = Path.Combine(dir, "launcher.log");
                string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + message;
                File.AppendAllText(logPath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                // 兜底：把日志写入失败的原因单独记下，避免完全静默、无从排查
                try
                {
                    File.AppendAllText(
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logger-err.txt"),
                        "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + ex.Message + Environment.NewLine,
                        Encoding.UTF8);
                }
                catch { }
            }
        }
    }
}
