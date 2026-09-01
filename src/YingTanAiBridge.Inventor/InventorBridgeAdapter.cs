using YingTanAiBridge.Contracts;
#if INVENTOR_SDK
using System;
using System.Runtime.InteropServices;
using Inventor;
#endif

namespace YingTanAiBridge.Inventor;

public static class InventorBridgeAdapter
{
    public static BridgeHostDescriptor Descriptor { get; } = new BridgeHostDescriptor(
        BridgeHostKind.Inventor,
        "盈碳 Autodesk Inventor AI Bridge",
        OperationSafetyPolicy.Capabilities(
            BridgeCapability.ReadModel,
            BridgeCapability.QueryElements,
            BridgeCapability.InspectSelection,
            BridgeCapability.CreateElements,
            BridgeCapability.UpdateElements,
            BridgeCapability.DeleteElements,
            BridgeCapability.RunNativeCommand));
}

#if INVENTOR_SDK
[Guid("E3C42077-90D8-4A8F-8592-A1D52C00B204")]
public sealed class YingTanInventorAiBridgeAddIn : ApplicationAddInServer
{
    public void Activate(ApplicationAddInSite addInSiteObject, bool firstTime)
    {
        addInSiteObject.Application.StatusBarText = "盈碳 Inventor AI Bridge 已加载。";
    }

    public void Deactivate() { }

    public void ExecuteCommand(int commandID) { }

    public object Automation => this;
}
#endif
