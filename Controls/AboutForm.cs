using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Forms;

namespace launcher.Controls
{
    /// <summary>
    /// 简易“关于”对话框：显示版本号与本项目（worldoi/launcher）链接。
    /// </summary>
    public class AboutForm : Form
    {
        public AboutForm()
        {
            var asm = Assembly.GetExecutingAssembly();
            var version = (asm.GetName().Version ?? new Version(1, 0, 0, 0)).ToString();
            var buildDate = System.IO.File.GetLastWriteTimeUtc(asm.Location).ToString("yyyy-MM-dd");

            Text = "关于 Launcher";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new System.Drawing.Size(380, 220);

            var label = new Label
            {
                Left = 16,
                Top = 16,
                Width = 348,
                Height = 130,
                Font = new System.Drawing.Font("微软雅黑", 9.75F),
                Text =
                    "Launcher — Windows 快速启动器\n\n" +
                    "版本：" + version + "（构建于 " + buildDate + "）\n" +
                    "基于 .NET Framework 4.7.2 / WinForms\n\n" +
                    "项目主页（worldoi/launcher）："
            };
            Controls.Add(label);

            var link = new LinkLabel
            {
                Left = 16,
                Top = 150,
                Width = 348,
                Height = 20,
                Text = "https://github.com/worldoi/launcher"
            };
            link.LinkClicked += (s, e) =>
                Process.Start(new ProcessStartInfo(link.Text) { UseShellExecute = true });
            Controls.Add(link);

            var credit = new Label
            {
                Left = 16,
                Top = 178,
                Width = 348,
                Height = 20,
                Font = new System.Drawing.Font("微软雅黑", 8.25F),
                ForeColor = System.Drawing.SystemColors.GrayText,
                Text = "基于上游 cornradio/launcher 修改"
            };
            Controls.Add(credit);
        }
    }
}
