using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace DotCraft.Protocol.AppServer;

/// <summary>
/// stdio JSONL transport for the AppServer protocol.
/// Reads newline-delimited JSON-RPC messages from <see cref="Console.OpenStandardInput"/>
/// and writes to <see cref="Console.OpenStandardOutput"/>.
/// All diagnostic and log output must go to stderr before this transport is started.
/// </summary>
public sealed class StdioTransport : IAppServerTransport
{
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private int _nextOutgoingId;

    // Pending server-initiated requests awaiting a client response (keyed by request id)
    private readonly ConcurrentDictionary<int, TaskCompletionSource<AppServerIncomingMessage>> _pendingClientRequests = new();

    // Queue of client-initiated messages (requests + notifications) for the host loop
    private readonly ConcurrentQueue<AppServerIncomingMessage> _incomingQueue = new();
    private readonly SemaphoreSlim _incomingSemaphore = new(0);

    private Task? _readerLoop;
    private readonly CancellationTokenSource _disposeCts = new();
    private int _disposed;

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private StdioTransport(Stream input, Stream output)
    {
        _reader = new StreamReader(input, Encoding.UTF8);
        _writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true
        };
    }

    /// <summary>
    /// Creates a transport using the process's stdin and stdout streams.
    /// Stdout must already have been redirected to stderr or reserved for JSON-RPC before this call.
    /// </summary>
    public static StdioTransport CreateStdio() =>
        new(Console.OpenStandardInput(), Console.OpenStandardOutput());

    /// <summary>
    /// Creates a transport over arbitrary streams (e.g. subprocess stdin/stdout pipes,
    /// in-memory streams for testing).
    /// </summary>
    public static StdioTransport Create(Stream input, Stream output) => new(input, output);

    /// <summary>
    /// Starts the background reader loop. Must be called before any read/write operations.
    /// </summary>
    public void Start()
    {
        _readerLoop = Task.Run(ReaderLoopAsync);
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
        catch (OperationCanceledException) { /* Expected */ }

        return null;
    }

    /// <inheritdoc />
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
            var request = new
            {
                jsonrpc = "2.0",
                id,
                method,
                @params
            };
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
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await _reader.ReadLineAsync(ct);
                if (line == null)
                    break; // EOF

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
                    // Fix 3: Per JSON-RPC 2.0 Section 5, a parse error must return an error response
                    // with id: null and code -32700.
                    var parseErrorResponse = new
                    {
                        jsonrpc = "2.0",
                        id = (object?)null,
                        error = new { code = AppServerErrors.ParseErrorCode, message = "Parse error" }
                    };
                    await WriteMessageAsync(parseErrorResponse, ct);
                    continue;
                }

                // Route responses to server-initiated requests (approval flow)
                if (msg.IsResponse && msg.Id.HasValue && msg.Id.Value.ValueKind == JsonValueKind.Number)
                {
                    var responseId = msg.Id.Value.GetInt32();
                    if (_pendingClientRequests.TryRemove(responseId, out var tcs))
                    {
                        tcs.TrySetResult(msg);
                        continue;
                    }
                }

                // Enqueue client-initiated messages for the host's main loop
                _incomingQueue.Enqueue(msg);
                _incomingSemaphore.Release();
            }
        }
        catch (OperationCanceledException) { /* Expected on dispose */ }
        finally
        {
            // Unblock ReadMessageAsync so the host loop exits cleanly
            try { await _disposeCts.CancelAsync(); } catch { /* Already disposed */ }

            // Cancel all pending server requests
            foreach (var tcs in _pendingClientRequests.Values)
                tcs.TrySetCanceled();

            // Release the semaphore so any blocked ReadMessageAsync returns null
            _incomingSemaphore.Release();
        }
    }

    // -------------------------------------------------------------------------
    // IAsyncDisposable
    // -------------------------------------------------------------------------

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
            catch { /* Expected */ }
        }

        _disposeCts.Dispose();
        _writeLock.Dispose();
        _incomingSemaphore.Dispose();
    }
}
