# Dynamo 版本方案

这是一条不写 Revit add-in 的替代路线：用 Dynamo for Revit 2027 里的 CPython3/PythonNet3 节点执行 Revit API 操作。

## 适用场景

适合：

- 快速验证 Codex 控制 Revit 的命令格式。
- 给 BIM 工程师一个能在 Dynamo Player 里看见、运行、停用的方案。
- 项目里不方便先部署 C# add-in，但允许运行 Dynamo 图。
- 批量查询、轻量创建、参数写入、模型检查。

不适合：

- 长期后台监听。
- 无人值守的实时控制。
- 高频连续操作。
- 复杂事务、失败恢复、权限审计。

如果目标是稳定生产化，C# add-in 桥接仍然是主路线；Dynamo 版更像是低门槛原型和半自动工具。

## Revit 2027 注意点

Revit 2027 的 Dynamo 更新到 Dynamo 4.0.2，并且 CPython 节点会迁移到 PythonNet3。这里提供的脚本按 CPython3/PythonNet3 写法准备。

## 文件结构

```text
dynamo/
  RevitCodexBridge_Dynamo.py
  examples/
    health.json
    get_active_document.json
    list_levels.json
    list_wall_types.json
    count_walls.json
    create_wall.dryrun.json
    create_wall.write.json
    set_parameter.dryrun.json
    set_parameter.write.json
```

## Dynamo 图怎么搭

在 Revit 2027 中打开 Dynamo，创建一个新图：

1. 新建一个 `String` 节点，填命令 JSON 文件路径，例如：

```text
C:\Users\xuyin\Documents\AI接入REVIT\dynamo\examples\list_levels.json
```

2. 新建第二个 `String` 节点，填结果输出路径，例如：

```text
C:\Users\xuyin\Documents\AI接入REVIT\dynamo\result.json
```

3. 新建一个 `Boolean` 节点，命名理解为 `Allow Writes`。

```text
false
```

4. 新建一个 `Python Script` 节点，Python engine 选择 CPython3。
5. 把 [RevitCodexBridge_Dynamo.py](../dynamo/RevitCodexBridge_Dynamo.py) 的内容复制进 Python 节点。
6. 将三个输入连接到 Python 节点：

```text
IN[0] = command JSON path
IN[1] = result JSON path
IN[2] = allow writes
```

7. 保存 Dynamo 图，例如：

```text
C:\Users\xuyin\Documents\AI接入REVIT\dynamo\RevitCodexBridge_Dynamo.dyn
```

后续可以用 Dynamo Player 运行这个图。

## 运行查询命令

查询当前模型：

```text
dynamo\examples\get_active_document.json
```

列出标高：

```text
dynamo\examples\list_levels.json
```

统计墙：

```text
dynamo\examples\count_walls.json
```

执行后结果会写入你在 `IN[1]` 指定的 `result.json`。

## 创建墙体

先 dry-run：

```text
dynamo\examples\create_wall.dryrun.json
```

确认结果后，换成写入命令：

```text
dynamo\examples\create_wall.write.json
```

同时必须把 Dynamo 图里的 `Allow Writes` Boolean 改成：

```text
true
```

这是双重保险：JSON 里要 `dryRun=false`，Dynamo 图里也要允许写入。

## 修改参数

先编辑示例里的元素 ID 和参数名：

```json
{
  "command": "set_parameter",
  "elementId": 123456,
  "parameterName": "Comments",
  "value": "由 Codex Dynamo 写入",
  "dryRun": true
}
```

确认 dry-run 输出后，把 JSON 改为：

```json
{
  "command": "set_parameter",
  "elementId": 123456,
  "parameterName": "Comments",
  "value": "由 Codex Dynamo 写入",
  "dryRun": false
}
```

并把 `Allow Writes` 改成 `true`。

## Codex 怎么配合

最简单的工作流：

1. Codex 根据你的自然语言生成或修改一个 JSON 命令文件。
2. 你在 Dynamo Player 里运行 `RevitCodexBridge_Dynamo.dyn`。
3. Codex 读取 `result.json`，继续生成下一步命令。

这不是实时控制，但安全、透明，BIM 人员也容易理解。

## 和 Add-in 方案对比

| 能力 | Dynamo 版 | Add-in 桥接版 |
| --- | --- | --- |
| 部署难度 | 低 | 中 |
| 是否需要编译 | 不需要 | 需要 .NET SDK / Revit API |
| 调用方式 | 手动运行 Dynamo 图 | Codex/CLI/MCP 直接调用 |
| 后台监听 | 不推荐 | 支持 |
| 写操作安全 | Boolean + dryRun | dryRun + 白名单 + Transaction |
| 生产稳定性 | 中 | 高 |
| 适合人群 | BIM/Dynamo 用户 | 软件/BIM 二开团队 |

## 建议

先用 Dynamo 版验证命令格式和业务场景，确认哪些操作最有价值；稳定后，把高频、关键、需要审计的命令迁移到 C# add-in 桥接层。

