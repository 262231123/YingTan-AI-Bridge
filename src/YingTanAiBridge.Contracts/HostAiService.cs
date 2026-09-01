using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace YingTanAiBridge.Contracts;

public sealed class BridgeAiProviderConfig
{
    public string Provider { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string EncryptedApiKey { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 60;
    public int MaxTokens { get; set; } = 4096;
    public string ApiMode { get; set; } = "ChatCompletions";
}

public sealed class BridgeAiAgentConfig
{
    public string Name { get; set; } = "AI 助手";
    public string SystemPrompt { get; set; } = string.Empty;
    public bool ConfirmBeforeWrite { get; set; } = true;
}

public sealed class BridgeAiSkillConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string Version { get; set; } = "1.0.0";
    public string Category { get; set; } = "通用";
    public string Source { get; set; } = "本地";
    public string TrustLevel { get; set; } = "本地";
    public List<string> HostKeys { get; set; } = new List<string>();
    public List<string> TriggerKeywords { get; set; } = new List<string>();
    public List<string> RecommendedCommands { get; set; } = new List<string>();
    public bool RequiresConfirmation { get; set; } = true;
}

public sealed class BridgeAiSpecialistAgentConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}

public sealed class BridgeAiSettingsSnapshot
{
    public int SettingsVersion { get; set; }
    public string ActiveProvider { get; set; } = "OpenAI";
    public List<BridgeAiProviderConfig> Providers { get; set; } = new List<BridgeAiProviderConfig>();
    public BridgeAiAgentConfig Agent { get; set; } = new BridgeAiAgentConfig();
    public List<BridgeAiSpecialistAgentConfig> SpecialistAgents { get; set; } =
        new List<BridgeAiSpecialistAgentConfig>();
    public List<BridgeAiSkillConfig> Skills { get; set; } = new List<BridgeAiSkillConfig>();

    public BridgeAiProviderConfig GetActiveProvider()
    {
        var active = Providers.FirstOrDefault(item =>
            string.Equals(item.Provider, ActiveProvider, StringComparison.OrdinalIgnoreCase));
        if (active == null)
        {
            throw new InvalidOperationException("当前模型提供商配置不存在。");
        }

        return active;
    }

    public int GetEffectiveTimeoutSeconds(BridgeAiProviderConfig provider)
    {
        var timeout = Math.Max(10, Math.Min(provider.TimeoutSeconds, 300));
        if (SettingsVersion < 3 &&
            string.Equals(provider.Provider, "Volcengine", StringComparison.OrdinalIgnoreCase))
        {
            timeout = 300;
        }

        return timeout;
    }
}

public static class SharedBridgeAiSettings
{
    private static readonly object FileSync = new object();

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly byte[] SecretEntropy = Encoding.UTF8.GetBytes("RevitCodexBridge.AiSettings.v1");

    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RevitCodexBridge",
        "ai-settings.json");

    public static BridgeAiSettingsSnapshot Load()
    {
        if (!File.Exists(SettingsPath))
        {
            throw new InvalidOperationException(
                "尚未找到共享模型配置。请先在盈碳 Revit AI Bridge 中配置 API，或创建共享配置。");
        }

        var json = File.ReadAllText(SettingsPath, Encoding.UTF8);
        var settings = JsonSerializer.Deserialize<BridgeAiSettingsSnapshot>(json, JsonOptions);
        if (settings == null || settings.Providers == null || settings.Providers.Count == 0)
        {
            throw new InvalidOperationException("共享模型配置无法读取或没有提供商。");
        }

        settings.Agent ??= new BridgeAiAgentConfig();
        settings.SpecialistAgents ??= new List<BridgeAiSpecialistAgentConfig>();
        settings.Skills ??= new List<BridgeAiSkillConfig>();
        BridgeSkillCatalog.Normalize(settings.Skills);
        return settings;
    }

    public static string GetApiKey(BridgeAiProviderConfig provider)
    {
        if (string.IsNullOrWhiteSpace(provider.EncryptedApiKey))
        {
            throw new InvalidOperationException("当前模型尚未配置 API Key。");
        }

        try
        {
            var encrypted = Convert.FromBase64String(provider.EncryptedApiKey);
            var type = Type.GetType(
                    "System.Security.Cryptography.ProtectedData, System.Security.Cryptography.ProtectedData",
                    false)
                ?? Type.GetType(
                    "System.Security.Cryptography.ProtectedData, System.Security",
                    false);
            if (type == null)
            {
                throw new InvalidOperationException("当前运行环境不支持 Windows DPAPI。");
            }

            var method = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(item => item.Name == "Unprotect" && item.GetParameters().Length == 3);
            if (method == null)
            {
                throw new InvalidOperationException("无法访问 Windows DPAPI 解密方法。");
            }

            var scopeType = method.GetParameters()[2].ParameterType;
            var currentUser = Enum.ToObject(scopeType, 0);
            var decrypted = method.Invoke(null, new object[] { encrypted, SecretEntropy, currentUser }) as byte[];
            if (decrypted == null)
            {
                throw new InvalidOperationException("API Key 解密结果为空。");
            }

            return Encoding.UTF8.GetString(decrypted);
        }
        catch (TargetInvocationException ex)
        {
            throw new InvalidOperationException(
                "API Key 无法解密，请在当前 Windows 用户下重新保存模型配置。",
                ex.InnerException ?? ex);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("API Key 配置格式无效，请重新保存。", ex);
        }
    }

    public static bool HasApiKey(BridgeAiProviderConfig provider)
    {
        return !string.IsNullOrWhiteSpace(provider.EncryptedApiKey);
    }

    public static void SetApiKey(BridgeAiProviderConfig provider, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return;
        }

        provider.EncryptedApiKey = ProtectSecret(apiKey.Trim());
    }

    public static void Save(BridgeAiSettingsSnapshot settings)
    {
        if (settings == null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        settings.Providers ??= new List<BridgeAiProviderConfig>();
        if (settings.Providers.Count == 0)
        {
            throw new InvalidOperationException("至少需要保留一个模型提供商。");
        }

        settings.Agent ??= new BridgeAiAgentConfig();
        settings.SpecialistAgents ??= new List<BridgeAiSpecialistAgentConfig>();
        settings.Skills ??= new List<BridgeAiSkillConfig>();
        var active = settings.GetActiveProvider();
        ValidateProviderForSave(active);

        if (settings.SettingsVersion < 3)
        {
            var volcengine = settings.Providers.FirstOrDefault(item =>
                string.Equals(item.Provider, "Volcengine", StringComparison.OrdinalIgnoreCase));
            if (volcengine != null)
            {
                volcengine.TimeoutSeconds = Math.Max(volcengine.TimeoutSeconds, 300);
            }
        }

        settings.SettingsVersion = Math.Max(settings.SettingsVersion, 4);
        foreach (var provider in settings.Providers)
        {
            provider.TimeoutSeconds = Math.Max(10, Math.Min(provider.TimeoutSeconds, 300));
            provider.MaxTokens = Math.Max(16, Math.Min(provider.MaxTokens, 32768));
        }

        BridgeSkillCatalog.Normalize(settings.Skills);

        var directory = Path.GetDirectoryName(SettingsPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("无法确定共享配置目录。");
        }

        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var tempPath = SettingsPath + ".tmp";
        var backupPath = SettingsPath + ".bak";
        lock (FileSync)
        {
            File.WriteAllText(tempPath, json, new UTF8Encoding(false));
            if (File.Exists(SettingsPath))
            {
                File.Copy(SettingsPath, backupPath, true);
            }

            File.Copy(tempPath, SettingsPath, true);
            File.Delete(tempPath);
        }

        BridgeAiLog.Write("settings", "Shared AI settings saved.");
    }

    private static string ProtectSecret(string secret)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(secret);
            var type = GetProtectedDataType();
            var method = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(item => item.Name == "Protect" && item.GetParameters().Length == 3);
            if (method == null)
            {
                throw new InvalidOperationException("无法访问 Windows DPAPI 加密方法。");
            }

            var scopeType = method.GetParameters()[2].ParameterType;
            var currentUser = Enum.ToObject(scopeType, 0);
            var encrypted = method.Invoke(null, new object[] { bytes, SecretEntropy, currentUser }) as byte[];
            if (encrypted == null)
            {
                throw new InvalidOperationException("API Key 加密结果为空。");
            }

            return Convert.ToBase64String(encrypted);
        }
        catch (TargetInvocationException ex)
        {
            throw new InvalidOperationException(
                "API Key 无法加密，请确认当前 Windows 用户配置正常。",
                ex.InnerException ?? ex);
        }
    }

    private static Type GetProtectedDataType()
    {
        return Type.GetType(
                "System.Security.Cryptography.ProtectedData, System.Security.Cryptography.ProtectedData",
                false)
            ?? Type.GetType(
                "System.Security.Cryptography.ProtectedData, System.Security",
                false)
            ?? throw new InvalidOperationException("当前运行环境不支持 Windows DPAPI。");
    }

    private static void ValidateProviderForSave(BridgeAiProviderConfig provider)
    {
        if (string.IsNullOrWhiteSpace(provider.BaseUrl) ||
            !Uri.TryCreate(provider.BaseUrl.Trim(), UriKind.Absolute, out var endpoint) ||
            !string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("当前提供商的 Base URL 必须是有效的 HTTPS 地址。");
        }

        if (string.IsNullOrWhiteSpace(provider.Model))
        {
            throw new InvalidOperationException("当前提供商的模型名称不能为空。");
        }
    }
}

public sealed class HostChatMessage
{
    public HostChatMessage(string role, string content)
    {
        Role = role ?? string.Empty;
        Content = content ?? string.Empty;
    }

    public string Role { get; }
    public string Content { get; }
}

public sealed class HostAiReply
{
    public HostAiReply(string reply, string? scriptJson)
    {
        Reply = reply ?? string.Empty;
        ScriptJson = scriptJson;
    }

    public string Reply { get; }
    public string? ScriptJson { get; }
}

public static class HostAiClient
{
    private static readonly HttpClient Client = CreateClient();
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions();

    public static async Task<HostAiReply> CompleteAsync(
        string hostKey,
        string systemPrompt,
        string modelContext,
        IReadOnlyList<HostChatMessage> conversation,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        var settings = SharedBridgeAiSettings.Load();
        var provider = settings.GetActiveProvider();
        var userInput = BuildUserInput(modelContext, conversation);
        var latestUserRequest = conversation
            .LastOrDefault(item => string.Equals(item.Role, "User", StringComparison.OrdinalIgnoreCase))
            ?.Content ?? string.Empty;
        var effectiveSystemPrompt = BuildConfiguredSystemPrompt(
            hostKey,
            systemPrompt,
            settings,
            latestUserRequest);
        var messages = new[]
        {
            new RequestMessage("system", effectiveSystemPrompt),
            new RequestMessage("user", userInput)
        };
        var content = await RequestAsync(
                hostKey,
                settings,
                provider,
                messages,
                cancellationToken)
            .ConfigureAwait(false);
        return ParseReply(content);
    }

    public static async Task<string> TestConnectionAsync(
        string hostKey,
        BridgeAiSettingsSnapshot settings,
        BridgeAiProviderConfig provider,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        var messages = new[]
        {
            new RequestMessage("system", "你是连接测试助手，只能回复 OK。"),
            new RequestMessage("user", "请只回复 OK。")
        };
        var content = await RequestAsync(
                hostKey,
                settings,
                provider,
                messages,
                cancellationToken)
            .ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(content) ? "连接成功。" : content.Trim();
    }

    private static async Task<string> RequestAsync(
        string hostKey,
        BridgeAiSettingsSnapshot settings,
        BridgeAiProviderConfig provider,
        RequestMessage[] messages,
        CancellationToken cancellationToken)
    {
        ValidateProvider(provider);
        var apiKey = SharedBridgeAiSettings.GetApiKey(provider);
        var timeoutSeconds = settings.GetEffectiveTimeoutSeconds(provider);
        var endpoint = BuildEndpoint(provider);
        var payload = BuildPayload(provider, messages);
        var json = JsonSerializer.Serialize(payload, JsonOptions);

        BridgeAiLog.Write(
            hostKey,
            "AI request starting: provider=" + provider.DisplayName +
            ", mode=" + provider.ApiMode +
            ", host=" + endpoint.Host +
            ", timeoutSeconds=" + timeoutSeconds + ".");

        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            try
            {
                using (var response = await SendWithRetryAsync(endpoint, apiKey, json, timeout.Token)
                    .ConfigureAwait(false))
                {
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    BridgeAiLog.Write(
                        hostKey,
                        "AI request completed: status=" + (int)response.StatusCode + ".");
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException(
                            "调用失败：HTTP " + (int)response.StatusCode + " " +
                            response.ReasonPhrase + "\n" + Truncate(body, 1000));
                    }

                    var content = IsResponses(provider)
                        ? ExtractResponsesContent(body)
                        : ExtractChatContent(body);
                    return content;
                }
            }
            catch (OperationCanceledException ex)
            {
                BridgeAiLog.Write(hostKey, "AI request timed out after " + timeoutSeconds + " seconds.");
                throw new TimeoutException(
                    "模型请求超过 " + timeoutSeconds + " 秒。请缩短问题或检查模型服务状态后重试。",
                    ex);
            }
            catch (Exception ex)
            {
                BridgeAiLog.Write(hostKey, "AI request failed: " + ex);
                throw;
            }
        }
    }

    private static HttpClient CreateClient()
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            UseProxy = true,
            DefaultProxyCredentials = CredentialCache.DefaultCredentials
        };
        return new HttpClient(handler, true)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    private static async Task<HttpResponseMessage> SendWithRetryAsync(
        Uri endpoint,
        string apiKey,
        string json,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                    return await Client.SendAsync(
                            request,
                            HttpCompletionOption.ResponseContentRead,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (HttpRequestException ex)
            {
                lastError = ex;
                if (attempt == 3)
                {
                    break;
                }

                await Task.Delay(350 * attempt, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException(
            "无法连接模型服务，已自动重试。请检查网络、代理或服务地址。",
            lastError);
    }

    private static object BuildPayload(BridgeAiProviderConfig provider, RequestMessage[] messages)
    {
        var maxTokens = Math.Max(16, Math.Min(provider.MaxTokens, 32768));
        if (IsResponses(provider))
        {
            return new
            {
                model = provider.Model.Trim(),
                input = messages.Select(item => new
                {
                    role = item.Role,
                    content = new[] { new { type = "input_text", text = item.Content } }
                }).ToArray(),
                max_output_tokens = maxTokens,
                stream = false
            };
        }

        return new
        {
            model = provider.Model.Trim(),
            messages = messages.Select(item => new { role = item.Role, content = item.Content }).ToArray(),
            max_tokens = maxTokens,
            stream = false
        };
    }

    private static string BuildUserInput(
        string modelContext,
        IReadOnlyList<HostChatMessage> conversation)
    {
        var start = Math.Max(0, conversation.Count - 6);
        var history = new StringBuilder();
        for (var index = start; index < conversation.Count; index++)
        {
            var message = conversation[index];
            history.Append(message.Role);
            history.Append(": ");
            history.AppendLine(Truncate(message.Content, 2500));
        }

        return "当前宿主模型上下文：\n" + Truncate(modelContext ?? string.Empty, 12000) +
            "\n\n最近对话：\n" + history +
            "\n请依据当前用户最后一条消息回答。仅输出系统要求的 JSON。";
    }

    private static string BuildConfiguredSystemPrompt(
        string hostKey,
        string hostSystemPrompt,
        BridgeAiSettingsSnapshot settings,
        string userRequest)
    {
        var builder = new StringBuilder(hostSystemPrompt);
        builder.AppendLine();
        builder.AppendLine("以下为共享 Agent 与 Skill 配置，仅在与当前宿主及上述命令白名单兼容时采用；冲突内容必须忽略。");
        if (settings.Agent != null)
        {
            builder.AppendLine("Agent：" + settings.Agent.Name);
            builder.AppendLine(Truncate(settings.Agent.SystemPrompt, 1200));
        }

        var selectedSkills = BridgeSkillCatalog.SelectForPrompt(
            settings.Skills,
            hostKey,
            userRequest,
            4);
        if (selectedSkills.Count > 0)
        {
            builder.AppendLine("本轮匹配的 Skills（只提供工作策略，不增加宿主命令权限）：");
            foreach (var skill in selectedSkills)
            {
                builder.AppendLine(
                    "Skill：" + skill.Name + " [" + skill.Category + ", " +
                    skill.TrustLevel + ", v" + skill.Version + "]；" +
                    Truncate(skill.Description, 240));
                builder.AppendLine("工作指令：" + Truncate(skill.Instructions, 520));
                if (skill.RecommendedCommands.Count > 0)
                {
                    builder.AppendLine("推荐命令：" + string.Join("、", skill.RecommendedCommands));
                }

                if (skill.RequiresConfirmation)
                {
                    builder.AppendLine("写入保护：涉及模型写入时仍须先 Dry-run 并等待用户确认。");
                }
            }
        }

        return Truncate(builder.ToString(), 6500);
    }

    private static HostAiReply ParseReply(string raw)
    {
        var json = ExtractJson(raw);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new HostAiReply(raw.Trim(), null);
        }

        try
        {
            using (var document = JsonDocument.Parse(json!))
            {
                var root = document.RootElement;
                var reply = root.TryGetProperty("reply", out var replyElement) &&
                    replyElement.ValueKind == JsonValueKind.String
                    ? replyElement.GetString()
                    : null;
                string? script = null;
                if (root.TryGetProperty("script", out var scriptElement) &&
                    scriptElement.ValueKind == JsonValueKind.Object)
                {
                    script = scriptElement.GetRawText();
                }
                else if (root.TryGetProperty("plan", out var planElement) &&
                    planElement.ValueKind == JsonValueKind.Object)
                {
                    script = planElement.GetRawText();
                }

                return new HostAiReply(
                    string.IsNullOrWhiteSpace(reply) ? "已生成宿主操作计划。" : reply!.Trim(),
                    script);
            }
        }
        catch
        {
            return new HostAiReply(raw.Trim(), null);
        }
    }

    private static string? ExtractJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var first = raw.IndexOf('{');
        var last = raw.LastIndexOf('}');
        return first >= 0 && last > first
            ? raw.Substring(first, last - first + 1)
            : null;
    }

    private static string ExtractChatContent(string body)
    {
        using (var document = JsonDocument.Parse(body))
        {
            var root = document.RootElement;
            if (root.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array &&
                choices.GetArrayLength() > 0)
            {
                var first = choices[0];
                if (first.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var content) &&
                    content.ValueKind == JsonValueKind.String)
                {
                    return content.GetString() ?? string.Empty;
                }
            }
        }

        return Truncate(body, 1000);
    }

    private static string ExtractResponsesContent(string body)
    {
        using (var document = JsonDocument.Parse(body))
        {
            var root = document.RootElement;
            if (root.TryGetProperty("output_text", out var outputText) &&
                outputText.ValueKind == JsonValueKind.String)
            {
                return outputText.GetString() ?? string.Empty;
            }

            if (root.TryGetProperty("output", out var output) &&
                output.ValueKind == JsonValueKind.Array)
            {
                var parts = new List<string>();
                foreach (var item in output.EnumerateArray())
                {
                    if (!item.TryGetProperty("content", out var content) ||
                        content.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var part in content.EnumerateArray())
                    {
                        if (part.TryGetProperty("text", out var text) &&
                            text.ValueKind == JsonValueKind.String)
                        {
                            var value = text.GetString();
                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                parts.Add(value!);
                            }
                        }
                    }
                }

                if (parts.Count > 0)
                {
                    return string.Join("\n", parts);
                }
            }
        }

        return Truncate(body, 1000);
    }

    private static Uri BuildEndpoint(BridgeAiProviderConfig provider)
    {
        var trimmed = provider.BaseUrl.Trim().TrimEnd('/');
        var resource = IsResponses(provider) ? "/responses" : "/chat/completions";
        return new Uri(
            trimmed.EndsWith(resource, StringComparison.OrdinalIgnoreCase)
                ? trimmed
                : trimmed + resource);
    }

    private static bool IsResponses(BridgeAiProviderConfig provider)
    {
        return string.Equals(provider.ApiMode, "Responses", StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateProvider(BridgeAiProviderConfig provider)
    {
        if (string.IsNullOrWhiteSpace(provider.BaseUrl) ||
            !Uri.TryCreate(provider.BaseUrl, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("模型 Base URL 必须是有效的 HTTPS 地址。");
        }

        if (string.IsNullOrWhiteSpace(provider.Model))
        {
            throw new InvalidOperationException("模型名称不能为空。");
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value ?? string.Empty;
        }

        return value.Substring(0, maxLength) + "\n...已压缩";
    }

    private sealed class RequestMessage
    {
        public RequestMessage(string role, string content)
        {
            Role = role;
            Content = content;
        }

        public string Role { get; }
        public string Content { get; }
    }
}

public static class BridgeAiLog
{
    private static readonly object Sync = new object();

    public static void Write(string hostKey, string message)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "YingTanAiBridge",
                "logs");
            Directory.CreateDirectory(directory);
            var safeHost = string.IsNullOrWhiteSpace(hostKey) ? "host" : hostKey;
            var path = Path.Combine(directory, safeHost + ".log");
            lock (Sync)
            {
                File.AppendAllText(
                    path,
                    "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + message + Environment.NewLine,
                    new UTF8Encoding(false));
            }
        }
        catch
        {
            // Logging must never interrupt the host application.
        }
    }
}
