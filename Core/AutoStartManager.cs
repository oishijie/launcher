using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace launcher.Core
{
    // 开机自启管理：读写注册表 HKCU\...\Run 键（从 Form1 抽取，消除 UI 层对注册表的直接依赖）
    public static class AutoStartManager
    {
        private const string RunRegKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AutoStartName = "Launcher";

        public static bool IsEnabled()
        {
            try
            {
                var v = Registry.GetValue(RunRegKey, AutoStartName, null);
                return v != null && v.ToString().IndexOf(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        public static void SetEnabled(bool enable)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key == null) return;
                    if (enable) key.SetValue(AutoStartName, "\"" + Application.ExecutablePath + "\"");
                    else key.DeleteValue(AutoStartName, false);
                }
                Logger.Log("开机自启已" + (enable ? "开启" : "关闭"));
            }
            catch (Exception ex) { Logger.Log("设置开机自启失败：" + ex.Message); }
        }
    }
}
