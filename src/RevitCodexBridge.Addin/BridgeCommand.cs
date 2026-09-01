using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitCodexBridge.Addin;

[Transaction(TransactionMode.Manual)]
public sealed class BridgeCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        BridgeLog.Info("Revit AI dockable pane command clicked.");

        try
        {
            var pane = commandData.Application.GetDockablePane(ChatDockPane.PaneId);
            pane.Show();
            ChatDockPane.Current?.FocusComposer();
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            BridgeLog.Error("Failed to show Revit AI dockable pane.", ex);
            message = ex.Message;
            Autodesk.Revit.UI.TaskDialog.Show("Revit AI 助手", $"无法打开聊天面板：\n{ex.Message}");
            return Result.Failed;
        }
    }
}
