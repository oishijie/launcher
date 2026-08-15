using System;
using System.Drawing;
using System.Text;

namespace launcher.Core
{
    // Markdown 子集解析 + RTF 生成（与 UI 解耦，可单测）。
    // 支持的子集：`#`~`######` 标题、**粗体**、*斜体*、`行内代码`、-/*/+ 列表、[文字](链接)。
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
            bool first = true;
            foreach (var raw in lines)
            {
                string line = raw.TrimEnd();
                if (!first) sb.Append(@"\par ");
                first = false;
                if (line.Trim().Length == 0) continue;

                string trimmed = line.TrimStart();

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
