using Microsoft.VisualStudio.TestTools.UnitTesting;
using launcher.Controls;
using launcher.Core;
using System;
using System.IO;

namespace Launcher.Tests
{
    [TestClass]
    public class IconRendererTests
    {
        private string _iconsDir;

        [TestInitialize]
        public void Init()
        {
            _iconsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icons");
            Directory.CreateDirectory(_iconsDir);
            File.WriteAllText(Path.Combine(_iconsDir, "fav.ico"), "x");
        }

        [TestCleanup]
        public void Clean()
        {
            try { Directory.Delete(_iconsDir, true); }
            catch { }
        }

        [TestMethod]
        public void IsHttpUrl_RecognizesHttpHttps_NotFiles()
        {
            Assert.IsTrue(UrlUtil.IsHttpUrl("http://example.com"));
            Assert.IsTrue(UrlUtil.IsHttpUrl("https://example.com/a?b=1"));
            Assert.IsFalse(UrlUtil.IsHttpUrl("file:///C:/x"));
            Assert.IsFalse(UrlUtil.IsHttpUrl("C:\\Programs\\app.exe"));
            Assert.IsFalse(UrlUtil.IsHttpUrl(""));
        }

        [TestMethod]
        public void ResolveIconPath_AbsoluteReturnedAsIs()
        {
            string abs = @"C:\icons\my.png";
            Assert.AreEqual(abs, IconRenderer.ResolveIconPath(abs));
        }

        [TestMethod]
        public void ResolveIconPath_RelativeResolvedAndFallsBackToIconsDir()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            Assert.AreEqual(Path.Combine(baseDir, "rel.png"), IconRenderer.ResolveIconPath("rel.png"));
            Assert.AreEqual(Path.Combine(baseDir, "icons", "fav.ico"), IconRenderer.ResolveIconPath("fav.ico"));
        }
    }
}
