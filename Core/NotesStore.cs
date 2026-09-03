using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace launcher.Core
{
    // 多便签存储（纯逻辑，可单测）：
    // - 每条便签一个 .txt 文件，存放在程序目录下 notes/ 子目录
    // - 文件名即便签名（不含扩展名），列表按名称排序，边缘整齐排列
    // - 兼容迁移：首次使用时若存在旧版单文件 notes.txt，自动迁移为「便签 1」
    public class NotesStore
    {
        private readonly string _dir;
        private string _current; // 当前便签名（不含扩展名）

        public NotesStore(string baseDir)
        {
            _dir = Path.Combine(baseDir ?? AppDomain.CurrentDomain.BaseDirectory, "notes");
        }

        public string NotesDirectory => _dir;
        public string CurrentName => _current;

        // ===== 初始化 =====
        // 加载便签目录：不存在则创建；空则尝试迁移旧 notes.txt，再不行就建「便签 1」。
        // 返回当前便签名。
        public string Load()
        {
            try { Directory.CreateDirectory(_dir); }
            catch { }

            var names = ListNames();
            if (names.Count == 0)
            {
                MigrateLegacyNotes();
                names = ListNames();
                if (names.Count == 0)
                {
                    CreateNote("便签 1");
                    names = ListNames();
                }
            }
            _current = names[0];
            return _current;
        }

        // 旧版单文件 notes.txt → 迁移为 notes/便签 1.txt（成功后旧文件改名备份）
        public bool MigrateLegacyNotes()
        {
            try
            {
                string legacy = Path.Combine(Path.GetDirectoryName(_dir), "notes.txt");
                if (!File.Exists(legacy)) return false;
                string content = File.ReadAllText(legacy, Encoding.UTF8);
                string target = UniqueFileName("便签 1");
                File.WriteAllText(Path.Combine(_dir, target + ".txt"), content ?? string.Empty, Encoding.UTF8);
                try { File.Move(legacy, legacy + ".bak"); } catch { }
                return true;
            }
            catch { return false; }
        }

        // ===== 列表 =====
        // 所有便签名（排序：自然顺序，数字后缀按数值排，避免 便签10 排在 便签2 前面）
        public List<string> ListNames()
        {
            var result = new List<string>();
            try
            {
                if (!Directory.Exists(_dir)) return result;
                foreach (var f in Directory.GetFiles(_dir, "*.txt"))
                    result.Add(Path.GetFileNameWithoutExtension(f));
            }
            catch { }
            result.Sort(NaturalCompare);
            return result;
        }

        // 自然排序比较："便签 2" < "便签 10"
        public static int NaturalCompare(string a, string b)
        {
            int ia = 0, ib = 0;
            while (ia < a.Length && ib < b.Length)
            {
                char ca = a[ia], cb = b[ib];
                if (char.IsDigit(ca) && char.IsDigit(cb))
                {
                    // 提取完整数字段比较数值
                    long na = 0, nb = 0;
                    int sa = ia, sb = ib;
                    while (ia < a.Length && char.IsDigit(a[ia])) { na = na * 10 + (a[ia] - '0'); ia++; }
                    while (ib < b.Length && char.IsDigit(b[ib])) { nb = nb * 10 + (b[ib] - '0'); ib++; }
                    if (na != nb) return na.CompareTo(nb);
                }
                else
                {
                    int cmp = char.ToUpperInvariant(ca).CompareTo(char.ToUpperInvariant(cb));
                    if (cmp != 0) return cmp;
                    ia++; ib++;
                }
            }
            return (a.Length - ia).CompareTo(b.Length - ib);
        }

        // ===== 读写 =====
        public string Read(string name)
        {
            try
            {
                string p = FilePath(name);
                return File.Exists(p) ? File.ReadAllText(p, Encoding.UTF8) : string.Empty;
            }
            catch { return string.Empty; }
        }

        public bool Write(string name, string content)
        {
            try
            {
                Directory.CreateDirectory(_dir);
                File.WriteAllText(FilePath(name), content ?? string.Empty, Encoding.UTF8);
                return true;
            }
            catch { return false; }
        }

        // ===== 新建 / 删除 / 重命名 =====
        // 新建便签：名字空则自动编号「便签 N」；重名自动加后缀。返回实际名字（失败返回 null）
        public string CreateNote(string desiredName)
        {
            try
            {
                Directory.CreateDirectory(_dir);
                string name = string.IsNullOrWhiteSpace(desiredName) ? NextAutoName() : desiredName.Trim();
                name = UniqueFileName(name);
                File.WriteAllText(Path.Combine(_dir, name + ".txt"), string.Empty, Encoding.UTF8);
                return name;
            }
            catch { return null; }
        }

        // 下一个自动编号：已有 便签1/便签3 → 返回「便签 4」
        public string NextAutoName()
        {
            var names = ListNames();
            int max = 0;
            foreach (var n in names)
            {
                // 匹配「便签 数字」或「便签数字」
                string s = n.Replace("便签", "").Trim();
                int v;
                if (int.TryParse(s, out v) && v > max) max = v;
            }
            return "便签 " + (max + 1);
        }

        public bool Delete(string name)
        {
            try
            {
                string p = FilePath(name);
                if (!File.Exists(p)) return false;
                File.Delete(p);
                // 删的是当前便签 → 切到列表第一条；全删光了自动补一条
                if (_current == name)
                {
                    var names = ListNames();
                    if (names.Count > 0) _current = names[0];
                    else _current = CreateNote(NextAutoName()) ?? string.Empty;
                }
                return true;
            }
            catch { return false; }
        }

        public bool Rename(string oldName, string newName)
        {
            try
            {
                newName = (newName ?? "").Trim();
                if (string.IsNullOrEmpty(newName)) return false;
                if (newName == oldName) return true;
                if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
                string src = FilePath(oldName);
                if (!File.Exists(src)) return false;
                string dst = UniqueFileName(newName);
                File.Move(src, Path.Combine(_dir, dst + ".txt"));
                if (_current == oldName) _current = dst;
                return true;
            }
            catch { return false; }
        }

        // 当前便签切换
        public bool SetCurrent(string name)
        {
            if (!File.Exists(FilePath(name))) return false;
            _current = name;
            return true;
        }

        // ===== 工具 =====
        private string FilePath(string name) => Path.Combine(_dir, (name ?? "").Trim() + ".txt");

        // 重名自动追加 " 2"/" 3"…
        private string UniqueFileName(string desired)
        {
            var taken = new HashSet<string>(ListNames(), StringComparer.OrdinalIgnoreCase);
            if (!taken.Contains(desired)) return desired;
            int n = 2;
            while (taken.Contains(desired + " " + n)) n++;
            return desired + " " + n;
        }
    }
}
