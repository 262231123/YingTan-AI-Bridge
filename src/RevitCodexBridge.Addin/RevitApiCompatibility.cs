using Autodesk.Revit.DB;

namespace RevitCodexBridge.Addin;

internal static class RevitApiCompatibility
{
    public static long GetElementIdValue(ElementId id)
    {
#if REVIT2020 || REVIT2021 || REVIT2022 || REVIT2023
        return id.IntegerValue;
#else
        return id.Value;
#endif
    }

    public static ElementId CreateElementId(long value)
    {
#if REVIT2020 || REVIT2021 || REVIT2022 || REVIT2023
        if (value > int.MaxValue || value < int.MinValue)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "This Revit version only supports 32-bit ElementId values.");
        }

        return new ElementId((int)value);
#else
        return new ElementId(value);
#endif
    }
}
