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

        // ===== 搜索 / 键盘打开 =====
        private SearchController _search;
        private const string SearchCue = "搜索…";
        private bool _suppressPageEviction = false; // 搜索合并视图正在跨页取图标，期间暂停 LRU 驱逐

        // ===== 页面懒加载缓存（只渲染当前页 + 切换/搜索按需加载，LRU 上限防内存膨胀）=====
        private readonly HashSet<string> _pageCacheLoaded = new HashSet<string>(); // 已渲染过缓存的页名
        private readonly List<string> _pageTouchOrder = new List<string>();        // 页名最近使用顺序（末尾=最新）
        private const int PageCacheLimit = 4; // 最多同时保留渲染缓存页数；超出时释放最久未用页（搜索期间不驱逐）

        // ===== 启动分批渲染（避免一次性渲染整页图标阻塞 UI 消息循环，导致启动慢/鼠标卡顿）=====
        private Timer _initialRenderTimer;
        private int _initialRenderCursor;
        private string _initialRenderPage;
        private const int InitialRenderBatchSize = 4;  // 每 tick 渲染几个图标
        private const int InitialRenderInterval = 5;   // tick 间隔（ms）

        // ===== 撤销删除（引用 IconSlot，不再持有裸 PictureBox）=====
        private class DeletedSlot
        {
            public IconSlot Slot;
            public SlotData Data;
        }
        private readonly Stack<DeletedSlot> _undoStack = new Stack<DeletedSlot>();
        private const int MaxUndoDepth = 50;
        private const int DragThreshold = 5;

        // ===== 便签持久化（多便签）：自动保存（防抖）=====
        private Timer _notesTimer;
        private bool _suppressNotesSave = false;
        private NotesStore _notes;                       // 多便签存储（notes/ 目录，每条一个 .txt）
        private readonly List<Button> _noteTabButtons = new List<Button>(); // 左边缘便签按钮
        private ToolTip _noteTips;                       // 便签按钮悬停提示（显示完整便签名）
        private string _notesPath => _notes != null
            ? Path.Combine(_notes.NotesDirectory, _notes.CurrentName + ".txt")
            : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "notes.txt"); // 兼容：_notes 未初始化时退回旧路径

        // ===== 动画定时器 =====
        private Timer _animationTimer;

        private ConfigWatcher _configWatcher;

        private bool _realExit = false; // 仅托盘菜单“退出”时置 true，点 × / Alt+F4 仅最小化到托盘

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
                else if (id == NativeMethods.HOTKEY_ID_NOTES) ToggleNotesPanel();
                else if (id == NativeMethods.HOTKEY_ID_THEME) ToggleTheme();
                return;
            }

            base.WndProc(ref message);

            if (message.Msg == NativeMethods.WM_NCHITTEST && (int)message.Result == NativeMethods.HTCLIENT)
            {
                // 边缘/角落返回对应缩放命中区（无边框窗口也可拖动调整大小），中心区返回标题区以便拖动移动
                Point p = PointToClient(Cursor.Position);
                int w = ClientSize.Width, h = ClientSize.Height;
                bool left = p.X <= NativeMethods.ResizeGrip, right = p.X >= w - NativeMethods.ResizeGrip;
                bool top = p.Y <= NativeMethods.ResizeGrip, bottom = p.Y >= h - NativeMethods.ResizeGrip;
                int hit = NativeMethods.HTCAPTION;
                if (top && left) hit = NativeMethods.HTTOPLEFT;
                else if (top && right) hit = NativeMethods.HTTOPRIGHT;
                else if (bottom && left) hit = NativeMethods.HTBOTTOMLEFT;
                else if (bottom && right) hit = NativeMethods.HTBOTTOMRIGHT;
                else if (left) hit = NativeMethods.HTLEFT;
                else if (right) hit = NativeMethods.HTRIGHT;
                else if (top) hit = NativeMethods.HTTOP;
                else if (bottom) hit = NativeMethods.HTBOTTOM;
                message.Result = (IntPtr)hit;
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
                this.Show();
                if (this.WindowState == FormWindowState.Minimized)
                    this.WindowState = FormWindowState.Normal;
                this.Activate();
                // 呼出时如果搜索框有内容，清除搜索恢复当前页（打开程序后再次呼出应显示原始页面）
                if (searchBox != null && !string.IsNullOrWhiteSpace(searchBox.Text) && searchBox.Text != SearchCue)
                {
                    searchBox.Text = SearchCue;
                    searchBox.ForeColor = _palette.SearchCue;
                    _search.ShowAll();
                }
                if (searchBox != null) searchBox.Focus();
            }
        }

        private void ToggleNotesPanel()
        {
            if (this.InvokeRequired) { this.Invoke(new Action(ToggleNotesPanel)); return; }
            if (!this.Visible)
            {
                this.Show();
                if (this.WindowState == FormWindowState.Minimized)
                    this.WindowState = FormWindowState.Normal;
                this.Activate();
            }
            btnNotes.PerformClick(); // 复用便签面板开关逻辑
        }

        // ===== 主题 =====
        private void SetPalette(ThemeMode mode)
        {
            _theme = mode;
            _palette = ThemePalette.For(mode);
        }

        // 仅重涂控件颜色（不重绘图标），用于启动初始化
        private void ApplyThemeVisuals()
        {
            this.BackColor = _palette.FormBack;
            panel1.BackColor = _palette.Sep;
            label2.ForeColor = _palette.LabelFore;
            searchBox.BackColor = _palette.TextBack;
            if (!searchBox.Focused) searchBox.ForeColor = _palette.SearchCue;
            textBox1.BackColor = _palette.TextBack;
            textBox1.ForeColor = _palette.TextFore;
            rtbNotesPreview.BackColor = _palette.TextBack;
            rtbNotesPreview.ForeColor = _palette.TextFore;
            btnMdToggle.BackColor = _palette.MenuBack;
            btnMdToggle.ForeColor = _notesPreviewing ? _palette.AccentPencil : Color.Silver;
            if (_notesPreviewing) RenderNotesPreview(); // 预览态换肤后重新渲染配色
            contextMenuStrip1.BackColor = _palette.MenuBack;
            contextMenuStrip1.ForeColor = _palette.MenuFore;
            exitToolStripMenuItem.BackColor = _palette.MenuBack;
            exitToolStripMenuItem.ForeColor = _palette.MenuFore;
            btnPinTop.ForeColor = this.TopMost ? _palette.AccentStick : Color.Silver;
            btnNotes.ForeColor = _palette.AccentPencil;
            HighlightCurrentNote(); // 便签列表栏随主题换肤

            RefreshPageIndicator();
            foreach (var slot in _slots) slot.ApplyTheme(_palette);
        }

        // 刷新分页指示器（页数 / 配色 / 页名 / 当前选中位），页面增删改名后统一调用
        private void RefreshPageIndicator()
        {
            if (_pageIndicator == null) return;
            _pageIndicator.Setup(_store.Layout.PageCount, _palette.DotActive, _palette.DotInactive);
            _pageIndicator.SetPageNames(_store.PageNames);
            _pageIndicator.SelectedIndex = _store.PageNames.IndexOf(_currentPageName);
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

        // ===== 动态生成控件 =====
        private void BuildSlots()
        {
            int gap = 12;
            int step = _store.Layout.IconSize + gap;
            int left = 15, top = 28;
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

            int formW = left + _store.Layout.Columns * step - gap + 19;
            int formH = top + rows * step - gap + 28;
            // 自定义窗口尺寸：用户拖拽调整后写回配置；0 则按列数×图标大小自动计算。
            // 不做硬下限——缩小后图标列数自适应减少（见 LayoutChrome），只会变多行、不会裁掉图标。
            // 仅保留一个很小的绝对下限（标题栏高度 + 一行），防止窗口被压到看不见。
            _minGridSize = new Size(formW, formH);
            int finalW = _store.Layout.Width > 0 ? _store.Layout.Width : formW;
            int finalH = _store.Layout.Height > 0 ? _store.Layout.Height : formH;
            this.MinimumSize = new Size(100, 48);
            this.ClientSize = new Size(finalW, finalH);
            LayoutChrome();
        }

        // 按当前窗口尺寸重排：图标网格自适应宽度（列数随可用宽度增减，所有图标始终可见），
        // 网格水平居中、顶部按钮/搜索框贴右、底部圆点居中
        private void LayoutChrome()
        {
            int gap = 12;
            int step = _store.Layout.IconSize + gap;
            // 自适应列数：窗口缩小时减少列数（行数增多），放大时增加列数；
            // 高度不足时底部图标随窗口裁掉（缩小即少看几行，拉高即恢复），图标永不横向挤压。
            int cols = Math.Max(1, (this.ClientSize.Width - 30 + gap) / step);
            int rows = (int)Math.Ceiling(_store.Layout.SlotCount / (double)cols);
            int totalW = cols * step - gap;
            int left = Math.Max(15, (this.ClientSize.Width - totalW) / 2);
            int top = 28;

            for (int i = 0; i < _slots.Count; i++)
            {
                int col = i % cols;
                int row = i / cols;
                _slots[i].Location = new Point(left + col * step, top + row * step);
            }

            // 右上角按钮组（默认 550 宽时的 x：theme=409 notes=467 pin=496 close=522）随右边缘对齐
            int rightMargin = this.ClientSize.Width - 28;
            btnClose.Left = rightMargin;
            btnPinTop.Left = rightMargin - 26;
            btnNotes.Left = rightMargin - 26 - 29;
            btnTheme.Left = rightMargin - 26 - 29 - 33;
            // 搜索框拉宽，右端贴到主题按钮左侧
            searchBox.Width = Math.Max(100, btnTheme.Left - searchBox.Left - 14);

            RepositionPageDots();
            RepositionNotesPanel();
        }

        private void RepositionPageDots()
        {
            if (_pageIndicator == null) return;
            int totalW = _store.Layout.PageCount * 12 + (_store.Layout.PageCount - 1) * 12;
            int startX = Math.Max(4, (this.ClientSize.Width - totalW) / 2);
            int y = this.ClientSize.Height - 26;
            _pageIndicator.Location = new Point(startX, y);
        }

        // 便签面板随窗口尺寸自适应：面板铺满内容区（标题栏以下），
        // 左边缘为便签列表栏（多便签切换），「预览」按钮贴在右上角、
        // 文本框/预览框占列表栏右侧的剩余区域——缩放后按钮不再跑到窗外
        private void RepositionNotesPanel()
        {
            if (panel2 == null || !panel2.Visible) return;
            panel2.Location = new Point(0, 24);
            int w = this.ClientSize.Width;
            int h = this.ClientSize.Height;
            panel2.Width = Math.Max(0, w);
            panel2.Height = Math.Max(0, h - 24);

            const int margin = 8;
            const int listW = 46;          // 左边缘便签列表栏宽度
            int btnW = btnMdToggle.Width;
            int top = 6;

            // 便签列表栏：贴左边缘，占满面板高度（内部按钮自动从上往下排，超出滚动）
            panelNotesList.Location = new Point(0, 0);
            panelNotesList.Size = new Size(listW, panel2.Height);

            // 「预览」按钮：右上角，始终保持在面板可视区域内
            btnMdToggle.Location = new Point(Math.Max(margin + listW, panel2.Width - btnW - margin), top);
            btnMdToggle.BringToFront();

            // 编辑框与预览框共用同一区域：列表栏右侧、顶部按钮栏以下
            int textTop = top + 34;
            int tw = Math.Max(20, panel2.Width - listW - margin * 2);
            int th = Math.Max(20, panel2.Height - textTop - margin);
            textBox1.Location = new Point(listW + margin, textTop);
            textBox1.Size = new Size(tw, th);
            rtbNotesPreview.Location = textBox1.Location;
            rtbNotesPreview.Size = textBox1.Size;
        }

        private void BuildPageDots()
        {
            _pageIndicator = new PageIndicator();
            _pageIndicator.Setup(_store.Layout.PageCount, _palette.DotActive, _palette.DotInactive);
            _pageIndicator.SetPageNames(_store.PageNames);
            _pageIndicator.PageSelected += (s, idx) => SwitchToPage(idx);
            _pageIndicator.PageRenameRequested += (s, idx) => RenamePage(idx);
            this.Controls.Add(_pageIndicator);
            RepositionPageDots();
        }

        public Launcher()
        {
            InitializeComponent();
            _store.Load();
            SetPalette(_store.Theme);
            BuildSlots();
            BuildPageDots();
            // 恢复上次所在页（重启记忆）
            int lastIdx = _store.PageNames.IndexOf(_store.LastPage);
            _currentPageName = (lastIdx >= 0) ? _store.PageNames[lastIdx] : (_store.PageNames.Count > 0 ? _store.PageNames[0] : "第1页");

            LoadRuntimeAssets();
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.MouseWheel += new MouseEventHandler(Form1_MouseWheel);
            this.Deactivate += Launcher_Deactivate;
            RegisterGlobalHotkeys();
            SetupDesktopMouseHook();
            _search = new SearchController(_slots, _pageIndicator, _store,
                () => _currentPageName,
                EnsurePageLoaded,
                v => _suppressPageEviction = v,
                () => _palette);
            // 钩子不在构造函数安装：WH_MOUSE_LL 回调须由本线程消息循环执行，
            // 开机自启时构造函数还在做加载等繁重工作（未进消息循环），安装过早会让全局鼠标输入被卡住发飘。
            // 推迟到首次显示后安装，此时消息循环已就绪，不再拖累启动。
            // 同时分批异步渲染当前页图标，避免一次性渲染阻塞消息循环导致鼠标卡顿。
            this.Shown += (s, e) =>
            {
                _desktopMouseHook.Install();
                StartInitialPageRender(_currentPageName);
            };

            _animationTimer = new Timer();
            _animationTimer.Interval = 50;
            _animationTimer.Tick += AnimationTimer_Tick;

            _configWatcher = new ConfigWatcher(this, AppDomain.CurrentDomain.BaseDirectory);
            _configWatcher.Reloaded += ReloadConfigFromWatcher;
            _configWatcher.Start();

            _notesTimer = new Timer();
            _notesTimer.Interval = 600;
            _notesTimer.Tick += (s, e) => { _notesTimer.Stop(); SaveNotes(); };
            textBox1.TextChanged += (s, e) =>
            {
                if (_suppressNotesSave) return;
                _notesTimer.Stop();
                _notesTimer.Start();
            };
            // 多便签：初始化存储并加载当前便签，刷新左边缘列表
            _notes = new NotesStore(AppDomain.CurrentDomain.BaseDirectory);
            _notes.Load();
            _suppressNotesSave = true;
            textBox1.Text = _notes.Read(_notes.CurrentName);
            _suppressNotesSave = false;
            RefreshNotesList();

            // 托盘菜单"开机自启"按当前注册表状态打勾
            try { autostartToolStripMenuItem.Checked = AutoStartManager.IsEnabled(); } catch { }

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
            string btnImg = Path.Combine(baseDir, "Resources", "button2.png");
            if (File.Exists(btnImg))
            {
                try { btnOpenFolder.Image = Image.FromFile(btnImg); }
                catch (Exception ex) { Log("加载按钮图片失败: " + ex); }
            }
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
            RefreshPageIndicator();
            InvalidateAllPageCaches();
            EnsurePageLoaded(_currentPageName);
            LoadPageFromCache(_currentPageName);
        }

        // ===== 槽位交互 =====
        private void StartWindowDrag()
        {
            // 手动启动窗口移动：释放鼠标捕获后向窗体发送 HTCAPTION 非客户区按下，
            // 让 Windows 接管拖拽循环（等价于按住标题栏拖动），无边框窗口也能整窗拖动。
            try
            {
                NativeMethods.ReleaseCapture();
                NativeMethods.SendMessage(this.Handle, NativeMethods.WM_NCLBUTTONDOWN, (IntPtr)NativeMethods.HTCAPTION, IntPtr.Zero);
            }
            catch { }
        }

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

        // ===== 便签持久化（多便签）=====
        private void SaveNotes()
        {
            if (_notes == null) return;
            _notes.Write(_notes.CurrentName, textBox1.Text ?? string.Empty);
        }

        // 刷新左边缘便签列表：每条便签一个按钮，从上往下整齐排列；当前便签高亮。
        // 按钮显示便签名的首行摘要（过长截断），悬停 Tooltip 显示全名。
        private void RefreshNotesList()
        {
            if (_notes == null || flowNotesList == null) return;
            try
            {
                if (_noteTips == null) _noteTips = new ToolTip();
                // 清空旧按钮
                foreach (var b in _noteTabButtons) { flowNotesList.Controls.Remove(b); b.Dispose(); }
                _noteTabButtons.Clear();

                var names = _notes.ListNames();
                foreach (var name in names)
                {
                    var btn = new Button();
                    btn.FlatStyle = FlatStyle.Flat;
                    btn.FlatAppearance.BorderSize = 0;
                    btn.Font = new Font("微软雅黑", 8.25F, FontStyle.Regular, GraphicsUnit.Point, ((byte)(134)));
                    btn.Size = new Size(38, 28);
                    btn.Margin = new Padding(1, 1, 1, 1);
                    btn.Cursor = Cursors.Hand;
                    btn.Tag = name;
                    // 摘要：取便签内容首行非空文字，最多 3 字（列宽窄），空便签显示首字
                    string content = _notes.Read(name);
                    string firstLine = (content ?? "").Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
                    string summary = string.IsNullOrWhiteSpace(firstLine)
                        ? (name.Length > 2 ? name.Substring(0, 2) : name)
                        : firstLine.Trim();
                    if (summary.Length > 3) summary = summary.Substring(0, 3);
                    btn.Text = summary;
                    _noteTips.SetToolTip(btn, name); // 悬停显示完整便签名
                    btn.Click += (s, e) => SwitchNote(name);
                    btn.MouseUp += (s, e) =>
                    {
                        if (e.Button == MouseButtons.Right) ShowNoteTabMenu(name);
                    };
                    _noteTabButtons.Add(btn);
                    flowNotesList.Controls.Add(btn);
                }
                HighlightCurrentNote();
            }
            catch (Exception ex) { Log("刷新便签列表失败: " + ex.Message); }
        }

        // 当前便签按钮高亮（选中色随主题）
        private void HighlightCurrentNote()
        {
            Color active = _theme == ThemeMode.Dark ? Color.FromArgb(60, 60, 60) : Color.FromArgb(220, 230, 245);
            Color activeFore = _theme == ThemeMode.Dark ? Color.White : Color.Black;
            Color idleBack = _theme == ThemeMode.Dark ? Color.FromArgb(38, 38, 38) : Color.FromArgb(240, 240, 240);
            Color idleFore = _theme == ThemeMode.Dark ? Color.Silver : Color.DimGray;
            foreach (var b in _noteTabButtons)
            {
                bool current = string.Equals(b.Tag as string, _notes?.CurrentName, StringComparison.Ordinal);
                b.BackColor = current ? active : idleBack;
                b.ForeColor = current ? activeFore : idleFore;
            }
            if (panelNotesList != null)
                panelNotesList.BackColor = _theme == ThemeMode.Dark ? Color.FromArgb(25, 25, 25) : Color.FromArgb(235, 235, 235);
            if (btnAddNote != null)
            {
                btnAddNote.BackColor = _theme == ThemeMode.Dark ? Color.FromArgb(25, 25, 25) : Color.FromArgb(235, 235, 235);
                btnAddNote.ForeColor = _theme == ThemeMode.Dark ? Color.Silver : Color.DimGray;
            }
        }

        // 切换便签：先保存当前，再加载目标便签内容
        private void SwitchNote(string name)
        {
            if (_notes == null || name == _notes.CurrentName) return;
            SaveNotes(); // 切走前落盘当前便签
            // 预览态先切回编辑态，避免预览内容与新便签错位
            if (_notesPreviewing) btnMdToggle.PerformClick();
            if (_notes.SetCurrent(name))
            {
                _suppressNotesSave = true;
                textBox1.Text = _notes.Read(name);
                _suppressNotesSave = false;
                HighlightCurrentNote();
                Log("切换便签: " + name);
            }
        }

        // 便签按钮右键菜单：重命名 / 删除
        private void ShowNoteTabMenu(string name)
        {
            using (var menu = new ContextMenuStrip())
            {
                var renameItem = new ToolStripMenuItem("重命名");
                renameItem.Click += (s, e) => RenameNote(name);
                var deleteItem = new ToolStripMenuItem("删除");
                deleteItem.Click += (s, e) => DeleteNote(name);
                menu.Items.Add(renameItem);
                menu.Items.Add(deleteItem);
                menu.Show(Cursor.Position);
            }
        }

        private void RenameNote(string name)
        {
            using (var dlg = new PageNameDialog("重命名便签", name, _palette))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (_notes.Rename(name, dlg.PageName))
                {
                    RefreshNotesList();
                    if (_notes.CurrentName == dlg.PageName)
                    {
                        _suppressNotesSave = true;
                        textBox1.Text = _notes.Read(dlg.PageName);
                        _suppressNotesSave = false;
                    }
                    SaveNotes();
                    Log("重命名便签: " + name + " → " + dlg.PageName);
                }
            }
        }

        private void DeleteNote(string name)
        {
            var r = MessageBox.Show("确定删除便签「" + name + "」吗？\n删除后不可恢复。",
                "删除便签", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (r != DialogResult.Yes) return;
            if (_notes.Delete(name))
            {
                _suppressNotesSave = true;
                textBox1.Text = _notes.Read(_notes.CurrentName);
                _suppressNotesSave = false;
                RefreshNotesList();
                Log("删除便签: " + name);
            }
        }

        // 「+」新建便签：自动命名，立即出现在列表顶部序列并切换过去
        private void btnAddNote_Click(object sender, EventArgs e)
        {
            if (_notes == null) return;
            SaveNotes(); // 先保存当前，防止丢字
            string name = _notes.CreateNote(null);
            if (name == null) { Log("新建便签失败"); return; }
            _notes.SetCurrent(name);
            _suppressNotesSave = true;
            textBox1.Text = string.Empty;
            _suppressNotesSave = false;
            RefreshNotesList();
            textBox1.Focus();
            Log("新建便签: " + name);
        }

        // ===== 便签 Markdown 预览（纯逻辑在 Core/MarkdownRenderer，可单测）=====

        private bool _notesPreviewing = false; // 便签是否处于 Markdown 预览态

        // 随主题取 Markdown 配色：正文/标题/链接/代码前景/代码背景
        private (Color TextFore, Color Heading, Color Link, Color CodeFore, Color CodeBack) MarkdownColors()
        {
            bool dark = _theme == ThemeMode.Dark;
            return (
                _palette.TextFore,
                dark ? Color.FromArgb(255, 200, 90) : Color.FromArgb(0, 120, 180),   // 标题：深色=暖黄，浅色=靛蓝
                dark ? Color.FromArgb(90, 170, 255) : Color.FromArgb(0, 102, 204),   // 链接
                dark ? Color.FromArgb(120, 210, 140) : Color.FromArgb(0, 128, 80),   // 代码前景：深色=绿，浅色=深绿
                dark ? Color.FromArgb(35, 35, 35) : Color.FromArgb(240, 240, 240)    // 代码背景
            );
        }

        // 用当前 textBox1 内容渲染预览 RTF（预览态随主题刷新时也调用）
        private void RenderNotesPreview()
        {
            var c = MarkdownColors();
            rtbNotesPreview.Rtf = MarkdownRenderer.ToRtf(textBox1.Text, c.TextFore, c.Heading, c.Link, c.CodeFore, c.CodeBack);
        }

        private void btnMdToggle_Click(object sender, EventArgs e)
        {
            try
            {
                if (!_notesPreviewing)
                {
                    SaveNotes(); // 切到预览前先落盘
                    RenderNotesPreview();
                    textBox1.Visible = false;
                    rtbNotesPreview.Visible = true;
                    btnMdToggle.Text = "编辑";
                    btnMdToggle.ForeColor = _palette.AccentPencil;
                    _notesPreviewing = true;
                }
                else
                {
                    rtbNotesPreview.Visible = false;
                    textBox1.Visible = true;
                    btnMdToggle.Text = "预览";
                    btnMdToggle.ForeColor = Color.Silver;
                    _notesPreviewing = false;
                    textBox1.Focus();
                }
            }
            catch (Exception ex) { Log("便签预览切换失败: " + ex.Message); }
        }

        // 预览态点击链接：用系统默认浏览器打开
        private void rtbNotesPreview_LinkClicked(object sender, LinkClickedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(e.LinkText)) return;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.LinkText) { UseShellExecute = true });
                Log("打开链接: " + e.LinkText);
            }
            catch (Exception ex) { Log("打开链接失败: " + ex.Message); }
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

        // 启动时分批异步渲染当前页图标：每 tick 渲染少量图标后让出控制权，
        // 消息循环得以在渲染间隙处理鼠标事件（含低级鼠标钩子回调），避免一次性渲染导致鼠标卡顿。
        private void StartInitialPageRender(string pageName)
        {
            // 停止可能在跑的上一轮分批渲染（如用户快速切页），避免两个 Timer 同时渲染
            try { _initialRenderTimer?.Stop(); _initialRenderTimer?.Dispose(); } catch { }
            _initialRenderTimer = null;
            var list = _store.GetPage(pageName);
            if (list == null) { EnsurePageLoaded(pageName); LoadPageFromCache(pageName); return; }
            _initialRenderPage = pageName;
            _initialRenderCursor = 0;
            _initialRenderTimer = new Timer { Interval = InitialRenderInterval };
            _initialRenderTimer.Tick += InitialRenderTimer_Tick;
            _initialRenderTimer.Start();
        }

        private void InitialRenderTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                // 渲染期间用户已切页：停止分批，交由 SwitchToPage 的 EnsurePageLoaded 接管
                if (_currentPageName != _initialRenderPage) { _initialRenderTimer.Stop(); return; }
                var list = _store.GetPage(_initialRenderPage);
                if (list == null) { _initialRenderTimer.Stop(); return; }
                int end = Math.Min(_initialRenderCursor + InitialRenderBatchSize, list.Count);
                for (int i = _initialRenderCursor; i < end; i++)
                {
                    var data = list[i];
                    if (data == null || data.IsEmpty) { data?.DisposeImage(); continue; }
                    if (data.RawIcon == null)
                        data.RawIcon = IconRenderer.RenderIconOnly(_store.Layout.IconSize, _store.Layout.IconSize, data.FilePath, data.IconPath);
                    data.CachedImage?.Dispose();
                    data.CachedImage = IconRenderer.ComposeBitmap(data.RawIcon, _store.Layout.IconSize, _store.Layout.IconSize, data.FilePath, _palette, data.DisplayName);
                }
                LoadPageFromCache(_initialRenderPage);
                _initialRenderCursor = end;
                if (_initialRenderCursor >= list.Count)
                {
                    _initialRenderTimer.Stop();
                    if (!_pageCacheLoaded.Contains(_initialRenderPage)) _pageCacheLoaded.Add(_initialRenderPage);
                    TouchPage(_initialRenderPage);
                    EnforcePageCacheLimit();
                }
                Invalidate();
            }
            catch (Exception ex) { Log("启动渲染图标失败: " + ex.Message); try { _initialRenderTimer?.Stop(); } catch { } }
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

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // 缩放过程中（含程序启动设置 ClientSize）同步重排控件，避免元素错位
            try { LayoutChrome(); }
            catch (Exception ex) { Log("重排布局异常: " + ex.Message); }
        }

        // 拖拽缩放结束时：把用户调整后的尺寸写回配置，下次启动保持
        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            try
            {
                if (_minGridSize.Width <= 0 || _minGridSize.Height <= 0) return;
                if (_store.Layout.Width == ClientSize.Width && _store.Layout.Height == ClientSize.Height) return;
                _store.Layout.Width = ClientSize.Width;
                _store.Layout.Height = ClientSize.Height;
                SaveConfig();
                Log($"窗口尺寸已调整并保存: {ClientSize.Width}×{ClientSize.Height}");
            }
            catch (Exception ex) { Log("保存窗口尺寸失败: " + ex.Message); }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_realExit)
            {
                e.Cancel = true;
                SaveConfig();
                SaveNotes();
                this.Hide();
                return;
            }

            base.OnFormClosing(e);
            UnregisterGlobalHotkeys();
            _configWatcher?.Dispose();
            _desktopMouseHook.Uninstall();
            try { _initialRenderTimer?.Stop(); _initialRenderTimer?.Dispose(); } catch { }
            SaveConfig();
            SaveNotes();

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
            // 置顶(钉住)模式不隐藏；autoHide 关闭时不隐藏
            if (this.TopMost) return;
            if (!_store.Layout.AutoHideEffective) return;
            this.Hide();
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
            btnPinTop.ForeColor = this.TopMost ? _palette.AccentStick : Color.Silver;
        }

        // 便签面板开合
        private void btnNotes_Click(object sender, EventArgs e)
        {
            if (!panel2.Visible)
            {
                panel2.Show();
                RepositionNotesPanel();
                btnNotes.ForeColor = Color.Cyan;
                textBox1.Focus();
            }
            else
            {
                panel2.Hide();
                btnNotes.ForeColor = _palette.AccentPencil;
                SaveNotes();
            }
        }

        // 标题栏🌓按钮：切换深浅色皮肤
        private void btnTheme_Click(object sender, EventArgs e) => ToggleTheme();

        // 托盘菜单：切换深浅色皮肤
        private void themeToolStripMenuItem_Click(object sender, EventArgs e) => ToggleTheme();

        // 托盘菜单：用系统默认编辑器打开 launcher.json（含 hotkeys 配置段）
        private void settingsToolStripMenuItem_Click(object sender, EventArgs e)
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

        private void autostartToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try { AutoStartManager.SetEnabled(autostartToolStripMenuItem.Checked); }
            catch (Exception ex) { Log("开机自启切换失败：" + ex.Message); }
        }

        // 托盘菜单：导出配置（launcher.json + notes.txt + launcher.ini 打包为 zip）
        private void exportConfigToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                SaveConfig();
                SaveNotes();
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

        // 托盘菜单：导入配置（从 zip 还原并热重载）
        private void importConfigToolStripMenuItem_Click(object sender, EventArgs e)
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

        // 托盘菜单：重命名当前页
        private void renamePageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            int idx = _store.PageNames.IndexOf(_currentPageName);
            if (idx >= 0) RenamePage(idx);
        }

        // 托盘菜单：新增一页
        private void addPageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            int idx = _store.AddPage();
            if (idx < 0) return;
            SaveConfig();   // 内部已用 _skipNextReload 包裹，避免写盘触发文件监视器二次重载
            RefreshPageIndicator();
            SwitchToPage(idx);
            Log("新增一页（共 " + _store.Layout.PageCount + " 页）");
        }

        // 托盘菜单：删除当前页
        private void removePageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (_store.Layout.PageCount <= 1)
            {
                MessageBox.Show("只剩最后一页了，无法删除。", "无法删除", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int idx = _store.PageNames.IndexOf(_currentPageName);
            if (idx < 0) return;

            // 页内还有图标时二次确认，避免误删
            if (!_store.IsPageEmpty(_currentPageName))
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

        // 托盘菜单：关于
        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try { using (var f = new AboutForm()) f.ShowDialog(this); }
            catch (Exception ex) { Log("打开关于失败：" + ex.Message); }
        }

        private void Launcher_KeyDown(object sender, KeyEventArgs e)
        {
            // 便签编辑中：全部按键交给 notes 文本框，不拦截
            if (panel2.Visible && (textBox1.Focused || rtbNotesPreview.Focused)) return;

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
                    searchBox.Text = SearchCue;
                    searchBox.ForeColor = _palette.SearchCue;
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
            // 离开搜索框时，如果框里有内容就保持搜索状态，否则清除
            if (string.IsNullOrWhiteSpace(searchBox.Text))
            {
                searchBox.Text = SearchCue;
                searchBox.ForeColor = _palette.SearchCue;
            }
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
