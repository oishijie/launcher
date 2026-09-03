using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace launcher.Core
{
    // Markdown 子集解析 + RTF 生成（与 UI 解耦，可单测）。
    // 支持的子集：`#`~`######` 标题、**粗体**、*斜体*、`行内代码`、-/*/+ 列表、[文字](链接)、
    //             | 管道表格（含 |---| 分隔行、:---: 对齐）。
    // 输出可直接赋给 RichTextBox.Rtf；链接用 RTF HYPERLINK 字段，RichTextBox 触发 LinkClicked。
    public static class MarkdownRenderer
    {
        // markdown -> RTF。颜色参数：正文、标题、链接、代码前景、代码背景。
        public static string ToRtf(string markdown, Color textFore, Color heading, Color link,
            Color codeFore, Color codeBack)
        {
            if (markdown == null) markdown = string.Empty;

            var sb = new StringBuilder();
            sb.Append(@"{\rtf1\ansi\deff0 {\fonttbl {\f0 Consolas;}{\f1 Microsoft YaHei;}}");
            sb.Append(@"{\colortbl ;");
            sb.Append(Col(textFore)).Append(";");   // 1 正文
            sb.Append(Col(heading)).Append(";");    // 2 标题
            sb.Append(Col(link)).Append(";");       // 3 链接
            sb.Append(Col(codeFore)).Append(";");   // 4 代码前景
            sb.Append(Col(codeBack)).Append(";}");  // 5 代码背景
            sb.Append(@"\f1\fs20\cf1 ");

            string[] lines = markdown.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd();
                if (i > 0) sb.Append(@"\par ");
                if (line.Trim().Length == 0) continue;

                string trimmed = line.TrimStart();

                // 表格：当前行以 | 开头，且下一行是 |---|---| 分隔行
                if (trimmed.StartsWith("|") && i + 1 < lines.Length && IsTableSeparator(lines[i + 1]))
                {
                    // 收集从分隔行之后的全部数据行
                    int end = i + 2;
                    while (end < lines.Length && lines[end].TrimStart().StartsWith("|")) end++;

                    var header = SplitTableRow(trimmed);
                    var rows = new List<List<string>>();
                    for (int r = i + 2; r < end; r++) rows.Add(SplitTableRow(lines[r]));
                    var aligns = ParseAligns(SplitTableRow(lines[i + 1]), header.Count);

                    EmitTable(sb, header, rows, aligns);
                    i = end - 1; // 跳过已消费的行（外层 for 会再 +1）
                    continue;
                }

                // 标题：# ~ ###### 后跟空格
                int level = 0;
                while (level < trimmed.Length && level < 6 && trimmed[level] == '#') level++;
                if (level >= 1 && (level == trimmed.Length || trimmed[level] == ' '))
                {
                    string content = trimmed.Substring(level).TrimStart();
                    sb.Append(@"\b\cf2\fs").Append(20 + (6 - level) * 4).Append(" ");
                    WriteInline(sb, content);
                    sb.Append(@"\cf1\fs20\b0 ");
                    continue;
                }

                // 列表项：- / * / + 后跟空格
                if (trimmed.StartsWith("- ") || trimmed.StartsWith("* ") || trimmed.StartsWith("+ "))
                {
                    sb.Append(@"\li400\u8226?\tab ");
                    WriteInline(sb, trimmed.Substring(2));
                    continue;
                }

                WriteInline(sb, trimmed);
            }
            sb.Append("}");
            return sb.ToString();
        }

        // ===== 表格支持 =====
        // 分隔行：只含 | - : 空格，且至少有一个 -
        private static bool IsTableSeparator(string line)
        {
            string t = (line ?? "").Trim();
            if (t.Length == 0 || t.IndexOf('-') < 0) return false;
            foreach (char c in t)
                if (c != '|' && c != '-' && c != ':' && c != ' ') return false;
            return true;
        }

        // 拆一行表格为单元格（去掉首尾 |，按 | 分割，支持转义 \|）
        private static List<string> SplitTableRow(string line)
        {
            string t = (line ?? "").Trim();
            if (t.StartsWith("|")) t = t.Substring(1);
            if (t.EndsWith("|")) t = t.Substring(0, t.Length - 1);
            var cells = new List<string>();
            var cur = new StringBuilder();
            for (int i = 0; i < t.Length; i++)
            {
                if (t[i] == '\\' && i + 1 < t.Length && t[i + 1] == '|') { cur.Append('|'); i++; }
                else if (t[i] == '|') { cells.Add(cur.ToString().Trim()); cur.Length = 0; }
                else cur.Append(t[i]);
            }
            cells.Add(cur.ToString().Trim());
            return cells;
        }

        // 从分隔行解析每列对齐：0 左 / 1 中 / 2 右
        private static int[] ParseAligns(List<string> sepCells, int colCount)
        {
            var aligns = new int[colCount];
            for (int c = 0; c < colCount && c < sepCells.Count; c++)
            {
                string s = sepCells[c];
                bool left = s.StartsWith(":"), right = s.EndsWith(":");
                if (left && right) aligns[c] = 1;
                else if (right) aligns[c] = 2;
            }
            return aligns;
        }

        // 生成 RTF 表格：带边框，表头加粗+底纹，列宽按内容长度比例分配
        private static void EmitTable(StringBuilder sb, List<string> header, List<List<string>> rows, int[] aligns)
        {
            int cols = header.Count;
            // 每列最大字符宽（中文按 2 计），用于分配列宽
            var maxLen = new int[cols];
            for (int c = 0; c < cols; c++) maxLen[c] = DisplayWidth(header[c]);
            foreach (var row in rows)
                for (int c = 0; c < cols && c < row.Count; c++)
                    maxLen[c] = Math.Max(maxLen[c], DisplayWidth(row[c]));

            // 总宽约 7600 twips（≈507px，适配便签面板文本区），单列下限 900
            const int totalW = 7600, minW = 900;
            int sum = 0; for (int c = 0; c < cols; c++) sum += Math.Max(3, maxLen[c]);
            var widths = new int[cols];
            int used = 0;
            for (int c = 0; c < cols; c++)
            {
                widths[c] = Math.Max(minW, totalW * Math.Max(3, maxLen[c]) / sum);
                used += widths[c];
            }
            // 归一化到 totalW（按比例压缩超出部分）
            if (used > totalW)
                for (int c = 0; c < cols; c++) widths[c] = widths[c] * totalW / used;

            // 单元格边框定义（细线 0.5pt）
            string border = @"\clbrdrt\brdrs\brdrw10\clbrdl\brdrs\brdrw10\clbrdrb\brdrs\brdrw10\clbrdrr\brdrs\brdrw10";

            // 表头行（加粗 + 代码背景色底纹）
            sb.Append(@"\pard\sa0\sl0").Append(@"\trowd\trgaph80\trleft0");
            int x = 0;
            for (int c = 0; c < cols; c++)
            {
                x += widths[c];
                sb.Append(@"\clcbpat5").Append(border).Append(@"\cellx").Append(x);
            }
            sb.Append(' ');
            for (int c = 0; c < cols; c++)
            {
                sb.Append(@"\b ");
                WriteCellContent(sb, CellAt(header, c), aligns[c]);
                sb.Append(@"\b0\cell ");
            }
            sb.Append(@"\row ");

            // 数据行
            foreach (var row in rows)
            {
                sb.Append(@"\trowd\trgaph80\trleft0");
                x = 0;
                for (int c = 0; c < cols; c++)
                {
                    x += widths[c];
                    sb.Append(border).Append(@"\cellx").Append(x);
                }
                sb.Append(' ');
                for (int c = 0; c < cols; c++)
                {
                    WriteCellContent(sb, CellAt(row, c), aligns[c]);
                    sb.Append(@"\cell ");
                }
                sb.Append(@"\row ");
            }
            sb.Append(@"\pard\sa80 "); // 表格结束后恢复段落间距
        }

        // 单元格内容：支持对齐 + 行内格式（粗体/斜体/代码/链接）
        private static void WriteCellContent(StringBuilder sb, string cell, int align)
        {
            if (align == 1) sb.Append(@"\qc ");
            else if (align == 2) sb.Append(@"\qr ");
            WriteInline(sb, cell ?? string.Empty);
            if (align != 0) sb.Append(@"\ql ");
        }

        private static string CellAt(List<string> row, int idx)
            => idx < row.Count ? row[idx] : string.Empty;

        // 显示宽度：CJK 字符按 2、其余按 1
        private static int DisplayWidth(string s)
        {
            int w = 0;
            foreach (char ch in (s ?? string.Empty))
                w += (ch >= 0x2E80 && ch <= 0x9FFF) || (ch >= 0xF900 && ch <= 0xFAFF) || (ch >= 0xFF00 && ch <= 0xFF60) ? 2 : 1;
            return w;
        }

        // 行内解析：递归处理 **粗体**、*斜体*、`代码`、[文字](链接)，其余原文追加
        private static void WriteInline(StringBuilder rtf, string s)
        {
            int i = 0;
            while (i < s.Length)
            {
                // 链接 [label](url)
                if (s[i] == '[')
                {
                    int close = s.IndexOf(']', i);
                    if (close > i && close + 1 < s.Length && s[close + 1] == '(')
                    {
                        int end = s.IndexOf(')', close + 2);
                        if (end > close + 2)
                        {
                            string label = s.Substring(i + 1, close - i - 1);
                            string url = s.Substring(close + 2, end - close - 2);
                            rtf.Append(@"{\field{\*\fldinst{HYPERLINK \""").Append(EscapeUrl(url))
                               .Append(@"\""}}{\fldrslt{\ul\cf3 ").Append(EscapeText(label)).Append("}}}");
                            i = end + 1;
                            continue;
                        }
                    }
                    rtf.Append(EscapeText("["));
                    i++;
                    continue;
                }

                // 粗体 **text**
                if (s[i] == '*' && i + 1 < s.Length && s[i + 1] == '*')
                {
                    int close = s.IndexOf("**", i + 2);
                    if (close > i + 2)
                    {
                        rtf.Append(@"\b ");
                        WriteInline(rtf, s.Substring(i + 2, close - i - 2));
                        rtf.Append(@"\b0 ");
                        i = close + 2;
                        continue;
                    }
                    rtf.Append(EscapeText("**"));
                    i += 2;
                    continue;
                }

                // 斜体 *text*
                if (s[i] == '*')
                {
                    int close = s.IndexOf('*', i + 1);
                    if (close > i)
                    {
                        rtf.Append(@"\i ");
                        WriteInline(rtf, s.Substring(i + 1, close - i - 1));
                        rtf.Append(@"\i0 ");
                        i = close + 1;
                        continue;
                    }
                    rtf.Append(EscapeText("*"));
                    i++;
                    continue;
                }

                // 行内代码 `text`
                if (s[i] == '`')
                {
                    int close = s.IndexOf('`', i + 1);
                    if (close > i)
                    {
                        string code = s.Substring(i + 1, close - i - 1);
                        rtf.Append(@"\f0\cf4\highlight5 ").Append(EscapeText(code))
                           .Append(@"\highlight0\cf1\f1 ");
                        i = close + 1;
                        continue;
                    }
                    rtf.Append(EscapeText("`"));
                    i++;
                    continue;
                }

                // 普通文本：一直追加到下一个特殊标记
                int next = s.Length;
                int idxStar = s.IndexOf('*', i);
                if (idxStar >= 0 && idxStar < next) next = idxStar;
                int idxTick = s.IndexOf('`', i);
                if (idxTick >= 0 && idxTick < next) next = idxTick;
                int idxBracket = s.IndexOf('[', i);
                if (idxBracket >= 0 && idxBracket < next) next = idxBracket;
                if (next > i)
                {
                    rtf.Append(EscapeText(s.Substring(i, next - i)));
                    i = next;
                }
                else
                {
                    rtf.Append(EscapeText(s[i].ToString()));
                    i++;
                }
            }
        }

        private static string Col(Color c) => string.Format(@"\red{0}\green{1}\blue{2}", c.R, c.G, c.B);

        private static string EscapeUrl(string u)
        {
            return u.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        // RTF 文本转义：\ { } 反斜杠转义；非 ASCII 用 \uN? 编码（>32767 用有符号负数表示）
        private static string EscapeText(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c == '\\') sb.Append(@"\\");
                else if (c == '{') sb.Append(@"\{");
                else if (c == '}') sb.Append(@"\}");
                else if (c == '\n') sb.Append(@"\par ");
                else if (c < 128) sb.Append(c);
                else
                {
                    int u = c;
                    if (u > 32767) u -= 65536;
                    sb.Append(@"\u").Append(u).Append("?");
                }
            }
            return sb.ToString();
        }
    }
}
