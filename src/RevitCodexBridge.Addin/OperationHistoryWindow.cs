using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Binding = System.Windows.Data.Binding;
using Button = System.Windows.Controls.Button;
using Clipboard = System.Windows.Clipboard;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using DataGrid = System.Windows.Controls.DataGrid;
using FontFamily = System.Windows.Media.FontFamily;
using MessageBox = System.Windows.MessageBox;
using TextBox = System.Windows.Controls.TextBox;

namespace RevitCodexBridge.Addin;

internal sealed class OperationHistoryWindow : Window
{
    private static OperationHistoryWindow? _current;
    private readonly BridgeRuntime _runtime;
    private readonly DataGrid _recordsGrid;
    private readonly TextBox _detailBox;
    private readonly TextBlock _summaryText;

    private OperationHistoryWindow(BridgeRuntime runtime)
    {
        _runtime = runtime;
        Title = "Revit AI 助手 · 处理记录";
        Width = 920;
        Height = 650;
        MinWidth = 720;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        FontSize = 12;
        Background = new SolidColorBrush(Color.FromRgb(245, 246, 248));

        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var refreshButton = CreateButton("刷新");
        refreshButton.Click += (_, _) => RefreshRecords();
        DockPanel.SetDock(refreshButton, Dock.Right);
        header.Children.Add(refreshButton);
        _summaryText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(32, 36, 43))
        };
        header.Children.Add(_summaryText);
        root.Children.Add(header);

        _recordsGrid = new DataGrid
        {
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(217, 221, 228)),
            BorderThickness = new Thickness(1)
        };
        _recordsGrid.Columns.Add(Column("编号", nameof(BridgeOperationRecord.Sequence), 58));
        _recordsGrid.Columns.Add(Column("时间", nameof(BridgeOperationRecord.StartedAtText), 145));
        _recordsGrid.Columns.Add(Column("状态", nameof(BridgeOperationRecord.StatusText), 60));
        _recordsGrid.Columns.Add(Column("命令", nameof(BridgeOperationRecord.Command), 190));
        _recordsGrid.Columns.Add(Column("模型", nameof(BridgeOperationRecord.DocumentTitle), 180));
        _recordsGrid.Columns.Add(Column("耗时", nameof(BridgeOperationRecord.DurationText), 85));
        _recordsGrid.SelectionChanged += (_, _) => ShowSelectedRecord();
        Grid.SetRow(_recordsGrid, 1);
        root.Children.Add(_recordsGrid);

        _detailBox = new TextBox
        {
            Margin = new Thickness(0, 10, 0, 10),
            Padding = new Thickness(10),
            IsReadOnly = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(217, 221, 228))
        };
        Grid.SetRow(_detailBox, 2);
        root.Children.Add(_detailBox);

        var footer = new DockPanel();
        var openLogButton = CreateButton("打开完整日志");
        openLogButton.Click += (_, _) => OpenLog();
        DockPanel.SetDock(openLogButton, Dock.Right);
        footer.Children.Add(openLogButton);
        var copyButton = CreateButton("复制当前明细");
        copyButton.Margin = new Thickness(0, 0, 8, 0);
        copyButton.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_detailBox.Text)) Clipboard.SetText(_detailBox.Text);
        };
        DockPanel.SetDock(copyButton, Dock.Right);
        footer.Children.Add(copyButton);
        footer.Children.Add(new TextBlock
        {
            Text = $"仅保留本次 Revit 会话最近 200 条；完整日志：{BridgeLog.LogPath}",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(104, 112, 125)),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        Content = root;
        _runtime.OperationRecorded += RuntimeOnOperationRecorded;
        Closed += (_, _) =>
        {
            _runtime.OperationRecorded -= RuntimeOnOperationRecorded;
            if (ReferenceEquals(_current, this)) _current = null;
        };
        RefreshRecords();
    }

    public static void ShowOrActivate(BridgeRuntime runtime)
    {
        if (_current is { IsVisible: true })
        {
            _current.RefreshRecords();
            if (_current.WindowState == WindowState.Minimized) _current.WindowState = WindowState.Normal;
            _current.Activate();
            return;
        }

        var window = new OperationHistoryWindow(runtime);
        _current = window;
        var owner = Process.GetCurrentProcess().MainWindowHandle;
        if (owner != IntPtr.Zero) new WindowInteropHelper(window).Owner = owner;
        window.Show();
        window.Activate();
    }

    private void RuntimeOnOperationRecorded(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke((Action)RefreshRecords, DispatcherPriority.Background);
    }

    private void RefreshRecords()
    {
        var records = _runtime.GetOperationHistory().OrderByDescending(record => record.Sequence).ToArray();
        _recordsGrid.ItemsSource = records;
        _summaryText.Text = $"处理记录：成功 {_runtime.ProcessedCount} · 失败 {_runtime.FailedCount} · 当前显示 {records.Length} 条";
        if (records.Length == 0)
        {
            _detailBox.Text = "本次 Revit 会话尚无处理记录。发送消息或执行计划后，可在这里查看命令返回结果。";
            return;
        }

        _recordsGrid.SelectedIndex = 0;
    }

    private void ShowSelectedRecord()
    {
        if (_recordsGrid.SelectedItem is not BridgeOperationRecord record) return;
        var result = string.IsNullOrWhiteSpace(record.ResultJson) ? "（无返回数据）" : record.ResultJson;
        var error = string.IsNullOrWhiteSpace(record.ErrorDetails) ? "（无）" : record.ErrorDetails;
        _detailBox.Text =
            $"编号：{record.Sequence}\r\n" +
            $"状态：{record.StatusText}\r\n" +
            $"命令：{record.Command}\r\n" +
            $"模型：{record.DocumentTitle ?? "（未知）"}\r\n" +
            $"开始：{record.StartedAtText}\r\n" +
            $"耗时：{record.DurationText}\r\n\r\n" +
            $"错误原因：\r\n{error}\r\n\r\n" +
            $"返回结果：\r\n{result}";
        _detailBox.ScrollToHome();
    }

    private static DataGridTextColumn Column(string header, string property, double width) => new()
    {
        Header = header,
        Binding = new Binding(property),
        Width = width
    };

    private static Button CreateButton(string text) => new()
    {
        Content = text,
        Height = 30,
        Padding = new Thickness(12, 0, 12, 0),
        Background = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromRgb(217, 221, 228)),
        Cursor = System.Windows.Input.Cursors.Hand
    };

    private static void OpenLog()
    {
        try
        {
            if (!File.Exists(BridgeLog.LogPath))
            {
                MessageBox.Show("当前还没有生成日志文件。", "Revit AI 助手", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Process.Start(new ProcessStartInfo { FileName = BridgeLog.LogPath, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法打开日志：{ex.Message}", "Revit AI 助手", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
