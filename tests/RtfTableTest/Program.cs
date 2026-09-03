using System;
using System.Drawing;
using System.Windows.Forms;

// 表格渲染验证程序：加载 launcher.exe，调用 MarkdownRenderer.ToRtf 生成含表格的 RTF，
// 塞进 RichTextBox 显示，人工确认 + 自动断言关键字段。
class RtfTableTest
{
    [STAThread]
    static void Main()
    {
        var asm = System.Reflection.Assembly.LoadFrom(@"C:\Users\administraor\Desktop\launcher\bin\Release\launcher.exe");
        var md = asm.GetType("launcher.Core.MarkdownRenderer");
        var method = md.GetMethod("ToRtf");

        string test = "# 性价比梯队\n\n" +
            "| 模型 | 输入(¥/M) | 输出(¥/M) | 评价 |\n" +
            "|------|-----------|-----------|------|\n" +
            "| **GLM-5.3-Flash** | 0.4 | 1.4 | 折后无敌，适合高频对话 |\n" +
            "| Qwen3.7-Flash | 1.2 | 4.8 | 视觉+推理全支持 |\n" +
            "| Mimo-V2.5-Pro | 3 | 6 | 输出价最低 |\n\n" +
            "- 列表项测试\n" +
            "普通文本 [链接](https://example.com)\n\n" +
            "| 左对齐 | 居中 | 右对齐 |\n" +
            "|:---|:---:|---:|\n" +
            "| a | b | c |";

        var args = new object[] {
            test,
            Color.White, Color.DodgerBlue, Color.LightBlue,
            Color.Gray, Color.FromArgb(40, 40, 40)
        };
        string rtf = (string)method.Invoke(null, args);

        Console.WriteLine("RTF length: " + rtf.Length);
        Console.WriteLine("trowd: " + rtf.Contains("trowd"));
        Console.WriteLine("cellx: " + rtf.Contains("cellx"));
        Console.WriteLine("row : " + rtf.Contains("\\row"));
        Console.WriteLine("居中: " + rtf.Contains("\\qc"));
        Console.WriteLine("右对齐: " + rtf.Contains("\\qr"));

        var form = new Form
        {
            Text = "便签表格渲染验证",
            Width = 720, Height = 520,
            BackColor = Color.Black
        };
        var rtb = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Black,
            ForeColor = Color.White,
            BorderStyle = BorderStyle.None,
            Font = new Font("微软雅黑", 9.75f)
        };
        rtb.Rtf = rtf;
        form.Controls.Add(rtb);
        form.ShowDialog();
    }
}
