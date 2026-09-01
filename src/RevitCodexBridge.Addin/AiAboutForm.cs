using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace RevitCodexBridge.Addin;

internal sealed class AiAboutForm : Form
{
    private const string Website = "https://ytszkj.net";
    private const string Email = "scott.xyc@foxmail.com";

    public AiAboutForm()
    {
        Text = "关于 Revit AI 助手";
        Width = 590;
        Height = 490;
        MinimumSize = new Size(540, 440);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MaximizeBox = false;
        UiTheme.ApplyForm(this);
        BuildLayout();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        var panel = UiTheme.CreateSurfacePanel(new Padding(20, 18, 20, 18));
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 10 };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 14));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

        content.Controls.Add(UiTheme.CreateTitle(this, "盈碳 Revit AI Bridge", 21F), 0, 0);
        content.Controls.Add(UiTheme.CreateMutedLabel("面向 Autodesk Revit 的对话式 AI 桥接助手"), 0, 1);
        content.Controls.Add(CreateInfoLabel($"版本：{GetVersion()}"), 0, 3);
        content.Controls.Add(CreateInfoLabel("适用平台：Autodesk Revit 2027"), 0, 4);
        content.Controls.Add(CreateLink("官方网站：ytszkj.net", Website), 0, 5);
        content.Controls.Add(CreateLink("联系邮箱：scott.xyc@foxmail.com", $"mailto:{Email}"), 0, 6);

        var copyright = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            ForeColor = UiTheme.MutedText,
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular),
            Text = "版权声明\r\nCopyright © 2026 ytszkj.net. All rights reserved.\r\n本软件及其源代码、界面、文档和配套资源受相关著作权法律保护。未经权利人书面许可，不得以侵犯著作权的方式复制、修改、传播或用于未经授权的商业再分发；法律法规另有规定的除外。\r\n\r\nAutodesk 和 Revit 是 Autodesk, Inc. 的商标。本软件与 Autodesk 无隶属、赞助或背书关系。",
            TextAlign = ContentAlignment.TopLeft
        };
        content.Controls.Add(copyright, 0, 8);

        panel.Controls.Add(content);
        root.Controls.Add(panel, 0, 0);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        actions.Controls.Add(UiTheme.CreateButton("关闭", UiButtonKind.Primary, (_, _) => Close()));
        root.Controls.Add(actions, 0, 1);
        Controls.Add(root);
    }

    private static Label CreateInfoLabel(string text)
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Text = text,
            ForeColor = UiTheme.Text,
            TextAlign = ContentAlignment.MiddleLeft
        };
    }

    private static LinkLabel CreateLink(string text, string uri)
    {
        var link = new LinkLabel
        {
            Dock = DockStyle.Fill,
            Text = text,
            LinkColor = UiTheme.Primary,
            ActiveLinkColor = UiTheme.PrimaryHover,
            VisitedLinkColor = UiTheme.Primary,
            TextAlign = ContentAlignment.MiddleLeft
        };
        link.Click += (_, _) => OpenUri(uri);
        return link;
    }

    private static string GetVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "V1.0.0" : $"V{version.Major}.{version.Minor}.{version.Build}";
    }

    private static void OpenUri(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = uri, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法打开链接：{uri}\n{ex.Message}", "Revit AI 助手", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
