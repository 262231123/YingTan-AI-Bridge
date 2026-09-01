# Revit 2027 Codex 控制方案

## 目标

把 Codex 的自然语言意图变成可审计、可回滚、可白名单控制的 Revit 操作。第一版采用本地桥接：

```text
Codex -> revitctl / MCP -> 127.0.0.1:7878 -> Revit Add-in -> ExternalEvent -> Revit API
```

## Revit 2027 技术基线

- 插件目标框架：`net10.0-windows`。
- Revit API 引用路径默认：`C:\Program Files\Autodesk\Revit 2027`。
- 如果 C 盘不存在，会自动探测：`D:\Program Files\Autodesk\Revit 2027`。
- 也可通过环境变量覆盖：`REVIT_2027_DIR=C:\Program Files\Autodesk\Revit 2027`。
- 所有 Revit API 写操作必须在 Revit 进程内执行，并通过 `Transaction` 提交。
- 本地 HTTP 服务只负责收 JSON、排队、返回结果；不在网络线程直接碰 Revit API。

## 第一版命令

- `get_active_document`：查询当前模型标题、路径、活动视图。
- `list_levels`：列出标高。
- `list_wall_types`：列出墙类型。
- `count_elements`：统计元素，可按 `BuiltInCategory` 过滤。
- `set_parameter`：按元素 id 修改参数，默认 `dryRun`。
- `create_wall`：按毫米坐标创建直墙，默认 `dryRun`。

## 安全策略

- 服务只绑定 `127.0.0.1`。
- 默认不写模型，写操作需要 `--write` 或 `dryRun=false`。
- 命令白名单，不执行任意 C#、Python 或 Dynamo 脚本。
- 参数修改前返回 `before/proposed/after`。
- 后续需要加操作日志、项目级权限、事务组和撤销策略。

## 下一步

1. 安装 .NET SDK 10.0.100 或更新版本。
2. 安装完整 Revit 2027，并确认 `RevitAPI.dll` 与 `RevitAPIUI.dll` 在安装目录。
3. `dotnet build` 编译插件和 CLI。
4. 运行 `deploy\install-addin.ps1` 写入 `.addin` manifest。
5. 重启 Revit 2027，打开模型。
6. 使用 `revitctl health`、`revitctl doc`、`revitctl levels` 验证桥接。
