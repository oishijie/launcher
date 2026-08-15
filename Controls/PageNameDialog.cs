using System;
using System.Drawing;
using System.Windows.Forms;
using launcher.Core;

namespace launcher.Controls
{
    // 重命名页面对话框：随主题配色，返回用户输入的页名；取消或空值返回 null。
    public class PageNameDialog : Form
    {
        private readonly TextBox _nameBox;

        public string PageName { get; private set; }

        public PageNameDialog(string title, string initialName, ThemePalette palette)
        {
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.ShowInTaskbar = false;
            this.BackColor = palette.FormBack;
            this.ForeColor = palette.TextFore;
            this.Text = title;
            this.ClientSize = new Size(320, 118);

            var label = new Label
            {
                Text = "页面名称：",
                ForeColor = palette.TextFore,
                AutoSize = true,
                Location = new Point(14, 16)
            };

            _nameBox = new TextBox
            {
                Text = initialName ?? string.Empty,
                BackColor = palette.TextBack,
                ForeColor = palette.TextFore,
                Location = new Point(14, 42),
                Size = new Size(292, 24),
                BorderStyle = BorderStyle.FixedSingle
            };

            var ok = new Button
            {
                Text = "确定",
                BackColor = palette.MenuBack,
                ForeColor = palette.MenuFore,
                FlatStyle = FlatStyle.Flat,
                DialogResult = DialogResult.OK,
                Size = new Size(80, 28),
                Location = new Point(140, 80)
            };
            var cancel = new Button
            {
                Text = "取消",
                BackColor = palette.MenuBack,
                ForeColor = palette.MenuFore,
                FlatStyle = FlatStyle.Flat,
                DialogResult = DialogResult.Cancel,
                Size = new Size(80, 28),
                Location = new Point(226, 80)
            };

            this.Controls.Add(label);
            this.Controls.Add(_nameBox);
            this.Controls.Add(ok);
            this.Controls.Add(cancel);
            this.AcceptButton = ok;
            this.CancelButton = cancel;

            ok.Click += (s, e) => { PageName = _nameBox.Text.Trim(); };
            this.Load += (s, e) =>
            {
                _nameBox.SelectAll();
                _nameBox.Focus();
            };
        }
    }
}
