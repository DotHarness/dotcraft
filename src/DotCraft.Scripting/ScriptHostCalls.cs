using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace DotCraft.Scripting;

public sealed class ScriptHostCalls(string idPrefix)
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<object?>> _pending = new(StringComparer.Ordinal);
    private int _sequence;

    public async Task<object?> CallAsync(Func<string, Task> sendRequest, CancellationToken cancellationToken)
    {
        var id = $"{idPrefix}{Interlocked.Increment(ref _sequence):D4}";
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;
        await sendRequest(id).ConfigureAwait(false);
        return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Resolve(string id, JsonNode? result, string? error)
    {
        if (!_pending.TryRemove(id, out var completion))
            throw new ScriptProtocolException("protocol_operation_unknown", $"Unknown operation '{id}'.");
        if (error != null)
            completion.TrySetException(new InvalidOperationException(error));
        else
            completion.TrySetResult(ScriptValues.ToClr(result));
    }

    public void FailAll(Exception exception)
    {
        foreach (var id in _pending.Keys)
            if (_pending.TryRemove(id, out var completion)) completion.TrySetException(exception);
    }

    public void CancelAll()
    {
        foreach (var id in _pending.Keys)
            if (_pending.TryRemove(id, out var completion)) completion.TrySetCanceled();
    }
}
