using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RevitCodexBridge.Addin;

// Defense in depth for explicitly reviewed, trusted scripts. NOT a security sandbox.
internal static class RevitScriptCompiler
{
    internal static string Hash(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant();

    internal static BlockSyntax CheckBody(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 24000) throw new InvalidOperationException("脚本正文须为1–24000字符。");
        var block = SyntaxFactory.ParseStatement("{\n" + code + "\n}", options: new CSharpParseOptions(LanguageVersion.CSharp12), consumeFullText: true) as BlockSyntax;
        if (block is null || block.ContainsDiagnostics) throw new InvalidOperationException("脚本必须是完整C#方法体，不能包含using声明、类或外部成员。\n" + string.Join("\n", block?.GetDiagnostics() ?? []));
        if (block.DescendantTrivia().Any(t => t.IsDirective)) throw new InvalidOperationException("脚本禁止预处理/加载指令。");
        foreach (var node in block.DescendantNodes())
        {
            if (node is LocalFunctionStatementSyntax or UnsafeStatementSyntax or FixedStatementSyntax or GotoStatementSyntax
                or AwaitExpressionSyntax or AnonymousMethodExpressionSyntax or LockStatementSyntax
                or TryStatementSyntax or PointerTypeSyntax or FunctionPointerTypeSyntax or StackAllocArrayCreationExpressionSyntax)
                throw new InvalidOperationException("脚本禁止本地函数、unsafe、跳转、异步、反射、锁和捕获异常；由宿主处理失败。" );
        }
        var denied = new HashSet<string>(StringComparer.Ordinal)
        {
            "dynamic", "Transaction", "SubTransaction", "TransactionGroup", "Assembly", "Activator", "GetType", "Type",
            "GetTypeInfo", "GetMethod", "Invoke", "CreateDelegate", "DllImport", "Environment", "GC", "Thread", "Task",
            "Parallel", "Process", "File", "Directory", "Registry", "HttpClient", "WebClient", "Console", "AppDomain",
            "Application", "Dispose", "Close", "Save", "SaveAs", "Export", "Import", "LoadFamily", "LoadFamilySymbol",
            "SynchronizeWithCentral", "ReloadLatest", "PostCommand", "SendKeys", "GetService", "GetServices",
            "ScriptEntry", "ScriptBudget", "Action", "Func", "Delegate",
            "Load", "LoadFrom", "Unload", "ReadJournalComment", "WriteJournalComment", "DocumentSaving", "DocumentChanged"
        };
        foreach (var token in block.DescendantTokens())
            if (token.IsKind(SyntaxKind.IdentifierToken) && (denied.Contains(token.ValueText) || token.ValueText.StartsWith("__", StringComparison.Ordinal)))
                throw new InvalidOperationException("脚本不允许此标识符：" + token.ValueText);
        return block;
    }

    internal static byte[] Compile(string code, IEnumerable<MetadataReference> references)
    {
        var body = CheckBody(code);
        var wrapped = "using System; using System.Linq; using System.Collections.Generic; using System.Text.Json; "
            + "using Autodesk.Revit.DB; using Autodesk.Revit.DB.Structure; "
            + "public static class ScriptEntry { public static string Run(Document doc, RevitCodexBridge.Addin.ScriptBudget __budget) "
            + body.ToFullString() + "}";
        var tree = CSharpSyntaxTree.ParseText(wrapped, new CSharpParseOptions(LanguageVersion.CSharp12));
        var compilation = CSharpCompilation.Create("YingTanScript_" + Guid.NewGuid().ToString("N"), [tree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: false));
        var model = compilation.GetSemanticModel(tree);
        // Reject access to other installed add-ins, IO, networking, runtime loading, reflection, UI, etc.
        foreach (var name in tree.GetRoot().DescendantNodes().OfType<SimpleNameSyntax>())
        {
            if (name.Ancestors().Any(n => n is UsingDirectiveSyntax)) continue;
            var symbol = model.GetSymbolInfo(name).Symbol;
            var type = symbol as INamedTypeSymbol ?? symbol?.ContainingType;
            if (type is null || type.Name is "ScriptEntry" or "ScriptBudget" || type.IsAnonymousType) continue;
            var ns = type.ContainingNamespace.ToDisplayString();
            if (!(ns == "System" || ns == "System.Linq" || ns == "System.Collections" || ns == "System.Collections.Generic"
                || ns == "System.Text.Json" || ns == "Autodesk.Revit.Creation" || ns == "Autodesk.Revit.DB" || ns.StartsWith("Autodesk.Revit.DB.", StringComparison.Ordinal)))
                throw new InvalidOperationException("脚本不能访问此API：" + type.ToDisplayString());
        }
        var guarded = (CSharpSyntaxNode)new LoopGuard().Visit(tree.GetRoot())!;
        compilation = compilation.ReplaceSyntaxTree(tree, CSharpSyntaxTree.Create(guarded, (CSharpParseOptions)tree.Options));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success) throw new InvalidOperationException("脚本编译失败（未执行）：\n" + string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Take(12)));
        return stream.ToArray();
    }

    private sealed class LoopGuard : CSharpSyntaxRewriter
    {
        private static StatementSyntax Guard(StatementSyntax statement) => SyntaxFactory.Block(SyntaxFactory.ParseStatement("__budget.Tick();"), statement);
        public override SyntaxNode? VisitForStatement(ForStatementSyntax n) { var x = (ForStatementSyntax)base.VisitForStatement(n)!; return x.WithStatement(Guard(x.Statement)); }
        public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax n) { var x = (ForEachStatementSyntax)base.VisitForEachStatement(n)!; return x.WithStatement(Guard(x.Statement)); }
        public override SyntaxNode? VisitForEachVariableStatement(ForEachVariableStatementSyntax n) { var x = (ForEachVariableStatementSyntax)base.VisitForEachVariableStatement(n)!; return x.WithStatement(Guard(x.Statement)); }
        public override SyntaxNode? VisitWhileStatement(WhileStatementSyntax n) { var x = (WhileStatementSyntax)base.VisitWhileStatement(n)!; return x.WithStatement(Guard(x.Statement)); }
        public override SyntaxNode? VisitDoStatement(DoStatementSyntax n) { var x = (DoStatementSyntax)base.VisitDoStatement(n)!; return x.WithStatement(Guard(x.Statement)); }
    }
}

// Cooperative loop budget. A single long-running native Revit API call cannot be forcibly interrupted.
public sealed class ScriptBudget
{
    private readonly System.Diagnostics.Stopwatch _watch = System.Diagnostics.Stopwatch.StartNew();
    private int _steps;
    public void Tick()
    {
        if (++_steps > 20000 || _watch.Elapsed > TimeSpan.FromSeconds(10))
            throw new InvalidOperationException("脚本超过20,000次循环或10秒协作预算；操作已中止。");
    }
}
