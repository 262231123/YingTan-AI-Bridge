using System.Reflection;
using Microsoft.CodeAnalysis;
using RevitCodexBridge.Addin;

internal static class ScriptChecks
{
    public static void Run(Action<bool, string> check)
    {
        void Reject(string code, string label)
        {
            try { RevitScriptCompiler.CheckBody(code); }
            catch (InvalidOperationException) { check(true, label); return; }
            check(false, label);
        }
        check(RevitScriptCompiler.CheckBody("return JsonSerializer.Serialize(new { title=doc.Title });") is not null, "C# query body accepted");
        Reject("} public class Evil { } {", "wrapper escape rejected");
        Reject("#if false\nreturn null;\n#endif\nreturn null;", "preprocessor directives rejected");
        Reject("System.IO.File.Delete(\"x\"); return null;", "filesystem identifier rejected");
        Reject("using var t = new Transaction(doc); return null;", "script-owned transactions rejected");
        Reject("return doc.GetType().ToString();", "reflection rejected");
        Reject("try { while(true) {} } catch {} return null;", "budget exceptions cannot be swallowed");
        Reject("int Run() => Run(); return null;", "local recursive function rejected");
        Reject("__budget = null; return null;", "host budget identifier cannot be overridden");
        Reject("return ScriptEntry.Run(doc, null);", "generated entrypoint recursion rejected");
        check(RevitScriptCompiler.CheckBody("return JsonSerializer.Serialize(new { name=\"File\" });") is not null, "ordinary model strings are not treated as forbidden identifiers");
        Reject(new string('x', 24001), "script source length bounded");
        var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(ScriptBudget).Assembly.Location).Distinct().Select(p => MetadataReference.CreateFromFile(p)).ToArray();
        var bytes = RevitScriptCompiler.Compile("return JsonSerializer.Serialize(new { title=doc.Title });", refs);
        var run = Assembly.Load(bytes).GetType("ScriptEntry")!.GetMethod("Run")!;
        var json = (string)run.Invoke(null, new object[] { new Autodesk.Revit.DB.Document(), new ScriptBudget() })!;
        check(json.Contains("Test model"), "compiled script reads supplied document and returns JSON");
        var badApi = false;
        try { RevitScriptCompiler.Compile("return doc.DoesNotExist();", refs); } catch (InvalidOperationException) { badApi = true; }
        check(badApi, "unknown API fails compilation before execution");
        var outside = false;
        try { RevitScriptCompiler.Compile("return System.Net.Dns.GetHostName();", refs); } catch (InvalidOperationException) { outside = true; }
        check(outside, "semantic policy blocks networking APIs");
        var loop = Assembly.Load(RevitScriptCompiler.Compile("while (true) { } return \"{}\";", refs)).GetType("ScriptEntry")!.GetMethod("Run")!;
        var stopped = false;
        try { loop.Invoke(null, new object[] { new Autodesk.Revit.DB.Document(), new ScriptBudget() }); }
        catch (TargetInvocationException e) { stopped = e.InnerException is InvalidOperationException; }
        check(stopped, "instrumented infinite loop stops at cooperative budget");
        var plan = AgentPlanPolicy.Normalize("{\"command\":\"execute_revit_script\",\"scriptId\":\"draft\"}", "doc-A");
        check(BridgePayloadBuilder.HasMutation(plan) && !AgentPlanPolicy.IsSinglePlatformPlan(plan), "script writes cannot inherit platform auto authorization");
        check(BridgePayloadBuilder.Build(plan, false).GetProperty("operations")[0].GetProperty("dryRun").GetBoolean(), "script write preview stays dry-run");
        check(AgentPlanPolicy.VerificationPlan(new { data = new { modifiedElementIds = new[] { 42 } } }, "doc-A")!.Contains("42"), "script modified IDs feed native result verification");
    }
}

// Portable API stand-ins only; host integration requires actual Revit.
namespace Autodesk.Revit.DB { public class Document { public string Title => "Test model"; } }
namespace Autodesk.Revit.DB.Structure { public enum StructuralType { Beam } }
