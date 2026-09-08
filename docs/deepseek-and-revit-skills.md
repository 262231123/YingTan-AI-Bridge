# DeepSeek V4 Pro 与 Revit AI Skills

## DeepSeek V4 Pro

插件内置了以下默认配置：

| 配置 | 值 |
|---|---|
| 提供商 | DeepSeek V4 Pro |
| Base URL | `https://api.deepseek.com` |
| 模型 | `deepseek-v4-pro` |
| API 模式 | OpenAI-compatible Chat Completions |
| 默认超时 | 120 秒 |
| 默认最大输出 | 8192 tokens |

DeepSeek 官方文档说明 V4 Pro 使用 `deepseek-v4-pro` 模型名，并保持 OpenAI Chat Completions 兼容接口。API Key 仅在当前 Windows 用户下用 DPAPI 加密保存，不会写入仓库或安装包。

参考：

- [DeepSeek 首次 API 调用](https://api-docs.deepseek.com/)
- [DeepSeek API 更新日志](https://api-docs.deepseek.com/updates)

## Revit AI Skill 调研与取舍

公开 Revit MCP 项目已覆盖项目信息、模型分析、构件 CRUD、批处理、视图与图纸、质量检查和数据导出等方向。操作技能也强调单位、ElementId、先查询后写入、破坏性操作确认和如实报告工具限制。

本项目新增的内置 Skills：

- 模型健康审计：整合项目、标高、墙体、视图、图纸和明细表检查。
- 参数治理：先识别构件与参数类型，再生成最小写入计划。
- 空间与房间规划：核实标高、坐标、边界、名称和编号。
- 门窗宿主协调：先查族类型和墙宿主，再放置门窗。
- 交付预检：核对视图、图纸、明细表和未放置内容。
- 安全批量执行：先 Dry-run，再用原子批次和整体回滚保护模型。

下列社区能力尚未映射为本插件命令，因此没有仅通过 Skill 提示词伪装支持：真实冲突检测、规范自动合规、PDF/DWG/IFC 导出、族编辑、MEP 系统路由、结构分析与任意构件删除。

调研参考：

- [LuDattilo/revit-mcp-server](https://github.com/LuDattilo/revit-mcp-server)
- [shuotao/REVIT_MCP_study](https://github.com/shuotao/REVIT_MCP_study)
- [AUTOM8LABS/mcp-connector-skills](https://github.com/AUTOM8LABS/mcp-connector-skills)
