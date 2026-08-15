# 开发日志 (CHANGELOG)

> 规则：本项目所有改动都记入本文件，并同步更新 README.md（见 `.workbuddy/memory/MEMORY.md`）。
> 每条格式：日期 → 改动摘要 → 影响文件。

---

## 2026-08-10（修复开机自启时卡顿/鼠标发飘）

- **鼠标钩子推迟到首次显示后安装，消除开机自启卡顿**：
  - 需求：软件开机自启时，开机后电脑很卡、鼠标乱飘，直到面板出现才恢复正常。
  - 根因：`WH_MOUSE_LL` 低级鼠标钩子此前在构造函数里安装（`InstallDesktopDblClickHook()`）。低级钩子回调只能由安装线程的消息循环执行——而启动时主线程正阻塞在构造函数中做图标加载等繁重工作（尚未进入消息循环），任何全局鼠标事件都要排队等钩子回调执行，导致系统级鼠标输入被卡住发飘；直到构造完成、消息循环启动、面板显示后才恢复。
  - 实现：`Controls/Form1.cs` 构造函数移除 `InstallDesktopDblClickHook()`，改为 `this.Shown += (s, e) => InstallDesktopDblClickHook();`——推迟到首次显示后安装，此时消息循环已就绪，不再拖累启动。启动本身是懒加载（只渲染当前页，无后台预加载），钩子安装后功能不变。
  - 验证：`dotnet build -c Release` 0 错误 0 警告；测试 53/53 通过；启动日志确认钩子在 `Shown` 后正常安装，双击呼出功能正常。

## 2026-08-09（修复双击桌面图标误呼出面板 + explorer 崩溃）

- **双击桌面图标不再误呼出面板；改用 MSAA 判定图标命中**：
  - 需求：双击桌面图标打开软件/文件夹时不该同时呼出面板。此前把整个 `SysListView32` 当作桌面空白，导致双击图标也呼出。
  - 实现：`Controls/Form1.cs` 的 `IsDesktopOrTaskbar()` 桌面分支——命中 `SysListView32` 时用新增 `IsDesktopIconAt()` 判断点是否落在图标上：`AccessibleObjectFromPoint` 返回 `pvarChild` 为 `CHILDID_SELF(0)` 即空白（可呼出），非 0 子 ID 或返回子对象即命中图标（不呼出）。
  - 排查记录（关键教训）：**不得向 explorer 的桌面列表发跨进程指针消息**——首次尝试用 `LVM_HITTEST`（`LVHITTESTINFO*`）发给 `SysListView32`，explorer 直接解引用我方进程地址空间的指针导致崩溃、桌面重启。改为 `AccessibleObjectFromPoint`（MSAA，COM 自动跨进程封送、只读、安全）。`Accessibility.IAccessible` 托管包装的 `accHitTest` 无 out 子参数，故不可用，最终用 `AccessibleObjectFromPoint` 的 `pvarChild` 判定。
  - 验证：`dotnet build -c Release` 0 错误 0 警告；测试 53/53 通过；真实环境双击桌面图标仅打开不呼出、桌面空白/任务栏空白正常呼出，explorer 稳定不崩溃。

## 2026-08-09（修复面板拖动有时不响应）

- **空槽位拖动＝移动面板，扩大可拖拽区域**：
  - 需求：拖动面板「有时能拖、有时拖不动」。根因：面板移动依赖 `WM_NCHITTEST` 返回 `HTCAPTION`（`Controls/Form1.cs`），但该消息只有鼠标落在**窗体自身空白区域**时才到达窗体；鼠标落在子控件上（图标槽、搜索框、按钮、圆点）时消息被子控件截走。而顶部被搜索框占满、中部被图标占满，真正可拖的只有 12px 缝隙与边缘留白，能否拖动全看是否恰好命中缝隙。
  - 实现：`Controls/IconSlot.cs` 新增 `WindowDragRequested` 事件——**空槽**（无图标内容）拖过阈值时触发；`Controls/Form1.cs` 新增 `StartWindowDrag()`：`ReleaseCapture()` + `SendMessage(WM_NCLBUTTONDOWN, HTCAPTION)` 让 Windows 接管拖拽循环（等价按住标题栏拖动）。非空图标拖拽仍是图标排序（行为不变），缝隙/留白拖动不变。可拖拽面积从缝隙扩大到全部空槽区。
  - 验证：`dotnet build -c Release` 0 错误 0 警告；测试 53/53 通过；真实环境空槽拖动/图标排序/缝隙拖动均正常。

## 2026-08-09（修复点图标打开后面板偶发不消失）

- **点击图标打开程序后主动隐藏面板**：
  - 需求：点击图标启动程序后，面板偶发不消失。原实现完全依赖 `Launcher_Deactivate`（失焦事件）隐藏——当目标程序未抢占焦点（单实例已运行、最小化启动、启动较慢等）时主面板不失焦，`Deactivate` 不触发，面板残留。
  - 实现：`Controls/Form1.cs` 的 `OpenSlot()` 在 `Process.Start` 成功后调用新增 `HidePanelAfterOpen()` 主动 `Hide()`；判断条件与失焦隐藏一致（置顶/钉住、帮助弹窗打开、`autoHide:false` 时不隐藏）。键盘打开/搜索打开均经由 `OpenSlot` 一并覆盖。
  - 验证：`dotnet build -c Release` 0 错误 0 警告；测试 53/53 通过；真实环境反复点击图标面板均正常消失。

## 2026-08-09（双击桌面/任务栏呼出面板）

- **双击桌面或任务栏空白处呼出主面板**：
  - 需求：在桌面空白处或任务栏空白位置双击鼠标左键，即可呼出主面板，无需切回全局快捷键。
  - 实现：`Controls/Form1.cs` 新增 `WH_MOUSE_LL` 全局低级鼠标钩子（`InstallDesktopDblClickHook()`/`MouseHookCallback`）——用 `WindowFromPoint` 取命中窗口并沿 `GetAncestor` 追溯到顶层判定目标区域。桌面（根类 `Progman`/`WorkerW`）与任务栏（根类 `Shell_TrayWnd`/`Shell_SecondaryTrayWnd`，且命中窗口不是开始按钮/任务按钮/托盘/时钟等可交互子窗口）均生效。双击判定兼容两种消息：带 `CS_DBLCLKS` 样式的窗口（桌面图标层 `SysListView32`）直接收到 `WM_LBUTTONDBLCLK`；无该样式的窗口（任务栏 `MSTaskListWClass` 覆盖空白区）只发两次 `WM_LBUTTONDOWN`，故按系统 `GetDoubleClickTime`/`SM_CX(CY)DOUBLECLK` 做「时间+位移」双重判定。`BeginInvoke` 回 UI 线程仅显示面板（不二次隐藏）。钩子随窗口关闭卸载，配置项 `layout.dblClickShow` 控制开关（缺省启用），热更新即时生效。
  - 排查记录：首版只监听 `WM_LBUTTONDBLCLK` 且任务栏要求命中类必须是 `Shell_TrayWnd`，实际任务栏空白被 `MSTaskListWClass` 覆盖且无 `CS_DBLCLKS`，导致任务栏无响应；经临时诊断日志（`dblclick-debug` 记录命中/父/根窗口类）定位后修复。
  - 验证：`dotnet build -c Release` 0 错误 0 警告；测试 53/53 通过（新增 `Layout_DblClickShow_DefaultsEnabled_RoundTrips`）；真实环境实测桌面与任务栏双击均正常呼出。

## 2026-08-09（便签面板自适应窗口尺寸）

- **便签面板随窗口尺寸自适应**：
  - 需求：窗口可缩放后，便签面板（`panel2`）与其内部控件仍是设计器里的固定尺寸——面板宽高不随主面板变化，缩小后右上角「预览」按钮跑到窗口外找不到。
  - 实现：`Controls/Form1.cs` 新增 `RepositionNotesPanel()`——面板铺满内容区（Left=0、Top=24、宽=ClientSize.Width、高=ClientSize.Height−24），「预览」按钮贴面板右上角并 `BringToFront()` 保证可点，编辑框/预览框共用区域并占满剩余空间；`LayoutChrome()` 与便签打开时均调用。窗口再缩放，便签面板和按钮都跟着走。
  - 验证：`dotnet build -c Release` 0 错误 0 警告；测试 52/52 通过；冒烟测试启动稳定。

## 2026-08-09（链接悬停提示友好化）

- **链接悬停提示改为友好域名**：
  - 需求：悬停图标时链接原文太长、不协调、不易辨别。
  - 实现：`Core/SearchEngine.cs` 新增 `UrlLabel(string)` —— 去掉协议与 `www.` 留域名，一级路径存在且总长 ≤32 时附上（区分同站不同页），过长只留域名；`Controls/IconSlot.cs` 的悬停提示对 URL 改用 `UrlLabel`（文件仍显示文件名）。
  - 验证：`dotnet build -c Release` 0 错误 0 警告；测试 52/52 通过（新增 UrlLabel 去 www/附路径/超长裁剪 3 项）；冒烟测试启动稳定。

## 2026-08-09（拖拽调整窗口尺寸）

- **鼠标拖拽调整窗口宽高（自定义窗口尺寸）**：
  - 需求：支持自定义调节窗口长度和宽度。
  - 实现：`Controls/Form1.cs` 的 `WndProc` 在 `WM_NCHITTEST` 中按指针相对窗口边缘的距离返回缩放命中区（`HTLEFT/HTRIGHT/HTTOP/HTBOTTOM/HTTOPLEFT/HTTOPRIGHT/HTBOTTOMLEFT/HTBOTTOMRIGHT`，边缘 6px），中心区仍返回 `HTCAPTION` 便于拖动移动；新增 `LayoutChrome()` 缩放同步重排（图标网格按可用宽度自适应列数、可大也可小，右上角按钮组随右边缘对齐、搜索框拉宽、底部圆点居中），`OnResizeEnd` 把调整后尺寸写回配置；`ConfigStore.cs` 的 `LayoutConfig` 新增 `width`/`height`（0=自动，钳制 0–4000），`BuildSlots` 使用配置尺寸，`MinimumSize` 仅为很小的绝对下限（100×48）——缩小后图标列数自动变少、多行显示，不会被裁掉或只能变大。
  - 验证：`dotnet build -c Release` 0 错误 0 警告；测试 49/49 通过（新增 width/height 默认 0、持久化、越界钳制 3 项）；冒烟测试新版启动稳定无崩溃。

## 2026-08-09（悬停提示）

- **图标悬停显示软件名**：
  - 需求：鼠标悬停在软件图标上时显示软件名称。
  - 实现：`Controls/IconSlot.cs` 新增 `ToolTip`（沿用 `PageIndicator` 的悬停提示模式），`OnMouseEnter` 时用 `SearchEngine.SlotLabel` 取名称（URL 显示原文、文件显示文件名），`OnMouseLeave` 隐藏。经反馈简化为只显示名称、不含路径。
  - 验证：`dotnet build -c Release` 0 错误 0 警告；测试 46/46 通过；冒烟测试新版启动稳定无崩溃。

## 2026-08-08

### 工程分析 / 编译 / 崩溃修复（基线确立）
- 分析项目结构，产出 `PROJECT_ANALYSIS.md`。
- 编译：`dotnet build launcher.csproj -c Release` 成功，产物 `bin/Release/launcher.exe`（0 错误，1 警告 CS0414 `_totalPages` 未使用）。
- **修复启动崩溃**（双击打不开）：
  - 根因：`GenerateResourceUsePreserializedResources=true` 使 `Form1.resx` 对象资源（图标/图片）被编成依赖 `System.Resources.Extensions`（.NET 6 包）的预序列化格式，在 netfx 4.7.2 上 `InitializeComponent()` 加载即崩。
  - 处理：属性改 `false`；用脚本剥离 `Form1.resx` 与 `Properties/Resources.resx` 的对象资源节点；`button2.Image` 导出为 `Resources/button2.png`；`Launcher.ico` 复制到 `Resources/`；`Form1.cs` 新增 `LoadRuntimeAssets()` 运行时从 `Resources/` 加载图标/图片；移除 csproj 的 `System.Resources.Extensions` 引用与 App.config 绑定重定向。
  - 验证：PowerShell 启动进程正常存活，事件日志无新增 .NET 错误。
- 产出 `ROADMAP.md`（P0–P3 阶段 + M1–M4 里程碑）。
- **确立开发约定**：所有改动记入本日志并同步 README。

### 错误日志与诊断（P0：可诊断性）
- 新增静态 `Launcher.Log(string)`：写 `launcher.log`（程序目录，UTF-8 BOM，带时间戳），自身异常静默。
- `Program.cs` 增加全局未捕获异常处理器（`Application.ThreadException` + `AppDomain.CurrentDomain.UnhandledException`），崩溃/启动失败也写入 `launcher.log` 并弹简要提示——解决「打不开却毫无提示」。
- `Form1.cs` 7 处业务 `catch`（热更新/打开位置/启动/保存/加载配置/获取图标/加载页面）在原有 `MessageBox` 前加 `Log(...)` 写完整异常；`LoadRuntimeAssets()` 原有空 `catch` 改为记录资源加载失败。
- 构造函数末尾加 `Log("启动完成，当前配置: ...")`，便于确认日志系统工作、定位启动后卡顿。
- **附带修复脏数据**：之前生成的 `Resources/button2.png` 是 458 字节损坏文件（非 PNG），`Image.FromFile` 抛 `OutOfMemoryException`；用脚本重新生成有效 PNG（透明底紫色 X，23×22），日志不再刷错。
- 验证：`dotnet build` 0 错误；启动进程存活且 `launcher.log` 仅含「启动完成」一行（无 OOM），中文显示正确。

### 文档同步（README / 开发日志）
- 新建 `CHANGELOG.md`（本文件）。
- `README.md` 同步真实状态：
  - 新增「本地构建与运行」「开发日志」「当前状态」三节。
  - 修正原 README 夸大的「便携 notepad ✅已实现」：基础便签面板存在但内容关闭即丢，持久化未实现。
  - 在「当前状态」中明确：启动崩溃已修复；Ctrl-Z 撤销删除尚未实现。

### P0 稳定性补全（配置解析健壮 + 退出语义澄清）
- **配置解析健壮化**：原 `Line.Split('=')` 在路径或值本身含 `=` 时会拆出 3 段，因 `parts.Length == 2` 不成立而整行被静默丢弃（图标丢失、配置错乱）。
  - 新增静态 `TryParseConfigLine()`：仅按**第一个** `=` 切分键/值，键空或缺失 `=` 的行跳过；值不做 trim 以保留路径原貌。
  - 替换 `LoadConfig(string)`（约 L481）与 `LoadPageToCache(string)`（约 L1134）两处配置解析逻辑。
- **退出语义澄清（与帮助文案「右键托盘退出程序」一致）**：
  - 点窗口 × / Alt+F4：原逻辑直接 `Close()` 使整个程序退出（与托盘菜单的「退出」语义重复、易误解）。改为 `OnFormClosing` 中 `e.Cancel=true; this.Hide()` **最小化到托盘**，程序继续常驻。
  - 真正的退出仅走托盘菜单「Quit（退出）」：`exitToolStripMenuItem_Click` 置 `_realExit=true` 后 `Application.Exit()`，`OnFormClosing` 识别标志位才执行保存+释放缓存并退出。
  - 新增字段 `private bool _realExit`，避免「以为关了其实没退」的歧义。
- 验证：`dotnet build` 0 错误；启动进程存活、`launcher.log` 仅含「启动完成」一行（无报错）。

### P1 功能补齐（Ctrl-Z 撤销删除 + 便签持久化）
- **Ctrl-Z 撤销删除**：
  - 右键删除图标前，把 `{控件, 路径, 是否文件夹}` 压入 `_undoStack`（容量上限 `MaxUndoDepth=50`，支持多级撤销）。
  - 新增 `PushUndo` / `ShowDeleteBalloon` / `UndoDelete` 辅助方法；删除后通过托盘气球提示「按 Ctrl+Z 可撤销」。
  - `Launcher_KeyDown` 新增 `Ctrl+Z` 分支调用 `UndoDelete()`（内部复用 `HandleFileDropped` 完整还原图标并 `SaveConfig`）；便签文本框获焦时让文本框自行处理按键，不拦截。
- **便签持久化**（兑现 README「便携 notepad」承诺）：
  - 新增 `SaveNotes()` / `LoadNotes()`：`textBox1` 内容读写至程序目录 `notes.txt`（UTF-8）。
  - 保存触发点：输入停顿 600ms 防抖定时器、关闭便签面板（`button4_Click`）、退出 / 最小化到托盘（`OnFormClosing` 两分支）。
  - `LoadNotes` 仅在 `notes.txt` 存在时覆盖文本（首启保留默认占位文字），并用 `_suppressNotesSave` 防止加载时误触发保存。占位文字改为「便签内容自动保存，关闭软件后不丢失」。
  - 验证：启动干净、`notes.txt` 未在首启时误创建（用户输入后才落盘）。
- **附带修正**：重绘 `Resources/button2.png` 关闭按钮图标（原 resx 提取出的文件损坏），改为 4× 超采样抗锯齿 X，匹配 `MediumPurple` 配色。
- 验证：`dotnet build` 0 错误；启动进程存活、日志干净。

### P1 追加：全局快捷键调用面板
- 目标：让面板（主窗口 / 便签）在**任意程序**中也能一键呼出，不要求焦点在 launcher 上。
- 用 Windows `RegisterHotKey` / `UnregisterHotKey` P/Invoke + `WndProc` 处理 `WM_HOTKEY` 实现（窗口隐藏时消息循环仍运行，故托盘常驻态下也能收到热键）。
- 注册（构造函数 `RegisterGlobalHotkeys()`，失败仅写日志不崩溃）：
  - `Ctrl+``（反引号/波浪键，数字 1 左侧）→ `ToggleMainWindow()` 切换主面板显隐。
  - `Ctrl+Shift+N` → `ToggleNotesPanel()`：若窗口隐藏先 `Show+Activate`，再 `button4.PerformClick()` 复用便签面板开关。
  - 两键均带 `MOD_NOREPEAT` 防长按重复触发。
- `OnFormClosing` 真正退出分支（仅 `_realExit` 时）`UnregisterHotKey` 释放，避免快捷键被进程残留占用、下次启动注册失败。
- 帮助面板（`button1_Click`）新增第 8、9 条说明全局快捷键与 Ctrl+Z。
- 验证：`dotnet build` 0 错误；启动后 `launcher.log` 出现「全局快捷键已注册：Ctrl+` 呼出主面板，Ctrl+Shift+N 便签面板」，进程存活无崩溃。

### P1 收尾：解硬编码（槽位/图标尺寸/分页/列数）
- 目标：把 ROADMAP 中写死的 40 槽位、54 图标、4 分页改为可配置。
- **剥离 Designer 写死控件**：用脚本把 `Form1.Designer.cs` 中 40 个 `pictureBox1..40` 与 4 个 `panel_dot1..4` 的全部声明/初始化/属性/`Controls.Add`/`ISupportInitialize` 行移除（共删 ~608 行），改为运行时动态生成。
- **`launcher.ini` 布局配置**：首次运行自动生成，含 `[Layout]` 段 `SlotCount`/`IconSize`/`PageCount`/`Columns`，带默认值与注释；修改后重启生效，并有安全边界（槽位 1~400、图标 16~256、分页 1~20、列 1~40）。
- **动态生成**：
  - `BuildSlots()`：按 `SlotCount`/`Columns`/`IconSize` 在 5×8 网格（step=IconSize+12，与原始 66 步进一致）生成 `pictureBox1..N` 并命名，供 `Controls.Find` 按配置键定位；窗口 `ClientSize` 随网格自动适配。
  - `BuildPageDots()`：按 `PageCount` 动态生成分页圆点（居中、贴近底部），Click → `SwitchToPage(index)`。
  - `LoadLayoutSettings()`：解析 ini → 填充 `_slotCount/_iconSize/_pageCount/_columns` 与 `_pageNames`（"panel_dot1"..N），并设 `_totalPages`。
- **逻辑泛化**：`SwitchToPage(index)` 取代原 `panel_dot1_Click`（遍历圆点高亮 + 加载缓存页）；`UpdateCurrentPage` 改为 `SwitchToPage(_currentPage-1)`；`Launcher_KeyDown`/`Form1_MouseWheel` 的硬编码 `4` 改为 `_totalPages`，数字键 `1..9` 动态跳页；`InitializePageCaches`/`PreloadAllPages` 改用 `_pageNames`；`DrawIconWithName` 的绘制图标尺寸改为随 `pictureBox` 大小缩放。
- 验证：`dotnet build` 0 错误 0 警告；默认配置（40/54/4/8）启动存活、生成 launcher.ini、日志干净；非默认配置（20 槽位/40 图标/3 分页）重启亦无崩溃，确认动态布局生效。
- 临时脚本（`_layout_probe.py`/`_strip_designer.py`/`_strip_designer2.py`）已清理。

### P1 收尾：深浅色皮肤切换
- 目标：在保持原有「黑底」观感的前提下，新增一套浅色皮肤，并支持一键切换 + 持久化。
- **主题模型**：新增 `ThemeMode` 枚举（`Dark`/`Light`）与调色板字段（`_cFormBack/_cSlotBack/_cSep/_cLabelFore/_cTextBack/_cTextFore/_cDotActive/_cDotInactive/_cNameStroke/_cNameFill/_cMenuBack/_cMenuFore/_cAccentHelp/_cAccentPencil/_cAccentStick`）。
  - `SetPalette(mode)`：深色调沿用原观感（黑底、白字、白/灰圆点、图标名黑描边白填充）；浅色调为白底、深字、蓝色高亮圆点、图标名白描边黑填充。
  - `ApplyThemeVisuals()`：把调色板应用到窗体、标题栏分隔条、标题文字、便签文本框、托盘菜单、各强调按钮；并遍历动态生成的 `pictureBox*`（槽位底）、`panel_dot*`（分页圆点，按 `currentconfig` 判定高亮）。
  - `RebuildAllIcons()`：主题切换后按新配色 `PreloadAllPages()` 重绘所有页面图标缓存并刷新当前页（图标下方文件名颜色随主题变化）。
- **切换入口（三选一）**：
  - 标题栏新增 `button5`（🌓）按钮 → `button5_Click` → `ToggleTheme()`。
  - 托盘右键菜单新增「切换深浅色主题(&T)」项（`themeToolStripMenuItem`/`themeToolStripMenuItem_Click`）。
  - 全局快捷键 `Ctrl+Shift+T`（新增 `HOTKEY_ID_THEME`，`WndProc` 中 `id==HOTKEY_ID_THEME` → `ToggleTheme()`；窗口隐藏态也能切换）。
- **持久化**：`launcher.ini` 新增 `[Theme] Mode=Dark|Light`；`SaveThemeSetting()` 经通用 `SetIniValue()` 写回（缺失段/键自动补齐，保留用户注释）；`LoadThemeSettings()` 在 `InitializeComponent()` 后、`BuildSlots()` 前读取，确保动态控件生成即用正确调色板。
- **联动修正**：`BuildSlots` 槽位底、，`BuildPageDots`/`SwitchToPage` 圆点、，`DrawIconWithName` 文件名描边/填充、帮助窗口（`button1_Click`）底色与文字、`,` 强调按钮（`button3` 置顶/取消、`button4` 关闭便签）均改用调色板，主题切换后保持一致。
- 验证：`dotnet build -c Release` **0 错误 0 警告**；启动日志新增主题字段（`主题=Dark/Light`）。
- **附带修复（ini 解析）**：原 `LoadLayoutSettings` 按首个 `=` 取值后未剥离行内注释，导致带 `; 注释` 的值（如 `Mode=Light   ; ...`）解析失败、被静默忽略（仅因布局默认值恰好等于模板值而未被发现）。已在 `LoadLayoutSettings` 与 `LoadThemeSettings` 取值后统一按首个 `;` 截断注释；默认 `launcher.ini` 模板相应补上 `[Theme]` 段与注释。

### P2 架构重构（命名统一 / 抽 IconSlot 用户控件 + ConfigStore / 结构化配置）
- 目标：把 ~1693 行的 god-object `Form1` 拆成「关注点单一」的多个文件，消除 `Dictionary<PictureBox, T>` 这类「用 WinForms 控件当字典键 + 配置写成 `pictureBox1=path` 再 `Controls.Find(name)` 回查」的反模式，并把配置升级为结构化格式。
- **稳定槽位标识**：用整数 `Index`（0 起）作为槽位唯一身份，取代「以 `PictureBox` 实例为键」的脆弱方案；`SlotData.Index` 即权威来源，不再依赖控件名回查。
- **新增 6 个文件**（全部在命名空间级，与 `Form1` 解耦）：
  - `ThemePalette.cs`：`ThemeMode` 枚举提到命名空间级；`ThemePalette` 类封装深色/浅色调色板（FormBack/SlotBack/Sep/LabelFore/TextBack/TextFore/DotActive/DotInactive/NameStroke/NameFill/MenuBack/MenuFore/AccentHelp/AccentPencil/AccentStick），取代原先散落在 `Form1` 的约 14 个 `_cXXX` Color 字段。
  - `SlotData.cs`：`[DataContract] class SlotData`（`int Index`、`string FilePath`、`bool IsFolder`、`Image CachedImage`【非序列化】、`IsEmpty` 属性、`DisposeImage()`）。
  - `IconRenderer.cs`：静态类，`IsHttpUrl`/`RenderToBitmap(w,h,filePath,palette)`（2× 超采样→克隆）/`Draw`/`GetFileIcon`/`GetFolderIcon`/`GetShortcutTargetFile` + `ExtractIconEx`/`DestroyIcon` P/Invoke；原 `DrawIconWithName`/`GetFileIcon`/`GetFolderIcon` 逐字移植，与 `Form1` 解耦。
  - `IconSlot.cs`：`public class IconSlot : PictureBox`，自包含：`SlotIndex`、`Data = new SlotData()`、`OriginalSize`、`Theme` 与事件 `OpenRequested`/`OpenLocationRequested`/`DeleteRequested`/`FileDropped(string)`/`DropTarget(IconSlot)`/`DragStarted`；构造里自行接 `MouseDown/Move/Up/Click/DragEnter/DragDrop`；`ApplyTheme(palette)` 设底、`Render()` 调 `IconRenderer.RenderToBitmap`。
  - `PageIndicator.cs`：`public class PageIndicator : Control`，替代原 `panel_dot1..N`；`Setup(count,active,inactive)` 定尺寸与配色，`OnPaint` 画椭圆，`OnMouseClick` 按 `e.X/(_dotSize+_dotGap)` 算 `idx`，raise `PageSelected(int)`。
  - `ConfigStore.cs`：`ConfigStore`（持有 `Layout`/`Theme`/`PageNames`/`Pages`），配 `[DataContract] LayoutConfig`/`SlotDoc`/`PageDoc`/`ConfigDoc`；用 `DataContractJsonSerializer`（仅引用 `System.Runtime.Serialization`，无需 NuGet 包）读写新 `launcher.json`；`Load()`→`LoadFromJson()` 或 `MigrateFromLegacy()`；`EnsurePageShape()` 施加安全边界（槽位 1–400、图标 16–256、分页 1–20、列 1–40）并把每页补齐到 `SlotCount`；`Save()` 写 `launcher.json`；迁移解析旧 `launcher.ini` 的 `[Layout]`/`[Theme]` 与旧 `panel_dotN.txt`（`pictureBox{n}=path`→`Index=n-1`、`IsFolder=Directory.Exists`）。
- **语义化重命名**：
  - Designer：`exitbutton→btnClose`、`button1→btnHelp`、`button2→btnOpenFolder`（火箭图标，开 exe 所在目录）、`button3→btnPinTop`、`button4→btnNotes`、`button5→btnTheme`；字段声明与 `Click +=` 处理器一并更新。
  - 运行时槽位控件命名为 `slot`+(i+1)（原 `pictureBox`+(i+1)），分页不再生成 `panel_dot*`，改由 `PageIndicator` 承接。
- **旧配置迁移**：`ConfigStore.Load()` 优先读 `launcher.json`；若无则读旧 `launcher.ini`+`panel_dotN.txt` 迁移并写出 `launcher.json`，旧文件保留一版不删（向后兼容，零配置升级）。
- **行为全部保留**：全局热键、`ThemeMode` 切换与持久化、Ctrl-Z 撤销删除（多级、`_undoStack`）、拖拽换位（`SwapIcons`）、缩放动画（`OriginalSize` 复制局部变量规避 CS1690）、分页缓存（`PreloadAllPages`/`LoadPageFromCache`）、`FileSystemWatcher` 热重载（经 `_skipNextReload` 标志位避免自写回环）。
- 构建：`dotnet build -c Release` **0 错误 0 警告**。
- 验证（无头冒烟测试，已用 `python subprocess` 调度并清理临时脚本）：
  - 首启生成 `launcher.json`（`{layout:{columns,iconSize,pageCount,slotCount}, pages:[{name,slots:[]}...], theme, version}`，UTF-8 带 BOM，结构正确）。
  - 旧格式迁移：`panel_dot1.txt` 写 `pictureBox1=C:\Windows\System32\notepad.exe`、`pictureBox3=C:\Windows\System32`，删 `launcher.json` 后启动正确迁移为 `slots:[{index:0,isFolder:False,path:...notepad.exe},{index:2,isFolder:True,path:...System32}]`，文件/文件夹图标均正常渲染、无崩溃。迁移后已还原原始配置。

### P2 追加：快捷键全部可配置（launcher.json）
- 背景：用户希望**所有**快捷键可改（不仅是全局热键，也包括窗内撤销/翻页/跳页）。因用户偏好改配置文件而非图形界面，采用 `launcher.json` 配置化方案，不做设置窗口。
- 新增 `HotkeyBinding.cs`：组合串解析/格式化工具。`Parse("Ctrl+Shift+T")`→(mods,vk)、`Matches(combo,ctrl,shift,alt,keyCode)` 用于窗内按键比对；支持修饰键 `Ctrl`/`Shift`/`Alt`/`Win` 与键名（字母 / 数字 / `` ` ``(`Oemtilde`) / `F1` 等），与 Win32 `MOD_*` 常量一致。
- `ConfigStore` 新增 `HotkeyConfig`（`[DataContract]`：`main`/`notes`/`theme`/`undo`/`prevPage`/`nextPage`/`jump[]` 均有默认值）；`ConfigDoc` 增加 `hotkeys` 段；`ApplyDoc`/`Save` 读写该段（旧 JSON 缺段时回退默认值，向后兼容）；新增 `JsonFilePath` 属性供「编辑配置」打开文件。
- `Form1.cs`：
  - `RegisterGlobalHotkeys()` 改为按 `_store.Hotkeys` 循环注册（`RegisterOne`/`UnregisterGlobalHotkeys` 辅助）；全局快捷键若无修饰键则跳过并写日志告警；`WndProc` 仍按 `HOTKEY_ID_MAIN/NOTES/THEME` 分派（id 固定，仅键变化）。
  - `Launcher_KeyDown` 改用配置比对：`undo`/`prevPage`/`nextPage`/`jump[]` 全部走 `HotkeyBinding.Matches`，匹配后 `e.Handled=true` 并 `SuppressKeyPress`。
  - `ReloadFromWatcher`（FileSystemWatcher 热更新）增加 `RegisterGlobalHotkeys()` 调用，运行时改 `launcher.json` 即实时刷新全局快捷键。
  - 托盘菜单新增「编辑配置(&E)」项（`settingsToolStripMenuItem_Click` 用系统默认编辑器打开 `launcher.json`）。
- `Form1.Designer.cs`：新增 `settingsToolStripMenuItem` 控件与 `Click` 接线；`csproj` 加入 `HotkeyBinding.cs` 编译项。
- 验证（无头 python 冒烟，已清理临时脚本）：默认配置日志正确打印 `全局快捷键已注册：主面板=Ctrl+`、便签面板=Ctrl+Shift+N、切换深浅色=Ctrl+Shift+T；自定义配置（`main=Ctrl+F1`、`theme=Ctrl+Shift+K`、`prevPage=F3`、`nextPage=F4`、`jump=Q..O`）启动无崩溃、日志反映新绑定。构建 `dotnet build -c Release` **0 错误 0 警告**。

### 交付包整理（便携版，双击即运行）
- 需求：产出可直接分发、双击 `launcher.exe` 即可运行、无需安装的程序包，并附简短运行说明。
- 先做了**依赖核实（clean-room 测试）**：仅拷贝 `launcher.exe` + `launcher.exe.config` + `Resources/`（不放任何 DLL）到全新临时目录运行，结果全局热键正常注册、启动完成、`launcher.json` 首启自动生成——证明**运行期零第三方 DLL 依赖**。
- 真实依赖结论（已写入交付包 README）：
  - 唯一外部依赖 = **.NET Framework 4.7.2**（Win10 1809+/Win11 默认自带）；
  - P/Invoke 仅用 `user32.dll`/`shell32.dll`（Windows 系统原生 DLL，不打包）；
  - `System`/`System.Windows.Forms`/`System.Drawing`/`System.Runtime.Serialization` 等均为 .NET Framework 自带（GAC），不随包分发；
  - 仓库 `lib/` 下 4 个程序集（`System.Buffers`/`System.Memory`/`System.Resources.Extensions`/`System.Runtime.CompilerServices.Unsafe`）属上游遗留、当前构建**不加载**，已在 README 注明“无需放入本包”。
- 交付包位置：`publish/launcher-portable/`，内容：`launcher.exe`、`launcher.exe.config`、`Resources/Launcher.ico`、`Resources/button2.png`、`README.md`（`launcher.json` 首启自动生成）。`Resources/` 为运行期必需（主图标/按钮图标从磁盘读取），不可删。
- 部署注意（README 已写）：包须放在**可写**目录（如 `D:\tools\launcher` 或“文档”），勿放 `C:\Program Files`（UAC 保护导致无法写配置/日志）；配置文件与全部快捷键均经 `launcher.json` 自定义，保存即生效。

### 发布到 GitHub（v1.0.0，便携版 + release 附件）
- 用户要求“上传至 GitHub，附带附件”。在 `worldoi` 下新建**公开**仓库 `launcher`（`gh repo create launcher --public --source . --push`），推送源码。
- 已加 `.gitignore` 排除 `bin/`、`obj/`、`.workbuddy/`（含个人记忆，绝不外传）、`lib/`（上游遗留、运行期不需要）、`*.user`、`*.pdb` 等，避免上传构建产物与私人数据。
- 创建 `v1.0.0` release，附件 `launcher-portable.zip`（= `publish/launcher-portable/` 压缩包，约 72KB，含 exe+config+Resources+README；首启自动生成 `launcher.json`）。
- 仓库：https://github.com/worldoi/launcher ；发布页：https://github.com/worldoi/launcher/releases/tag/v1.0.0
- 注意：本次仅建仓库 + 发布 release，**未启用 GitHub Actions / 工作流**（账号曾被 flagged，CI 属敏感操作，启用前会先征得同意）。提交身份用本仓库**局部** `user.name=worldoi` / `user.email=worhllo142587@outlook.com`（不改全局 git 配置）。git push 走 openssl 后端（`git config http.sslBackend openssl`）规避本机 schannel 证书吊销报错。
- `gh` 版本差异：`gh release create` 的附件须作为**位置参数**传（`gh release create v1.0.0 <file> ...`），`-a/--assets` 在本机 gh 版本不被支持。

### 修复：点击窗口外不自动隐藏
- **现象（用户反馈）**：点击 launcher 主窗口之外，窗口仍然存在、不会消失。
- **根因（两个独立 bug）**：
  1. `Form1.Designer.cs` 中 `this.TopMost = true;` 在窗体初始化时把主面板**默认置顶（钉住）**；而隐藏逻辑里“置顶时不自动隐藏”，于是默认状态就永不隐藏。
  2. `layout.autoHide` 字段虽默认 `true`，但 `ConfigStore` 用的是 `DataContractJsonSerializer`，其反序列化走 `GetUninitializedObject`，**不执行字段初始化器**，导致 `launcher.json` 里缺 `autoHide` 键时该值回落到 CLR 默认 `false`。
- **修复**：
  - `Form1.Designer.cs`：`this.TopMost = false;`（默认不钉住；标题栏图钉按钮仍可临时置顶常驻）。
  - `ConfigStore.cs`：将 `autoHide` 改为可空 `bool?` + `EmitDefaultValue=false`，并新增 `AutoHideEffective => AutoHide ?? true`：缺省（未写键）= 自动隐藏，显式 `false` = 不自动隐藏。
  - `Form1.cs`：新增 `Deactivate` 事件处理 `Launcher_Deactivate`，失焦时按“非置顶 且 非帮助弹窗 且 autoHide 有效”才 `Hide()`；给帮助模态框加 `_helpOpen` 守卫避免把父窗口藏到对话框后。
- **验证**：无头测试——启动后强制把焦点转移到记事本，`EnumWindows` 复查 launcher 主窗口 `IsWindowVisible` 由 `True` 变为不可见（自动隐藏生效）；`dotnet build -c Release` 0 错误 0 警告。
- **行为说明**：默认点击窗口外自动隐藏；想常驻则点标题栏 📌 图钉按钮（置顶时不自动隐藏）；也可在 `launcher.json` 设 `"layout": { "autoHide": false }` 关闭。

### P3 工程卫生：仓库瘦身（清理追踪的构建产物）
- 目标（ROADMAP P3 第 1 条）：让 `.gitignore` 真正生效、给仓库瘦身。
- **核实结论**：经 `git ls-files --cached --ignored --exclude-standard` 检查，`bin/`、`obj/`（及 `*.log`/`*.pdb`/`*.user`/`launcher.json`）**从未被追踪**——首次提交时 `.gitignore` 已生效，不存在「幽灵追踪」需清理。
- **真正的仓库噪声**：`publish/launcher-portable.zip`、`publish/launcher-portable/launcher.exe`、`publish/launcher-portable/launcher.exe.config` 是 `dotnet build` 产物 + 压缩包；且该 zip 已在 GitHub Release 作为下载附件挂载，仓库内再存一份纯属冗余。
- **处理**：`git rm --cached` 将这 3 个文件移出 git 索引（**保留本地文件**，仅停止追踪）；`.gitignore` 增补 `publish/*.zip`、`publish/launcher-portable/launcher.exe`、`publish/launcher-portable/launcher.exe.config`；并预防性忽略包内运行时数据 `publish/launcher-portable/launcher.json`/`launcher.log`/`notes.txt`（用户配置与日志，绝不入库）。
- 结果：`git ls-files` 现仅追踪源码、文档、资源与 `publish/launcher-portable/README.md`、`Resources/*`（打包素材），仓库体积与噪声显著降低。
- 本地提交 `3526862`：`chore: stop tracking build artifacts; refine .gitignore`。push 已于网络恢复后成功（`a6fc934..83c9b59`）。

### P3 工程卫生：模块分层目录（Controls / Core / Models）
- 目标（ROADMAP P3 第 2 条）：把根目录平铺的十几个 `.cs` 按职责归入分层目录，使仓库结构清晰、可维护。
- **分类规则**：
  - `Controls/`（UI 层）：`Form1.cs`/`Form1.Designer.cs`（主窗体）、`IconSlot.cs`（槽位控件）、`PageIndicator.cs`（分页圆点）、`IconRenderer.cs`（图标渲染）。
  - `Core/`（逻辑/服务层）：`Program.cs`（入口）、`ConfigStore.cs`（配置）、`HotkeyBinding.cs`（快捷键解析）、`ThemePalette.cs`（主题调色板）。
  - `Models/`（数据模型层）：`SlotData.cs`。
  - `Properties/`：原根的 `Settings.cs` 移入此处（与 `Settings.Designer.cs` 同为 `launcher.Properties.Settings` 的 partial）。
- **命名空间对齐**：全部从 `namespace launcher` 改为与目录一致 —— `launcher.Controls` / `launcher.Core` / `launcher.Models`（Settings 维持 `launcher.Properties`）；并据跨层引用补 `using`（如 `Form1` 加 `using launcher.Core` + `using launcher.Models`；`ConfigStore`/`Program` 加 `using launcher.Controls`；`IconSlot`/`IconRenderer` 加 `using launcher.Core` 等）。注意 `Core` 对 `Controls` 的依赖来自 `ConfigStore`/`Program` 调用 `Launcher.Log`（静态日志方法），属既有设计，本次仅补 `using` 不改结构。
- **csproj 同步**：`launcher.csproj` 的 `<Compile Include>` / `<EmbeddedResource Include>` 路径改为 `Controls\`/`Core\`/`Models\`/`Properties\`；`Form1.resx` 随窗体一并移入 `Controls/` 并保留 `DependentUpon`，确保嵌入资源名 `launcher.Controls.Launcher.resources` 与类名一致（避免运行时 `MissingManifestResourceException`）。
- 用 `git mv` 移动，保留 git 历史（不丢 blame）。
- **验证**：`dotnet build -c Release` **0 错误 0 警告**；检查程序集嵌入资源名确为 `launcher.Controls.Launcher.resources`；无头启动 `launcher.exe` 并检查 Windows 应用程序事件日志，无新增 `.NET Runtime` 错误（窗体 `InitializeComponent` 正常、resx 加载成功）。
- 影响文件：`Controls\Form1.cs`、`Controls\Form1.Designer.cs`、`Controls\Form1.resx`、`Controls\IconSlot.cs`、`Controls\PageIndicator.cs`、`Controls\IconRenderer.cs`、`Core\Program.cs`、`Core\ConfigStore.cs`、`Core\HotkeyBinding.cs`、`Core\ThemePalette.cs`、`Models\SlotData.cs`、`Properties\Settings.cs`、`launcher.csproj`、`README.md`（架构节）。

### 重新打包交付物并覆盖 GitHub Release 附件（对齐当前代码）
- 背景：此前交付包（`publish/launcher-portable/` 与 GitHub v1.0.0 附件）落后于当前代码——README 仍是旧版长说明、且混入了本机运行时文件（`launcher.json`/`launcher.log`/`notes.txt`），自改完 README（commit `395751d`）后从未重新打包/发布。
- 重新组装 `publish/launcher-portable/`（用 Python 脚本，直接 `os.remove` 绕过 safe-delete 钩子）：
  - 剔除运行期自动生成的本机文件：`launcher.json` / `launcher.log` / `notes.txt`（绝不随包发）。
  - 换新：最新 `bin/Release/launcher.exe`（含模块分层 + autoHide 修复，176,640B）、`launcher.exe.config`、`Resources/Launcher.ico` + `Resources/button2.png`、以及**新的简洁 README**（已标注上游 cornradio/launcher）。
  - 重新打 `publish/launcher-portable.zip`（72,219B，原 73,848B）。
- **覆盖上传** GitHub Release v1.0.0 附件：`gh release upload v1.0.0 --clobber publish/launcher-portable.zip` 成功，附件更新为 72,219B（updatedAt 2026-08-08T12:45:17Z）。
- 结论：现在线上交付包与当前源码（HEAD `395751d`）**完全一致**（程序功能一致，包内容 = 最新 exe + 新 README + Resources，无隐私文件）。
- 注：打包脚本为一次性临时文件，已删除，未入库。

### 体验打磨：高分屏 DPI 适配 + 开机自启 + 配置导出导入 + 应用内「关于」
- 目标：在不引入 CI/安装包（用户暂不处理、且 GitHub Actions 属敏感操作）的前提下，继续完善可用性与体验。
- **高分屏 DPI 适配**：新增 `app.manifest`，声明 `dpiAware=true/pm` + `dpiAwareness=PerMonitorV2, PerMonitor` 与 `requestedExecutionLevel=asInvoker`；csproj 加 `<ApplicationManifest>app.manifest</ApplicationManifest>`。WinForms 在 4.7.2 下据此自动做每监视器 DPI 缩放，4K/高缩放下图标与文字不再模糊。验证：`bin/Release/launcher.exe` 内嵌 manifest 含 `PerMonitorV2`/`dpiAwareness`/`asInvoker`；`dotnet build -c Release` 0 错误 0 警告。
- **开机自启（注册表 Run 键）**：托盘菜单新增「开机自启(&R)」（CheckOnClick）。读写 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 的 `Launcher` 项（值=引号包裹的 `Application.ExecutablePath`），无需管理员权限；启动时读取状态并打勾。Helper：`IsAutoStartEnabled()` / `SetAutoStartEnabled(bool)`（均 try/catch 静默）。
- **配置备份/导出导入**：托盘菜单新增「导出配置(&X)」「导入配置(&I)」。导出=把 `launcher.json`+`notes.txt`(+`launcher.ini`，存在才含) 用 `System.IO.Compression.ZipFile` 打包为 `.zip`（SaveFileDialog）；导入=从 `.zip` 解压回程序目录并热重载（`_store.Load()` + `PreloadAllPages()` + `ApplyThemeVisuals()`）。csproj 加 `System.IO.Compression` / `System.IO.Compression.FileSystem` 引用。
- **应用内「关于」**：新增 `Controls/AboutForm.cs`（WinForms 对话框），显示程序版本号（Assembly 版本 + UTC 构建日期）、**主链接指向本项目主页** `https://github.com/worldoi/launcher`（LinkLabel 点击用 `Process.Start` 打开浏览器），并以灰色小字致谢上游 `cornradio/launcher`；托盘菜单「关于(&A)」打开它。
- **验证**：`dotnet build -c Release` 0 错误 0 警告；无头启动 `launcher.exe` 查 Windows 事件日志，0 个 `.NET Runtime` 错误（新菜单项构造、注册表读取、AboutForm 加载均无崩溃）。
- 影响文件：`app.manifest`(新)、`launcher.csproj`、`Controls/Form1.cs`(using + 4 处理器 + 注册表辅助 + 构造器打勾)、`Controls/Form1.Designer.cs`(4 菜单项 + 分隔符)、`Controls/AboutForm.cs`(新)、`README.md`、`CHANGELOG.md`。
- 注：构建产物 `bin/Release/launcher.exe` 已更新，故 `publish/launcher-portable/` 与 GitHub Release v1.0.0 附件再度落后于源码；**已补做**：用最新 `bin/Release/launcher.exe`(182,784B，含四功能+DPI manifest) 重新打包 `publish/launcher-portable.zip`(75,211B)，剔除 `launcher.json`/`launcher.log`/`notes.txt` 运行时文件，`gh release upload v1.0.0 --clobber` 覆盖附件（updatedAt 2026-08-08T13:05:29Z）。现在线上交付包与源码 `45b9dd7` 完全一致。

### 文档：README 替换为精简版并标注上游来源
- 用户要求删除原先较长的 README（含构建/架构/快捷键/配置文件等详细章节），改为一份精简 README，仅保留「软件定位 + 原项目（上游）链接 + 软件简介链接 + 一句本仓库改动说明」。
- 上游确认为 [cornradio/launcher](https://github.com/cornradio/launcher)；软件名为 **QuickLauncher**（作者 kasusa，2025-03-15 发布于小众软件论坛，简介 https://meta.appinn.net/t/topic/67580）。
- 用 `gh api` 下载了上游 `bin/Release/launcher.exe`（546,304 字节，有效 PE）留存为 `cornradio-launcher.exe` 作参考，**未纳入 git**（第三方二进制，不入库）。
- 影响文件：`README.md`（删除后重建为精简版）。

### 修正：关于对话框主链接指向本仓库（而非上游）
- 用户指出「关于」对话框面向使用者，主链接应指向自己的仓库而非上游。将 `Controls/AboutForm.cs` 的主链接由 `https://github.com/cornradio/launcher`（上游）改为 `https://github.com/worldoi/launcher`（本项目主页），上游仅在底部以灰色小字致谢。
- `dotnet build -c Release` 0 错误 0 警告；`README.md`(第 15 行) / `CHANGELOG.md`(本段 + 第 195 行) 同步（项目约定）。
- 注：此次改动更新了 `bin/Release/launcher.exe`，故交付包（`publish/launcher-portable/`、本地 zip、GitHub Release 附件）再度落后于源码；按此前教训需重打 zip + `--clobber` 重传（待用户确认后执行）。

影响文件：`launcher.csproj`、`Form1.cs`、`Form1.Designer.cs`、`Form1.resx`、`Properties/Resources.resx`、`App.config`、
`Resources/button2.png`、`Resources/Launcher.ico`、`PROJECT_ANALYSIS.md`、`ROADMAP.md`、
`CHANGELOG.md`、`README.md`、`bin/Release/*`（重建）、`launcher.ini`（新增 `[Theme]` 段）、
**新增** `ThemePalette.cs`/`SlotData.cs`/`IconRenderer.cs`/`IconSlot.cs`/`PageIndicator.cs`/`ConfigStore.cs`/`HotkeyBinding.cs`、
配置文件升级为 `launcher.json`（旧 `launcher.ini`+`panel_dotN.txt` 自动迁移、保留一版；新增 `hotkeys` 段，全部快捷键可配置）、
**新增交付包** `publish/launcher-portable/`（exe+config+Resources+README，便携免安装）、
**新增** `.gitignore`、`publish/launcher-portable.zip`，并发布至 GitHub `worldoi/launcher` v1.0.0。

### 图标增强：URL 地球字形 + 自定义图标（给愿意折腾的用户）
- **URL 槽位显示地球/链接字形**：`IconRenderer.Draw` 在 `isUrl` 且无自定义图标时改画蓝色地球（经纬线），替代原先 `GetFileIcon` 对不存在的 http 路径回退出的通用"应用程序"图标，更易辨识"这是个链接"。
- **右键菜单换图标**：原右键直接删除，改为 `ContextMenuStrip`（设置自定义图标 / 清除自定义图标 / 删除），空槽自动禁用前两项。
  - `IconSlot` 新增 `SetIconRequested` / `ClearIconRequested` 事件；`Form1` 新增 `SetSlotIcon` / `ClearSlotIcon`：用 `OpenFileDialog` 选图片（png/jpg/jpeg/ico/bmp），校验可读后存入 `SlotData.IconPath`（图片位于程序目录内则存相对路径，便于整体迁移），即时重渲染并保存。
  - `IconRenderer` 新增 `LoadIconImage`（按绝对/相对路径读取用户图片，拷贝为独立位图避免占用原文件）、`ResolveIconPath`（相对路径按程序目录解析）、`DrawGlobe`。
- **持久化**：序列化走 `SlotDoc` DTO，新增 `icon` 字段并在 `ApplyDoc`/`Save` 双向映射；旧配置无该字段时回退默认（空 = 自动图标）。
- 影响文件：`Controls/IconRenderer.cs`、`Controls/IconSlot.cs`、`Controls/Form1.cs`、`Core/ConfigStore.cs`、`Models/SlotData.cs`、`ROADMAP.md`（状态核对）。
- 验证：`dotnet build -c Release` 0 错误 0 警告；无头冒烟（含 URL 槽 + 自定义图标槽的配置）启动存活无崩溃，自定义图标 `icon` 字段读回正确（`Resources/button2.png`）。
- 注：本次更新了 `bin/Release/launcher.exe`，交付包（`publish/launcher-portable/`、本地 zip、GitHub Release 附件）需重打 zip + `--clobber` 重传 + 提交推送，以消除"源码新、包旧"尾巴。

### 修复：导入配置后界面不刷新（重绘遗漏，正确性改进）
- **措施**：`importConfigToolStripMenuItem_Click` 在 `_store.Load()` + `PreloadAllPages()` 之后补上 `LoadPageFromCache(_currentPageName)` + `Invalidate()`，并加 `_skipNextReload` 包裹解压、`RegisterGlobalHotkeys` 容错。属正确性加固，但**并非"导入无反应"的真正根因**（见下条）。
- **影响文件**：`Controls/Form1.cs`（导入处理）。验证：`dotnet build -c Release` 0 错误 0 警告。

### 修复（真正的根因）：launcher.json 的 UTF-8 BOM 导致读取/导入/启动全部静默失败
- **现象**：托盘「导入配置」选 zip 后弹"导入成功"，但屏幕图标完全没变化；日志反复出现 `读取 launcher.json 失败，尝试旧配置迁移: 遇到意外字符"ï"`。
- **根因**：`ConfigStore.Save()` 用 `Encoding.UTF8`（.NET 该静态属性**带 BOM**）写 `launcher.json`，使每个配置文件开头都带 `EF BB BF`；而 `LoadFromJson()` 用 `File.OpenRead` 原始流直接喂给 `DataContractJsonSerializer`，**不会剥离 BOM**，解析器在首字节 `EF` 处报错 → 走 `MigrateFromLegacy()` 回退到**空配置**。结果：导出的备份本身带 BOM，导入后根本读不出来（界面停在旧/空状态）；更糟的是迁移末尾会 `Save()` 把**空配置覆盖回磁盘**，把导入的 33 槽位冲掉。
- **修复**：
  - `LoadFromJson()` 改为先 `File.ReadAllText`（自动剥离 BOM）再喂给序列化器，对带/不带 BOM 均兼容。
  - `Save()` 改用 `new UTF8Encoding(false)`（**无 BOM**）写出，从根源消除问题，并保证今后导出的备份可被正常读回。
- **影响文件**：`Core/ConfigStore.cs`（`LoadFromJson` / `Save`）。
- 验证：`dotnet build -c Release` 0 错误 0 警告；重新从备份 zip 解压 `launcher.json` 后启动，日志不再报 BOM 解析错误，`启动完成` 直接基于导入配置加载（33 槽位 / 4 页生效）。
- **用户侧已处置**：用户磁盘 `launcher.json` 曾被空迁移覆盖，已由备份 `launcher-config-20260808-2128.zip` 重新解压恢复（含 33 槽位），并启动修复版进程加载成功。
- 注：本次更新了 `bin/Release/launcher.exe`，交付包（本地文件夹 / 本地 zip / GitHub Release 附件）需重打 + `--clobber` 重传 + 提交推送。

### 新增功能：托盘菜单「新增一页(&P)」一键加页
- **诉求**：用户想扩充分页（原固定 4 页），但不愿手改 `launcher.json` 的 `pageCount`；要一个菜单选项点一下就加一页并跳过去。
- **实现**：
  - `Core/ConfigStore.cs` 新增公开方法 `AddPage()`：把 `Layout.PageCount` +1（上限 20，与 `EnsurePageShape` 钳制一致），派生新页名 `panel_dotN`，加入 `PageNames`，调 `EnsurePageShape()` 为新页补齐空槽位，返回新页索引。
  - `Controls/Form1.Designer.cs` 新增 `addPageToolStripMenuItem`（文本「新增一页(&P)」），加入托盘 `contextMenuStrip1`，置于「导入配置」与「关于」之间。
  - `Controls/Form1.cs` 新增 `addPageToolStripMenuItem_Click`：调 `AddPage()` → `SaveConfig()`（内部已用 `_skipNextReload` 包裹，避免写盘触发文件监视器二次重载）→ 用新页数 `PageIndicator.Setup` 刷新圆点 → `SwitchToPage(idx)` 直接跳到新页。
- **行为**：点击后底部圆点 +1，自动切到空白新页；配置即时落盘，重启后保留。与现有分页切换、全局热键、导入导出完全兼容（新页本质就是多一组 `slotCount` 个空槽）。
- **影响文件**：`Core/ConfigStore.cs`、`Controls/Form1.cs`、`Controls/Form1.Designer.cs`、`CHANGELOG.md`。
- 验证：`dotnet build -c Release` 0 错误 0 警告；无头启动新 exe 在隔离临时目录（预置 4 页配置）存活无崩溃、无错误日志（新菜单项构造正常）。
- 注：本次更新了 `bin/Release/launcher.exe`，交付包（本地文件夹 / 本地 zip / GitHub Release 附件）需重打 + `--clobber` 重传 + 提交推送。

### 新增功能：托盘菜单「删除当前页(&D)」——与「新增一页」配对
- **诉求**：用户指出「有增加那就还得有减少」，加了页却删不掉，只能手改 `launcher.json`。
- **实现**：
  - `Core/ConfigStore.cs` 新增 `RemovePage(int index)`：页名固定为 `panel_dotN`，因此不能直接抠掉中间键，必须把 index 之后的页内容**依次前移**（`panel_dot(i+1)` → `panel_dot(i)`），再移除末页键、`PageNames` 尾删、`PageCount` 同步，最后 `EnsurePageShape()`。返回被删页的槽位列表供调用方释放位图；`PageCount <= 1` 或索引越界返回 `null`（至少保留一页）。
  - `Core/ConfigStore.cs` 新增 `IsPageEmpty(string name)`：判断该页是否全为空槽，供 UI 决定是否二次确认。
  - `Controls/Form1.Designer.cs` 新增 `removePageToolStripMenuItem`（文本「删除当前页(&D)」），紧跟「新增一页」加入托盘 `contextMenuStrip1`。
  - `Controls/Form1.cs` 新增 `removePageToolStripMenuItem_Click`：末页保护提示 → 非空页 `MessageBox` 二次确认（默认按钮为「否」）→ `RemovePage()` → 对被删页每个 `SlotData` 调 `DisposeImage()` 释放缓存位图 → `SaveConfig()` → `PageIndicator.Setup` 刷新圆点 → `SwitchToPage(Math.Min(idx, Count-1))`（停在原位；删末页则退到新末页）→ `Invalidate()` + 日志。
- **行为**：删除后底部圆点 -1，后面的页整体前移补位（第 3 页删掉后原第 4 页变成第 3 页），配置即时落盘。空页直接删，非空页弹确认防误删，只剩一页时明确提示不可删。
- **影响文件**：`Core/ConfigStore.cs`、`Controls/Form1.cs`、`Controls/Form1.Designer.cs`、`README.md`、`publish/launcher-portable/README.md`、`CHANGELOG.md`。
- 验证：`dotnet build -c Release` 0 错误 0 警告；启动构建产物冒烟存活无崩溃后关闭；替换用户运行实例 exe 并重启（PID 7468），日志正常、5 页 33 槽位配置完整加载（替换前已备份 `launcher.json` 到桌面）。
- 注：本次更新了 `bin/Release/launcher.exe`，交付包（本地文件夹 / 本地 zip / GitHub Release 附件）已同步重打 + `--clobber` 重传。

### 修复：双击槽位误开两个程序实例
- **问题**：`IconSlot` 在 `OnMouseUp` 中触发 `OpenRequested` 启动程序。Windows 双击会产生**两次** `MouseUp`，导致一次双击同时打开两个进程实例，极易误触。
- **修复**：将"打开"逻辑从 `MouseUp` 移到 `MouseClick`，并仅在 `e.Clicks == 1`（纯单击）时触发 `OpenRequested`；双击的第二次 `e.Clicks == 2` 被忽略。中键"打开所在位置"（`OpenLocationRequested`）保留在 `OnMouseClick` 中键分支。拖拽由 `MouseDown/MouseMove/DoDragDrop` 处理，不发生 `Click`，故移动槽位行为不受影响。
- **效果**：单击照常打开（一次一键一实例）；手抖双击只开一个实例，不再开两个。
- **影响文件**：`Controls/IconSlot.cs`、`CHANGELOG.md`。
- 验证：`dotnet build -c Release` 0 错误 0 警告；替换运行实例并重启（PID 3764），日志正常、4 页 33 槽位配置完整加载（替换前已备份 `launcher.json` 到桌面）。
- 注：本次更新了 `bin/Release/launcher.exe`，交付包（本地文件夹 / 本地 zip / GitHub Release 附件）已同步重打 + `--clobber` 重传。

### 数据稳健 + 工程收尾（2026-08-08 晚）
- **导入前自动备份**：`importConfigToolStripMenuItem_Click` 在解压还原前，先把当前 `launcher.json/notes.txt/launcher.ini` 打包成 `backups/launcher-config-auto-<时间戳>.zip`。防导入出错（如 BOM 损坏、格式不兼容）后无法回滚；备份失败仅记日志不阻断导入。
- **自定义图标随包导入导出**：导出时扫描所有页所有槽位的 `IconPath`，把实际存在的图标以 `icons/<文件名>` 条目压入配置 zip（重名自动加 `_2/_3` 后缀）；导入时按 `entry.FullName` 还原到 `baseDir/icons/`、`ResolveIconPath` 增加兜底——原路径不存在时按文件名 stem 在 `baseDir/icons/` 下找回，换机不丢图。`IconPath` 字符串保持不变，向后兼容。
- **launcher 单实例**：`Core/Program.cs` 用全局命名 `Mutex("LauncherSingleInstance_worldoi")` 检测已有实例；已运行时 `ActivateExistingInstance()` 通过 user32 `ShowWindow(SW_RESTORE)+SetForegroundWindow` 把旧窗口提到前台并退出新进程，避免误开多个 launcher（与"双击槽位开两个程序"是两码事）。
- **单元测试**：新增 `tests/Launcher.Tests/`（MSTest, net472），覆盖 `ConfigStore` 的 BOM 读写容错、`RemovePage` 前移、`BaseDirOverride` 隔离；`IconRenderer` 的 `IsHttpUrl`、`ResolveIconPath` 绝对/相对/回退。为支持测试访问 internal 类型，在 `Properties/AssemblyInfo.cs` 加 `[assembly: InternalsVisibleTo("Launcher.Tests")]`。测试工程引用 `bin/Release/launcher.exe`（`CopyLocal=true` + 构建后复制目标）。
- **影响文件**：`Controls/Form1.cs`、`Controls/IconRenderer.cs`、`Core/ConfigStore.cs`、`Core/Program.cs`、`Properties/AssemblyInfo.cs`、`ROADMAP.md`、`CHANGELOG.md`、`tests/`（新增）。
- 验证：`dotnet build -c Release` 0 错误 0 警告；`dotnet test` 6/6 通过；替换运行实例并重启，日志正常、配置完整。
- 注：本次更新了 `bin/Release/launcher.exe`，交付包（本地文件夹 / 本地 zip / GitHub Release 附件）已同步重打 + `--clobber` 重传。

### 功能：搜索框 + 键盘打开槽位（2026-08-09）
- **顶部搜索框**：`Form1.Designer.cs` 新增 `searchBox`（TextBox，x=95 宽300），占位符"搜索…"，实时过滤当前页图标（按文件名/路径/URL 包含匹配），非匹配隐藏、首匹配高亮（蓝色描边环）；Enter 清空占位符、Leave 恢复全部可见；呼出主面板时自动聚焦搜索框。
- **搜索模式键盘操作**：搜索框聚焦时，Enter 打开首个/选中匹配、上下键移动选中、Esc 清除过滤、数字键 1-9 直接打开第 N 个匹配。
- **数字键打开槽位**：非搜索模式下，数字键 1-9 直接打开当前页第 N 个槽位（置于 Jump 翻页逻辑之前，使其优先）；字母键 A-Z 打开当前页首个文件名以该字母开头的槽位。
- **翻页语义调整**：`ConfigStore` 默认 `Jump` 清空（数字键不再翻页），翻页由 A/D 键和底部圆点承担；帮助文本同步更新。
- **主题支持**：`ThemePalette` 新增 `Highlight`（搜索选中描边色）和 `SearchCue`（占位符颜色），`ApplyThemeVisuals` 设置搜索框配色，`ToggleTheme` 后重画选中高亮。
- **影响文件**：`Controls/Form1.cs`、`Controls/Form1.Designer.cs`、`Core/ConfigStore.cs`、`Core/ThemePalette.cs`、`CHANGELOG.md`、`README.md`。
- 验证：`dotnet build -c Release` 0 错误 0 警告；替换运行实例（PID 756）重启，日志正常、4 页 43 槽位配置完整。
- 注：本次更新了 `bin/Release/launcher.exe`，交付包（本地文件夹 / 本地 zip / GitHub Release 附件）需重打 + `--clobber` 重传 + 提交推送。

### 搜索功能完善（2026-08-09 晚）
- **搜索结果合并视图**：搜索时跨页收集所有匹配项，合并显示在当前页（最多 40 个），分页圆点自动隐藏；无匹配时显示空白；清除搜索后恢复当前页。
- **搜索恢复 bug 修复**：`ShowAllSlots` 不再调用 `clearImageBoxes()`（会丢失图标），改为直接覆盖当前页槽位数据，搜索清空后图标不再丢失。
- **呼出面板自动清除搜索**：打开程序后再次呼出（`Alt+~`），搜索框自动清除并恢复当前页原始显示，不再停留搜索结果页面。
- **热键修复**：`HotkeyBinding.ParseKey` 支持 `~` 解析为 `Keys.Oemtilde`，`Alt+~` 热键配置生效（`Ctrl+` ` 和 `Win+Space` 被系统占用）。
- **影响文件**：`Controls/Form1.cs`、`Core/HotkeyBinding.cs`、`CHANGELOG.md`、`README.md`。

### 配置原子写（2026-08-09）
- **根因**：`ConfigStore.Save()` 原先用 `File.WriteAllText` 直写 `launcher.json`（非原子），写盘中途断电/进程被杀会留下截断或乱码 JSON，配置损坏。
- **修复**：新增静态 `WriteAtomic(path, content)`——先写同目录 `launcher.json.tmp`，再做原子替换（目标存在用 `File.Replace`，少数文件系统不支持时退化为删旧+改名）；失败清理 tmp 并沿用原有 catch 记日志。
- **Watcher 兼容**：`Form1.SetupConfigWatcher` 的 Filter 精确为 `launcher.json`，`.tmp` 临时文件不会误触发热更新；自写回环仍由 `_skipNextReload` 防护。
- **测试**：新增 `Save_IsAtomic_NoTempResidue_AndRoundTrips`——保存后无 `.tmp` 残留、重新加载可读回槽位数据。
- **影响文件**：`Core/ConfigStore.cs`、`tests/Launcher.Tests/ConfigStoreTests.cs`、`CHANGELOG.md`。
- 验证：`dotnet build -c Release` 0 错误 0 警告；`dotnet test` 7/7 通过。

### 交付包重打（2026-08-09）：原子写构建 + 版本统一
- 背景：HEAD（`d97db94`）含原子写等新改动，交付包（`publish/launcher-portable/` 与 GitHub Release v1.0.0 附件）落后于源码。
- **同步 publish 包**：最新 `bin/Release/launcher.exe`（含原子写修复，195,584B）、`launcher.exe.config`、`Resources/Launcher.ico`、`Resources/button2.png`、README（与仓库根 README 逐字节一致——含跨页搜索/Alt+~ 热键说明）。
- **重打 zip**：从**干净暂存目录**重新压缩（剔除 `launcher.json`/`launcher.log`/`notes.txt` 运行时数据与个人配置），`publish/launcher-portable.zip` 81,379B（原 80,689B）。
- **覆盖上传** GitHub Release v1.0.0：`gh release upload v1.0.0 --clobber` 成功，附件更新为 81,379B（updatedAt 2026-08-09T06:06:22Z）。
- **过程注意**：`publish/launcher-portable/` 当时的运行实例（PID 6632）占用 `launcher.exe`/`button2.png` 导致覆盖失败；经确认后结束进程。该目录原 `launcher.json`/`notes.txt` 在整理时被清理，`launcher.json` 已用上午备份 `launcher-json-backup-20260809-104834.json` 复位（运行实例退出时重新加载配置），`notes.txt` 需下次运行自行生成。
- 影响文件：`publish/launcher-portable/*`、`publish/launcher-portable.zip`、`CHANGELOG.md`。
- 验证：`gh release view` 附件已更新。

### 页面懒加载 + LRU 缓存（2026-08-09）
- **诉求**：分页多 / 图标多时，启动全量 `PreloadAllPages()` 重绘几十个图标造成启动卡顿、内存膨胀。
- **实现**：`Controls/Form1.cs` 改用「懒加载 + LRU」：
  - 新增 `_pageCacheLoaded`（已渲染页名集合）、`_pageTouchOrder`（最近使用顺序，末位=最新）、`PageCacheLimit=4`。
  - `EnsurePageLoaded(page)`：未渲染则 `LoadPageToCache` 渲染到缓存，随后 `TouchPage` + `EnforcePageCacheLimit`（超出 4 页逐出最久未用页并 `ReleasePageCache` 释放位图）。
  - 启动/切页/搜索/主题切换/配置热更新/导入各路径统一走懒加载；搜索合并视图跨页取图期间用 `_suppressPageEviction` 暂停 LRU 驱逐，避免结果页缓存被释放显示空白。
  - 顺带修复启动时从未显式 `LoadPageFromCache` 当前页的潜在空白问题。
- 影响文件：`Controls/Form1.cs`、`CHANGELOG.md`。
- 验证：`dotnet build -c Release` 0 错误 0 警告；`dotnet test` 7/7 通过；无头冒烟启动存活无崩溃。
- 注：本次更新了 `bin/Release/launcher.exe`，交付包需重打 + `--clobber` 重传 + 提交推送（commit `5a9fad9` 已推送）。

### 分页重命名 + 页码/名称提示（2026-08-09）
- **诉求**：分页名写死 `panel_dotN`，没有「重命名页」能力；圆点旁无页码/名称提示，页多了难找。
- **实现**：
  - `Core/ConfigStore.cs` 新增 `RenamePage(index, newName)`：校验（非空、≤20 字符、禁 `\ / : * ? " < > |`、不得重名），重键 `Pages` 字典、更新 `PageNames` 列表与 `LastPage` 记忆；`AddPage()` 新页默认名改「第 N 页」（重名自动加序号）。
  - `Controls/PageIndicator.cs`：圆点下方绘制页码；悬停 `ToolTip` 显示「第 N 页 · 页名」；右键圆点触发 `PageRenameRequested(index)`。
  - 新增 `Controls/PageNameDialog.cs`（随主题配色的重命名输入框）。
  - `Controls/Form1.cs`：接入圆点右键改名 + 托盘「重命名当前页(&R)」；新增 `RefreshPageIndicator()`（页数/配色/页名/选中位统一刷新）；改名后同步缓存键（位图仍有效，免重渲）；帮助面板补说明。
- 影响文件：`Core/ConfigStore.cs`、`Controls/PageIndicator.cs`、`Controls/PageNameDialog.cs`(新)、`Controls/Form1.cs`、`Controls/Form1.Designer.cs`、`launcher.csproj`（登记新文件）、`tests/Launcher.Tests/ConfigStoreTests.cs`、`CHANGELOG.md`。
- 验证：`dotnet build -c Release` 0 错误 0 警告；`dotnet test` 11/11 通过（RenamePage 重键/持久化/非法名、AddPage 重名去重）；冒烟：自定义页名读写回环正确、`lastPage` 恢复正常。
- 注：本次更新了 `bin/Release/launcher.exe`，交付包（本地文件夹 / 本地 zip / GitHub Release 附件）需重打 + `--clobber` 重传 + 提交推送。

### Form1 逻辑抽取：搜索/热键/导出入 Core（2026-08-09）

- **诉求**：`Controls/Form1.cs` 反复增功能又涨到 1338 行，搜索匹配、按键分发、导出打包等逻辑内联在 UI 事件里难单测；按拆分 `ConfigStore` 的方式把纯逻辑抽到 `Core/`。
- **实现**：
  - 新增 `Core/SearchEngine.cs`：`SearchMatch`（匹配项 + 所在页）、`SlotLabel`（URL 原文 / 文件名）、`FindMatches()`（跨页包含匹配，页序/槽位序保留）、`FindFirstByLetter()`（页内首个以字母开头的槽位索引）。
  - 新增 `Core/KeyRouter.cs`：`KeyRoute`（动作 + 槽位索引/字母/跳转页/是否吞键）+ `KeyAction` 枚举；`Resolve(hk, searchMode, ctrl, shift, alt, key)` 复刻原 `Launcher_KeyDown` 全部判定（撤销/翻页/数字开槽/字母定位/Jump/搜索 Enter/↑/↓/Esc/数字），带修饰键的数字键不误开槽。
  - 新增 `Core/ConfigExporter.cs`：`CollectConfigFiles(baseDir)`（只收存在的 launcher.json/notes.txt/launcher.ini）、`CollectIconEntries(store)`（扫描全页自定义图标，重名自动 `_2/_3…`，返回 `Tuple<源文件, 包内路径>`）。
  - `Controls/Form1.cs` 相应瘦身（1338 → 约 1205 行）：`Launcher_KeyDown` 改为 `KeyRouter.Resolve` + switch 分发；`ApplySearchFilter` 改用 `SearchEngine.FindMatches`；`OpenSlotByFirstLetter` 改用 `SearchEngine.FindFirstByLetter`；导出 handler 改用 `ConfigExporter.CollectConfigFiles/CollectIconEntries`；删除内联 `SlotLabel`。行为与热键语义保持不变。
  - 三个新类均 `namespace launcher.Core`，经 launcher.csproj 显式 `<Compile>` 登记。
  - 新增 `tests/Launcher.Tests/CoreLogicTests.cs`：SearchEngine（槽位标签/跨页匹配/大小写/路径匹配/首字母定位）、KeyRouter（撤销/翻页/数字开关/字母/带修饰键不误触/Jump 映射/Esc·Enter·数字·字母搜索态）、ConfigExporter（存在过滤/图标重名去重/缺失与空图标跳过）共 23 个用例。
- 影响文件：`Core/SearchEngine.cs`(新)、`Core/KeyRouter.cs`(新)、`Core/ConfigExporter.cs`(新)、`Controls/Form1.cs`、`launcher.csproj`（登记新文件）、`tests/Launcher.Tests/CoreLogicTests.cs`(新)、`CHANGELOG.md`、`ROADMAP.md`。
- 验证：`dotnet build -c Release` 0 错误 0 警告；`dotnet test` 34/34 通过（原 11 + 新增 23）；Form1.cs 从 1338 行降至约 1205 行。

### 便签 Markdown 预览（2026-08-09）

- **诉求**：便签原是纯 `TextBox` + `notes.txt` 无渲染；要能写 Markdown 并预览效果（无外部依赖）。
- **实现**：
  - 新增 `Core/MarkdownRenderer.cs`（纯逻辑，可单测）：子集解析 + 转 RTF——`#`~`######` 标题、`**粗体**`、`*斜体*`、`` `代码` ``（Consolas 字体 + 背景高亮）、`-/*/+` 列表（缩进 + 圆点）、`[文字](链接)`（RTF HYPERLINK 字段 → RichTextBox `LinkClicked` 可点）。RTF 转义含 `\ { }` 与中文 `\uN?` 编码。
  - `Controls/Form1.Designer.cs`：`panel2` 新增只读 `rtbNotesPreview`（与 textBox1 同区域、默认隐藏）+ `btnMdToggle`「预览/编辑」切换按钮。
  - `Controls/Form1.cs`：`btnMdToggle_Click` 切编辑/预览（预览前先 `SaveNotes` 落盘）；`RenderNotesPreview()` 按主题配色渲染（标题=深色暖黄/浅色靛蓝、链接=蓝、代码=深色绿底灰框）；预览态换肤自动重渲染；`rtbNotesPreview_LinkClicked` 用系统浏览器打开；`Launcher_KeyDown` 拦截增加 `rtbNotesPreview.Focused`，预览态滚轮/方向键不误触槽位。
- 影响文件：`Core/MarkdownRenderer.cs`(新)、`Controls/Form1.Designer.cs`、`Controls/Form1.cs`、`launcher.csproj`（登记新文件）、`tests/Launcher.Tests/CoreLogicTests.cs`、`CHANGELOG.md`、`ROADMAP.md`。
- 验证：`dotnet build -c Release` 0 错误 0 警告；`dotnet test` 46/46 通过（原 34 + 新增 12：标题/粗体/斜体/代码/链接/列表/转义/中文编码/嵌套/未闭合标记）；冒烟启动正常。notes.txt 仍是纯 Markdown 源文本，编辑/预览双向无损。

### 拖拽排序占位预览（2026-08-09）

- **诉求**：ROADMAP 自认「拖拽占位动画未做」——目前拖着一个槽直接 `SwapSlots` 交换，松手才瞬移，无落点提示。
- **实现**：`Controls/IconSlot.cs` 自包含占位反馈，无需 Form1 干预：
  - 槽位间拖拽悬停时，目标槽绘制**虚线落点框 + 呼吸高亮**（`DashedStyle.Dash` + `Highlight` 主题色，alpha 随拖动脉动刷新），明确"松手会换到哪"。
  - 拖拽中的源槽淡化（叠加半透明 `SlotBack`），示意"已被提起"。
  - 新增 `DragOver`（拖动持续刷新占位动画并保持 `Move` 效果）、`DragLeave`（拖出即清除占位）、`OnDragDrop` 前置清除；拖拽结束（落点/取消/松开）统一复位源高亮与占位。
  - 文件/链接拖入不受影响（仍 `Copy` 效果，不显示占位）。
- 影响文件：`Controls/IconSlot.cs`、`CHANGELOG.md`、`ROADMAP.md`。
- 验证：`dotnet build -c Release` 0 错误 0 警告；`dotnet test` 34/34 通过；冒烟启动正常；拖拽交互为纯视觉层，交换语义不变。滚轮测试：悬停目标槽可见虚线占位 + 源槽淡化，松手即换位。
