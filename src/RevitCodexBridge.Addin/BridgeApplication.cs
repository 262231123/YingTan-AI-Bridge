using System.Reflection;
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
        _server?.Dispose();
        _runtime?.Dispose();
        BridgeRuntime.Current = null;
        ChatDockPane.Current = null;
        return Result.Succeeded;
    }

    private static void AddRibbonButtons(UIControlledApplication application)
    {
        try
        {
            var assemblyPath = Assembly.GetExecutingAssembly().Location;
            var panel = application.CreateRibbonPanel("盈碳 AI");

            var assistant = new PushButtonData(
                "RevitCodexBridge.Status",
                "盈碳 AI\nBridge",
                assemblyPath,
                typeof(BridgeCommand).FullName)
            {
                ToolTip = "打开盈碳 Revit AI Bridge 停靠式对话面板。",
                LongDescription = "用于配置模型 API、Agent、Skill，并在确认后执行受控 Revit 操作。",
                AvailabilityClassName = typeof(BridgeCommandAvailability).FullName
            };

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
            panel.AddItem(help);
            panel.AddItem(about);
            BridgeLog.Info("YingTan Revit AI ribbon buttons created.");
        }
        catch (Exception ex)
        {
            BridgeLog.Error("Failed to create YingTan ribbon buttons.", ex);
        }
    }
}

public sealed class BridgeCommandAvailability : IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
    {
        return true;
    }
}
