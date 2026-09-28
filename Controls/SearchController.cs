using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using launcher.Core;
using launcher.Models;

namespace launcher.Controls
{
    // 搜索过滤控制器：封装搜索状态与跨页合并视图的渲染逻辑（从 Form1 抽取，单一职责）。
    // 与 UI 控件的交互通过依赖注入的回调完成，保持可单测、降低 Form1 体积。
    internal sealed class SearchController
    {
        private readonly List<IconSlot> _slots;
        private readonly PageIndicator _pageIndicator;
        private readonly ConfigStore _store;
        private readonly Func<string> _getCurrentPageName;
        private readonly Action<string> _ensurePageLoaded;
        private readonly Action<bool> _setEvictionSuppressed;
        private readonly Func<ThemePalette> _getPalette;
        private readonly Action<string> _externalSearch;   // 本地结果就绪后异步去问 Everything（结果回来再追加）

        private List<IconSlot> _searchMatches = new List<IconSlot>();
        private int _searchSel;

        public SearchController(List<IconSlot> slots, PageIndicator pageIndicator, ConfigStore store,
            Func<string> getCurrentPageName, Action<string> ensurePageLoaded,
            Action<bool> setEvictionSuppressed, Func<ThemePalette> getPalette,
            Action<string> externalSearch = null)
        {
            _slots = slots;
            _pageIndicator = pageIndicator;
            _store = store;
            _getCurrentPageName = getCurrentPageName;
            _ensurePageLoaded = ensurePageLoaded;
            _setEvictionSuppressed = setEvictionSuppressed;
            _getPalette = getPalette;
            _externalSearch = externalSearch;
        }

        public bool IsSearching => _searchMatches != null && _searchMatches.Count > 0;
        public int Count => _searchMatches == null ? 0 : _searchMatches.Count;

        // 退出搜索模式，恢复当前页显示
        public void ShowAll()
        {
            _searchMatches = null;
            _searchSel = 0;
            _pageIndicator.Visible = true; // 恢复分页圆点
            _ensurePageLoaded(_getCurrentPageName()); // 保护：当前页缓存可能已被 LRU 释放，恢复时补渲染
            // 恢复当前页显示（不 clearImageBoxes，直接覆盖）
            var list = _store.GetPage(_getCurrentPageName());
            if (list != null)
            {
                for (int i = 0; i < _slots.Count && i < list.Count; i++)
                {
                    var data = list[i];
                    _slots[i].Data = data ?? new SlotData(i);
                    _slots[i].Image = data?.CachedImage;
                    _slots[i].Visible = true;
                    ClearSlotHighlight(_slots[i]);
                }
            }
        }

        // 应用搜索过滤（跨页合并视图）
        public void ApplyFilter(string q)
        {
            if (string.IsNullOrWhiteSpace(q))
            {
                // 退出搜索模式，恢复当前页
                ShowAll();
                return;
            }

            // 搜索合并视图从各页取图，禁止 LRU 驱逐，避免刚加载的匹配页缓存被释放
            _setEvictionSuppressed(true);
            try
            {
                // 跨页收集所有匹配项（匹配逻辑已抽到 SearchEngine，可单测）
                var matches = SearchEngine.FindMatches(_store, q);
                var matchedPages = new HashSet<string>();
                foreach (var m in matches) matchedPages.Add(m.PageName);

                // 懒加载：结果用到的页按需渲染（此时驱逐被抑制，搜索期间不丢缓存）
                foreach (var pn in matchedPages) _ensurePageLoaded(pn);

                if (matches.Count == 0)
                {
                    // 本地一个都没命中：槽位清空，但**保留搜索态**（_searchMatches 置空列表而非 null），
                    // 否则后来追加的 Everything 全盘结果会被当成"已退出搜索"而被直接丢弃。
                    _searchMatches = new List<IconSlot>();
                    _searchSel = 0;
                    foreach (var s in _slots) { s.Visible = false; ClearSlotHighlight(s); }
                    _pageIndicator.Visible = false;
                    return;
                }

                // 进入搜索合并视图模式
                _pageIndicator.Visible = false;

                // 把所有匹配项填进 _slots（最多 40 个）
                int count = Math.Min(matches.Count, _slots.Count);
                for (int i = 0; i < _slots.Count; i++)
                {
                    if (i < count)
                    {
                        _slots[i].Data = matches[i].Data;
                        _slots[i].Image = matches[i].Data.CachedImage;
                        _slots[i].Visible = true;
                        ClearSlotHighlight(_slots[i]);
                    }
                    else
                    {
                        _slots[i].Data = new SlotData(i);
                        _slots[i].Image = null;
                        _slots[i].Visible = false;
                    }
                }

                // 建立 _searchMatches 用于键盘导航
                _searchMatches = _slots.Take(count).ToList();
                _searchSel = 0;
                UpdateHighlight();
            }
            finally
            {
                _setEvictionSuppressed(false);
                // 本地结果已经即时显示了，接着异步去问 Everything，命中结果回来后追加到槽位末尾。
                // 放在 finally 里是因为「本地零匹配」那条分支是提前 return 的。
                if (_externalSearch != null) _externalSearch(q);
            }
        }

        /// <summary>
        /// 追加外部搜索结果（Everything 全盘命中）：接在本地匹配之后，占用还没用上的槽位。
        /// 只改 _slots，绝不碰 _store —— 这些是临时项，不能被 SaveConfig 序列化进 launcher.json。
        /// </summary>
        public void AppendExternal(IList<SlotData> items)
        {
            if (items == null || items.Count == 0) return;
            if (_searchMatches == null) return;   // 已退出搜索态，结果作废

            int start = _searchMatches.Count;
            int used = 0;
            for (int k = 0; k < items.Count && start + k < _slots.Count; k++)
            {
                var d = items[k];
                if (d == null) continue;
                d.Index = start + k;
                _slots[start + k].Data = d;
                _slots[start + k].Image = d.CachedImage;
                _slots[start + k].Visible = true;
                ClearSlotHighlight(_slots[start + k]);
                _searchMatches.Add(_slots[start + k]);
                used = k + 1;
            }
            // 槽位不够、放不下的那些，位图要就地释放，否则每搜一次就漏一批 GDI 对象
            for (int k = used; k < items.Count; k++)
                if (items[k] != null) items[k].DisposeImage();
        }

        public void UpdateHighlight()
        {
            foreach (var s in _slots) ClearSlotHighlight(s);
            if (_searchMatches != null && _searchSel >= 0 && _searchSel < _searchMatches.Count)
                SetSlotHighlight(_searchMatches[_searchSel]);
        }

        // 当前高亮项（键盘回车打开）
        public IconSlot GetSelected()
        {
            if (_searchMatches == null || _searchMatches.Count == 0) return null;
            return _searchMatches[Math.Max(0, Math.Min(_searchSel, _searchMatches.Count - 1))];
        }

        // 第 idx 个匹配项（数字键直接打开）
        public IconSlot GetNth(int idx)
        {
            if (_searchMatches == null || idx < 0 || idx >= _searchMatches.Count) return null;
            return _searchMatches[idx];
        }

        public void MoveUp()
        {
            if (!IsSearching) return;
            _searchSel = Math.Max(0, _searchSel - 1);
            UpdateHighlight();
        }

        public void MoveDown()
        {
            if (!IsSearching) return;
            _searchSel = Math.Min(_searchMatches.Count - 1, _searchSel + 1);
            UpdateHighlight();
        }

        private void SetSlotHighlight(IconSlot slot)
        {
            if (slot == null) return;
            slot.Padding = new Padding(3);
            slot.BackColor = _getPalette().Highlight;
        }

        private void ClearSlotHighlight(IconSlot slot)
        {
            if (slot == null) return;
            slot.Padding = Padding.Empty;
            slot.BackColor = _getPalette().SlotBack;
        }
    }
}
