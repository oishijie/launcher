using System.Drawing;

namespace launcher.Core
{
    // 主题模式（原先嵌在 Form1 内部的 enum，抽到命名空间级别便于 ConfigStore / IconSlot 共用）
    public enum ThemeMode { Dark, Light }

    // 调色板：随主题切换，集中管理所有界面颜色（替代原先散落在 Form1 的十几个 _cXXX 字段）
    public class ThemePalette
    {
        public Color FormBack, SlotBack, Sep, LabelFore, TextBack, TextFore;
        public Color DotActive, DotInactive, NameStroke, NameFill;
        public Color MenuBack, MenuFore;
        public Color AccentPencil, AccentStick;
        public Color Highlight, SearchCue;

        public static ThemePalette For(ThemeMode mode)
        {
            var p = new ThemePalette();
            if (mode == ThemeMode.Dark)
            {
                p.FormBack = Color.Black;
                p.SlotBack = Color.Black;
                p.Sep = Color.LightGray;
                p.LabelFore = SystemColors.ActiveBorder;
                p.TextBack = Color.Black;
                p.TextFore = SystemColors.MenuBar;
                p.DotActive = Color.White;
                p.DotInactive = Color.Gray;
                p.NameStroke = Color.Black;
                p.NameFill = Color.White;
                p.MenuBack = Color.FromArgb(43, 43, 43);
                p.MenuFore = Color.White;
                p.AccentPencil = Color.Gray;
                p.AccentStick = Color.Yellow;
                p.Highlight = Color.DodgerBlue;       // 搜索选中描边
                p.SearchCue = Color.Gray;             // 搜索框占位符颜色
            }
            else // Light
            {
                p.FormBack = Color.White;
                p.SlotBack = Color.White;
                p.Sep = Color.Gray;
                p.LabelFore = Color.DimGray;
                p.TextBack = Color.White;
                p.TextFore = Color.Black;
                p.DotActive = Color.DodgerBlue;
                p.DotInactive = Color.LightGray;
                p.NameStroke = Color.White;
                p.NameFill = Color.Black;
                p.MenuBack = Color.White;
                p.MenuFore = Color.Black;
                p.AccentPencil = Color.DimGray;
                p.AccentStick = Color.Goldenrod;
                p.Highlight = Color.DodgerBlue;
                p.SearchCue = Color.Gray;
            }
            return p;
        }
    }
}
