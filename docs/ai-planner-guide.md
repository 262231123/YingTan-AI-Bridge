# AI Planner 使用说明

AI Planner 是自然语言/图片到 Revit 命令之间的规划层。它不会直接改模型，而是先生成一个可审查的 `BuildPlan`，再把其中已经支持的操作导出成 Add-in 桥接层或 Dynamo 版可以执行的 JSON 命令。

## 流程

```text
文字 / 图片
  -> OpenAI Responses API
  -> BuildPlan JSON
  -> 本地校验
  -> 导出 bridge command JSON
  -> Revit Add-in / Dynamo 执行
```

OpenAI 官方文档说明，最新模型支持文本和图片输入；Responses API 支持工具和结构化输出。因此这里使用多模态输入 + JSON Schema 结构化输出的方式生成建模计划。

参考：

- [Models](https://developers.openai.com/api/docs/models)
- [Images and vision](https://developers.openai.com/api/docs/guides/images-vision)
- [Structured Outputs](https://developers.openai.com/api/docs/guides/structured-outputs)
- [Function calling](https://developers.openai.com/api/docs/guides/function-calling)

## 文件

```text
planner/
  revit_ai_planner.py
  schemas/revit_build_plan.schema.json
  examples/simple_room.prompt.txt
  examples/simple_room.buildplan.json
```

## 1. 准备 API Key

PowerShell:

```powershell
$env:OPENAI_API_KEY = "你的 API Key"
```

可选指定模型：

```powershell
$env:OPENAI_MODEL = "gpt-5.5"
```

如果你想先看请求体而不真正调用 API，可以用 `--write-request`。

## 2. 从自然语言生成 BuildPlan

```powershell
python .\planner\revit_ai_planner.py plan `
  --prompt-file .\planner\examples\simple_room.prompt.txt `
  --out .\planner\buildplan.generated.json
```

直接写一句话也可以：

```powershell
python .\planner\revit_ai_planner.py plan `
  --prompt "在 Level 1 上创建一个 8000mm x 6000mm 的矩形房间，墙高 3000mm，先 dry-run。" `
  --out .\planner\buildplan.generated.json
```

## 3. 从图片 + 文字生成 BuildPlan

图片必须尽量包含比例尺或至少一个已知尺寸。没有尺度时，Planner 会把尺寸标为假设或降低置信度。

```powershell
python .\planner\revit_ai_planner.py plan `
  --prompt "根据这张户型图生成墙体建模计划。图中客厅开间为 4200mm，可用它作为比例参考。先 dry-run。" `
  --image .\samples\floorplan.png `
  --out .\planner\buildplan.from-image.json
```

支持多个图片：

```powershell
python .\planner\revit_ai_planner.py plan `
  --prompt "综合这些平面图和标注生成 Revit 建模计划。" `
  --image .\samples\plan-1.png `
  --image .\samples\plan-2.png `
  --out .\planner\buildplan.multi-image.json
```

## 4. 不调用 API，只生成请求 JSON

```powershell
python .\planner\revit_ai_planner.py plan `
  --prompt-file .\planner\examples\simple_room.prompt.txt `
  --write-request .\planner\openai-request.preview.json
```

这适合检查 prompt、图片 data URL 和 JSON Schema 是否正确。

## 5. 校验 BuildPlan

```powershell
python .\planner\revit_ai_planner.py validate `
  --plan .\planner\examples\simple_room.buildplan.json
```

校验会检查：

- 顶层字段是否完整。
- 目标版本是否为 Revit 2027。
- 单位是否为 mm。
- `create_wall` 是否有起点、终点、高度和标高。
- `set_parameter` 是否有元素 ID 和参数名。

## 6. 导出可执行命令

导出 dry-run 命令：

```powershell
python .\planner\revit_ai_planner.py export-commands `
  --plan .\planner\examples\simple_room.buildplan.json `
  --out-dir .\planner\commands
```

生成结果类似：

```text
planner\commands\op-001.create_wall.json
planner\commands\op-002.create_wall.json
planner\commands\op-003.create_wall.json
planner\commands\op-004.create_wall.json
```

如果 BuildPlan 中某些操作明确是 `dryRun=false`，并且你确认要导出写入命令：

```powershell
python .\planner\revit_ai_planner.py export-commands `
  --plan .\planner\buildplan.generated.json `
  --out-dir .\planner\commands `
  --write
```

## 7. 接到 Add-in 桥接层

Add-in 版本接收单条命令 JSON。导出后，可以直接用 `revitctl run-json` 或 `revitctl run-dir` 发给本地桥接服务。

执行单条命令：

```powershell
dotnet run --project .\src\RevitCodexBridge.Cli -- run-json .\planner\commands\op-001.create_wall.json
```

按文件名顺序执行整个目录：

```powershell
dotnet run --project .\src\RevitCodexBridge.Cli -- run-dir .\planner\commands
```

当前可直接执行的命令类型：

- `get_active_document`
- `list_levels`
- `list_wall_types`
- `list_family_symbols`
- `count_elements`
- `create_wall`
- `set_parameter`
- `place_door`
- `place_window`
- `create_room`

其他类型，例如 `create_floor`、`create_level`，Planner 可以先规划，但执行层还需要继续扩展。

也可以跳过导出目录，直接运行 BuildPlan：

```powershell
dotnet run --project .\src\RevitCodexBridge.Cli -- run-plan .\planner\buildplan.generated.json
```

默认强制 dry-run；真正写入再加：

```powershell
dotnet run --project .\src\RevitCodexBridge.Cli -- run-plan .\planner\buildplan.generated.json --write
```

推荐在多操作计划中使用批量模式。批量模式会把整个 BuildPlan 作为一个 `run_batch` 请求发送到 Revit，写入时默认使用 `TransactionGroup`，任一操作失败则整体回滚：

```powershell
dotnet run --project .\src\RevitCodexBridge.Cli -- run-plan .\planner\buildplan.generated.json --batch
dotnet run --project .\src\RevitCodexBridge.Cli -- run-plan .\planner\buildplan.generated.json --batch --write
```

可选项：

- `--continue-on-error`：继续执行后续操作，便于一次收集多个错误。
- `--non-atomic`：关闭整体事务组，按单条命令提交；生产模型中谨慎使用。

## 8. 安全建议

- 图片识别到的尺寸不要直接用于施工级建模，必须有比例尺或已知尺寸校准。
- 默认使用 dry-run。
- 首次执行只在测试模型中运行。
- 对包含 `dryRun=false` 的计划先人工审查。
- 批量建模前先保存 Revit 文件副本。
