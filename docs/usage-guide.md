# Revit Codex Bridge 使用说明

新版聊天设计与操作流程、示例提示词和实测范围见[对话式设计 Agent](conversational-design.md)。

本文说明如何在 Revit 2027 中加载桥接插件，并用 Codex/命令行控制当前打开的 Revit 模型。

## 1. 工作原理

整体链路如下：

```text
Codex / MCP 客户端 / 命令行
  -> RevitCodexBridge.Mcp / revitctl
  -> http://127.0.0.1:7878
  -> RevitCodexBridge.Addin
  -> Revit ExternalEvent
  -> Revit API
```

关键点：

- Revit API 只能在 Revit 进程内安全执行。
- 外部程序只发 JSON 指令，不直接调用 Revit API。
- 所有写入模型的操作默认都是 `dryRun`，必须显式加 `--write` 才会真正修改模型。
- 写入模型时，插件默认会在 Revit 内弹出确认框，确认后才进入 `Transaction`。

## 2. 前置条件

需要准备：

- Revit 2027 完整版。
- .NET SDK 10.0.100 或更新版本。
- Revit 2027 安装目录中存在：

```powershell
C:\Program Files\Autodesk\Revit 2027\RevitAPI.dll
C:\Program Files\Autodesk\Revit 2027\RevitAPIUI.dll
```

如果 Revit 安装在其他位置，先设置环境变量：

```powershell
$env:REVIT_2027_DIR = "D:\Autodesk\Revit 2027"
```

当前项目配置会自动探测 C 盘和 D 盘常见安装目录：

```powershell
C:\Program Files\Autodesk\Revit 2027
D:\Program Files\Autodesk\Revit 2027
```

## 3. 编译项目

在仓库根目录执行：

```powershell
cd <仓库所在目录>
dotnet build
```

如果提示找不到 `.NET SDK 10.0.100`，先安装 .NET SDK 10。

如果提示找不到 `RevitAPI.dll`，检查 `REVIT_2027_DIR` 或 `Directory.Build.props` 里的 `RevitInstallDir`。

## 4. 安装 Revit 插件

编译成功后执行：

```powershell
.\deploy\install-addin.ps1 -Configuration Debug -RevitVersion 2027
```

脚本会生成 Revit add-in manifest 到：

```powershell
$env:APPDATA\Autodesk\Revit\Addins\2027\RevitCodexBridge.addin
```

然后重启 Revit 2027。

## 5. 启动桥接服务

打开 Revit 2027 后，插件会随 Revit 启动，并监听：

```text
http://127.0.0.1:7878
```

你可以在 Revit 功能区看到 `Codex` 面板和 `Codex Bridge` 按钮。点击它可以查看桥接状态。

### 配置 DeepSeek V4 Pro

在 Revit 的“模型接口设置”中选择 `DeepSeek V4 Pro`，填入你自己的 DeepSeek API Key 并保存。默认值为：

```text
Base URL: https://api.deepseek.com
Model: deepseek-v4-pro
API mode: ChatCompletions
```

点击“测试连接”验证配置。API Key 使用 Windows DPAPI 按当前用户加密，不要将 `%LOCALAPPDATA%\RevitCodexBridge\ai-settings.json` 发布到 GitHub。

### 通过提示词生成 Revit / Dynamo 脚本

在聊天框中明确提出“生成/编写”以及目标格式，例如 C# ExternalCommand、pyRevit、Dynamo Python 或 `.dyn`。脚本工作室会生成完整工件、执行规则型静态检查，并保存到 `%LOCALAPPDATA%\RevitCodexBridge\script-studio`。它不会自动编译或执行，详见 [脚本工作室](script-studio.md)。

## 6. 基础验证

保持 Revit 2027 打开，然后在 PowerShell 中执行：

```powershell
dotnet run --project .\src\RevitCodexBridge.Cli -- health
```

正常会返回桥接状态、Revit 版本和支持的命令。

查看当前打开的模型：

```powershell
dotnet run --project .\src\RevitCodexBridge.Cli -- doc
```

列出标高：

```powershell
dotnet run --project .\src\RevitCodexBridge.Cli -- levels
```

列出墙类型：

```powershell
dotnet run --project .\src\RevitCodexBridge.Cli -- wall-types
```

列出门类型：

```powershell
dotnet run --project .\src\RevitCodexBridge.Cli -- door-types
```

列出窗类型：

```powershell
dotnet run --project .\src\RevitCodexBridge.Cli -- window-types
```

统计墙数量：

```powershell
dotnet run --project .\src\RevitCodexBridge.Cli -- count OST_Walls
```

等待 Revit 桥接服务启动，最多等 60 秒：

```powershell
dotnet run --project .\src\RevitCodexBridge.Cli -- wait 60
```

## 7. 创建墙体

先 dry-run，不真正写入模型：

```powershell
dotnet run --project .\src\RevitCodexBridge.Cli -- create-wall "Level 1" 0 0 5000 0 3000
```

参数含义：

- `"Level 1"`：标高名称。
- `0 0`：起点坐标，单位毫米。
- `5000 0`：终点坐标，单位毫米。
- `3000`：墙高，单位毫米。

中文样板里标高可能是 `标高 1`。也可以指定墙类型：

```powershell
dotnet run --project .\src\RevitCodexBridge.Cli -- create-wall "标高 1" 0 0 5000 0 3000 --wall-type "常规 - 200mm"
```

确认返回结果没问题后，再加 `--write` 真正创建：

```powershell
dotnet run --project .\src\RevitCodexBridge.Cli -- create-wall "Level 1" 0 0 5000 0 3000 --write
```

执行写入时 Revit 会弹出确认框，取消则不会创建墙体。

## 8. 修改参数

先 dry-run：

```powershell
dotnet run --project .\src\RevitCodexBridge.Cli -- set-param 123456 "Comments" "由 Codex 写入"
```

确认后写入：

```powershell
dotnet run --project .\src\RevitCodexBridge.Cli -- set-param 123456 "Comments" "由 Codex 写入" --write
```

如果参数是长度类 double，并且希望输入毫米：

```powershell
dotnet run --project .\src\RevitCodexBridge.Cli -- set-param 123456 "Unconnected Height" 3000 --double-unit mm --write
```

注意：

- `123456` 是 Revit 元素 ID。
- 只读参数不能修改。
- 参数名需要和 Revit 里显示或 API 可查到的名称一致。

## 9. MCP 工具方式

MCP server 已经可以把 Revit 桥接能力暴露成 `revit_*` 工具。它通过 stdio 与 MCP 客户端通信，再把命令转发给本地 Revit 桥。

运行：

```powershell
dotnet run --project .\src\RevitCodexBridge.Mcp\RevitCodexBridge.Mcp.csproj
```

可用工具：

- `revit_health`
- `revit_get_active_document`
- `revit_list_levels`
- `revit_list_wall_types`
- `revit_list_family_symbols`
- `revit_count_elements`
- `revit_create_wall`
- `revit_set_parameter`
- `revit_place_door`
- `revit_place_window`
- `revit_create_room`

MCP 写入类工具同样默认 `dryRun=true`。如果传入 `dryRun=false`，Revit 插件默认会弹出确认框。只有当外部系统已经有自己的审批界面时，才建议显式传 `confirmInRevit=false`。

## 10. 给 Codex 使用的 CLI 方式

当前第一版建议让 Codex 通过 CLI 间接控制 Revit，例如：

```powershell
dotnet run --project .\src\RevitCodexBridge.Cli -- levels
```

如果 Codex 已经生成了 BuildPlan，可以直接执行：

```powershell
dotnet run --project .\src\RevitCodexBridge.Cli -- run-plan .\planner\buildplan.generated.json
```

默认会强制 dry-run。真正写入需要 BuildPlan 里对应 operation 是 `dryRun=false`，并且 CLI 加 `--write`：

```powershell
dotnet run --project .\src\RevitCodexBridge.Cli -- run-plan .\planner\buildplan.generated.json --write
```

如果希望整组 BuildPlan 作为一个批量请求进入 Revit，并且写入时支持整体回滚，加 `--batch`：

```powershell
dotnet run --project .\src\RevitCodexBridge.Cli -- run-plan .\planner\buildplan.generated.json --batch
```

真正写入时：

```powershell
dotnet run --project .\src\RevitCodexBridge.Cli -- run-plan .\planner\buildplan.generated.json --batch --write
```

批量模式默认是原子执行：如果任一写入操作失败，整组写入会回滚。调试时可以加 `--continue-on-error` 收集更多失败结果；只有确实需要逐条提交时才使用 `--non-atomic`。

## 11. 常见问题

### 连接不上 `127.0.0.1:7878`

检查：

- Revit 2027 是否已启动。
- 插件是否安装到 `Addins\2027`。
- 是否重启过 Revit。
- 端口 `7878` 是否被其他程序占用。

### `No active Revit document is open`

Revit 已打开，但没有打开任何项目或族文件。先打开一个 `.rvt` 或 `.rfa`。

### `Parameter is read-only`

该参数是只读参数，不能直接写。需要换参数，或改类型/族/系统生成逻辑。

### 标高名称找不到

先运行：

```powershell
dotnet run --project .\src\RevitCodexBridge.Cli -- levels
```

确认实际标高名称，例如中文项目里可能叫 `标高 1`，不是 `Level 1`。

### 墙创建了但位置不对

CLI 输入坐标单位是毫米，Revit API 内部单位是英尺。桥接层会自动转换，但项目坐标原点和视图方向仍然取决于当前模型。

## 12. 建议使用习惯

- 先查询，再 dry-run，再 `--write`。
- 批量操作前另存模型。
- 第一次只在测试模型里运行。
- 对修改参数、创建构件、批量删除这类操作保留日志。
- 后续生产版应增加操作审批、命令白名单配置和事务回滚策略。

## 13. 卸载

```powershell
.\deploy\uninstall-addin.ps1 -RevitVersion 2027
```

该脚本只删除当前用户的 add-in manifest 和 `%LOCALAPPDATA%\YingTanAiBridge\Revit\2027` 插件文件。
