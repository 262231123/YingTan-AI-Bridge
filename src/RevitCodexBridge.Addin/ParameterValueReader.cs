using System.Text.Json;
using Autodesk.Revit.DB;

namespace RevitCodexBridge.Addin;

internal static class ParameterValueReader
{
    public static object? Read(Parameter parameter)
    {
        return parameter.StorageType switch
        {
            StorageType.String => parameter.AsString(),
            StorageType.Integer => parameter.AsInteger(),
            StorageType.Double => new
            {
                raw = parameter.AsDouble(),
                display = parameter.AsValueString()
            },
            StorageType.ElementId => RevitApiCompatibility.GetElementIdValue(parameter.AsElementId()),
            StorageType.None => null,
            _ => parameter.AsValueString()
        };
    }

    public static object? JsonValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => value.GetRawText()
        };
    }
}


