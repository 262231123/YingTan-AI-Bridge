# 安全策略

请不要在公开 issue 中披露可被利用的漏洞、API Key、客户模型或日志中的敏感数据。在仓库启用 GitHub Private vulnerability reporting 后，请通过“Security → Report a vulnerability”私密报告。

报告应包含受影响版本、复现步骤、影响和建议修复。发布者将在确认后协调修复与披露。

## 运行时安全边界

- 桥接服务默认只监听 `127.0.0.1`。
- 写操作默认 dry-run，真实写入需显式授权并由 Revit 内确认。
- 仅安装从可信 GitHub Release 获取且 SHA-256 校验一致的安装包。
