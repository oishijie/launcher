using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace launcher.Core
{
    // 快捷键组合串 <-> (修饰键, 虚拟键) 的解析与比对工具。
    // 组合串格式：修饰键 + '+' + 键名，例如 "Ctrl+Shift+T"、"Ctrl+`"、"A"、"1"。
    // 修饰键支持：Ctrl / Shift / Alt / Win（大小写不敏感）。
    internal static class HotkeyBinding
    {
        // 与 Win32 MOD_* 常量一致，便于直接传给 RegisterHotKey。
        public const int MOD_CTRL = 0x0002;
        public const int MOD_SHIFT = 0x0004;
        public const int MOD_ALT = 0x0001;
        public const int MOD_WIN = 0x0008;

        // 解析组合串 -> (修饰键位掩码, 虚拟键)。键名非法或缺失则返回 key=Keys.None。
        public static (int mods, Keys key) Parse(string combo)
        {
            int mods = 0;
            Keys key = Keys.None;
            if (string.IsNullOrWhiteSpace(combo)) return (mods, key);
            foreach (var raw in combo.Split('+'))
            {
                var tok = raw.Trim();
                if (tok.Length == 0) continue;
                if (tok.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || tok.Equals("Control", StringComparison.OrdinalIgnoreCase)) mods |= MOD_CTRL;
                else if (tok.Equals("Shift", StringComparison.OrdinalIgnoreCase)) mods |= MOD_SHIFT;
                else if (tok.Equals("Alt", StringComparison.OrdinalIgnoreCase)) mods |= MOD_ALT;
                else if (tok.Equals("Win", StringComparison.OrdinalIgnoreCase) || tok.Equals("Super", StringComparison.OrdinalIgnoreCase)) mods |= MOD_WIN;
                else key = ParseKey(tok);
            }
            return (mods, key);
        }

        private static Keys ParseKey(string tok)
        {
            if (tok == "`" || tok == "~") return Keys.Oemtilde;
            if (tok.Length == 1 && tok[0] >= '0' && tok[0] <= '9')
                return Keys.D0 + (tok[0] - '0'); // "1" -> Keys.D1（主键盘行）
            if (Enum.TryParse<Keys>(tok, true, out var k)) return k;
            return Keys.None;
        }

        // 反向格式化，用于日志/显示（与 Parse 可往返）。
        public static string Format(int mods, Keys key)
        {
            var parts = new List<string>();
            if ((mods & MOD_CTRL) != 0) parts.Add("Ctrl");
            if ((mods & MOD_SHIFT) != 0) parts.Add("Shift");
            if ((mods & MOD_ALT) != 0) parts.Add("Alt");
            if ((mods & MOD_WIN) != 0) parts.Add("Win");
            parts.Add(FormatKey(key));
            return string.Join("+", parts);
        }

        private static string FormatKey(Keys key)
        {
            if (key == Keys.Oemtilde) return "~";
            if (key >= Keys.D0 && key <= Keys.D9) return ((int)key - (int)Keys.D0).ToString();
            if (key >= Keys.NumPad0 && key <= Keys.NumPad9) return ((int)key - (int)Keys.NumPad0).ToString();
            return key.ToString();
        }

        // 判断一次按键事件是否匹配某组合串（用于窗内快捷键比对）。
        public static bool Matches(string combo, bool ctrl, bool shift, bool alt, Keys keyCode)
        {
            var (mods, key) = Parse(combo);
            if (key == Keys.None) return false;
            int pressed = (ctrl ? MOD_CTRL : 0) | (shift ? MOD_SHIFT : 0) | (alt ? MOD_ALT : 0);
            return pressed == mods && keyCode == key;
        }
    }
}
