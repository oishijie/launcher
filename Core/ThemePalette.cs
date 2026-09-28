using System.Drawing;

namespace launcher.Core
{
    // 主题模式（原先嵌在 Form1 内部的 enum，抽到命名空间级别便于 ConfigStore / IconSlot 共用）
    public enum ThemeMode { Dark, Light }

    // ============================================================================
    //  调色板
    //
    //  字段名保持不变（全项目 200+ 处引用），本次只重做**取值体系**。
    //
    //  重构前的问题：色阶挤在一起。深色下窗口底 #1E1E1E(30)、搜索框 #262626(38)、
    //  菜单 #2B2B2B(43)、悬停 #323232(50) —— 四层只差 5~8 级亮度，人眼几乎读不出层次，
    //  于是整块界面"糊成一片"；卡片 #232323 压在同为深灰的页底上，等于隐形。
    //
    //  现在的做法（借鉴 Fluent 2 的 Layer 概念）：
    //    深色 —— 四层，每层拉开 10 级以上，让"底 → 卡片 → 控件 → 悬停"一眼可辨：
    //              Base #1C1C1C(28) → Card #262626(38) → Control #2E2E2E(46) → Hover #383838(56)
    //    浅色 —— 反过来：**内容区底用浅灰 #FAFAFA，卡片用纯白 #FFFFFF**，
    //              让卡片"浮起来"（Linear / Notion / VS Code 的通用做法）；
    //              深色下靠明度抬升分层，浅色下靠"白块 + 淡边框"分层。
    //
    //  另：边框统一降低对比。原来 #3A3A3A 比卡片亮 23 级，边框比内容还抢眼 —— 这是
    //  "廉价感"的常见来源。现在边框只比卡片亮 13 级，起"轻轻勾一下轮廓"的作用。
    // ============================================================================
    public class ThemePalette
    {
        public Color FormBack, SlotBack, Sep, LabelFore, TextBack, TextFore;
        public Color DotActive, DotInactive, NameStroke, NameFill;
        public Color MenuBack, MenuFore;
        public Color AccentPencil, AccentStick;
        public Color Highlight, SearchCue;

        // ===== 主界面 chrome：标题栏图标按钮 / 搜索框 / 槽位悬停 / 菜单 =====
        public Color IconFore, IconForeActive, IconHover, IconActiveBack;
        public Color SearchBack, SearchBorder, SearchBorderFocus;
        public Color SlotHover;
        public Color MenuHover, MenuBorder;

        // ===== 设置面板 / 便签：四层色阶 + 文字三级 + 强调色 =====
        public Color PanelBack, SideBack, CardBack, BorderCol;
        public Color TextPrimary, TextMuted, TextHint;
        public Color TabHover, TabActive;
        public Color Accent, AccentHover, TrackBack, ToggleOff, Danger;

        /// <summary>底部导航条底衬：比窗口底略暗，让分页胶囊在图标网格下方有独立区域感。</summary>
        public Color BottomBarBack;

        /// <summary>卡片/控件的内部面（比 CardBack 再亮一档），用于输入框内胆等需要"凹进去"或"凸出来"的元素。</summary>
        public Color ControlBack;

        // ===== 叠加色组（借鉴 WinKit）=====
        // WinKit 的悬停/选中一律不用实色，而是叠一层**低透明度黑**：
        //   悬停 #10000000(6%) / 选中 #18000000(9%)
        // 好处有三：① 底色变了自动跟随，不必为"悬停压在卡片上"再配一个色；
        // ② 悬停 + 选中叠加时自然累积成更深的一层，不需要第三、第四个色值；
        // ③ 深浅主题共用同一套语义，只是把黑换成白。
        // GDI+ 的 SolidBrush 原生支持 alpha，FillPath 直接完成混合，不必手工算颜色。
        public Color OverlaySubtle, OverlayHover, OverlaySelected, OverlayPressed;

        /// <summary>
        /// 窗口 1px 外描边。无边框窗口若只靠"一块纯色"和桌面相邻，边缘会显得糊、发飘；
        /// 一圈极淡的描边就能把它"贴"在桌面上（WinKit 每个窗口都有 #22000000 的边）。
        /// </summary>
        public Color WindowBorder;

        public static ThemePalette For(ThemeMode mode)
        {
            var p = new ThemePalette();
            if (mode == ThemeMode.Dark)
            {
                // —— ① 主面板（图标网格）——
                // 窗口底与设置面板同色（#1C1C1C），两个界面共用一套语言，不再是"一个纯黑一个深灰"。
                p.FormBack = Color.FromArgb(28, 28, 28);        // Base
                p.SlotBack = Color.FromArgb(28, 28, 28);
                p.Sep = Color.FromArgb(51, 51, 51);
                p.LabelFore = Color.FromArgb(176, 176, 176);
                p.TextBack = Color.FromArgb(28, 28, 28);
                p.TextFore = Color.FromArgb(237, 237, 237);
                p.DotActive = Color.FromArgb(74, 158, 255);
                p.DotInactive = Color.FromArgb(90, 90, 90);
                p.NameStroke = Color.FromArgb(28, 28, 28);      // 图标名描边：与底同色，只起"防边缘发虚"作用
                p.NameFill = Color.White;
                p.MenuBack = Color.FromArgb(42, 42, 42);
                p.MenuFore = Color.White;
                p.AccentPencil = Color.FromArgb(180, 180, 180);
                p.AccentStick = Color.FromArgb(74, 158, 255);
                p.Highlight = Color.FromArgb(74, 158, 255);
                p.SearchCue = Color.FromArgb(138, 138, 138);

                // —— ② chrome（标题栏 / 搜索框 / 悬停 / 菜单）——
                p.IconFore = Color.FromArgb(168, 168, 168);
                p.IconForeActive = Color.FromArgb(255, 255, 255);
                p.IconHover = Color.FromArgb(48, 48, 48);
                p.IconActiveBack = Color.FromArgb(31, 58, 92);  // 强调蓝的低饱和底：比纯灰底更能标识"已开启"
                p.SearchBack = Color.FromArgb(38, 38, 38);      // Control：输入框比窗口底亮一档
                p.SearchBorder = Color.FromArgb(51, 51, 51);
                p.SearchBorderFocus = Color.FromArgb(74, 158, 255);
                p.SlotHover = Color.FromArgb(46, 46, 46);       // 悬停：比槽位底亮 18 级，清晰但不刺眼
                p.MenuHover = Color.FromArgb(58, 58, 58);
                p.MenuBorder = Color.FromArgb(61, 61, 61);
                p.BottomBarBack = Color.FromArgb(24, 24, 24);   // 底部导航底衬略暗一档

                // —— ③ 设置面板 / 便签（阅读型界面，讲究层次而非极致对比）——
                p.PanelBack = Color.FromArgb(28, 28, 28);       // Base：内容区底
                p.SideBack = Color.FromArgb(22, 22, 22);        // Layer：侧栏再沉一档，形成"深→浅"的横向分区
                p.CardBack = Color.FromArgb(38, 38, 38);        // Card：卡片明显亮于底（差 10 级，可辨）
                p.ControlBack = Color.FromArgb(31, 31, 31);     // Control：比卡片**暗**一档 —— 输入框/分段槽是"凹"进去的
                p.BorderCol = Color.FromArgb(51, 51, 51);       // 边框：只比卡片亮 13 级，勾轮廓而不抢戏
                p.TextPrimary = Color.FromArgb(237, 237, 237);
                p.TextMuted = Color.FromArgb(180, 180, 180);
                p.TextHint = Color.FromArgb(122, 122, 122);
                p.TabHover = Color.FromArgb(36, 36, 36);
                p.TabActive = Color.FromArgb(46, 46, 46);       // 选中态比侧栏底亮 24 级 —— 一眼可辨
                p.Accent = Color.FromArgb(74, 158, 255);
                p.AccentHover = Color.FromArgb(107, 176, 255);
                p.TrackBack = Color.FromArgb(58, 58, 58);
                p.ToggleOff = Color.FromArgb(69, 69, 69);
                p.Danger = Color.FromArgb(224, 82, 82);

                // —— ④ 叠加色组 ——
                // 深色底上用**白**叠：叠得越多越亮，语义与浅色完全对称。
                p.OverlaySubtle = Color.FromArgb(10, 255, 255, 255);     // 4%：列表行 / 菜单项
                p.OverlayHover = Color.FromArgb(16, 255, 255, 255);      // 6%：悬停
                p.OverlaySelected = Color.FromArgb(26, 255, 255, 255);   // 10%：选中
                p.OverlayPressed = Color.FromArgb(34, 255, 255, 255);    // 13%：按下
                p.WindowBorder = Color.FromArgb(30, 255, 255, 255);      // 深色窗口在深色桌面上要"亮"一点才看得见
            }
            else // Light
            {
                // —— ① 主面板 ——
                p.FormBack = Color.White;
                p.SlotBack = Color.White;
                p.Sep = Color.FromArgb(229, 229, 229);
                p.LabelFore = Color.FromArgb(85, 85, 85);
                p.TextBack = Color.White;
                p.TextFore = Color.FromArgb(26, 26, 26);
                p.DotActive = Color.FromArgb(47, 111, 208);
                p.DotInactive = Color.FromArgb(200, 200, 200);
                p.NameStroke = Color.White;
                p.NameFill = Color.FromArgb(26, 26, 26);
                p.MenuBack = Color.White;
                p.MenuFore = Color.FromArgb(26, 26, 26);
                p.AccentPencil = Color.FromArgb(90, 90, 90);
                p.AccentStick = Color.FromArgb(47, 111, 208);
                p.Highlight = Color.FromArgb(47, 111, 208);
                p.SearchCue = Color.FromArgb(154, 154, 154);

                // —— ② chrome ——
                p.IconFore = Color.FromArgb(90, 90, 90);
                p.IconForeActive = Color.FromArgb(26, 26, 26);
                p.IconHover = Color.FromArgb(236, 236, 236);
                p.IconActiveBack = Color.FromArgb(220, 232, 250);
                p.SearchBack = Color.FromArgb(244, 244, 244);
                p.SearchBorder = Color.FromArgb(226, 226, 226);
                p.SearchBorderFocus = Color.FromArgb(47, 111, 208);
                p.SlotHover = Color.FromArgb(242, 242, 242);
                p.MenuHover = Color.FromArgb(239, 239, 239);
                p.MenuBorder = Color.FromArgb(224, 224, 224);
                p.BottomBarBack = Color.FromArgb(247, 247, 247);

                // —— ③ 设置面板 / 便签 ——
                // 浅色下反过来：底用浅灰、卡片用纯白 —— 卡片"浮起来"。
                p.PanelBack = Color.FromArgb(250, 250, 250);
                p.SideBack = Color.FromArgb(242, 242, 242);
                p.CardBack = Color.White;
                p.ControlBack = Color.FromArgb(240, 240, 240);  // 比卡片（纯白）暗一档：凹陷感，与深色同语义
                p.BorderCol = Color.FromArgb(230, 230, 230);
                p.TextPrimary = Color.FromArgb(26, 26, 26);
                p.TextMuted = Color.FromArgb(90, 90, 90);
                p.TextHint = Color.FromArgb(140, 140, 140);
                p.TabHover = Color.FromArgb(234, 234, 234);
                p.TabActive = Color.White;                      // 选中项白底，浮在灰侧栏上
                p.Accent = Color.FromArgb(47, 111, 208);
                p.AccentHover = Color.FromArgb(37, 89, 168);
                p.TrackBack = Color.FromArgb(224, 224, 224);
                p.ToggleOff = Color.FromArgb(207, 207, 207);
                p.Danger = Color.FromArgb(209, 67, 67);

                // —— ④ 叠加色组（浅色底上换用黑叠）——
                p.OverlaySubtle = Color.FromArgb(10, 0, 0, 0);
                p.OverlayHover = Color.FromArgb(16, 0, 0, 0);
                p.OverlaySelected = Color.FromArgb(26, 0, 0, 0);
                p.OverlayPressed = Color.FromArgb(34, 0, 0, 0);
                p.WindowBorder = Color.FromArgb(34, 0, 0, 0);
            }
            return p;
        }
    }
}
