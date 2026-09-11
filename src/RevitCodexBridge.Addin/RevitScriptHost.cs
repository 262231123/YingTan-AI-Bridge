using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Microsoft.CodeAnalysis;

namespace RevitCodexBridge.Addin;

internal static class RevitScriptHost
{
    private sealed class Revision { public long Value; }
    private sealed class Prepared
    {
        public required string Token, Code, Hash, Mode, Purpose;
        public required byte[] Assembly;
        public DateTime Created = DateTime.UtcNow;
        public long? CheckedRevision;
    }
    private static readonly ConditionalWeakTable<Document, Revision> Revisions = new();
    private static readonly Dictionary<string, Prepared> Scripts = new(); // Revit ExternalEvent thread only.
    public static void DocumentChanged(object? sender, DocumentChangedEventArgs e) => Revisions.GetValue(e.GetDocument(), _ => new Revision()).Value++;

    public static object Prepare(Document doc, JsonElement p)
    {
        var mode = p.GetRequiredString("mode");
        if (mode is not "query" and not "write") throw new InvalidOperationException("脚本mode必须为query或write。");
        var purpose = p.GetRequiredString("purpose");
        if (string.IsNullOrWhiteSpace(purpose) || purpose.Length > 1000) throw new InvalidOperationException("用途说明须为1–1000字符。");
        var code = p.GetRequiredString("code");
        var binary = RevitScriptCompiler.Compile(code, References());
        foreach (var key in Scripts.Where(s => DateTime.UtcNow - s.Value.Created > TimeSpan.FromMinutes(20)).Select(s => s.Key).ToArray()) Scripts.Remove(key);
        while (Scripts.Count >= 20) Scripts.Remove(Scripts.OrderBy(s => s.Value.Created).First().Key);
        var id = Guid.NewGuid().ToString("N");
        var script = new Prepared { Token = DesignAgentTools.Token(doc), Code = code, Hash = RevitScriptCompiler.Hash(code), Mode = mode, Purpose = purpose, Assembly = binary };
        Scripts.Add(id, script);
        BridgeLog.Info($"Script compiled {id} {script.Hash} {mode}; not executed.");
        return new { scriptId = id, sha256 = script.Hash, mode, purpose, compiled = true, executed = false,
            nextCommand = mode == "query" ? "query_revit_script" : "execute_revit_script",
            note = "仅完成编译，未执行。所有脚本执行都强制源码审阅；脚本不是安全沙箱。ID限本次Revit会话、当前文档，20分钟失效。" };
    }

    public static object Query(Document doc, JsonElement p)
    {
        if (doc.IsModifiable) throw new InvalidOperationException("查询脚本不能在已有写事务内执行。");
        var script = Get(doc, p, "query");
        ScriptReviewWindow.Require(script.Purpose, script.Code, script.Hash, "查询", doc.Title);
        // No transaction: normal model writes fail. This is not an OS/process sandbox.
        var data = Invoke(doc, script);
        BridgeLog.Info($"Script query completed {p.GetRequiredString("scriptId")} {script.Hash}");
        return new { executed = true, mode = "query", scriptId = p.GetRequiredString("scriptId"), data };
    }

    public static object Execute(Document doc, JsonElement p)
    {
        var script = Get(doc, p, "write");
        if (doc.IsReadOnly || doc.IsFamilyDocument || doc.IsModifiable) throw new InvalidOperationException("写脚本需要未处于事务内的可写项目文档。");
        var dryRun = p.GetOptionalBoolean("dryRun", true);
        var revision = Revisions.GetValue(doc, _ => new Revision());
        if (!dryRun && script.CheckedRevision != revision.Value) throw new InvalidOperationException("脚本未试运行，或试运行后文档发生变化；请重新预检。");
        // Never honor confirmInRevit=false for arbitrary scripts, including platform automatic mode.
        ScriptReviewWindow.Require(script.Purpose, script.Code, script.Hash, dryRun ? "试运行并回滚" : "正式提交", doc.Title);
        if (!dryRun && script.CheckedRevision != revision.Value) throw new InvalidOperationException("审阅期间文档发生变化，请重新预检。");
        script.CheckedRevision = null;
        using var group = new TransactionGroup(doc, "AI脚本：" + (dryRun ? "试运行" : "正式执行"));
        if (group.Start() != TransactionStatus.Started) throw new InvalidOperationException("不能开始脚本事务组。");
        try
        {
            JsonElement data;
            using (var transaction = new Transaction(doc, "AI脚本：" + script.Purpose))
            {
                if (transaction.Start() != TransactionStatus.Started) throw new InvalidOperationException("不能开始脚本事务。");
                var failures = new ScriptFailures();
                transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions().SetFailuresPreprocessor(failures).SetClearAfterRollback(true));
                data = Invoke(doc, script);
                doc.Regenerate();
                if (transaction.Commit() != TransactionStatus.Committed)
                    throw new InvalidOperationException("Revit拒绝提交脚本：" + string.Join("；", failures.Messages));
            }
            if (dryRun)
            {
                if (group.RollBack() != TransactionStatus.RolledBack) throw new InvalidOperationException("试运行未正确回滚。");
                script.CheckedRevision = revision.Value;
                BridgeLog.Info($"Script trial rolled back {p.GetRequiredString("scriptId")} {script.Hash}");
                // Do not leak IDs created by the temporary transaction as valid subsequent input.
                return new { dryRun = true, trialPassed = true, scriptId = p.GetRequiredString("scriptId"), sha256 = script.Hash,
                    note = "脚本试运行并提交检查通过，已回滚；没有保留模型修改。临时结果不作为有效构件ID返回。仍须确认正式提交。" };
            }
            if (group.Assimilate() != TransactionStatus.Committed) throw new InvalidOperationException("脚本事务组未提交。");
            Scripts.Remove(p.GetRequiredString("scriptId"));
            BridgeLog.Info($"Script committed {p.GetRequiredString("scriptId")} {script.Hash}");
            return new { committed = true, scriptId = p.GetRequiredString("scriptId"), data,
                note = "宿主已提交脚本；data为脚本返回数据，须重新查询构件核实结果。" };
        }
        catch
        {
            if (group.GetStatus() == TransactionStatus.Started) group.RollBack();
            throw;
        }
    }

    private static Prepared Get(Document doc, JsonElement p, string mode)
    {
        var id = p.GetRequiredString("scriptId");
        if (!Scripts.TryGetValue(id, out var script) || script.Token != DesignAgentTools.Token(doc) || script.Mode != mode
            || DateTime.UtcNow - script.Created > TimeSpan.FromMinutes(20))
            throw new InvalidOperationException("脚本ID失效、模式不符或属于其他文档，请重新prepare_revit_script。");
        return script;
    }

    private static IEnumerable<MetadataReference> References()
    {
        var trusted = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        return trusted.Concat(new[] { typeof(Document).Assembly.Location, typeof(ScriptBudget).Assembly.Location })
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static JsonElement Invoke(Document doc, Prepared script)
    {
        var context = new AssemblyLoadContext("YingTan reviewed script", isCollectible: true);
        context.Resolving += (_, name) => AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name.Name);
        try
        {
            using var bytes = new MemoryStream(script.Assembly, writable: false);
            var assembly = context.LoadFromStream(bytes);
            var result = assembly.GetType("ScriptEntry", throwOnError: true)!.GetMethod("Run")!.Invoke(null, new object[] { doc, new ScriptBudget() }) as string;
            if (result is null || result.Length > 64000) throw new InvalidOperationException("脚本必须返回不超过64000字符的JSON字符串；请分页并仅返回普通数据。");
            using var json = JsonDocument.Parse(result, new JsonDocumentOptions { MaxDepth = 32 });
            return json.RootElement.Clone();
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw new InvalidOperationException("脚本运行失败：" + ex.InnerException.Message, ex.InnerException);
        }
        finally { context.Unload(); }
    }

    private sealed class ScriptFailures : IFailuresPreprocessor
    {
        public List<string> Messages { get; } = new();
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            foreach (var message in accessor.GetFailureMessages()) Messages.Add(message.GetDescriptionText());
            // Conservatively reject warnings as well; do not silently dismiss model quality problems.
            return Messages.Count > 0 ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }
    }
}
