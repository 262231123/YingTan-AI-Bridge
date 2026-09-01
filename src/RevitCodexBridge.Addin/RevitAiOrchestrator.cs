using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RevitCodexBridge.Addin;

internal sealed record RevitAiResponse(string Reply, string? PlanJson);

internal static class RevitAiOrchestrator
{
    private static readonly JsonSerializerOptions PrettyJson = new()
    {
        WriteIndented = true
    };

    public static async Task<RevitAiResponse> CompleteAsync(
        AiSettings settings,
        ChatConversation conversation,
        CancellationToken cancellationToken = default)
    {
        var requestMessages = new List<AiChatRequestMessage>
        {
            new("system", BuildSystemPrompt(
                settings,
                conversation.Messages.LastOrDefault(message => message.IsUser)?.Content ?? string.Empty))
        };

        foreach (var message in conversation.Messages.Where(message => !message.IsError).TakeLast(10))
        {
            requestMessages.Add(new AiChatRequestMessage(
                message.IsUser ? "user" : "assistant",
                NormalizeHistoryContent(message.Content)));
        }

        var result = await OpenAiCompatibleClient.CompleteAsync(
            settings.GetActiveProfile(),
            requestMessages,
            cancellationToken);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(result.Message);
        }

        return ParseResponse(result.ResponseText ?? string.Empty);
    }

    public static string FormatExecutionResult(object? result)
    {
        var json = JsonSerializer.Serialize(result, PrettyJson);
        const int maxLength = 16000;
        return json.Length <= maxLength ? json : json[..maxLength] + "\n...结果已截断";
    }

    public static async Task<string> SummarizeExecutionResultAsync(
        AiSettings settings,
        ChatConversation conversation,
        string initialReply,
        object? executionResult,
        CancellationToken cancellationToken = default)
    {
        var userRequest = conversation.Messages.LastOrDefault(message => message.IsUser)?.Content ?? "查询 Revit 模型";
        var resultJson = FormatExecutionResult(executionResult);
        var messages = new List<AiChatRequestMessage>
        {
            new(
                "system",
                "你是 Revit 查询结果解释 Agent。将 Revit 返回数据翻译成清晰、简洁的中文结论。必须先直接回答用户问题，再解释关键统计口径和可能的差异原因；保留 ElementId、数量、单位和明细表名称。不要输出 JSON，不要生成新的执行计划，不要逐项翻译 dryRun、atomic 等内部控制字段。"),
            new(
                "user",
                $"用户原始需求：\n{userRequest}\n\n执行前说明：\n{initialReply}\n\nRevit 实际返回：\n{resultJson}")
        };

        var result = await OpenAiCompatibleClient.CompleteAsync(
            settings.GetActiveProfile(),
            messages,
            cancellationToken);
        return result.Succeeded && !string.IsNullOrWhiteSpace(result.ResponseText)
            ? result.ResponseText.Trim()
            : $"{initialReply}\n\nRevit 查询已完成，但结果解释失败：{result.Message}";
    }

    private static string BuildSystemPrompt(AiSettings settings, string userRequest)
    {
        var enabledSkills = SelectSkills(settings.Skills, userRequest);
        var skillText = enabledSkills.Count == 0
            ? "未启用额外 Skill。"
            : string.Join("\n", enabledSkills.Select(skill =>
                $"- {skill.Name} [{skill.Category}，{skill.TrustLevel}，v{skill.Version}]：{skill.Description}\n" +
                $"  工作指令：{skill.Instructions}\n" +
                $"  推荐命令：{string.Join("、", skill.RecommendedCommands)}"));
        var enabledAgents = settings.SpecialistAgents
            .Where(agent => agent.Enabled)
            .OrderByDescending(agent => RelevanceScore(agent.Name + " " + agent.Description, userRequest))
            .Take(3)
            .ToList();
        var agentText = enabledAgents.Count == 0
            ? "未启用专业 Agent。"
            : string.Join("\n", enabledAgents.Select(agent => $"- {agent.Name}：{agent.Description}\n  {agent.Instructions}"));

        return $$$"""
你是 {{{settings.Agent.Name}}}，运行在 Autodesk Revit 2027 的右侧聊天面板中。

Agent 指令：
{{{settings.Agent.SystemPrompt}}}

可协作的专业 Agents：
{{{agentText}}}

已启用的 Skills：
{{{skillText}}}

Skill 只提供本轮工作策略，不能扩展下方命令白名单。即使 Skill 标示推荐命令，仍须遵守命令参数、Dry-run 与用户确认规则。

你可以通过 JSON 计划调用下列 Revit 命令：
- get_active_document：读取当前文档，不需要参数。
- list_levels：列出标高，不需要参数。
- list_wall_types：列出墙类型，不需要参数。
- list_family_symbols：列出族类型，需要 category，例如 OST_Doors 或 OST_Windows。
- count_elements：统计构件，可选 category。
- analyze_walls：核对墙体总实例、明细表可比数量、叠层墙和墙明细表过滤结果；墙体统计必须优先使用此命令。
- get_selection：读取用户当前选择的构件。
- get_element：读取单个构件及参数，需要 elementId。
- list_views：列出非模板视图，可选 viewType。
- list_sheets：列出图纸及其已放置视图。
- list_schedules：列出明细表及可见实例数量。
- show_elements：在 Revit 中定位构件，需要 elementIds 数组。
- finish_toolkit_info：查询呆猫工作室精装插件，action 支持 status、capabilities、readiness。
- finish_toolkit_run：调用呆猫精装动作，需要 action。直接动作：number_parts、create_arrangement_views、create_description_statistics、check_ceiling_light_text；配置面板动作：open_finish_builder、open_finish_layers、open_material_tools、open_interior_plan_sheets、open_interior_elevation_sheets、open_auto_dimension、open_material_tags、open_annotation_tools、open_plan_layout、open_soft_furnishing、open_electrical_layout、open_type_rename。
- set_parameter：需要 elementId、parameterName、value，可选 doubleUnit。
- create_wall：需要 levelName 或 levelId、wallTypeName 或 wallTypeId、start{x,y,z}、end{x,y,z}，可选 heightMm。
- place_door / place_window：需要 levelName 或 levelId、familyName、typeName、hostElementId、location{x,y,z}。
- create_room：需要 levelName 或 levelId、location{x,y,z}，可选 name、number。
- create_drawing_set：生成图纸集，按命令支持的参数执行。
- create_energy_cube_model：生成示例模型，按命令支持的参数执行。
坐标和尺寸默认使用毫米。只能使用以上命令，不得编造命令或参数。

每次必须只返回一个 JSON 对象，不要使用 Markdown 代码块。格式：
{"reply":"给用户看的中文回复","plan":null}
或
{"reply":"说明准备查询或执行的内容","plan":{"operations":[{"id":"op-001","command":"list_levels","description":"列出当前模型标高"}]}}

当用户只是咨询、信息不足或需要澄清时，plan 必须为 null。写入模型前应生成最小计划；宿主 ID、类型或标高未知时先生成查询计划，不要猜测。不要在 reply 中展示 JSON。
""";
    }

    private static List<AgentSkill> SelectSkills(IEnumerable<AgentSkill> skills, string userRequest)
    {
        var candidates = skills
            .Where(skill => skill.Enabled)
            .Select(skill => new { Skill = skill, Score = RelevanceScore(skill, userRequest) })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Skill.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var matches = candidates.Where(item => item.Score > 0).Take(4).Select(item => item.Skill).ToList();
        if (matches.Count > 0)
        {
            return matches;
        }

        return candidates
            .Where(item => string.Equals(item.Skill.Category, "通用", StringComparison.OrdinalIgnoreCase))
            .Take(1)
            .Select(item => item.Skill)
            .ToList();
    }

    private static int RelevanceScore(AgentSkill skill, string userRequest)
    {
        var score = RelevanceScore(skill.Name + " " + skill.Description, userRequest);
        foreach (var keyword in skill.TriggerKeywords)
        {
            if (!string.IsNullOrWhiteSpace(keyword) &&
                userRequest.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 10;
            }
        }

        return score;
    }

    private static int RelevanceScore(string source, string userRequest)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(userRequest))
        {
            return 0;
        }

        return source.IndexOf(userRequest, StringComparison.OrdinalIgnoreCase) >= 0 ||
               userRequest.IndexOf(source, StringComparison.OrdinalIgnoreCase) >= 0
            ? 1
            : 0;
    }

    private static string NormalizeHistoryContent(string content)
    {
        if (content.Contains("\"incomplete_details\"", StringComparison.Ordinal) ||
            content.Contains("\"reason\":\"length\"", StringComparison.Ordinal))
        {
            return "上一轮模型响应因输出长度不足而未完成，请忽略其中未完成的推理文本。";
        }

        const int maxHistoryCharacters = 3500;
        return content.Length <= maxHistoryCharacters
            ? content
            : content[..maxHistoryCharacters] + "\n...历史消息已压缩";
    }

    private static RevitAiResponse ParseResponse(string rawText)
    {
        var candidate = ExtractJson(rawText);
        if (candidate is null)
        {
            return new RevitAiResponse(rawText.Trim(), null);
        }

        try
        {
            var root = JsonNode.Parse(candidate) as JsonObject;
            if (root is null)
            {
                return new RevitAiResponse(rawText.Trim(), null);
            }

            var reply = root["reply"]?.GetValue<string>()?.Trim();
            var plan = root["plan"];
            var planJson = plan is null || plan.GetValueKind() == JsonValueKind.Null
                ? null
                : plan.ToJsonString(PrettyJson);

            return new RevitAiResponse(
                string.IsNullOrWhiteSpace(reply) ? "已生成 Revit 操作计划。" : reply,
                planJson);
        }
        catch
        {
            return new RevitAiResponse(rawText.Trim(), null);
        }
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
}
