using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RevitCodexBridge.Addin;

internal sealed class ChatConversation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Title { get; set; } = "新对话";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    public ObservableCollection<ChatMessage> Messages { get; set; } = [];

    public string? PendingPlanJson { get; set; }

    [JsonIgnore]
    public string UpdatedText => UpdatedAt.LocalDateTime.ToString("MM-dd HH:mm");
}

internal sealed class ChatMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Role { get; set; } = "assistant";

    public string Content { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public bool IsError { get; set; }

    [JsonIgnore]
    public bool IsUser => Role.Equals("user", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public string RoleLabel => IsUser ? "你" : "AI 助手";

    [JsonIgnore]
    public string TimeText => CreatedAt.LocalDateTime.ToString("HH:mm");
}

internal static class ChatHistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string HistoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RevitCodexBridge",
        "chat-history.json");

    public static ObservableCollection<ChatConversation> Load()
    {
        try
        {
            if (!File.Exists(HistoryPath))
            {
                return [];
            }

            var json = File.ReadAllText(HistoryPath, Encoding.UTF8);
            var conversations = JsonSerializer.Deserialize<List<ChatConversation>>(json, JsonOptions) ?? [];
            // Plans refer to a particular open document session and must be regenerated after restart.
            foreach (var conversation in conversations) conversation.PendingPlanJson = null;
            return new ObservableCollection<ChatConversation>(conversations.OrderByDescending(item => item.UpdatedAt));
        }
        catch (Exception ex)
        {
            BridgeLog.Error("Failed to load chat history.", ex);
            return [];
        }
    }

    public static void Save(IEnumerable<ChatConversation> conversations)
    {
        try
        {
            var directory = Path.GetDirectoryName(HistoryPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var ordered = conversations.OrderByDescending(item => item.UpdatedAt).ToList();
            File.WriteAllText(HistoryPath, JsonSerializer.Serialize(ordered, JsonOptions), new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            BridgeLog.Error("Failed to save chat history.", ex);
        }
    }
}
