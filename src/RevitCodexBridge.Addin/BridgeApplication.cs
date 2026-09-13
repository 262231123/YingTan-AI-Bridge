using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitCodexBridge.Addin;

public sealed class BridgeApplication : IExternalApplication
{
    private BridgeRuntime? _runtime;
    private BridgeServer? _server;

    public Result OnStartup(UIControlledApplication application)
    {
        BridgeLog.Info("Starting YingTan Revit AI Bridge.");
        application.ControlledApplication.DocumentOpened += OnDocumentOpened;
        application.ControlledApplication.DocumentCreated += OnDocumentCreated;
        application.ControlledApplication.DocumentClosing += OnDocumentClosing;
        application.ControlledApplication.DocumentChanged += RevitScriptHost.DocumentChanged;
        _runtime = new BridgeRuntime(application.ControlledApplication.VersionNumber);

        var handler = new BridgeExternalEventHandler(_runtime);
        var externalEvent = ExternalEvent.Create(handler);
        _runtime.AttachExternalEvent(externalEvent);
        BridgeRuntime.Current = _runtime;

        try
        {
            var pane = new ChatDockPane(_runtime);
            ChatDockPane.Current = pane;
            application.RegisterDockablePane(ChatDockPane.PaneId, "盈碳 Revit AI Bridge", pane);
            BridgeLog.Info("YingTan Revit AI dockable pane registered.");
        }
        catch (Exception ex)
        {
            BridgeLog.Error("Failed to register YingTan Revit AI dockable pane.", ex);
            Autodesk.Revit.UI.TaskDialog.Show("盈碳 Revit AI Bridge", $"无法注册 AI 对话面板：\n{ex.Message}");
        }

        try
        {
            _server = new BridgeServer(_runtime, BridgeServer.DefaultPort);
            _server.Start();
            BridgeLog.Info($"Local bridge server listening at {_runtime.ServerUrl}.");
        }
        catch (Exception ex)
        {
            BridgeLog.Error("Failed to start local bridge server.", ex);
            Autodesk.Revit.UI.TaskDialog.Show("盈碳 Revit AI Bridge", $"本地桥接服务启动失败：\n{ex.Message}");
        }

        AddRibbonButtons(application);
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        BridgeLog.Info("Stopping YingTan Revit AI Bridge.");
        application.ControlledApplication.DocumentOpened -= OnDocumentOpened;
        application.ControlledApplication.DocumentCreated -= OnDocumentCreated;
        application.ControlledApplication.DocumentClosing -= OnDocumentClosing;
        application.ControlledApplication.DocumentChanged -= RevitScriptHost.DocumentChanged;
        _server?.Dispose();
        _runtime?.Dispose();
        BridgeRuntime.Current = null;
        ChatDockPane.Current = null;
        return Result.Succeeded;
    }

    private static void OnDocumentOpened(object? sender, Autodesk.Revit.DB.Events.DocumentOpenedEventArgs e) => RegisterDocument(e.Document);
    private static void OnDocumentCreated(object? sender, Autodesk.Revit.DB.Events.DocumentCreatedEventArgs e) => RegisterDocument(e.Document);
    private static void OnDocumentClosing(object? sender, Autodesk.Revit.DB.Events.DocumentClosingEventArgs e)
    {
        var document = e.Document;
        DesignAgentTools.DocumentClosing(document);
        RevitScriptHost.DocumentClosing(document);
    }
    private static void RegisterDocument(Document document)
    {
        DesignAgentTools.DocumentOpened(document);
        RevitScriptHost.DocumentOpened(document);
    }

    private static void AddRibbonButtons(UIControlledApplication application)
    {
        try
        {
            var assemblyPath = Assembly.GetExecutingAssembly().Location;
            // The one-argument overload creates this panel on Revit's built-in Add-Ins tab.
            var panel = application.CreateRibbonPanel("盈碳 AI 助手");

            var assistant = new PushButtonData(
                "RevitCodexBridge.Status",
                "AI\n助手",
                assemblyPath,
                typeof(BridgeCommand).FullName)
            {
                ToolTip = "打开盈碳 Revit AI Bridge 停靠式对话面板。",
                LongDescription = "用于配置模型 API、Agent、Skill，并在确认后执行受控 Revit 操作。",
                AvailabilityClassName = typeof(BridgeCommandAvailability).FullName
            };
            assistant.Image = LoadRibbonIcon(16);
            assistant.LargeImage = LoadRibbonIcon(32);

            var help = new PushButtonData(
                "RevitCodexBridge.Help",
                "使用\n帮助",
                assemblyPath,
                typeof(AiHelpCommand).FullName)
            {
                ToolTip = "查看盈碳 Revit AI Bridge 的使用说明。",
                LongDescription = "包含模型配置、Agent、Skill、Dry-run、确认执行和日志排查说明。"
            };

            var about = new PushButtonData(
                "RevitCodexBridge.About",
                "关于\n软件",
                assemblyPath,
                typeof(AiAboutCommand).FullName)
            {
                ToolTip = "查看产品版本、官网、联系邮箱和版权声明。",
                LongDescription = "盈碳 Revit AI Bridge，对话式 Revit AI 桥接助手。"
            };

            panel.AddItem(assistant);
            panel.AddStackedItems(help, about);
            BridgeLog.Info("YingTan Revit AI standalone icon created on the built-in Add-Ins tab.");
        }
        catch (Exception ex)
        {
            BridgeLog.Error("Failed to create YingTan ribbon buttons.", ex);
        }
    }

    private static ImageSource LoadRibbonIcon(int pixelWidth)
    {
        var assembly = typeof(BridgeApplication).Assembly;
        using var stream = assembly.GetManifestResourceStream("RevitCodexBridge.Addin.Assets.YingTanAiBridge.png")
            ?? throw new InvalidOperationException("Embedded Revit ribbon icon was not found.");
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = pixelWidth;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}

public sealed class BridgeCommandAvailability : IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
    {
        return true;
    }
}
