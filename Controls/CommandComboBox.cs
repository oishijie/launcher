using System;
using System.Windows.Forms;

namespace launcher.Controls
{
    // 下拉选择框：输入框 + 右侧三角箭头（ComboBox DropDown 样式），点击展开预设列表，
    // 选中回填命令到输入框并收起。用户也可直接输入自定义命令。
    public class CommandComboBox : ComboBox
    {
        // 预设项：Label（下拉显示的友好名称）+ Command（选中后回填到输入框的命令）
        public class PresetItem
        {
            public string Label { get; set; }
            public string Command { get; set; }
            // DisplayMember 兜底：若属性绑定未生效，ToString 决定下拉显示文本
            public override string ToString() => Label ?? Command ?? "";
        }

        // 选中预设项时触发，参数为友好名称（供调用方自动填显示名称）
        public event Action<string> PresetSelected;

        // 选中预设时返回预设的 Command（::{GUID}/shell:.../cmd 等），否则返回输入框文本。
        // 不能用 Text：有 DisplayMember 时选中后 Text 是 Label（"此电脑"）而非 Command。
        public string EffectiveCommand
        {
            get
            {
                if (SelectedIndex >= 0 && SelectedIndex < Items.Count && Items[SelectedIndex] is PresetItem pi)
                    return pi.Command ?? "";
                return Text;
            }
        }

        public CommandComboBox()
        {
            DropDownStyle = ComboBoxStyle.DropDown;
            DisplayMember = nameof(PresetItem.Label);
            foreach (var p in CommonPresets) Items.Add(p);
            SelectedIndexChanged += (s, e) =>
            {
                // 选中预设项：把友好名称替换为实际命令回填到输入框
                if (SelectedIndex >= 0 && Items[SelectedIndex] is PresetItem pi)
                {
                    Text = pi.Command;
                    PresetSelected?.Invoke(pi.Label);
                }
            };
        }

        // 常见 GUID / shell: 路径 / shell 命令预设
        public static PresetItem[] CommonPresets => new[]
        {
            new PresetItem { Label = "此电脑", Command = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}" },
            new PresetItem { Label = "控制面板", Command = "::{21EC2020-3AEA-1069-A2D8-08002B30309D}" },
            new PresetItem { Label = "回收站", Command = "::{645FF040-5081-101B-9F08-00AA002F954E}" },
            new PresetItem { Label = "我的文档", Command = "::{450D8FBA-AD25-11D0-98A8-0800361B1103}" },
            new PresetItem { Label = "网络", Command = "::{208D2C34-3AEA-1069-A2D7-08002B30309D}" },
            new PresetItem { Label = "桌面", Command = "shell:Desktop" },
            new PresetItem { Label = "下载", Command = "shell:Downloads" },
            new PresetItem { Label = "文档", Command = "shell:Documents" },
            new PresetItem { Label = "图片", Command = "shell:Pictures" },
            new PresetItem { Label = "音乐", Command = "shell:Music" },
            new PresetItem { Label = "视频", Command = "shell:Videos" },
            new PresetItem { Label = "应用列表", Command = "shell:AppsFolder" },
            new PresetItem { Label = "设备和打印机", Command = "shell:PrintersFolder" },
            new PresetItem { Label = "管理工具", Command = "shell:Administrative Tools" },
            new PresetItem { Label = "命令提示符", Command = "cmd" },
            new PresetItem { Label = "PowerShell", Command = "powershell" },
            new PresetItem { Label = "任务管理器", Command = "taskmgr" },
            new PresetItem { Label = "注册表编辑器", Command = "regedit" },
            new PresetItem { Label = "系统配置", Command = "msconfig" },
            new PresetItem { Label = "远程桌面", Command = "mstsc" },
            new PresetItem { Label = "计算器", Command = "calc" },
            new PresetItem { Label = "记事本", Command = "notepad" },
        };
    }
}
