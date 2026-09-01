using System.Text.Json;

namespace RevitCodexBridge.Addin;

internal static class JsonElementExtensions
{
    public static JsonElement GetRequiredProperty(this JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            throw new InvalidOperationException($"Missing required JSON property '{propertyName}'.");
        }

        return value;
    }

    public static string GetRequiredString(this JsonElement element, string propertyName)
    {
        var value = element.GetRequiredProperty(propertyName);
        return value.GetString()
            ?? throw new InvalidOperationException($"JSON property '{propertyName}' must be a string.");
    }

    public static string? GetOptionalString(this JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetString()
            : null;
    }

    public static long GetRequiredInt64(this JsonElement element, string propertyName)
    {
        var value = element.GetRequiredProperty(propertyName);
        return value.GetInt64();
    }

    public static double GetRequiredDouble(this JsonElement element, string propertyName)
    {
        var value = element.GetRequiredProperty(propertyName);
        return value.GetDouble();
    }

    public static double GetOptionalDouble(this JsonElement element, string propertyName, double defaultValue)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetDouble()
            : defaultValue;
    }

    public static bool GetOptionalBoolean(this JsonElement element, string propertyName, bool defaultValue)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetBoolean()
            : defaultValue;
    }
}
