# v0.7.0-preview — 对话脚本补充查询与操作

原生命令不足时，助手现在可生成并编译C#查询脚本，获取真实数据后继续规划，再用写入脚本补充Revit操作。编译/API错误会返回AI修正，不再仅因缺少专用命令直接拒绝。

新增prepare_revit_script、query_revit_script、execute_revit_script与内置“对话脚本补充查询与操作”Skill。查询、试运行、正式提交分别强制完整源码审阅；写入先事务试运行并回滚，文档变化使预检失效，成功提交后ID不可重放。保留只生成文件、Dynamo和pyRevit工件流程。

重要：进程内脚本不是安全沙箱，静态检查不能保证安全，回滚不覆盖外部副作用。仅运行审阅后可信代码，先保存并使用项目副本。脚本不能绕过系统应用程序控制或结构工程验算。

本机插件及便携回归项目编译成功；回归通过GitHub CI验证，未完成真实Revit脚本端到端及安装器交互验收。EXE包含Revit2026.5（26.5.0.55/.NET10）与AutoCAD2024组件及Roslyn编译依赖，未签名。其他宿主版本未验证。MIT许可证，依赖遵循其各自许可证。

[完整使用说明与限制](https://github.com/262231123/YingTan-AI-Bridge/blob/master/docs/live-scripting.md)
