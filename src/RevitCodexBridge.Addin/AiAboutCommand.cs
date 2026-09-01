using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitCodexBridge.Addin;

[Transaction(TransactionMode.Manual)]
public sealed class AiAboutCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        using var form = new AiAboutForm();
        form.ShowDialog(new RevitDialogOwner(commandData.Application.MainWindowHandle));
        return Result.Succeeded;
    }
}
