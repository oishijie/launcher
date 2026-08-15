using System.Collections.Generic;
using System.IO;
using launcher.Models;

namespace launcher.Core
{
    // 单条搜索结果：匹配到的槽位及其所在页（跨页搜索合并视图用）
    public class SearchMatch
    {
        public SlotData Data;
        public string PageName;
    }

    // 搜索匹配逻辑（与 UI 解耦，可单测）：槽位显示名 + 跨页包含匹配 + 首字母定位。
    // Form1 只负责把结果填进槽位控件，这里专注"哪些命中、命中在哪些页"的纯逻辑。
    public static class SearchEngine
    {
        // 槽位显示名：URL 直接拿原文；否则取文件名
        public static string SlotLabel(SlotData d)
        {
            if (d == null || d.IsEmpty) return string.Empty;
            if (UrlUtil.IsHttpUrl(d.FilePath)) return d.FilePath;
            try { return Path.GetFileName(d.FilePath) ?? d.FilePath; }
            catch { return d.FilePath; }
        }

        // 链接的友好名称（用于悬停提示等展示）：去掉协议与 www.，留域名；
        // 若含一级路径且总长不长，则附上以便区分同站点不同页面；太长只留域名。
        public static string UrlLabel(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return string.Empty;
            string host = null, path = string.Empty;
            try
            {
                var uri = new System.Uri(url);
                host = uri.Host;
                path = uri.AbsolutePath.Trim('/');
            }
            catch { }
            if (string.IsNullOrEmpty(host)) host = url; // 解析失败时回退原文

            string baseName = host.StartsWith("www.", System.StringComparison.OrdinalIgnoreCase)
                ? host.Substring(4) : host;
            if (string.IsNullOrEmpty(path)) return baseName;
            string first = path.Split('/')[0];
            string withPath = baseName + "/" + first;
            return withPath.Length <= 32 ? withPath : baseName;
        }

        // 跨页收集所有匹配项（保留页序、槽位序），query 空白返回空列表
        public static List<SearchMatch> FindMatches(ConfigStore store, string query)
        {
            var result = new List<SearchMatch>();
            if (store == null || string.IsNullOrWhiteSpace(query)) return result;
            string ql = query.ToLowerInvariant();
            foreach (var pageName in store.PageNames)
            {
                var list = store.GetPage(pageName);
                if (list == null) continue;
                foreach (var d in list)
                {
                    if (d == null || d.IsEmpty) continue;
                    string label = SlotLabel(d);
                    string labelLower = label.ToLowerInvariant();
                    string pathLower = (d.FilePath ?? string.Empty).ToLowerInvariant();
                    if (labelLower.Contains(ql) || pathLower.Contains(ql) ||
                        PinyinHelper.IsPinyinMatch(label, ql))
                        result.Add(new SearchMatch { Data = d, PageName = pageName });
                }
            }
            return result;
        }

        // 返回页面中首个"文件名以该字母开头"的槽位索引；支持英文首字母和中文拼音首字母
        public static int FindFirstByLetter(List<SlotData> page, char c)
        {
            char cl = char.ToUpperInvariant(c);
            if (page == null) return -1;
            for (int i = 0; i < page.Count; i++)
            {
                var d = page[i];
                if (d == null || d.IsEmpty) continue;
                string label = SlotLabel(d);
                if (string.IsNullOrEmpty(label)) continue;
                // 英文/数字首字母匹配
                if (char.ToUpperInvariant(label[0]) == cl) return i;
                // 中文拼音首字母匹配
                if (label[0] >= 0x4E00 && label[0] <= 0x9FFF &&
                    PinyinHelper.GetFirstLetter(label[0]) == cl) return i;
            }
            return -1;
        }
    }
}
