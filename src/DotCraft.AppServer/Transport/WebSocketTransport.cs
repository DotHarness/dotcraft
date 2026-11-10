using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using DotCraft.Sessions.Wire;

namespace DotCraft.AppServer;

public sealed class WebSocketTransport : IAppServerTransport
{
    private readonly WebSocket _socket;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private int _nextOutgoingId;

    private readonly ConcurrentDictionary<int, TaskCompletionSource<AppServerIncomingMessage>> _pendingClientRequests = new();

    private const int IncomingCapacity = 128;
    private readonly Channel<AppServerIncomingMessage> _incoming =
        Channel.CreateBounded<AppServerIncomingMessage>(IncomingCapacity);
    private readonly ILogger _logger;
    private readonly Guid _connectionId;

    private Task? _readerLoop;
    private readonly CancellationTokenSource _disposeCts = new();

    /// <summary>Completes when the reader exits without propagating reader errors.</summary>
    public Task Completed { get; private set; } = Task.CompletedTask;

    private const int MaxMessageBytes = 4 * 1024 * 1024;

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public WebSocketTransport(WebSocket socket, ILogger? logger = null, Guid connectionId = default)
    {
        _logger = logger ?? NullLogger.Instance;
        _connectionId = connectionId;
        _socket = socket;
    }

    public void Start()
    {
        _readerLoop = Task.Run(ReaderLoopAsync);
        Completed = WrapReaderLoopAsync(_readerLoop);
    }

    private static async Task WrapReaderLoopAsync(Task readerLoop)
    {
        try { await readerLoop; }
        catch { }
    }

    public async Task<AppServerIncomingMessage?> ReadMessageAsync(CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
        try
        {
            return await _incoming.Reader.ReadAsync(linked.Token);
        }
        catch (OperationCanceledException) { }
        catch (ChannelClosedException) { }

        return null;
    }

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

    public async Task<AppServerPendingClientRequest> StartClientRequestAsync(
        string method,
        object? @params,
        CancellationToken ct = default,
        TimeSpan? timeout = null)
    {
        using var writeCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
        writeCancellation.Token.ThrowIfCancellationRequested();
        var id = Interlocked.Increment(ref _nextOutgoingId);
        var tcs = new TaskCompletionSource<AppServerIncomingMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingClientRequests[id] = tcs;
        var response = AwaitClientResponseAsync(id, tcs, ct, timeout);

        try
        {
            await WriteMessageAsync(new { jsonrpc = "2.0", id, method, @params }, writeCancellation.Token);
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
        }
        return new AppServerPendingClientRequest(id, response);
    }

    private async Task<AppServerIncomingMessage> AwaitClientResponseAsync(
        int id,
        TaskCompletionSource<AppServerIncomingMessage> completion,
        CancellationToken ct,
        TimeSpan? timeout)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
        cancellation.CancelAfter(timeout ?? TimeSpan.FromSeconds(120));
        using var registration = cancellation.Token.Register(() =>
        {
            if (_pendingClientRequests.TryRemove(id, out var pending))
                pending.TrySetCanceled(cancellation.Token);
        });
        try
        {
            return await completion.Task;
        }
        finally
        {
            _pendingClientRequests.TryRemove(id, out _);
        }
    }

    private async Task ReaderLoopAsync()
    {
        var ct = _disposeCts.Token;
        var buffer = new byte[MaxMessageBytes];

        try
        {
            while (!ct.IsCancellationRequested)
            {
                int totalBytes = 0;
                WebSocketReceiveResult result;

                do
                {
                    var remaining = MaxMessageBytes - totalBytes;
                    if (remaining <= 0)
                    {
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

                if (msg.IsResponse)
                {
                    if (msg.Id is { ValueKind: JsonValueKind.Number } responseId
                        && responseId.TryGetInt32(out var id)
                        && _pendingClientRequests.TryRemove(id, out var pending))
                    {
                        pending.TrySetResult(msg);
                    }
                    continue;
                }

                if (!msg.IsRequest)
                {
                    await _incoming.Writer.WriteAsync(msg, ct);
                }
                else if (!_incoming.Writer.TryWrite(msg))
                {
                    _logger.LogWarning(
                        "AppServer incoming buffer full: {RequestMethod} ({RequestId}), connection {ConnectionId}, transport {Transport}, capacity {Capacity}",
                        msg.Method, msg.Id, _connectionId, nameof(WebSocketTransport), IncomingCapacity);
                    await WriteMessageAsync(AppServerRequestHandler.BuildErrorResponse(
                        msg.Id, AppServerErrors.ServerOverloaded().ToError()), ct);
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            try { await _disposeCts.CancelAsync(); } catch { }

            _incoming.Writer.TryComplete();
        }
    }

    private async Task CloseSocketAsync(WebSocketCloseStatus status, string description)
    {
        try
        {
            if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                await _socket.CloseAsync(status, description, CancellationToken.None);
        }
        catch { }
    }

    private async Task CloseOutputAsync(WebSocketCloseStatus status, string description)
    {
        try
        {
            if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                await _socket.CloseOutputAsync(status, description, CancellationToken.None);
        }
        catch { }
    }

    public async ValueTask DisposeAsync()
    {
        await _disposeCts.CancelAsync();

        if (_readerLoop != null)
        {
            try { await _readerLoop; }
            catch { }
        }

        await CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Server shutting down");
        _socket.Dispose();

        _disposeCts.Dispose();
        _writeLock.Dispose();
    }
}
