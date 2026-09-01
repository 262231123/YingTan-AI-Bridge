using System.Drawing;
using System.Windows.Forms;

namespace RevitCodexBridge.Addin;

internal sealed class AiHelpForm : Form
{
    public AiHelpForm()
    {
        Text = "Revit AI 助手 - 使用帮助";
        Width = 920;
        Height = 690;
        MinimumSize = new Size(780, 560);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        UiTheme.ApplyForm(this);
        BuildLayout();
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
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        var header = UiTheme.CreateSurfacePanel(new Padding(16, 10, 16, 10));
        var headerLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        headerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        headerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        headerLayout.Controls.Add(UiTheme.CreateTitle(this, "Revit Codex Bridge 使用帮助", 15F), 0, 0);
        headerLayout.Controls.Add(
            UiTheme.CreateMutedLabel("在 Revit 右侧面板中对话、查询模型、配置 Agent 与 Skill，并通过 Dry-run 和确认执行受控操作。"),
            0,
            1);
        header.Controls.Add(headerLayout);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(CreateTabs(), 0, 1);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        actions.Controls.Add(UiTheme.CreateButton("关闭", UiButtonKind.Primary, (_, _) => Close()));
        root.Controls.Add(actions, 0, 2);
        Controls.Add(root);
    }

    private Control CreateTabs()
    {
        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 9.5F, FontStyle.Regular),
            Padding = new Point(14, 5)
        };

        tabs.TabPages.Add(CreatePage("快速开始", """
1. 在 Revit 功能区点击“Revit AI 助手”，右侧将显示对话面板。

2. 点击“新建”创建独立会话。左侧保留会话记录，当前会话中显示完整问答与执行结果。

3. 首次使用请点击顶部“配置”，选择大模型服务并填入 API Key。API Key 会使用当前 Windows 用户凭据加密保存。

4. 从查询类问题开始，例如：
   - 当前项目有哪些标高？
   - 列出墙类型。
   - 当前模型有多少面墙？请说明统计口径。

5. 涉及创建或修改模型的任务，助手会先生成操作计划并进行 Dry-run。确认计划后才会写入 Revit 模型。
"""));
        tabs.TabPages.Add(CreatePage("模型、Agent 与 Skill", """
模型配置

支持 OpenAI、智谱 GLM、Kimi/Moonshot 和火山方舟/豆包等兼容接口。每个配置可独立设置 Base URL、模型名称、API Key、超时与最大输出长度。点击“测试连接”后再开始正式对话。

Agent

Agent 用于定义助手的角色与工作边界。内置角色包括 BIM 数据核对、建模执行、图纸交付和模型 QA。可以启用或关闭专业 Agent，并为团队写入项目专属指令。

Skill

Skill 用于固化可复用的工作规则，例如：
   - 查询前优先读取当前文档、标高、类型与选择集。
   - 墙体统计必须说明模型与明细表的差异口径。
   - 写操作先 Dry-run，再等待确认。
   - 已有插件可以作为 Skill 由助手查询能力、检查就绪条件并调度。
"""));
        tabs.TabPages.Add(CreatePage("对话与执行", """
查询任务

查询不会修改模型。常见查询包括当前文档、标高、墙类型、族类型、构件数量、选择集、图纸、视图和明细表。

写入任务

创建墙、房间、门窗、修改参数、生成图纸集等任务属于写入操作。信息不完整时，助手应先查询，不会猜测标高、墙类型、宿主或 ElementId。

Dry-run 与确认

系统默认先预演操作计划。预演通过后，聊天面板出现“执行计划”按钮；只有点击该按钮，才会在 Revit 的事务中写入模型。执行完成后，请检查创建对象、参数、数量与图面效果。

建议

对批量或关键模型操作，先在项目副本中验证，再应用到正式模型。
"""));
        tabs.TabPages.Add(CreatePage("模型 QA 与统计", """
数量统计不应只返回一个数字。

例如墙体数量，模型直接计数和墙明细表可见数量可能因叠层墙、成员墙、阶段、设计选项、明细表过滤或可见性设置而不同。请使用“说明统计口径”或让助手进一步定位异常 ElementId。

推荐提问方式

   - 统计墙体数量，并比较墙明细表的可见数量。
   - 找出未放置在图纸中的视图。
   - 读取当前选中的构件，列出关键参数。
   - 定位这些 ElementId，并说明它们的共同特征。

AI 的结论应作为模型核查的起点。涉及规范、工程量、施工和交付的结论，请由专业人员复核。
"""));
        tabs.TabPages.Add(CreatePage("常见问题", """
点击插件后没有显示面板

请在功能区点击“Revit AI 助手”，然后检查右侧是否有停靠面板。必要时退出并重新启动 Revit。

测试连接成功但实际对话失败

检查模型名称、Base URL、网络访问和 API Key 权限。火山方舟等服务请确认 API 模式与模型接口一致。日志文件可用于定位具体错误。

回答被截断

在“配置”中提高“最大输出”值，并确认供应商账户允许该模型的输出长度。复杂任务可以拆为查询、计划、执行和复核多个回合。

为什么助手没有直接改模型

这是预期的安全行为。写入前需要足够的模型信息、Dry-run 和用户确认。

日志位置

桥接日志与配置文件位于：%LOCALAPPDATA%\\RevitCodexBridge\\。提交问题时请附上时间、操作描述和相关日志片段，并避免公开 API Key。
"""));
        return tabs;
    }

    private TabPage CreatePage(string title, string content)
    {
        var page = new TabPage(title)
        {
            BackColor = UiTheme.WindowBackground,
            Padding = new Padding(10)
        };
        var text = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = UiTheme.Surface,
            ForeColor = UiTheme.Text,
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular),
            Text = content.Trim(),
            WordWrap = true
        };
        page.Controls.Add(text);
        return page;
    }
}
