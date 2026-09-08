using System.Text.Json;

namespace RevitCodexBridge.Addin;

internal sealed class PendingBridgeRequest
{
    private int _state; // 0 queued, 1 running, 2 cancelled before dispatch
    public bool TryStart() => Interlocked.CompareExchange(ref _state, 1, 0) == 0;
    public bool CancelQueued()
    {
        if (Interlocked.CompareExchange(ref _state, 2, 0) != 0) return false;
        _completion.TrySetCanceled();
        return true;
    }
    private readonly TaskCompletionSource<object?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public PendingBridgeRequest(JsonElement payload)
    {
        Payload = payload;
    }

    public JsonElement Payload { get; }

    public Task<object?> Completion => _completion.Task;

    public void SetResult(object? result)
    {
        _completion.TrySetResult(result);
    }

    public void SetException(Exception exception)
    {
        _completion.TrySetException(exception);
    }
}
