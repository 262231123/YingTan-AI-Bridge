using Autodesk.Revit.UI;

namespace RevitCodexBridge.Addin;

internal sealed class BridgeExternalEventHandler : IExternalEventHandler
{
    private readonly BridgeRuntime _runtime;

    public BridgeExternalEventHandler(BridgeRuntime runtime)
    {
        _runtime = runtime;
    }

    public void Execute(UIApplication app)
    {
        _runtime.ExecutePending(app);
    }

    public string GetName()
    {
        return "Revit Codex Bridge command dispatcher";
    }
}

