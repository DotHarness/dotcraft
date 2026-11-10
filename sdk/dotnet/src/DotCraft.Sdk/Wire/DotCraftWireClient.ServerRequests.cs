using System.Collections.Concurrent;
using System.Text.Json;

namespace DotCraft.Sdk.Wire;

public sealed partial class DotCraftWireClient
{
    private readonly ConcurrentDictionary<string, PendingServerRequest> _serverRequests = new(StringComparer.Ordinal);
    private long _serverRequestEpoch;

    private void StartServerRequest(JsonElement id, string method, JsonElement parameters)
    {
        var pending = new PendingServerRequest(
            CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token),
            Interlocked.Read(ref _serverRequestEpoch));
        var key = id.GetRawText();
        _serverRequests[key] = pending;
        _ = DispatchServerRequestAsync(key, pending, new ServerRequest(id, method, parameters));
    }

    private async Task DispatchServerRequestAsync(string key, PendingServerRequest pending, ServerRequest request)
    {
        var token = pending.Token;
        bool CanReply() => !token.IsCancellationRequested && !_disposed
            && pending.Epoch == Interlocked.Read(ref _serverRequestEpoch);
        try
        {
            if (!_serverRequestHandlers.TryGetValue(request.Method, out var handler))
            {
                if (CanReply())
                    await SendServerErrorAsync(request.Id, -32601, $"Method not handled: {request.Method}", token);
                return;
            }

            var result = await handler(request, token);
            if (CanReply())
                await _transport.WriteAsync(new
                {
                    jsonrpc = "2.0",
                    id = request.Id,
                    result = result ?? new { }
                }, token);
        }
        catch (Exception ex)
        {
            if (CanReply())
            {
                try
                {
                    await SendServerErrorAsync(request.Id, -32603, ex.Message, token);
                }
                catch
                {
                }
            }
        }
        finally
        {
            if (_serverRequests.TryRemove(new KeyValuePair<string, PendingServerRequest>(key, pending)))
                pending.Cancellation.Dispose();
        }
    }

    private Task SendServerErrorAsync(JsonElement id, int code, string message, CancellationToken token) =>
        _transport.WriteAsync(new
        {
            jsonrpc = "2.0",
            id,
            error = new { code, message }
        }, token);

    private void CancelServerRequest(JsonElement id)
    {
        if (_serverRequests.TryRemove(id.GetRawText(), out var pending))
            pending.Cancel();
    }

    private void CancelServerRequests()
    {
        Interlocked.Increment(ref _serverRequestEpoch);
        foreach (var entry in _serverRequests)
            if (_serverRequests.TryRemove(entry))
                entry.Value.Cancel();
    }

    private sealed class PendingServerRequest(CancellationTokenSource cancellation, long epoch)
    {
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public CancellationToken Token { get; } = cancellation.Token;
        public long Epoch { get; } = epoch;

        public void Cancel()
        {
            try { Cancellation.Cancel(); }
            finally { Cancellation.Dispose(); }
        }
    }
}
