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
    private CancellationTokenSource? _taskCancellation;

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
        _runtime.OperationRecorded += RuntimeOnOperationRecorded;
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
        if (_busy) return;
        var conversation = CreateConversation();
        _conversations.Insert(0, conversation);
        ConversationList.SelectedItem = conversation;
        SaveHistory();
        FocusComposer();
    }

    private void DeleteConversationOnClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
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
        if (_busy) return;
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
        conversation.PendingPlanJson = null;
        _taskCancellation = new CancellationTokenSource();
        var cancellationToken = _taskCancellation.Token;
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
                    _runtime.RevitVersion,
                    cancellationToken);
                var warningText = string.Join("\n", artifact.Warnings.Select(item => $"- {item}"));
                conversation.Messages.Add(new ChatMessage
                {
                    Role = "assistant",
                    Content = $"{artifact.Reply}\n\n脚本工作室已保存工件（未执行）：\n格式：{artifact.Format}\n路径：{artifact.FilePath}\nSHA-256：{artifact.Sha256}\n\n检查结果：\n{warningText}"
                });
                return;
            }

            var turn = await RunAgentAsync(settings, conversation, cancellationToken);
            conversation.PendingPlanJson = turn.PendingPlan;
            conversation.Messages.Add(new ChatMessage { Role = "assistant", Content = turn.Reply });
        }
        catch (OperationCanceledException)
        {
            conversation.Messages.Add(new ChatMessage { Role = "assistant", Content = "本轮已停止。" });
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
            _taskCancellation?.Dispose();
            _taskCancellation = null;
        }
    }

    private async void ExecutePlanOnClick(object sender, RoutedEventArgs e)
    {
        var conversation = SelectedConversation;
        if (_busy || conversation is null || string.IsNullOrWhiteSpace(conversation.PendingPlanJson))
        {
            return;
        }

        await ExecutePlanAsync(conversation);
    }

    private async Task ExecutePlanAsync(ChatConversation conversation)
    {
        var ownsCancellation = _taskCancellation is null;

        SetBusy(true, "正在向 Revit 提交写入计划...");
        _taskCancellation ??= new CancellationTokenSource();
        var submitted = false;
        var plan = conversation.PendingPlanJson ?? throw new InvalidOperationException("没有待执行计划。");
        conversation.PendingPlanJson = null;
        SaveHistory();
        try
        {
            var settings = AiSettingsStore.Load();
            var payload = BridgePayloadBuilder.Build(
                plan,
                allowWrites: true,
                confirmWrites: settings.Agent.ConfirmBeforeWrite);
            var result = await _runtime.EnqueueAsync(payload, TimeSpan.FromMinutes(3), _taskCancellation.Token);
            var error = AgentPlanPolicy.Failure(result);
            if (error is not null) throw new InvalidOperationException(error);
            var state = JsonSerializer.SerializeToElement(result);
            if (!state.TryGetProperty("committed", out var committed) || committed.ValueKind != JsonValueKind.True)
                throw new InvalidOperationException("Revit 没有返回批次已提交状态，未继续核验。" );
            submitted = true;

            conversation.Messages.Add(new ChatMessage
            {
                Role = "assistant",
                Content = $"Revit 已返回执行结果：\n{AgentPlanPolicy.BoundedResult(result)}"
            });
            RefreshConversation(conversation);
            using var planDocument = JsonDocument.Parse(plan);
            var documentToken = planDocument.RootElement.GetProperty("expectedDocumentToken").GetString()!;
            var verificationPlan = AgentPlanPolicy.VerificationPlan(result, documentToken);
            if (verificationPlan is not null)
            {
                SetBusy(true, "正在重新读取修改后的构件…");
                var check = await _runtime.EnqueueAsync(BridgePayloadBuilder.Build(verificationPlan, false), TimeSpan.FromMinutes(2), _taskCancellation.Token);
                var checkError = AgentPlanPolicy.Failure(check);
                if (checkError is not null) throw new InvalidOperationException(checkError);
                conversation.Messages.Add(new ChatMessage { Role = "assistant", Content = "修改后的构件回读（最多40个，返回内容可能截断）：\n" + AgentPlanPolicy.BoundedResult(check) });
            }
            var verification = await RunAgentAsync(settings, conversation, _taskCancellation.Token, verificationOnly: true,
                expectedDocumentToken: documentToken);
            conversation.Messages.Add(new ChatMessage { Role = "assistant", Content = verification.Reply });
        }
        catch (OperationCanceledException)
        {
            conversation.Messages.Add(new ChatMessage { Role = "assistant", Content = submitted ? "操作已返回，后续核验已停止。" : "执行已取消。" });
        }
        catch (Exception ex)
        {
            BridgeLog.Error("Revit plan execution failed.", ex);
            conversation.Messages.Add(new ChatMessage
            {
                Role = "assistant",
                Content = submitted ? $"操作已返回，但核验未完成：{ex.Message}" : $"执行未能确认成功，请核对模型和返回状态：{ex.Message}",
                IsError = true
            });
        }
        finally
        {
            Touch(conversation);
            RefreshConversation(conversation);
            SetBusy(false);
            if (ownsCancellation)
            {
                _taskCancellation?.Dispose();
                _taskCancellation = null;
            }
        }
    }

    private void StopOnClick(object sender, RoutedEventArgs e)
    {
        _taskCancellation?.Cancel();
        ComposerHint.Text = "正在停止；已进入 Revit 的事务会完成或回滚…";
    }

    private async Task<AgentTurnResult> RunAgentAsync(AiSettings settings, ChatConversation conversation,
        CancellationToken cancellationToken, bool verificationOnly = false, string? expectedDocumentToken = null)
    {
        SetBusy(true, "正在读取当前模型和选择集…");
        var context = await _runtime.EnqueueAsync(JsonSerializer.SerializeToElement(new { command = "get_model_context", expectedDocumentToken }),
            TimeSpan.FromSeconds(45), cancellationToken);
        var contextNode = JsonSerializer.SerializeToNode(context)!.AsObject();
        var activeToken = contextNode["documentToken"]!.GetValue<string>();
        if (conversation.PlatformFactsDocumentToken != activeToken)
        {
            conversation.PlatformFacts.Clear();
            conversation.PlatformFactsDocumentToken = activeToken;
        }
        contextNode["previousPlatformQueriesInThisDocument"] = JsonSerializer.SerializeToNode(conversation.PlatformFacts);
        if (!verificationOnly)
        {
            var names = conversation.Messages.Where(m => m.IsUser).Reverse().Select(m => PlatformLayout.ParseGridNames(m.Content)).FirstOrDefault(n => n is not null);
            if (names is not null)
            {
                var documentToken = contextNode["documentToken"]!.GetValue<string>();
                SetBusy(true, "正在读取指定轴网位置、尺寸及结构类型…");
                try
                {
                    var region = await _runtime.EnqueueAsync(JsonSerializer.SerializeToElement(new
                    {
                        command = "resolve_grid_region", gridA = names[0], gridB = names[1], grid1 = names[2], grid2 = names[3], expectedDocumentToken = documentToken
                    }), TimeSpan.FromSeconds(45), cancellationToken);
                    contextNode["requestedGridRegion"] = JsonSerializer.SerializeToNode(region);
                }
                catch (InvalidOperationException ex) { contextNode["gridRegionError"] = ex.Message; }
                var types = await _runtime.EnqueueAsync(JsonSerializer.SerializeToElement(new { command = "list_structure_types", expectedDocumentToken = documentToken }),
                    TimeSpan.FromSeconds(45), cancellationToken);
                contextNode["structureTypes"] = JsonSerializer.SerializeToNode(types);
            }
        }
        return await RevitAgentLoop.RunAsync(JsonSerializer.SerializeToElement(contextNode),
            (observations, ct) => RevitAiOrchestrator.CompleteAsync(settings, conversation, ct, observations, _runtime.RevitVersion),
            (payload, ct) => _runtime.EnqueueAsync(payload, TimeSpan.FromMinutes(2), ct),
            status => SetBusy(true, status), cancellationToken, verificationOnly,
            (plan, result) =>
            {
                using var query = JsonDocument.Parse(plan);
                foreach (var op in query.RootElement.GetProperty("operations").EnumerateArray())
                {
                    var command = op.GetProperty("command").GetString()!;
                    if (command is "preview_steel_platform" or "resolve_grid_region" or "list_structure_types" or "prepare_revit_script" or "query_revit_script")
                        conversation.PlatformFacts[command] = "查询参数：" + op.GetRawText() + "\n返回数据：" + AgentPlanPolicy.BoundedResult(result);
                }
            });
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
        StopButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ConversationList.IsEnabled = !busy;
        BusyProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ComposerHint.Text = busy ? status ?? "处理中..." : "描述目标；可引用 @选择集、当前层";
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
            ? $"处理中 · Revit {_runtime.RevitVersion} · 待处理 {_runtime.PendingCount} · 点击查看明细"
            : $"Bridge 已连接 · Revit {_runtime.RevitVersion} · 成功 {_runtime.ProcessedCount} · 失败 {_runtime.FailedCount} · 点击查看明细";
    }

    private void RuntimeStatusOnClick(object sender, RoutedEventArgs e)
    {
        OperationHistoryWindow.ShowOrActivate(_runtime);
    }

    private void RuntimeOnOperationRecorded(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(RefreshRuntimeStatus, DispatcherPriority.Background);
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
