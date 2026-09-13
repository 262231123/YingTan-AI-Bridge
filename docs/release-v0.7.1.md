# v0.7.1-preview — Revit 2026真实宿主测试与文档令牌修复

修复同一Revit文档在不同ExternalEvent调用中产生不同文档令牌的问题。令牌现在按Document.CreationGUID登记，并在文档重新打开时更新；脚本修订计数使用同一稳定身份，避免查询、预检和正式提交被误判为切换文档。

已在本机Revit 2026.5与Autodesk自带Structural Analysis模板完成真实宿主烟雾测试：查询脚本编译/审阅/返回数据成功；四面墙写脚本试建成功并回滚，墙数保持0；正式提交创建4面6000/4000mm长、3000mm高的常规200mm墙，ElementId、墙型、标高、尺寸、注释回读正确；已提交脚本不能重放。测试模板未保存，不涉及用户正式项目。

便携回归新增“同文档令牌稳定、重新打开令牌更新”检查，由GitHub CI运行。外部测试进程受Windows策略限制，未调用加密的DeepSeek配置；最终自然语言Agent规划需在Revit聊天面板内用发布版继续验收。

EXE包含Revit 2026.5（26.5.0.55/.NET10）和AutoCAD 2024组件，未签名。其他宿主版本未验证。MIT许可证及第三方通知随安装包提供。

[对话脚本使用说明与限制](https://github.com/262231123/YingTan-AI-Bridge/blob/master/docs/live-scripting.md)
