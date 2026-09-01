# 呆猫工作室精装 Skill

## 调用流程

1. 使用 `finish_toolkit_info`，`action=status` 检查插件与 AI API 是否加载。
2. 使用 `finish_toolkit_info`，`action=readiness` 检查房间、类型、材质、CAD 和当前选择。
3. 直接动作或面板动作统一通过 `finish_toolkit_run` 调用。
4. `finish_toolkit_run` 必须先 Dry-run，再由用户确认写入或打开配置面板。

## 直接动作

| action | 功能 | 影响 |
| --- | --- | --- |
| `number_parts` | 零件自动编号 | 写入墙、楼板零件及幕墙嵌板编号 |
| `create_arrangement_views` | 生成零件排布图 | 创建视图、标记和图纸 |
| `create_description_statistics` | 生成说明统计 | 创建汇总表和分组明细图纸 |
| `check_ceiling_light_text` | 天花点位校验 | 检查文字并标红不一致项 |

## 配置面板动作

| action | 打开的呆猫工具 |
| --- | --- |
| `open_finish_builder` | 一键硬装 |
| `open_finish_layers` | 饰面层工具 |
| `open_material_tools` | 材质工具 |
| `open_interior_plan_sheets` | 内装平面图 |
| `open_interior_elevation_sheets` | 内装立剖面 |
| `open_auto_dimension` | 自动标注 |
| `open_material_tags` | 材质标记 |
| `open_annotation_tools` | 注释工具 |
| `open_plan_layout` | AI 平面方案 |
| `open_soft_furnishing` | 一键软装 |
| `open_electrical_layout` | 开关灯具 |
| `open_type_rename` | 族类型改名 |

配置面板动作只负责打开原插件界面。房间、材料、视图和规则仍由用户在呆猫面板中确认，避免 AI 猜测高影响参数。
