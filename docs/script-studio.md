# Revit 脚本工作室

脚本工作室可在 Revit 聊天面板中根据提示词生成以下工件：

- C# `IExternalCommand` 源文件（`.cs`）
- pyRevit Python 脚本（`.py`）
- Dynamo Python 节点脚本（`.py`）
- 基础 Dynamo 图（`.dyn`）

例如：

```text
为 Revit 2026 编写一个 pyRevit 脚本，把当前选择墙的“注释”参数设置为“待复核”。
```

```text
生成 Dynamo Python 脚本，输入房间列表，输出名称、编号和面积，不修改模型。
```

生成结果保存在：

```text
%LOCALAPPDATA%\RevitCodexBridge\script-studio
```

每个工件旁会写入 `.metadata.json`，记录格式、目标 Revit 版本、依赖、SHA-256、检查提示和 `executed=false`。

## 安全边界

当前版本只生成、检查和保存，不会自动编译或执行。规则型静态检查会阻止网络访问、外部进程、PowerShell/cmd、注册表、文件删除、动态程序集加载，以及缺少事务保护的常见写入代码。静态检查不能证明代码完全安全或正确；请先人工审阅，再在模型副本中测试。

C# 工件还必须实现 `IExternalCommand.Execute` 并声明 `TransactionAttribute`；Dynamo Python 必须提供 `IN/OUT`；`.dyn` 必须包含 `Nodes`、`Connectors` 和 `View`。单个工件最大 500 KB。

`.dyn` 生成适合基础图和模板起点。不同 Dynamo/Revit 版本的节点签名可能变化，打开后应检查缺失节点和依赖包。

为降低完整代码被截断的概率，脚本工作室单次生成会临时采用至少 8192 tokens、120 秒超时；不会修改用户保存的模型接口参数。
