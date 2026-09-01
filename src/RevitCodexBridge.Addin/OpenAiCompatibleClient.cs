using System.Net.Http.Headers;
using System.Net.Mime;
using System.Net;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace RevitCodexBridge.Addin;

internal sealed record AiCallResult(bool Succeeded, string Message, string? ResponseText);

internal sealed record AiChatRequestMessage(string Role, string Content);

internal static class OpenAiCompatibleClient
{
    private const int MaxTransportAttempts = 3;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private static readonly HttpClient SharedClient = CreateHttpClient();

    public static async Task<AiCallResult> TestConnectionAsync(
        AiProviderProfile profile,
        CancellationToken cancellationToken = default)
    {
        var result = await CompleteAsync(
            profile,
            "请只回复 OK，用于测试 Revit AI 调用设置。",
            "你是 Revit AI 调用测试助手，只能回复 OK。",
            cancellationToken);

        return result.Succeeded
            ? result with { Message = $"连接成功：{result.ResponseText}" }
            : result;
    }

    public static async Task<AiCallResult> CompleteAsync(
        AiProviderProfile profile,
        string userMessage,
        string? systemMessage = null,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<AiChatRequestMessage>();
        if (!string.IsNullOrWhiteSpace(systemMessage))
        {
            messages.Add(new AiChatRequestMessage("system", systemMessage));
        }

        messages.Add(new AiChatRequestMessage("user", userMessage));
        return await CompleteAsync(profile, messages, cancellationToken);
    }

    public static async Task<AiCallResult> CompleteAsync(
        AiProviderProfile profile,
        IReadOnlyList<AiChatRequestMessage> messages,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ValidateProfile(profile);
            var endpoint = BuildEndpoint(profile);
            var apiKey = profile.GetApiKey()
                ?? throw new InvalidOperationException("API Key 无法读取，请重新填写并保存。");

            var payload = BuildPayload(profile, messages);
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            var stopwatch = Stopwatch.StartNew();
            BridgeLog.Info(
                $"AI request starting: provider={profile.DisplayName}, mode={profile.ApiMode}, host={endpoint.Host}, messages={messages.Count}, timeoutSeconds={Math.Clamp(profile.TimeoutSeconds, 10, 300)}.");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(profile.TimeoutSeconds, 10, 300)));
            using var response = await SendWithTransportRetryAsync(
                endpoint,
                apiKey,
                json,
                profile.DisplayName,
                timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            stopwatch.Stop();
            BridgeLog.Info(
                $"AI request completed: provider={profile.DisplayName}, status={(int)response.StatusCode}, elapsedMs={stopwatch.ElapsedMilliseconds}.");

            if (!response.IsSuccessStatusCode)
            {
                var message = $"调用失败：HTTP {(int)response.StatusCode} {response.ReasonPhrase}\n{Truncate(body, 1000)}";
                return new AiCallResult(false, message, null);
            }

            if (profile.ApiMode == AiApiMode.Responses && TryGetIncompleteReason(body, out var incompleteReason))
            {
                return new AiCallResult(
                    false,
                    incompleteReason.Equals("length", StringComparison.OrdinalIgnoreCase)
                        ? $"模型输出达到上限（{profile.MaxTokens} tokens）。请在模型接口设置中提高“最大输出”，或缩短对话历史后重试。"
                        : $"模型响应未完成：{incompleteReason}",
                    null);
            }

            var content = profile.ApiMode == AiApiMode.Responses
                ? ExtractResponsesContent(body)
                : ExtractChatCompletionsContent(body);
            return new AiCallResult(true, "调用成功。", content);
        }
        catch (Exception ex)
        {
            BridgeLog.Error("AI provider call failed.", ex);
            return new AiCallResult(false, $"调用失败：{BuildUserFacingError(ex)}", null);
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            ConnectTimeout = TimeSpan.FromSeconds(20),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            MaxConnectionsPerServer = 8,
            UseProxy = true,
            DefaultProxyCredentials = CredentialCache.DefaultCredentials
        };

        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    private static async Task<HttpResponseMessage> SendWithTransportRetryAsync(
        Uri endpoint,
        string apiKey,
        string json,
        string providerName,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxTransportAttempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Version = HttpVersion.Version11,
                    VersionPolicy = HttpVersionPolicy.RequestVersionExact,
                    Content = new StringContent(json, Encoding.UTF8, MediaTypeNames.Application.Json)
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaTypeNames.Application.Json));

                return await SharedClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
            }
            catch (HttpRequestException ex) when (IsTransientTlsEof(ex) && attempt < MaxTransportAttempts)
            {
                BridgeLog.Info(
                    $"Transient TLS handshake interruption for {providerName} at {endpoint.Host}; retry {attempt}/{MaxTransportAttempts - 1}.");
                await Task.Delay(TimeSpan.FromMilliseconds(350 * attempt), cancellationToken);
            }
        }

        throw new InvalidOperationException("AI 请求在重试后仍未发送。此错误不应发生。");
    }

    private static bool IsTransientTlsEof(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is IOException &&
                (current.Message.Contains("unexpected EOF", StringComparison.OrdinalIgnoreCase) ||
                 current.Message.Contains("0 bytes from the transport stream", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (current.InnerException is null)
            {
                break;
            }
        }

        return false;
    }

    private static string BuildUserFacingError(Exception exception)
    {
        if (IsTransientTlsEof(exception))
        {
            return "与模型服务建立 TLS 连接时被网络或代理提前断开，已自动重试仍未成功。请检查系统代理后重试。";
        }

        if (exception is OperationCanceledException)
        {
            return "请求已取消或超过配置的超时时间。";
        }

        var innermost = exception;
        while (innermost.InnerException is not null)
        {
            innermost = innermost.InnerException;
        }

        return innermost.Message;
    }

    private static void ValidateProfile(AiProviderProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.BaseUrl))
        {
            throw new InvalidOperationException("Base URL 不能为空。");
        }

        if (!Uri.TryCreate(profile.BaseUrl.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Base URL 必须是 HTTPS 地址。");
        }

        if (string.IsNullOrWhiteSpace(profile.Model))
        {
            throw new InvalidOperationException("模型名不能为空。");
        }

        if (!profile.HasApiKey)
        {
            throw new InvalidOperationException("请先填写并保存 API Key。");
        }
    }

    private static object BuildPayload(AiProviderProfile profile, IReadOnlyList<AiChatRequestMessage> messages)
    {
        if (profile.ApiMode == AiApiMode.Responses)
        {
            return new
            {
                model = profile.Model.Trim(),
                input = messages.Select(item => new
                {
                    role = item.Role,
                    content = new[]
                    {
                        new { type = "input_text", text = item.Content }
                    }
                }),
                max_output_tokens = Math.Clamp(profile.MaxTokens, 16, 32768),
                stream = false
            };
        }

        return new
        {
            model = profile.Model.Trim(),
            messages = messages.Select(item => new { role = item.Role, content = item.Content }),
            max_tokens = Math.Clamp(profile.MaxTokens, 16, 32768),
            stream = false
        };
    }

    private static Uri BuildEndpoint(AiProviderProfile profile)
    {
        var trimmed = profile.BaseUrl.Trim().TrimEnd('/');
        var resource = profile.ApiMode == AiApiMode.Responses ? "/responses" : "/chat/completions";
        return trimmed.EndsWith(resource, StringComparison.OrdinalIgnoreCase)
            ? new Uri(trimmed)
            : new Uri(trimmed + resource);
    }

    private static string ExtractChatCompletionsContent(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (!root.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array ||
                choices.GetArrayLength() == 0)
            {
                return Truncate(body, 1000);
            }

            var firstChoice = choices[0];
            if (firstChoice.TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var content) &&
                content.ValueKind == JsonValueKind.String)
            {
                return content.GetString() ?? string.Empty;
            }

            return Truncate(body, 1000);
        }
        catch
        {
            return Truncate(body, 1000);
        }
    }

    private static string ExtractResponsesContent(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.TryGetProperty("output_text", out var outputText) &&
                outputText.ValueKind == JsonValueKind.String)
            {
                return outputText.GetString() ?? string.Empty;
            }

            if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
            {
                return Truncate(body, 1000);
            }

            var textParts = new List<string>();
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var part in content.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    {
                        var value = text.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            textParts.Add(value);
                        }
                    }
                }
            }

            return textParts.Count > 0
                ? string.Join("\n", textParts)
                : Truncate(body, 1000);
        }
        catch
        {
            return Truncate(body, 1000);
        }
    }

    private static bool TryGetIncompleteReason(string body, out string reason)
    {
        reason = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (!root.TryGetProperty("status", out var status) ||
                !string.Equals(status.GetString(), "incomplete", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (root.TryGetProperty("incomplete_details", out var details) &&
                details.TryGetProperty("reason", out var reasonElement))
            {
                reason = reasonElement.GetString() ?? "unknown";
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                reason = "unknown";
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string Truncate(string text, int maxLength)
    {
        return text.Length <= maxLength
            ? text
            : text[..maxLength] + "\n...已截断";
    }
}
