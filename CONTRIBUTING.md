# 贡献指南

感谢你参与改进盈碳 AI Bridge。请先创建 issue 说明问题、宿主软件及版本，再提交聚焦于单一目标的 pull request。

## 本地验证

```powershell
dotnet build .\src\RevitCodexBridge.Cli\RevitCodexBridge.Cli.csproj
dotnet build .\src\RevitCodexBridge.Mcp\RevitCodexBridge.Mcp.csproj
python .\planner\revit_ai_planner.py validate --plan .\planner\examples\simple_room.buildplan.json
```

Revit、AutoCAD、Rhino 和 Inventor 插件需要本机安装对应宿主及 SDK 程序集。提交涉及写模型的改动时，请附上 dry-run 结果和测试模型中的实测结果。不得提交 Autodesk/Rhino 等第三方 SDK DLL、API Key、本地配置或客户模型。
