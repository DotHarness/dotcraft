using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace DotCraft.Protocol.AppServer;

/// <summary>
/// WebSocket transport for the AppServer protocol.
/// Reads JSON-RPC messages from WebSocket text frames and writes responses as text frames.
/// Each complete WebSocket message (potentially spanning multiple frames) is one JSON-RPC message,
/// per the wire protocol spec Section 15.6.
///
/// Intended for use in server-side per-connection handling.
/// Instantiate with an already-accepted <see cref="WebSocket"/>, then call <see cref="Start"/>.
/// </summary>
public sealed class WebSocketTransport : IAppServerTransport
{
    private readonly WebSocket _socket;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private int _nextOutgoingId;

    // Pending server-initiated requests awaiting a client response (approval flow)
    private readonly ConcurrentDictionary<int, TaskCompletionSource<AppServerIncomingMessage>> _pendingClientRequests = new();

    // Queue of client-initiated messages (requests + notifications) for the host loop
    private readonly ConcurrentQueue<AppServerIncomingMessage> _incomingQueue = new();
    private readonly SemaphoreSlim _incomingSemaphore = new(0);

    private Task? _readerLoop;
    private readonly CancellationTokenSource _disposeCts = new();

    /// <summary>
    /// A Task that completes when the background reader loop finishes
    /// (WebSocket closed, cancelled, or errored). Await this to block
    /// until the transport disconnects without reading from the raw socket.
    /// Never faults — expected exceptions are swallowed internally.
    /// </summary>
    public Task Completed { get; private set; } = Task.CompletedTask;

    // 4 MB max message size per spec §15.6
    private const int MaxMessageBytes = 4 * 1024 * 1024;

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public WebSocketTransport(WebSocket socket)
    {
        _socket = socket;
    }

    /// <summary>
    /// Starts the background reader loop. Must be called before any read/write operations.
    /// </summary>
    public void Start()
    {
        _readerLoop = Task.Run(ReaderLoopAsync);
        Completed = WrapReaderLoopAsync(_readerLoop);
    }

    /// <summary>
    /// Wraps the reader loop task so that awaiting <see cref="Completed"/>
    /// never throws. Expected exceptions (cancellation, WebSocket errors)
    /// are swallowed; the Task simply transitions to <see cref="TaskStatus.RanToCompletion"/>.
    /// </summary>
    private static async Task WrapReaderLoopAsync(Task readerLoop)
    {
        try { await readerLoop; }
        catch (OperationCanceledException) { }
        catch (Exception) { }
    }

    // -------------------------------------------------------------------------
    // IAppServerTransport
    // -------------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<AppServerIncomingMessage?> ReadMessageAsync(CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
        try
        {
            await _incomingSemaphore.WaitAsync(linked.Token);
            if (_incomingQueue.TryDequeue(out var msg))
                return msg;
        }
        catch (OperationCanceledException) { }

        return null;
    }

    /// <inheritdoc />
    public async Task WriteMessageAsync(object message, CancellationToken ct = default)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, SessionWireJsonOptions.Default);

        await _writeLock.WaitAsync(ct);
        try
        {
            await _socket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<AppServerIncomingMessage> SendClientRequestAsync(
        string method,
        object? @params,
        CancellationToken ct = default,
        TimeSpan? timeout = null)
    {
        var id = Interlocked.Increment(ref _nextOutgoingId);
        var tcs = new TaskCompletionSource<AppServerIncomingMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingClientRequests[id] = tcs;

        try
        {
            var request = new { jsonrpc = "2.0", id, method, @params };
            await WriteMessageAsync(request, ct);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(120));
            await using var reg = cts.Token.Register(() => tcs.TrySetCanceled(cts.Token));
            return await tcs.Task;
        }
        finally
        {
            _pendingClientRequests.TryRemove(id, out _);
        }
    }

    // -------------------------------------------------------------------------
    // Reader loop
    // -------------------------------------------------------------------------

    private async Task ReaderLoopAsync()
    {
        var ct = _disposeCts.Token;
        var buffer = new byte[MaxMessageBytes];

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Accumulate WebSocket frames until EndOfMessage
                int totalBytes = 0;
                WebSocketReceiveResult result;

                do
                {
                    var remaining = MaxMessageBytes - totalBytes;
                    if (remaining <= 0)
                    {
                        // Message exceeds 4 MB limit per spec §15.6
                        await CloseSocketAsync(WebSocketCloseStatus.MessageTooBig, "Message exceeds 4 MB limit");
                        return;
                    }

                    try
                    {
                        result = await _socket.ReceiveAsync(
                            new ArraySegment<byte>(buffer, totalBytes, remaining), ct);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (WebSocketException)
                    {
                        return;
                    }

                    if (result.MessageType == WebSocketMessageType.Close)
                        return;

                    // Binary frames are not used per spec §15.6; skip any that arrive
                    if (result.MessageType == WebSocketMessageType.Binary)
                        continue;

                    totalBytes += result.Count;

                } while (!result.EndOfMessage);

                if (totalBytes == 0)
                    continue;

                var json = Encoding.UTF8.GetString(buffer, 0, totalBytes);
                if (string.IsNullOrWhiteSpace(json))
                    continue;

                AppServerIncomingMessage? msg;
                try
                {
                    msg = JsonSerializer.Deserialize<AppServerIncomingMessage>(json, ReadOptions);
                    if (msg == null) continue;
                }
                catch (JsonException)
                {
                    // Per JSON-RPC 2.0 Section 5, a parse error must return an error response with id: null
                    var parseErrorResponse = new
                    {
                        jsonrpc = "2.0",
                        id = (object?)null,
                        error = new AppServerError
                        {
                            Code = AppServerErrors.ParseErrorCode,
                            Message = "Parse error"
                        }
                    };
                    await WriteMessageAsync(parseErrorResponse, ct);
                    continue;
                }

                // Route responses to server-initiated requests (approval flow)
                if (msg.IsResponse && msg.Id.HasValue && msg.Id.Value.ValueKind == JsonValueKind.Number)
                {
                    var responseId = msg.Id.Value.GetInt32();
                    if (_pendingClientRequests.TryRemove(responseId, out var pendingTcs))
                    {
                        pendingTcs.TrySetResult(msg);
                        continue;
                    }
                }

                // Enqueue client-initiated messages for the host's main loop
                _incomingQueue.Enqueue(msg);
                _incomingSemaphore.Release();
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            // Unblock ReadMessageAsync so the host loop exits cleanly
            try { await _disposeCts.CancelAsync(); } catch { }

            // Cancel all pending server requests
            foreach (var tcs in _pendingClientRequests.Values)
                tcs.TrySetCanceled();

            // Release the semaphore so any blocked ReadMessageAsync returns null
            _incomingSemaphore.Release();
        }
    }

    /// <summary>
    /// Sends a Close frame and waits for the client's Close response.
    /// Used for error-driven closes (e.g. message too large) where we want
    /// the full handshake before tearing down.
    /// </summary>
    private async Task CloseSocketAsync(WebSocketCloseStatus status, string description)
    {
        try
        {
            if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                await _socket.CloseAsync(status, description, CancellationToken.None);
        }
        catch { /* best-effort */ }
    }

    /// <summary>
    /// Sends a Close frame WITHOUT waiting for the client's response.
    /// Used in <see cref="DisposeAsync"/> so that shutdown is non-blocking
    /// even when the client is not actively reading.
    /// </summary>
    private async Task CloseOutputAsync(WebSocketCloseStatus status, string description)
    {
        try
        {
            // Open: we initiate the close; CloseReceived: client already sent Close, we respond
            if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                await _socket.CloseOutputAsync(status, description, CancellationToken.None);
        }
        catch { /* best-effort */ }
    }

    // -------------------------------------------------------------------------
    // IAsyncDisposable
    // -------------------------------------------------------------------------

    public async ValueTask DisposeAsync()
    {
        // Cancel the reader loop first so it can exit its ReceiveAsync cleanly
        await _disposeCts.CancelAsync();

        // Wait for the reader loop to drain (it exits quickly after cancellation)
        if (_readerLoop != null)
        {
            try { await _readerLoop; }
            catch { /* expected: OperationCanceledException or WebSocketException */ }
        }

        // Send a Close frame without waiting for the client's Close response.
        // Using CloseOutputAsync (one-way) prevents a deadlock when the client is not
        // actively reading (e.g. already disposed or in a non-graceful teardown path).
        await CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Server shutting down");
        _socket.Dispose();

        _disposeCts.Dispose();
        _writeLock.Dispose();
        _incomingSemaphore.Dispose();
    }
}
