using Microsoft.VisualStudio.TestTools.UnitTesting;
using launcher.Core;
using launcher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Launcher.Tests
{
    [TestClass]
    public class ConfigStoreTests
    {
        private string _dir;

        [TestInitialize]
        public void Init()
        {
            _dir = Path.Combine(Path.GetTempPath(), "cfgtest_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_dir);
        }

        [TestCleanup]
        public void Clean()
        {
            try { Directory.Delete(_dir, true); }
            catch { }
        }

        [TestMethod]
        public void Load_ToleratesBom_InsteadOfEmptyFallback()
        {
            // 带 BOM 的配置应被正常解析（曾因 BOM 回退空配置导致导入/启动丢数据）
            string json = "{\"layout\":{\"pageCount\":2,\"slotCount\":4},\"pages\":[{\"name\":\"第1页\",\"slots\":[{\"index\":0,\"path\":\"a.exe\",\"icon\":\"\"}]},{\"name\":\"第2页\",\"slots\":[]}]}";
            File.WriteAllText(Path.Combine(_dir, "launcher.json"), json, new UTF8Encoding(true));
            var store = new ConfigStore { BaseDirOverride = _dir };
            store.Load();
            Assert.AreEqual(2, store.PageNames.Count, "带 BOM 的配置应解析出 2 页，而非回退空配置");
            Assert.IsTrue(store.Pages.ContainsKey("第1页"));
        }

        [TestMethod]
        public void Save_WritesWithoutBom()
        {
            var store = new ConfigStore { BaseDirOverride = _dir };
            store.Layout.PageCount = 1;
            store.Save();
            byte[] bytes = File.ReadAllBytes(Path.Combine(_dir, "launcher.json"));
            Assert.AreNotEqual(0xEF, bytes[0], "首字节不应是 UTF-8 BOM (EF)");
        }

        [TestMethod]
        public void Save_IsAtomic_NoTempResidue_AndRoundTrips()
        {
            var store = new ConfigStore { BaseDirOverride = _dir };
            store.Layout.PageCount = 1;
            store.PageNames = new List<string> { "panel_dot1" };
            store.Pages = new Dictionary<string, List<SlotData>> { { "panel_dot1", new List<SlotData>() } };
            store.SetSlot("panel_dot1", 0, new SlotData(0) { FilePath = @"C:\Windows\System32\notepad.exe" });
            store.Save();
            Assert.IsTrue(File.Exists(Path.Combine(_dir, "launcher.json")), "应正常写出 launcher.json");
            Assert.IsFalse(File.Exists(Path.Combine(_dir, "launcher.json.tmp")), "原子写不应残留 .tmp");

            var reload = new ConfigStore { BaseDirOverride = _dir };
            reload.Load();
            Assert.AreEqual(1, reload.PageNames.Count);
            Assert.AreEqual(@"C:\Windows\System32\notepad.exe", reload.GetSlot("第1页", 0).FilePath);
        }

        [TestMethod]
        public void RemovePage_ShiftsSubsequentPagesForward()
        {
            var store = new ConfigStore { BaseDirOverride = _dir };
            store.Layout.PageCount = 3;
            store.Layout.SlotCount = 2;
            store.PageNames = new List<string> { "panel_dot1", "panel_dot2", "panel_dot3" };
            store.Pages = new Dictionary<string, List<SlotData>>
            {
                { "panel_dot1", new List<SlotData> { new SlotData(0) { FilePath = "a" } } },
                { "panel_dot2", new List<SlotData> { new SlotData(0) { FilePath = "b" } } },
                { "panel_dot3", new List<SlotData> { new SlotData(0) { FilePath = "c" } } },
            };
            var removed = store.RemovePage(0);
            Assert.IsNotNull(removed);
            Assert.AreEqual(2, store.PageNames.Count);
            Assert.AreEqual("panel_dot1", store.PageNames[0]);
            Assert.AreEqual("panel_dot2", store.PageNames[1]);
            Assert.AreEqual("b", store.Pages["panel_dot1"][0].FilePath);
            Assert.AreEqual("c", store.Pages["panel_dot2"][0].FilePath);
        }

        [TestMethod]
        public void RenamePage_ReKeysDictionaryAndList()
        {
            var store = new ConfigStore { BaseDirOverride = _dir };
            store.Layout.PageCount = 2;
            store.Layout.SlotCount = 2;
            store.PageNames = new List<string> { "panel_dot1", "panel_dot2" };
            store.Pages = new Dictionary<string, List<SlotData>>
            {
                { "panel_dot1", new List<SlotData> { new SlotData(0) { FilePath = "a" } } },
                { "panel_dot2", new List<SlotData> { new SlotData(0) { FilePath = "b" } } },
            };
            Assert.IsTrue(store.RenamePage(0, "工作"));
            Assert.AreEqual("工作", store.PageNames[0]);
            Assert.AreEqual("panel_dot2", store.PageNames[1]);
            Assert.IsFalse(store.Pages.ContainsKey("panel_dot1"), "旧页名应从字典移除");
            Assert.AreEqual("a", store.Pages["工作"][0].FilePath);
        }

        [TestMethod]
        public void RenamePage_UpdatesLastPageAndPersists()
        {
            var store = new ConfigStore { BaseDirOverride = _dir };
            store.Layout.PageCount = 2;
            store.Layout.SlotCount = 1;
            store.PageNames = new List<string> { "panel_dot1", "panel_dot2" };
            store.Pages = new Dictionary<string, List<SlotData>>
            {
                { "panel_dot1", new List<SlotData>() },
                { "panel_dot2", new List<SlotData>() },
            };
            store.LastPage = "panel_dot1";
            Assert.IsTrue(store.RenamePage(0, "常用"));
            Assert.AreEqual("常用", store.LastPage, "LastPage 应随重命名同步");
            store.Save();

            var reload = new ConfigStore { BaseDirOverride = _dir };
            reload.Load();
            Assert.AreEqual("常用", reload.PageNames[0]);
            Assert.AreEqual("常用", reload.LastPage);
        }

        [TestMethod]
        public void RenamePage_RejectsInvalidNames()
        {
            var store = new ConfigStore { BaseDirOverride = _dir };
            store.PageNames = new List<string> { "panel_dot1" };
            store.Pages = new Dictionary<string, List<SlotData>> { { "panel_dot1", new List<SlotData>() } };
            Assert.IsFalse(store.RenamePage(0, ""), "空名应拒绝");
            Assert.IsFalse(store.RenamePage(0, "a/b"), "含路径分隔符应拒绝");
            Assert.IsFalse(store.RenamePage(0, "a\"b"), "含引号应拒绝");
            Assert.IsFalse(store.RenamePage(0, new string('x', 21)), "超长应拒绝");
            Assert.IsFalse(store.RenamePage(0, "panel_dot1"), "重名应拒绝");
            Assert.IsFalse(store.RenamePage(5, "ok"), "越界索引应拒绝");
            Assert.AreEqual("panel_dot1", store.PageNames[0], "非法改名不应改变现有页名");
        }

        [TestMethod]
        public void AddPage_UsesFriendlyName_AndIsUnique()
        {
            var store = new ConfigStore { BaseDirOverride = _dir };
            store.Layout.PageCount = 1;
            store.Layout.SlotCount = 2;
            store.PageNames = new List<string> { "第2页" }; // 预置一个可能冲突的名字
            store.Pages = new Dictionary<string, List<SlotData>> { { "第2页", new List<SlotData>() } };
            int idx = store.AddPage();
            Assert.AreEqual(1, idx);
            Assert.AreEqual("第2页 2", store.PageNames[1], "重名时自动追加序号");
        }

        [TestMethod]
        public void Layout_AbsentWidthHeight_DefaultsToAutoZero()
        {
            var store = new ConfigStore { BaseDirOverride = _dir };
            store.Load();
            Assert.AreEqual(0, store.Layout.Width, "未配置 width 时应为 0(自动)");
            Assert.AreEqual(0, store.Layout.Height, "未配置 height 时应为 0(自动)");
        }

        [TestMethod]
        public void Layout_WidthHeight_RoundTripPersists()
        {
            var store = new ConfigStore { BaseDirOverride = _dir };
            store.Layout.PageCount = 1;
            store.PageNames = new List<string> { "panel_dot1" };
            store.Pages = new Dictionary<string, List<SlotData>> { { "panel_dot1", new List<SlotData>() } };
            store.Layout.Width = 900;
            store.Layout.Height = 600;
            store.Save();

            var reload = new ConfigStore { BaseDirOverride = _dir };
            reload.Load();
            Assert.AreEqual(900, reload.Layout.Width, "width 应持久化");
            Assert.AreEqual(600, reload.Layout.Height, "height 应持久化");
        }

        [TestMethod]
        public void Layout_WidthHeight_ClampedToSafeBounds()
        {
            string json = "{\"layout\":{\"width\":99999,\"height\":-5,\"pageCount\":1,\"slotCount\":1},\"pages\":[{\"name\":\"panel_dot1\",\"slots\":[]}]}";
            File.WriteAllText(Path.Combine(_dir, "launcher.json"), json);
            var store = new ConfigStore { BaseDirOverride = _dir };
            store.Load();
            Assert.AreEqual(4000, store.Layout.Width, "超大 width 应收敛到上限");
            Assert.AreEqual(0, store.Layout.Height, "负 height 应收敛到 0(自动)");
        }

        [TestMethod]
        public void Layout_DblClickShow_DefaultsEnabled_RoundTrips()
        {
            var store = new ConfigStore { BaseDirOverride = _dir };
            store.Load();
            Assert.IsTrue(store.Layout.DblClickShowEffective, "缺省应启用双击呼出");

            store.Layout.DblClickShow = false;
            store.Layout.PageCount = 1;
            store.PageNames = new List<string> { "panel_dot1" };
            store.Pages = new Dictionary<string, List<SlotData>> { { "panel_dot1", new List<SlotData>() } };
            store.Save();

            var reload = new ConfigStore { BaseDirOverride = _dir };
            reload.Load();
            Assert.IsFalse(reload.Layout.DblClickShowEffective, "显式 false 应持久化并生效");
        }
    }
}
