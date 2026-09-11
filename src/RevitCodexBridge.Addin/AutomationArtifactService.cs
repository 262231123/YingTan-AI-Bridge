using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RevitCodexBridge.Addin;

internal sealed record AutomationArtifactResult(
    string Reply,
    string Format,
    string FilePath,
    string Sha256,
    IReadOnlyList<string> Warnings);

internal static class AutomationArtifactService
{
    private static readonly string[] TriggerWords =
    [
        "脚本", "代码", "externalcommand", "pyrevit", "python", "dynamo", ".dyn", "节点图", "宏"
    ];

    private static readonly string[] ForbiddenCode =
    [
        "process.start", "system.diagnostics.process", "cmd.exe", "powershell", "dllimport",
        "assembly.load", "httpclient", "webclient", "file.delete", "directory.delete",
        "registry.", "environment.exit", "subprocess", "os.system", "shutil.rmtree",
        "requests.", "urllib.", "socket."
    ];

    public static bool ShouldGenerate(string request)
    {
        var value = request ?? string.Empty;
        // Explicit download-only / alternate-runtime artifacts remain in the studio.
        // Requests to use C# scripts to inspect or operate the model belong to the live agent.
        var exportOnly = new[] { "只生成", "仅生成", "不要执行", "不执行", "保存为", "导出脚本", "下载脚本", "dynamo", "pyrevit", "python", ".dyn", "externalcommand" }
            .Any(word => value.Contains(word, StringComparison.OrdinalIgnoreCase));
        if (!exportOnly) return false;
        return TriggerWords.Any(word => value.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
            && (value.Contains("生成", StringComparison.OrdinalIgnoreCase)
                || value.Contains("编写", StringComparison.OrdinalIgnoreCase)
                || value.Contains("创建", StringComparison.OrdinalIgnoreCase)
                || value.Contains("制作", StringComparison.OrdinalIgnoreCase)
                || value.Contains("帮我做", StringComparison.OrdinalIgnoreCase)
                || value.Contains("写", StringComparison.OrdinalIgnoreCase));
    }

    public static async Task<AutomationArtifactResult> GenerateAndSaveAsync(
        AiSettings settings,
        string request,
        string revitVersion,
        CancellationToken cancellationToken = default)
    {
        var configuredProfile = settings.GetActiveProfile();
        var profile = new AiProviderProfile
        {
            Provider = configuredProfile.Provider,
            DisplayName = configuredProfile.DisplayName,
            BaseUrl = configuredProfile.BaseUrl,
            Model = configuredProfile.Model,
            EncryptedApiKey = configuredProfile.EncryptedApiKey,
            ApiMode = configuredProfile.ApiMode,
            TimeoutSeconds = Math.Max(configuredProfile.TimeoutSeconds, 120),
            MaxTokens = Math.Max(configuredProfile.MaxTokens, 8192)
        };
        var system = BuildSystemPrompt(revitVersion, InferRequestedFormat(request));
        var result = await OpenAiCompatibleClient.CompleteAsync(profile, request, system, cancellationToken);
        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.ResponseText))
        {
            throw new InvalidOperationException(result.Message);
        }

        var artifact = Parse(result.ResponseText);
        var warnings = Validate(artifact.Format, artifact.Content);
        var errors = warnings.Where(item => item.StartsWith("阻止：", StringComparison.Ordinal)).ToList();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException("脚本未保存，静态安全检查未通过：\n" + string.Join("\n", errors));
        }

        var extension = ExtensionFor(artifact.Format);
        var safeName = SanitizeFileName(Path.GetFileNameWithoutExtension(artifact.FileName));
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RevitCodexBridge",
            "script-studio");
        Directory.CreateDirectory(directory);

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var filePath = Path.Combine(directory, $"{stamp}-{safeName}{extension}");
        File.WriteAllText(filePath, artifact.Content, new UTF8Encoding(false));
        var sha256 = ComputeSha256(artifact.Content);

        var metadata = new
        {
            schemaVersion = "1.0",
            createdAt = DateTimeOffset.Now,
            artifact.Format,
            fileName = Path.GetFileName(filePath),
            revitVersion,
            artifact.Description,
            artifact.Dependencies,
            sha256,
            warnings,
            executed = false
        };
        File.WriteAllText(
            filePath + ".metadata.json",
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));

        return new AutomationArtifactResult(artifact.Reply, artifact.Format, filePath, sha256, warnings);
    }

    private static string BuildSystemPrompt(string revitVersion, string requestedFormat) =>
        $"你是 YingTan Revit 脚本工作室。为 Revit {revitVersion} 生成一个完整但不执行的自动化工件。\n" +
        $"用户要求的优先格式为 {requestedFormat}；除非用户明确表达其他格式，否则必须使用它。\n" +
        "仅输出一个 JSON 对象，不要 Markdown 代码围栏：\n" +
        "{\"reply\":\"中文说明\",\"artifact\":{\"format\":\"csharp-external-command|pyrevit-python|dynamo-python|dynamo-graph\",\"fileName\":\"文件名\",\"content\":\"完整文件内容\",\"description\":\"用途\",\"dependencies\":[\"依赖\"]}}\n" +
        "C# 必须实现 IExternalCommand 并声明 Autodesk.Revit.Attributes.Transaction；pyRevit 使用 revit.doc；Dynamo Python 使用 IN/OUT；dynamo-graph 必须输出可解析的 .dyn JSON，含 Nodes、Connectors 和 View。\n" +
        "所有模型写入必须使用 Revit Transaction 或 Dynamo TransactionManager，并在执行前校验活动文档、选择集、类型和参数。不得访问网络、启动进程、调用 PowerShell/cmd、操作注册表、删除文件、动态加载程序集或包含密钥。不得声称已经编译或执行。依赖不能内嵌二进制。";

    private static ParsedArtifact Parse(string response)
    {
        var json = ExtractJson(response);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("artifact", out var node) || node.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("模型没有返回有效的 artifact 对象。");
        }

        var reply = root.TryGetProperty("reply", out var replyNode) ? replyNode.GetString() ?? string.Empty : string.Empty;
        var format = RequiredString(node, "format").Trim().ToLowerInvariant();
        var fileName = RequiredString(node, "fileName");
        var content = RequiredString(node, "content");
        var description = node.TryGetProperty("description", out var desc) ? desc.GetString() ?? string.Empty : string.Empty;
        var dependencies = node.TryGetProperty("dependencies", out var deps) && deps.ValueKind == JsonValueKind.Array
            ? deps.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? string.Empty).ToArray()
            : [];
        return new ParsedArtifact(reply, format, fileName, content, description, dependencies);
    }

    private static List<string> Validate(string format, string content)
    {
        _ = ExtensionFor(format);
        var findings = new List<string>();
        if (content.Length < 40)
        {
            findings.Add("阻止：工件内容过短，无法构成完整脚本。");
        }
        if (content.Length > 500_000)
        {
            findings.Add("阻止：工件超过 500 KB 安全上限。");
        }
        foreach (var token in ForbiddenCode.Where(token => content.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0))
        {
            findings.Add($"阻止：检测到不允许的能力“{token}”。");
        }

        if (format == "csharp-external-command" &&
            (!content.Contains("IExternalCommand", StringComparison.Ordinal) || !content.Contains("Execute(", StringComparison.Ordinal)))
        {
            findings.Add("阻止：C# 工件未实现 IExternalCommand.Execute。");
        }
        if (format == "csharp-external-command" &&
            !content.Contains("Transaction(TransactionMode.", StringComparison.Ordinal))
        {
            findings.Add("阻止：C# ExternalCommand 未声明 TransactionAttribute。");
        }
        if (format == "dynamo-python" &&
            (!content.Contains("IN[", StringComparison.Ordinal) || !content.Contains("OUT", StringComparison.Ordinal)))
        {
            findings.Add("阻止：Dynamo Python 工件缺少 IN/OUT 接口。");
        }

        if (format == "dynamo-graph")
        {
            try
            {
                using var graph = JsonDocument.Parse(content);
                if (!graph.RootElement.TryGetProperty("Nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array ||
                    !graph.RootElement.TryGetProperty("Connectors", out var connectors) || connectors.ValueKind != JsonValueKind.Array ||
                    !graph.RootElement.TryGetProperty("View", out var view) || view.ValueKind != JsonValueKind.Object)
                {
                    findings.Add("阻止：Dynamo 图缺少 Nodes、Connectors 或 View。");
                }
            }
            catch (JsonException)
            {
                findings.Add("阻止：Dynamo .dyn 内容不是有效 JSON。");
            }
        }

        if (LooksLikeWrite(content))
        {
            var hasTransaction = format switch
            {
                "dynamo-python" => content.Contains("TransactionManager", StringComparison.Ordinal),
                "dynamo-graph" => true,
                _ => content.Contains("Transaction", StringComparison.Ordinal)
            };
            if (!hasTransaction)
            {
                findings.Add("阻止：检测到模型写入代码，但未检测到事务保护。");
            }
        }

        if (findings.Count == 0)
        {
            findings.Add("提示：已通过规则型静态检查，但仍需人工审阅，并在项目副本中测试。");
        }
        return findings;
    }

    private static bool LooksLikeWrite(string content)
    {
        string[] tokens = ["Wall.Create", "NewFamilyInstance", ".Set(", ".Delete(", "doc.Create", "Document.Create"];
        return tokens.Any(token => content.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static string InferRequestedFormat(string request)
    {
        if (request.IndexOf(".dyn", StringComparison.OrdinalIgnoreCase) >= 0 ||
            request.IndexOf("节点图", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "dynamo-graph";
        }
        if (request.IndexOf("dynamo", StringComparison.OrdinalIgnoreCase) >= 0 &&
            request.IndexOf("python", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "dynamo-python";
        }
        if (request.IndexOf("c#", StringComparison.OrdinalIgnoreCase) >= 0 ||
            request.IndexOf("externalcommand", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "csharp-external-command";
        }
        return "pyrevit-python";
    }

    private static string ExtensionFor(string format) => format switch
    {
        "csharp-external-command" => ".cs",
        "pyrevit-python" => ".py",
        "dynamo-python" => ".py",
        "dynamo-graph" => ".dyn",
        _ => throw new InvalidOperationException($"不支持的脚本格式：{format}")
    };

    private static string ExtractJson(string value)
    {
        var trimmed = value.Trim();
        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            throw new InvalidOperationException("模型没有返回可解析的 JSON。");
        }
        return trimmed.Substring(start, end - start + 1);
    }

    private static string RequiredString(JsonElement node, string name)
    {
        if (!node.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidOperationException($"artifact.{name} 缺失。");
        }
        return value.GetString()!;
    }

    private static string SanitizeFileName(string value)
    {
        var cleaned = string.Concat((string.IsNullOrWhiteSpace(value) ? "revit-automation" : value)
            .Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '-' : character));
        return cleaned.Length > 64 ? cleaned.Substring(0, 64) : cleaned;
    }

    private static string ComputeSha256(string content)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(content))).Replace("-", string.Empty).ToLowerInvariant();
    }

    private sealed record ParsedArtifact(
        string Reply,
        string Format,
        string FileName,
        string Content,
        string Description,
        IReadOnlyList<string> Dependencies);
}
