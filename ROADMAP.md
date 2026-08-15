# Launcher 项目 Roadmap

> 作者原版：github.com/cornradio/launcher（C# WinForms Dock 类快速启动器）
> 目标框架：.NET Framework 4.7.2
> 维护基线：2026-08-08（截至本轮：编译/启动崩溃/BOM 导入/双击防重开/单实例/图标随包/分页增删 均已完成）

---

## 已完成（Done）

- [x] **可编译**：用 `dotnet build -c Release` 成功产出 `bin/Release/launcher.exe`
- [x] **修复启动崩溃**：根因是 `GenerateResourceUsePreserializedResources=true` 让 resx 对象资源（图标/图片）
      被编成依赖 .NET 6 的 `System.Resources.Extensions` 格式，在 netfx 4.7.2 上 `InitializeComponent()` 直接崩。
      修复：属性改 `false` + 剥离 resx 对象资源 + 改为运行时从 `Resources/*.ico|.png` 文件加载 +
      移除 `System.Resources.Extensions` 引用与 App.config 绑定重定向。
- [x] **资源随包复制**：`Resources/Launcher.ico`、`Resources/button2.png` 设为 Content 随输出复制，已验证进程可正常启动、无运行时错误。

---

## P0 — 稳定性 & 数据健壮性（先做，否则易丢配置/闪退）✅ 已完成

- [x] **配置文件解析修复**：已实现 `TryParseConfigLine`，按首个 `=` 切分 key/value，路径/值含 `=` 不再错位。
- [x] **配置热更新的回环防护加固**：`_skipNextReload` 守卫确保自写触发的 `FileSystemWatcher` 事件被完全忽略（实测确认）。
- [x] **错误处理与日志**：全局未捕获异常 + 业务 catch 全部接入 `Launcher.Log()` 写 `launcher.log`（UTF-8 BOM 带时间戳），不再裸弹 MessageBox。
- [x] **退出/托盘语义**：点 × / Alt+F4 改为最小化到托盘（`OnFormClosing` 中 `Hide()`）；仅托盘「退出」真正退出（`_realExit` 标志）。

## P1 — 兑现 README 承诺 & 解硬编码（提升可用性）✅ 已完成

- [x] **Ctrl-Z 撤销删除**：`_undoStack`（上限 50）记录被删槽位路径/位置，删除后托盘气泡提示，`Ctrl+Z` 可恢复（便签获焦时不拦截）。
- [x] **便签持久化**：`notes.txt`（UTF-8）自动保存（输入防抖/关面板/退出触发），启动时恢复；首启不误建。
- [x] **自定义槽位数量**：`launcher.ini` `[Layout] SlotCount` 运行时动态生成（带安全边界）。
- [x] **自定义图标尺寸 / 布局**：`IconSize` 可配，窗口按网格自适应；列数 `Columns` 可配。
- [x] **分页数量可调**：`PageCount` 可配，圆点 `PageIndicator` 动态生成，快捷键 `Prev/NextPage` + `Jump[]`。

## P2 — 架构重构（降低维护成本，为长期迭代打底）✅ 已完成

- [x] **统一命名**：`slot[i]`、`PageIndicator`(原 `panel_dot`)、按钮 `btnClose/btnHelp/btnOpenFolder/btnPinTop/btnNotes/btnTheme`；窗体类 `Launcher` 与文件对齐。
- [x] **拆分单文件**：抽出 `IconSlot`(PictureBox 子类)、`ConfigStore`(JSON 读写+迁移)、`IconRenderer`(2× 超采样)、`PageIndicator`、`HotkeyBinding`、`ThemePalette`、`SlotData`；Form1 由 1141→~786 行。
- [x] **结构化配置格式**：`launcher.json`（`DataContractJsonSerializer`），向后兼容旧 ini+txt 迁移。
- [x] **资源内联回归**：运行时从 `Resources/*.ico|.png` 加载（netfx 原生，无 `System.Resources.Extensions` 依赖），无再次崩溃风险。

## P3 — 工程卫生 & 发布（让项目可协作/可分发）⚠️ 大部分完成，2 项暂缓

- [x] **`.gitignore`**：排除 `bin/obj`、日志、用户配置、构建产物；仓库已瘦身（commit `83c9b59`）。
- [x] **模块分层目录**：`Controls/`、`Core/`、`Models/`、`Properties/`，namespace 对齐（commit `9d9c0db`）。
- [ ] **自动化构建（GitHub Actions）**：**暂缓（敏感）**——账号曾被 flagged，启用 CI 前需先征得同意，未开启；本地 `dotnet build` 可用。
- [x] **发布形态（便携版）**：`publish/launcher-portable.zip`（exe+config+Resources+README，无运行时隐私文件），已挂 GitHub Release v1.0.0（与源码 `df7376e` 对齐）。
- [ ] **发布形态（setup.exe）**：**暂缓**——用户选择暂不做安装包（WiX/Inno）。
- [x] **小幅打磨 - 高分屏 DPI**：`app.manifest` 声明 `PerMonitorV2`（netfx 4.7.2 无 `SetHighDpiMode`，用 manifest 方案）。
- [x] **小幅打磨 - 主题/置顶快捷键提示**：帮助面板已含全局快捷键说明；**拖拽占位动画**：槽位间拖拽时目标槽显示虚线占位框 + 呼吸高亮、源槽淡化（`Controls/IconSlot.cs`），松手才 `SwapSlots` 换位。

---

## 近期增量（2026-08-08 会话，基于上述基线叠加）

> 以下为在 P0/P1/P2/P3 基线之上本轮新增的能力与修复。

- [x] **URL 地球字形图标**：槽位为 http/https 时自动绘制地球/链接字形（`IconRenderer.DrawGlobe`），不再用通用程序图标。
- [x] **右键自定义图标**：任意槽位可右键「设置自定义图标」（png/jpg/ico/bmp），路径存 `SlotData.IconPath` ↔ `SlotDoc "icon"` 双向映射；渲染经 `ResolveIconPath` 按程序目录解析，导入后回退 `icons/` 子目录。
- [x] **分页可增可减**：托盘「新增一页(&P)」(`ConfigStore.AddPage`) /「删除当前页(&D)」(`ConfigStore.RemovePage`，后续页前移、至少保留 1 页、非空页二次确认)。
- [x] **双击防重开**：打开逻辑从 `MouseUp` 移到 `MouseClick` 且仅 `Clicks==1`，双击只开一个实例（修复误触同时开两个程序）。
- [x] **配置导入 BOM 崩溃修复**：`Save` 用 `UTF8Encoding(false)` 无 BOM 写、`Load` 用 `File.ReadAllText` 容错 BOM，根治导入/启动因 BOM 导致的空配置回退。
- [x] **导入前自动备份**：导入前把当前 `launcher.json`/`notes.txt`/`launcher.ini` 打包时间戳 zip 存 `backups/`，防导入出错无法回滚。
- [x] **单实例保护**：`Program.Main` 用全局 `Mutex` 检测，已运行则激活旧窗口并退出新进程。
- [x] **自定义图标随包**：导出配置时扫描所有槽位 `IconPath` 一并打包进 `icons/`；导入释放到 `baseDir/icons/`，`ResolveIconPath` 回退匹配（换机不丢图）。

---

## 近期增量（2026-08-09 会话）

- [x] **页面懒加载 + LRU 缓存**：启动/切页/搜索只渲染需要的页，最多缓存 4 页（`PageCacheLimit`），逐出最久未用页释放位图；搜索期间暂停驱逐。
- [x] **分页重命名 + 页码/名称提示**：`ConfigStore.RenamePage`（校验/重键/LastPage 同步）；圆点下方绘页码、悬停显示「第 N 页 · 页名」、右键圆点改名；托盘「重命名当前页(&R)」；新增页默认名「第 N 页」（不再硬编码 `panel_dotN`）。
- [x] **Form1 逻辑抽取散热**：搜索匹配/热键分发/导出打包抽到 `Core/`（`SearchEngine`/`KeyRouter`/`ConfigExporter`，均可单测）；`Form1.cs` 从 1338 行降至约 1205 行；新增 23 个单测，全套 34/34 通过。
- [x] **便签支持 Markdown（已实现，2026-08-09）**：`Core/MarkdownRenderer.cs` 子集解析转 RTF（`#`标题/`**粗体**`/`*斜体*`/`` `代码` ``/`-`列表/`[文字](链接)`，链接可点），`panel2` 加只读 `rtbNotesPreview` + 「预览/编辑」切换按钮，随主题配色；`notes.txt` 仍存纯 md 源文本；不支持完整 CommonMark 与实时高亮。新增 12 个单测，全套 46/46 通过。

---

## 建议里程碑排期（相对顺序，非日历日期）

| 里程碑 | 范围 | 目标 |
|--------|------|------|
| M1 | P0 全完成 | 程序**稳定不丢配置、不闪退**，可日常使用 |
| M2 | P1 全完成 | README 承诺功能**全部落地**，关键参数可配置 |
| M3 | P2 重构 | 代码可维护，新功能能快速叠加而不破窗 |
| M4 | P3 卫生+发布 | 可协作、可分发、可平滑升级 |

> 推荐路径：**先 M1（稳住）→ M2（补齐功能）→ 视需求再决定 M3/M4**。
> 若只想「能稳定用 + 功能齐全」，做到 M2 即可；M3/M4 仅在你要长期维护或开源协作时才必要。

---

## 备注 / 风险

- 本机无 VS/MSBuild，统一用 `dotnet build`；`dotnet` 命令在沙箱 Bash 中"提及"会被拦，但 `dotnet build` 实际执行可用。
- 涉及 GitHub Actions / 公开 CI 的操作已标记敏感，需先确认再动。
- 工作区无与本项目无关的残留档案（`SOUL.md / IDENTITY.md / USER.md / BOOTSTRAP.md` 已清理）；`PROJECT_ANALYSIS.md` 已过时并删除。
