# v0.5.0-preview — 对话式设计 Agent

新增实时模型上下文、连续查询与规划、基于真实错误的计划修正、写入结果回读和只读核验。支持 @选择集，新增构件分页检索、警告查询、矩形房间（四墙+房间）创建命令及3个实际工作流 Skill。

修复批次失败仍显示预检成功、旧计划被重复执行、请求超时后仍留在队列等问题；计划绑定文档会话并提供停止按钮。

验证：Revit 插件 Release 构建与21项代理回归断言通过，CI覆盖无需宿主的代理测试。尚未在Revit中完成真实模型/真实AI调用端到端测试或安装器交互验收。

EXE包含本机构建的 Revit 2026.5（26.5.0.55 / .NET 10）和 AutoCAD 2024 组件。其他Revit小版本未验证。安装包未签名。MIT许可证。

使用说明与竞品调研：[对话式设计 Agent](https://github.com/262231123/YingTan-AI-Bridge/blob/master/docs/conversational-design.md)。
