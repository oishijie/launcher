namespace launcher.Core
{
    /// <summary>
    /// 视觉度量的单一事实来源：间距栅格、圆角分级、控件与窗口尺寸。
    ///
    /// 为什么要有这个类：重构前的界面"节奏乱"，根因不是审美，而是**同一个界面里并存
    /// 七种互不相干的间距**——主面板同时有 15 / 12 / 28 / 34 / 8 / 14 / 2，设置面板的行距
    /// 有 44 / 48 / 42 / 44 四种。人眼读不出规律，观感就是"拼凑"。
    ///
    /// 两条约束（沿用业界主流做法）：
    ///   ① 间距一律是 4 的倍数（8pt 栅格）——Material / Fluent / Apple HIG 通用基准。
    ///   ② 圆角分三级，按元素体量选，而不是到处写 6 或 8——
    ///      窗口与卡片 8（大块面）、控件 6（按钮 / 输入框）、小元件 4（滑块轨 / 徽章）。
    ///
    /// 注意：所有布局数字都应取自这里。若某处确实需要例外，写在调用点并注释原因，
    /// 不要往这里加一次性常量——那正是它要治的病。
    /// </summary>
    internal static class UiMetrics
    {
        // ===== 间距栅格（4pt 基准）=====
        public const int XS = 4;
        public const int S = 8;
        public const int M = 12;
        public const int L = 16;
        public const int XL = 20;
        public const int XXL = 24;
        public const int XXXL = 32;

        // ===== 圆角分级 =====
        // 窗口取 10 而非 8：窗口是整块大面，圆角半径要按体量同步放大才不显"方"。
        // （WinKit 的窗口也是 10，卡片 8，控件 4~6 —— 同一套分级）
        public const int RadiusWindow = 10;   // 窗口外沿
        public const int RadiusCard = 8;      // 卡片 / 分组容器 / 搜索框
        public const int RadiusControl = 6;   // 按钮 / 输入框 / 分段控件
        public const int RadiusSmall = 4;     // 滑块轨 / 小徽章 / 分页胶囊

        // ===== 窗口标题栏（主面板 / 设置 / 便签 / 各对话框 共用同一套）=====
        // 统一前，这四个数值不一的"标题栏高"散在五个窗口里：
        //   主面板 36、便签 36、设置面板 50、两个对话框 46 —— 同一产品的窗口各自定高。
        //   连带标题字号（9.5pt / 11pt）、关闭按钮（两种实现，悬停反馈还不一样）、
        //   按钮间距（2 / 8 / 12）也各写各的，同一个动作在不同窗口手感不同。
        // 现在这里只留一个数；要改标题栏高度，改这一处即全项目生效。
        //
        // 标题文字的**左内距**不在这一组里：它跟随各窗口内容区的左内距（同一窗口内
        // 标题与内容左对齐才顺眼），各窗口取值 16 / 20 / 28 都是有意的。
        public const int TitleBarH = 36;      // 标题栏高（含搜索框与图标按钮）

        // 窗口外描边
        // 无边框窗口只靠"一块纯色"和桌面相邻会发飘；1px 极淡描边把它贴回桌面。
        public const int WindowBorderW = 1;

        // ===== 侧栏选中标识（NavigationView 式）=====
        // 关键在"短条"而非"通高条"：与整行等高的竖条是系统 TabControl 的语言，
        // 一段居中的短条才是现代侧栏的做法，且不与"整块填充"的选中底重复表达同一件事。
        public const int SideBarW = 3;        // 选中条宽
        public const int SideBarH = 16;       // 选中条高（行内垂直居中）
        public const int SideBarRadius = 2;   // 选中条圆角（半圆头，不是直角）

        // ===== 开关 =====
        public const int SwitchW = 40;        // 轨道宽
        public const int SwitchH = 22;        // 轨道高（圆角 = 高/2，胶囊）
        public const int SwitchKnob = 16;     // 滑块直径
        public const int SwitchInset = 3;     // 滑块与轨道边缘的间隙

        // ===== 控件通用高度 =====
        public const int ControlH = 30;       // 输入框 / 数字框 / 分段控件
        public const int ButtonH = 32;        // 普通按钮
        public const int ButtonHBig = 34;     // 面板底部主按钮

        // ===== 主面板 =====
        public const int BottomBarH = 40;     // 底部分页导航预留高度
        public const int SlotGap = 16;        // 图标槽间距
        public const int GridPadH = 16;       // 网格左右内边距
        public const int GridPadTop = 10;     // 网格与标题栏之间的呼吸
        public const int ChromeBtnW = 34;
        public const int ChromeBtnH = 30;
        public const int ChromeBtnGap = 2;    // 标题栏按钮之间（三个窗口一致）
        public const int SearchH = 32;        // 展开态搜索框高度
        // 展开态搜索框的最小可用宽度。低于这个宽度就不展开、退回放大镜图标 ——
        // 与其塞一个只能显示两三个字的输入框，不如老实显示图标。
        public const int SearchMinW = 120;

        // ===== 设置面板 =====
        // 标题栏高不再单独定义：与主面板 / 便签共用 TitleBarH，见上方「窗口标题栏」。
        // （原先这里是 SettingsTitleH = 50，导致设置面板的标题栏比另外两个窗口高一截。）
        public const int SettingsW = 660;
        public const int SettingsH = 500;
        public const int SettingsSideW = 148;  // 左侧标签栏
        public const int SideItemH = 40;       // 侧栏单项高
        public const int SettingsPadL = 28;    // 内容左内边距
        public const int SettingsPadTop = 20;  // 内容与标题栏间距
        public const int RowH = 48;            // 统一行距（所有页面一致）
        public const int CardRadiusPad = 12;   // 卡片内四边留白
        public const int FooterH = 56;         // 底部按钮条高
        public const int CardTitleH = 30;      // 卡片内分组标题占位高

        // ===== 便签窗口 =====
        public const int NotesW = 600;
        public const int NotesH = 440;
        public const int NotesMinW = 380;
        public const int NotesMinH = 280;
        public const int NotesSideW = 58;      // 左侧便签列表栏
    }
}
