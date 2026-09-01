# 盈碳 Autodesk Inventor AI Bridge

该工程用于 Autodesk Inventor 的 AddIn 适配。所有写入类 Agent/Skill 操作会先经过共享的 Dry-run 与显式确认策略。

未安装 Inventor 的机器默认仅构建共享协议验证库。安装 Inventor SDK 后设置目录并启用宿主编译：

```powershell
$env:INVENTOR_INSTALL_DIR = 'C:\Program Files\Autodesk\Inventor 2025'
dotnet build .\src\YingTanAiBridge.Inventor\YingTanAiBridge.Inventor.csproj -c Release -p:EnableInventorSdk=true
```

最终发布时，需要随目标版本测试 AddIn manifest 注册、COM 互操作和 Inventor 用户界面集成。
