# v0.7.6-preview

新增设备钢结构平台的直接创建流程。

- 用户明确要求跳过预览且已确定分跨数时，AI 使用 `create_steel_platform_direct`。
- 直接创建不再生成或依赖 `previewId`，会重新核实轴网、标高、结构族类型、梁高限制和平台尺寸。
- `bays` 只接受 1、2 或 3，构件布置与原有预览方案使用同一套几何生成器。
- 写入安全流程不变：先在 Revit 事务内使用真实族试建、验证顶高并回滚；用户点击“执行计划”后才正式提交。

原有 `preview_steel_platform` → `create_steel_platform` 流程仍保留，供需要比较 1/2/3 跨方案时使用。
