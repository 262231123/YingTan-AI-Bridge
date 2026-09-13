using System.Text.Json;

namespace RevitCodexBridge.Addin;

internal sealed record BridgeOperationRecord(
    long Sequence,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string Command,
    string? DocumentTitle,
    bool Succeeded,
    string ResultJson,
    string ErrorDetails)
{
    public string StatusText => Succeeded ? "成功" : "失败";
    public string StartedAtText => StartedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string DurationText => $"{Math.Max(0, (CompletedAtUtc - StartedAtUtc).TotalMilliseconds):0} ms";
}

internal sealed class BridgeOperationHistory
{
    private const int MaximumRecords = 200;
    private const int MaximumDetailLength = 24000;
    private readonly object _sync = new();
    private readonly List<BridgeOperationRecord> _records = new();
    private long _sequence;

    public BridgeOperationRecord AddResult(
        string command,
        DateTimeOffset startedAtUtc,
        object? result,
        string? documentTitle)
    {
        var resultJson = SerializeResult(result);
        var succeeded = !ResultIndicatesFailure(JsonSerializer.SerializeToElement(result));
        var error = succeeded ? string.Empty : ExtractFailureSummary(resultJson);
        return Add(command, startedAtUtc, documentTitle, succeeded, resultJson, error);
    }

    public BridgeOperationRecord AddException(
        string command,
        DateTimeOffset startedAtUtc,
        Exception exception,
        string? documentTitle) =>
        Add(command, startedAtUtc, documentTitle, false, string.Empty, Limit(exception.ToString()));

    public IReadOnlyList<BridgeOperationRecord> Snapshot()
    {
        lock (_sync)
        {
            return _records.ToArray();
        }
    }

    private BridgeOperationRecord Add(
        string command,
        DateTimeOffset startedAtUtc,
        string? documentTitle,
        bool succeeded,
        string resultJson,
        string errorDetails)
    {
        var record = new BridgeOperationRecord(
            Interlocked.Increment(ref _sequence),
            startedAtUtc,
            DateTimeOffset.UtcNow,
            command,
            documentTitle,
            succeeded,
            resultJson,
            errorDetails);

        lock (_sync)
        {
            _records.Add(record);
            if (_records.Count > MaximumRecords)
            {
                _records.RemoveRange(0, _records.Count - MaximumRecords);
            }
        }

        return record;
    }

    private static bool ResultIndicatesFailure(JsonElement node)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Array:
                return node.EnumerateArray().Any(ResultIndicatesFailure);
            case JsonValueKind.Object:
                foreach (var property in node.EnumerateObject())
                {
                    if ((property.Name.Equals("ok", StringComparison.OrdinalIgnoreCase)
                         || property.Name.Equals("succeeded", StringComparison.OrdinalIgnoreCase))
                        && property.Value.ValueKind == JsonValueKind.False)
                    {
                        return true;
                    }

                    if (property.Name.Equals("failures", StringComparison.OrdinalIgnoreCase)
                        && property.Value.ValueKind == JsonValueKind.Number
                        && property.Value.TryGetInt64(out var failures)
                        && failures > 0)
                    {
                        return true;
                    }

                    if (property.Name.Equals("rolledBack", StringComparison.OrdinalIgnoreCase)
                        && property.Value.ValueKind == JsonValueKind.True)
                    {
                        return true;
                    }

                    if (ResultIndicatesFailure(property.Value))
                    {
                        return true;
                    }
                }

                return false;
            default:
                return false;
        }
    }

    private static string SerializeResult(object? result)
    {
        try
        {
            return Limit(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            return $"[返回结果无法序列化：{ex.Message}]";
        }
    }

    private static string ExtractFailureSummary(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var messages = new List<string>();
            CollectMessages(document.RootElement, messages);
            var summary = string.Join(Environment.NewLine, messages.Distinct().Take(8));
            return string.IsNullOrWhiteSpace(summary)
                ? "命令返回了失败状态；请查看下方完整返回结果。"
                : summary;
        }
        catch
        {
            return "命令返回了失败状态；请查看下方完整返回结果。";
        }
    }

    private static void CollectMessages(JsonElement node, List<string> messages)
    {
        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in node.EnumerateArray()) CollectMessages(child, messages);
            return;
        }

        if (node.ValueKind != JsonValueKind.Object) return;
        foreach (var property in node.EnumerateObject())
        {
            if ((property.Name.Equals("error", StringComparison.OrdinalIgnoreCase)
                 || property.Name.Equals("message", StringComparison.OrdinalIgnoreCase)
                 || property.Name.Equals("reason", StringComparison.OrdinalIgnoreCase))
                && property.Value.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(property.Value.GetString()))
            {
                messages.Add($"{property.Name}: {property.Value.GetString()}");
            }

            CollectMessages(property.Value, messages);
        }
    }

    private static string Limit(string value) => value.Length <= MaximumDetailLength
        ? value
        : value[..MaximumDetailLength] + Environment.NewLine + "[明细已截断；完整异常仍保存在 bridge.log]";
}
