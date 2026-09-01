# 盈碳 AutoCAD AI Bridge

该工程是 AutoCAD 宿主适配器。它与 Revit、Rhino、Inventor 版本共享 `YingTanAiBridge.Contracts` 的 Agent/Skill 操作协议和 Dry-run 确认策略。

当前目标版本为 AutoCAD 2022（.NET Framework 4.8）。本机安装与更新：

```powershell
.\deploy\install-autocad-2022.ps1
```

安装后重启 AutoCAD 2022，运行 `YINGTANAIBRIDGE`。点击面板右上角“配置”，可直接维护共享的模型、API Key、Agent 和 Skill，并测试当前提供商连接。配置保存在 `%LOCALAPPDATA%\RevitCodexBridge\ai-settings.json`，API Key 使用 Windows DPAPI 加密。
