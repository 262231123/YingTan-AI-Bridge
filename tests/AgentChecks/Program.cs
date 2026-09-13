using System.Text.Json;
using RevitCodexBridge.Addin;

var passed = 0;
void Check(bool valid, string name)
{
    if (!valid) throw new Exception("FAILED: " + name);
    Console.WriteLine("PASS: " + name);
    passed++;
}
void Rejected(string plan, string name)
{
    try { AgentPlanPolicy.Normalize(plan, "doc-A"); }
    catch (Exception) { Check(true, name); return; }
    throw new Exception("Accepted invalid plan: " + name);
}
var context = JsonSerializer.SerializeToElement(new { documentToken = "doc-A" });
const string read = "{\"operations\":[{\"command\":\"get_selection\"}]}";
const string write = "{\"operations\":[{\"command\":\"set_parameter\",\"elementId\":42,\"parameterName\":\"Comments\",\"value\":\"Review\"}]}";
var call = 0;
var executions = 0;
var recordedReads = 0;
var turn = await RevitAgentLoop.RunAsync(context, (messages, ct) =>
{
    call++;
    if (call == 2) Check(messages.Any(x => x.Content.Contains("42")), "real read result reaches next planning round");
    return Task.FromResult(new RevitAiResponse("修改选择集注释", call == 1 ? read : write));
}, (payload, ct) =>
{
    executions++;
    Check(payload.GetProperty("dryRun").GetBoolean(), "loop never dispatches writes");
    Check(payload.GetProperty("expectedDocumentToken").GetString() == "doc-A", "document binding preserved");
    return Task.FromResult<object?>(new { failures = 0, results = new[] { new { ok = true, result = new { selectedId = 42 } } } });
}, _ => { }, CancellationToken.None, recordQuery: (plan, result) => { recordedReads++; });
Check(turn.PendingPlan is not null && executions == 2, "read then write preview in one user turn");
Check(recordedReads == 1, "only successful read results are saved for multi-turn design context");

call = 0;
turn = await RevitAgentLoop.RunAsync(context, (messages, ct) =>
{
    call++;
    if (call == 2) Check(messages.Any(x => x.Content.Contains("readonly parameter")), "batch failure feedback reaches model");
    return Task.FromResult(call == 1 ? new RevitAiResponse("", write) : new RevitAiResponse("参数只读，无法修改", null));
}, (_, _) => Task.FromResult<object?>(new { failures = 1, results = new[] { new { ok = false, error = "readonly parameter" } } }), _ => { }, CancellationToken.None);
Check(turn.PendingPlan is null, "failed preview does not become executable plan");

executions = 0;
turn = await RevitAgentLoop.RunAsync(context, (_, _) => Task.FromResult(new RevitAiResponse("", write)),
    (_, _) => { executions++; return Task.FromResult<object?>(new { }); }, _ => { }, CancellationToken.None, true);
Check(executions == 0 && turn.PendingPlan is null, "verification cannot dispatch mutations");

turn = await RevitAgentLoop.RunAsync(context, (_, _) => Task.FromResult(new RevitAiResponse("", read)),
    (_, _) => Task.FromResult<object?>(new { failures = 0 }), _ => { }, CancellationToken.None);
Check(turn.PendingPlan is null && turn.Reply.Contains("重复"), "duplicate query loop stops");
Rejected("{\"operations\":[{\"command\":\"run_batch\"}]}", "nested batch rejected");
Rejected("{\"operations\":[{\"command\":\"set_parameter\",\"payload\":{}}]}", "ambiguous payload wrapper rejected");
Rejected("{\"command\":\"delete_everything\"}", "unknown command rejected");
Rejected("{\"operations\":[]}", "empty plan rejected");
Rejected("{\"command\":\"count_elements\",\"operations\":[{\"command\":\"create_wall\"}]}", "hidden writes rejected");
Check(AgentPlanPolicy.Failure(new { rolledBack = true, failures = 0 }) is not null, "rollback never reported as success");
var verificationPlan = AgentPlanPolicy.VerificationPlan(new { results = new[] { new { result = new { roomId = 12, wallIds = new[] { 13, 14, 15, 16 } } } } }, "doc-A")!;
using (var verify = JsonDocument.Parse(verificationPlan))
    Check(verify.RootElement.GetProperty("operations").GetArrayLength() == 5 && !BridgePayloadBuilder.HasMutation(verificationPlan), "created room and walls are read back by actual IDs");
Check(AgentPlanPolicy.Failure(new { results = new[] { new { ok = true, result = new { succeeded = false } } } }) is not null, "nested failure detected");
var queued = new PendingBridgeRequest(context);
Check(queued.CancelQueued() && !queued.TryStart(), "cancelled queue item never starts");
var running = new PendingBridgeRequest(context);
Check(running.TryStart() && !running.CancelQueued(), "running transaction not abandoned");
using var cancellation = new CancellationTokenSource();
try
{
    await RevitAgentLoop.RunAsync(context, (_, _) => { cancellation.Cancel(); return Task.FromResult(new RevitAiResponse("", write)); },
        (_, _) => throw new Exception("should never dispatch"), _ => { }, cancellation.Token);
    throw new Exception("Cancellation was ignored");
}
catch (OperationCanceledException) { Check(true, "cancellation between AI and dispatch prevents execution"); }
var axisNames = PlatformLayout.ParseGridNames("我想在K-H轴/36-37轴区域内布置平台");
Check(axisNames is not null && axisNames.SequenceEqual(new[] { "K", "H", "36", "37" }), "Chinese design sentence resolves the four exact grid names");
var frame = PlatformLayout.Resolve(new("K", new(0, 0), new(0, 10000)), new("H", new(10000, 10000), new(10000, 0)),
    new("36", new(0, 0), new(10000, 0)), new("37", new(10000, 12000), new(0, 12000)));
Check(Math.Abs(frame.WidthMm - 10000) < 0.001 && Math.Abs(frame.LengthMm - 12000) < 0.001, "grid intersection ignores endpoint order and datum segment extents");
var angle = Math.PI / 6;
PlanPoint Rotate(PlanPoint p) => new PlanPoint(p.X * Math.Cos(angle) - p.Y * Math.Sin(angle), p.X * Math.Sin(angle) + p.Y * Math.Cos(angle)) + new PlanPoint(45000, -32000);
AxisLine RotAxis(string name, PlanPoint a, PlanPoint b) => new(name, Rotate(a), Rotate(b));
var rotated = PlatformLayout.Resolve(RotAxis("K", new(0, 0), new(0, 10000)), RotAxis("H", new(10000, 0), new(10000, 10000)),
    RotAxis("36", new(0, 0), new(10000, 0)), RotAxis("37", new(0, 12000), new(10000, 12000)));
Check((rotated.At(10000, 12000) - Rotate(new(10000, 12000))).Length < 0.001, "rotated and translated grid preserves world coordinates");
var few = PlatformLayout.Build(rotated, 4000, 10000, 2000, 1000, 2000, 100, 0, 1, 1500);
var more = PlatformLayout.Build(rotated, 4000, 10000, 2000, 1000, 2000, 100, 0, 3, 1500);
Check(few.ColumnCount == 8 && more.ColumnCount == 16 && more.LongitudinalSpanMm < few.LongitudinalSpanMm, "few-column vs shorter-span candidates expose real tradeoff");
Check(few.Decks.Select(x => x.TopMm).SequenceEqual(new[] {1000.0, 2000.0, 1000.0}) && few.Decks.Count == 3, "two operating decks and central equipment deck have requested top heights");
Check(few.Members.Where(x => x.Kind == "beam").All(x => x.TopMm == 900 || x.TopMm == 1900), "beam tops lie below deck thickness");
Check(few.Members.Where(x => x.Kind == "column").Select(x => x.Start).Distinct().Count() == few.ColumnCount, "inner columns are shared without duplicate columns");
var badSizes = false;
try { PlatformLayout.Build(frame, 8000, 10000, 2000, 1000, 2000, 100, 0, 1, 1500); } catch (InvalidOperationException) { badSizes = true; }
Check(badSizes, "oversized platform cannot silently extend past the grid region");
var badGrids = false;
try { PlatformLayout.Resolve(new("K", new(0,0),new(0,10000)), new("H",new(10000,0),new(11000,10000)), new("36",new(0,0),new(10000,0)),new("37",new(0,10000),new(10000,10000))); }
catch(InvalidOperationException) { badGrids = true; }
Check(badGrids, "skewed nonrectangular grid is rejected rather than approximated");
var platformPlan = AgentPlanPolicy.Normalize("{\"command\":\"create_steel_platform\",\"previewId\":\"abc\"}", "doc-A");
Check(BridgePayloadBuilder.HasMutation(platformPlan), "steel platform remains an explicit mutation plan");
var dryPlatform = BridgePayloadBuilder.Build(platformPlan, false);
Check(dryPlatform.GetProperty("operations")[0].GetProperty("dryRun").GetBoolean(), "steel platform dry-run propagated to host command");
Rejected("{\"operations\":[{\"command\":\"query_revit_script\",\"scriptId\":\"draft\"},{\"command\":\"set_parameter\"}]}", "script operations cannot be mixed into a write batch");
ScriptChecks.Run(Check);
var sessionTokens = new DocumentSessionTokens();
var documentGuid = Guid.NewGuid();
var firstToken = sessionTokens.Get(documentGuid);
Check(firstToken == sessionTokens.Get(documentGuid), "same native document identity keeps one plan token across wrapper calls");
sessionTokens.Close(documentGuid);
Check(firstToken != sessionTokens.Get(documentGuid), "reopened document receives a new plan token");
var operationHistory = new BridgeOperationHistory();
var successfulOperation = operationHistory.AddResult("get_model_context", DateTimeOffset.UtcNow, new { ok = true, count = 4 }, "测试模型");
var failedOperation = operationHistory.AddResult("run_batch", DateTimeOffset.UtcNow, new { failures = 1, results = new[] { new { ok = false, error = "族类型未加载" } } }, "测试模型");
Check(successfulOperation.Succeeded && !failedOperation.Succeeded && failedOperation.ErrorDetails.Contains("族类型未加载"), "operation history exposes semantic batch failure details");
Check(operationHistory.Snapshot().Count == 2 && operationHistory.Snapshot()[1].Sequence > operationHistory.Snapshot()[0].Sequence, "operation history preserves ordered command records");
var fallbackCalls = 0;
var fallback = await RevitAgentLoop.RunAsync(context, (messages, ct) =>
{
    fallbackCalls++;
    if (fallbackCalls == 2) Check(messages.Any(m => m.Content.Contains("prepare_revit_script")), "native capability refusal triggers script capability reminder");
    return Task.FromResult(new RevitAiResponse("无法直接完成，需要确认具体API限制", null));
}, (_, _) => throw new Exception("No script should execute without a plan"), _ => {}, CancellationToken.None);
Check(fallbackCalls == 2 && fallback.PendingPlan is null, "unsupported fallback is bounded to one retry");
Console.WriteLine($"{passed} assertions passed.");

namespace RevitCodexBridge.Addin
{
    internal sealed record AiChatRequestMessage(string Role, string Content);
}
