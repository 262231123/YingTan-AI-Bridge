using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace YingTanAiBridge.Contracts;

/// <summary>CAD/BIM hosts supported by the YingTan AI Bridge family.</summary>
public enum BridgeHostKind
{
    Revit,
    AutoCad,
    Rhino,
    Inventor
}

public enum BridgeCapability
{
    ReadModel,
    QueryElements,
    CreateElements,
    UpdateElements,
    DeleteElements,
    RunNativeCommand,
    InspectSelection
}

public sealed class BridgeHostDescriptor
{
    public BridgeHostDescriptor(
        BridgeHostKind hostKind,
        string productName,
        IReadOnlyCollection<BridgeCapability> capabilities)
    {
        HostKind = hostKind;
        ProductName = productName ?? throw new ArgumentNullException(nameof(productName));
        Capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
    }

    public BridgeHostKind HostKind { get; }

    public string ProductName { get; }

    public IReadOnlyCollection<BridgeCapability> Capabilities { get; }
}

/// <summary>
/// A provider-neutral plan created by an Agent or Skill. Parameters stay JSON so every
/// CAD host can own its native object mapping without coupling to another host's SDK.
/// </summary>
public sealed class BridgeOperation
{
    public BridgeOperation(
        string operationId,
        string skillId,
        string intent,
        string parametersJson,
        bool changesDocument)
    {
        OperationId = operationId ?? throw new ArgumentNullException(nameof(operationId));
        SkillId = skillId ?? throw new ArgumentNullException(nameof(skillId));
        Intent = intent ?? throw new ArgumentNullException(nameof(intent));
        ParametersJson = parametersJson ?? "{}";
        ChangesDocument = changesDocument;
    }

    public string OperationId { get; }

    public string SkillId { get; }

    public string Intent { get; }

    public string ParametersJson { get; }

    public bool ChangesDocument { get; }
}

public sealed class OperationSafetyDecision
{
    public OperationSafetyDecision(bool canRun, bool requiresConfirmation, string reason)
    {
        CanRun = canRun;
        RequiresConfirmation = requiresConfirmation;
        Reason = reason ?? string.Empty;
    }

    public bool CanRun { get; }

    public bool RequiresConfirmation { get; }

    public string Reason { get; }
}

/// <summary>Shared guardrail: document changes always require a preview and a user confirmation.</summary>
public static class OperationSafetyPolicy
{
    public static OperationSafetyDecision Evaluate(BridgeHostDescriptor host, BridgeOperation operation)
    {
        if (host == null) throw new ArgumentNullException(nameof(host));
        if (operation == null) throw new ArgumentNullException(nameof(operation));

        if (!operation.ChangesDocument)
        {
            return new OperationSafetyDecision(true, false, "Read-only operation.");
        }

        var supportsWrites = host.Capabilities.Contains(BridgeCapability.CreateElements)
            || host.Capabilities.Contains(BridgeCapability.UpdateElements)
            || host.Capabilities.Contains(BridgeCapability.DeleteElements)
            || host.Capabilities.Contains(BridgeCapability.RunNativeCommand);

        return supportsWrites
            ? new OperationSafetyDecision(true, true, "Document changes require dry-run preview and explicit confirmation.")
            : new OperationSafetyDecision(false, false, "This host adapter does not expose document write capabilities.");
    }

    public static IReadOnlyCollection<BridgeCapability> Capabilities(params BridgeCapability[] capabilities)
    {
        return new ReadOnlyCollection<BridgeCapability>(capabilities ?? Array.Empty<BridgeCapability>());
    }
}
