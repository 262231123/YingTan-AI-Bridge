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
}, _ => { }, CancellationToken.None);
Check(turn.PendingPlan is not null && executions == 2, "read then write preview in one user turn");

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
Console.WriteLine($"{passed} assertions passed.");

namespace RevitCodexBridge.Addin
{
    internal sealed record AiChatRequestMessage(string Role, string Content);
}
