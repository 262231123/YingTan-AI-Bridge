# v0.7.5-preview

修复 AI 生成 `prepare_revit_script` 时遗漏 `mode` 导致连续校验失败的问题。

- 明确的只读查询脚本遗漏 `mode` 时，宿主安全补全为 `query`。
- 可用 `description` 补全缺失的 `purpose`；缺少 `code` 或 `scriptId` 时在进入 Revit 前给出明确错误。
- 含糊的缺省模式不会被猜测为写入，必须由 AI 明确选择 `query` 或 `write`。
- 轴网区域和结构类型已读取时，引导 AI 复用 `depthMm` / `thicknessMm` 并直接进入 `preview_steel_platform`，不再用脚本重复查询族截面。

所有写入安全边界保持不变：查询脚本不在写事务内运行，写入脚本仍需源码审阅、试运行回滚和正式提交确认。
