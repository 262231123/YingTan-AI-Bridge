using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RevitCodexBridge.Addin;

internal static class RevitAutomationFallback
{
    private static readonly JsonSerializerOptions PrettyJson = new()
    {
        WriteIndented = true
    };

    public static bool ShouldUseNativeScript(ChatConversation conversation)
    {
        var request = conversation.Messages.LastOrDefault(message => message.IsUser)?.Content ?? string.Empty;
        return RequiresAutomation(request) && !ExplicitlyRequestsFinishToolkit(request);
    }

    public static async Task<RevitAiResponse> EnsureExecutableScriptAsync(
        AiSettings settings,
        ChatConversation conversation,
        RevitAiResponse initial,
        CancellationToken cancellationToken = default)
    {
        var request = conversation.Messages.LastOrDefault(message => message.IsUser)?.Content ?? string.Empty;
        if (!RequiresAutomation(request))
        {
            return initial;
        }

        if (HasUsableNativeScript(initial.PlanJson, request))
        {
            SaveScript(initial.PlanJson!);
            return initial;
        }

        BridgeLog.Info("Requesting native Revit automation script fallback.");
        var history = string.Join(
            "\n\n",
            conversation.Messages
                .Where(message => !message.IsError)
                .TakeLast(6)
                .Select(message => $"{(message.IsUser ? "User" : "Assistant")}: {Normalize(message.Content)}"));

        var result = await OpenAiCompatibleClient.CompleteAsync(
            settings.GetActiveProfile(),
            new[]
            {
                new AiChatRequestMessage("system", RecoveryPrompt),
                new AiChatRequestMessage("user", $"Conversation:\n{history}\n\nCurrent request:\n{request}")
            },
            cancellationToken);

        if (!result.Succeeded)
        {
            BridgeLog.Error("Native Revit automation fallback failed.", new InvalidOperationException(result.Message));
            if (string.IsNullOrWhiteSpace(initial.Reply))
            {
                throw new InvalidOperationException(result.Message);
            }

            return initial;
        }

        var recovered = Parse(result.ResponseText ?? string.Empty);
        if (string.IsNullOrWhiteSpace(recovered.PlanJson))
        {
            if (string.IsNullOrWhiteSpace(initial.Reply))
            {
                throw new InvalidOperationException("AI 未生成可执行的 Revit 脚本，请补充目标对象、范围或参数后重试。");
            }

            return initial;
        }

        SaveScript(recovered.PlanJson);
        return recovered;
    }

    private static bool HasUsableNativeScript(string? planJson, string request)
    {
        if (string.IsNullOrWhiteSpace(planJson))
        {
            return false;
        }

        try
        {
            var root = JsonNode.Parse(planJson) as JsonObject;
            var commands = GetCommands(root).ToList();
            if (commands.Count == 0)
            {
                return false;
            }

            var usesFinishToolkit = commands.Any(command => command.StartsWith("finish_toolkit_", StringComparison.OrdinalIgnoreCase));
            return !usesFinishToolkit || ExplicitlyRequestsFinishToolkit(request);
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<string> GetCommands(JsonObject? root)
    {
        if (root is null)
        {
            yield break;
        }

        if (root["command"]?.GetValue<string>() is { Length: > 0 } command)
        {
            yield return command;
        }

        if (root["operations"] is not JsonArray operations)
        {
            yield break;
        }

        foreach (var operation in operations.OfType<JsonObject>())
        {
            if (operation["command"]?.GetValue<string>() is { Length: > 0 } operationCommand)
            {
                yield return operationCommand;
            }
        }
    }

    private static bool RequiresAutomation(string request)
    {
        var actionWords = new[]
        {
            "创建", "生成", "替换", "修改", "设置", "删除", "统计", "查询", "提取", "检查", "定位", "显示", "导出", "绘制", "建模", "执行", "运行",
            "create", "replace", "change", "set ", "count", "query", "inspect", "show", "export", "run "
        };
        return actionWords.Any(word => request.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ExplicitlyRequestsFinishToolkit(string request)
    {
        var terms = new[] { "呆猫", "精装", "硬装", "软装", "Finish Toolkit", "FinishParts" };
        return terms.Any(term => request.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static RevitAiResponse Parse(string rawText)
    {
        var json = ExtractJson(rawText);
        if (json is null)
        {
            return new RevitAiResponse(rawText.Trim(), null);
        }

        try
        {
            var root = JsonNode.Parse(json) as JsonObject;
            var reply = root?["reply"]?.GetValue<string>()?.Trim();
            var script = root?["script"] ?? root?["plan"];
            var plan = script is JsonObject objectScript &&
                (objectScript["operations"] is not null || objectScript["command"] is not null)
                ? objectScript.ToJsonString(PrettyJson)
                : null;
            return new RevitAiResponse(
                string.IsNullOrWhiteSpace(reply) ? "已生成 Revit 自动化脚本。" : reply,
                plan);
        }
        catch
        {
            return new RevitAiResponse(rawText.Trim(), null);
        }
    }

    private static void SaveScript(string scriptJson)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RevitCodexBridge",
                "automation-scripts");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"revit-script-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
            File.WriteAllText(path, scriptJson, new UTF8Encoding(false));
            BridgeLog.Info($"AI automation script saved: {path}");
        }
        catch (Exception ex)
        {
            BridgeLog.Error("Failed to save AI automation script.", ex);
        }
    }

    private static string Normalize(string content)
    {
        const int limit = 2500;
        return content.Length <= limit ? content : content[..limit] + "\n...history compressed";
    }

    private static string? ExtractJson(string rawText)
    {
        var text = rawText.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = text.IndexOf('\n');
            var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (firstLineEnd >= 0 && lastFence > firstLineEnd)
            {
                text = text[(firstLineEnd + 1)..lastFence].Trim();
            }
        }

        if (text.StartsWith('{') && text.EndsWith('}'))
        {
            return text;
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : null;
    }

    private const string RecoveryPrompt = """
You are the native Revit automation script generator. Return exactly one JSON object, without Markdown.
Reply in Chinese and always include a script for a Revit action request.

General Revit requests must use native commands. Do not use finish_toolkit_info or finish_toolkit_run unless the user explicitly asks for 呆猫, 精装, 硬装, 软装, Finish Toolkit, or FinishParts. Never say that an optional plugin lacks a feature.

Read commands: get_active_document, list_levels, list_wall_types, list_family_symbols, count_elements, analyze_walls, get_selection, get_element, list_views, list_sheets, list_schedules, show_elements.
Write commands: create_wall, place_door, place_window, create_room, set_parameter, create_drawing_set, create_energy_cube_model, create_compound_wall_type.
For a layered system wall use create_compound_wall_type with sourceWallTypeName or sourceWallTypeId, newWallTypeName, layers [{materialName, thicknessMm, function}], and optional replaceSourceWallTypeName or replaceSourceWallTypeId. Available functions are Structure, Substrate, Insulation, Finish1, Finish2, Membrane.
If essential IDs or type names are unknown, create a read-only query script first. Never invent them.

Format:
{"reply":"中文说明","script":{"operations":[{"id":"op-001","command":"list_wall_types","description":"读取墙类型"}]}}
""";
}
