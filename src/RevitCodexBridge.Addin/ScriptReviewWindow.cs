using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using FontFamily = System.Windows.Media.FontFamily;

namespace RevitCodexBridge.Addin;

internal static class ScriptReviewWindow
{
    public static void Require(string purpose, string code, string hash, string phase, string document)
    {
        var window = new Window { Title = "审阅 Revit 脚本 — " + phase, Width = 900, Height = 700, MinWidth = 650, MinHeight = 450,
            WindowStartupLocation = WindowStartupLocation.CenterScreen };
        var owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
        if (owner != IntPtr.Zero) new System.Windows.Interop.WindowInteropHelper(window).Owner = owner;
        var panel = new DockPanel { Margin = new Thickness(16) };
        var intro = new TextBlock { Text = $"文档：{document}\n用途：{purpose}\n阶段：{phase}\nSHA-256：{hash}\n\n脚本在Revit进程内运行，不是隔离沙箱。静态检查不保证安全；事务回滚仅覆盖当前文档模型，不覆盖外部副作用。确认前请审阅完整源码，只运行可信代码，并先保存项目副本。查询结果会发送给已配置的AI服务。",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(intro, Dock.Top); panel.Children.Add(intro);
        var bottom = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        var accept = new CheckBox { Content = "我已审阅此脚本并信任其来源，允许本次运行", Margin = new Thickness(0, 0, 0, 8) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var run = new Button { Content = "允许本次" + phase, IsEnabled = false, Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(8, 0, 0, 0) };
        var cancel = new Button { Content = "取消", IsCancel = true, Padding = new Thickness(16, 6, 16, 6) };
        accept.Checked += (_, _) => run.IsEnabled = true; accept.Unchecked += (_, _) => run.IsEnabled = false;
        run.Click += (_, _) => window.DialogResult = true;
        buttons.Children.Add(cancel); buttons.Children.Add(run); bottom.Children.Add(accept); bottom.Children.Add(buttons);
        DockPanel.SetDock(bottom, Dock.Bottom); panel.Children.Add(bottom);
        panel.Children.Add(new TextBox { Text = code, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas"), FontSize = 13 });
        window.Content = panel;
        if (window.ShowDialog() != true) throw new OperationCanceledException("用户取消脚本审阅，未运行脚本。");
    }
}
