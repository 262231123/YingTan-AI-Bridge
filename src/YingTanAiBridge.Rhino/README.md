# 盈碳 Rhino AI Bridge

此版本基于本机 Rhino 8 的 `RhinoCommon.dll` 与 `Eto.dll` 构建。加载后运行 Rhino 命令 `YingTanAiBridge`，即可打开盈碳 AI 对话窗。

本机安装与更新：

```powershell
.\deploy\install-rhino-8.ps1
```

安装后重启 Rhino 8，运行 `YingTanAiBridge`。点击窗口右上角“配置”，可直接维护共享的模型、API Key、Agent 和 Skill，并测试当前提供商连接。插件支持模型上下文查询以及经过确认的点、线、圆和图层操作。
