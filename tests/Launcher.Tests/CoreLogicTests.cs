using Microsoft.VisualStudio.TestTools.UnitTesting;
using launcher.Core;
using launcher.Models;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Launcher.Tests
{
    [TestClass]
    public class SearchEngineTests
    {
        private static ConfigStore BuildStore()
        {
            var store = new ConfigStore();
            store.PageNames = new List<string> { "工作", "游戏" };
            store.Pages = new Dictionary<string, List<SlotData>>
            {
                { "工作", new List<SlotData> { new SlotData(0) { FilePath = @"C:\Work\Report.docx" }, new SlotData(1) } },
                { "游戏", new List<SlotData> { new SlotData(0) { FilePath = @"D:\Games\report.exe" }, new SlotData(1) { FilePath = "https://example.com/tool" } } },
            };
            return store;
        }

        [TestMethod]
        public void SlotLabel_ReturnsFileNameForFile()
        {
            var d = new SlotData(0) { FilePath = @"C:\Work\Report.docx" };
            Assert.AreEqual("Report.docx", SearchEngine.SlotLabel(d));
        }

        [TestMethod]
        public void SlotLabel_ReturnsUrlForUrl()
        {
            var d = new SlotData(0) { FilePath = "https://example.com/tool" };
            Assert.AreEqual("https://example.com/tool", SearchEngine.SlotLabel(d));
        }

        [TestMethod]
        public void SlotLabel_ReturnsEmptyForEmpty()
        {
            Assert.AreEqual(string.Empty, SearchEngine.SlotLabel(null));
            Assert.AreEqual(string.Empty, SearchEngine.SlotLabel(new SlotData(0)));
        }

        [TestMethod]
        public void UrlLabel_StripsSchemeAndWww_KeepsDomain()
        {
            Assert.AreEqual("example.com", SearchEngine.UrlLabel("https://www.example.com"));
            Assert.AreEqual("example.com", SearchEngine.UrlLabel("http://example.com/"));
            Assert.AreEqual("github.com", SearchEngine.UrlLabel("https://github.com"));
        }

        [TestMethod]
        public void UrlLabel_AppendsFirstPathSegment_WhenShort()
        {
            Assert.AreEqual("example.com/maps", SearchEngine.UrlLabel("https://www.example.com/maps/place/xyz"));
            Assert.AreEqual("example.com/tool", SearchEngine.UrlLabel("https://example.com/tool"));
        }

        [TestMethod]
        public void UrlLabel_TrimsToDomain_WhenTooLong()
        {
            // 一级路径超过 32 字符总长上限时只留域名，避免悬停提示过长
            Assert.AreEqual("example.com", SearchEngine.UrlLabel("https://www.example.com/veryLongFirstPathSegmentOverLimit"));
            Assert.AreEqual(string.Empty, SearchEngine.UrlLabel(""));
            Assert.AreEqual(string.Empty, SearchEngine.UrlLabel(null));
        }

        [TestMethod]
        public void FindMatches_MatchesAcrossPages_ByFileName()
        {
            var matches = SearchEngine.FindMatches(BuildStore(), "report");
            Assert.AreEqual(2, matches.Count, "文件名含 report 的应命中两个页");
            Assert.AreEqual("工作", matches[0].PageName);
            Assert.AreEqual("游戏", matches[1].PageName);
        }

        [TestMethod]
        public void FindMatches_IsCaseInsensitive()
        {
            var matches = SearchEngine.FindMatches(BuildStore(), "REPORT");
            Assert.AreEqual(2, matches.Count);
        }

        [TestMethod]
        public void FindMatches_MatchesByFullPath()
        {
            var matches = SearchEngine.FindMatches(BuildStore(), "D:\\Games");
            Assert.AreEqual(1, matches.Count);
            Assert.AreEqual("游戏", matches[0].PageName);
        }

        [TestMethod]
        public void FindMatches_EmptyQuery_ReturnsEmpty()
        {
            Assert.AreEqual(0, SearchEngine.FindMatches(BuildStore(), "").Count);
            Assert.AreEqual(0, SearchEngine.FindMatches(BuildStore(), null).Count);
            Assert.AreEqual(0, SearchEngine.FindMatches(BuildStore(), "  ").Count);
        }

        [TestMethod]
        public void FindMatches_NoHit_ReturnsEmpty()
        {
            Assert.AreEqual(0, SearchEngine.FindMatches(BuildStore(), "zzz").Count);
        }

        [TestMethod]
        public void FindFirstByLetter_ReturnsIndexOfFirstFileMatchingLetter()
        {
            var page = new List<SlotData>
            {
                new SlotData(0) { FilePath = @"C:\Work\Beta.txt" },
                new SlotData(1) { FilePath = @"C:\Work\Alpha.txt" },
                new SlotData(2) { FilePath = @"C:\Work\Gamma.txt" },
            };
            Assert.AreEqual(1, SearchEngine.FindFirstByLetter(page, 'a'));
            Assert.AreEqual(0, SearchEngine.FindFirstByLetter(page, 'B'));
            Assert.AreEqual(-1, SearchEngine.FindFirstByLetter(page, 'z'));
            Assert.AreEqual(-1, SearchEngine.FindFirstByLetter(null, 'a'));
        }

        [TestMethod]
        public void FindFirstByLetter_ChinesePinyinInitial()
        {
            var page = new List<SlotData>
            {
                new SlotData(0) { FilePath = @"C:\Apps\微信.exe" },   // 微 → w
                new SlotData(1) { FilePath = @"C:\Apps\钉钉.exe" },   // 钉 → d
                new SlotData(2) { FilePath = @"C:\Apps\QQ.exe" },
            };
            Assert.AreEqual(0, SearchEngine.FindFirstByLetter(page, 'w'), "微拼音首字母是 w");
            Assert.AreEqual(1, SearchEngine.FindFirstByLetter(page, 'd'), "钉拼音首字母是 d");
        }

        [TestMethod]
        public void FindMatches_ChinesePinyinInitials()
        {
            var store = new ConfigStore();
            store.PageNames = new List<string> { "工具" };
            store.Pages = new Dictionary<string, List<SlotData>>
            {
                { "工具", new List<SlotData>
                    {
                        new SlotData(0) { FilePath = @"C:\Apps\微信.exe" },
                        new SlotData(1) { FilePath = @"C:\Apps\钉钉.exe" },
                        new SlotData(2) { FilePath = @"C:\Apps\记事本.exe" },
                    }
                },
            };
            // 拼音首字母匹配：wx → 微信
            var m1 = SearchEngine.FindMatches(store, "wx");
            Assert.AreEqual(1, m1.Count, "wx 应命中 微信");
            Assert.AreEqual(0, m1[0].Data.Index);

            // 拼音首字母匹配：jsb → 记事本
            var m2 = SearchEngine.FindMatches(store, "jsb");
            Assert.AreEqual(1, m2.Count, "jsb 应命中 记事本");

            // 全拼匹配：ding → 钉钉
            var m3 = SearchEngine.FindMatches(store, "ding");
            Assert.AreEqual(1, m3.Count, "ding 应命中 钉钉");
        }

        [TestMethod]
        public void FindMatches_ChinesePinyin_NoFalsePositive()
        {
            var store = new ConfigStore();
            store.PageNames = new List<string> { "p" };
            store.Pages = new Dictionary<string, List<SlotData>>
            {
                { "p", new List<SlotData>
                    {
                        new SlotData(0) { FilePath = @"C:\Report.docx" },
                    }
                },
            };
            // 英文文件名不应触发拼音匹配
            Assert.AreEqual(0, SearchEngine.FindMatches(store, "bg").Count, "纯英文文件名不应拼音匹配");
        }
    }

    [TestClass]
    public class PinyinHelperTests
    {
        [TestMethod]
        public void GetInitials_ChineseCharacters()
        {
            Assert.AreEqual("wx", PinyinHelper.GetInitials("微信"));
            Assert.AreEqual("nh", PinyinHelper.GetInitials("你好"));
        }

        [TestMethod]
        public void GetInitials_MixedChineseAndEnglish()
        {
            // 非中文字符保持原样（小写）
            Assert.AreEqual("nhworld", PinyinHelper.GetInitials("你好World"));
        }

        [TestMethod]
        public void GetInitials_PureEnglish()
        {
            Assert.AreEqual("report.docx", PinyinHelper.GetInitials("Report.docx"));
        }

        [TestMethod]
        public void GetFullPinyin_Basic()
        {
            Assert.AreEqual("wei xin", PinyinHelper.GetFullPinyin("微信"));
            Assert.AreEqual("ni hao", PinyinHelper.GetFullPinyin("你好"));
        }

        [TestMethod]
        public void IsPinyinMatch_InitialsMatch()
        {
            Assert.IsTrue(PinyinHelper.IsPinyinMatch("微信.exe", "wx"));
            Assert.IsTrue(PinyinHelper.IsPinyinMatch("记事本.exe", "jsb"));
        }

        [TestMethod]
        public void IsPinyinMatch_FullPinyinMatch()
        {
            Assert.IsTrue(PinyinHelper.IsPinyinMatch("微信.exe", "weixin"));
            Assert.IsTrue(PinyinHelper.IsPinyinMatch("你好", "nihao"));
        }

        [TestMethod]
        public void IsPinyinMatch_NoMatch()
        {
            Assert.IsFalse(PinyinHelper.IsPinyinMatch("微信.exe", "zs"));
            Assert.IsFalse(PinyinHelper.IsPinyinMatch("微信.exe", "abc"));
        }

        [TestMethod]
        public void IsPinyinMatch_PureEnglish_ReturnsFalse()
        {
            // 纯英文不需要拼音匹配（由 Contains 处理）
            Assert.IsFalse(PinyinHelper.IsPinyinMatch("Report.docx", "rep"));
        }

        [TestMethod]
        public void IsPinyinMatch_EmptyInput()
        {
            Assert.IsFalse(PinyinHelper.IsPinyinMatch("", "wx"));
            Assert.IsFalse(PinyinHelper.IsPinyinMatch("微信", ""));
            Assert.IsFalse(PinyinHelper.IsPinyinMatch(null, "wx"));
        }

        [TestMethod]
        public void GetFirstLetter_Chinese()
        {
            Assert.AreEqual('W', PinyinHelper.GetFirstLetter('微'));
            Assert.AreEqual('N', PinyinHelper.GetFirstLetter('你'));
            Assert.AreEqual('Z', PinyinHelper.GetFirstLetter('中'));
        }

        [TestMethod]
        public void GetFirstLetter_English()
        {
            Assert.AreEqual('A', PinyinHelper.GetFirstLetter('a'));
            Assert.AreEqual('Z', PinyinHelper.GetFirstLetter('Z'));
        }
    }

    [TestClass]
    public class KeyRouterTests
    {
        private static HotkeyConfig Defaults() => new HotkeyConfig();

        [TestMethod]
        public void Resolve_NonSearch_CtrlZ_IsUndo()
        {
            var r = KeyRouter.Resolve(Defaults(), false, true, false, false, Keys.Z);
            Assert.IsTrue(r.Handled);
            Assert.AreEqual(KeyAction.UndoDelete, r.Action);
        }

        [TestMethod]
        public void Resolve_NonSearch_APrevPage_DNextPage()
        {
            var r = KeyRouter.Resolve(Defaults(), false, false, false, false, Keys.A);
            Assert.AreEqual(KeyAction.PrevPage, r.Action);
            r = KeyRouter.Resolve(Defaults(), false, false, false, false, Keys.D);
            Assert.AreEqual(KeyAction.NextPage, r.Action);
        }

        [TestMethod]
        public void Resolve_NonSearch_DigitMapsToZeroBasedSlotIndex()
        {
            var r = KeyRouter.Resolve(Defaults(), false, false, false, false, Keys.D3);
            Assert.AreEqual(KeyAction.OpenSlotByIndex, r.Action);
            Assert.AreEqual(2, r.SlotIndex);
        }

        [TestMethod]
        public void Resolve_NonSearch_LetterMapsToOpenByLetter()
        {
            var r = KeyRouter.Resolve(Defaults(), false, false, false, false, Keys.Q);
            Assert.AreEqual(KeyAction.OpenSlotByLetter, r.Action);
            Assert.AreEqual('Q', r.Letter);
        }

        [TestMethod]
        public void Resolve_NonSearch_DigitWithModifier_IsNotSlotOpen()
        {
            var r = KeyRouter.Resolve(Defaults(), false, true, false, false, Keys.D3);
            Assert.IsFalse(r.Handled, "带 Ctrl 的数字键不应触发打开槽位");
        }

        [TestMethod]
        public void Resolve_NonSearch_JumpConfigured_ReturnsJumpIndex()
        {
            var hk = Defaults();
            hk.Jump = new List<string> { "Ctrl+1", "Ctrl+2", "Ctrl+3" };
            var r = KeyRouter.Resolve(hk, false, true, false, false, Keys.D2);
            Assert.AreEqual(KeyAction.JumpPage, r.Action);
            Assert.AreEqual(1, r.JumpIndex);
        }

        [TestMethod]
        public void Resolve_NonSearch_UnknownKey_NotHandled()
        {
            var r = KeyRouter.Resolve(Defaults(), false, false, false, false, Keys.F9);
            Assert.IsFalse(r.Handled);
        }

        [TestMethod]
        public void Resolve_SearchMode_EnterUpDownEsc()
        {
            Assert.AreEqual(KeyAction.SearchOpenSelected, KeyRouter.Resolve(Defaults(), true, false, false, false, Keys.Enter).Action);
            Assert.AreEqual(KeyAction.SearchMoveUp, KeyRouter.Resolve(Defaults(), true, false, false, false, Keys.Up).Action);
            Assert.AreEqual(KeyAction.SearchMoveDown, KeyRouter.Resolve(Defaults(), true, false, false, false, Keys.Down).Action);
            Assert.AreEqual(KeyAction.SearchClear, KeyRouter.Resolve(Defaults(), true, false, false, false, Keys.Escape).Action);
        }

        [TestMethod]
        public void Resolve_SearchMode_DigitMapsToNthMatch()
        {
            var r = KeyRouter.Resolve(Defaults(), true, false, false, false, Keys.D5);
            Assert.AreEqual(KeyAction.SearchOpenNth, r.Action);
            Assert.AreEqual(4, r.SlotIndex);
        }

        [TestMethod]
        public void Resolve_SearchMode_Letter_IsNotHandled()
        {
            var r = KeyRouter.Resolve(Defaults(), true, false, false, false, Keys.A);
            Assert.IsFalse(r.Handled, "搜索框聚焦时普通字母交给输入");
        }

        [TestMethod]
        public void Resolve_NonSearch_JumpUnknownIndexNotHandled()
        {
            var r = KeyRouter.Resolve(Defaults(), false, false, false, false, Keys.D0);
            Assert.IsFalse(r.Handled);
        }
    }

    [TestClass]
    public class ConfigExporterTests
    {
        private string _dir;

        [TestInitialize]
        public void Init()
        {
            _dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "exp_" + System.IO.Path.GetRandomFileName());
            System.IO.Directory.CreateDirectory(_dir);
        }

        [TestCleanup]
        public void Clean()
        {
            try { System.IO.Directory.Delete(_dir, true); }
            catch { }
        }

        [TestMethod]
        public void CollectConfigFiles_OnlyExisting()
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(_dir, "launcher.json"), "{}");
            System.IO.File.WriteAllText(System.IO.Path.Combine(_dir, "notes.txt"), "hi");
            var files = ConfigExporter.CollectConfigFiles(_dir);
            Assert.AreEqual(2, files.Count);
            Assert.IsTrue(files.Exists(f => f.EndsWith("launcher.json")));
            Assert.IsTrue(files.Exists(f => f.EndsWith("notes.txt")));
        }

        [TestMethod]
        public void CollectIconEntries_DeduplicatesSameFileName()
        {
            var store = new ConfigStore();
            store.PageNames = new List<string> { "p1", "p2" };
            var a = System.IO.Path.Combine(_dir, "a", "icon.png");
            var b = System.IO.Path.Combine(_dir, "b", "icon.png");
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(_dir, "a"));
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(_dir, "b"));
            System.IO.File.WriteAllBytes(a, new byte[] { 1 });
            System.IO.File.WriteAllBytes(b, new byte[] { 2 });
            store.Pages = new Dictionary<string, List<SlotData>>
            {
                { "p1", new List<SlotData> { new SlotData(0) { IconPath = a } } },
                { "p2", new List<SlotData> { new SlotData(0) { IconPath = b } } },
            };
            var entries = ConfigExporter.CollectIconEntries(store);
            Assert.AreEqual(2, entries.Count);
            var names = new HashSet<string>();
            foreach (var e in entries) { Assert.IsTrue(System.IO.File.Exists(e.Item1)); Assert.IsTrue(names.Add(e.Item2)); }
            Assert.IsTrue(entries.Exists(e => e.Item2 == "icons/icon.png"));
            Assert.IsTrue(entries.Exists(e => e.Item2 == "icons/icon_2.png"));
        }

        [TestMethod]
        public void CollectIconEntries_SkipsMissingAndEmpty()
        {
            var store = new ConfigStore();
            store.PageNames = new List<string> { "p1" };
            var page = new List<SlotData>();
            page.Add(new SlotData(0) { IconPath = System.IO.Path.Combine(_dir, "missing.png") });
            page.Add(new SlotData(1) { IconPath = string.Empty });
            page.Add(new SlotData(2));
            store.Pages = new Dictionary<string, List<SlotData>> { { "p1", page } };
            Assert.AreEqual(0, ConfigExporter.CollectIconEntries(store).Count);
        }
    }

    [TestClass]
    public class MarkdownRendererTests
    {
        private static readonly System.Drawing.Color F = System.Drawing.Color.White;
        private static readonly System.Drawing.Color H = System.Drawing.Color.Orange;
        private static readonly System.Drawing.Color L = System.Drawing.Color.Blue;
        private static readonly System.Drawing.Color CF = System.Drawing.Color.Green;
        private static readonly System.Drawing.Color CB = System.Drawing.Color.Gray;

        [TestMethod]
        public void ToRtf_PlainText_ContainsEscapedText()
        {
            string rtf = MarkdownRenderer.ToRtf("hello world", F, H, L, CF, CB);
            StringAssert.Contains(rtf, @"{\rtf1\ansi");
            StringAssert.Contains(rtf, "hello world");
        }

        [TestMethod]
        public void ToRtf_Heading_UsesHeadingColorAndBold()
        {
            string rtf = MarkdownRenderer.ToRtf("# Title", F, H, L, CF, CB);
            StringAssert.Contains(rtf, @"\b\cf2\fs");
            StringAssert.Contains(rtf, "Title");
        }

        [TestMethod]
        public void ToRtf_Bold_ContainsBoldControl()
        {
            string rtf = MarkdownRenderer.ToRtf("**bold**", F, H, L, CF, CB);
            StringAssert.Contains(rtf, @"\b ");
            StringAssert.Contains(rtf, @"\b0 ");
            StringAssert.Contains(rtf, "bold");
        }

        [TestMethod]
        public void ToRtf_Italic_ContainsItalicControl()
        {
            string rtf = MarkdownRenderer.ToRtf("*it*", F, H, L, CF, CB);
            StringAssert.Contains(rtf, @"\i ");
            StringAssert.Contains(rtf, @"\i0 ");
            StringAssert.Contains(rtf, "it");
        }

        [TestMethod]
        public void ToRtf_Code_ContainsCodeFontAndHighlight()
        {
            string rtf = MarkdownRenderer.ToRtf("`x = 1`", F, H, L, CF, CB);
            StringAssert.Contains(rtf, @"\f0\cf4\highlight5 ");
            StringAssert.Contains(rtf, "x = 1");
        }

        [TestMethod]
        public void ToRtf_Link_ContainsHyperlinkField()
        {
            string rtf = MarkdownRenderer.ToRtf("[a](https://example.com)", F, H, L, CF, CB);
            StringAssert.Contains(rtf, "HYPERLINK");
            StringAssert.Contains(rtf, @"\""https://example.com\""");
            StringAssert.Contains(rtf, "a");
        }

        [TestMethod]
        public void ToRtf_ListItem_HasBulletAndIndent()
        {
            string rtf = MarkdownRenderer.ToRtf("- item", F, H, L, CF, CB);
            StringAssert.Contains(rtf, @"\li400");
            StringAssert.Contains(rtf, @"\u8226?");
            StringAssert.Contains(rtf, "item");
        }

        [TestMethod]
        public void ToRtf_EscapesSpecialChars()
        {
            string rtf = MarkdownRenderer.ToRtf(@"\{ \} \\", F, H, L, CF, CB);
            StringAssert.Contains(rtf, @"\{");
            StringAssert.Contains(rtf, @"\}");
            StringAssert.Contains(rtf, @"\\");
        }

        [TestMethod]
        public void ToRtf_Chinese_EncodesAsUnicodeControl()
        {
            string rtf = MarkdownRenderer.ToRtf("你好", F, H, L, CF, CB);
            StringAssert.Contains(rtf, @"\u");
        }

        [TestMethod]
        public void ToRtf_NullOrEmpty_StillProducesValidRtf()
        {
            Assert.IsTrue(MarkdownRenderer.ToRtf(null, F, H, L, CF, CB).StartsWith("{\\rtf1"));
            Assert.IsTrue(MarkdownRenderer.ToRtf("", F, H, L, CF, CB).StartsWith("{\\rtf1"));
        }

        [TestMethod]
        public void ToRtf_BoldContainsItalic_NestedParsing()
        {
            string rtf = MarkdownRenderer.ToRtf("**bold *it* bold**", F, H, L, CF, CB);
            StringAssert.Contains(rtf, "bold");
            StringAssert.Contains(rtf, "it");
        }

        [TestMethod]
        public void ToRtf_UnmatchedMarker_TreatedAsLiteral()
        {
            string rtf = MarkdownRenderer.ToRtf("plain * asterisk", F, H, L, CF, CB);
            StringAssert.Contains(rtf, "plain * asterisk");
        }
    }
}
