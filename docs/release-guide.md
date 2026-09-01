# GitHub 发布指南

## 发布前检查

1. 确认根目录 `LICENSE` 中的 MIT 版权人和年份信息正确。
2. 确认 `.gitignore` 已排除 `artifacts/`、`.tmp/`、`bin/`、`obj/`、`.env*` 和签名密钥。
3. 不要提交 RevitAPI.dll、RevitAPIUI.dll 或其他第三方 SDK 程序集。
4. 运行 CI 同等的可移植组件验证，并在对应版本 Revit 内完成 dry-run 与写入冒烟测试。
5. 确认 README 的功能和限制与当前代码一致。

## 生成 Revit 安装包

在安装了目标 Revit 版本的 Windows 主机上运行：

```powershell
.\deploy\build-release.ps1 -Version 0.1.0 -RevitVersion 2026
```

当前默认映射为：Revit 2020–2024 使用 `net48`，Revit 2025 使用 `net8.0-windows`，Revit 2026–2027 使用 `net10.0-windows`。该映射与当前 Revit API 程序集保持一致；Autodesk 更新 API 运行时后应重新验证。

如果 Revit 不在默认目录：

```powershell
.\deploy\build-release.ps1 -Version 0.1.0 -RevitVersion 2027 `
  -RevitInstallDir "D:\Autodesk\Revit 2027"
```

脚本会在 `release/` 下生成 ZIP 和对应 `.sha256` 文件。ZIP 包含插件二进制、安装/卸载脚本与使用说明，不包含 Autodesk API DLL。

## 生成 Windows 一键 EXE 安装包

先安装 Inno Setup 6，然后运行：

```powershell
winget install JRSoftware.InnoSetup
.\deploy\build-windows-installer.ps1 -Version 0.1.0
```

脚本会检测当前构建机上的 Revit 2020–2027、Rhino 7–8 和 AutoCAD 2022–2027，仅将成功构建的版本写入 EXE。安装时再次检测用户机器，在组件页显示可多选的已安装宿主版本。Inventor 源码尚未完成 AddIn manifest/COM 注册，在产出有效 payload 前不会在安装器中开放选择。

## GitHub 步骤

1. 创建空仓库，建议默认分支使用 `main`。
2. 首次提交前运行 `git status --ignored` 复核跟踪范围。
3. 推送源码，等待 GitHub Actions CI 通过。
4. 创建 `v0.1.0` 等标签和 Release，上传 ZIP 与 `.sha256`。
5. 在 Release Notes 中写明支持的 Revit 版本、已知限制和冒烟测试结果。
