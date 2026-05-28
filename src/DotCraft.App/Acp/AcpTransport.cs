using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace DotCraft.Acp;

/// <summary>
/// stdio-based JSON-RPC transport for ACP.
/// Reads newline-delimited JSON-RPC messages from stdin and writes to stdout.
/// Supports bidirectional request/response for both Client→Agent and Agent→Client calls.
/// </summary>
public sealed class AcpTransport(Stream input, Stream output) : IAsyncDisposable
{
    private readonly StreamReader _reader = new(input, Encoding.UTF8);
    private readonly StreamWriter _writer = new(output, new UTF8Encoding(false)) { AutoFlush = true };
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private TimeSpan _writeTimeout = TimeSpan.FromSeconds(30);
    private int _nextOutgoingId;

    /// <summary>
    /// Timeout for individual write operations to the output stream.
    /// When the pipe buffer is full (client not consuming fast enough), a write will be
    /// cancelled after this duration to prevent indefinite lock convoy.
    /// Defaults to 30 seconds. Set to <see cref="Timeout.InfiniteTimeSpan"/> to disable.
    /// Must be set before <see cref="StartReaderLoop"/> is called.
    /// </summary>
    public TimeSpan WriteTimeout
    {
        get => _writeTimeout;
        set => _writeTimeout = value > TimeSpan.Zero || value == Timeout.InfiniteTimeSpan
            ? value : TimeSpan.FromSeconds(30);
    }

    // Pending Agent→Client requests awaiting a response
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pendingClientRequests = new();

    // Incoming messages that are actual Client→Agent requests (not responses to our requests)
    private readonly ConcurrentQueue<JsonRpcRequest> _incomingRequests = new();
    private readonly SemaphoreSlim _incomingSemaphore = new(0);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private Task? _readerLoop;
    private Task? _heartbeatLoop;
    private int _consecutiveHeartbeatFailures;
    private readonly CancellationTokenSource _disposeCts = new();

    /// <summary>
    /// Interval between keep-alive ping notifications sent to the client.
    /// Default: 15 seconds. Set to <see cref="Timeout.InfiniteTimeSpan"/> to disable.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Maximum number of consecutive heartbeat failures before the transport
    /// considers the connection dead and raises <see cref="OnDisconnected"/>.
    /// Default: 3.
    /// </summary>
    public int MaxConsecutiveHeartbeatFailures { get; set; } = 3;

    /// <summary>
    /// Raised when <see cref="MaxConsecutiveHeartbeatFailures"/> consecutive heartbeat
    /// writes fail, indicating the client has likely disconnected.
    /// </summary>
    public event Action? OnDisconnected;

    /// <summary>
    /// Optional protocol logger. Set before calling <see cref="StartReaderLoop"/>.
    /// </summary>
    public AcpLogger? Logger { get; set; }

    /// <summary>
    /// Creates a transport using stdin/stdout.
    /// </summary>
    public static AcpTransport CreateStdio()
    {
        return new AcpTransport(Console.OpenStandardInput(), Console.OpenStandardOutput());
    }

    /// <summary>
    /// Starts the background reader loop and heartbeat that dispatches incoming messages.
    /// Must be called before using ReadRequestAsync or SendClientRequestAsync.
    /// </summary>
    public void StartReaderLoop()
    {
        _readerLoop = Task.Run(ReaderLoopAsync);
        if (HeartbeatInterval > TimeSpan.Zero && HeartbeatInterval != Timeout.InfiniteTimeSpan)
            _heartbeatLoop = Task.Run(HeartbeatLoopAsync);
    }

    /// <summary>
    /// Background loop that sends periodic <c>_dotcraft/ping</c> notifications
    /// to detect client disconnection via write failures.
    /// </summary>
    private async Task HeartbeatLoopAsync()
    {
        var ct = _disposeCts.Token;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(HeartbeatInterval, ct);

                try
                {
                    await SendNotificationAsync("_dotcraft/ping", null, ct);
                    _consecutiveHeartbeatFailures = 0;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                    _consecutiveHeartbeatFailures++;
                    Logger?.LogError(
                        $"Heartbeat write failed ({_consecutiveHeartbeatFailures}/{MaxConsecutiveHeartbeatFailures})");

                    if (_consecutiveHeartbeatFailures >= MaxConsecutiveHeartbeatFailures)
                    {
                        Logger?.LogError(
                            $"Transport disconnected after {MaxConsecutiveHeartbeatFailures} heartbeat failures");
                        OnDisconnected?.Invoke();
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException) { /* Expected on dispose */ }
    }

    private async Task ReaderLoopAsync()
    {
        var ct = _disposeCts.Token;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await _reader.ReadLineAsync(ct);
                if (line == null) break; // EOF
                if (string.IsNullOrWhiteSpace(line)) continue;

                Logger?.LogIncoming(line);

                JsonElement root;
                try
                {
                    root = JsonSerializer.Deserialize<JsonElement>(line, JsonOptions);
                }
                catch (JsonException)
                {
                    continue;
                }

                // Check if this is a response to one of our outgoing requests
                if (root.TryGetProperty("id", out var idProp) &&
                    !root.TryGetProperty("method", out _) &&
                    idProp.ValueKind == JsonValueKind.Number)
                {
                    var id = idProp.GetInt32();
                    if (_pendingClientRequests.TryRemove(id, out var tcs))
                    {
                        if (root.TryGetProperty("result", out var resultProp))
                            tcs.TrySetResult(resultProp);
                        else if (root.TryGetProperty("error", out var errorProp))
                            tcs.TrySetException(new AcpClientException(errorProp.ToString()));
                        else
                            tcs.TrySetResult(default);
                        continue;
                    }
                }

                // Otherwise, treat as an incoming request/notification
                try
                {
                    var request = JsonSerializer.Deserialize<JsonRpcRequest>(line, JsonOptions);
                    if (request != null)
                    {
                        _incomingRequests.Enqueue(request);
                        _incomingSemaphore.Release();
                    }
                }
                catch (JsonException)
                {
                    // Skip malformed
                }
            }
        }
        catch (OperationCanceledException) { /* Expected on dispose */ }
        finally
        {
            // Signal EOF to unblock ReadRequestAsync and prevent process hang
            try { await _disposeCts.CancelAsync(); } catch { /* already disposed */ }
        }
    }

    /// <summary>
    /// Reads the next Client→Agent request/notification.
    /// Returns null on EOF / cancellation.
    /// </summary>
    public async Task<JsonRpcRequest?> ReadRequestAsync(CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
        try
        {
            await _incomingSemaphore.WaitAsync(linked.Token);
            if (_incomingRequests.TryDequeue(out var request))
                return request;
        }
        catch (OperationCanceledException) { /* Expected */ }

        return null;
    }

    /// <summary>
    /// Sends a JSON-RPC response.
    /// </summary>
    public async Task SendResponseAsync(JsonElement? id, object? result, CancellationToken ct = default)
    {
        var response = new JsonRpcResponse { Id = id, Result = result };
        await WriteLineAsync(response, ct);
    }

    /// <summary>
    /// Sends a JSON-RPC error response.
    /// </summary>
    public async Task SendErrorAsync(JsonElement? id, int code, string message, object? data = null, CancellationToken ct = default)
    {
        var response = new JsonRpcResponse
        {
            Id = id,
            Error = new JsonRpcError { Code = code, Message = message, Data = data }
        };
        await WriteLineAsync(response, ct);
    }

    /// <summary>
    /// Sends a JSON-RPC notification (no id, no response expected).
    /// </summary>
    public async Task SendNotificationAsync(string method, object? @params = null, CancellationToken ct = default)
    {
        var notification = new JsonRpcNotification { Method = method, Params = @params };
        await WriteLineAsync(notification, ct);
    }

    /// <summary>
    /// Sends a JSON-RPC request to the client.
    /// </summary>
    private async Task SendRequestAsync(int id, string method, object? @params = null, CancellationToken ct = default)
    {
        var request = new
        {
            jsonrpc = "2.0",
            id,
            method,
            @params
        };
        await WriteLineAsync(request, ct);
    }

    /// <summary>
    /// Sends a JSON-RPC request to the client and awaits the response.
    /// Used for Agent→Client method calls (fs/readTextFile, terminal/create, etc.).
    /// An optional <paramref name="timeout"/> overrides the default 30-second limit.
    /// </summary>
    public async Task<JsonElement> SendClientRequestAsync(string method, object? @params, CancellationToken ct = default, TimeSpan? timeout = null)
    {
        var id = Interlocked.Increment(ref _nextOutgoingId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingClientRequests[id] = tcs;

        try
        {
            await SendRequestAsync(id, method, @params, ct);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(30));
            await using var reg = cts.Token.Register(() => tcs.TrySetCanceled());
            return await tcs.Task;
        }
        finally
        {
            _pendingClientRequests.TryRemove(id, out _);
        }
    }

    /// <summary>
    /// Serializes <paramref name="message"/> to JSON and writes it as a newline-delimited
    /// line to the output stream. Acquires the write semaphore asynchronously and applies
    /// <see cref="WriteTimeout"/> to prevent indefinite blocking when the client pipe is full.
    /// </summary>
    private async Task WriteLineAsync(object message, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);
        Logger?.LogOutgoing(json);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
        if (_writeTimeout != Timeout.InfiniteTimeSpan)
            timeoutCts.CancelAfter(_writeTimeout);

        try
        {
            await _writeLock.WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            Logger?.LogError($"Write timeout ({_writeTimeout.TotalSeconds:F0}s) sending: {(json.Length > 200 ? json[..200] + "..." : json)}");
            throw;
        }

        try
        {
            await _writer.WriteLineAsync(json.AsMemory(), timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            Logger?.LogError($"Write blocked ({_writeTimeout.TotalSeconds:F0}s) on pipe: {(json.Length > 200 ? json[..200] + "..." : json)}");
            throw;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _disposeCts.CancelAsync();
        _reader.Dispose();
        await _writeLock.WaitAsync();
        try
        {
            _writer.Dispose();
        }
        finally
        {
            _writeLock.Release();
        }
        if (_readerLoop != null)
        {
            try { await _readerLoop; } catch { /* Expected */ }
        }
        if (_heartbeatLoop != null)
        {
            try { await _heartbeatLoop; } catch { /* Expected */ }
        }
        _disposeCts.Dispose();
        _incomingSemaphore.Dispose();
        _writeLock.Dispose();
    }
}

public sealed class AcpClientException(string message) : Exception(message);
