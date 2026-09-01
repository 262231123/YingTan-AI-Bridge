using System.Text.Json;

namespace RevitCodexBridge.Addin;

internal sealed class PendingBridgeRequest
{
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

