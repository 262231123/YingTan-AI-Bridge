using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.PlugIns;
using YingTanAiBridge.Contracts;

namespace YingTanAiBridge.Rhino;

[Guid("A76223CC-1C1B-4D73-A3F6-7F86D65C1B26")]
public sealed class YingTanRhinoAiBridgePlugin : PlugIn
{
    public static YingTanRhinoAiBridgePlugin? Instance { get; private set; }

    public YingTanRhinoAiBridgePlugin()
    {
        Instance = this;
    }

    protected override LoadReturnCode OnLoad(ref string errorMessage)
    {
        RhinoApp.WriteLine("盈碳 Rhino AI Bridge 已加载。输入 YingTanAiBridge 打开助手。");
        return LoadReturnCode.Success;
    }
}

public sealed class YingTanRhinoAiBridgeCommand : global::Rhino.Commands.Command
{
    private static BridgeChatForm? _form;

    public override string EnglishName => "YingTanAiBridge";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        if (_form == null || !_form.Visible)
        {
            _form = new BridgeChatForm();
            _form.Closed += (_, _) => _form = null;
            _form.Show();
        }
        else
        {
            _form.BringToFront();
        }

        return Result.Success;
    }
}

internal sealed class BridgeChatForm : Form
{
    private const string SystemPrompt =
        "你是盈碳 Rhino AI Bridge。你必须依据提供的当前 Rhino 文档上下文回答，不得虚构对象数量。" +
        "只输出一个 JSON 对象，格式为 {\"reply\":\"给用户的中文说明\",\"script\":null}。" +
        "需要修改模型时，script 使用 {\"operations\":[{\"command\":\"命令\",\"parameters\":{}}]}。" +
        "允许的写命令仅有：create_layer(name,color_argb)、set_current_layer(name)、" +
        "add_point(point:[x,y,z],layer)、add_line(start:[x,y,z],end:[x,y,z],layer)、" +
        "add_circle(center:[x,y,z],radius,layer)、delete_selected。" +
        "查询、统计、解释类请求直接利用上下文回答，script 必须为 null。" +
        "写操作要先简要说明将创建或修改什么，不要输出 Markdown 代码块，不要调用未列出的命令。";

    private readonly TextArea _history = new TextArea { ReadOnly = true, Wrap = true };
    private readonly TextArea _composer = new TextArea { Height = 76, Wrap = true };
    private readonly Label _status = new Label { Text = "就绪", TextColor = Colors.Gray };
    private readonly Button _send = new Button { Text = "发送" };
    private readonly List<HostChatMessage> _conversation = new List<HostChatMessage>();

    public BridgeChatForm()
    {
        Title = "盈碳 Rhino AI Bridge";
        MinimumSize = new Size(440, 560);
        Size = new Size(520, 700);
        Padding = 14;
        Resizable = true;

        AppendMessage(
            "系统",
            "盈碳 Rhino AI Bridge 已就绪。可查询对象、选择集、图层和类型数量，也可要求创建图层、点、线或圆。所有对话文字均可选择复制。");

        _send.Click += async (_, _) => await SubmitAsync();
        _composer.KeyDown += async (_, e) =>
        {
            if (e.Key == Keys.Enter && !e.Modifiers.HasFlag(Keys.Shift))
            {
                e.Handled = true;
                await SubmitAsync();
            }
        };

        var settings = new Button { Text = "配置" };
        settings.Click += (_, _) => ShowSettings();

        Content = new TableLayout
        {
            Spacing = new Size(8, 8),
            Rows =
            {
                new TableRow(
                    new Label { Text = "盈碳 Rhino AI Bridge", Font = SystemFonts.Bold(16) },
                    null,
                    settings),
                new TableRow(_history) { ScaleHeight = true },
                new TableRow(_composer),
                new TableRow(_status, null, _send)
            }
        };
    }

    private async Task SubmitAsync()
    {
        var prompt = (_composer.Text ?? string.Empty).Trim();
        if (prompt.Length == 0 || !_send.Enabled)
        {
            return;
        }

        _composer.Text = string.Empty;
        _conversation.Add(new HostChatMessage("User", prompt));
        AppendMessage("你", prompt);
        SetBusy(true, "正在读取 Rhino 文档并调用模型...");

        try
        {
            var context = BuildModelContext();
            var response = await HostAiClient.CompleteAsync(
                "rhino-8",
                SystemPrompt,
                context,
                _conversation);
            AppendMessage("AI", response.Reply);
            _conversation.Add(new HostChatMessage("Assistant", response.Reply));

            var scriptJson = response.ScriptJson;
            if (!string.IsNullOrWhiteSpace(scriptJson))
            {
                SaveScript(scriptJson);
                ExecuteScriptWithConfirmation(scriptJson);
            }
        }
        catch (Exception ex)
        {
            BridgeAiLog.Write("rhino-8", "Chat failed: " + ex);
            AppendMessage("错误", ex.Message);
        }
        finally
        {
            SetBusy(false, "就绪");
        }
    }

    private static string BuildModelContext()
    {
        var document = RhinoDoc.ActiveDoc
            ?? throw new InvalidOperationException("当前没有打开的 Rhino 文档。");
        var objects = document.Objects.GetObjectList(ObjectType.AnyObject).ToList();
        var counts = objects
            .GroupBy(item => item.ObjectType.ToString())
            .OrderBy(item => item.Key)
            .Select(item => item.Key + "=" + item.Count());
        var layers = document.Layers
            .Where(item => !item.IsDeleted)
            .OrderBy(item => item.FullPath)
            .Select(item => item.FullPath)
            .ToList();
        var selected = document.Objects
            .GetSelectedObjects(false, false)
            .Select(item => item.ObjectType + "#" + item.Id)
            .ToList();

        var builder = new StringBuilder();
        builder.AppendLine("文档：" + (document.Name ?? "未命名"));
        builder.AppendLine("单位：" + document.ModelUnitSystem);
        builder.AppendLine("对象总数：" + objects.Count);
        builder.AppendLine("对象分类：" + string.Join("，", counts));
        builder.AppendLine("图层：" + string.Join("，", layers));
        builder.AppendLine("当前选择：" + (selected.Count == 0 ? "无" : string.Join("，", selected)));
        return builder.ToString();
    }

    private void ExecuteScriptWithConfirmation(string scriptJson)
    {
        using var document = JsonDocument.Parse(scriptJson);
        var operations = GetOperations(document.RootElement);
        if (operations.Count == 0)
        {
            return;
        }

        var commands = string.Join("、", operations.Select(item => item.GetProperty("command").GetString()));
        var result = MessageBox.Show(
            this,
            "AI 计划执行以下 Rhino 操作：\n\n" + commands + "\n\n是否执行？",
            "确认写入 Rhino 文档",
            MessageBoxButtons.YesNo,
            MessageBoxType.Question);
        if (result != DialogResult.Yes)
        {
            AppendMessage("系统", "已取消写入操作。");
            return;
        }

        ExecuteOperations(operations);
    }

    private static List<JsonElement> GetOperations(JsonElement root)
    {
        var result = new List<JsonElement>();
        if (root.TryGetProperty("command", out _))
        {
            result.Add(root.Clone());
        }
        else if (root.TryGetProperty("operations", out var operations) &&
            operations.ValueKind == JsonValueKind.Array)
        {
            result.AddRange(operations.EnumerateArray().Select(item => item.Clone()));
        }

        return result;
    }

    private void ExecuteOperations(IReadOnlyList<JsonElement> operations)
    {
        var document = RhinoDoc.ActiveDoc
            ?? throw new InvalidOperationException("当前没有打开的 Rhino 文档。");
        var executed = new List<string>();

        foreach (var operation in operations)
        {
            var command = operation.GetProperty("command").GetString() ?? string.Empty;
            var parameters = operation.TryGetProperty("parameters", out var value)
                ? value
                : default;
            switch (command.ToLowerInvariant())
            {
                case "create_layer":
                    EnsureLayer(document, GetString(parameters, "name"), GetInt(parameters, "color_argb", 0));
                    break;
                case "set_current_layer":
                    document.Layers.SetCurrentLayerIndex(
                        EnsureLayer(document, GetString(parameters, "name"), 0),
                        true);
                    break;
                case "add_point":
                    AddPoint(document, parameters);
                    break;
                case "add_line":
                    AddLine(document, parameters);
                    break;
                case "add_circle":
                    AddCircle(document, parameters);
                    break;
                case "delete_selected":
                    DeleteSelected(document);
                    break;
                default:
                    throw new InvalidOperationException("不支持的 Rhino AI 命令：" + command);
            }

            executed.Add(command);
        }

        document.Views.Redraw();
        AppendMessage("系统", "已执行：" + string.Join("、", executed));
    }

    private static int EnsureLayer(RhinoDoc document, string name, int colorArgb)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("图层名称不能为空。");
        }

        var existing = document.Layers.FindName(name);
        if (existing != null)
        {
            return existing.Index;
        }

        var layer = new Layer { Name = name };
        if (colorArgb != 0)
        {
            layer.Color = System.Drawing.Color.FromArgb(colorArgb);
        }

        var index = document.Layers.Add(layer);
        if (index < 0)
        {
            throw new InvalidOperationException("无法创建 Rhino 图层：" + name);
        }

        return index;
    }

    private static void AddPoint(RhinoDoc document, JsonElement parameters)
    {
        var attributes = GetAttributes(document, parameters);
        var id = document.Objects.AddPoint(GetPoint(parameters, "point"), attributes);
        EnsureCreated(id, "点");
    }

    private static void AddLine(RhinoDoc document, JsonElement parameters)
    {
        var attributes = GetAttributes(document, parameters);
        var id = document.Objects.AddLine(
            GetPoint(parameters, "start"),
            GetPoint(parameters, "end"),
            attributes);
        EnsureCreated(id, "直线");
    }

    private static void AddCircle(RhinoDoc document, JsonElement parameters)
    {
        var center = GetPoint(parameters, "center");
        var radius = GetDouble(parameters, "radius");
        if (radius <= 0)
        {
            throw new InvalidOperationException("圆半径必须大于 0。");
        }

        var circle = new Circle(Plane.WorldXY, center, radius);
        var id = document.Objects.AddCircle(circle, GetAttributes(document, parameters));
        EnsureCreated(id, "圆");
    }

    private static ObjectAttributes GetAttributes(RhinoDoc document, JsonElement parameters)
    {
        var attributes = new ObjectAttributes();
        var layer = GetOptionalString(parameters, "layer");
        if (!string.IsNullOrWhiteSpace(layer))
        {
            attributes.LayerIndex = EnsureLayer(document, layer, 0);
        }

        return attributes;
    }

    private static void DeleteSelected(RhinoDoc document)
    {
        var selected = document.Objects.GetSelectedObjects(false, false).ToList();
        if (selected.Count == 0)
        {
            throw new InvalidOperationException("当前没有可删除的选择对象。");
        }

        foreach (var item in selected)
        {
            document.Objects.Delete(item, true);
        }
    }

    private static void EnsureCreated(Guid id, string objectName)
    {
        if (id == Guid.Empty)
        {
            throw new InvalidOperationException("无法创建 Rhino " + objectName + "。");
        }
    }

    private static Point3d GetPoint(JsonElement parameters, string name)
    {
        if (!parameters.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.Array ||
            value.GetArrayLength() < 2)
        {
            throw new InvalidOperationException(name + " 必须是 [x,y,z] 坐标数组。");
        }

        return new Point3d(
            value[0].GetDouble(),
            value[1].GetDouble(),
            value.GetArrayLength() > 2 ? value[2].GetDouble() : 0);
    }

    private static string GetString(JsonElement parameters, string name)
    {
        var result = GetOptionalString(parameters, name);
        if (string.IsNullOrWhiteSpace(result))
        {
            throw new InvalidOperationException(name + " 不能为空。");
        }

        return result;
    }

    private static string GetOptionalString(JsonElement parameters, string name)
    {
        return parameters.ValueKind == JsonValueKind.Object &&
            parameters.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static int GetInt(JsonElement parameters, string name, int defaultValue)
    {
        return parameters.ValueKind == JsonValueKind.Object &&
            parameters.TryGetProperty(name, out var value) &&
            value.TryGetInt32(out var result)
            ? result
            : defaultValue;
    }

    private static double GetDouble(JsonElement parameters, string name)
    {
        return parameters.ValueKind == JsonValueKind.Object &&
            parameters.TryGetProperty(name, out var value) &&
            value.TryGetDouble(out var result)
            ? result
            : 0;
    }

    private void ShowSettings()
    {
        try
        {
            var dialog = new RhinoSettingsDialog();
            dialog.ShowModal(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "模型配置", MessageBoxButtons.OK, MessageBoxType.Warning);
        }
    }

    private static void SaveScript(string scriptJson)
    {
        var directory = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "YingTanAiBridge",
            "automation-scripts");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "rhino-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".json"),
            scriptJson,
            new UTF8Encoding(false));
    }

    private void AppendMessage(string role, string message)
    {
        if (!string.IsNullOrEmpty(_history.Text))
        {
            _history.Append("\n\n");
        }

        _history.Append(role + "：\n" + message);
        _history.CaretIndex = _history.Text.Length;
    }

    private void SetBusy(bool busy, string status)
    {
        _send.Enabled = !busy;
        _composer.Enabled = !busy;
        _status.Text = status;
    }
}

internal sealed class RhinoSettingsDialog : Dialog
{
    private readonly BridgeAiSettingsSnapshot _settings;
    private readonly DropDown _provider = new DropDown();
    private readonly TextBox _baseUrl = new TextBox();
    private readonly TextBox _model = new TextBox();
    private readonly DropDown _apiMode = new DropDown();
    private readonly NumericStepper _timeout = new NumericStepper();
    private readonly NumericStepper _maxTokens = new NumericStepper();
    private readonly PasswordBox _apiKey = new PasswordBox();
    private readonly Label _keyState = new Label();
    private readonly TextBox _agentName = new TextBox();
    private readonly TextArea _agentPrompt = new TextArea { Wrap = true };
    private readonly CheckBox _confirmWrites = new CheckBox { Text = "写入宿主文档前要求确认" };
    private readonly ListBox _skillList = new ListBox();
    private readonly CheckBox _skillEnabled = new CheckBox { Text = "启用" };
    private readonly TextBox _skillName = new TextBox();
    private readonly TextArea _skillDescription = new TextArea { Wrap = true, Height = 80 };
    private readonly TextArea _skillInstructions = new TextArea { Wrap = true, Height = 150 };
    private readonly Label _status = new Label { Text = "配置在 Revit、AutoCAD、Rhino 之间共享", TextColor = Colors.Gray };
    private readonly Button _test = new Button { Text = "测试连接" };
    private readonly Button _save = new Button { Text = "保存" };
    private BridgeAiProviderConfig? _currentProvider;
    private int _selectedSkillIndex = -1;
    private bool _loading;

    public RhinoSettingsDialog()
    {
        _settings = SharedBridgeAiSettings.Load();
        Title = "盈碳 AI Bridge 配置";
        MinimumSize = new Size(660, 580);
        Size = new Size(760, 680);
        Padding = 12;
        Resizable = true;
        BuildUi();
        LoadSettings();
    }

    private void BuildUi()
    {
        var tabs = new TabControl();
        tabs.Pages.Add(new TabPage { Text = "模型", Content = BuildProviderTab() });
        tabs.Pages.Add(new TabPage { Text = "Agent", Content = BuildAgentTab() });
        tabs.Pages.Add(new TabPage { Text = "Skills", Content = BuildSkillsTab() });

        _test.Click += async (_, _) => await TestConnectionAsync();
        _save.Click += (_, _) => SaveAndClose();
        var cancel = new Button { Text = "取消" };
        cancel.Click += (_, _) => Close();

        Content = new TableLayout
        {
            Spacing = new Size(8, 8),
            Rows =
            {
                new TableRow(tabs) { ScaleHeight = true },
                new TableRow(_status, null, _test, _save, cancel)
            }
        };
    }

    private Control BuildProviderTab()
    {
        _provider.SelectedIndexChanged += (_, _) => ProviderChanged();
        _apiMode.Items.Add("ChatCompletions");
        _apiMode.Items.Add("Responses");
        _timeout.MinValue = 10;
        _timeout.MaxValue = 300;
        _timeout.Increment = 10;
        _timeout.DecimalPlaces = 0;
        _maxTokens.MinValue = 16;
        _maxTokens.MaxValue = 32768;
        _maxTokens.Increment = 256;
        _maxTokens.DecimalPlaces = 0;

        var clear = new Button { Text = "清除" };
        clear.Click += (_, _) =>
        {
            if (_currentProvider != null)
            {
                _currentProvider.EncryptedApiKey = string.Empty;
                _apiKey.Text = string.Empty;
                UpdateKeyState();
            }
        };

        return new TableLayout
        {
            Padding = 14,
            Spacing = new Size(8, 10),
            Rows =
            {
                FormRow("提供商", _provider),
                FormRow("Base URL", _baseUrl),
                FormRow("模型 ID", _model),
                FormRow("调用模式", _apiMode),
                FormRow("超时（秒）", _timeout),
                FormRow("最大输出", _maxTokens),
                FormRow("API Key", new TableLayout
                {
                    Spacing = new Size(6, 0),
                    Rows = { new TableRow(_apiKey, clear) }
                }),
                FormRow("密钥状态", _keyState),
                new TableRow(new Label
                {
                    Text = "API Key 留空会保留原密钥，并使用当前 Windows 用户的 DPAPI 加密保存。",
                    TextColor = Colors.Gray
                }),
                new TableRow { ScaleHeight = true }
            }
        };
    }

    private Control BuildAgentTab()
    {
        return new TableLayout
        {
            Padding = 14,
            Spacing = new Size(8, 10),
            Rows =
            {
                FormRow("Agent 名称", _agentName),
                new TableRow(new Label { Text = "系统提示词", VerticalAlignment = VerticalAlignment.Top }, _agentPrompt)
                {
                    ScaleHeight = true
                },
                FormRow("安全策略", _confirmWrites)
            }
        };
    }

    private Control BuildSkillsTab()
    {
        _skillList.SelectedIndexChanged += (_, _) => SkillSelectionChanged();
        var add = new Button { Text = "新增 Skill" };
        add.Click += (_, _) => AddSkill();
        var remove = new Button { Text = "删除" };
        remove.Click += (_, _) => RemoveSkill();

        var listPanel = new TableLayout
        {
            Width = 190,
            Spacing = new Size(6, 6),
            Rows =
            {
                new TableRow(_skillList) { ScaleHeight = true },
                new TableRow(add, remove)
            }
        };
        var detail = new TableLayout
        {
            Spacing = new Size(8, 8),
            Rows =
            {
                FormRow("名称", _skillName),
                FormRow("状态", _skillEnabled),
                new TableRow(new Label { Text = "说明", VerticalAlignment = VerticalAlignment.Top }, _skillDescription),
                new TableRow(new Label { Text = "执行指令", VerticalAlignment = VerticalAlignment.Top }, _skillInstructions)
                {
                    ScaleHeight = true
                }
            }
        };

        return new TableLayout
        {
            Padding = 12,
            Spacing = new Size(10, 0),
            Rows = { new TableRow(listPanel, detail) { ScaleHeight = true } }
        };
    }

    private void LoadSettings()
    {
        _loading = true;
        foreach (var item in _settings.Providers)
        {
            _provider.Items.Add(item.DisplayName);
        }

        var activeIndex = _settings.Providers.FindIndex(item =>
            string.Equals(item.Provider, _settings.ActiveProvider, StringComparison.OrdinalIgnoreCase));
        _provider.SelectedIndex = activeIndex >= 0 ? activeIndex : 0;
        _currentProvider = _settings.Providers[_provider.SelectedIndex];
        LoadProvider(_currentProvider);
        _agentName.Text = _settings.Agent.Name;
        _agentPrompt.Text = _settings.Agent.SystemPrompt;
        _confirmWrites.Checked = _settings.Agent.ConfirmBeforeWrite;
        RefreshSkillList(0);
        _loading = false;
    }

    private void ProviderChanged()
    {
        if (_loading || _provider.SelectedIndex < 0 ||
            _provider.SelectedIndex >= _settings.Providers.Count)
        {
            return;
        }

        CaptureProvider();
        _currentProvider = _settings.Providers[_provider.SelectedIndex];
        LoadProvider(_currentProvider);
    }

    private void LoadProvider(BridgeAiProviderConfig provider)
    {
        _loading = true;
        _baseUrl.Text = provider.BaseUrl;
        _model.Text = provider.Model;
        _apiMode.SelectedIndex = string.Equals(provider.ApiMode, "Responses", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        _timeout.Value = Math.Max(_timeout.MinValue, Math.Min(_timeout.MaxValue, provider.TimeoutSeconds));
        _maxTokens.Value = Math.Max(_maxTokens.MinValue, Math.Min(_maxTokens.MaxValue, provider.MaxTokens));
        _apiKey.Text = string.Empty;
        UpdateKeyState();
        _loading = false;
    }

    private void CaptureProvider()
    {
        if (_loading || _currentProvider == null)
        {
            return;
        }

        _currentProvider.BaseUrl = (_baseUrl.Text ?? string.Empty).Trim();
        _currentProvider.Model = (_model.Text ?? string.Empty).Trim();
        _currentProvider.ApiMode = _apiMode.SelectedIndex == 1 ? "Responses" : "ChatCompletions";
        _currentProvider.TimeoutSeconds = (int)_timeout.Value;
        _currentProvider.MaxTokens = (int)_maxTokens.Value;
        if (!string.IsNullOrWhiteSpace(_apiKey.Text))
        {
            SharedBridgeAiSettings.SetApiKey(_currentProvider, _apiKey.Text);
            _apiKey.Text = string.Empty;
            UpdateKeyState();
        }
    }

    private void RefreshSkillList(int preferredIndex)
    {
        _loading = true;
        _skillList.Items.Clear();
        foreach (var skill in _settings.Skills)
        {
            _skillList.Items.Add(string.IsNullOrWhiteSpace(skill.Name) ? "未命名 Skill" : skill.Name);
        }

        if (_settings.Skills.Count > 0)
        {
            _skillList.SelectedIndex = Math.Max(0, Math.Min(preferredIndex, _settings.Skills.Count - 1));
            _selectedSkillIndex = _skillList.SelectedIndex;
            LoadSkill(_settings.Skills[_selectedSkillIndex]);
        }
        else
        {
            _selectedSkillIndex = -1;
            LoadSkill(null);
        }

        _loading = false;
    }

    private void SkillSelectionChanged()
    {
        if (_loading)
        {
            return;
        }

        CaptureSkill();
        _selectedSkillIndex = _skillList.SelectedIndex;
        LoadSkill(_selectedSkillIndex >= 0 && _selectedSkillIndex < _settings.Skills.Count
            ? _settings.Skills[_selectedSkillIndex]
            : null);
    }

    private void LoadSkill(BridgeAiSkillConfig? skill)
    {
        var enabled = skill != null;
        _skillName.Enabled = enabled;
        _skillEnabled.Enabled = enabled;
        _skillDescription.Enabled = enabled;
        _skillInstructions.Enabled = enabled;
        _skillName.Text = skill?.Name ?? string.Empty;
        _skillEnabled.Checked = skill?.Enabled ?? false;
        _skillDescription.Text = skill?.Description ?? string.Empty;
        _skillInstructions.Text = skill?.Instructions ?? string.Empty;
    }

    private void CaptureSkill()
    {
        if (_loading || _selectedSkillIndex < 0 || _selectedSkillIndex >= _settings.Skills.Count)
        {
            return;
        }

        var skill = _settings.Skills[_selectedSkillIndex];
        skill.Name = (_skillName.Text ?? string.Empty).Trim();
        skill.Enabled = _skillEnabled.Checked == true;
        skill.Description = (_skillDescription.Text ?? string.Empty).Trim();
        skill.Instructions = (_skillInstructions.Text ?? string.Empty).Trim();
    }

    private void AddSkill()
    {
        CaptureSkill();
        _settings.Skills.Add(new BridgeAiSkillConfig
        {
            Name = "新 Skill",
            Enabled = true
        });
        RefreshSkillList(_settings.Skills.Count - 1);
    }

    private void RemoveSkill()
    {
        if (_selectedSkillIndex < 0 || _selectedSkillIndex >= _settings.Skills.Count)
        {
            return;
        }

        _settings.Skills.RemoveAt(_selectedSkillIndex);
        RefreshSkillList(Math.Max(0, _selectedSkillIndex - 1));
    }

    private async Task TestConnectionAsync()
    {
        try
        {
            CaptureProvider();
            if (_currentProvider == null)
            {
                return;
            }

            SetBusy(true, "正在测试连接...");
            var result = await HostAiClient.TestConnectionAsync(
                "rhino-settings",
                _settings,
                _currentProvider);
            _status.Text = "连接成功：" + result;
            MessageBox.Show(this, "连接成功：" + result, "模型配置");
        }
        catch (Exception ex)
        {
            _status.Text = "连接失败";
            MessageBox.Show(this, ex.Message, "连接失败", MessageBoxButtons.OK, MessageBoxType.Warning);
        }
        finally
        {
            SetBusy(false, _status.Text);
        }
    }

    private void SaveAndClose()
    {
        try
        {
            CaptureProvider();
            CaptureSkill();
            if (_currentProvider == null)
            {
                throw new InvalidOperationException("请选择模型提供商。");
            }

            _settings.ActiveProvider = _currentProvider.Provider;
            _settings.Agent.Name = (_agentName.Text ?? string.Empty).Trim();
            _settings.Agent.SystemPrompt = (_agentPrompt.Text ?? string.Empty).Trim();
            _settings.Agent.ConfirmBeforeWrite = _confirmWrites.Checked == true;
            SharedBridgeAiSettings.Save(_settings);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "保存失败", MessageBoxButtons.OK, MessageBoxType.Warning);
        }
    }

    private void UpdateKeyState()
    {
        var hasKey = _currentProvider != null && SharedBridgeAiSettings.HasApiKey(_currentProvider);
        _keyState.Text = hasKey ? "已保存加密密钥" : "尚未配置";
        _keyState.TextColor = hasKey ? Colors.Green : Colors.Red;
    }

    private void SetBusy(bool busy, string status)
    {
        _test.Enabled = !busy;
        _save.Enabled = !busy;
        _status.Text = status;
    }

    private static TableRow FormRow(string label, Control control)
    {
        return new TableRow(
            new Label { Text = label, VerticalAlignment = VerticalAlignment.Center, Width = 110 },
            control);
    }
}
