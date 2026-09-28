using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using launcher.Models;

namespace launcher.Core
{
    [DataContract]
    public class LayoutConfig
    {
        [DataMember(Name = "slotCount")] public int SlotCount = 40;
        [DataMember(Name = "iconSize")] public int IconSize = 54;
        [DataMember(Name = "pageCount")] public int PageCount = 4;
        [DataMember(Name = "columns")] public int Columns = 8;
        // 自定义窗口尺寸（像素）；0=自动按 列数×图标大小 计算。用户在拖拽调整窗口大小后自动写回。
        [DataMember(Name = "width", EmitDefaultValue = false)] public int Width = 0;
        [DataMember(Name = "height", EmitDefaultValue = false)] public int Height = 0;
        // 失焦（点击窗口外）时是否自动隐藏主面板；置顶(钉住)模式始终不隐藏。
        // 用 bool? + EmitDefaultValue=false：缺省(未写)=true 自动隐藏；显式 false=不隐藏。
        [DataMember(Name = "autoHide", EmitDefaultValue = false)] public bool? AutoHide = null;
        public bool AutoHideEffective => AutoHide ?? true;
        // 双击桌面空白处或任务栏空白处时呼出主面板（全局鼠标钩子监听）。
        // bool? + EmitDefaultValue=false：缺省(未写)=true 启用；显式 false=关闭。
        [DataMember(Name = "dblClickShow", EmitDefaultValue = false)] public bool? DblClickShow = null;
        public bool DblClickShowEffective => DblClickShow ?? true;
        // 主面板不透明度（百分比，100=完全不透明）。<100 会把窗口变成 WS_EX_LAYERED 分层窗口，
        // 子控件重绘要经 DWM alpha 合成、鼠标扫过图标网格时略卡；性能优先建议保持 100。
        [DataMember(Name = "opacity", EmitDefaultValue = false)] public int Opacity = 100;
        // 贴边自动隐藏：面板拖到屏幕左/右/上边缘停稳后自动缩进去只留一条边，鼠标移上去再滑出来。
        // bool? + EmitDefaultValue=false：缺省(未写)=false 不启用；显式 true=启用。
        [DataMember(Name = "edgeAutoHide", EmitDefaultValue = false)] public bool? EdgeAutoHide = null;
        public bool EdgeAutoHideEffective => EdgeAutoHide ?? false;
        // 搜索时是否顺带做 Everything 全盘搜索（把本地没建图标、但磁盘上存在的文件也搜出来）。
        // 默认开，但 Everything 是「第一次真的用到搜索时才拉起」，不影响启动速度。
        // bool? + EmitDefaultValue=false：缺省(未写)=true 启用；显式 false=关闭。
        [DataMember(Name = "everythingSearch", EmitDefaultValue = false)] public bool? EverythingSearch = null;
        public bool EverythingSearchEffective => EverythingSearch ?? true;
    }

    // 快捷键绑定：全部可在 launcher.json 的 hotkeys 段自定义（组合串形如 "Ctrl+Shift+T"、"Ctrl+`"、"A"、"1"）
    [DataContract]
    public class HotkeyConfig
    {
        [DataMember(Name = "main")] public string Main = "Alt+`";
        [DataMember(Name = "notes")] public string Notes = "Ctrl+Shift+N";
        [DataMember(Name = "theme")] public string Theme = "Ctrl+Shift+T";
        [DataMember(Name = "undo")] public string Undo = "Ctrl+Z";
        [DataMember(Name = "prevPage")] public string PrevPage = "A";
        [DataMember(Name = "nextPage")] public string NextPage = "D";
        // 注：默认不再用数字键翻页（数字键 1-9 现用于"直接打开当前页第 N 个槽位"，见 Form1.Launcher_KeyDown）；
        // 翻页由 A/D（上一页/下一页）与底部圆点承担。如需数字键翻页，可自行在 launcher.json 的 hotkeys.jump 配置。
        [DataMember(Name = "jump")] public List<string> Jump = new List<string>();
    }

    [DataContract]
    public class SlotDoc
    {
        [DataMember(Name = "index")] public int Index;
        [DataMember(Name = "path")] public string Path;
        [DataMember(Name = "isFolder")] public bool IsFolder;
        [DataMember(Name = "icon")] public string Icon;
        [DataMember(Name = "name")] public string Name;
    }

    [DataContract]
    public class PageDoc
    {
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "slots")] public List<SlotDoc> Slots = new List<SlotDoc>();
    }

    [DataContract]
    public class ConfigDoc
    {
        [DataMember(Name = "version")] public int Version = 1;
        [DataMember(Name = "layout")] public LayoutConfig Layout = new LayoutConfig();
        [DataMember(Name = "theme")] public string Theme = "Dark";
        [DataMember(Name = "lastPage")] public string LastPage = string.Empty; // 上次所在页名
        [DataMember(Name = "hotkeys")] public HotkeyConfig Hotkeys = new HotkeyConfig();
        [DataMember(Name = "pages")] public List<PageDoc> Pages = new List<PageDoc>();
    }

    // 单一配置源：布局 / 主题 / 各页槽位数据，统一读写结构化 launcher.json；
    // 首次运行若无 json 但有旧 launcher.ini + panel_dotN.txt，则迁移并保留旧文件一个版本。
    public class ConfigStore
    {
        public LayoutConfig Layout = new LayoutConfig();
        public ThemeMode Theme = ThemeMode.Dark;
        public List<string> PageNames = new List<string>();
        public Dictionary<string, List<SlotData>> Pages = new Dictionary<string, List<SlotData>>();
        public HotkeyConfig Hotkeys = new HotkeyConfig();
        public string LastPage { get; set; } = string.Empty; // 上次所在页名（重启恢复）

        private string JsonPath => Path.Combine(BaseDir, "launcher.json");
        private string IniPath => Path.Combine(BaseDir, "launcher.ini");
        public string JsonFilePath => JsonPath;

        /// <summary>
        /// 测试可覆盖此属性，将配置读写重定向到临时目录，实现隔离；
        /// 留空时仍使用程序所在目录（产品运行时默认行为）。
        /// </summary>
        public string BaseDirOverride { get; set; }

        private string BaseDir => string.IsNullOrEmpty(BaseDirOverride)
            ? AppDomain.CurrentDomain.BaseDirectory
            : BaseDirOverride;

        public void Load()
        {
            if (File.Exists(JsonPath)) { LoadFromJson(); return; }
            MigrateFromLegacy();
        }

        private void LoadFromJson()
        {
            try
            {
                // File.ReadAllText 会自动剥离 UTF-8 BOM，避免 DataContractJsonSerializer 在流首字节处报错
                string text = File.ReadAllText(JsonPath);
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(text)))
                {
                    var ser = new DataContractJsonSerializer(typeof(ConfigDoc));
                    var doc = (ConfigDoc)ser.ReadObject(ms);
                    if (doc != null) ApplyDoc(doc);
                }
            }
            catch (Exception ex)
            {
                Logger.Log("读取 launcher.json 失败，尝试旧配置迁移: " + ex.Message);
                MigrateFromLegacy();
            }
        }

        private void ApplyDoc(ConfigDoc doc)
        {
            Layout = doc.Layout ?? new LayoutConfig();
            Theme = (doc.Theme != null && doc.Theme.Equals("Light", StringComparison.OrdinalIgnoreCase))
                ? ThemeMode.Light : ThemeMode.Dark;
            Hotkeys = doc.Hotkeys ?? new HotkeyConfig();
            if (Hotkeys.Jump == null) Hotkeys.Jump = new List<string>();
            LastPage = doc.LastPage ?? string.Empty; // 上次所在页名（重启恢复）
            PageNames = new List<string>();
            Pages = new Dictionary<string, List<SlotData>>();
            if (doc.Pages != null)
            {
                foreach (var p in doc.Pages)
                {
                    var list = new List<SlotData>();
                    if (p.Slots != null)
                        foreach (var s in p.Slots)
                            list.Add(new SlotData { Index = s.Index, FilePath = s.Path ?? string.Empty, IsFolder = s.IsFolder, IconPath = s.Icon ?? string.Empty, DisplayName = s.Name ?? string.Empty });
                    string name = NormalizePageName(p.Name); // 退役遗留内部键 panel_dotN
                    Pages[name] = list;
                    PageNames.Add(name);
                }
            }
            EnsurePageShape();
        }

        // 把遗留内部键 panel_dotN 归一化为友好页名「第 N 页」，使其彻底退役；
        // 其它名称原样返回。仅在加载既有配置时调用，向前兼容旧 launcher.json。
        private static string NormalizePageName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;
            if (raw.StartsWith("panel_dot", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(raw.Substring("panel_dot".Length), out int n))
                return "第" + n + "页";
            return raw;
        }

        private void EnsurePageShape()
        {
            if (PageNames.Count == 0)
                for (int i = 1; i <= Layout.PageCount; i++) PageNames.Add("第" + i + "页");

            // 安全边界
            Layout.SlotCount = Math.Max(1, Math.Min(Layout.SlotCount, 400));
            Layout.IconSize = Math.Max(16, Math.Min(Layout.IconSize, 256));
            Layout.PageCount = Math.Max(1, Math.Min(Layout.PageCount, 20));
            Layout.Columns = Math.Max(1, Math.Min(Layout.Columns, 40));
            Layout.Width = Math.Max(0, Math.Min(Layout.Width, 4000));
            Layout.Height = Math.Max(0, Math.Min(Layout.Height, 4000));
            Layout.Opacity = Math.Max(30, Math.Min(Layout.Opacity, 100));

            foreach (var name in PageNames)
            {
                if (!Pages.ContainsKey(name)) Pages[name] = new List<SlotData>();
                var list = Pages[name];
                for (int i = 0; i < Layout.SlotCount; i++)
                {
                    if (i >= list.Count) list.Add(new SlotData(i));
                    else if (list[i] == null) list[i] = new SlotData(i);
                }
            }
        }

        private void MigrateFromLegacy()
        {
            Layout = new LayoutConfig();
            Theme = ThemeMode.Dark;
            PageNames = new List<string>();
            Pages = new Dictionary<string, List<SlotData>>();

            // 1) 读取旧 launcher.ini
            if (File.Exists(IniPath))
            {
                bool inLayout = false, inTheme = false;
                foreach (var raw in File.ReadAllLines(IniPath))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        inLayout = line.Equals("[Layout]", StringComparison.OrdinalIgnoreCase);
                        inTheme = line.Equals("[Theme]", StringComparison.OrdinalIgnoreCase);
                        continue;
                    }
                    if (string.IsNullOrEmpty(line) || line.StartsWith(";") || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string val = line.Substring(eq + 1).Trim();
                    int sc = val.IndexOf(';'); if (sc >= 0) val = val.Substring(0, sc).Trim();
                    if (inLayout)
                    {
                        if (key == "slotcount" && int.TryParse(val, out int v)) Layout.SlotCount = v;
                        else if (key == "iconsize" && int.TryParse(val, out int v2)) Layout.IconSize = v2;
                        else if (key == "pagecount" && int.TryParse(val, out int v3)) Layout.PageCount = v3;
                        else if (key == "columns" && int.TryParse(val, out int v4)) Layout.Columns = v4;
                    }
                    else if (inTheme && key == "mode")
                    {
                        if (val.Equals("light", StringComparison.OrdinalIgnoreCase)) Theme = ThemeMode.Light;
                        else if (val.Equals("dark", StringComparison.OrdinalIgnoreCase)) Theme = ThemeMode.Dark;
                    }
                }
            }

            for (int i = 1; i <= Layout.PageCount; i++) PageNames.Add("第" + i + "页");
            EnsurePageShape();

            // 2) 读取旧 panel_dotN.txt（键为 pictureBox 名 -> 取其序号作为槽位 index）
            foreach (var name in PageNames)
            {
                string txt = Path.Combine(BaseDir, name + ".txt");
                if (!File.Exists(txt)) continue;
                foreach (var raw in File.ReadAllLines(txt))
                {
                    int eq = raw.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = raw.Substring(0, eq).Trim();
                    string val = raw.Substring(eq + 1);
                    if (string.IsNullOrEmpty(val)) continue;
                    if (key.StartsWith("pictureBox") && int.TryParse(key.Substring("pictureBox".Length), out int idx) && idx >= 1)
                    {
                        int slotIndex = idx - 1;
                        if (slotIndex < Layout.SlotCount)
                            Pages[name][slotIndex] = new SlotData
                            {
                                Index = slotIndex,
                                FilePath = val,
                                IsFolder = Directory.Exists(val)
                            };
                    }
                }
            }

            Save(); // 写出结构化配置；旧文件保留一个版本
        }

        public void Save()
        {
            try
            {
                var doc = new ConfigDoc
                {
                    Version = 1,
                    Layout = Layout,
                    Theme = Theme == ThemeMode.Dark ? "Dark" : "Light",
                    Hotkeys = Hotkeys,
                    LastPage = this.LastPage, // 上次所在页名（重启恢复）
                    Pages = new List<PageDoc>()
                };
                foreach (var name in PageNames)
                {
                    var pd = new PageDoc { Name = name, Slots = new List<SlotDoc>() };
                    if (Pages.ContainsKey(name))
                    {
                        foreach (var s in Pages[name])
                        {
                            if (s != null && !string.IsNullOrEmpty(s.FilePath))
                                pd.Slots.Add(new SlotDoc { Index = s.Index, Path = s.FilePath, IsFolder = s.IsFolder, Icon = s.IconPath, Name = s.DisplayName });
                        }
                    }
                    doc.Pages.Add(pd);
                }
                using (var ms = new MemoryStream())
                {
                    new DataContractJsonSerializer(typeof(ConfigDoc)).WriteObject(ms, doc);
                    // 用无 BOM 的 UTF-8 写出，防止读取端（DataContractJsonSerializer）因 BOM 首字节报错
                    WriteAtomic(JsonPath, Encoding.UTF8.GetString(ms.ToArray()));
                }
            }
            catch (Exception ex) { Logger.Log("保存 launcher.json 失败: " + ex.Message); }
        }

        // 原子写：先写同目录 tmp 再替换目标，避免写盘中途崩溃/断电损坏配置文件；
        // 不用 File.WriteAllText 直写（非原子，中途失败会留下截断/乱码的 JSON）。
        private static void WriteAtomic(string path, string content)
        {
            var bytes = new UTF8Encoding(false).GetBytes(content);
            string tmp = path + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            if (File.Exists(path))
            {
                try { File.Replace(tmp, path, null); return; }
                catch (Exception) { /* 个别文件系统/被占用时不支持 Replace，退化为覆盖 */ }
            }
            File.Delete(path);
            File.Move(tmp, path);
        }

        public List<SlotData> GetPage(string name) => Pages.ContainsKey(name) ? Pages[name] : null;

        public SlotData GetSlot(string name, int index)
        {
            if (!Pages.ContainsKey(name)) return null;
            var list = Pages[name];
            return (index >= 0 && index < list.Count) ? list[index] : null;
        }

        public void SetSlot(string name, int index, SlotData data)
        {
            if (!Pages.ContainsKey(name)) return;
            var list = Pages[name];
            while (list.Count <= index) list.Add(new SlotData(list.Count));
            list[index] = data;
        }

        public void RemoveSlot(string name, int index)
        {
            if (!Pages.ContainsKey(name)) return;
            var list = Pages[name];
            if (index >= 0 && index < list.Count) list[index] = new SlotData(index);
        }

        public void SwapSlots(string name, int a, int b)
        {
            if (!Pages.ContainsKey(name)) return;
            var list = Pages[name];
            if (a < 0 || b < 0 || a >= list.Count || b >= list.Count) return;
            var tmp = list[a]; list[a] = list[b]; list[b] = tmp;
        }

        // 新增一页（分页 +1），返回新页索引；上限 20 页（与 EnsurePageShape 的钳制一致）。
        // 新页默认名「第 N 页」，可随后用 RenamePage 改成有意义的名字（不再硬编码 panel_dotN）。
        public int AddPage()
        {
            int newCount = Math.Min(Layout.PageCount + 1, 20);
            Layout.PageCount = newCount;
            string newName = GenerateUniqueName("第" + newCount + "页");
            if (!PageNames.Contains(newName))
            {
                PageNames.Add(newName);
                Pages[newName] = new List<SlotData>();
            }
            EnsurePageShape();   // 为新页（及既有页）补齐空槽位
            return PageNames.IndexOf(newName);
        }

        // 生成不与其他页冲突的页名（重名时追加数字后缀）
        private string GenerateUniqueName(string baseName)
        {
            if (!PageNames.Contains(baseName)) return baseName;
            for (int n = 2; ; n++)
            {
                string candidate = baseName + " " + n;
                if (!PageNames.Contains(candidate)) return candidate;
            }
        }

        // 重命名第 index 页为 newName。校验：非空、长度 ≤ 20、不含路径分隔/引号等危险字符、不与现有页重名。
        // 成功后同步重键 Pages 字典，并更新 LastPage 记忆。返回是否成功。
        public bool RenamePage(int index, string newName)
        {
            if (index < 0 || index >= PageNames.Count) return false;
            string oldName = PageNames[index];
            string name = (newName ?? string.Empty).Trim();
            if (name.Length == 0) return false;
            if (name.Length > 20) return false;
            if (name.IndexOfAny(new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' }) >= 0) return false;
            foreach (var n in PageNames)
                if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return false;
            if (oldName == name) return true;

            PageNames[index] = name;
            var list = Pages.ContainsKey(oldName) ? Pages[oldName] : new List<SlotData>();
            Pages.Remove(oldName);
            Pages[name] = list;
            if (LastPage == oldName) LastPage = name;
            return true;
        }

        // 删除第 index 页（0 基）。页名现为友好名「第 N 页」，删除后必须把后续页整体前移以保持编号连续。
        // 返回被移除页原有的槽位数据（供调用方释放图像资源）；失败返回 null。
        public List<SlotData> RemovePage(int index)
        {
            if (Layout.PageCount <= 1) return null;                 // 至少保留一页
            if (index < 0 || index >= PageNames.Count) return null;

            string target = PageNames[index];
            var removed = Pages.ContainsKey(target) ? Pages[target] : new List<SlotData>();

            // 后续页依次前移：第 (i+1) 页的内容搬到第 i 页
            for (int i = index; i < PageNames.Count - 1; i++)
            {
                string cur = PageNames[i], next = PageNames[i + 1];
                Pages[cur] = Pages.ContainsKey(next) ? Pages[next] : new List<SlotData>();
            }

            string last = PageNames[PageNames.Count - 1];
            Pages.Remove(last);
            PageNames.RemoveAt(PageNames.Count - 1);
            Layout.PageCount = PageNames.Count;
            EnsurePageShape();
            return removed;
        }

        // 当前页是否含有非空槽位（删除页前用于提示确认）
        public bool IsPageEmpty(string name)
        {
            if (!Pages.ContainsKey(name)) return true;
            foreach (var s in Pages[name])
                if (s != null && !s.IsEmpty) return false;
            return true;
        }
    }
}
