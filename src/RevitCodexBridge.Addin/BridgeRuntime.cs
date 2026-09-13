using System.Collections.Concurrent;
using System.Text.Json;
using Autodesk.Revit.UI;

namespace RevitCodexBridge.Addin;

internal sealed class BridgeRuntime : IDisposable
{
    private readonly ConcurrentQueue<PendingBridgeRequest> _pending = new();
    private readonly BridgeOperationHistory _history = new();
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

    public event EventHandler? OperationRecorded;

    public IReadOnlyList<BridgeOperationRecord> GetOperationHistory() => _history.Snapshot();

    public void AttachExternalEvent(ExternalEvent externalEvent)
    {
        _externalEvent = externalEvent;
    }

    public async Task<object?> EnqueueAsync(JsonElement payload, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_externalEvent is null)
        {
            throw new InvalidOperationException("Revit external event has not been initialized.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var request = new PendingBridgeRequest(payload.Clone());
        using var cancellation = cancellationToken.Register(() => request.CancelQueued());
        _pending.Enqueue(request);
        _externalEvent.Raise();

        var timeoutTask = Task.Delay(timeout);
        var completed = await Task.WhenAny(request.Completion, timeoutTask).ConfigureAwait(false);

        if (completed == timeoutTask)
        {
            if (request.CancelQueued())
                throw new TimeoutException("Revit 未及时处理请求，已取消队列中的操作。");
            // An operation already running in Revit cannot be safely abandoned or replayed.
            return await request.Completion.ConfigureAwait(false);
        }

        return await request.Completion.ConfigureAwait(false);
    }

    public void ExecutePending(UIApplication app)
    {
        while (_pending.TryDequeue(out var request))
        {
            if (!request.TryStart()) continue;
            var command = GetCommandName(request.Payload);
            var startedAtUtc = DateTimeOffset.UtcNow;
            var documentTitle = app.ActiveUIDocument?.Document?.Title;
            LastCommand = command;
            BridgeLog.Info($"Executing command: {command}");

            try
            {
                var result = CommandExecutor.Execute(app, request.Payload);
                var record = _history.AddResult(command, startedAtUtc, result, documentTitle);
                if (record.Succeeded)
                {
                    Interlocked.Increment(ref _processedCount);
                    LastError = null;
                }
                else
                {
                    Interlocked.Increment(ref _failedCount);
                    LastError = record.ErrorDetails;
                }
                NotifyOperationRecorded();
                request.SetResult(result);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _failedCount);
                LastError = ex.Message;
                _history.AddException(command, startedAtUtc, ex, documentTitle);
                NotifyOperationRecorded();
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

    private void NotifyOperationRecorded()
    {
        try
        {
            OperationRecorded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            BridgeLog.Error("Operation history UI notification failed.", ex);
        }
    }
}
