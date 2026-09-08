using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Autodesk.Revit.UI;
using WinForms = System.Windows.Forms;

namespace RevitCodexBridge.Addin;

public sealed partial class ChatDockPane : System.Windows.Controls.UserControl, IDockablePaneProvider
{
    private static readonly Guid PaneGuid = new("A4E5D537-664C-4AC7-A4DC-9388B5B52C1E");
    private readonly BridgeRuntime _runtime;
    private readonly ObservableCollection<ChatConversation> _conversations;
    private bool _busy;

    internal ChatDockPane(BridgeRuntime runtime)
    {
        InitializeComponent();
        _runtime = runtime;
        _conversations = ChatHistoryStore.Load();
        ConversationList.ItemsSource = _conversations;

        if (_conversations.Count == 0)
        {
            _conversations.Add(CreateConversation());
        }

        ConversationList.SelectedIndex = 0;
        RefreshProviderStatus();
        RefreshRuntimeStatus();
    }

    public static DockablePaneId PaneId { get; } = new(PaneGuid);

    public static ChatDockPane? Current { get; set; }

    private ChatConversation? SelectedConversation => ConversationList.SelectedItem as ChatConversation;

    public void SetupDockablePane(DockablePaneProviderData data)
    {
        data.FrameworkElement = this;
        data.InitialState = new DockablePaneState
        {
            DockPosition = DockPosition.Right
        };
    }

    public void FocusComposer()
    {
        Dispatcher.BeginInvoke(
            () => PromptBox.Focus(),
            DispatcherPriority.Input);
    }

    private void NewConversationOnClick(object sender, RoutedEventArgs e)
    {
        var conversation = CreateConversation();
        _conversations.Insert(0, conversation);
        ConversationList.SelectedItem = conversation;
        SaveHistory();
        FocusComposer();
    }

    private void DeleteConversationOnClick(object sender, RoutedEventArgs e)
    {
        var conversation = SelectedConversation;
        if (conversation is null)
        {
            return;
        }

        var result = System.Windows.MessageBox.Show(
            $"确定删除对话“{conversation.Title}”及其全部消息吗？",
            "Revit AI 助手",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _conversations.Remove(conversation);
        if (_conversations.Count == 0)
        {
            _conversations.Add(CreateConversation());
        }

        ConversationList.SelectedIndex = 0;
        SaveHistory();
    }

    private void ConversationListOnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var conversation = SelectedConversation;
        MessageItems.ItemsSource = conversation?.Messages;
        ConversationTitle.Text = conversation?.Title ?? "新对话";
        PendingPlanBar.Visibility = string.IsNullOrWhiteSpace(conversation?.PendingPlanJson)
            ? Visibility.Collapsed
            : Visibility.Visible;
        RefreshEmptyState();
        ScrollToLatest();
    }

    private void SettingsOnClick(object sender, RoutedEventArgs e)
    {
        using var form = new AiSettingsForm();
        var ownerHandle = Process.GetCurrentProcess().MainWindowHandle;
        var result = ownerHandle == IntPtr.Zero
            ? form.ShowDialog()
            : form.ShowDialog(new Win32Window(ownerHandle));

        if (result == WinForms.DialogResult.OK)
        {
            RefreshProviderStatus();
            AddAssistantMessage("配置已更新，后续消息将使用新的 API、Agent 和 Skill 设置。");
        }
    }

    private async void SendOnClick(object sender, RoutedEventArgs e)
    {
        await SendAsync();
    }

    private async void PromptBoxOnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            await SendAsync();
        }
    }

    private async Task SendAsync()
    {
        var conversation = SelectedConversation;
        var text = PromptBox.Text.Trim();
        if (_busy || conversation is null || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        PromptBox.Clear();
        conversation.Messages.Add(new ChatMessage { Role = "user", Content = text });
        if (conversation.Title == "新对话")
        {
            conversation.Title = CreateTitle(text);
        }

        Touch(conversation);
        RefreshConversation(conversation);
        SetBusy(true, "AI 正在理解并生成 Revit 计划...");

        try
        {
            var settings = AiSettingsStore.Load();
            var profile = settings.GetActiveProfile();
            if (!profile.HasApiKey)
            {
                throw new InvalidOperationException("尚未配置 API Key。请点击右上角“配置”，填写模型接口后再发送消息。");
            }

            if (AutomationArtifactService.ShouldGenerate(text))
            {
                SetBusy(true, "脚本工作室正在生成并检查工件...");
                var artifact = await AutomationArtifactService.GenerateAndSaveAsync(
                    settings,
                    text,
                    _runtime.RevitVersion);
                var warningText = string.Join("\n", artifact.Warnings.Select(item => $"- {item}"));
                conversation.Messages.Add(new ChatMessage
                {
                    Role = "assistant",
                    Content = $"{artifact.Reply}\n\n脚本工作室已保存工件（未执行）：\n格式：{artifact.Format}\n路径：{artifact.FilePath}\nSHA-256：{artifact.Sha256}\n\n检查结果：\n{warningText}"
                });
                return;
            }

            RevitAiResponse response;
            if (RevitAutomationFallback.ShouldUseNativeScript(conversation))
            {
                response = await RevitAutomationFallback.EnsureExecutableScriptAsync(
                    settings,
                    conversation,
                    new RevitAiResponse(string.Empty, null));
            }
            else
            {
                response = await RevitAiOrchestrator.CompleteAsync(settings, conversation);
                response = await RevitAutomationFallback.EnsureExecutableScriptAsync(settings, conversation, response);
            }
            var assistantText = response.Reply;

            if (!string.IsNullOrWhiteSpace(response.PlanJson))
            {
                assistantText += "\n\nAI 自动化脚本已保存到 %LOCALAPPDATA%\\RevitCodexBridge\\automation-scripts。";
            }

            if (!string.IsNullOrWhiteSpace(response.PlanJson))
            {
                SetBusy(true, "正在 Revit 中安全预演计划...");
                var payload = BridgePayloadBuilder.Build(response.PlanJson, allowWrites: false);
                var preview = await _runtime.EnqueueAsync(payload, TimeSpan.FromMinutes(2));

                if (BridgePayloadBuilder.HasMutation(response.PlanJson))
                {
                    conversation.PendingPlanJson = response.PlanJson;
                    assistantText += "\n\n计划已通过安全预演。请检查说明后点击“执行计划”，写入前会按 Agent 设置进行确认。";
                }
                else
                {
                    SetBusy(true, "正在整理并解释 Revit 查询结果...");
                    assistantText = await RevitAiOrchestrator.SummarizeExecutionResultAsync(
                        settings,
                        conversation,
                        response.Reply,
                        preview);
                }
            }

            conversation.Messages.Add(new ChatMessage { Role = "assistant", Content = assistantText });
        }
        catch (Exception ex)
        {
            BridgeLog.Error("Chat request failed.", ex);
            conversation.Messages.Add(new ChatMessage
            {
                Role = "assistant",
                Content = ex.Message,
                IsError = true
            });
        }
        finally
        {
            Touch(conversation);
            RefreshConversation(conversation);
            SetBusy(false);
        }
    }

    private async void ExecutePlanOnClick(object sender, RoutedEventArgs e)
    {
        var conversation = SelectedConversation;
        if (_busy || conversation is null || string.IsNullOrWhiteSpace(conversation.PendingPlanJson))
        {
            return;
        }

        SetBusy(true, "正在向 Revit 提交写入计划...");
        try
        {
            var settings = AiSettingsStore.Load();
            var payload = BridgePayloadBuilder.Build(
                conversation.PendingPlanJson,
                allowWrites: true,
                confirmWrites: settings.Agent.ConfirmBeforeWrite);
            var result = await _runtime.EnqueueAsync(payload, TimeSpan.FromMinutes(3));

            conversation.Messages.Add(new ChatMessage
            {
                Role = "assistant",
                Content = $"计划执行完成。\n\nRevit 返回：\n{RevitAiOrchestrator.FormatExecutionResult(result)}"
            });
            conversation.PendingPlanJson = null;
        }
        catch (Exception ex)
        {
            BridgeLog.Error("Revit plan execution failed.", ex);
            conversation.Messages.Add(new ChatMessage
            {
                Role = "assistant",
                Content = $"计划未写入模型：{ex.Message}",
                IsError = true
            });
        }
        finally
        {
            Touch(conversation);
            RefreshConversation(conversation);
            SetBusy(false);
        }
    }

    private void AddAssistantMessage(string content)
    {
        var conversation = SelectedConversation;
        if (conversation is null)
        {
            return;
        }

        conversation.Messages.Add(new ChatMessage { Role = "assistant", Content = content });
        Touch(conversation);
        RefreshConversation(conversation);
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _busy = busy;
        SendButton.IsEnabled = !busy;
        ExecutePlanButton.IsEnabled = !busy;
        ConversationList.IsEnabled = !busy;
        BusyProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ComposerHint.Text = busy ? status ?? "处理中..." : "AI 生成的写操作会先预演";
        RefreshRuntimeStatus();
    }

    private void RefreshConversation(ChatConversation conversation)
    {
        MessageItems.ItemsSource = conversation.Messages;
        ConversationTitle.Text = conversation.Title;
        PendingPlanBar.Visibility = string.IsNullOrWhiteSpace(conversation.PendingPlanJson)
            ? Visibility.Collapsed
            : Visibility.Visible;
        ConversationList.Items.Refresh();
        RefreshEmptyState();
        ScrollToLatest();
        SaveHistory();
    }

    private void RefreshProviderStatus()
    {
        var settings = AiSettingsStore.Load();
        var profile = settings.GetActiveProfile();
        var keyText = profile.HasApiKey ? "已配置" : "未配置 Key";
        ProviderStatus.Text = $"{settings.Agent.Name} · {profile.DisplayName} / {profile.Model} · {keyText}";
    }

    private void RefreshRuntimeStatus()
    {
        RuntimeStatus.Text = _busy
            ? $"处理中 · Revit {_runtime.RevitVersion} · Pending {_runtime.PendingCount}"
            : $"Bridge 已连接 · Revit {_runtime.RevitVersion} · 已处理 {_runtime.ProcessedCount} · 失败 {_runtime.FailedCount}";
    }

    private void RefreshEmptyState()
    {
        EmptyState.Visibility = SelectedConversation?.Messages.Count > 0
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void ScrollToLatest()
    {
        Dispatcher.BeginInvoke(
            () => MessagesScroll.ScrollToEnd(),
            DispatcherPriority.Background);
    }

    private void SaveHistory()
    {
        ChatHistoryStore.Save(_conversations);
    }

    private static void Touch(ChatConversation conversation)
    {
        conversation.UpdatedAt = DateTimeOffset.Now;
    }

    private static ChatConversation CreateConversation()
    {
        return new ChatConversation();
    }

    private static string CreateTitle(string text)
    {
        var firstLine = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return firstLine.Length <= 18 ? firstLine : firstLine[..18] + "...";
    }

    private sealed class Win32Window : WinForms.IWin32Window
    {
        public Win32Window(IntPtr handle)
        {
            Handle = handle;
        }

        public IntPtr Handle { get; }
    }
}
