using System.Text.Json;
using System.Text.Json.Nodes;

namespace RevitCodexBridge.Addin;

internal static class BridgePayloadBuilder
{
    private static readonly HashSet<string> MutationCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "set_parameter",
        "create_wall",
        "place_door",
        "place_window",
        "create_room",
        "create_room_layout",
        "create_compound_wall_type",
        "create_drawing_set",
        "create_energy_cube_model",
        "finish_toolkit_run"
    };

    public static JsonElement Build(string rawJson, bool allowWrites, bool confirmWrites = true)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            throw new InvalidOperationException("AI 没有生成可执行的 Revit 计划。");
        }

        var root = JsonNode.Parse(rawJson) as JsonObject
            ?? throw new InvalidOperationException("Revit 计划的 JSON 顶层必须是对象。");
        var payload = Build(root, allowWrites, confirmWrites);
        return JsonSerializer.SerializeToElement(payload).Clone();
    }

    public static bool HasMutation(string rawJson)
    {
        var root = JsonNode.Parse(rawJson) as JsonObject;
        if (root is null)
        {
            return false;
        }

        if (TryGetCommand(root, out var command) && MutationCommands.Contains(command))
        {
            return true;
        }

        return root["operations"] is JsonArray operations && operations
            .OfType<JsonObject>()
            .Any(operation => TryGetCommand(operation, out var operationCommand) && MutationCommands.Contains(operationCommand));
    }

    private static JsonObject Build(JsonObject root, bool allowWrites, bool confirmWrites)
    {
        if (root.ContainsKey("operations") && !root.ContainsKey("command"))
        {
            var batchPayload = new JsonObject
            {
                ["command"] = "run_batch",
                ["expectedDocumentToken"] = root["expectedDocumentToken"]?.DeepClone(),
                ["dryRun"] = !allowWrites,
                ["atomic"] = true,
                ["continueOnError"] = false,
                ["confirmInRevit"] = allowWrites && confirmWrites,
                ["operations"] = root["operations"]?.DeepClone()
                    ?? throw new InvalidOperationException("Revit 计划缺少 operations。")
            };
            ApplyOperationWriteMode(batchPayload, allowWrites);
            return batchPayload;
        }

        if (!TryGetCommand(root, out var command))
        {
            throw new InvalidOperationException("Revit 计划需要包含 command，或包含 BuildPlan operations。");
        }

        var payload = (JsonObject)root.DeepClone();
        if (MutationCommands.Contains(command))
        {
            payload["dryRun"] = !allowWrites;
            payload["confirmInRevit"] = allowWrites && confirmWrites;
        }

        if (command.Equals("run_batch", StringComparison.OrdinalIgnoreCase))
        {
            payload["dryRun"] = !allowWrites;
            payload["atomic"] ??= true;
            payload["continueOnError"] ??= false;
            payload["confirmInRevit"] = allowWrites && confirmWrites;
            ApplyOperationWriteMode(payload, allowWrites);
        }

        return payload;
    }

    private static void ApplyOperationWriteMode(JsonObject payload, bool allowWrites)
    {
        if (payload["operations"] is not JsonArray operations)
        {
            return;
        }

        foreach (var operation in operations.OfType<JsonObject>())
        {
            if (TryGetCommand(operation, out var command) && MutationCommands.Contains(command))
            {
                operation["dryRun"] = !allowWrites;
            }
        }
    }

    private static bool TryGetCommand(JsonObject node, out string command)
    {
        command = string.Empty;
        if (!node.TryGetPropertyValue("command", out var commandNode) || commandNode is null)
        {
            return false;
        }

        try
        {
            command = commandNode.GetValue<string>();
            return !string.IsNullOrWhiteSpace(command);
        }
        catch
        {
            return false;
        }
    }
}
