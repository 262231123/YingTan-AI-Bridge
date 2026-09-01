using System.Text.Json;
using Autodesk.Revit.DB;

namespace RevitCodexBridge.Addin;

internal static class ParameterValueWriter
{
    public static void Write(Parameter parameter, JsonElement value, string? doubleUnit)
    {
        switch (parameter.StorageType)
        {
            case StorageType.String:
                parameter.Set(value.ValueKind == JsonValueKind.Null ? string.Empty : value.ToString());
                break;

            case StorageType.Integer:
                parameter.Set(ReadInteger(value));
                break;

            case StorageType.Double:
                parameter.Set(ReadDouble(value, doubleUnit));
                break;

            case StorageType.ElementId:
                parameter.Set(RevitApiCompatibility.CreateElementId(ReadInteger(value)));
                break;

            default:
                throw new InvalidOperationException($"Cannot write parameter storage type '{parameter.StorageType}'.");
        }
    }

    private static int ReadInteger(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetInt32(),
            JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
            JsonValueKind.True => 1,
            JsonValueKind.False => 0,
            _ => throw new InvalidOperationException("Parameter value must be an integer.")
        };
    }

    private static double ReadDouble(JsonElement value, string? doubleUnit)
    {
        var raw = value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String when double.TryParse(value.GetString(), out var parsed) => parsed,
            _ => throw new InvalidOperationException("Parameter value must be a number.")
        };

        return string.Equals(doubleUnit, "mm", StringComparison.OrdinalIgnoreCase)
            ? raw / 304.8
            : raw;
    }
}
