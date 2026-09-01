using System.Collections.Concurrent;
using System.Text.Json;
using Autodesk.Revit.UI;

namespace RevitCodexBridge.Addin;

internal sealed class BridgeRuntime : IDisposable
{
    private readonly ConcurrentQueue<PendingBridgeRequest> _pending = new();
    private ExternalEvent? _externalEvent;
    private bool _disposed;
    private long _processedCount;
    private long _failedCount;

    public BridgeRuntime(string revitVersion)
    {
        RevitVersion = revitVersion;
        ServerUrl = $"http://127.0.0.1:{BridgeServer.DefaultPort}";
        StartedAtUtc = DateTimeOffset.UtcNow;
    }

    public static BridgeRuntime? Current { get; set; }

    public string RevitVersion { get; }

    public string ServerUrl { get; }

    public int PendingCount => _pending.Count;

    public DateTimeOffset StartedAtUtc { get; }

    public long ProcessedCount => Interlocked.Read(ref _processedCount);

    public long FailedCount => Interlocked.Read(ref _failedCount);

    public string? LastCommand { get; private set; }

    public string? LastError { get; private set; }

    public void AttachExternalEvent(ExternalEvent externalEvent)
    {
        _externalEvent = externalEvent;
    }

    public async Task<object?> EnqueueAsync(JsonElement payload, TimeSpan timeout)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_externalEvent is null)
        {
            throw new InvalidOperationException("Revit external event has not been initialized.");
        }

        var request = new PendingBridgeRequest(payload.Clone());
        _pending.Enqueue(request);
        _externalEvent.Raise();

        var timeoutTask = Task.Delay(timeout);
        var completed = await Task.WhenAny(request.Completion, timeoutTask).ConfigureAwait(false);

        if (completed == timeoutTask)
        {
            throw new TimeoutException("Revit did not process the request before the bridge timeout.");
        }

        return await request.Completion.ConfigureAwait(false);
    }

    public void ExecutePending(UIApplication app)
    {
        while (_pending.TryDequeue(out var request))
        {
            var command = GetCommandName(request.Payload);
            LastCommand = command;
            BridgeLog.Info($"Executing command: {command}");

            try
            {
                var result = CommandExecutor.Execute(app, request.Payload);
                Interlocked.Increment(ref _processedCount);
                LastError = null;
                request.SetResult(result);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _failedCount);
                LastError = ex.Message;
                BridgeLog.Error($"Command failed: {command}", ex);
                request.SetException(ex);
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;

        while (_pending.TryDequeue(out var request))
        {
            request.SetException(new ObjectDisposedException(nameof(BridgeRuntime)));
        }

        _externalEvent?.Dispose();
    }

    private static string GetCommandName(JsonElement payload)
    {
        return payload.TryGetProperty("command", out var command) && command.ValueKind == JsonValueKind.String
            ? command.GetString() ?? "<empty>"
            : "<missing>";
    }
}
