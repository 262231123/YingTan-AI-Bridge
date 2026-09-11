# 对话脚本补充查询与操作（0.7.0预览版）

## 为什么以前说有脚本却仍然拒绝

旧版“脚本工作室”只生成、静态检查并保存代码文件，未连接Revit运行器。聊天规划器也只能选择固定原生命令。现在增加了编译与宿主执行链，AI可以在原生命令不足时主动使用C#补充能力，不必要求你重新提出“生成脚本”。

## 使用

关闭Revit并安装新版，在项目副本中新建对话，例如：

> 请先查询指定区域构件的类型、位置和参数。原生命令查不到的信息，请生成查询脚本补充。根据真实数据和我的设计条件制定方案，必要时编写操作脚本；先试运行，确认后再提交。

流程如下：

1. AI优先查询已有原生命令。字段或操作不足时，生成C#方法体并调用`prepare_revit_script`编译。
2. 查询脚本调用`query_revit_script`：弹出完整源码、目的、当前文档和SHA-256，勾选“已审阅并信任”后运行。结果作为数据返回AI，继续查询或澄清必要条件。
3. 写入脚本调用`execute_revit_script`预检：先审阅源码，在宿主管理的事务中运行，检查Revit提交是否成功，再回滚整个试运行。警告和错误均保守地视为失败；临时构件ID不作为有效ID返回。
4. 预检通过后点击聊天中的“执行计划”，再次审阅同一脚本后正式提交。文档修改会使预检失效，必须重新预检；执行成功后的脚本ID不能再次提交。
5. 返回的构件ID用于原生命令回读，AI核验后说明已完成、未完成及待确认事项。

源码/编译/API错误会返回AI，单轮最多3次失败修正、8步规划。点击取消会停止本轮，不会因取消立即改写代码继续请求执行。跨轮保留脚本查询结果，但脚本本身只缓存于当前Revit进程内，绑定当前文档，20分钟失效，最多20个；重启需重新生成。

“自动创建设备平台”开关不授权任何脚本。即使设置关闭普通写入确认，脚本源码确认仍不可跳过。

## 支持与不支持

支持使用当前安装Revit API编译C#方法体，读取当前文档与已加载链接数据、组合几何查询、批量参数或API支持的建模操作。不是“任意想法都能自动实现”：仍受实际Revit API、族、模型约束、设计输入及代码限制影响。

C#正文有`doc`变量，宿主提供System、System.Linq、System.Collections.Generic、System.Text.Json、Autodesk.Revit.DB和Autodesk.Revit.DB.Structure命名空间。不要写using指令、类、完整ExternalCommand或事务。

查询例子：

```csharp
return JsonSerializer.Serialize(
    new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
        .Take(100).Select(x => new { id = x.Id.Value, name = x.Name, elevationMm = x.Elevation * 304.8 })
        .ToArray());
```

写入脚本必须先核实并检查目标/类型/参数，返回普通JSON，例如`createdElementIds`或`modifiedElementIds`。查询模式不启动写事务，正常模型写入将失败。正式运行发生错误则回滚当前脚本事务；批次还由原有原子事务组保护。

显式说“只生成、不执行”“导出脚本”，或请求Dynamo/pyRevit/Python工件，仍走文件生成流程。新版只执行C#方法体，不直接运行`.dyn`、Python或任意DLL。

## 安全与测试边界

**进程内C#不是隔离沙箱，静态检查不是安全证明。** 只有在完整审阅并信任代码后才允许运行。查询结果会发送给你配置的AI供应商，不应查询超出任务范围的敏感项目信息。

默认拒绝文件、网络、进程、反射、UI、外部程序集、异步线程、自建事务等常见危险入口，也禁止预处理、本地函数、捕获异常、unsafe和goto。源码限制24,000字符，JSON结果限制64,000字符。循环插入20,000次/10秒协作预算，但不能强制中断单次耗时Revit API、LINQ调用，也没有进程级内存隔离。事务回滚只能撤销当前文档的模型修改，不能撤销外部副作用；勿将其当作执行不可信代码的保障。

如Windows应用程序控制、企业策略或宿主拒绝加载动态程序集，会报告真实错误；不要关闭系统防护作为绕过方式。日志记录脚本哈希/阶段，当前对话内存保留源码和查询数据，聊天消息也可能包含查询结果，请按项目保密要求管理。

本机Revit2026.5/.NET10插件及便携测试项目可编译；本机策略阻止测试程序运行，因此便携用例在GitHub CI验证。便携测试使用最小Document替身，不代表真实Revit API端到端验收。未完成真实Revit项目中的脚本审阅窗口、试运行和提交验收，必须先在副本试用。

结构设计仍需独立工程验算，脚本执行能力不意味着承载力、安全施工或优化结果已经验证。

实现依据：[Microsoft明确说明AssemblyLoadContext不提供安全功能](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.loader.assemblyloadcontext?view=net-10.0)、[Autodesk事务规则](https://help.autodesk.com/cloudhelp/2024/ENU/Revit-API/files/Revit_API_Developers_Guide/Basic_Interaction_with_Revit_Elements/Revit_API_Revit_API_Developers_Guide_Basic_Interaction_with_Revit_Elements_Transactions_html.html)。
