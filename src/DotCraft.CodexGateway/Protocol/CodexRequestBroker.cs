using System.Text.Json;

namespace DotCraft.CodexGateway.Protocol;

public sealed class CodexRequestBroker(Func<JsonElement, CancellationToken, Task> send) : IAsyncDisposable
{
    private sealed record Pending(string Id, TaskCompletionSource<JsonElement> Completion);
    private readonly Dictionary<string, Pending> _logical = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Pending> _transport = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private long _nextId;
    private bool _disposed;

    public bool TryReceive(JsonElement message)
    {
        if (message.ValueKind != JsonValueKind.Object || message.TryGetProperty("method", out _) ||
            !message.TryGetProperty("id", out var id))
            return false;
        lock (_gate)
        {
            if (!_transport.Remove(id.GetRawText(), out var pending)) return true;
            if (message.TryGetProperty("error", out _))
                pending.Completion.TrySetException(new IOException("codex_gateway.client_request_failed: The client did not answer the interactive request."));
            else if (message.TryGetProperty("result", out var result))
                pending.Completion.TrySetResult(result.Clone());
            else
                pending.Completion.TrySetException(new InvalidDataException("codex_gateway.invalid_response: Interactive response requires result or error."));
        }
        return true;
    }

    public async Task<JsonElement> RequestAsync(
        string logicalKey, string method, object parameters, CancellationToken ct)
    {
        Pending pending;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_logical.TryGetValue(logicalKey, out var existing))
            {
                if (!existing.Completion.Task.IsCompleted)
                    throw new InvalidOperationException("The logical request already has an active transport.");
                _transport.Remove(JsonSerializer.Serialize(existing.Id));
            }
            pending = new Pending("gateway:" + ++_nextId,
                new(TaskCreationOptions.RunContinuationsAsynchronously));
            _logical[logicalKey] = pending;
            _transport.Add(JsonSerializer.Serialize(pending.Id), pending);
        }

        using var cancellation = ct.Register(() =>
        {
            lock (_gate)
            {
                _transport.Remove(JsonSerializer.Serialize(pending.Id));
                pending.Completion.TrySetCanceled(ct);
            }
        });
        try
        {
            ct.ThrowIfCancellationRequested();
            await send(JsonSerializer.SerializeToElement(new { id = pending.Id, method, @params = parameters }), ct);
            return await pending.Completion.Task;
        }
        catch
        {
            lock (_gate)
            {
                _transport.Remove(JsonSerializer.Serialize(pending.Id));
                pending.Completion.TrySetCanceled();
            }
            throw;
        }
    }

    public async Task ResolveAsync(string logicalKey, string externalThreadId, CancellationToken ct)
    {
        Pending? pending;
        lock (_gate)
        {
            if (!_logical.Remove(logicalKey, out pending)) return;
            _transport.Remove(JsonSerializer.Serialize(pending.Id));
            pending.Completion.TrySetCanceled();
        }
        await send(JsonSerializer.SerializeToElement(new
        {
            method = "serverRequest/resolved",
            @params = new { threadId = externalThreadId, requestId = pending.Id }
        }), ct);
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _disposed = true;
            foreach (var pending in _logical.Values) pending.Completion.TrySetCanceled();
            _logical.Clear();
            _transport.Clear();
        }
        return ValueTask.CompletedTask;
    }
}
