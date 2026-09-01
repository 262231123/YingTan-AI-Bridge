# 当前完成度与补充清单

## 已完成

- Revit 2027 Add-in 项目，目标框架 `net10.0-windows`。
- 本地桥接服务：`http://127.0.0.1:7878`。
- CLI：`revitctl.exe`。
- Dynamo Python 节点版本。
- AI Planner：自然语言/图片输入生成 BuildPlan JSON。
- BuildPlan 本地校验和命令导出。
- MCP server：通过 stdio 暴露 `revit_*` 工具，并转发到本地 Revit 桥。
- Revit 内写入确认：`dryRun=false` 时默认弹出确认框，确认后才进入 `Transaction`。
- 批量执行：`run_batch` 支持整组 BuildPlan/bridge commands dry-run、一次确认和原子回滚。
- Revit 2027 D 盘安装目录自动探测。
- Add-in manifest 安装脚本。
- 插件运行日志：

```text
%LOCALAPPDATA%\RevitCodexBridge\bridge.log
```

## 当前执行层支持的命令

查询类：

- `get_active_document`
- `list_levels`
- `list_wall_types`
- `list_family_symbols`
- `count_elements`

写入类：

- `create_wall`
- `set_parameter`
- `place_door`
- `place_window`
- `create_room`

写入类命令默认应保持 `dryRun=true`，确认后再写入。

## 需要在 Revit 内验证

这些必须在 Revit 2027 启动并打开模型后验证：

```powershell
.\src\RevitCodexBridge.Cli\bin\Debug\net10.0\revitctl.exe wait 60
.\src\RevitCodexBridge.Cli\bin\Debug\net10.0\revitctl.exe health
.\src\RevitCodexBridge.Cli\bin\Debug\net10.0\revitctl.exe doc
.\src\RevitCodexBridge.Cli\bin\Debug\net10.0\revitctl.exe levels
.\src\RevitCodexBridge.Cli\bin\Debug\net10.0\revitctl.exe wall-types
.\src\RevitCodexBridge.Cli\bin\Debug\net10.0\revitctl.exe door-types
.\src\RevitCodexBridge.Cli\bin\Debug\net10.0\revitctl.exe window-types
```

## 从自然语言到 Revit 的最短链路

生成 BuildPlan：

```powershell
python .\planner\revit_ai_planner.py plan `
  --prompt "在 Level 1 上创建一个 8000mm x 6000mm 的矩形房间，墙高 3000mm，先 dry-run。" `
  --out .\planner\buildplan.generated.json
```

校验：

```powershell
python .\planner\revit_ai_planner.py validate `
  --plan .\planner\buildplan.generated.json
```

执行 BuildPlan，默认仍会强制 dry-run：

```powershell
.\src\RevitCodexBridge.Cli\bin\Debug\net10.0\revitctl.exe run-plan .\planner\buildplan.generated.json
.\src\RevitCodexBridge.Cli\bin\Debug\net10.0\revitctl.exe run-plan .\planner\buildplan.generated.json --batch
```

真正写入需要两个条件同时满足：

- BuildPlan 中对应 operation 是 `dryRun=false`。
- CLI 命令加 `--write`。

```powershell
.\src\RevitCodexBridge.Cli\bin\Debug\net10.0\revitctl.exe run-plan .\planner\buildplan.generated.json --write
.\src\RevitCodexBridge.Cli\bin\Debug\net10.0\revitctl.exe run-plan .\planner\buildplan.generated.json --batch --write
```

## 仍然缺的生产化能力

- 操作审批 UI 进阶版：在 Revit 内显示完整 BuildPlan、风险、dry-run 结果和批量审批记录。
- 元素映射表：让 AI 使用稳定业务 ID，而不是直接猜 Revit ElementId。
- 更完整的构件命令：楼板、天花、柱、梁、轴网、标注、图纸、明细表。
- 图片矢量化增强：平面图墙线识别、比例尺校准、门窗符号识别。
- 测试模型与回归脚本：固定样板模型，验证每条命令行为。
- 日志查看命令：CLI 直接读取桥接日志和最近错误。

## 建议下一阶段

优先把 Revit 内确认从 `TaskDialog` 升级成 dockable panel：集中展示 BuildPlan、dry-run 差异、风险提示、元素预览和审批历史。随后补批量事务与元素映射表，让多步骤建模可以整体提交或整体回滚。
