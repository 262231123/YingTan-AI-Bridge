using YingTanAiBridge.Contracts;
#if AUTOCAD_SDK
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Color = System.Drawing.Color;
using Exception = System.Exception;
using Font = System.Drawing.Font;
#endif

namespace YingTanAiBridge.AutoCad;

public static class AutoCadBridgeAdapter
{
    public static BridgeHostDescriptor Descriptor { get; } = new BridgeHostDescriptor(
        BridgeHostKind.AutoCad,
        "盈碳 AutoCAD AI Bridge",
        OperationSafetyPolicy.Capabilities(
            BridgeCapability.ReadModel,
            BridgeCapability.QueryElements,
            BridgeCapability.InspectSelection,
            BridgeCapability.CreateElements,
            BridgeCapability.UpdateElements,
            BridgeCapability.DeleteElements,
            BridgeCapability.RunNativeCommand));
}

#if AUTOCAD_SDK
public sealed class YingTanAutoCadAiBridgeExtension : IExtensionApplication
{
    private static ResolveEventHandler? _assemblyResolver;

    public void Initialize()
    {
        _assemblyResolver = ResolveLocalAssembly;
        AppDomain.CurrentDomain.AssemblyResolve += _assemblyResolver;
        Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
            "\n盈碳 AutoCAD AI Bridge 已加载。输入 YINGTANAIBRIDGE 打开助手。\n");
    }

    public void Terminate()
    {
        AutoCadAssistantPalette.Dispose();
        if (_assemblyResolver != null)
        {
            AppDomain.CurrentDomain.AssemblyResolve -= _assemblyResolver;
            _assemblyResolver = null;
        }
    }

    private static Assembly? ResolveLocalAssembly(object sender, ResolveEventArgs args)
    {
        var name = new AssemblyName(args.Name).Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(typeof(YingTanAutoCadAiBridgeExtension).Assembly.Location);
        var path = Path.Combine(directory ?? string.Empty, name + ".dll");
        return File.Exists(path) ? Assembly.LoadFrom(path) : null;
    }
}

public sealed class YingTanAutoCadAiBridgeCommands
{
    [CommandMethod("YINGTANAIBRIDGE", CommandFlags.Session)]
    public void OpenAssistant()
    {
        AutoCadAssistantPalette.Show();
    }
}

internal static class AutoCadAssistantPalette
{
    private static readonly Guid PaletteId = new Guid("C26C7711-4B14-4F72-A469-B14CC32FC283");
    private static PaletteSet? _palette;

    public static void Show()
    {
        if (_palette == null)
        {
            _palette = new PaletteSet("盈碳 AutoCAD AI Bridge", PaletteId)
            {
                MinimumSize = new Size(420, 520),
                Size = new Size(480, 720),
                DockEnabled = DockSides.Left | DockSides.Right,
                Style = PaletteSetStyles.ShowAutoHideButton |
                    PaletteSetStyles.ShowCloseButton |
                    PaletteSetStyles.ShowPropertiesMenu
            };
            _palette.Add("AI 助手", new AutoCadChatControl());
            _palette.Dock = DockSides.Right;
        }

        _palette.Visible = true;
        _palette.KeepFocus = true;
    }

    public static void Dispose()
    {
        _palette?.Dispose();
        _palette = null;
    }
}

internal sealed class AutoCadChatControl : UserControl
{
    private const string SystemPrompt =
        "你是盈碳 AutoCAD AI Bridge。你必须依据提供的当前 DWG 上下文回答，不得虚构对象数量。" +
        "只输出一个 JSON 对象，格式为 {\"reply\":\"给用户的中文说明\",\"script\":null}。" +
        "需要修改图形时，script 使用 {\"operations\":[{\"command\":\"命令\",\"parameters\":{}}]}。" +
        "允许的写命令仅有：create_layer(name,color_index)、set_current_layer(name)、" +
        "draw_line(start:[x,y,z],end:[x,y,z],layer)、draw_circle(center:[x,y,z],radius,layer)、erase_selected。" +
        "查询、统计、解释类请求直接利用上下文回答，script 必须为 null。" +
        "写操作要先简要说明将创建或修改什么，不要输出 Markdown 代码块，不要调用未列出的命令。";

    private readonly RichTextBox _history = new RichTextBox();
    private readonly TextBox _composer = new TextBox();
    private readonly Button _sendButton = new Button();
    private readonly Label _status = new Label();
    private readonly List<HostChatMessage> _conversation = new List<HostChatMessage>();

    public AutoCadChatControl()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(247, 248, 250);
        BuildUi();
        AppendMessage(
            "系统",
            "盈碳 AutoCAD AI Bridge 已就绪。可查询当前图形、选择集、图层和对象数量，也可要求创建图层、直线或圆。所有对话文字均可选择复制。");
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12),
            BackColor = BackColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = BackColor
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
        header.Controls.Add(new Label
        {
            Text = "盈碳 AutoCAD AI Bridge",
            Dock = DockStyle.Fill,
            Font = new Font("Microsoft YaHei UI", 11, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        var settingsButton = new Button
        {
            Text = "配置",
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.System
        };
        settingsButton.Click += (_, _) => ShowSettings();
        header.Controls.Add(settingsButton, 1, 0);

        _history.Dock = DockStyle.Fill;
        _history.ReadOnly = true;
        _history.BackColor = Color.White;
        _history.BorderStyle = BorderStyle.FixedSingle;
        _history.Font = new Font("Microsoft YaHei UI", 9.5f);
        _history.DetectUrls = true;
        _history.HideSelection = false;

        _composer.Dock = DockStyle.Fill;
        _composer.Multiline = true;
        _composer.ScrollBars = ScrollBars.Vertical;
        _composer.Font = new Font("Microsoft YaHei UI", 9.5f);
        _composer.KeyDown += ComposerOnKeyDown;

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = BackColor
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
        _status.Text = "就绪";
        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.ForeColor = Color.DimGray;
        _sendButton.Text = "发送";
        _sendButton.Dock = DockStyle.Fill;
        _sendButton.Click += async (_, _) => await SubmitAsync();
        footer.Controls.Add(_status, 0, 0);
        footer.Controls.Add(_sendButton, 1, 0);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(_history, 0, 1);
        root.Controls.Add(_composer, 0, 2);
        root.Controls.Add(footer, 0, 3);
        Controls.Add(root);
    }

    private async void ComposerOnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && !e.Shift)
        {
            e.SuppressKeyPress = true;
            await SubmitAsync();
        }
    }

    private async Task SubmitAsync()
    {
        var prompt = _composer.Text.Trim();
        if (prompt.Length == 0 || !_sendButton.Enabled)
        {
            return;
        }

        _composer.Clear();
        _conversation.Add(new HostChatMessage("User", prompt));
        AppendMessage("你", prompt);
        SetBusy(true, "正在读取 DWG 并调用模型...");

        try
        {
            var context = BuildModelContext();
            var response = await HostAiClient.CompleteAsync(
                "autocad-2022",
                SystemPrompt,
                context,
                _conversation);
            AppendMessage("AI", response.Reply);
            _conversation.Add(new HostChatMessage("Assistant", response.Reply));

            var scriptJson = response.ScriptJson;
            if (scriptJson != null && scriptJson.Trim().Length > 0)
            {
                SaveScript(scriptJson);
                ExecuteScriptWithConfirmation(scriptJson);
            }
        }
        catch (Exception ex)
        {
            BridgeAiLog.Write("autocad-2022", "Chat failed: " + ex);
            AppendMessage("错误", ex.Message);
        }
        finally
        {
            SetBusy(false, "就绪");
        }
    }

    private static string BuildModelContext()
    {
        var document = Application.DocumentManager.MdiActiveDocument
            ?? throw new InvalidOperationException("当前没有打开的 DWG 文档。");
        var database = document.Database;
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var layers = new List<string>();
        var selected = new List<string>();
        var currentLayer = string.Empty;

        using (document.LockDocument())
        using (var transaction = database.TransactionManager.StartOpenCloseTransaction())
        {
            var space = (BlockTableRecord)transaction.GetObject(database.CurrentSpaceId, OpenMode.ForRead);
            foreach (ObjectId id in space)
            {
                if (transaction.GetObject(id, OpenMode.ForRead, false) is Entity entity)
                {
                    var name = entity.GetType().Name;
                    counts[name] = counts.TryGetValue(name, out var count) ? count + 1 : 1;
                }
            }

            var layerTable = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
            currentLayer = ((LayerTableRecord)transaction.GetObject(database.Clayer, OpenMode.ForRead)).Name;
            foreach (ObjectId id in layerTable)
            {
                var layer = (LayerTableRecord)transaction.GetObject(id, OpenMode.ForRead);
                layers.Add(layer.Name);
            }

            var selection = document.Editor.SelectImplied();
            if (selection.Status == PromptStatus.OK)
            {
                foreach (var id in selection.Value.GetObjectIds())
                {
                    if (transaction.GetObject(id, OpenMode.ForRead, false) is Entity entity)
                    {
                        selected.Add(entity.GetType().Name + "#" + entity.Handle);
                    }
                }
            }
        }

        var builder = new StringBuilder();
        builder.AppendLine("文档：" + document.Name);
        builder.AppendLine("当前图层：" + currentLayer);
        builder.AppendLine("当前空间对象总数：" + counts.Values.Sum());
        builder.AppendLine("对象分类：" + string.Join("，", counts.OrderBy(item => item.Key).Select(item => item.Key + "=" + item.Value)));
        builder.AppendLine("图层：" + string.Join("，", layers.OrderBy(item => item)));
        builder.AppendLine("当前选择：" + (selected.Count == 0 ? "无" : string.Join("，", selected)));
        return builder.ToString();
    }

    private void ExecuteScriptWithConfirmation(string scriptJson)
    {
        using (var document = JsonDocument.Parse(scriptJson))
        {
            var operations = GetOperations(document.RootElement);
            if (operations.Count == 0)
            {
                return;
            }

            var commands = string.Join("、", operations.Select(item => item.GetProperty("command").GetString()));
            var result = MessageBox.Show(
                this,
                "AI 计划执行以下 AutoCAD 操作：\n\n" + commands + "\n\n是否执行？",
                "确认写入 DWG",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (result != DialogResult.Yes)
            {
                AppendMessage("系统", "已取消写入操作。");
                return;
            }

            ExecuteOperations(operations);
        }
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
        var document = Application.DocumentManager.MdiActiveDocument
            ?? throw new InvalidOperationException("当前没有打开的 DWG 文档。");
        var database = document.Database;
        var executed = new List<string>();

        using (document.LockDocument())
        using (var transaction = database.TransactionManager.StartTransaction())
        {
            foreach (var operation in operations)
            {
                var command = operation.GetProperty("command").GetString() ?? string.Empty;
                var parameters = operation.TryGetProperty("parameters", out var value)
                    ? value
                    : default(JsonElement);
                switch (command.ToLowerInvariant())
                {
                    case "create_layer":
                        EnsureLayer(database, transaction, GetString(parameters, "name"), GetInt(parameters, "color_index", 7));
                        break;
                    case "set_current_layer":
                        database.Clayer = EnsureLayer(database, transaction, GetString(parameters, "name"), 7);
                        break;
                    case "draw_line":
                        AddLine(database, transaction, parameters);
                        break;
                    case "draw_circle":
                        AddCircle(database, transaction, parameters);
                        break;
                    case "erase_selected":
                        EraseSelected(document, transaction);
                        break;
                    default:
                        throw new InvalidOperationException("不支持的 AutoCAD AI 命令：" + command);
                }

                executed.Add(command);
            }

            transaction.Commit();
        }

        document.Editor.Regen();
        AppendMessage("系统", "已执行：" + string.Join("、", executed));
    }

    private static ObjectId EnsureLayer(
        Database database,
        Transaction transaction,
        string name,
        int colorIndex)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("图层名称不能为空。");
        }

        var table = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
        if (table.Has(name))
        {
            return table[name];
        }

        table.UpgradeOpen();
        var record = new LayerTableRecord
        {
            Name = name,
            Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                ColorMethod.ByAci,
                (short)Math.Max(1, Math.Min(colorIndex, 255)))
        };
        var id = table.Add(record);
        transaction.AddNewlyCreatedDBObject(record, true);
        return id;
    }

    private static void AddLine(Database database, Transaction transaction, JsonElement parameters)
    {
        var start = GetPoint(parameters, "start");
        var end = GetPoint(parameters, "end");
        var layer = GetOptionalString(parameters, "layer");
        var space = (BlockTableRecord)transaction.GetObject(database.CurrentSpaceId, OpenMode.ForWrite);
        var entity = new Line(start, end);
        if (!string.IsNullOrWhiteSpace(layer))
        {
            entity.LayerId = EnsureLayer(database, transaction, layer, 7);
        }

        space.AppendEntity(entity);
        transaction.AddNewlyCreatedDBObject(entity, true);
    }

    private static void AddCircle(Database database, Transaction transaction, JsonElement parameters)
    {
        var center = GetPoint(parameters, "center");
        var radius = GetDouble(parameters, "radius");
        if (radius <= 0)
        {
            throw new InvalidOperationException("圆半径必须大于 0。");
        }

        var layer = GetOptionalString(parameters, "layer");
        var space = (BlockTableRecord)transaction.GetObject(database.CurrentSpaceId, OpenMode.ForWrite);
        var entity = new Circle(center, Vector3d.ZAxis, radius);
        if (!string.IsNullOrWhiteSpace(layer))
        {
            entity.LayerId = EnsureLayer(database, transaction, layer, 7);
        }

        space.AppendEntity(entity);
        transaction.AddNewlyCreatedDBObject(entity, true);
    }

    private static void EraseSelected(Document document, Transaction transaction)
    {
        var selection = document.Editor.SelectImplied();
        if (selection.Status != PromptStatus.OK)
        {
            throw new InvalidOperationException("当前没有可删除的选择对象。");
        }

        foreach (var id in selection.Value.GetObjectIds())
        {
            if (transaction.GetObject(id, OpenMode.ForWrite, false) is Entity entity)
            {
                entity.Erase();
            }
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
            using (var dialog = new AutoCadSettingsForm())
            {
                dialog.ShowDialog(this);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "模型配置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static void SaveScript(string scriptJson)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YingTanAiBridge",
            "automation-scripts");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "autocad-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".json"),
            scriptJson,
            new UTF8Encoding(false));
    }

    private void AppendMessage(string role, string message)
    {
        if (_history.TextLength > 0)
        {
            _history.AppendText(Environment.NewLine + Environment.NewLine);
        }

        _history.SelectionFont = new Font(_history.Font, FontStyle.Bold);
        _history.AppendText(role + "  ");
        _history.SelectionFont = _history.Font;
        _history.AppendText(message);
        _history.SelectionStart = _history.TextLength;
        _history.ScrollToCaret();
    }

    private void SetBusy(bool busy, string status)
    {
        _sendButton.Enabled = !busy;
        _composer.Enabled = !busy;
        _status.Text = status;
    }
}

internal sealed class AutoCadSettingsForm : Form
{
    private readonly BridgeAiSettingsSnapshot _settings;
    private readonly ComboBox _provider = new ComboBox();
    private readonly TextBox _baseUrl = new TextBox();
    private readonly TextBox _model = new TextBox();
    private readonly ComboBox _apiMode = new ComboBox();
    private readonly NumericUpDown _timeout = new NumericUpDown();
    private readonly NumericUpDown _maxTokens = new NumericUpDown();
    private readonly TextBox _apiKey = new TextBox();
    private readonly Label _keyState = new Label();
    private readonly TextBox _agentName = new TextBox();
    private readonly TextBox _agentPrompt = new TextBox();
    private readonly CheckBox _confirmWrites = new CheckBox();
    private readonly DataGridView _skills = new DataGridView();
    private readonly Label _status = new Label();
    private readonly Button _test = new Button();
    private readonly Button _save = new Button();
    private BridgeAiProviderConfig? _currentProvider;
    private bool _loading;

    public AutoCadSettingsForm()
    {
        _settings = SharedBridgeAiSettings.Load();
        Text = "盈碳 AI Bridge 配置";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(660, 580);
        Size = new Size(760, 680);
        Font = new Font("Microsoft YaHei UI", 9f);
        BackColor = Color.FromArgb(247, 248, 250);
        BuildUi();
        LoadSettings();
    }

    private void BuildUi()
    {
        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(new TabPage("模型") { Controls = { BuildProviderTab() } });
        tabs.TabPages.Add(new TabPage("Agent") { Controls = { BuildAgentTab() } });
        tabs.TabPages.Add(new TabPage("Skills") { Controls = { BuildSkillsTab() } });

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            ColumnCount = 2,
            Padding = new Padding(10, 8, 10, 8)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _status.Text = "配置在 Revit、AutoCAD、Rhino 之间共享";
        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.ForeColor = Color.DimGray;

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight,
            WrapContents = false
        };
        _test.Text = "测试连接";
        _test.AutoSize = true;
        _test.Click += async (_, _) => await TestConnectionAsync();
        _save.Text = "保存";
        _save.AutoSize = true;
        _save.Click += (_, _) => SaveAndClose();
        var cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
        actions.Controls.Add(_test);
        actions.Controls.Add(_save);
        actions.Controls.Add(cancel);
        footer.Controls.Add(_status, 0, 0);
        footer.Controls.Add(actions, 1, 0);

        Controls.Add(tabs);
        Controls.Add(footer);
        AcceptButton = _save;
        CancelButton = cancel;
    }

    private Control BuildProviderTab()
    {
        var panel = CreateFormTable();
        _provider.DropDownStyle = ComboBoxStyle.DropDownList;
        _provider.SelectedIndexChanged += (_, _) => ProviderChanged();
        _apiMode.DropDownStyle = ComboBoxStyle.DropDownList;
        _apiMode.Items.AddRange(new object[] { "ChatCompletions", "Responses" });
        _timeout.Minimum = 10;
        _timeout.Maximum = 300;
        _timeout.Increment = 10;
        _maxTokens.Minimum = 16;
        _maxTokens.Maximum = 32768;
        _maxTokens.Increment = 256;
        _apiKey.UseSystemPasswordChar = true;

        AddRow(panel, "提供商", _provider);
        AddRow(panel, "Base URL", _baseUrl);
        AddRow(panel, "模型 ID", _model);
        AddRow(panel, "调用模式", _apiMode);
        AddRow(panel, "超时（秒）", _timeout);
        AddRow(panel, "最大输出", _maxTokens);

        var keyPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        keyPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        keyPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var clear = new Button { Text = "清除", AutoSize = true };
        clear.Click += (_, _) =>
        {
            if (_currentProvider != null)
            {
                _currentProvider.EncryptedApiKey = string.Empty;
                _apiKey.Clear();
                UpdateKeyState();
            }
        };
        keyPanel.Controls.Add(_apiKey, 0, 0);
        keyPanel.Controls.Add(clear, 1, 0);
        AddRow(panel, "API Key", keyPanel);
        AddRow(panel, "密钥状态", _keyState);

        var note = new Label
        {
            Text = "API Key 使用当前 Windows 用户的 DPAPI 加密保存，不会在界面中回显。",
            AutoSize = true,
            ForeColor = Color.DimGray,
            Margin = new Padding(4, 12, 4, 4)
        };
        panel.Controls.Add(note, 1, panel.RowCount);
        return panel;
    }

    private Control BuildAgentTab()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(14)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        _agentName.Dock = DockStyle.Fill;
        _agentPrompt.Dock = DockStyle.Fill;
        _agentPrompt.Multiline = true;
        _agentPrompt.ScrollBars = ScrollBars.Vertical;
        _confirmWrites.Text = "写入宿主文档前要求确认";
        _confirmWrites.AutoSize = true;
        panel.Controls.Add(CreateLabel("Agent 名称"), 0, 0);
        panel.Controls.Add(_agentName, 1, 0);
        panel.Controls.Add(CreateLabel("系统提示词"), 0, 1);
        panel.Controls.Add(_agentPrompt, 1, 1);
        panel.Controls.Add(CreateLabel("安全策略"), 0, 2);
        panel.Controls.Add(_confirmWrites, 1, 2);
        return panel;
    }

    private Control BuildSkillsTab()
    {
        _skills.Dock = DockStyle.Fill;
        _skills.AutoGenerateColumns = false;
        _skills.AllowUserToAddRows = false;
        _skills.AllowUserToDeleteRows = false;
        _skills.RowHeadersVisible = false;
        _skills.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _skills.MultiSelect = false;
        _skills.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        _skills.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "启用",
            DataPropertyName = "Enabled",
            Width = 52
        });
        _skills.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "名称",
            DataPropertyName = "Name",
            Width = 130
        });
        _skills.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "说明",
            DataPropertyName = "Description",
            Width = 190,
            DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.True }
        });
        _skills.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "执行指令",
            DataPropertyName = "Instructions",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.True }
        });

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 42,
            Padding = new Padding(8),
            FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight
        };
        var add = new Button { Text = "新增 Skill", AutoSize = true };
        add.Click += (_, _) =>
        {
            var binding = (BindingList<BridgeAiSkillConfig>)_skills.DataSource;
            binding.Add(new BridgeAiSkillConfig
            {
                Name = "新 Skill",
                Enabled = true
            });
        };
        var remove = new Button { Text = "删除", AutoSize = true };
        remove.Click += (_, _) =>
        {
            if (_skills.CurrentRow?.DataBoundItem is BridgeAiSkillConfig skill)
            {
                ((BindingList<BridgeAiSkillConfig>)_skills.DataSource).Remove(skill);
            }
        };
        buttons.Controls.Add(add);
        buttons.Controls.Add(remove);

        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
        panel.Controls.Add(_skills);
        panel.Controls.Add(buttons);
        return panel;
    }

    private void LoadSettings()
    {
        _loading = true;
        _provider.DataSource = _settings.Providers;
        _provider.DisplayMember = "DisplayName";
        var activeIndex = _settings.Providers.FindIndex(item =>
            string.Equals(item.Provider, _settings.ActiveProvider, StringComparison.OrdinalIgnoreCase));
        _provider.SelectedIndex = activeIndex >= 0 ? activeIndex : 0;
        _currentProvider = (BridgeAiProviderConfig)_provider.SelectedItem;
        LoadProvider(_currentProvider);
        _agentName.Text = _settings.Agent.Name;
        _agentPrompt.Text = _settings.Agent.SystemPrompt;
        _confirmWrites.Checked = _settings.Agent.ConfirmBeforeWrite;
        _skills.DataSource = new BindingList<BridgeAiSkillConfig>(_settings.Skills);
        _loading = false;
    }

    private void ProviderChanged()
    {
        if (_loading || _provider.SelectedItem is not BridgeAiProviderConfig selected)
        {
            return;
        }

        CaptureProvider();
        _currentProvider = selected;
        LoadProvider(selected);
    }

    private void LoadProvider(BridgeAiProviderConfig provider)
    {
        _loading = true;
        _baseUrl.Text = provider.BaseUrl;
        _model.Text = provider.Model;
        _apiMode.SelectedItem = string.Equals(provider.ApiMode, "Responses", StringComparison.OrdinalIgnoreCase)
            ? "Responses"
            : "ChatCompletions";
        _timeout.Value = Math.Max(_timeout.Minimum, Math.Min(_timeout.Maximum, provider.TimeoutSeconds));
        _maxTokens.Value = Math.Max(_maxTokens.Minimum, Math.Min(_maxTokens.Maximum, provider.MaxTokens));
        _apiKey.Clear();
        UpdateKeyState();
        _loading = false;
    }

    private void CaptureProvider()
    {
        if (_loading || _currentProvider == null)
        {
            return;
        }

        _currentProvider.BaseUrl = _baseUrl.Text.Trim();
        _currentProvider.Model = _model.Text.Trim();
        _currentProvider.ApiMode = _apiMode.SelectedItem?.ToString() ?? "ChatCompletions";
        _currentProvider.TimeoutSeconds = (int)_timeout.Value;
        _currentProvider.MaxTokens = (int)_maxTokens.Value;
        if (!string.IsNullOrWhiteSpace(_apiKey.Text))
        {
            SharedBridgeAiSettings.SetApiKey(_currentProvider, _apiKey.Text);
            _apiKey.Clear();
            UpdateKeyState();
        }
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
                "autocad-settings",
                _settings,
                _currentProvider);
            _status.Text = "连接成功：" + result;
            MessageBox.Show(this, "连接成功：" + result, "模型配置", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _status.Text = "连接失败";
            MessageBox.Show(this, ex.Message, "连接失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            _skills.EndEdit();
            CaptureProvider();
            if (_currentProvider == null)
            {
                throw new InvalidOperationException("请选择模型提供商。");
            }

            _settings.ActiveProvider = _currentProvider.Provider;
            _settings.Agent.Name = _agentName.Text.Trim();
            _settings.Agent.SystemPrompt = _agentPrompt.Text.Trim();
            _settings.Agent.ConfirmBeforeWrite = _confirmWrites.Checked;
            _settings.Skills = ((BindingList<BridgeAiSkillConfig>)_skills.DataSource).ToList();
            SharedBridgeAiSettings.Save(_settings);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void UpdateKeyState()
    {
        _keyState.Text = _currentProvider != null && SharedBridgeAiSettings.HasApiKey(_currentProvider)
            ? "已保存加密密钥"
            : "尚未配置";
        _keyState.ForeColor = _currentProvider != null && SharedBridgeAiSettings.HasApiKey(_currentProvider)
            ? Color.DarkGreen
            : Color.DarkRed;
    }

    private void SetBusy(bool busy, string status)
    {
        _test.Enabled = !busy;
        _save.Enabled = !busy;
        _status.Text = status;
    }

    private static TableLayoutPanel CreateFormTable()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(16),
            AutoScroll = true
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return panel;
    }

    private static void AddRow(TableLayoutPanel panel, string label, Control control)
    {
        var row = panel.RowCount++;
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(4, 6, 4, 6);
        panel.Controls.Add(CreateLabel(label), 0, row);
        panel.Controls.Add(control, 1, row);
    }

    private static Label CreateLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(4, 10, 4, 4)
        };
    }
}
#endif
