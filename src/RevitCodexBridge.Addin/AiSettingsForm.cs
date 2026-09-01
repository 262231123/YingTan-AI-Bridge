using System.Drawing;
using System.Windows.Forms;

namespace RevitCodexBridge.Addin;

internal sealed class AiSettingsForm : Form
{
    private readonly ComboBox _providerComboBox = new();
    private readonly TextBox _baseUrlTextBox = new();
    private readonly TextBox _modelTextBox = new();
    private readonly TextBox _apiKeyTextBox = new();
    private readonly NumericUpDown _timeoutSecondsInput = new();
    private readonly NumericUpDown _maxTokensInput = new();
    private readonly Label _keyStatusLabel = UiTheme.CreateMutedLabel();
    private readonly Label _apiModeLabel = UiTheme.CreateMutedLabel();
    private readonly Button _testButton = new();
    private readonly Button _saveButton = new();
    private readonly Button _clearKeyButton = new();
    private readonly CheckBox _showKeyCheckBox = new();

    private readonly TextBox _agentNameTextBox = new();
    private readonly TextBox _agentPromptTextBox = new();
    private readonly CheckBox _confirmWritesCheckBox = new();
    private readonly CheckedListBox _specialistAgentsList = new();

    private readonly ListBox _skillsListBox = new();
    private readonly TextBox _skillNameTextBox = new();
    private readonly TextBox _skillCategoryTextBox = new();
    private readonly TextBox _skillTriggersTextBox = new();
    private readonly TextBox _skillDescriptionTextBox = new();
    private readonly TextBox _skillInstructionsTextBox = new();
    private readonly CheckBox _skillEnabledCheckBox = new();
    private readonly Label _skillMetadataLabel = UiTheme.CreateMutedLabel();

    private readonly AiSettings _settings;
    private AiProviderKind _currentProvider;
    private AgentSkill? _currentSkill;
    private bool _loadingProvider;
    private bool _loadingSkill;

    public AiSettingsForm()
    {
        _settings = AiSettingsStore.Load();
        _currentProvider = _settings.ActiveProvider;

        Text = "Revit AI 配置";
        Width = 860;
        Height = 650;
        MinimumSize = new Size(780, 580);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MaximizeBox = false;
        UiTheme.ApplyForm(this);

        BuildLayout();
        PopulateProviders();
        LoadProvider(_settings.ActiveProvider);
        LoadAgent();
        PopulateSpecialistAgents();
        PopulateSkills();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));

        root.Controls.Add(CreateHeader(), 0, 0);
        root.Controls.Add(CreateTabs(), 0, 1);
        root.Controls.Add(CreateButtonBar(), 0, 2);
        Controls.Add(root);
    }

    private Control CreateHeader()
    {
        var panel = UiTheme.CreateSurfacePanel(new Padding(16, 10, 16, 10));
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(UiTheme.CreateTitle(this, "Revit AI 配置", 15F), 0, 0);
        layout.Controls.Add(
            UiTheme.CreateMutedLabel("统一配置模型接口、Agent 行为和可调用 Skills；API Key 使用当前 Windows 用户凭据加密保存。"),
            0,
            1);
        panel.Controls.Add(layout);
        return panel;
    }

    private Control CreateTabs()
    {
        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 9.5F, FontStyle.Regular),
            Padding = new Point(14, 5)
        };

        tabs.TabPages.Add(CreateTabPage("模型接口", CreateProviderPage()));
        tabs.TabPages.Add(CreateTabPage("Agent", CreateAgentPage()));
        tabs.TabPages.Add(CreateTabPage("Skills", CreateSkillsPage()));
        return tabs;
    }

    private static TabPage CreateTabPage(string title, Control content)
    {
        var page = new TabPage(title)
        {
            BackColor = UiTheme.WindowBackground,
            Padding = new Padding(10)
        };
        page.Controls.Add(content);
        return page;
    }

    private Control CreateProviderPage()
    {
        var panel = UiTheme.CreateSurfacePanel(new Padding(18, 16, 18, 12));
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 9 };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
        for (var i = 0; i < fields.RowCount; i++)
        {
            fields.RowStyles.Add(new RowStyle(i == 8 ? SizeType.Percent : SizeType.Absolute, i == 8 ? 100 : 42));
        }

        _providerComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _providerComboBox.Dock = DockStyle.Fill;
        _providerComboBox.SelectedIndexChanged += ProviderComboBoxOnSelectedIndexChanged;
        UiTheme.StyleComboBox(_providerComboBox);

        ConfigureTextBox(_baseUrlTextBox);
        ConfigureTextBox(_modelTextBox);
        ConfigureTextBox(_apiKeyTextBox);
        _apiKeyTextBox.UseSystemPasswordChar = true;

        _timeoutSecondsInput.Minimum = 10;
        _timeoutSecondsInput.Maximum = 300;
        _timeoutSecondsInput.Dock = DockStyle.Left;
        _timeoutSecondsInput.Width = 120;

        _maxTokensInput.Minimum = 16;
        _maxTokensInput.Maximum = 32768;
        _maxTokensInput.Increment = 16;
        _maxTokensInput.Dock = DockStyle.Left;
        _maxTokensInput.Width = 120;

        _clearKeyButton.Text = "清除密钥";
        _clearKeyButton.Dock = DockStyle.Fill;
        _clearKeyButton.FlatStyle = FlatStyle.Flat;
        UiTheme.ApplyButtonKind(_clearKeyButton, UiButtonKind.Quiet);
        _clearKeyButton.Click += (_, _) => ClearCurrentApiKey();

        var defaultsButton = UiTheme.CreateButton("恢复默认", UiButtonKind.Quiet, (_, _) => RestoreProviderDefaults());
        defaultsButton.Dock = DockStyle.Fill;

        _showKeyCheckBox.Text = "显示输入内容";
        _showKeyCheckBox.Dock = DockStyle.Fill;
        _showKeyCheckBox.ForeColor = UiTheme.MutedText;
        _showKeyCheckBox.CheckedChanged += (_, _) => _apiKeyTextBox.UseSystemPasswordChar = !_showKeyCheckBox.Checked;

        AddRow(fields, 0, "供应商", _providerComboBox, _apiModeLabel);
        AddRow(fields, 1, "Base URL", _baseUrlTextBox, defaultsButton);
        AddRow(fields, 2, "模型名", _modelTextBox, null);
        AddRow(fields, 3, "API Key", _apiKeyTextBox, _clearKeyButton);
        AddRow(fields, 4, string.Empty, _showKeyCheckBox, null);
        AddRow(fields, 5, "密钥状态", _keyStatusLabel, null);
        AddRow(fields, 6, "超时秒数", _timeoutSecondsInput, null);
        AddRow(fields, 7, "最大输出", _maxTokensInput, null);

        var path = UiTheme.CreateMutedLabel($"保存位置：{AiSettingsStore.SettingsPath}");
        path.AutoEllipsis = true;
        fields.Controls.Add(path, 0, 8);
        fields.SetColumnSpan(path, 3);
        panel.Controls.Add(fields);
        return panel;
    }

    private Control CreateAgentPage()
    {
        var panel = UiTheme.CreateSurfacePanel(new Padding(18, 16, 18, 12));
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6 };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));

        ConfigureTextBox(_agentNameTextBox);
        _agentPromptTextBox.Dock = DockStyle.Fill;
        _agentPromptTextBox.Multiline = true;
        _agentPromptTextBox.ScrollBars = ScrollBars.Vertical;
        _agentPromptTextBox.AcceptsReturn = true;
        UiTheme.StyleTextBox(_agentPromptTextBox);

        _confirmWritesCheckBox.Text = "执行写操作前在 Revit 中再次确认";
        _confirmWritesCheckBox.Dock = DockStyle.Fill;
        _confirmWritesCheckBox.ForeColor = UiTheme.Text;

        _specialistAgentsList.Dock = DockStyle.Fill;
        _specialistAgentsList.BorderStyle = BorderStyle.FixedSingle;
        _specialistAgentsList.CheckOnClick = true;
        _specialistAgentsList.DisplayMember = nameof(SpecialistAgent.Name);

        AddRow(fields, 0, "Agent 名称", _agentNameTextBox, null);
        fields.Controls.Add(UiTheme.CreateFieldLabel("系统指令"), 0, 1);
        fields.Controls.Add(UiTheme.CreateMutedLabel("定义角色、工作边界、专业偏好和回复方式。"), 1, 1);
        _agentPromptTextBox.Margin = new Padding(0, 0, 0, 8);
        fields.Controls.Add(_agentPromptTextBox, 1, 2);
        fields.Controls.Add(_confirmWritesCheckBox, 1, 3);
        fields.Controls.Add(UiTheme.CreateFieldLabel("专业 Agents"), 0, 4);
        fields.Controls.Add(_specialistAgentsList, 1, 5);
        panel.Controls.Add(fields);
        return panel;
    }

    private Control CreateSkillsPage()
    {
        var panel = UiTheme.CreateSurfacePanel(new Padding(0));
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
            SplitterDistance = 230,
            SplitterWidth = 1,
            BackColor = UiTheme.Border
        };

        var listLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(12)
        };
        listLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        listLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        listLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        listLayout.Controls.Add(UiTheme.CreateFieldLabel("可用 Skills"), 0, 0);

        _skillsListBox.Dock = DockStyle.Fill;
        _skillsListBox.BorderStyle = BorderStyle.FixedSingle;
        _skillsListBox.DisplayMember = nameof(AgentSkill.Name);
        _skillsListBox.SelectedIndexChanged += SkillsListOnSelectedIndexChanged;
        listLayout.Controls.Add(_skillsListBox, 0, 1);

        var skillButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        skillButtons.Controls.Add(UiTheme.CreateButton("新建", UiButtonKind.Secondary, (_, _) => NewSkill()));
        skillButtons.Controls.Add(UiTheme.CreateButton("删除", UiButtonKind.Quiet, (_, _) => DeleteSkill()));
        listLayout.Controls.Add(skillButtons, 0, 2);
        split.Panel1.BackColor = UiTheme.Surface;
        split.Panel1.Controls.Add(listLayout);

        var detail = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 9,
            Padding = new Padding(18, 14, 18, 12),
            BackColor = UiTheme.Surface
        };
        detail.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        detail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        detail.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        detail.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        detail.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        detail.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        detail.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        detail.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        detail.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        detail.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        detail.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

        ConfigureTextBox(_skillNameTextBox);
        ConfigureTextBox(_skillCategoryTextBox);
        ConfigureTextBox(_skillTriggersTextBox);
        _skillDescriptionTextBox.Dock = DockStyle.Fill;
        _skillDescriptionTextBox.Multiline = true;
        UiTheme.StyleTextBox(_skillDescriptionTextBox);
        _skillInstructionsTextBox.Dock = DockStyle.Fill;
        _skillInstructionsTextBox.Multiline = true;
        _skillInstructionsTextBox.ScrollBars = ScrollBars.Vertical;
        _skillInstructionsTextBox.AcceptsReturn = true;
        UiTheme.StyleTextBox(_skillInstructionsTextBox);

        _skillEnabledCheckBox.Text = "启用此 Skill";
        _skillEnabledCheckBox.Dock = DockStyle.Fill;
        _skillEnabledCheckBox.ForeColor = UiTheme.Text;

        AddRow(detail, 0, "名称", _skillNameTextBox, null);
        detail.Controls.Add(_skillEnabledCheckBox, 1, 1);
        AddRow(detail, 2, "分类", _skillCategoryTextBox, null);
        AddRow(detail, 3, "触发词", _skillTriggersTextBox, null);
        detail.Controls.Add(UiTheme.CreateFieldLabel("说明"), 0, 4);
        detail.Controls.Add(_skillDescriptionTextBox, 1, 5);
        detail.Controls.Add(UiTheme.CreateFieldLabel("Skill 指令"), 0, 6);
        detail.Controls.Add(_skillInstructionsTextBox, 1, 7);
        _skillMetadataLabel.AutoEllipsis = true;
        detail.Controls.Add(_skillMetadataLabel, 1, 8);
        split.Panel2.Controls.Add(detail);

        panel.Controls.Add(split);
        return panel;
    }

    private Control CreateButtonBar()
    {
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };

        var cancelButton = UiTheme.CreateButton("取消", UiButtonKind.Quiet, (_, _) => Close());
        cancelButton.DialogResult = DialogResult.Cancel;

        ConfigureActionButton(_saveButton, "保存", 88, UiButtonKind.Primary);
        _saveButton.Click += (_, _) => SaveSettings();
        ConfigureActionButton(_testButton, "测试连接", 104, UiButtonKind.Secondary);
        _testButton.Click += TestButtonOnClick;

        buttons.Controls.Add(_saveButton);
        buttons.Controls.Add(_testButton);
        buttons.Controls.Add(cancelButton);
        AcceptButton = _saveButton;
        CancelButton = cancelButton;
        return buttons;
    }

    private static void ConfigureTextBox(TextBox textBox)
    {
        textBox.Dock = DockStyle.Fill;
        UiTheme.StyleTextBox(textBox);
    }

    private static void ConfigureActionButton(Button button, string text, int width, UiButtonKind kind)
    {
        button.Text = text;
        button.Width = width;
        button.Height = 34;
        button.Margin = new Padding(8, 8, 0, 8);
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        UiTheme.ApplyButtonKind(button, kind);
    }

    private static void AddRow(TableLayoutPanel fields, int row, string labelText, Control input, Control? action)
    {
        var label = UiTheme.CreateFieldLabel(labelText);
        label.Margin = new Padding(0, 0, 10, 0);
        input.Margin = new Padding(0, 5, 0, 5);
        fields.Controls.Add(label, 0, row);
        fields.Controls.Add(input, 1, row);
        if (action is not null)
        {
            action.Margin = new Padding(8, 5, 0, 5);
            fields.Controls.Add(action, 2, row);
        }
    }

    private void PopulateProviders()
    {
        _providerComboBox.Items.Clear();
        foreach (var profile in _settings.Providers.OrderBy(item => item.Provider))
        {
            _providerComboBox.Items.Add(new ProviderComboItem(profile.Provider, profile.DisplayName));
        }
    }

    private void LoadProvider(AiProviderKind provider)
    {
        _loadingProvider = true;
        try
        {
            _currentProvider = provider;
            var profile = _settings.GetProfile(provider);
            for (var i = 0; i < _providerComboBox.Items.Count; i++)
            {
                if (_providerComboBox.Items[i] is ProviderComboItem item && item.Provider == provider)
                {
                    _providerComboBox.SelectedIndex = i;
                    break;
                }
            }

            _baseUrlTextBox.Text = profile.BaseUrl;
            _modelTextBox.Text = profile.Model;
            _apiKeyTextBox.Clear();
            _showKeyCheckBox.Checked = false;
            _timeoutSecondsInput.Value = Math.Clamp(profile.TimeoutSeconds, 10, 300);
            _maxTokensInput.Value = Math.Clamp(profile.MaxTokens, 16, 32768);
            _apiModeLabel.Text = profile.ApiMode == AiApiMode.Responses ? "Responses API" : "Chat API";
            UpdateKeyStatus(profile);
        }
        finally
        {
            _loadingProvider = false;
        }
    }

    private void ProviderComboBoxOnSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_loadingProvider || _providerComboBox.SelectedItem is not ProviderComboItem item)
        {
            return;
        }

        CaptureCurrentProvider();
        LoadProvider(item.Provider);
    }

    private void CaptureCurrentProvider()
    {
        var profile = _settings.GetProfile(_currentProvider);
        profile.BaseUrl = _baseUrlTextBox.Text.Trim();
        profile.Model = _modelTextBox.Text.Trim();
        profile.TimeoutSeconds = (int)_timeoutSecondsInput.Value;
        profile.MaxTokens = (int)_maxTokensInput.Value;
        if (!string.IsNullOrWhiteSpace(_apiKeyTextBox.Text))
        {
            profile.SetApiKey(_apiKeyTextBox.Text);
            _apiKeyTextBox.Clear();
        }

        UpdateKeyStatus(profile);
    }

    private bool ValidateAndCaptureProvider()
    {
        var baseUrl = _baseUrlTextBox.Text.Trim();
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            ShowValidationError("Base URL 必须是完整的 HTTPS 地址。", _baseUrlTextBox);
            return false;
        }

        if (string.IsNullOrWhiteSpace(_modelTextBox.Text))
        {
            ShowValidationError("模型名不能为空。", _modelTextBox);
            return false;
        }

        CaptureCurrentProvider();
        return true;
    }

    private void LoadAgent()
    {
        _agentNameTextBox.Text = _settings.Agent.Name;
        _agentPromptTextBox.Text = _settings.Agent.SystemPrompt;
        _confirmWritesCheckBox.Checked = _settings.Agent.ConfirmBeforeWrite;
    }

    private void PopulateSpecialistAgents()
    {
        _specialistAgentsList.Items.Clear();
        foreach (var agent in _settings.SpecialistAgents)
        {
            _specialistAgentsList.Items.Add(agent, agent.Enabled);
        }
    }

    private bool CaptureAgent()
    {
        if (string.IsNullOrWhiteSpace(_agentNameTextBox.Text))
        {
            ShowValidationError("Agent 名称不能为空。", _agentNameTextBox);
            return false;
        }

        if (string.IsNullOrWhiteSpace(_agentPromptTextBox.Text))
        {
            ShowValidationError("Agent 系统指令不能为空。", _agentPromptTextBox);
            return false;
        }

        _settings.Agent.Name = _agentNameTextBox.Text.Trim();
        _settings.Agent.SystemPrompt = _agentPromptTextBox.Text.Trim();
        _settings.Agent.ConfirmBeforeWrite = _confirmWritesCheckBox.Checked;
        for (var index = 0; index < _specialistAgentsList.Items.Count; index++)
        {
            if (_specialistAgentsList.Items[index] is SpecialistAgent agent)
            {
                agent.Enabled = _specialistAgentsList.GetItemChecked(index);
            }
        }
        return true;
    }

    private void PopulateSkills(AgentSkill? selected = null)
    {
        _loadingSkill = true;
        try
        {
            _skillsListBox.Items.Clear();
            foreach (var skill in _settings.Skills)
            {
                _skillsListBox.Items.Add(skill);
            }

            _skillsListBox.SelectedItem = selected ?? _settings.Skills.FirstOrDefault();
        }
        finally
        {
            _loadingSkill = false;
        }

        LoadSkill(_skillsListBox.SelectedItem as AgentSkill);
    }

    private void SkillsListOnSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_loadingSkill)
        {
            return;
        }

        CaptureCurrentSkill();
        LoadSkill(_skillsListBox.SelectedItem as AgentSkill);
    }

    private void LoadSkill(AgentSkill? skill)
    {
        _loadingSkill = true;
        try
        {
            _currentSkill = skill;
            var enabled = skill is not null;
            _skillNameTextBox.Enabled = enabled;
            _skillCategoryTextBox.Enabled = enabled;
            _skillTriggersTextBox.Enabled = enabled;
            _skillDescriptionTextBox.Enabled = enabled;
            _skillInstructionsTextBox.Enabled = enabled;
            _skillEnabledCheckBox.Enabled = enabled;
            _skillNameTextBox.Text = skill?.Name ?? string.Empty;
            _skillCategoryTextBox.Text = skill?.Category ?? string.Empty;
            _skillTriggersTextBox.Text = skill is null ? string.Empty : string.Join("，", skill.TriggerKeywords);
            _skillDescriptionTextBox.Text = skill?.Description ?? string.Empty;
            _skillInstructionsTextBox.Text = skill?.Instructions ?? string.Empty;
            _skillEnabledCheckBox.Checked = skill?.Enabled ?? false;
            _skillMetadataLabel.Text = skill is null
                ? string.Empty
                : $"{skill.Source} · {skill.TrustLevel} · v{skill.Version} · 写入始终需要确认";
        }
        finally
        {
            _loadingSkill = false;
        }
    }

    private void CaptureCurrentSkill()
    {
        if (_loadingSkill || _currentSkill is null)
        {
            return;
        }

        _currentSkill.Name = string.IsNullOrWhiteSpace(_skillNameTextBox.Text) ? "未命名 Skill" : _skillNameTextBox.Text.Trim();
        _currentSkill.Category = _skillCategoryTextBox.Text.Trim();
        _currentSkill.TriggerKeywords = SplitSkillValues(_skillTriggersTextBox.Text);
        _currentSkill.Description = _skillDescriptionTextBox.Text.Trim();
        _currentSkill.Instructions = _skillInstructionsTextBox.Text.Trim();
        _currentSkill.Enabled = _skillEnabledCheckBox.Checked;
        _currentSkill.Normalize();
    }

    private void NewSkill()
    {
        CaptureCurrentSkill();
        var skill = new AgentSkill
        {
            Category = "自定义",
            Source = "本地",
            TrustLevel = "本地",
            RequiresConfirmation = true
        };
        _settings.Skills.Add(skill);
        PopulateSkills(skill);
        _skillNameTextBox.Focus();
        _skillNameTextBox.SelectAll();
    }

    private void DeleteSkill()
    {
        var skill = _currentSkill;
        if (skill is null)
        {
            return;
        }

        if (MessageBox.Show($"确定删除 Skill“{skill.Name}”吗？", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        _settings.Skills.Remove(skill);
        _currentSkill = null;
        PopulateSkills();
    }

    private static List<string> SplitSkillValues(string? value)
    {
        return (value ?? string.Empty)
            .Split(new[] { ',', '，', ';', '；', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void SaveSettings()
    {
        try
        {
            CaptureCurrentSkill();
            if (!ValidateAndCaptureProvider() || !CaptureAgent())
            {
                return;
            }

            _settings.ActiveProvider = _currentProvider;
            AiSettingsStore.Save(_settings);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存失败：{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void TestButtonOnClick(object? sender, EventArgs e)
    {
        if (!ValidateAndCaptureProvider())
        {
            return;
        }

        var profile = _settings.GetProfile(_currentProvider);
        if (!profile.HasApiKey)
        {
            MessageBox.Show("请先填写 API Key，再测试连接。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SetBusy(true);
        try
        {
            var result = await OpenAiCompatibleClient.TestConnectionAsync(profile);
            MessageBox.Show(result.Message, Text, MessageBoxButtons.OK, result.Succeeded ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        UseWaitCursor = busy;
        _testButton.Enabled = !busy;
        _saveButton.Enabled = !busy;
        _clearKeyButton.Enabled = !busy;
    }

    private void ClearCurrentApiKey()
    {
        var profile = _settings.GetProfile(_currentProvider);
        profile.EncryptedApiKey = null;
        _apiKeyTextBox.Clear();
        UpdateKeyStatus(profile);
    }

    private void RestoreProviderDefaults()
    {
        var profile = AiProviderDefaults.CreateDefaults().First(item => item.Provider == _currentProvider);
        _baseUrlTextBox.Text = profile.BaseUrl;
        _modelTextBox.Text = profile.Model;
        _timeoutSecondsInput.Value = profile.TimeoutSeconds;
        _maxTokensInput.Value = profile.MaxTokens;
    }

    private void UpdateKeyStatus(AiProviderProfile profile)
    {
        _keyStatusLabel.Text = profile.HasApiKey
            ? "已保存密钥；输入新 Key 可替换，留空保留原密钥。"
            : "未保存密钥；可先保存其他配置，稍后填写。";
    }

    private void ShowValidationError(string message, Control target)
    {
        MessageBox.Show(message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        target.Focus();
    }

    private sealed record ProviderComboItem(AiProviderKind Provider, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }
}
