using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using DotCraft.Sessions.Wire;

namespace DotCraft.AppServer;

public sealed class StdioTransport : IAppServerTransport
{
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
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
    private int _disposed;

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private StdioTransport(Stream input, Stream output, ILogger? logger, Guid connectionId)
    {
        _logger = logger ?? NullLogger.Instance;
        _connectionId = connectionId;
        _reader = new StreamReader(input, Encoding.UTF8);
        _writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true
        };
    }

    /// <summary>Keep stdout reserved for JSON-RPC and send diagnostics to stderr.</summary>
    public static StdioTransport CreateStdio(ILogger? logger = null, Guid connectionId = default) =>
        new(Console.OpenStandardInput(), Console.OpenStandardOutput(), logger, connectionId);

    public static StdioTransport Create(
        Stream input, Stream output, ILogger? logger = null, Guid connectionId = default) =>
        new(input, output, logger, connectionId);

    public void Start()
    {
        _readerLoop = Task.Run(ReaderLoopAsync);
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
        var json = JsonSerializer.Serialize(message, SessionWireJsonOptions.Default);
        await _writeLock.WaitAsync(ct);
        try
        {
            await _writer.WriteLineAsync(json.AsMemory(), ct);
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
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await _reader.ReadLineAsync(ct);
                if (line == null)
                    break;

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                AppServerIncomingMessage? msg;
                try
                {
                    msg = JsonSerializer.Deserialize<AppServerIncomingMessage>(line, ReadOptions);
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
                        msg.Method, msg.Id, _connectionId, nameof(StdioTransport), IncomingCapacity);
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

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await _disposeCts.CancelAsync();
        _reader.Dispose();

        await _writeLock.WaitAsync();
        try { _writer.Dispose(); }
        finally { _writeLock.Release(); }

        if (_readerLoop != null)
        {
            try { await _readerLoop; }
            catch { }
        }

        _disposeCts.Dispose();
        _writeLock.Dispose();
    }
}
