# 盈碳 AI 工程助手

面向 Revit 的本地 AI 桥接与自动化工具集，同时包含 AutoCAD、Rhino 和 Inventor 适配器源码。Revit 插件在宿主进程内安全调用 API，CLI、MCP 或 AI 规划器只通过本机 JSON 桥接发出受控请求。

## 当前状态

已提供：

- `src/RevitCodexBridge.Addin`：Revit 2027 add-in，目标框架 `net10.0-windows`。
- `src/RevitCodexBridge.Cli`：命令行客户端 `revitctl`。
- `src/RevitCodexBridge.Mcp`：后续 MCP wrapper 预留目录。
- `planner`：把自然语言/图片输入转换成 Revit BuildPlan JSON 的 AI 规划层。
- `dynamo`：Dynamo Python 节点版本和 JSON 命令示例。
- `deploy`：Revit `.addin` manifest 模板与安装脚本。
- `deploy/build-release.ps1`：生成可上传 GitHub Releases 的 ZIP 和 SHA-256。
- `docs/revit-2027-implementation-plan.md`：实现计划和命令边界。
- `docs/usage-guide.md`：从编译、安装到命令调用的使用说明。
- `docs/dynamo-version-guide.md`：Dynamo 版本使用说明和与 add-in 方案的对比。
- `docs/ai-planner-guide.md`：自然语言/图片到 BuildPlan 的使用说明。
- `docs/completion-checklist.md`：当前完成度、试运行步骤和剩余生产化能力。
- `docs/release-guide.md`：GitHub 发布、授权与安装包制作清单。
- DeepSeek V4 Pro（`deepseek-v4-pro`）的内置 OpenAI-compatible API 配置。
- 模型健康审计、参数治理、房间规划、门窗协调、交付预检和安全批处理等内置 Revit Skills。

## 仓库状态

这是可试用的工程原型，不是经过 Autodesk 认证的生产插件。查询、dry-run、Revit 内写入确认和批量回滚边界已实现；详细能力与待完成项见 [完成度清单](docs/completion-checklist.md)。

本项目使用 [MIT License](LICENSE)。Autodesk、AutoCAD、Revit、Inventor、Rhino 及其他商标归各自权利人所有；本项目不包含也不重新分发其专有 SDK 程序集。

## 前置条件

- Revit 2027 完整版，不是 Revit LT。
- .NET SDK 10.0.100 或更新版本。
- `RevitAPI.dll` 和 `RevitAPIUI.dll` 位于：

```powershell
C:\Program Files\Autodesk\Revit 2027
```

如果安装目录不同，设置：

```powershell
$env:REVIT_2027_DIR = "D:\Autodesk\Revit 2027"
```

本仓库也会自动探测：

```powershell
C:\Program Files\Autodesk\Revit 2027
D:\Program Files\Autodesk\Revit 2027
```

## 编译

```powershell
dotnet build
```

整个 solution 还包含需要对应宿主 SDK 的 AutoCAD/Rhino/Inventor 项目。如果机器只安装 Revit，可单独编译 Revit 插件、CLI 和 MCP 项目，详见 [使用说明](docs/usage-guide.md)。

## 安装插件

```powershell
.\deploy\install-addin.ps1 -Configuration Debug -RevitVersion 2027
```

然后重启 Revit 2027。插件启动后会在本机监听：

```text
http://127.0.0.1:7878
```

## 使用 CLI 验证

```powershell
dotnet run --project .\src\RevitCodexBridge.Cli -- health
dotnet run --project .\src\RevitCodexBridge.Cli -- doc
dotnet run --project .\src\RevitCodexBridge.Cli -- levels
dotnet run --project .\src\RevitCodexBridge.Cli -- wall-types
dotnet run --project .\src\RevitCodexBridge.Cli -- count OST_Walls
dotnet run --project .\src\RevitCodexBridge.Cli -- create-wall "标高 1" 0 0 5000 0 3000 --wall-type "常规 - 200mm"
dotnet run --project .\src\RevitCodexBridge.Cli -- run-json .\planner\commands\op-001.create_wall.json
dotnet run --project .\src\RevitCodexBridge.Cli -- run-plan .\planner\buildplan.generated.json
```

默认写操作都是 dry-run：

```powershell
dotnet run --project .\src\RevitCodexBridge.Cli -- create-wall "Level 1" 0 0 5000 0 3000
```

确认后加 `--write` 才会真正修改模型：

```powershell
dotnet run --project .\src\RevitCodexBridge.Cli -- create-wall "Level 1" 0 0 5000 0 3000 --write
```

## 为什么这样设计

Revit API 不能被 Codex、Python、Node 或普通外部进程随意直接调用。可靠路径是让 Revit add-in 在 Revit 进程内接收请求，再通过 `ExternalEvent` 进入 Revit API 上下文，并在 `Transaction` 中执行模型修改。

## 文档

- [完整使用说明](docs/usage-guide.md)
- [AI Planner 指南](docs/ai-planner-guide.md)
- [MCP Server 说明](src/RevitCodexBridge.Mcp/README.md)
- [GitHub 发布指南](docs/release-guide.md)
- [DeepSeek V4 Pro 与 Revit AI Skills](docs/deepseek-and-revit-skills.md)
- [贡献指南](CONTRIBUTING.md) 与 [安全策略](SECURITY.md)
