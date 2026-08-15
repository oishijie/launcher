using System;
using System.Collections.Generic;
using System.IO;
using launcher.Controls;

namespace launcher.Core
{
    // 配置备份导出条目的纯逻辑（与 UI/对话框解耦，可单测）：
    // 决定"要导出哪些文件、以什么包内路径存放、重名如何去重"。
    public static class ConfigExporter
    {
        // 导出/导入涉及的配置文件（存在才包含）
        public static readonly string[] ConfigFileNames = { "launcher.json", "notes.txt", "launcher.ini" };

        // 返回程序目录下实际存在的配置文件绝对路径列表
        public static List<string> CollectConfigFiles(string baseDir)
        {
            var result = new List<string>();
            foreach (var f in ConfigFileNames)
            {
                string p = Path.Combine(baseDir, f);
                if (File.Exists(p)) result.Add(p);
            }
            return result;
        }

        // 扫描所有页所有槽位的自定义图标，产出 (源文件, 包内路径 icons/<文件名>)；
        // 文件名重名时自动追加 _2/_3… 后缀去重，换机导入不互相覆盖。
        // 返回 (SourcePath, EntryName) 元组列表。
        public static List<Tuple<string, string>> CollectIconEntries(ConfigStore store)
        {
            var entries = new List<Tuple<string, string>>();
            var iconNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (store == null) return entries;

            foreach (var name in store.PageNames)
            {
                var page = store.GetPage(name);
                if (page == null) continue;
                foreach (var slot in page)
                {
                    if (string.IsNullOrWhiteSpace(slot?.IconPath)) continue;
                    string src = IconRenderer.ResolveIconPath(slot.IconPath);
                    if (!File.Exists(src)) continue;
                    string baseName = Path.GetFileName(src);
                    string entryName = "icons/" + baseName;
                    if (!iconNames.Add(baseName))
                    {
                        int n = 2;
                        string stem = Path.GetFileNameWithoutExtension(baseName);
                        string ext = Path.GetExtension(baseName);
                        while (!iconNames.Add(stem + "_" + n + ext)) n++;
                        entryName = "icons/" + stem + "_" + n + ext;
                    }
                    entries.Add(Tuple.Create(src, entryName));
                }
            }
            return entries;
        }
    }
}
