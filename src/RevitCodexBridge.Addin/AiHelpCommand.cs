using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitCodexBridge.Addin;

[Transaction(TransactionMode.Manual)]
public sealed class AiHelpCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        using var form = new AiHelpForm();
        form.ShowDialog(new RevitDialogOwner(commandData.Application.MainWindowHandle));
        return Result.Succeeded;
    }
}
