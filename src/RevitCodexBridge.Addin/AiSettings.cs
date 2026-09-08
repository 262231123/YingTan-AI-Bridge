using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RevitCodexBridge.Addin;

internal enum AiProviderKind
{
    OpenAI,
    DeepSeek,
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

        if (SettingsVersion < 5)
        {
            // V5 adds DeepSeek V4 Pro as an OpenAI-compatible provider. Existing
            // encrypted keys and active-provider choices are left untouched.
            SettingsVersion = 5;
        }

        if (SettingsVersion < 6)
        {
            // V6 introduces the local Script Studio artifact workflow.
            SettingsVersion = 6;
        }

        if (SettingsVersion < 7)
        {
            // V7 expands built-in workflow skills and strengthens Script Studio validation.
            SettingsVersion = 7;
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
                Name = "对话式设计执行",
                Category = "建模", Source = "内置", TrustLevel = "官方内置", Version = "0.5.0",
                Description = "从当前模型读取设计依赖，连续查询后创建矩形房间、墙、门窗或图纸。",
                Instructions = "使用本轮 get_model_context 的标高、墙类型与选择集。矩形围合需求使用 create_room_layout，说明墙中心线尺寸、原点和不含门窗楼板；标高或类型有歧义时询问。不要用示例模型替代用户的具体设计需求。前一阶段的写入产生 ID 后，下一阶段再查询并放置门窗。",
                TriggerKeywords = ["设计", "房间", "办公室", "会议室", "创建", "建模"],
                RecommendedCommands = ["get_model_context", "find_elements", "create_room_layout", "create_wall", "place_door", "place_window"]
            },
            new AgentSkill
            {
                Name = "选择集连续编辑",
                Category = "数据治理", Source = "内置", TrustLevel = "官方内置", Version = "0.5.0",
                Description = "解析当前选择与条件过滤，核实真实参数后批量修改。",
                Instructions = "@选择集和这些构件以本轮选择为准。find_elements 分页查询范围，get_element 核实参数存储类型、单位、旧值和可写性后才能 set_parameter；超过40项拆批次并明确未处理范围。",
                TriggerKeywords = ["选择集", "选中", "这些", "注释", "批量", "修改"],
                RecommendedCommands = ["get_selection", "find_elements", "get_element", "set_parameter"]
            },
            new AgentSkill
            {
                Name = "实时警告核查",
                Category = "QA/QC", Source = "内置", TrustLevel = "官方内置", Version = "0.5.0",
                Description = "直接读取 Revit 警告并分页定位问题构件。",
                Instructions = "使用 list_warnings 获取真实警告，按 offset/limit 分页；get_element 核查、show_elements 定位。报告完整性取决于 hasMore，不把未读取的警告算作已审查；不会自动修复警告。",
                TriggerKeywords = ["警告", "审计", "质量", "检查模型"],
                RecommendedCommands = ["list_warnings", "get_element", "show_elements"]
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
                Name = "模型健康审计",
                Description = "按可解释口径检查模型基础信息、墙体、类型、视图、图纸与明细表完整性。",
                Instructions = "先读取项目和当前文档，再分项检查标高、墙类型、墙体统计、视图、图纸和明细表。报告必须区分已核实事实、疑似问题和当前命令无法验证的项目；不得把未实现的碰撞检测或规范校核描述为已完成。",
                Category = "QA/QC",
                Source = "内置（参考公开 Revit MCP 工作流）",
                TrustLevel = "官方内置",
                TriggerKeywords = ["模型健康", "模型审计", "质量检查", "QA", "QC", "模型检查", "健康度"],
                RecommendedCommands = ["get_active_document", "list_levels", "list_wall_types", "analyze_walls", "list_views", "list_sheets", "list_schedules"]
            },
            new AgentSkill
            {
                Name = "参数治理",
                Description = "检查选定构件的参数，并以最小变更计划规范化可写参数。",
                Instructions = "先用 get_selection 和 get_element 核实 ElementId、参数名、存储类型、单位和只读状态。批量修改时逐项给出旧值与目标值，先 Dry-run；不得猜测参数名或把类型参数当作实例参数写入。",
                Category = "数据治理",
                Source = "内置（参考公开 Revit MCP 工作流）",
                TrustLevel = "官方内置",
                TriggerKeywords = ["参数治理", "批量参数", "参数规范", "参数检查", "参数填写", "属性修改"],
                RecommendedCommands = ["get_selection", "get_element", "set_parameter"]
            },
            new AgentSkill
            {
                Name = "空间与房间规划",
                Description = "检查标高和房间放置条件，并生成可验证的房间创建计划。",
                Instructions = "创建房间前先确认目标标高、坐标、边界闭合条件、名称和编号。当前能力不能自动证明边界闭合，必须明确提示用户在 Dry-run 后检查房间是否可放置。",
                Category = "空间规划",
                Source = "内置（参考公开 Revit MCP 工作流）",
                TrustLevel = "官方内置",
                TriggerKeywords = ["房间规划", "空间规划", "创建房间", "房间编号", "房间名称", "空间"],
                RecommendedCommands = ["get_active_document", "list_levels", "create_room"]
            },
            new AgentSkill
            {
                Name = "门窗宿主协调",
                Description = "核实族类型、墙宿主和放置坐标后创建门窗。",
                Instructions = "先查询门窗族类型，再通过选择集或构件详情确认墙宿主 ElementId；不得仅凭自然语言猜测宿主。放置前检查标高、族名、类型名和毫米坐标，写入必须先 Dry-run。",
                Category = "建模",
                Source = "内置（参考公开 Revit MCP 工作流）",
                TrustLevel = "官方内置",
                TriggerKeywords = ["门窗", "放置门", "放置窗", "门族", "窗族", "宿主墙"],
                RecommendedCommands = ["list_levels", "list_family_symbols", "get_selection", "get_element", "place_door", "place_window"]
            },
            new AgentSkill
            {
                Name = "交付预检",
                Description = "在出图前核对视图、图纸、明细表及未放置内容。",
                Instructions = "先列出视图、图纸和明细表，识别空图纸、未放置视图和统计异常，再决定是否生成图纸集。当前能力不执行 PDF、DWG 或 IFC 导出，用户要求导出时应说明限制。",
                Category = "交付",
                Source = "内置（参考公开 Revit MCP 工作流）",
                TrustLevel = "官方内置",
                TriggerKeywords = ["交付预检", "出图检查", "发布检查", "图纸检查", "未放置视图", "空图纸"],
                RecommendedCommands = ["list_views", "list_sheets", "list_schedules", "create_drawing_set"]
            },
            new AgentSkill
            {
                Name = "安全批量执行",
                Description = "把多步骤模型修改组织成可审阅、可回滚的最小批次。",
                Instructions = "先查询所有依赖，再将操作按依赖顺序组织。第一次执行保持 Dry-run；真实写入应使用原子批次，任一步失败则整体回滚。不得为了减少步骤而跳过类型、标高、宿主或 ElementId 核验。",
                Category = "安全",
                Source = "内置（参考公开 Revit MCP 操作技能）",
                TrustLevel = "官方内置",
                TriggerKeywords = ["批量", "批处理", "原子", "回滚", "多步骤", "自动执行"],
                RecommendedCommands = ["get_active_document", "list_levels", "list_wall_types", "list_family_symbols", "get_element"]
            },
            new AgentSkill
            {
                Name = "脚本工作室",
                Description = "根据提示词生成 C# ExternalCommand、pyRevit、Dynamo Python 或基础 .dyn 工件。",
                Instructions = "仅生成并保存代码，不自动编译或运行。必须禁止网络、外部进程、注册表、删除文件、动态加载和密钥；模型写入必须具有 Transaction。输出后进行静态检查，并要求用户人工审阅后在项目副本中测试。",
                Category = "开发",
                Source = "内置",
                TrustLevel = "官方内置",
                TriggerKeywords = ["脚本", "代码", "C#", "ExternalCommand", "pyRevit", "Python", "Dynamo", ".dyn"],
                RecommendedCommands = []
            },
            new AgentSkill
            {
                Name = "工作共享安全预检",
                Description = "在批量修改前识别工作共享、元素所有权和中心模型风险。",
                Instructions = "先读取活动文档并明确是否为工作共享模型、当前是否为本地副本、目标元素是否可编辑。现有原生命令不能同步中心或借用工作集；需要深入检查时只生成只读脚本，不得自动 EnableWorksharing、ReloadLatest 或 SynchronizeWithCentral。",
                Category = "协作",
                Source = "内置（依据 Autodesk Revit API 工作共享指南）",
                TrustLevel = "官方内置",
                TriggerKeywords = ["工作共享", "中心模型", "本地模型", "工作集", "借用", "同步中心", "所有权"],
                RecommendedCommands = ["get_active_document", "get_selection", "get_element"]
            },
            new AgentSkill
            {
                Name = "警告分诊与修复建议",
                Description = "按类别、影响和可修复性组织 Revit 警告检查。",
                Instructions = "先区分可由当前命令核实的问题与需要生成只读 Revit API 脚本读取 Document.GetWarnings 的问题。默认只输出分组报告和 ElementId，不自动删除、解绑或修改构件；修复必须拆成最小 Dry-run 计划。",
                Category = "QA/QC",
                Source = "内置（依据 Autodesk Revit API 文档）",
                TrustLevel = "官方内置",
                TriggerKeywords = ["警告", "warnings", "重复标记", "重叠", "房间未封闭", "警告修复"],
                RecommendedCommands = ["get_active_document", "get_element", "show_elements"]
            },
            new AgentSkill
            {
                Name = "族与类型标准审计",
                Description = "检查族类型命名、重复类型、关键参数与放置条件。",
                Instructions = "先用 list_family_symbols 获取可核实类型；需要读取族分类、共享参数或类型重复详情时生成只读脚本。不得自动覆盖族文件、删除类型或更改共享参数定义；任何规范化操作先输出旧值、目标值和受影响实例数。",
                Category = "标准化",
                Source = "内置",
                TrustLevel = "官方内置",
                TriggerKeywords = ["族检查", "族标准", "类型命名", "重复类型", "族参数", "族库"],
                RecommendedCommands = ["list_family_symbols", "count_elements", "get_element", "set_parameter"]
            },
            new AgentSkill
            {
                Name = "阶段与设计方案审计",
                Description = "核对构件阶段、视图阶段和设计选项归属。",
                Instructions = "阶段和设计选项默认只读检查；先查询视图和目标构件，无法由原生命令核实时生成只读脚本。报告应区分创建阶段、拆除阶段、主模型与选项集，不得自动接受主方案或删除设计选项。",
                Category = "QA/QC",
                Source = "内置（依据 Autodesk Revit 2026 API 设置文档）",
                TrustLevel = "官方内置",
                TriggerKeywords = ["阶段", "拆除", "现状", "设计选项", "方案", "主模型"],
                RecommendedCommands = ["list_views", "get_selection", "get_element"]
            },
            new AgentSkill
            {
                Name = "链接与坐标核查",
                Description = "检查 Revit 链接、共享坐标、定位方式与链接状态。",
                Instructions = "默认生成只读审计脚本，列出链接实例、路径状态、变换、定位方式和共享坐标信息。不得自动发布坐标、获取坐标、重新载入或卸载链接；这类动作必须由用户在 Revit 中确认并操作。",
                Category = "协调",
                Source = "内置",
                TrustLevel = "官方内置",
                TriggerKeywords = ["链接", "共享坐标", "项目基点", "测量点", "坐标核查", "链接丢失"],
                RecommendedCommands = ["get_active_document", "list_views"]
            },
            new AgentSkill
            {
                Name = "MEP 数据完整性检查",
                Description = "检查机电构件的系统归属、连接器和关键参数完整性。",
                Instructions = "现有原生命令只支持通用构件计数和参数读取；连接器、系统分类和未连接端检查应生成只读脚本。报告按专业和系统分组，并给出 ElementId；不得自动连接、断开或改系统。",
                Category = "MEP",
                Source = "内置",
                TrustLevel = "官方内置",
                TriggerKeywords = ["MEP", "机电", "风管", "水管", "桥架", "连接器", "未连接", "系统分类"],
                RecommendedCommands = ["count_elements", "get_selection", "get_element", "show_elements"]
            },
            new AgentSkill
            {
                Name = "无障碍与规则检查脚本",
                Description = "把企业或项目规则转成可追溯的只读检查脚本。",
                Instructions = "先要求用户给出适用地区、规范版本、尺寸阈值和例外条件；不得凭常识宣称合规。生成的脚本只负责测量、标记和报告，结果必须注明单位、容差、规则来源和无法判断项。",
                Category = "规则检查",
                Source = "内置",
                TrustLevel = "官方内置",
                TriggerKeywords = ["无障碍", "净宽", "净高", "坡道", "规则检查", "规范检查", "合规"],
                RecommendedCommands = ["get_active_document", "get_selection", "get_element", "show_elements"]
            },
            new AgentSkill
            {
                Name = "Dynamo 工作流编排",
                Description = "设计 Dynamo 输入输出、节点分组、事务和版本兼容策略。",
                Instructions = "优先生成小型可验证图或 Dynamo Python 节点，明确 IN/OUT、单位、空值行为和依赖包。写入必须通过 TransactionManager；不得假设第三方节点包已安装。复杂流程拆成读取、验证、写入三组。",
                Category = "Dynamo",
                Source = "内置",
                TrustLevel = "官方内置",
                TriggerKeywords = ["Dynamo", ".dyn", "节点", "IN", "OUT", "图编排"],
                RecommendedCommands = []
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
                Provider = AiProviderKind.DeepSeek,
                DisplayName = "DeepSeek V4 Pro",
                BaseUrl = "https://api.deepseek.com",
                Model = "deepseek-v4-pro",
                ApiMode = AiApiMode.ChatCompletions,
                TimeoutSeconds = 120,
                MaxTokens = 8192
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
