using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using launcher.Core;

namespace launcher.Controls
{
    // 图标与文件名渲染：从 Form1 中抽取的纯静态工具类（单一职责、可独立测试）
    internal static class IconRenderer
    {
        // 渲染一个槽位到位图（2x 超采样后缩回原尺寸，保证清晰），返回位图由调用方持有
        public static Image RenderToBitmap(int width, int height, string filePath, ThemePalette palette, string iconPath = null)
        {
            using (Bitmap bmp = new Bitmap(width * 2, height * 2))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.ScaleTransform(2, 2);
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                Draw(g, width, height, filePath, palette, iconPath);

                using (Bitmap finalBmp = new Bitmap(width, height))
                using (Graphics gf = Graphics.FromImage(finalBmp))
                {
                    gf.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    gf.DrawImage(bmp, 0, 0, width, height);
                    return (Image)finalBmp.Clone();
                }
            }
        }

        public static void Draw(Graphics g, int width, int height, string filePath, ThemePalette palette, string iconPath = null)
        {
            if (string.IsNullOrEmpty(filePath)) return;
            DrawIconPart(g, width, height, filePath, iconPath);
            DrawLabel(g, width, height, filePath, palette);
        }

        // 渲染纯图标部分（不含文字标签），2x 超采样后缩回原尺寸。用于 RawIcon 缓存。
        public static Image RenderIconOnly(int width, int height, string filePath, string iconPath = null)
        {
            using (Bitmap bmp = new Bitmap(width * 2, height * 2))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.ScaleTransform(2, 2);
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                DrawIconPart(g, width, height, filePath, iconPath);

                using (Bitmap finalBmp = new Bitmap(width, height))
                using (Graphics gf = Graphics.FromImage(finalBmp))
                {
                    gf.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    gf.DrawImage(bmp, 0, 0, width, height);
                    return (Image)finalBmp.Clone();
                }
            }
        }

        // 将缓存的纯图标位图与当前主题的文字标签合成为最终位图。
        // 主题切换时调用此方法，避免重新提取系统图标（P/Invoke + 磁盘 I/O）。
        public static Image ComposeBitmap(Image rawIcon, int width, int height, string filePath, ThemePalette palette)
        {
            var result = new Bitmap(width, height);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.DrawImage(rawIcon, 0, 0, width, height);
                DrawLabel(g, width, height, filePath, palette);
            }
            return result;
        }

        // 纯图标绘制（系统图标 / 自定义图标 / URL 地球），不含文字标签
        private static void DrawIconPart(Graphics g, int width, int height, string filePath, string iconPath = null)
        {
            if (string.IsNullOrEmpty(filePath)) return;
            bool isFolder = Directory.Exists(filePath);
            bool isUrl = UrlUtil.IsHttpUrl(filePath);

            int iconSize = Math.Min(width, height) - 4;
            int iconY = (height - iconSize) / 2;
            int cx = width / 2;

            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            bool hasCustom = !string.IsNullOrWhiteSpace(iconPath) && File.Exists(ResolveIconPath(iconPath));
            if (hasCustom)
            {
                using (var img = LoadIconImage(iconPath))
                {
                    if (img != null)
                        g.DrawImage(img, new Rectangle(cx - iconSize / 2, iconY, iconSize, iconSize));
                    else
                        DrawSystemIcon(g, filePath, isFolder, cx, iconY, iconSize);
                }
            }
            else if (isUrl)
            {
                DrawGlobe(g, new Rectangle(cx - iconSize / 2, iconY, iconSize, iconSize));
            }
            else
            {
                DrawSystemIcon(g, filePath, isFolder, cx, iconY, iconSize);
            }
        }

        // 文字标签绘制（文件名/域名），颜色随主题变化
        private static void DrawLabel(Graphics g, int width, int height, string filePath, ThemePalette palette)
        {
            if (string.IsNullOrEmpty(filePath)) return;
            string fileName = Path.GetFileName(filePath);
            bool isFolder = Directory.Exists(filePath);
            string ext = Path.GetExtension(filePath).ToLower();
            bool isUrl = UrlUtil.IsHttpUrl(filePath);

            bool isTxt = ext == ".txt";
            bool isImage = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".ico", ".tiff",
                ".webp",".url",".bat",".toml",".md",".json",".yaml",".yml",".xml",
                ".zip",".rdp" }.Contains(ext);

            bool shouldShowName = isFolder || isTxt || isImage || isUrl;
            if (!shouldShowName) return;

            int iconSize = Math.Min(width, height) - 4;
            int iconY = (height - iconSize) / 2;

            string displayName = fileName;
            if (isUrl)
            {
                try { displayName = new Uri(filePath).Host; } catch { }
            }

            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                sf.Trimming = StringTrimming.EllipsisCharacter;

                Rectangle textRect = new Rectangle(2, iconY + (iconSize * 3 / 4), width - 4, iconSize / 4);

                using (Font font = new Font("微软雅黑", 9, FontStyle.Bold))
                using (GraphicsPath path = new GraphicsPath())
                {
                    float emSize = font.Size * g.DpiY / 72;
                    path.AddString(displayName, font.FontFamily, (int)font.Style, emSize, textRect, sf);
                    using (Pen pen = new Pen(palette.NameStroke, 3))
                    {
                        pen.LineJoin = LineJoin.Round;
                        g.DrawPath(pen, path);
                    }
                    using (Brush fill = new SolidBrush(palette.NameFill))
                    {
                        g.FillPath(fill, path);
                    }
                }
            }
        }

        // 系统文件/文件夹图标（.url 链接用信息图标）
        private static void DrawSystemIcon(Graphics g, string filePath, bool isFolder, int cx, int iconY, int iconSize)
        {
            Icon icon = isFolder ? GetFolderIcon() : GetFileIcon(filePath);
            if (icon == null) return;
            try
            {
                if (icon.Width >= 64)
                {
                    g.DrawIcon(icon, new Rectangle(cx - iconSize / 2, iconY, iconSize, iconSize));
                }
                else
                {
                    using (Bitmap iconBmp = icon.ToBitmap())
                    {
                        g.DrawImage(iconBmp,
                            new Rectangle(cx - iconSize / 2, iconY, iconSize, iconSize),
                            new Rectangle(0, 0, iconBmp.Width, iconBmp.Height),
                            GraphicsUnit.Pixel);
                    }
                }
            }
            finally
            {
                icon.Dispose();
            }
        }

        // 地球/链接字形：蓝色实心圆 + 经纬线（互联网图标通用蓝，主题无关）
        private static void DrawGlobe(Graphics g, Rectangle rect)
        {
            Color ocean = Color.FromArgb(38, 115, 230);
            Color line = Color.FromArgb(200, 225, 255);
            Color outline = Color.FromArgb(20, 50, 120);
            using (var brush = new SolidBrush(ocean))
                g.FillEllipse(brush, rect);
            using (var pen = new Pen(line, Math.Max(1, rect.Width / 24)))
            {
                g.DrawEllipse(pen, rect.X + rect.Width * 0.18f, rect.Y, rect.Width * 0.64f, rect.Height);
                g.DrawEllipse(pen, rect.X, rect.Y + rect.Height * 0.18f, rect.Width, rect.Height * 0.64f);
                g.DrawLine(pen, rect.X, rect.Y + rect.Height / 2, rect.X + rect.Width, rect.Y + rect.Height / 2);
            }
            using (var op = new Pen(outline, Math.Max(1, rect.Width / 32)))
                g.DrawEllipse(op, rect);
        }

        // 自定义图标路径：绝对路径原样，相对路径按程序目录解析
        public static string ResolveIconPath(string iconPath)
        {
            if (string.IsNullOrWhiteSpace(iconPath)) return iconPath;
            string resolved = Path.IsPathRooted(iconPath)
                ? iconPath
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, iconPath);
            // 回退：原路径不存在时，尝试程序目录下的 icons/ 子目录
            // （从其他机器导入配置时，自定义图标随包放在 icons/ 下，靠文件名 stem 匹配找回）
            if (!File.Exists(resolved))
            {
                try
                {
                    string iconsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icons");
                    if (Directory.Exists(iconsDir))
                    {
                        string stem = Path.GetFileNameWithoutExtension(iconPath);
                        string cand = Directory.GetFiles(iconsDir)
                            .FirstOrDefault(f => string.Equals(Path.GetFileNameWithoutExtension(f), stem, StringComparison.OrdinalIgnoreCase));
                        if (cand != null) resolved = cand;
                    }
                }
                catch { }
            }
            return resolved;
        }

        // 读取用户提供的图标图片（拷贝到独立位图，避免占用原文件）
        private static Image LoadIconImage(string iconPath)
        {
            string resolved = ResolveIconPath(iconPath);
            if (!File.Exists(resolved)) return null;
            try
            {
                using (var fs = new FileStream(resolved, FileMode.Open, FileAccess.Read))
                using (var tmp = (Image)Image.FromStream(fs))
                {
                    return new Bitmap(tmp);
                }
            }
            catch { return null; }
        }

        public static Icon GetFileIcon(string filePath)
        {
            try
            {
                // 如果是快捷方式，获取目标路径
                if (Path.GetExtension(filePath).ToLower() == ".lnk")
                {
                    string targetPath = GetShortcutTargetFile(filePath);
                    if (File.Exists(targetPath))
                    {
                        filePath = targetPath;
                    }
                }

                // .url 是网页链接，返回一个默认链接图标即可
                if (Path.GetExtension(filePath).ToLower() == ".url")
                {
                    return SystemIcons.Information;
                }

                // 如果文件不存在，返回系统默认图标
                if (!File.Exists(filePath))
                {
                    return SystemIcons.Application;
                }

                try
                {
                    Icon icon = Icon.ExtractAssociatedIcon(filePath);
                    if (icon != null)
                    {
                        return icon;
                    }
                }
                catch
                {
                    IntPtr[] largeIcons = new IntPtr[1];
                    IntPtr[] smallIcons = new IntPtr[1];
                    int iconCount = ExtractIconEx(filePath, 0, largeIcons, smallIcons, 1);
                    if (iconCount > 0 && largeIcons[0] != IntPtr.Zero)
                    {
                        Icon icon = Icon.FromHandle(largeIcons[0]);
                        if (smallIcons[0] != IntPtr.Zero)
                        {
                            DestroyIcon(smallIcons[0]);
                        }
                        return icon;
                    }
                }

                return SystemIcons.Application;
            }
            catch
            {
                return SystemIcons.Application;
            }
        }

        public static Icon GetFolderIcon()
        {
            IntPtr[] largeIcons = new IntPtr[1];
            IntPtr[] smallIcons = new IntPtr[1];

            ExtractIconEx("shell32.dll", 3, largeIcons, smallIcons, 1);

            if (largeIcons[0] != IntPtr.Zero)
            {
                Icon icon = Icon.FromHandle(largeIcons[0]);
                if (smallIcons[0] != IntPtr.Zero)
                {
                    DestroyIcon(smallIcons[0]);
                }
                return icon;
            }

            return Icon.ExtractAssociatedIcon(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        }

        public static string GetShortcutTargetFile(string shortcutFilename)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                dynamic shell = Activator.CreateInstance(t);
                var shortcut = shell.CreateShortcut(shortcutFilename);
                string targetFile = shortcut.TargetPath;
                Marshal.FinalReleaseComObject(shell);
                return targetFile;
            }
            catch
            {
                return shortcutFilename;
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern int ExtractIconEx(string szFileName, int nIconIndex,
            IntPtr[] phiconLarge, IntPtr[] phiconSmall, int nIcons);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);
    }
}
