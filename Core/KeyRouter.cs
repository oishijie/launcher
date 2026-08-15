using System.Windows.Forms;

namespace launcher.Core
{
    // 键盘路由结果：语义动作 + 参数（槽位索引 / 字母 / 跳转页）。
    // 与 UI 解耦，便于单测：给定按键状态与热键配置，输出"该做什么"。
    public struct KeyRoute
    {
        public KeyAction Action;
        public int SlotIndex;     // OpenSlotByIndex / SearchOpenNth
        public char Letter;       // OpenSlotByLetter
        public int JumpIndex;     // JumpPage
        public bool Handled;      // 是否应吞掉该键（suppress + handled）
    }

    public enum KeyAction
    {
        None,
        UndoDelete,
        PrevPage,
        NextPage,
        OpenSlotByIndex,
        OpenSlotByLetter,
        JumpPage,
        SearchOpenSelected,
        SearchMoveUp,
        SearchMoveDown,
        SearchClear,
        SearchOpenNth,
    }

    // 键盘分发：把 Form1.Launcher_KeyDown 的判定逻辑抽出来，纯函数、可单测。
    // 参数与事件一一对应（ctrl/shift/alt/keyCode），外加"是否处于搜索框聚焦态"。
    public static class KeyRouter
    {
        public static KeyRoute Resolve(HotkeyConfig hk, bool searchMode, bool ctrl, bool shift, bool alt, Keys key)
        {
            if (searchMode)
            {
                if (key == Keys.Enter) return Handled(KeyAction.SearchOpenSelected);
                if (key == Keys.Up) return Handled(KeyAction.SearchMoveUp);
                if (key == Keys.Down) return Handled(KeyAction.SearchMoveDown);
                if (key == Keys.Escape) return Handled(KeyAction.SearchClear);
                if (!ctrl && !alt && !shift && key >= Keys.D1 && key <= Keys.D9)
                    return new KeyRoute { Action = KeyAction.SearchOpenNth, SlotIndex = (int)(key - Keys.D1), Handled = true };
                return new KeyRoute { Action = KeyAction.None }; // 其余按键交给搜索框输入
            }

            if (HotkeyBinding.Matches(hk.Undo, ctrl, shift, alt, key))
                return Handled(KeyAction.UndoDelete);
            if (HotkeyBinding.Matches(hk.PrevPage, ctrl, shift, alt, key))
                return Handled(KeyAction.PrevPage);
            if (HotkeyBinding.Matches(hk.NextPage, ctrl, shift, alt, key))
                return Handled(KeyAction.NextPage);

            // 数字键 1-9：直接打开当前页第 N 个槽位（优先于 Jump）
            if (!ctrl && !alt && !shift && key >= Keys.D1 && key <= Keys.D9)
                return new KeyRoute { Action = KeyAction.OpenSlotByIndex, SlotIndex = (int)(key - Keys.D1), Handled = true };
            // 字母键：打开当前页首个文件名以该字母开头的槽位
            if (!ctrl && !alt && !shift && key >= Keys.A && key <= Keys.Z)
                return new KeyRoute { Action = KeyAction.OpenSlotByLetter, Letter = (char)('A' + (key - Keys.A)), Handled = true };

            for (int i = 0; i < (hk.Jump?.Count ?? 0); i++)
            {
                if (HotkeyBinding.Matches(hk.Jump[i], ctrl, shift, alt, key))
                    return new KeyRoute { Action = KeyAction.JumpPage, JumpIndex = i, Handled = true };
            }
            return new KeyRoute { Action = KeyAction.None };
        }

        private static KeyRoute Handled(KeyAction action) =>
            new KeyRoute { Action = action, Handled = true };
    }
}
