using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RevitCodexBridge.Addin;

internal enum AiProviderKind
{
    OpenAI,
    Zhipu,
    Kimi,
    Volcengine
}

internal enum AiApiMode
{
    ChatCompletions,
    Responses
}

internal sealed class AiSettings
{
    public int SettingsVersion { get; set; }

    public AiProviderKind ActiveProvider { get; set; } = AiProviderKind.OpenAI;

    public List<AiProviderProfile> Providers { get; set; } = AiProviderDefaults.CreateDefaults();

    public AgentProfile Agent { get; set; } = AgentProfile.CreateDefault();

    public List<SpecialistAgent> SpecialistAgents { get; set; } = SpecialistAgent.CreateDefaults();

    public List<AgentSkill> Skills { get; set; } = AgentSkill.CreateDefaults();

    public AiProviderProfile GetProfile(AiProviderKind provider)
    {
        EnsureDefaults();
        return Providers.First(item => item.Provider == provider);
    }

    public AiProviderProfile GetActiveProfile()
    {
        EnsureDefaults();
        return Providers.FirstOrDefault(item => item.Provider == ActiveProvider)
            ?? Providers.First(item => item.Provider == AiProviderKind.OpenAI);
    }

    public void EnsureDefaults()
    {
        Agent ??= AgentProfile.CreateDefault();
        if (string.IsNullOrWhiteSpace(Agent.Name))
        {
            Agent.Name = "Revit 建筑助手";
        }

        SpecialistAgents ??= [];
        foreach (var defaultAgent in SpecialistAgent.CreateDefaults())
        {
            if (!SpecialistAgents.Any(item => string.Equals(item.Name, defaultAgent.Name, StringComparison.OrdinalIgnoreCase)))
            {
                SpecialistAgents.Add(defaultAgent);
            }
        }

        Skills ??= [];
        foreach (var defaultSkill in AgentSkill.CreateDefaults())
        {
            var currentSkill = Skills.FirstOrDefault(item =>
                string.Equals(item.Name, defaultSkill.Name, StringComparison.OrdinalIgnoreCase));
            if (currentSkill is null)
            {
                Skills.Add(defaultSkill);
                continue;
            }

            currentSkill.ApplyMissingMetadata(defaultSkill);
        }

        var defaults = AiProviderDefaults.CreateDefaults();
        foreach (var defaultProfile in defaults)
        {
            var current = Providers.FirstOrDefault(item => item.Provider == defaultProfile.Provider);
            if (current is null)
            {
                Providers.Add(defaultProfile);
                continue;
            }

            if (string.IsNullOrWhiteSpace(current.DisplayName))
            {
                current.DisplayName = defaultProfile.DisplayName;
            }

            if (string.IsNullOrWhiteSpace(current.BaseUrl))
            {
                current.BaseUrl = defaultProfile.BaseUrl;
            }

            if (string.IsNullOrWhiteSpace(current.Model))
            {
                current.Model = defaultProfile.Model;
            }

            if (current.TimeoutSeconds <= 0)
            {
                current.TimeoutSeconds = defaultProfile.TimeoutSeconds;
            }

            if (current.MaxTokens <= 0)
            {
                current.MaxTokens = defaultProfile.MaxTokens;
            }

            if (defaultProfile.ApiMode == AiApiMode.Responses)
            {
                current.ApiMode = AiApiMode.Responses;
            }
        }

        if (SettingsVersion < 2)
        {
            var volcengine = Providers.FirstOrDefault(item => item.Provider == AiProviderKind.Volcengine);
            if (volcengine is not null)
            {
                volcengine.MaxTokens = Math.Max(volcengine.MaxTokens, 16384);
            }

            SettingsVersion = 2;
        }

        if (SettingsVersion < 3)
        {
            var volcengine = Providers.FirstOrDefault(item => item.Provider == AiProviderKind.Volcengine);
            if (volcengine is not null)
            {
                volcengine.TimeoutSeconds = Math.Max(volcengine.TimeoutSeconds, 300);
            }

            SettingsVersion = 3;
        }

        if (SettingsVersion < 4)
        {
            SettingsVersion = 4;
        }

        foreach (var skill in Skills)
        {
            skill.Normalize();
        }
    }
}

internal sealed class AgentProfile
{
    public string Name { get; set; } = "Revit 建筑助手";

    public string SystemPrompt { get; set; } = string.Empty;

    public bool ConfirmBeforeWrite { get; set; } = true;

    public static AgentProfile CreateDefault()
    {
        return new AgentProfile
        {
            Name = "Revit 建筑助手",
            SystemPrompt = "你是建筑师和 Revit 建模助手。先理解用户意图，信息不足时明确提问；能够安全执行时生成最小、可验证的 Revit 操作计划。",
            ConfirmBeforeWrite = true
        };
    }
}

internal sealed class SpecialistAgent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Instructions { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public static List<SpecialistAgent> CreateDefaults()
    {
        return
        [
            new SpecialistAgent
            {
                Name = "BIM 数据核对 Agent",
                Description = "核对构件数量、明细表口径和异常差异。",
                Instructions = "统计结果必须说明数据来源和过滤口径。墙体优先使用 analyze_walls，并对比叠层墙容器、成员和墙明细表可见实例，不得只报一个无法解释的数字。"
            },
            new SpecialistAgent
            {
                Name = "建模执行 Agent",
                Description = "负责墙、门、窗、房间和参数修改计划。",
                Instructions = "写入前先查询标高、类型、宿主和坐标条件，生成最小可回滚计划，先 Dry-run，再等待用户确认。"
            },
            new SpecialistAgent
            {
                Name = "图纸交付 Agent",
                Description = "负责视图、明细表、图纸和出图检查。",
                Instructions = "先使用 list_views、list_schedules 和 list_sheets 了解现状，再生成图纸操作；明确图号、视图和标题栏依赖。"
            },
            new SpecialistAgent
            {
                Name = "模型 QA Agent",
                Description = "检查选择构件、参数、类型和模型一致性。",
                Instructions = "使用 get_selection 和 get_element 检查对象身份与关键参数；发现异常时给出 ElementId，必要时使用 show_elements 帮助用户定位。"
            },
            new SpecialistAgent
            {
                Name = "呆猫精装 Agent",
                Description = "编排呆猫工作室的硬装、软装、施工图和零件自动化。",
                Instructions = "先调用 finish_toolkit_info action=readiness 检查项目条件。直接任务使用 finish_toolkit_run；需要房间、材料、视图或规则选择时打开对应配置面板，不得猜测用户选择。"
            }
        ];
    }
}

internal sealed class AgentSkill
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "新 Skill";

    public string Description { get; set; } = string.Empty;

    public string Instructions { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public string Version { get; set; } = "1.0.0";

    public string Category { get; set; } = "通用";

    public string Source { get; set; } = "本地";

    public string TrustLevel { get; set; } = "本地";

    public List<string> HostKeys { get; set; } = [];

    public List<string> TriggerKeywords { get; set; } = [];

    public List<string> RecommendedCommands { get; set; } = [];

    public bool RequiresConfirmation { get; set; } = true;

    public void ApplyMissingMetadata(AgentSkill defaultSkill)
    {
        Version = string.IsNullOrWhiteSpace(Version) ? defaultSkill.Version : Version;
        Category = string.IsNullOrWhiteSpace(Category) ? defaultSkill.Category : Category;
        Source = string.IsNullOrWhiteSpace(Source) ? defaultSkill.Source : Source;
        TrustLevel = string.IsNullOrWhiteSpace(TrustLevel) ? defaultSkill.TrustLevel : TrustLevel;
        HostKeys ??= [];
        TriggerKeywords ??= [];
        RecommendedCommands ??= [];
        if (HostKeys.Count == 0) HostKeys = [.. defaultSkill.HostKeys];
        if (TriggerKeywords.Count == 0) TriggerKeywords = [.. defaultSkill.TriggerKeywords];
        if (RecommendedCommands.Count == 0) RecommendedCommands = [.. defaultSkill.RecommendedCommands];
        Normalize();
    }

    public void Normalize()
    {
        Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id.Trim();
        Name = string.IsNullOrWhiteSpace(Name) ? "未命名 Skill" : Name.Trim();
        Description = (Description ?? string.Empty).Trim();
        Instructions = (Instructions ?? string.Empty).Trim();
        Version = string.IsNullOrWhiteSpace(Version) ? "1.0.0" : Version.Trim();
        Category = string.IsNullOrWhiteSpace(Category) ? "通用" : Category.Trim();
        Source = string.IsNullOrWhiteSpace(Source) ? "本地" : Source.Trim();
        TrustLevel = string.IsNullOrWhiteSpace(TrustLevel) ? "本地" : TrustLevel.Trim();
        HostKeys = NormalizeList(HostKeys);
        TriggerKeywords = NormalizeList(TriggerKeywords);
        RecommendedCommands = NormalizeList(RecommendedCommands);
    }

    private static List<string> NormalizeList(IEnumerable<string>? values)
    {
        return (values ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<AgentSkill> CreateDefaults()
    {
        return
        [
            new AgentSkill
            {
                Name = "模型查询",
                Description = "读取当前项目、标高、类型和构件数量。",
                Instructions = "查询时优先使用 get_active_document、list_levels、list_wall_types、list_family_symbols 和 count_elements，不要生成写操作。",
                Category = "通用",
                Source = "内置",
                TrustLevel = "官方内置",
                TriggerKeywords = ["查询", "项目", "标高", "类型", "多少", "数量", "统计"],
                RecommendedCommands = ["get_active_document", "list_levels", "list_wall_types", "list_family_symbols", "count_elements"]
            },
            new AgentSkill
            {
                Name = "建筑建模",
                Description = "创建墙、门、窗和房间，并修改参数。",
                Instructions = "建模前确认单位、标高、类型、坐标和关键尺寸。长度统一使用毫米；写操作必须先生成可 Dry-run 的计划。",
                Category = "建模",
                Source = "内置",
                TrustLevel = "官方内置",
                TriggerKeywords = ["创建", "新建", "建模", "墙", "门", "窗", "房间", "参数", "修改"],
                RecommendedCommands = ["list_levels", "list_wall_types", "list_family_symbols", "create_wall", "place_door", "place_window", "create_room", "set_parameter"]
            },
            new AgentSkill
            {
                Name = "图纸生成",
                Description = "生成平面、立面、尺寸、图纸和视口。",
                Instructions = "仅在用户明确要求生成图纸时使用 create_drawing_set，并说明将创建或复用的内容。",
                Category = "交付",
                Source = "内置",
                TrustLevel = "官方内置",
                TriggerKeywords = ["图纸", "出图", "视图", "图号", "图框", "图纸集"],
                RecommendedCommands = ["list_views", "list_sheets", "list_schedules", "create_drawing_set"]
            },
            new AgentSkill
            {
                Name = "数量核对与明细表",
                Description = "核对模型实例与明细表可见数量，解释统计差异。",
                Instructions = "墙体统计必须使用 analyze_walls；其他类别可使用 count_elements。报告结果时先给中文结论，再说明类别、过滤器、是否逐项列举和统计口径。",
                Category = "核对",
                Source = "内置",
                TrustLevel = "官方内置",
                TriggerKeywords = ["明细表", "核对", "差异", "墙体数量", "墙数量", "数量不符", "统计"],
                RecommendedCommands = ["analyze_walls", "count_elements", "list_schedules"]
            },
            new AgentSkill
            {
                Name = "构件检查与定位",
                Description = "读取选择集、构件参数并在模型中定位对象。",
                Instructions = "使用 get_selection 获取用户选择，使用 get_element 查看参数，使用 show_elements 定位 ElementId；不要猜测构件身份。",
                Category = "检查",
                Source = "内置",
                TrustLevel = "官方内置",
                TriggerKeywords = ["选择", "定位", "构件", "元素", "参数", "ElementId", "检查"],
                RecommendedCommands = ["get_selection", "get_element", "show_elements"]
            },
            new AgentSkill
            {
                Name = "视图与交付检查",
                Description = "查询视图、图纸和明细表，辅助出图自动化。",
                Instructions = "使用 list_views、list_sheets、list_schedules 核查交付内容；结果中使用中文视图类型说明，并指出模板、未放置视图或空图纸。",
                Category = "交付",
                Source = "内置",
                TrustLevel = "官方内置",
                TriggerKeywords = ["交付", "视图", "图纸", "明细表", "空图纸", "未放置"],
                RecommendedCommands = ["list_views", "list_sheets", "list_schedules"]
            },
            new AgentSkill
            {
                Name = "呆猫工作室精装自动化",
                Description = "调用精装零件、硬装、软装、材质、注释、出图和智能设计工具。",
                Instructions = "仅当用户明确提到精装或呆猫时使用。先用 finish_toolkit_info 查询 capabilities 或 readiness。可直接执行 number_parts、create_arrangement_views、create_description_statistics、check_ceiling_light_text；其余使用 open_finish_builder、open_finish_layers、open_material_tools、open_interior_plan_sheets、open_interior_elevation_sheets、open_auto_dimension、open_material_tags、open_annotation_tools、open_plan_layout、open_soft_furnishing、open_electrical_layout、open_type_rename 打开配置面板。所有执行动作通过 finish_toolkit_run，必须先 Dry-run 并由用户确认。",
                Category = "精装",
                Source = "内置适配",
                TrustLevel = "官方内置",
                TriggerKeywords = ["呆猫", "精装", "硬装", "软装", "Finish Toolkit", "FinishParts"],
                RecommendedCommands = ["finish_toolkit_info", "finish_toolkit_run"]
            }
        ];
    }
}

internal sealed class AiProviderProfile
{
    public AiProviderKind Provider { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string? EncryptedApiKey { get; set; }

    public int TimeoutSeconds { get; set; } = 60;

    public int MaxTokens { get; set; } = 512;

    public AiApiMode ApiMode { get; set; } = AiApiMode.ChatCompletions;

    [JsonIgnore]
    public bool HasApiKey => !string.IsNullOrWhiteSpace(EncryptedApiKey);

    public string? GetApiKey()
    {
        return AiSettingsStore.UnprotectSecret(EncryptedApiKey);
    }

    public void SetApiKey(string? apiKey)
    {
        EncryptedApiKey = string.IsNullOrWhiteSpace(apiKey)
            ? null
            : AiSettingsStore.ProtectSecret(apiKey.Trim());
    }
}

internal static class AiProviderDefaults
{
    public static List<AiProviderProfile> CreateDefaults()
    {
        return
        [
            new AiProviderProfile
            {
                Provider = AiProviderKind.OpenAI,
                DisplayName = "OpenAI",
                BaseUrl = "https://api.openai.com/v1",
                Model = "gpt-4.1-mini",
                TimeoutSeconds = 60,
                MaxTokens = 512
            },
            new AiProviderProfile
            {
                Provider = AiProviderKind.Zhipu,
                DisplayName = "智谱 GLM",
                BaseUrl = "https://open.bigmodel.cn/api/paas/v4",
                Model = "glm-5.2",
                TimeoutSeconds = 60,
                MaxTokens = 512
            },
            new AiProviderProfile
            {
                Provider = AiProviderKind.Kimi,
                DisplayName = "Kimi / Moonshot",
                BaseUrl = "https://api.moonshot.cn/v1",
                Model = "kimi-k2.6",
                TimeoutSeconds = 60,
                MaxTokens = 512
            },
            new AiProviderProfile
            {
                Provider = AiProviderKind.Volcengine,
                DisplayName = "火山方舟 / 豆包",
                BaseUrl = "https://ark.cn-beijing.volces.com/api/v3",
                Model = "doubao-seed-evolving",
                ApiMode = AiApiMode.Responses,
                TimeoutSeconds = 300,
                MaxTokens = 16384
            }
        ];
    }
}

internal static class AiSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly byte[] SecretEntropy = Encoding.UTF8.GetBytes("RevitCodexBridge.AiSettings.v1");

    public static string SettingsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RevitCodexBridge",
        "ai-settings.json");

    public static AiSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AiSettings();
            }

            var json = File.ReadAllText(SettingsPath, Encoding.UTF8);
            var settings = JsonSerializer.Deserialize<AiSettings>(json, JsonOptions) ?? new AiSettings();
            settings.EnsureDefaults();
            return settings;
        }
        catch (Exception ex)
        {
            BridgeLog.Error("Failed to load AI settings.", ex);
            return new AiSettings();
        }
    }

    public static void Save(AiSettings settings)
    {
        settings.EnsureDefaults();

        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json, new UTF8Encoding(false));
        BridgeLog.Info($"AI settings saved: {SettingsPath}");
    }

    public static string ProtectSecret(string secret)
    {
        var bytes = Encoding.UTF8.GetBytes(secret);
        var protectedBytes = ProtectedData.Protect(bytes, SecretEntropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    public static string? UnprotectSecret(string? encryptedSecret)
    {
        if (string.IsNullOrWhiteSpace(encryptedSecret))
        {
            return null;
        }

        try
        {
            var protectedBytes = Convert.FromBase64String(encryptedSecret);
            var bytes = ProtectedData.Unprotect(protectedBytes, SecretEntropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            BridgeLog.Error("Failed to decrypt AI API key.", ex);
            return null;
        }
    }

    public static string GetActiveProviderSummary()
    {
        var profile = Load().GetActiveProfile();
        var keyState = profile.HasApiKey ? "已保存密钥" : "未配置密钥";
        return $"{profile.DisplayName} / {profile.Model} / {keyState}";
    }
}
