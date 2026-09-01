# AI Revit 自动化脚本

盈碳 Revit AI Bridge 将 AI 的执行意图保存为本地 JSON 自动化脚本，而不是将任意 C# 代码直接注入 Revit 进程。脚本位于：

```text
%LOCALAPPDATA%\RevitCodexBridge\automation-scripts
```

普通 Revit 查询会自动执行并返回结果。创建、替换、修改、删除等写入脚本先以 Dry-run 预演，只有用户在聊天面板中点击“执行计划”并确认后才会写入模型。

## 路由规则

- 常规 Revit 意图优先使用原生桥接命令。
- 只有用户明确提到“呆猫、精装、硬装、软装、Finish Toolkit 或 FinishParts”时，才会调用精装插件 Skill。
- 精装插件缺少功能不会阻止普通 Revit 自动化执行。

## 三层系统墙示例

AI 可生成 `create_compound_wall_type` 脚本，参数包括原墙类型、目标墙类型和构造层。可选的 `replaceSourceWallTypeName` 会在同一事务内将匹配的现有墙实例替换为新类型。

```json
{
  "operations": [
    {
      "id": "op-001",
      "command": "create_compound_wall_type",
      "sourceWallTypeName": "Existing 100mm Wall",
      "newWallTypeName": "AI Partition 100mm",
      "layers": [
        { "materialName": "Wood Finish", "thicknessMm": 30, "function": "Finish1" },
        { "materialName": "Light Gauge Steel", "thicknessMm": 50, "function": "Structure" },
        { "materialName": "Tile", "thicknessMm": 20, "function": "Finish2" }
      ],
      "replaceSourceWallTypeName": "Existing 100mm Wall"
    }
  ]
}
```
