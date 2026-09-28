using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using launcher.Core;
using launcher.Models;

namespace launcher.Controls
{
    public partial class Launcher : Form
    {
        // ===== 配置与主题（P2：统一由 ConfigStore / ThemePalette 管理）=====
        private ConfigStore _store = new ConfigStore();
        private ThemePalette _palette;
        private ThemeMode _theme = ThemeMode.Dark;
        private string _currentPageName = "第1页";

        // ===== 槽位 / 分页（P2：IconSlot 用户控件 + PageIndicator）=====
        private List<IconSlot> _slots = new List<IconSlot>();
        private PageIndicator _pageIndicator;
        private IconSlot _dragSourceSlot = null;
        private IconSlot _currentAnimatingSlot = null;
        private bool _isSmall = false;
        private Size _minGridSize = new Size(0, 0); // 图标网格恰好容纳的窗口尺寸（自定义缩放下限）
        // 窄窗口「宽度换高度」：缩窄 → 列数变少 → 行数变多，高度不够就会出现「图标/分页条掉出可视区」。
        // 以下字段用于在**宽度变化**时把窗口撑到刚好容纳全部行，并在拉宽后自动回缩。
        private bool _heightFromUser = false;   // 用户是否手动拖过高度（拖过就不再自动回缩）
        private Size _resizeStartSize = Size.Empty; // 拖拽开始前的整块尺寸，用于区分「缩放」与「纯移动」
        private bool _userResizing = false;     // 用户正在拖拽缩放：期间不改窗口尺寸，避免与拖拽打架
        private bool _inLayout = false;         // 重排重入保护（内部改 ClientSize 会再触发 OnResize）

        // ===== 搜索 / 键盘打开 =====
        private SearchController _search;
        private const string SearchCue = "搜图标或文件…";
        private bool _suppressPageEviction = false; // 搜索合并视图正在跨页取图标，期间暂停 LRU 驱逐
        // 搜索框改「图标展开式」：收起态只剩标题栏一个放大镜图标，点开才占宽度。
        // 原来的常驻搜索框在窄窗口里吃掉整条标题栏，而多数时候用户并不搜索。
        private bool _searchBarOpen = false;

        // ===== 标题栏图标按钮的悬停提示 =====
        // 主面板标题栏全是纯图标（放大镜 / 文件夹 / 月亮 / 图钉 / 便签 / ×），没有文字线索，
        // 悬停提示是唯一能说明"这个图标是干什么的"的地方。便签窗口的便签列表一直有提示，
        // 主面板此前一个都没有 —— 关掉搜索框改成图标后，"那个放大镜点开会怎样"完全没处可问。
        private ToolTip _chromeTips;

        // ===== 页面懒加载缓存（只渲染当前页 + 切换/搜索按需加载，LRU 上限防内存膨胀）=====
        private readonly HashSet<string> _pageCacheLoaded = new HashSet<string>(); // 已渲染过缓存的页名
        private readonly List<string> _pageTouchOrder = new List<string>();        // 页名最近使用顺序（末尾=最新）
        private const int PageCacheLimit = 4; // 最多同时保留渲染缓存页数；超出时释放最久未用页（搜索期间不驱逐）

        // ===== 启动 / 切页异步渲染（后台线程渲染图标，UI 线程零阻塞）=====
        // 图标提取（.lnk 走 WScript.Shell COM 解析目标 + ExtractAssociatedIcon/ExtractIconEx P/Invoke）
        // 与 2x 超采样绘制都较慢，单张约 20–60ms。若在 UI 线程渲染，鼠标移动等消息会排队，
        // 表现就是「刚启动后鼠标一移到面板上就卡」。改为独立 STA 后台线程逐张渲染、再 BeginInvoke
        // 回 UI 线程赋值，UI 线程全程不被占用。
        private volatile int _renderGeneration;   // 每 +1 让在跑的旧渲染线程自行退出（切页 / 重载 / 重新渲染时）

        // ===== 撤销删除（引用 IconSlot，不再持有裸 PictureBox）=====
        private class DeletedSlot
        {
            public IconSlot Slot;
            public SlotData Data;
        }
        private readonly Stack<DeletedSlot> _undoStack = new Stack<DeletedSlot>();
        private const int MaxUndoDepth = 50;
        private const int DragThreshold = 5;

        // ===== 便签（已拆为独立窗口，见 Controls/NotesWindow.cs）=====
        // 原先便签内嵌在主面板里（Designer 的 panel2），但启动器"用完即走"（失焦自动隐藏）
        // 与便签"长时间驻留"是两种用法，内嵌会导致主面板一失焦便签就跟着消失。
        // 拆出后便签有自己的位置/尺寸/置顶状态，此处只保留一个懒创建的窗口引用。
        private NotesWindow _notesWindow;

        // ===== 动画定时器 =====
        private Timer _animationTimer;

        private ConfigWatcher _configWatcher;

        private bool _realExit = false; // 仅托盘菜单“退出”时置 true，点 × / Alt+F4 仅最小化到托盘
        private bool _suppressAutoHide = false; // 设置面板打开期间挂起「失焦自动隐藏」，否则预览不透明度时主面板会被藏掉
        private EdgeAutoHide _edgeHide;         // 贴边自动隐藏（借鉴亦安）：拖到屏幕边缘停稳后缩进去
        private volatile int _everythingGen;    // 全盘搜索的代数号：每发起一次 +1，旧结果回来时自行作废
        private const int MaxExternalResults = 40;  // 单次全盘搜索最多取多少条（与本地结果共用槽位网格）

        // 全局双击桌面/任务栏空白呼出面板（逻辑已抽到 DesktopMouseHook）
        private DesktopMouseHook _desktopMouseHook;

        private void SetupDesktopMouseHook()
        {
            _desktopMouseHook = new DesktopMouseHook(this,
                () => _store != null && _store.Layout != null && _store.Layout.DblClickShowEffective);
            _desktopMouseHook.DoubleClickDetected += ShowMainPanel;
        }


        // 只显示不切换（双击桌面/任务栏呼出，而不是再次双击隐藏）
        private void ShowMainPanel()
        {
            if (this.InvokeRequired) { this.Invoke(new Action(ShowMainPanel)); return; }
            if (this.Visible)
            {
                this.Activate();
                return;
            }
            ToggleMainWindow();
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == NativeMethods.WM_HOTKEY)
            {
                int id = message.WParam.ToInt32();
                if (id == NativeMethods.HOTKEY_ID_MAIN) ToggleMainWindow();
                else if (id == NativeMethods.HOTKEY_ID_NOTES) ToggleNotesWindow();
                else if (id == NativeMethods.HOTKEY_ID_THEME) ToggleTheme();
                return;
            }

            base.WndProc(ref message);

            if (message.Msg == NativeMethods.WM_NCHITTEST && (int)message.Result == NativeMethods.HTCLIENT)
            {
                // 边缘 / 四角 → 系统缩放命中区；其余客户区一律当标题栏。
                // 主面板没有"标题栏"这条边界：图标网格之间也是空白，只要能抓到就该能拖走窗口。
                // （鼠标落在图标或按钮上时 WM_NCHITTEST 由那些子控件应答，走不到这里。）
                var p = WindowChrome.HitPoint(this, message.LParam);
                int hit = WindowChrome.ResizeHit(p, ClientSize);
                message.Result = (IntPtr)(hit != 0 ? hit : NativeMethods.HTCAPTION);
            }
        }

        private void RegisterGlobalHotkeys()
        {
            try
            {
                UnregisterGlobalHotkeys();
                RegisterOne(NativeMethods.HOTKEY_ID_MAIN, _store.Hotkeys.Main, "主面板");
                RegisterOne(NativeMethods.HOTKEY_ID_NOTES, _store.Hotkeys.Notes, "便签面板");
                RegisterOne(NativeMethods.HOTKEY_ID_THEME, _store.Hotkeys.Theme, "切换深浅色");
            }
            catch (Exception ex)
            {
                Log("注册全局快捷键异常：" + ex.Message);
            }
        }

        private void RegisterOne(int id, string combo, string label)
        {
            var (mods, key) = HotkeyBinding.Parse(combo ?? string.Empty);
            if (key == Keys.None) { Log($"全局快捷键配置无效，已跳过：{label}={combo ?? "(空)"}"); return; }
            if (mods == 0) { Log($"全局快捷键需至少一个修饰键(Ctrl/Shift/Alt/Win)，已跳过：{label}={combo}"); return; }
            bool ok = NativeMethods.RegisterHotKey(this.Handle, id, mods | NativeMethods.MOD_NOREPEAT, (int)key);
            if (!ok) Log($"警告：全局快捷键注册失败(可能被占用)：{label}={combo}");
            else Log($"全局快捷键已注册：{label}={combo}");
        }

        private void UnregisterGlobalHotkeys()
        {
            NativeMethods.UnregisterHotKey(this.Handle, NativeMethods.HOTKEY_ID_MAIN);
            NativeMethods.UnregisterHotKey(this.Handle, NativeMethods.HOTKEY_ID_NOTES);
            NativeMethods.UnregisterHotKey(this.Handle, NativeMethods.HOTKEY_ID_THEME);
        }

        private void ToggleMainWindow()
        {
            if (this.InvokeRequired) { this.Invoke(new Action(ToggleMainWindow)); return; }
            if (this.Visible)
            {
                this.Hide();
            }
            else
            {
                // 呼出时如果还缩在屏幕边里，先整窗弹回工作区 —— 否则用户按了热键，
                // 窗口"出来了"却还只露着一条边，看着像没反应。
                if (_edgeHide != null) _edgeHide.Undock();
                this.Show();
                if (this.WindowState == FormWindowState.Minimized)
                    this.WindowState = FormWindowState.Normal;
                this.Activate();
                // 呼出时清除残留搜索并收起搜索框：再次呼出应回到原始页面，
                // 且搜索框默认是图标态（宽度还给图标网格），不再自动聚焦输入框。
                if (searchBox != null && !string.IsNullOrWhiteSpace(searchBox.Text) && searchBox.Text != SearchCue)
                {
                    searchBox.Text = SearchCue;
                    searchBox.ForeColor = _palette.SearchCue;
                    _search.ShowAll();
                }
                if (_searchBarOpen) CollapseSearchBar();
            }
        }

        // 便签独立窗口：热键与标题栏便签按钮共用此入口（已打开则收起，否则显示）
        private void ToggleNotesWindow()
        {
            if (this.InvokeRequired) { this.Invoke(new Action(ToggleNotesWindow)); return; }
            if (_notesWindow != null && !_notesWindow.IsDisposed && _notesWindow.Visible)
            {
                _notesWindow.Hide();
                return;
            }
            ShowNotesWindow();
        }

        private void ShowNotesWindow()
        {
            if (_notesWindow == null || _notesWindow.IsDisposed)
            {
                _notesWindow = new NotesWindow(_palette);
                _notesWindow.VisibilityChanged += (s, e) => SyncNotesButton();
                _notesWindow.FormClosed += (s, e) => SyncNotesButton();
            }
            else
            {
                _notesWindow.ApplyPalette(_palette);   // 主面板换过主题时同步过去
            }
            _notesWindow.Show();
            _notesWindow.Activate();
            _notesWindow.FocusEditor();
            SyncNotesButton();
        }

        private void SyncNotesButton()
        {
            if (btnNotes != null)
                btnNotes.Active = _notesWindow != null && !_notesWindow.IsDisposed && _notesWindow.Visible;
        }

        // ===== 主题 =====
        private void SetPalette(ThemeMode mode)
        {
            _theme = mode;
            _palette = ThemePalette.For(mode);
        }

        // 仅重涂控件颜色（不重绘图标），用于启动初始化与主题切换
        private void ApplyThemeVisuals()
        {
            this.BackColor = _palette.FormBack;
            _corners.BorderColor = _palette.WindowBorder;   // 窗口外沿 1px 描边（由 PaintBackground 统一绘制）

            // —— 标题栏（自绘 chrome）——
            if (searchBar != null) searchBar.Palette = _palette;
            if (searchBox != null)
            {
                // 内核 TextBox 底色必须与外壳一致，否则圆角内会露出一个方形色块
                searchBox.BackColor = _palette.SearchBack;
                if (!searchBox.Focused) searchBox.ForeColor = _palette.SearchCue;
            }
            foreach (var b in new[] { btnSearch, btnOpenLocation, btnTheme, btnNotes, btnPinTop, btnSettings, btnClose })
                if (b != null) b.Palette = _palette;
            if (btnPinTop != null) btnPinTop.Active = this.TopMost;
            SyncNotesButton();   // 便签按钮激活态 = 独立便签窗口是否可见

            // —— 便签：已拆成独立窗口，主题直接转交给它 ——
            if (_notesWindow != null && !_notesWindow.IsDisposed)
            {
                _notesWindow.ApplyPalette(_palette);
                _notesWindow.BackColor = _palette.PanelBack;
            }

            // —— 右键菜单 / 托盘菜单：自绘渲染器，去掉系统 3D 边框与渐变 ——
            if (contextMenuStrip1 != null)
            {
                contextMenuStrip1.BackColor = _palette.MenuBack;
                contextMenuStrip1.ForeColor = _palette.MenuFore;
                contextMenuStrip1.Renderer = new FlatMenuRenderer(_palette);
                // 内缩一点，菜单项才不会贴到圆角边上被裁掉
                contextMenuStrip1.Padding = new Padding(2, UiMetrics.XS, 2, UiMetrics.XS);
                MenuRounder.Attach(contextMenuStrip1);   // 圆角 + 系统投影（见 MenuRounder）
            }
            exitToolStripMenuItem.BackColor = _palette.MenuBack;
            exitToolStripMenuItem.ForeColor = _palette.MenuFore;

            RefreshPageIndicator();
            foreach (var slot in _slots) slot.ApplyTheme(_palette);
        }

        // 刷新分页指示器（页数 / 配色 / 页名 / 当前选中位），页面增删改名后统一调用
        private void RefreshPageIndicator()
        {
            if (_pageIndicator == null) return;
            _pageIndicator.Setup(_store.Layout.PageCount, _palette.DotActive, _palette.DotInactive);
            _pageIndicator.SetPageNames(_store.PageNames);
            // 指示器自身要铺「底部导航条底衬色」而非窗口底色：它是普通 Control（不透明），
            // 不设的话会在底部条上显出一块比周围亮的方块。
            _pageIndicator.BackColor = _palette.BottomBarBack;
            _pageIndicator.SelectedIndex = _store.PageNames.IndexOf(_currentPageName);
            RepositionPageDots();   // 页数变化会改变指示器宽度，需重新居中
        }

        // 重命名第 index 页：弹出输入框，成功后持久化并刷新指示器
        private void RenamePage(int index)
        {
            if (index < 0 || index >= _store.PageNames.Count) return;
            string oldName = _store.PageNames[index];
            using (var dlg = new PageNameDialog("重命名页面", oldName, _palette))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string newName = dlg.PageName;
                if (string.IsNullOrEmpty(newName)) return;
                if (_store.RenamePage(index, newName))
                {
                    if (_currentPageName == oldName) _currentPageName = newName;
                    // 缓存按页名跟踪，重命名后更新键（位图仍有效，不必重渲染）
                    RekeyPageCache(oldName, newName);
                    SaveConfig();
                    RefreshPageIndicator();
                    Log("重命名页面: " + oldName + " → " + newName);
                }
                else
                {
                    MessageBox.Show("页面名称无效：不能为空、超过 20 字符、包含 \\ / : * ? \" < > | 等字符，或与其他页重名。",
                        "无法重命名", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        // 主题切换：重涂控件 + 按新配色重绘图标缓存（文件名颜色随主题变化）
        private void ToggleTheme()
        {
            if (this.InvokeRequired) { this.Invoke(new Action(ToggleTheme)); return; }
            SetPalette(_theme == ThemeMode.Dark ? ThemeMode.Light : ThemeMode.Dark);
            SaveThemeSetting();
            ApplyThemeVisuals();
            RebuildAllIcons(); // 只重渲染当前页缓存；其他页下次切到/搜索到才按新配色渲染
            _pageIndicator.SelectedIndex = _store.PageNames.IndexOf(_currentPageName);
            // 搜索进行中：重建缓存后按当前查询重排，避免旧位图引用让结果空白
            if (_search.IsSearching && searchBox != null && searchBox.Text != SearchCue)
                _search.ApplyFilter(searchBox.Text);
            else if (_search.IsSearching)
                _search.UpdateHighlight();
            Invalidate();
        }

        private void RebuildAllIcons()
        {
            try
            {
                // 主题切换：用 RawIcon（纯图标，主题无关）+ 新配色重新合成所有已缓存页的位图，
                // 不重新提取系统图标。RawIcon 缺失时（如导入配置后路径失效）补渲染，避免图标空白。
                var pagesToRebuild = new HashSet<string>(_pageCacheLoaded) { _currentPageName };
                foreach (var name in pagesToRebuild)
                {
                    var list = _store.GetPage(name);
                    if (list == null) continue;
                    foreach (var d in list)
                    {
                        if (d == null || d.IsEmpty) continue;
                        if (d.RawIcon == null)
                            d.RawIcon = IconRenderer.RenderIconOnly(
                                _store.Layout.IconSize, _store.Layout.IconSize, d.FilePath, d.IconPath);
                        d.CachedImage?.Dispose();
                        d.CachedImage = IconRenderer.ComposeBitmap(
                            d.RawIcon, _store.Layout.IconSize, _store.Layout.IconSize, d.FilePath, _palette, d.DisplayName);
                    }
                }
                // 当前页刷到屏幕上（其余页下次切到/搜索到即用新配色位图）
                LoadPageFromCache(_currentPageName);
                Invalidate();
            }
            catch (Exception ex) { Log("重绘图标失败: " + ex.Message); }
        }

        private void SaveThemeSetting()
        {
            try
            {
                _store.Theme = _theme;
                SaveConfig();
            }
            catch (Exception ex) { Log("保存主题配置失败: " + ex.Message); }
        }

        // 应用窗口不透明度：100% 时保持完全不透明（不启用 WS_EX_LAYERED，性能最佳）；
        // 仅当用户显式调低时才启用分层窗口。下限 30% 防止窗口被调成"看不见"。
        private void ApplyOpacity()
        {
            try
            {
                int pct = _store.Layout != null ? _store.Layout.Opacity : 100;
                this.Opacity = (pct >= 100) ? 1.0 : Math.Max(0.30, pct / 100.0);
                // Opacity < 1 会让窗口变成 WS_EX_LAYERED，DWM 原生圆角随即失效；
                // 此时 ApplyRoundCorners 内部会判定并退回 Region 裁剪，调回 100% 再切回 DWM。
                // 所以这里必须无条件调用（按 pct 分支会在"从 100 调低"时丢掉圆角）。
                if (IsHandleCreated) ApplyRoundCorners();
            }
            catch (Exception ex) { Log("应用窗口不透明度失败: " + ex.Message); }
        }

        // ===== 标题栏 / 搜索框（自绘 chrome）=====
        // 标题栏全部换成自绘矢量图标按钮（原先 5 个原生 Button 字体各异、还会露系统浅色悬停块）。
        // 搜索框自 2026-09-28 起改为「图标展开式」：收起态只占一个放大镜图标位，点开才铺开成输入框，
        // 省下的宽度全给图标网格 —— 原来常驻的搜索框在窄窗口里会挤掉一整条标题栏。
        private void BuildChrome()
        {
            // 标题栏的"品牌名"（原先那个 "Launcher" Label）已删除：它占着一块不小的宽度，
            // 却只重复了任务栏/托盘已经说过的信息。删掉后搜索框直接从左边距起排。
            // 拖动不受影响 —— 主面板的整块客户区在 WM_NCHITTEST 里都返回 HTCAPTION
            //（见本类 WndProc），空白处、图标间隙都能拖走窗口，不依赖那个 Label。

            btnOpenLocation = new IconGlyphButton(_palette, GlyphKind.Folder);
            btnOpenLocation.Click += btnOpenFolder_Click;
            Controls.Add(btnOpenLocation);

            btnTheme = new IconGlyphButton(_palette, GlyphKind.Theme);
            btnTheme.Click += (s, e) => ToggleTheme();
            Controls.Add(btnTheme);

            btnNotes = new IconGlyphButton(_palette, GlyphKind.Note);
            btnNotes.Click += btnNotes_Click;
            Controls.Add(btnNotes);

            btnPinTop = new IconGlyphButton(_palette, GlyphKind.Pin);
            btnPinTop.Click += btnPinTop_Click;
            Controls.Add(btnPinTop);

            // 设置入口。此前主面板里没有任何进设置的按钮，只能右键托盘图标去找 ——
            // 一个图标按钮就解决了，不必再绕道托盘。
            btnSettings = new IconGlyphButton(_palette, GlyphKind.Settings);
            btnSettings.Click += (s, e) => ShowSettings();
            Controls.Add(btnSettings);

            btnClose = new IconGlyphButton(_palette, GlyphKind.Close) { Danger = true };
            btnClose.Click += exitbutton_Click;
            Controls.Add(btnClose);

            // 搜索：收起态就是这个放大镜图标；点开后在原位长出搜索框（内含同一个放大镜）
            btnSearch = new IconGlyphButton(_palette, GlyphKind.Search);
            btnSearch.Click += (s, e) => ToggleSearchBar();
            Controls.Add(btnSearch);

            // 搜索框：自绘圆角外壳 + 原生 TextBox 内核。
            // Designer 里那个 searchBox 直接注入外壳 —— 它已经绑好 TextChanged / Enter / Leave，
            // 再让外壳自建一个 Inner 就会出现"两个输入框"：外壳管着一个看不见的，
            // 用户实际在另一个里打字（放大镜被文字盖住、焦点边框永不亮、点外壳聚焦不上去）。
            searchBar = new RoundSearchBox(_palette, searchBox);
            searchBar.Visible = false;   // 默认收起，由 ToggleSearchBar 打开
            Controls.Add(searchBar);

            // 标题栏全是纯图标按钮，没有任何文字线索 —— 悬停提示是唯一能说明"这个图标是什么"的地方。
            // 对搜索尤其必要：收起态只有一个放大镜，没有提示的话用户不知道点它会展开输入框。
            // （便签窗口的便签列表早就这么做了，主面板一直缺。）
            _chromeTips = new ToolTip { InitialDelay = 400, ReshowDelay = 200 };
            _chromeTips.SetToolTip(btnSearch, "搜索：匹配本机图标，并追加 Everything 全盘命中");
            _chromeTips.SetToolTip(btnOpenLocation, "打开图标所在文件夹");
            _chromeTips.SetToolTip(btnTheme, "切换深浅色");
            _chromeTips.SetToolTip(btnNotes, "便签");
            _chromeTips.SetToolTip(btnPinTop, "置顶窗口（置顶后不自动隐藏）");
            _chromeTips.SetToolTip(btnSettings, "设置");
            _chromeTips.SetToolTip(btnClose, "隐藏到托盘（右键托盘图标可退出）");

            LayoutChrome();
        }

        // 这里原先挂了 CS_DROPSHADOW 给无边框窗口补系统投影，已移除（2026-09-28）。
        // 根因：CS_DROPSHADOW 是窗口类级的**矩形**投影，而本窗口走 Region 圆角裁剪，
        // 投影不会跟随圆角 —— 四角会各留一块方角阴影残影，越小的圆角越明显。
        // 想要投影就得放弃 Region 圆角，两件事在 Win10 上互斥；用户明确选择"不要阴影"。
        // 三个窗口（主面板 / 设置 / 便签）一致，见 WindowCorners.cs 顶部说明。

        // 句柄就绪后套窗口圆角：Win11+ 走 DWM 原生，Win10- 退回 Region 裁剪（见 ApplyRoundCorners）
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyRoundCorners();
        }

        private const int CornerRadius = UiMetrics.RadiusWindow;    // 窗口圆角半径（DIP，按 96dpi 基准，实际按 DeviceDpi 缩放）
        private bool? _dwmCorners;             // DWM 原生圆角可用性，首次探测后缓存
        private Size _regionFor = Size.Empty;  // 已按哪个尺寸裁过 Region，避免 resize 时重复重建
        private readonly WindowCornerState _corners = new WindowCornerState(CornerRadius); // 圆角抗锯齿（采样桌面底色）

        // 背景自绘：先铺一层从窗口外沿采来的桌面底色，再叠一层抗锯齿圆角。
        // Win10 只能用 Region 硬裁圆角，那条裁边正好落在这层底色上 → 锯齿消失（详见 WindowCorners）。
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (IsHandleCreated) _corners.PaintBackground(this, e.Graphics);
            else base.OnPaintBackground(e);
        }

        // 底部导航条底衬：一层略暗于窗口底的填充 + 一条上边线，
        // 让分页胶囊有"自己的区域"，而不是浮在图标网格下方的空白里（浮着时读者看不出
        // 它属于谁，也分不清图标到哪儿为止）。
        //
        // 画在 Form.OnPaint 而不是放一个 Panel：Panel 会吃掉鼠标消息，把底部这一小片
        // "拖动空白处移动窗口"的能力废掉；画在 Form 上则完全不参与命中测试。
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_pageIndicator == null || !_pageIndicator.Visible) return;   // 搜索态没有分页，也就不需要底衬

            int h = UiMetrics.BottomBarH;
            int y = ClientSize.Height - h;
            if (y <= 0) return;

            using (var b = new SolidBrush(_palette.BottomBarBack))
                e.Graphics.FillRectangle(b, 0, y, ClientSize.Width, h);
            using (var pen = new Pen(_palette.Sep, 1f))
                e.Graphics.DrawLine(pen, 0, y, ClientSize.Width, y);
        }

        // 套窗口圆角，两条路径：
        //   ① Win11+ → DWM 原生圆角（系统合成器绘制：抗锯齿、投影正常、随缩放自适应）
        //   ② Win10- → Region 裁剪兜底，边缘无抗锯齿 —— 由 OnPaintBackground 配合消锯齿：
        //      Region 半径比绘制半径大 1px，硬裁边落在「采样底色」那一圈上，与桌面同色即无缝。
        // 另：WS_EX_LAYERED（不透明度 < 100%）会让 DWM 圆角失效，此时也退回 Region。
        private void ApplyRoundCorners()
        {
            if (!IsHandleCreated) return;

            if (_dwmCorners == null)
            {
                try { _dwmCorners = NativeMethods.EnableRoundCorners(this.Handle); }
                catch { _dwmCorners = false; }
            }

            if (_dwmCorners == true && Opacity >= 1.0)
            {
                // 交给 DWM 处理。若此前裁过 Region 必须清掉 —— Region 会盖过 DWM 圆角，
                // 白白把抗锯齿的圆角换成带锯齿的裁剪。
                var stale = Region;
                if (stale != null) { Region = null; stale.Dispose(); }
                _regionFor = Size.Empty;
                return;
            }

            try
            {
                var size = new Size(Width, Height);
                if (_regionFor == size && Region != null) return;   // 尺寸没变，沿用现有 Region
                int r = _corners.Radius(this);
                var old = Region;
                Region = WindowCorners.BuildRegion(size, r);
                if (old != null) old.Dispose();
                _regionFor = size;
            }
            catch (Exception ex) { Log("应用窗口圆角失败: " + ex.Message); }
        }

        // ===== 动态生成控件 =====
        private void BuildSlots()
        {
            // 支持重复调用（设置面板改布局后重建）：先移除并释放旧槽位，避免控件叠加与句柄泄漏
            foreach (var old in _slots)
            {
                if (!old.IsDisposed) { this.Controls.Remove(old); old.Dispose(); }
            }

            int gap = UiMetrics.SlotGap;
            int step = _store.Layout.IconSize + gap;
            int left = UiMetrics.GridPadH, top = UiMetrics.TitleBarH + UiMetrics.GridPadTop;
            int rows = (int)Math.Ceiling(_store.Layout.SlotCount / (double)_store.Layout.Columns);

            _slots = new List<IconSlot>();
            for (int i = 0; i < _store.Layout.SlotCount; i++)
            {
                int col = i % _store.Layout.Columns;
                int row = i / _store.Layout.Columns;
                var slot = new IconSlot();
                slot.SlotIndex = i;
                slot.Name = "slot" + (i + 1);
                slot.OriginalSize = new Size(_store.Layout.IconSize, _store.Layout.IconSize);
                slot.BackgroundImageLayout = ImageLayout.Zoom;
                slot.Size = new Size(_store.Layout.IconSize, _store.Layout.IconSize);
                slot.SizeMode = PictureBoxSizeMode.Zoom;
                slot.Location = new Point(left + col * step, top + row * step);
                slot.TabIndex = i;
                slot.TabStop = false;
                slot.ApplyTheme(_palette);

                slot.OpenRequested += (s, e) => OpenSlot(slot);
                slot.OpenLocationRequested += (s, e) => OpenLocation(slot);
                slot.DeleteRequested += (s, e) => DeleteSlot(slot);
                slot.SetIconRequested += (s, e) => SetSlotIcon(slot);
                slot.ClearIconRequested += (s, e) => ClearSlotIcon(slot);
                slot.AddCommandRequested += (s, e) => AddOrEditCommand(slot);
                slot.MoveToPageRequested += (s, dir) => MoveSlotToPage(slot, dir);
                slot.CanMovePrev = () => _store.PageNames.IndexOf(_currentPageName) > 0;
                slot.CanMoveNext = () => _store.PageNames.IndexOf(_currentPageName) < _store.PageNames.Count - 1;
                slot.MoveToPageRequested += (s, dir) => MoveSlotToPage(slot, dir);
                slot.CanMovePrev = () => _store.PageNames.IndexOf(_currentPageName) > 0;
                slot.CanMoveNext = () => _store.PageNames.IndexOf(_currentPageName) < _store.PageNames.Count - 1;
                slot.FileDropped += (s, path) => HandleFileDropped(slot, path);
                slot.DropTarget += (s, src) => SwapSlots(src, slot);
                slot.DragStarted += (s, e) => { _dragSourceSlot = slot; };
                slot.WindowDragRequested += (s, e) => StartWindowDrag();

                _slots.Add(slot);
                this.Controls.Add(slot);
            }

            // 窗口尺寸 = 左右内边距 + 图标网格 + 底部分页条预留（全部取自 UiMetrics 栅格）
            int formW = left + _store.Layout.Columns * step - gap + UiMetrics.GridPadH;
            int formH = top + rows * step - gap + UiMetrics.BottomBarH;
            // 自定义窗口尺寸：用户拖拽调整后写回配置；0 则按列数×图标大小自动计算。
            // 不做硬下限——缩小后图标列数自适应减少（见 LayoutChrome），只会变多行、不会裁掉图标。
            // 仅保留一个很小的绝对下限（标题栏高度 + 一行），防止窗口被压到看不见。
            _minGridSize = new Size(formW, formH);
            int finalW = _store.Layout.Width > 0 ? _store.Layout.Width : formW;
            int finalH = _store.Layout.Height > 0 ? _store.Layout.Height : formH;
            // 配置里存过高度 = 用户手动拖过，之后自动高度只撑高不回缩（见 ApplyAutoHeight）
            _heightFromUser = _store.Layout.Height > 0;
            this.MinimumSize = new Size(100, 48);
            this.ClientSize = new Size(finalW, finalH);
            // 重建槽位后网格可能与当前窗口高矮不匹配（换了图标大小 / 列数），允许自动撑高
            LayoutChrome(true);
        }

        // 底部导航条预留高度（分页胶囊 + 上下留白）。图标网格不再往这块区域里排，
        // 否则缩窄窗口后行数变多，图标会一直排到窗口下沿、把分页 tab 顶掉/盖住。
        // 该区域内还会画一层略暗的底衬 + 上边线，让分页胶囊有"自己的区域"而不是浮在空白里。
        private const int BottomBarH = UiMetrics.BottomBarH;

        // 按当前窗口尺寸重排：图标网格自适应宽度（列数随可用宽度增减），
        // 网格水平居中、标题栏图标按钮贴右、底部分页胶囊居中。
        //
        // allowAutoSize = 是否允许「内容放不下就自动撑高窗口」。默认 false ——
        // 只有**宽度变化**（列数变→行数变）与**布局参数变化**（图标大小/列数/槽位数）
        // 才有资格改窗口高度。开关搜索框、移动窗口、纯拖高度一律传 false，
        // 否则用户刚调好的尺寸会被无声地改回去（这正是移动窗口后尺寸"弹回"的成因）。
        private void LayoutChrome(bool allowAutoSize = false)
        {
            if (_inLayout) return;   // 内部调 ClientSize 会再触发 OnResize，用重入保护避免半成品布局
            _inLayout = true;
            try
            {
                // ① 先保证高度放得下（缩窄 → 列数变少 → 行数变多 → 宽度换高度）
                if (allowAutoSize) ApplyAutoHeight();

                int gap = UiMetrics.SlotGap;
                int step = _store.Layout.IconSize + gap;
                int avail = this.ClientSize.Width - UiMetrics.GridPadH * 2;
                int cols = Math.Max(1, (avail + gap) / step);
                int totalW = cols * step - gap;
                int left = Math.Max(UiMetrics.GridPadH, (this.ClientSize.Width - totalW) / 2);
                int top = UiMetrics.TitleBarH + UiMetrics.GridPadTop;

                for (int i = 0; i < _slots.Count; i++)
                {
                    int col = i % cols;
                    int row = i / cols;
                    _slots[i].Location = new Point(left + col * step, top + row * step);
                }

                LayoutTitleBar();
                RepositionPageDots();
            }
            finally { _inLayout = false; }
        }

        // 标题栏排布。两条互不干扰的规则，按顺序执行：
        //   ① 右侧图标按钮组从右往左排，按「关闭在最右」的 Windows 习惯；窄窗口里放不下的
        //      自动隐藏（宁可少一个入口，也不要几个按钮叠在一起画）。
        //   ② 搜索：**收起态的放大镜图标与展开态的输入框共用左边距这个锚点**。
        //
        // 品牌名（"Launcher"）已删除，所以锚点固定 = 左内边距，不再有"品牌名字宽多少、
        // 放不放得下"这层判断 —— 旧实现里正是这层判断让"点一下搜索标题栏左半边就变样"。
        // 搜索展开/收起只影响它自己那一格，左右两边的东西一动不动。
        private void LayoutTitleBar()
        {
            if (btnClose == null) return;
            const int barH = UiMetrics.TitleBarH;
            const int minLeft = UiMetrics.ChromeBtnW;   // 左侧至少给搜索图标留一个按钮位，否则右侧按钮开始隐藏

            // —— ① 右侧按钮组（位置只由窗口宽度决定，与搜索状态无关）——
            int right = this.ClientSize.Width - UiMetrics.S;
            foreach (var b in new[] { btnClose, btnPinTop, btnNotes, btnTheme, btnOpenLocation, btnSettings })
            {
                if (b == null) continue;
                // 关闭按钮最后才让位（窗口再窄也要能把它藏起来）；其余按钮按剩余宽度逐个隐藏，
                // 宁可少一个入口，也不要几个按钮叠在一起画。
                bool fits = (b == btnClose) ? (right - b.Width >= UiMetrics.S) : (right - b.Width >= minLeft);
                b.Visible = fits;
                if (!fits) continue;
                b.Location = new Point(right - b.Width, (barH - b.Height) / 2);
                right -= b.Width + UiMetrics.ChromeBtnGap;
            }
            int groupLeft = right + UiMetrics.ChromeBtnGap;   // 按钮组的左边缘（右侧内容到此为止）

            if (btnSearch == null || searchBar == null) return;

            // —— ② 搜索：图标与输入框互斥，共用同一个锚点 ——
            int anchor = UiMetrics.GridPadH;
            int room = groupLeft - UiMetrics.ChromeBtnGap - anchor;   // 锚点到按钮组之间可用宽度

            if (_searchBarOpen && room >= UiMetrics.SearchMinW)
            {
                // 展开态：放大镜让位给输入框。框从锚点铺到按钮组左边缘，宽高都取栅格值，
                // 纵向与图标同高居中，所以视觉上是"那个放大镜原地变成了输入框"，没有位移。
                btnSearch.Visible = false;
                searchBar.SetBounds(anchor, (barH - UiMetrics.SearchH) / 2, room, UiMetrics.SearchH);
                searchBar.Visible = true;
            }
            else
            {
                // 收起态。窗口窄到放不下输入框时也走这里 —— 此时**放大镜必须可见**，
                // 用户点开又点上，看到的应该是同一个图标，而不是"图标没了"。
                //
                // 因宽度不足而被"挤回"收起态时，必须走完整的收起流程（清掉查询词、
                // 恢复网格），否则会留下"网格还停在筛选结果上、搜索框却已经没了"的死状态。
                // 里面的 LayoutChrome() 会被 _inLayout 拦下，不会递归回这里。
                if (_searchBarOpen) CollapseSearchBar();

                _searchBarOpen = false;
                searchBar.Visible = false;
                btnSearch.Visible = room >= btnSearch.Width;
                if (btnSearch.Visible)
                    btnSearch.Location = new Point(anchor, (barH - btnSearch.Height) / 2);
            }
        }

        // 缩窄窗口 → 列数变少 → 行数变多 → 需要更高才能放下全部图标与底部分页条。
        // 这里把高度撑到"刚好够"（上限取主屏工作区），但**只服务"从没自己定过尺寸"的新配置**：
        //   · 用户手动定过高度（配置里存过 Height）→ 高度完全由用户掌控，一个字都不改
        //   · 从没定过（全新配置）→ 完全跟随内容高度
        //
        // ⚠️ 本函数只允许由**宽度变化 / 布局参数变化**触发（见 LayoutChrome 的 allowAutoSize）。
        // 移动窗口与纯拖高度绝不能走到这里，否则用户刚改好的尺寸会被立刻改回去。
        private void ApplyAutoHeight()
        {
            if (_store.Layout.SlotCount <= 0 || !IsHandleCreated || _userResizing) return;
            int gap = UiMetrics.SlotGap;
            int step = _store.Layout.IconSize + gap;
            int top = UiMetrics.TitleBarH + UiMetrics.GridPadTop;
            int avail = this.ClientSize.Width - UiMetrics.GridPadH * 2;
            int cols = Math.Max(1, (avail + gap) / step);
            int rows = (int)Math.Ceiling(_store.Layout.SlotCount / (double)cols);
            int need = top + rows * step - gap + BottomBarH;

            int wa = Screen.FromControl(this).WorkingArea.Height;
            int target = Math.Min(need, Math.Max(200, wa - 80));
            if (ClientSize.Height == target) return;

            // ⭐ 用户手动定过高度 → 高度完全归用户，程序一律不改 —— 包括"放不下就撑高"。
            //
            // 旧实现在这里还留着一个兜底分支 `|| ClientSize.Height < target`（内容放不下就撑高）。
            // 看着很合理，但用户拖动窗口**右下角**同时缩窄+缩矮时，宽度确实变了 →
            // 走 widthChanged 路径进到这里 → 高度被这个兜底撑回"够放下全部图标"的高度，
            // 用户眼里就是「我把它改矮了，它自己又长回来」（日志里 492×350 ← 804×279 即此）。
            // 对用户来说：尺寸被程序悄悄改掉，比图标排到窗口外更难接受 —— 后者看得见、拖一下就修好。
            if (_heightFromUser)
            {
                // 唯一的兜底：矮到连一行图标 + 底部分页条都装不下时，窗口里是空的，
                // 用户会以为程序坏了。这时才抬到"一行 + 底栏"这个最小可读高度。
                int floor = top + step - gap + BottomBarH;
                if (ClientSize.Height < floor)
                    ClientSize = new Size(ClientSize.Width, floor);
                return;
            }

            ClientSize = new Size(ClientSize.Width, target);
        }

        private void RepositionPageDots()
        {
            if (_pageIndicator == null) return;
            // 宽度直接取指示器自身（胶囊与圆点宽度不同，不能再按页数硬算）
            int startX = Math.Max(UiMetrics.XS, (this.ClientSize.Width - _pageIndicator.Width) / 2);
            // 垂直居中于底部导航条内（而不是贴窗口下沿） —— 底部条有自己的底衬，胶囊要居其中
            int barTop = this.ClientSize.Height - UiMetrics.BottomBarH;
            int y = barTop + Math.Max(0, (UiMetrics.BottomBarH - _pageIndicator.Height) / 2);
            _pageIndicator.Location = new Point(startX, y);
            // 指示器必须压在图标之上：WinForms 里后 Add 的控件在 z-order 更靠后（更底层），
            // 而它在 BuildPageDots 里是最后添加的 —— 缩窄后图标换行排到底部就会把它盖住，
            // 表现正是「缩窄窗口后底下的页面 tab 看不见了」。
            if (Controls.GetChildIndex(_pageIndicator) != 0) _pageIndicator.BringToFront();
            // 底部条底衬画在 Form.OnPaint 上，位置随尺寸变化，这里主动作废那一小片区域
            Invalidate(new Rectangle(0, Math.Max(0, barTop - 2), ClientSize.Width, UiMetrics.BottomBarH + 2));
        }

        private void BuildPageDots()
        {
            // 支持重复调用（页数变化后重建）：先释放旧指示器
            if (_pageIndicator != null)
            {
                this.Controls.Remove(_pageIndicator);
                _pageIndicator.Dispose();
            }
            _pageIndicator = new PageIndicator();
            _pageIndicator.Setup(_store.Layout.PageCount, _palette.DotActive, _palette.DotInactive);
            _pageIndicator.SetPageNames(_store.PageNames);
            _pageIndicator.BackColor = _palette.BottomBarBack;   // 与底部导航条底衬同色，否则会显出色块
            _pageIndicator.PageSelected += (s, idx) => SwitchToPage(idx);
            _pageIndicator.PageRenameRequested += (s, idx) => RenamePage(idx);
            this.Controls.Add(_pageIndicator);
            RepositionPageDots();
        }

        public Launcher()
        {
            InitializeComponent();
            // 双缓冲：消除鼠标扫过面板 / 拖动窗口时的背景闪烁与撕裂（"漂移"观感的来源之一）。
            // UpdateStyles 让样式立即生效，不必等句柄重建。
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            DoubleBuffered = true;
            UpdateStyles();
            _store.Load();
            SetPalette(_store.Theme);
            ApplyOpacity();
            BuildChrome();   // 标题栏 / 搜索框外壳 / 便签切换按钮（自绘控件，需先有 _palette）
            BuildSlots();
            BuildPageDots();
            // 恢复上次所在页（重启记忆）
            int lastIdx = _store.PageNames.IndexOf(_store.LastPage);
            _currentPageName = (lastIdx >= 0) ? _store.PageNames[lastIdx] : (_store.PageNames.Count > 0 ? _store.PageNames[0] : "第1页");

            LoadRuntimeAssets();
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.MouseWheel += new MouseEventHandler(Form1_MouseWheel);
            this.Deactivate += Launcher_Deactivate;
            // 贴边自动隐藏（借鉴亦安 edge_auto_hide）：面板拖到屏幕左/右/上边缘停稳后自动缩进去，
            // 鼠标移上去停 500ms 再滑出来。计时器只在窗口可见时跑（见 OnVisibleChanged），隐藏期间停掉。
            _edgeHide = new EdgeAutoHide(this) { Enabled = _store.Layout.EdgeAutoHideEffective };
            RegisterGlobalHotkeys();
            SetupDesktopMouseHook();
            _search = new SearchController(_slots, _pageIndicator, _store,
                () => _currentPageName,
                EnsurePageLoaded,
                v => _suppressPageEviction = v,
                () => _palette,
                StartEverythingSearch);   // 本地结果出来后异步补一刀全盘搜索
            // 钩子不在构造函数安装：WH_MOUSE_LL 回调须由本线程消息循环执行，
            // 开机自启时构造函数还在做加载等繁重工作（未进消息循环），安装过早会让全局鼠标输入被卡住发飘。
            // 推迟到首次显示后安装，此时消息循环已就绪，不再拖累启动。
            // 同时分批异步渲染当前页图标，避免一次性渲染阻塞消息循环导致鼠标卡顿。
            this.Shown += (s, e) =>
            {
                _desktopMouseHook.Install();
                StartInitialPageRender(_currentPageName);
                if (_edgeHide != null) _edgeHide.Start();   // 首次显示兜底启动贴边监听
                ApplyEdgeAutoHide();                        // 按当前配置同步一次开关
            };

            _animationTimer = new Timer();
            _animationTimer.Interval = 50;
            _animationTimer.Tick += AnimationTimer_Tick;

            _configWatcher = new ConfigWatcher(this, AppDomain.CurrentDomain.BaseDirectory);
            _configWatcher.Reloaded += ReloadConfigFromWatcher;
            _configWatcher.Start();

            // 便签已拆成独立窗口，这里不再初始化：NotesWindow 在首次打开时自建
            // （存储仍是 notes/ 目录，懒加载还能省掉一次启动期的磁盘 IO）。

            // 开机自启的当前状态由设置面板「常规」页在打开时直接读注册表，托盘不再维护勾选态

            // 启动时只设主题色/圆点（快），不渲染图标——图标在 Shown 后分批异步渲染，
            // 避免构造函数同步渲染整页图标阻塞 UI 消息循环，导致窗口迟迟不显示 + 鼠标卡顿。
            ApplyThemeVisuals();
            Log("启动完成，当前配置: " + _currentPageName + "，主题=" + (_theme == ThemeMode.Dark ? "Dark" : "Light"));
        }

        // 图标与图片已从 resx 移出，改为运行时从文件加载
        private void LoadRuntimeAssets()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string icoPath = Path.Combine(baseDir, "Resources", "Launcher.ico");
            if (File.Exists(icoPath))
            {
                try { this.Icon = new Icon(icoPath); }
                catch (Exception ex) { Log("加载窗体图标失败: " + ex); }
                try { if (notifyIcon1 != null) notifyIcon1.Icon = new Icon(icoPath); }
                catch (Exception ex) { Log("加载托盘图标失败: " + ex); }
            }
            // 标题栏图标改为矢量自绘（见 UiGlyph），不再需要 Resources\button2.png
        }

        // 写日志（实现已下沉到 Core.Logger，这里仅作兼容转发，供窗体内各处调用）。
        public static void Log(string message) => Logger.Log(message);

        // 配置文件热更新已抽到 Core/ConfigWatcher
        private void ReloadConfigFromWatcher()
        {
            _store.Load();
            if (_store.PageNames.IndexOf(_currentPageName) < 0)
                _currentPageName = _store.PageNames.Count > 0 ? _store.PageNames[0] : "第1页";
            RegisterGlobalHotkeys();
            ApplyEdgeAutoHide();   // 手改 launcher.json 里的贴边开关也要即时生效
            RefreshPageIndicator();
            InvalidateAllPageCaches();
            // 走后台渲染，避免重载配置时在 UI 线程同步渲染整页图标（会卡鼠标）
            StartInitialPageRender(_currentPageName);
        }

        // ===== 槽位交互 =====
        // 手动启动窗口移动：让 Windows 接管拖拽循环（等价于按住标题栏拖动），无边框窗口也能整窗拖动。
        // 便签窗口里有一份逐字相同的实现，已一并收进 WindowChrome.StartDrag。
        private void StartWindowDrag() { WindowChrome.StartDrag(this.Handle); }

        private void OpenSlot(IconSlot slot)
        {
            if (slot.Data == null || slot.Data.IsEmpty) return;
            _currentAnimatingSlot = slot;
            _animationTimer.Start();
            try
            {
                LaunchPath(slot.Data.FilePath, slot.Data.IsFolder);
                HidePanelAfterOpen();
            }
            catch (Exception ex)
            {
                Log("启动失败: " + ex + " 命令: " + slot.Data.FilePath);
                MessageBox.Show("启动失败：" + ex.Message + "\n命令/路径：" + slot.Data.FilePath, "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // 统一启动入口：支持 文件/文件夹/URL/GUID(::{CLSID})/shell:路径/shell 命令(带参数)
        // 用户在 launcher.json 的 path 字段填入即可，图标用右键「设置自定义图标」提供。
        private static void LaunchPath(string filePath, bool isFolder)
        {
            if (string.IsNullOrEmpty(filePath)) return;

            // GUID（::{CLSID}，如此电脑/回收站/控制面板）或 shell: 路径（如 shell:AppsFolder）→ explorer.exe 打开
            if (filePath.StartsWith("::") || filePath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                System.Diagnostics.Process.Start("explorer.exe", filePath);
                return;
            }

            // URL → 默认浏览器
            if (UrlUtil.IsHttpUrl(filePath))
            {
                System.Diagnostics.Process.Start(filePath);
                return;
            }

            // 文件夹 → explorer
            if (isFolder || Directory.Exists(filePath))
            {
                System.Diagnostics.Process.Start("explorer.exe", filePath);
                return;
            }

            // 文件 / 命令：先尝试直接启动（关联打开 / .exe / .lnk），
            // 失败则解析为命令行（支持 cmd /c dir、powershell -Command ... 等带参数命令）。
            try
            {
                System.Diagnostics.Process.Start(filePath);
            }
            catch
            {
                // 直接启动失败（含空格/参数的命令串）→ 解析为命令+参数
                var (fileName, args) = ParseCommandLine(filePath);
                if (string.IsNullOrEmpty(fileName)) return;
                // 先用 UseShellExecute=true（ShellExecute 查 PATH + 关联，对 "cmd" 等无扩展名命令更灵活）
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(fileName, args) { UseShellExecute = true });
                }
                catch
                {
                    // 再用 UseShellExecute=false（CreateProcess，支持 cmd /c echo > file 等重定向/管道）
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(fileName, args) { UseShellExecute = false });
                }
            }
        }

        // 简单命令行解析：支持引号包裹的路径 + 多参数。如 "cmd /c dir" → ("cmd", "/c dir")
        private static (string FileName, string Arguments) ParseCommandLine(string cmd)
        {
            cmd = cmd.Trim();
            if (string.IsNullOrEmpty(cmd)) return ("", "");
            // 引号包裹的第一段（路径含空格）
            if (cmd[0] == '"')
            {
                int end = cmd.IndexOf('"', 1);
                if (end > 0)
                    return (cmd.Substring(1, end - 1), end + 1 < cmd.Length ? cmd.Substring(end + 1).Trim() : "");
            }
            // 按第一个空格分割命令与参数
            int space = cmd.IndexOf(' ');
            if (space < 0) return (cmd, "");
            return (cmd.Substring(0, space), cmd.Substring(space + 1));
        }

        // 点击图标打开后主动隐藏面板，避免目标程序未抢焦(focus)时 Deactivate 不触发导致面板残留
        private void HidePanelAfterOpen()
        {
            if (this.TopMost) return;                 // 置顶(钉住)模式不隐藏
            if (!_store.Layout.AutoHideEffective) return; // 关闭自动隐藏时不隐藏
            this.Hide();
        }

        private void OpenLocation(IconSlot slot)
        {
            if (slot.Data == null || slot.Data.IsEmpty) return;
            string filePath = slot.Data.FilePath;
            try
            {
                if (UrlUtil.IsHttpUrl(filePath))
                {
                    System.Diagnostics.Process.Start(filePath);
                }
                else if (File.Exists(filePath))
                {
                    System.Diagnostics.Process.Start("explorer.exe", "/select, \"" + filePath + "\"");
                }
                else if (Directory.Exists(filePath))
                {
                    System.Diagnostics.Process.Start("explorer.exe", filePath);
                }
            }
            catch (Exception ex)
            {
                Log("无法打开文件位置: " + ex);
                MessageBox.Show("无法打开文件位置：" + ex.Message);
            }
        }

        private void DeleteSlot(IconSlot slot)
        {
            if (slot.Data == null || slot.Data.IsEmpty) return;
            PushUndo(new DeletedSlot
            {
                Slot = slot,
                Data = new SlotData(slot.SlotIndex) { FilePath = slot.Data.FilePath, IsFolder = slot.Data.IsFolder }
            });
            slot.Data = new SlotData(slot.SlotIndex);
            slot.Image = null;
            _store.RemoveSlot(_currentPageName, slot.SlotIndex);
            ShowDeleteBalloon();
            SaveConfig();
        }

        // 右键菜单：设置自定义图标（用户自行提供图片文件）
        private void SetSlotIcon(IconSlot slot)
        {
            if (slot.Data == null || slot.Data.IsEmpty) return;
            try
            {
                using (var dlg = new OpenFileDialog())
                {
                    dlg.Filter = "图片 (*.png;*.jpg;*.jpeg;*.ico;*.bmp)|*.png;*.jpg;*.jpeg;*.ico;*.bmp";
                    dlg.Title = "选择自定义图标";
                    if (dlg.ShowDialog() != DialogResult.OK) return;
                    // 校验确实是可读取的图片
                    try { using (var probe = Image.FromFile(dlg.FileName)) { } }
                    catch (Exception ex) { MessageBox.Show("无法读取该图片：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
                    // 若图片位于程序目录内，存相对路径，便于整体迁移
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string stored = dlg.FileName;
                    if (stored.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                        stored = stored.Substring(baseDir.Length).TrimStart('\\', '/');
                    slot.Data.IconPath = stored;
                    // 清除旧 RawIcon/CachedImage，强制 RenderSlotFromStore 重新提取图标。
                    // 否则 RawIcon 复用旧系统图标，RenderIconOnly 被跳过，自定义图标不生效。
                    slot.Data.DisposeImage();
                    SaveConfig();
                    RenderSlotFromStore(slot);
                    Log("设置自定义图标: " + stored);
                }
            }
            catch (Exception ex) { Log("设置自定义图标失败: " + ex.Message); }
        }

        // 右键菜单：清除自定义图标，恢复自动图标
        private void ClearSlotIcon(IconSlot slot)
        {
            if (slot.Data == null || slot.Data.IsEmpty) return;
            try
            {
                slot.Data.IconPath = string.Empty;
                // 清除旧 RawIcon/CachedImage（含自定义图标），强制回退到系统图标
                slot.Data.DisposeImage();
                SaveConfig();
                RenderSlotFromStore(slot);
                Log("清除自定义图标: " + slot.Data.FilePath);
            }
            catch (Exception ex) { Log("清除自定义图标失败: " + ex.Message); }
        }

        // 右键菜单：添加/编辑命令/路径（支持文件/文件夹/URL/GUID/shell:路径/shell 命令）
        // 用户输入命令串，写入槽位；图标由用户后续右键「设置自定义图标」提供
        private void AddOrEditCommand(IconSlot slot)
        {
            try
            {
                string initialCmd = (slot.Data != null && !slot.Data.IsEmpty) ? slot.Data.FilePath : "";
                string initialName = (slot.Data != null) ? (slot.Data.DisplayName ?? "") : "";
                bool empty = string.IsNullOrEmpty(initialCmd);
                using (var dlg = new CommandDialog(
                    empty ? "添加命令/路径" : "编辑命令/路径",
                    initialCmd, initialName, _palette))
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    if (string.IsNullOrEmpty(dlg.Command)) return;
                    var data = new SlotData(slot.SlotIndex) { FilePath = dlg.Command, IsFolder = false, DisplayName = dlg.DisplayName ?? "" };
                    _store.SetSlot(_currentPageName, slot.SlotIndex, data);
                    SaveConfig();
                    RenderSlotFromStore(slot);
                    Log((empty ? "添加" : "编辑") + "命令/路径: " + dlg.Command + (string.IsNullOrEmpty(dlg.DisplayName) ? "" : "（名称=" + dlg.DisplayName + "）"));
                }
            }
            catch (Exception ex) { Log("添加/编辑命令失败: " + ex.Message); }
        }

        // 右键菜单：移动当前槽位到上一页/下一页的相同位置（direction=-1 上一页 / +1 下一页）
        private void MoveSlotToPage(IconSlot slot, int direction)
        {
            if (slot.Data == null || slot.Data.IsEmpty) return;
            int curIdx = _store.PageNames.IndexOf(_currentPageName);
            int targetIdx = curIdx + direction;
            if (targetIdx < 0 || targetIdx >= _store.PageNames.Count) return;
            string targetPage = _store.PageNames[targetIdx];
            // 复制当前槽位数据到目标页相同 index
            var data = slot.Data;
            var newData = new SlotData(slot.SlotIndex)
            {
                FilePath = data.FilePath,
                IsFolder = data.IsFolder,
                IconPath = data.IconPath,
                DisplayName = data.DisplayName
            };
            _store.SetSlot(targetPage, slot.SlotIndex, newData);
            // 当前槽位清空
            _store.RemoveSlot(_currentPageName, slot.SlotIndex);
            // 目标页若已缓存，释放缓存使下次切到时重新渲染（否则会显示旧位图）
            if (_pageCacheLoaded.Contains(targetPage)) ReleasePageCache(targetPage);
            SaveConfig();
            RenderSlotFromStore(slot);
            Log($"移动槽位 {slot.SlotIndex} 从 {_currentPageName} 到 {targetPage}");
        }

        private void HandleFileDropped(IconSlot slot, string filePath)
        {
            try
            {
                var data = new SlotData(slot.SlotIndex) { FilePath = filePath, IsFolder = Directory.Exists(filePath) };
                _store.SetSlot(_currentPageName, slot.SlotIndex, data);
                RenderSlotFromStore(slot);
                SaveConfig();
            }
            catch (Exception ex)
            {
                Log("获取图标失败: " + ex);
                MessageBox.Show("获取图标失败：" + ex.Message);
            }
        }

        private void SwapSlots(IconSlot source, IconSlot target)
        {
            if (source == null || target == null || source == target) return;
            _store.SwapSlots(_currentPageName, source.SlotIndex, target.SlotIndex);
            RenderSlotFromStore(source);
            RenderSlotFromStore(target);
            SaveConfig();
        }

        // 从 store 读取该槽位数据，渲染位图并刷新显示
        private void RenderSlotFromStore(IconSlot slot)
        {
            var data = _store.GetSlot(_currentPageName, slot.SlotIndex);
            if (data == null) return;
            if (!data.IsEmpty)
            {
                // 复用已有的 RawIcon（主题无关），避免重新提取系统图标；
                // 路径失效（导入的配置引用了不存在的文件）也渲染：GetFileIcon 退回系统默认图标，避免空白。
                if (data.RawIcon == null)
                    data.RawIcon = IconRenderer.RenderIconOnly(slot.Width, slot.Height, data.FilePath, data.IconPath);
                data.CachedImage?.Dispose();
                data.CachedImage = IconRenderer.ComposeBitmap(data.RawIcon, slot.Width, slot.Height, data.FilePath, _palette, data.DisplayName);
            }
            else
            {
                data?.DisposeImage();
            }
            slot.Data = data ?? new SlotData(slot.SlotIndex);
            // 确保显示用的 SlotData 也持有 RawIcon 引用（主题切换时可直接合成）
            if (slot.Data != data && data != null)
                slot.Data.RawIcon = data.RawIcon;
            slot.Image = slot.Data?.CachedImage;
        }

        private void AnimationTimer_Tick(object sender, EventArgs e)
        {
            if (_currentAnimatingSlot == null) return;

            if (!_isSmall)
            {
                Size os = _currentAnimatingSlot.OriginalSize;
                _currentAnimatingSlot.SizeMode = PictureBoxSizeMode.StretchImage;
                _currentAnimatingSlot.Padding = new Padding(
                    (int)(os.Width * 0.05),
                    (int)(os.Height * 0.05),
                    (int)(os.Width * 0.05),
                    (int)(os.Height * 0.05)
                );
                _isSmall = true;
            }
            else
            {
                _currentAnimatingSlot.SizeMode = PictureBoxSizeMode.Zoom;
                _currentAnimatingSlot.Padding = new Padding(0);
                _isSmall = false;
                _animationTimer.Stop();
                _currentAnimatingSlot = null;
            }
        }

        private void SaveConfig()
        {
            _store.LastPage = _currentPageName; // 记录当前页（重启恢复）
            _configWatcher.SkipNext = true;
            try { _store.Save(); }
            finally { _configWatcher.SkipNext = false; }
        }

        // ===== 撤销删除 =====
        private void PushUndo(DeletedSlot slot)
        {
            if (slot == null || slot.Slot == null) return;
            _undoStack.Push(slot);
            while (_undoStack.Count > MaxUndoDepth) _undoStack.Pop();
        }

        private void ShowDeleteBalloon()
        {
            try
            {
                notifyIcon1.BalloonTipTitle = "已删除图标";
                notifyIcon1.BalloonTipText = "按 Ctrl+Z 可撤销删除";
                notifyIcon1.ShowBalloonTip(2500);
            }
            catch { }
        }

        private void UndoDelete()
        {
            if (_undoStack.Count == 0) return;
            DeletedSlot slot = _undoStack.Pop();
            if (slot == null || slot.Slot == null || slot.Slot.IsDisposed) return;
            if (slot.Data == null || string.IsNullOrEmpty(slot.Data.FilePath)) return;
            try
            {
                _store.SetSlot(_currentPageName, slot.Slot.SlotIndex,
                    new SlotData(slot.Slot.SlotIndex) { FilePath = slot.Data.FilePath, IsFolder = slot.Data.IsFolder });
                RenderSlotFromStore(slot.Slot);
                SaveConfig();
                Log("撤销删除: " + slot.Data.FilePath);
            }
            catch (Exception ex)
            {
                Log("撤销删除失败: " + ex);
            }
        }

        // 重命名后把按页名跟踪的两个缓存集合的键同步为新名（位图仍有效，无需重渲染）
        private void RekeyPageCache(string oldName, string newName)
        {
            if (_pageCacheLoaded.Contains(oldName)) { _pageCacheLoaded.Remove(oldName); _pageCacheLoaded.Add(newName); }
            int ti = _pageTouchOrder.IndexOf(oldName);
            if (ti >= 0) _pageTouchOrder[ti] = newName;
        }

        // ===== 页面缓存（懒加载 + LRU）=====
        // 只渲染当前页 + 切换/搜索按需加载；超出 PageCacheLimit 释放最久未用页的位图（搜索期间不驱逐）
        // ===== Everything 全盘搜索（借鉴亦安 everything_search.rs）=====
        // 本地图标匹配是即时同步的；这里再异步去问 Everything，把磁盘上存在但没建图标的文件也搜出来，
        // 追加在本地结果后面。查询与图标渲染都放后台 STA 线程：SDK 查询会阻塞，
        // 图标提取要走 WScript.Shell COM（要求 STA）。
        private void StartEverythingSearch(string q)
        {
            if (_store == null || !_store.Layout.EverythingSearchEffective) return;
            if (string.IsNullOrWhiteSpace(q)) return;

            int gen = ++_everythingGen;
            int iconSize = _store.Layout.IconSize;
            var palette = _palette;

            var t = new System.Threading.Thread(() =>
            {
                var datas = new List<SlotData>();
                string err = null;
                try
                {
                    List<EverythingHit> hits;
                    if (EverythingSearch.Search(q, MaxExternalResults, out hits, out err) && hits != null)
                    {
                        foreach (var h in hits)
                        {
                            if (gen != _everythingGen) break;   // 用户已改词/已退出搜索，不必再渲染图标
                            Image img = null;
                            try { img = IconRenderer.RenderToBitmap(iconSize, iconSize, h.Path, palette); }
                            catch { }
                            var d = new SlotData(0) { FilePath = h.Path, IsFolder = h.IsFolder };
                            d.CachedImage = img;
                            datas.Add(d);
                        }
                    }
                }
                catch (Exception ex) { err = ex.Message; }

                try { BeginInvoke(new Action(() => ApplyEverythingResults(gen, q, datas, err))); }
                catch { }
            });
            t.IsBackground = true;
            t.SetApartmentState(System.Threading.ApartmentState.STA);
            t.Start();
        }

        private void ApplyEverythingResults(int gen, string q, List<SlotData> datas, string err)
        {
            if (!string.IsNullOrEmpty(err)) Log("Everything 全盘搜索: " + err);

            // 结果过期（查询词已换 / 已退出搜索）→ 丢弃并释放刚渲染的位图，
            // 否则每搜一次就漏一批 GDI 对象。
            bool stale = gen != _everythingGen
                || searchBox == null
                || string.IsNullOrWhiteSpace(searchBox.Text)
                || searchBox.Text != q;
            if (stale)
            {
                foreach (var d in datas) d.DisposeImage();
                return;
            }
            if (datas.Count == 0) return;
            _search.AppendExternal(datas);
        }

        private void EnsurePageLoaded(string pageName)
        {
            if (!_pageCacheLoaded.Contains(pageName))
            {
                LoadPageToCache(pageName);
                _pageCacheLoaded.Add(pageName);
            }
            TouchPage(pageName);
            EnforcePageCacheLimit();
        }

        private void TouchPage(string pageName)
        {
            _pageTouchOrder.Remove(pageName);
            _pageTouchOrder.Add(pageName); // 末尾=最近使用
        }

        // 搜索合并视图跨页使用各页 CachedImage，驱逐会使其变空，故搜索进行中暂停 LRU 清理
        private void EnforcePageCacheLimit()
        {
            if (_search.IsSearching) return;
            if (_suppressPageEviction) return;
            while (_pageTouchOrder.Count > PageCacheLimit)
            {
                string oldest = _pageTouchOrder[0];
                _pageTouchOrder.RemoveAt(0);
                ReleasePageCache(oldest);
            }
        }

        private void ReleasePageCache(string pageName)
        {
            if (!_pageCacheLoaded.Contains(pageName)) return;
            var list = _store.GetPage(pageName);
            if (list != null)
                foreach (var d in list) d?.DisposeImage();
            _pageCacheLoaded.Remove(pageName);
        }

        // 主题切换时只释放 CachedImage（含旧配色文字），保留 RawIcon（纯图标，主题无关）
        private void InvalidateAllPageCaches()
        {
            foreach (var name in new List<string>(_pageCacheLoaded))
            {
                var list = _store.GetPage(name);
                if (list != null)
                    foreach (var d in list)
                        if (d != null) { d.CachedImage?.Dispose(); d.CachedImage = null; }
            }
            _pageTouchOrder.Clear();
        }

        // 异步渲染指定页的图标：先在 UI 线程同步填「已有位图」（切换回渲染过的页时几乎瞬时显示），
        // 剩下的缺失位图交给后台 STA 线程逐张渲染，每张渲染完 BeginInvoke 回 UI 线程赋值。
        // 切页 / 重载时 _renderGeneration 递增，旧线程在下一张前自行退出，不会与新页抢渲染资源。
        private void StartInitialPageRender(string pageName)
        {
            var list = _store.GetPage(pageName);
            if (list == null) { EnsurePageLoaded(pageName); LoadPageFromCache(pageName); return; }

            // ── 一、同步填充已有位图（不渲染，纯引用赋值，开销可忽略）
            for (int i = 0; i < list.Count && i < _slots.Count; i++)
            {
                var d = list[i];
                if (d != null && !d.IsEmpty && d.CachedImage != null)
                {
                    _slots[i].Data = d;
                    _slots[i].Image = d.CachedImage;
                }
            }

            // ── 二、收集仍缺位图的槽位
            var pending = new List<int>();
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && !list[i].IsEmpty && list[i].CachedImage == null) pending.Add(i);

            int gen = ++_renderGeneration;   // 作废在跑的上一轮渲染
            if (pending.Count == 0) { MarkPageRendered(pageName); return; }

            int iconSize = _store.Layout.IconSize;
            var palette = _palette;
            var thread = new System.Threading.Thread(() => RenderPageWorker(list, pending, pageName, gen, iconSize, palette));
            thread.IsBackground = true;
            // WScript.Shell（解析 .lnk 目标）要求 STA；ExtractAssociatedIcon 在 STA 下也最稳
            try { thread.SetApartmentState(System.Threading.ApartmentState.STA); } catch { }
            thread.Start();
        }

        // 后台渲染线程体：逐张渲染后投递回 UI 线程。代数不匹配（已切页 / 已重载）即提前退出。
        private void RenderPageWorker(List<SlotData> list, List<int> pending, string pageName, int gen, int iconSize, ThemePalette palette)
        {
            foreach (int idx in pending)
            {
                if (gen != _renderGeneration) return;
                var data = list[idx];
                if (data == null || data.IsEmpty) continue;
                try
                {
                    if (data.RawIcon == null)
                        data.RawIcon = IconRenderer.RenderIconOnly(iconSize, iconSize, data.FilePath, data.IconPath);
                    Image composed = IconRenderer.ComposeBitmap(data.RawIcon, iconSize, iconSize, data.FilePath, palette, data.DisplayName);
                    int i = idx;
                    try
                    {
                        this.BeginInvoke(new Action(() =>
                        {
                            // 这期间可能已切页/重载：位图仍挂到 data 上（切回时立即可用），只是不再贴到屏幕
                            if (gen != _renderGeneration) { composed.Dispose(); return; }
                            data.CachedImage?.Dispose();
                            data.CachedImage = composed;
                            if (_currentPageName == pageName && i < _slots.Count && !_slots[i].IsDisposed)
                            {
                                _slots[i].Data = data;
                                _slots[i].Image = composed;
                            }
                        }));
                    }
                    catch { composed.Dispose(); }   // 窗体已关闭，BeginInvoke 失败
                }
                catch (Exception ex) { Log("渲染图标失败: " + ex.Message); }
            }

            // 全部完成：回 UI 线程登记本页缓存（纳入 LRU），供 EnforcePageCacheLimit 管理
            try
            {
                this.BeginInvoke(new Action(() =>
                {
                    if (gen != _renderGeneration) return;
                    MarkPageRendered(pageName);
                }));
            }
            catch { }
        }

        // 标记某页已完成渲染（纳入 LRU 缓存集合）
        private void MarkPageRendered(string pageName)
        {
            if (!_pageCacheLoaded.Contains(pageName)) _pageCacheLoaded.Add(pageName);
            TouchPage(pageName);
            EnforcePageCacheLimit();
        }

        private void LoadPageToCache(string pageName)
        {
            if (!_store.Pages.ContainsKey(pageName)) return;
            var list = _store.Pages[pageName];
            foreach (var data in list)
            {
                if (data == null || data.IsEmpty) { data?.DisposeImage(); continue; }
                // 渲染纯图标（主题无关）+ 合成最终位图（含主题色文字）。
                // 路径失效（如导入的配置引用不存在的文件）也渲染：GetFileIcon 退回系统默认图标，避免图标空白但可点击。
                if (data.RawIcon == null)
                    data.RawIcon = IconRenderer.RenderIconOnly(_store.Layout.IconSize, _store.Layout.IconSize, data.FilePath, data.IconPath);
                data.CachedImage?.Dispose();
                data.CachedImage = IconRenderer.ComposeBitmap(data.RawIcon, _store.Layout.IconSize, _store.Layout.IconSize, data.FilePath, _palette);
            }
        }

        private void LoadPageFromCache(string pageName)
        {
            var list = _store.GetPage(pageName);
            if (list == null) return;
            for (int i = 0; i < _slots.Count && i < list.Count; i++)
            {
                var data = list[i];
                _slots[i].Data = data ?? new SlotData(i);
                // 不复制旧槽位的 RawIcon：store 的 SlotData 在 LoadPageToCache 时已持有
                // 与自身 FilePath 匹配的 RawIcon；复制旧引用会导致导入配置后图标张冠李戴。
                _slots[i].Image = _slots[i].Data?.CachedImage;
            }
        }

        private void clearImageBoxes()
        {
            foreach (var slot in _slots)
            {
                slot.Image = null;
                slot.Data = new SlotData(slot.SlotIndex);
            }
        }

        // 切换到第 index 页（0 基）
        private void SwitchToPage(int index)
        {
            if (index < 0 || index >= _store.PageNames.Count) return;
            _pageIndicator.SelectedIndex = index;
            _currentPageName = _store.PageNames[index];
            clearImageBoxes();
            if (_pageCacheLoaded.Contains(_currentPageName))
            {
                // 已缓存：直接显示（TouchPage 维持 LRU 顺序）
                TouchPage(_currentPageName);
                EnforcePageCacheLimit();
                LoadPageFromCache(_currentPageName);
            }
            else
            {
                // 未缓存：分批渲染，避免一次性渲染整页图标阻塞消息循环导致鼠标卡顿
                StartInitialPageRender(_currentPageName);
            }
            // 切页后保持搜索状态一致：聚焦时按当前查询重过滤，否则恢复全部可见
            if (searchBox != null && searchBox.Focused)
                _search.ApplyFilter(searchBox.Text == SearchCue ? "" : searchBox.Text);
            else
                _search.ShowAll();
        }

        // 用户开始拖拽：记住起始整块尺寸，用于判断这次操作到底改了尺寸没有。
        // WM_ENTERSIZEMOVE/WM_EXITSIZEMOVE 对「移动窗口」和「缩放窗口」都会成对发出来，
        // 所以 OnResizeEnd 里必须先分辨这次是哪种 —— 见下面的 early return。
        protected override void OnResizeBegin(EventArgs e)
        {
            base.OnResizeBegin(e);
            _userResizing = true;
            _resizeStartSize = ClientSize;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // 缩放过程中（含程序启动设置 ClientSize）同步重排控件，避免元素错位
            try { LayoutChrome(true); }
            catch (Exception ex) { Log("重排布局异常: " + ex.Message); }
            // 走 Region 裁剪时圆角需随窗口尺寸重算，否则缩放后圆角会丢
            ApplyRoundCorners();
        }

        // 拖拽结束：把用户调整后的尺寸写回配置，下次启动保持。
        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            try
            {
                var now = ClientSize;
                bool sizeChanged = now != _resizeStartSize;
                bool widthChanged = now.Width != _resizeStartSize.Width;
                bool heightChanged = now.Height != _resizeStartSize.Height;
                _userResizing = false;

                // ⚠️ 纯移动窗口 → 立刻收工，不重排、不自动改尺寸。
                // 这是「窄窗调好尺寸后一拖窗口就弹回原尺寸」的根因：旧的 OnResizeEnd 不区分
                // 移动与缩放，移动末尾也会走 LayoutChrome()，里面的 ApplyAutoHeight 会把
                // 用户刚刚压下去的高度重新撑回"能放下全部图标"的高度 —— 用户眼里就是
                // 「我改的尺寸被撤销了」。移动窗口不该触碰任何尺寸。
                if (!sizeChanged) return;

                // 用户动过高度 → 之后自动高度只撑高、不主动回缩（不擅自改用户定的尺寸）
                if (heightChanged) _heightFromUser = true;

                if (_minGridSize.Width > 0 && _minGridSize.Height > 0 &&
                    (_store.Layout.Width != now.Width || _store.Layout.Height != now.Height))
                {
                    _store.Layout.Width = now.Width;
                    _store.Layout.Height = now.Height;
                    SaveConfig();
                    Log($"窗口尺寸已调整并保存: {now.Width}×{now.Height}");
                }

                // 只有**宽度**变了才需要重新考虑"高度够不够"（列数变 → 行数变）。
                // 用户单纯把窗口拖矮/拖高时不走自动高度 —— 那等于程序跟用户对着干。
                LayoutChrome(widthChanged);
            }
            catch (Exception ex) { Log("保存窗口尺寸失败: " + ex.Message); }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_realExit)
            {
                e.Cancel = true;
                SaveConfig();
                _notesWindow?.SaveNotesNow();   // 便签在独立窗口里，落盘当前内容（窗口本身保持打开）
                this.Hide();
                return;
            }

            base.OnFormClosing(e);
            UnregisterGlobalHotkeys();
            _configWatcher?.Dispose();
            _edgeHide?.Dispose();
            _desktopMouseHook.Uninstall();
            _renderGeneration++;   // 作废在跑的后台渲染线程，令其尽快退出
            SaveConfig();
            _notesWindow?.SaveNotesNow();

            foreach (var list in _store.Pages.Values)
                foreach (var d in list) d?.DisposeImage();
        }

        private void exitbutton_Click(object sender, EventArgs e)
        {
            this.Hide();
        }

        private void Launcher_Load(object sender, EventArgs e) { }

        private void notifyIcon1_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                ToggleMainWindow();
            // 右键由 notifyIcon1.ContextMenuStrip 自动弹出，无需手动处理。
        }

        // 失焦自动隐藏：点击窗口外（焦点离开主面板）时隐藏，符合 dock 启动器习惯
        private void Launcher_Deactivate(object sender, EventArgs e)
        {
            if (this.InvokeRequired) { this.Invoke(new Action(() => Launcher_Deactivate(sender, e))); return; }
            // 设置面板等模态子窗打开期间不隐藏（否则调不透明度时看不到主面板预览）
            if (_suppressAutoHide) return;
            // 贴边缩进中必须豁免：缩进去只露一条边，那条边就是唤出入口，
            // 在这里把它 Hide() 掉等于把门焊死 —— 鼠标再没有东西可停靠，功能直接废掉。
            if (_edgeHide != null && _edgeHide.KeepVisible) return;
            // 置顶(钉住)模式不隐藏；autoHide 关闭时不隐藏
            if (this.TopMost) return;
            if (!_store.Layout.AutoHideEffective) return;
            this.Hide();
        }

        // 贴边隐藏的计时器只在窗口可见时跑：窗口藏起来后没有可交互对象，60fps 空转纯属浪费。
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (_edgeHide == null) return;
            if (Visible) _edgeHide.Start(); else _edgeHide.Pause();
        }

        // 设置面板改过「贴边自动隐藏」后重新生效；关掉时要先把窗口从屏幕边弹回来。
        private void ApplyEdgeAutoHide()
        {
            if (_edgeHide == null) return;
            _edgeHide.Enabled = _store.Layout.EdgeAutoHideEffective;
            if (!_edgeHide.Enabled) _edgeHide.Undock();
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _realExit = true;
            Application.Exit();
        }

        // 小火箭：打开程序所在目录并选中本体
        private void btnOpenFolder_Click(object sender, EventArgs e)
        {
            string exePath = Application.ExecutablePath;
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{exePath}\"");
        }

        private void btnPinTop_Click(object sender, EventArgs e)
        {
            this.TopMost = !this.TopMost;
            if (btnPinTop != null) btnPinTop.Active = this.TopMost;
        }

        // 便签按钮：开合独立便签窗口（与 Ctrl+Shift+N 同一入口）
        private void btnNotes_Click(object sender, EventArgs e) => ToggleNotesWindow();

        // 标题栏🌓按钮：切换深浅色皮肤
        private void btnTheme_Click(object sender, EventArgs e) => ToggleTheme();

        // 托盘菜单「设置…」：打开统一设置面板
        private void settingsToolStripMenuItem_Click(object sender, EventArgs e) => ShowSettings();

        // 托盘菜单「显示/隐藏面板」：与左键单击托盘图标同义，给不用热键的场景一个显式入口
        private void trayToggleToolStripMenuItem_Click(object sender, EventArgs e) => ToggleMainWindow();

        // 用系统默认编辑器打开 launcher.json（设置面板「数据」页也会调用）
        private void OpenConfigFileAction()
        {
            try
            {
                string path = _store.JsonFilePath;
                if (!File.Exists(path)) SaveConfig(); // 确保文件存在
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                Log("已打开配置文件：" + path);
            }
            catch (Exception ex)
            {
                Log("打开配置文件失败：" + ex.Message);
                MessageBox.Show("打开配置文件失败：" + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ===== 统一设置面板 =====
        // 打开设置面板：临时挂起「失焦自动隐藏」，避免模态子窗一弹就把主面板藏掉
        // （否则调不透明度时看不到主面板实时预览）。
        private void ShowSettings()
        {
            // 布局快照：用于判断关闭后是否需要重建槽位 / 圆点
            int oldSlotCount = _store.Layout.SlotCount;
            int oldIconSize = _store.Layout.IconSize;
            int oldColumns = _store.Layout.Columns;
            int oldWidth = _store.Layout.Width;
            int oldHeight = _store.Layout.Height;
            int oldPageCount = _store.Layout.PageCount;
            ThemeMode oldTheme = _store.Theme;

            var actions = new SettingsActions
            {
                OpenConfigFile = OpenConfigFileAction,
                ExportConfig = ExportConfigAction,
                ImportConfig = ImportConfigAction,
                ClearIconCache = ClearIconCacheAndReload,
                AddPage = DoAddPage,
                RenamePage = idx => RenamePage(idx),
                RemovePage = idx => DoRemovePage(idx),
                PreviewOpacity = pct => { this.Opacity = (pct >= 100) ? 1.0 : Math.Max(0.30, pct / 100.0); }
            };

            DialogResult result;
            _suppressAutoHide = true;
            try
            {
                using (var dlg = new SettingsForm(_store, _palette, actions))
                    result = dlg.ShowDialog(this);
            }
            finally { _suppressAutoHide = false; }

            // 面板只在点「确定」时由 ApplyToStore() 写回 _store，所以「取消」无需回滚；
            // 这里只需落盘一次，并在 ApplyAllSettings 里把不透明度实时预览还原。
            if (result == DialogResult.OK) SaveConfig();

            ApplyAllSettings(oldSlotCount, oldIconSize, oldColumns, oldWidth, oldHeight, oldPageCount, oldTheme);

            try { this.Show(); this.Activate(); } catch { }
        }

        // 统一重新应用设置：主题 / 不透明度 / 热键 /（必要时）重建槽位与分页圆点 / 重渲染
        private void ApplyAllSettings(int oldSlotCount, int oldIconSize, int oldColumns,
            int oldWidth, int oldHeight, int oldPageCount, ThemeMode oldTheme)
        {
            bool layoutChanged = _store.Layout.SlotCount != oldSlotCount
                || _store.Layout.IconSize != oldIconSize
                || _store.Layout.Columns != oldColumns
                || _store.Layout.Width != oldWidth
                || _store.Layout.Height != oldHeight
                || _store.Layout.PageCount != oldPageCount;
            bool themeChanged = _store.Theme != oldTheme;

            SetPalette(_store.Theme);
            ApplyOpacity();
            ApplyThemeVisuals();
            RegisterGlobalHotkeys();
            ApplyEdgeAutoHide();   // 设置面板改过「贴边自动隐藏」→ 重新生效（关掉时把窗口弹回工作区）

            if (layoutChanged)
            {
                // 槽位与圆点都是新建控件：先把旧的移除并释放，避免叠加与句柄泄漏
                foreach (var s in _slots)
                {
                    if (!s.IsDisposed) { this.Controls.Remove(s); s.Dispose(); }
                }
                _slots.Clear();
                if (_pageIndicator != null)
                {
                    this.Controls.Remove(_pageIndicator);
                    _pageIndicator.Dispose();
                    _pageIndicator = null;
                }

                BuildSlots();
                BuildPageDots();

                // SearchController 持有 _slots / _pageIndicator 引用，控件重建后必须一并重建
                _search = new SearchController(_slots, _pageIndicator, _store,
                    () => _currentPageName,
                    EnsurePageLoaded,
                    v => _suppressPageEviction = v,
                    () => _palette);

                // 图标尺寸变了旧位图不再匹配：全部作废，后台重渲染
                _renderGeneration++;
                _pageCacheLoaded.Clear();
                _pageTouchOrder.Clear();
                foreach (var list in _store.Pages.Values)
                    foreach (var d in list) d?.DisposeImage();

                if (_store.PageNames.IndexOf(_currentPageName) < 0)
                    _currentPageName = _store.PageNames.Count > 0 ? _store.PageNames[0] : "第1页";
                RefreshPageIndicator();
                StartInitialPageRender(_currentPageName);
            }
            else if (themeChanged)
            {
                RebuildAllIcons();
                RefreshPageIndicator();
            }
            else
            {
                RefreshPageIndicator();
            }

            Invalidate();
            Log("设置已应用（布局" + (layoutChanged ? "有" : "无") + "变化 / 主题" + (themeChanged ? "有" : "无") + "变化）");
        }

        // 清除图标磁盘缓存 + 作废内存位图，随后重渲染当前页（设置面板「数据」页）
        private void ClearIconCacheAndReload()
        {
            int n = IconRenderer.ClearIconCache();
            if (n < 0)
            {
                MessageBox.Show("清除图标缓存失败（文件可能正被占用）。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _renderGeneration++;      // 停掉在跑的渲染
            _pageCacheLoaded.Clear();
            _pageTouchOrder.Clear();
            foreach (var list in _store.Pages.Values)
                foreach (var d in list) d?.DisposeImage();
            clearImageBoxes();
            StartInitialPageRender(_currentPageName);

            MessageBox.Show(n > 0 ? ("已清除 " + n + " 个图标缓存文件。") : "图标缓存本来就是空的。",
                "清除完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Log("已清除图标缓存：" + n + " 个文件");
        }

        // 导出配置（launcher.json + notes.txt + launcher.ini 打包为 zip）
        // 入口已从托盘菜单移到设置面板「数据」页，方法体保持不变。
        private void ExportConfigAction()
        {
            try
            {
                SaveConfig();
                _notesWindow?.SaveNotesNow();   // 先把便签落盘，再打包
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var sources = ConfigExporter.CollectConfigFiles(baseDir); // 收集逻辑已抽到 ConfigExporter，可单测
                if (sources.Count == 0)
                {
                    MessageBox.Show("当前没有可导出的配置（尚未生成 launcher.json）。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                using (var dlg = new SaveFileDialog())
                {
                    dlg.Filter = "配置备份 (*.zip)|*.zip";
                    dlg.FileName = "launcher-config-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".zip";
                    if (dlg.ShowDialog() != DialogResult.OK) return;
                    if (File.Exists(dlg.FileName)) File.Delete(dlg.FileName);
                    using (var zip = ZipFile.Open(dlg.FileName, ZipArchiveMode.Create))
                    {
                        foreach (var f in sources) zip.CreateEntryFromFile(f, Path.GetFileName(f));
                        // 多便签：notes/ 目录下所有便签一并打包
                        foreach (var (src, entry) in ConfigExporter.CollectNoteEntries(baseDir))
                            zip.CreateEntryFromFile(src, entry);
                        // 把各槽位引用的自定义图标一并打包，换机不丢图（去重逻辑已抽到 ConfigExporter）
                        foreach (var (src, entry) in ConfigExporter.CollectIconEntries(_store))
                            zip.CreateEntryFromFile(src, entry);
                    }
                    Log("已导出配置备份：" + dlg.FileName);
                    MessageBox.Show("配置已导出到：\n" + dlg.FileName, "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex) { Log("导出配置失败：" + ex.Message); MessageBox.Show("导出失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        // 导入配置（从 zip 还原并热重载）。入口已从托盘菜单移到设置面板「数据」页。
        private void ImportConfigAction()
        {
            try
            {
                using (var dlg = new OpenFileDialog())
                {
                    dlg.Filter = "配置备份 (*.zip)|*.zip";
                    if (dlg.ShowDialog() != DialogResult.OK) return;
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    // 导入前自动备份当前配置，防导入出错（如 BOM 损坏、格式不兼容）后无法回滚
                    try
                    {
                        string bkDir = Path.Combine(baseDir, "backups");
                        Directory.CreateDirectory(bkDir);
                        var cur = new[] { "launcher.json", "notes.txt", "launcher.ini" }
                            .Select(f => Path.Combine(baseDir, f))
                            .Where(File.Exists).ToArray();
                        if (cur.Length > 0)
                        {
                            string bk = Path.Combine(bkDir, "launcher-config-auto-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip");
                            using (var z = ZipFile.Open(bk, ZipArchiveMode.Create))
                                foreach (var f in cur) z.CreateEntryFromFile(f, Path.GetFileName(f));
                            Log("导入前已自动备份当前配置：" + bk);
                        }
                    }
                    catch (Exception ex) { Log("导入前自动备份失败（已忽略）：" + ex.Message); }
                    _configWatcher.SkipNext = true; // 导入直接写盘，避免触发文件监视器的二次重载
                    try
                    {
                        using (var zip = ZipFile.OpenRead(dlg.FileName))
                        {
                            foreach (var entry in zip.Entries)
                            {
                                if (string.IsNullOrEmpty(entry.Name)) continue; // 纯目录项跳过
                                string dest = Path.Combine(baseDir, entry.FullName);
                                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                                entry.ExtractToFile(dest, true);
                            }
                        }
                    }
                    finally { _configWatcher.SkipNext = false; }
                    _store.Load();
                    if (_store.PageNames.IndexOf(_currentPageName) < 0)
                        _currentPageName = _store.PageNames.Count > 0 ? _store.PageNames[0] : "第1页";
                    try { RegisterGlobalHotkeys(); }    // 配置可能含新的快捷键绑定，同步刷新；失败也不影响重绘
                    catch (Exception ex) { Log("导入后刷新快捷键失败（已忽略）：" + ex.Message); }
                    InvalidateAllPageCaches();           // 导入后旧缓存作废
                    EnsurePageLoaded(_currentPageName);   // 懒加载当前页
                    LoadPageFromCache(_currentPageName); // 关键：把导入后的数据/图标刷到屏幕上的槽位
                    ApplyThemeVisuals();
                    Invalidate();
                    Log("已从备份导入配置：" + dlg.FileName);
                    MessageBox.Show("配置已导入并重新加载。\n原配置已自动备份到 backups/ 目录，可随时回滚。", "导入成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex) { Log("导入配置失败：" + ex.Message); MessageBox.Show("导入失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        // 新增一页，返回新页索引（<0 表示失败）；设置面板「页面」页调用
        private int DoAddPage()
        {
            int idx = _store.AddPage();
            if (idx < 0) return -1;
            SaveConfig();   // 内部已用 _skipNextReload 包裹，避免写盘触发文件监视器二次重载
            RefreshPageIndicator();
            SwitchToPage(idx);
            Log("新增一页（共 " + _store.Layout.PageCount + " 页）");
            return idx;
        }

        // 删除第 index 页（含页内非空时的二次确认）；设置面板「页面」页调用
        private void DoRemovePage(int idx)
        {
            if (_store.Layout.PageCount <= 1)
            {
                MessageBox.Show("只剩最后一页了，无法删除。", "无法删除", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (idx < 0 || idx >= _store.PageNames.Count) return;

            string pageName = _store.PageNames[idx];

            // 页内还有图标时二次确认，避免误删
            if (!_store.IsPageEmpty(pageName))
            {
                var r = MessageBox.Show(
                    "第 " + (idx + 1) + " 页还有图标，删除后这一页的内容将丢失（后面的页会依次前移）。\n\n确定要删除吗？",
                    "删除当前页", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (r != DialogResult.Yes) return;
            }

            var removed = _store.RemovePage(idx);
            if (removed == null) return;
            foreach (var s in removed) s?.DisposeImage();   // 释放被删页的图标位图

            SaveConfig();   // 内部已用 _skipNextReload 包裹，避免写盘触发文件监视器二次重载
            RefreshPageIndicator();
            SwitchToPage(Math.Min(idx, _store.PageNames.Count - 1));   // 停在原位；删的是末页则退到新末页
            Invalidate();
            Log("删除第 " + (idx + 1) + " 页（剩余 " + _store.Layout.PageCount + " 页）");
        }

        private void Launcher_KeyDown(object sender, KeyEventArgs e)
        {
            // 搜索 / 普通两种模式的按键路由统一由 KeyRouter 判定（纯逻辑，可单测）
            var route = KeyRouter.Resolve(_store.Hotkeys, searchBox.Focused, e.Control, e.Shift, e.Alt, e.KeyCode);
            if (!route.Handled) return;
            e.Handled = true; e.SuppressKeyPress = true;

            switch (route.Action)
            {
                case KeyAction.UndoDelete: UndoDelete(); return;
                case KeyAction.PrevPage:
                    { int cur = _store.PageNames.IndexOf(_currentPageName); if (cur > 0) SwitchToPage(cur - 1); return; }
                case KeyAction.NextPage:
                    { int cur = _store.PageNames.IndexOf(_currentPageName); if (cur < _store.PageNames.Count - 1) SwitchToPage(cur + 1); return; }
                case KeyAction.OpenSlotByIndex: OpenSlotByIndex(route.SlotIndex); return;
                case KeyAction.OpenSlotByLetter: OpenSlotByFirstLetter(route.Letter); return;
                case KeyAction.JumpPage:
                    if (route.JumpIndex < _store.PageNames.Count) SwitchToPage(route.JumpIndex); return;

                // 搜索模式
                case KeyAction.SearchOpenSelected:
                    if (_search.IsSearching)
                        OpenSlot(_search.GetSelected());
                    return;
                case KeyAction.SearchMoveUp:
                    if (_search.IsSearching)
                    { _search.MoveUp(); }
                    return;
                case KeyAction.SearchMoveDown:
                    if (_search.IsSearching)
                    { _search.MoveDown(); }
                    return;
                case KeyAction.SearchClear:
                    // Esc：清空并收起搜索框（回到图标态，宽度还给图标网格）
                    CollapseSearchBar();
                    return;
                case KeyAction.SearchOpenNth:
                    if (_search.IsSearching && route.SlotIndex < _search.Count) OpenSlot(_search.GetNth(route.SlotIndex));
                    return;
            }
        }


        // ===== 键盘打开槽位 =====
        private void OpenSlotByIndex(int n)
        {
            if (n < 0 || n >= _slots.Count) return;
            var slot = _slots[n];
            if (slot.Data != null && !slot.Data.IsEmpty) OpenSlot(slot);
        }

        private void OpenSlotByFirstLetter(char c)
        {
            // 匹配逻辑已抽到 SearchEngine.FindFirstByLetter（可单测）；这里只做"打开"动作
            var list = _store.GetPage(_currentPageName);
            if (list == null) return;
            int idx = SearchEngine.FindFirstByLetter(list, c);
            if (idx >= 0 && idx < _slots.Count && _slots[idx].Data != null && !_slots[idx].Data.IsEmpty)
                OpenSlot(_slots[idx]);
        }

        // ===== 搜索框「图标展开 / 收起」=====
        // 收起态：标题栏只有一个放大镜图标，宽度全给图标网格；
        // 展开态：输入框从放大镜**原位**向右铺开，直到右侧按钮组左边缘；
        //         品牌名与按钮组的位置都不受影响（详见 LayoutTitleBar）。
        // 收起时机：Esc、空框失焦、或窗口窄到放不下输入框。
        private void ToggleSearchBar()
        {
            if (_searchBarOpen) CollapseSearchBar();
            else ExpandSearchBar();
        }

        private void ExpandSearchBar()
        {
            if (searchBar == null || _searchBarOpen) return;
            _searchBarOpen = true;
            LayoutChrome();
            // 宽度不足时 LayoutTitleBar 会把 _searchBarOpen 打回 false 并保持放大镜可见。
            // 那种情况下到此为止 —— 不聚焦、不报错，用户看到的就是"点了没变化"（因为本来就展不开）。
            if (!_searchBarOpen) return;
            searchBar.FocusInner();   // 触发 SearchBox_Enter → 自动清掉占位符
        }

        private void CollapseSearchBar()
        {
            if (searchBar == null) return;
            bool wasOpen = _searchBarOpen;
            _searchBarOpen = false;
            searchBar.Visible = false;
            // 收起即结束搜索：否则框没了但网格还停在筛选结果上，看起来像"图标丢了"
            if (searchBox.Text != SearchCue)
            {
                searchBox.Text = SearchCue;          // TextChanged → _search.ShowAll()
                searchBox.ForeColor = _palette.SearchCue;
            }
            // 本来就没展开就不必重排（LayoutTitleBar 里回退收起时会走到这里）
            if (wasOpen) LayoutChrome();
        }

        // 搜索框事件
        private void SearchBox_Enter(object sender, EventArgs e)
        {
            if (searchBox.Text == SearchCue)
            {
                searchBox.Text = "";
                searchBox.ForeColor = _palette.TextFore;
            }
            else
            {
                searchBox.SelectAll(); // 选中全部文字，方便直接替换
            }
        }

        private void SearchBox_Leave(object sender, EventArgs e)
        {
            if (searchBox.Text == SearchCue) return; // 已经是占位符，不做任何事
            // 有内容 → 保持搜索态（搜索框继续显示）；空框 → 清占位符并收起图标态。
            // 用 BeginInvoke 延后一轮：点搜索按钮时 Leave 先于 Click 触发，
            // 若同步收起会被随后的 Click 又展开（视觉上闪一下）。
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed || searchBox == null || searchBox.IsDisposed) return;
                if (!_searchBarOpen) return;
                if (searchBar != null && searchBar.ContainsFocus) return;
                if (!string.IsNullOrWhiteSpace(searchBox.Text) && searchBox.Text != SearchCue) return;
                CollapseSearchBar();
            }));
        }

        private void SearchBox_TextChanged(object sender, EventArgs e)
        {
            if (searchBox.Text == SearchCue) { _search.ShowAll(); return; }
            _search.ApplyFilter(searchBox.Text);
        }

        private void Form1_MouseWheel(object sender, MouseEventArgs e)
        {
            int cur = _store.PageNames.IndexOf(_currentPageName);
            if (e.Delta > 0)
            {
                if (cur > 0) SwitchToPage(cur - 1);
            }
            else
            {
                if (cur < _store.PageNames.Count - 1) SwitchToPage(cur + 1);
            }
        }
    }
}
