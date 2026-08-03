using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using DotCraft.Protocol.Contracts;
using DotCraft.Protocol.Contracts.AppServer;
using DotCraft.Sdk.AppServer;

namespace DotCraft.Sdk.Wire;

/// <summary>
/// Transport-agnostic JSON-RPC client for DotCraft AppServer.
/// </summary>
public sealed class DotCraftWireClient : IAsyncDisposable
{
    private readonly IJsonRpcTransport _transport;
    private readonly DotCraftWireClientOptions _options;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly ConcurrentDictionary<string, Func<ServerRequest, CancellationToken, Task<object?>>> _serverRequestHandlers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<long, Action<AppServerNotification>> _notificationHandlers = new();
    private readonly Channel<AppServerNotification> _notifications = Channel.CreateBounded<AppServerNotification>(
        new BoundedChannelOptions(1024) { FullMode = BoundedChannelFullMode.DropOldest, SingleWriter = true });
    private readonly CancellationTokenSource _disposeCts = new();
    private long _nextId;
    private long _nextNotificationHandlerId;
    private Task? _readerTask;
    private TaskCompletionSource _ready = CompletedReadySource();
    private DotCraftClientOptions? _initializeOptions;
    private int _queuedRequests;
    private long _connectionGeneration;
    private bool _disposed;

    /// <summary>Current observable Wire session state.</summary>
    public WireConnectionState State { get; private set; } = WireConnectionState.Disconnected;

    /// <summary>
    /// Monotonically increasing initialized Wire session generation. The value changes only after
    /// a successful initialize handshake, including one performed during reconnect.
    /// </summary>
    public long ConnectionGeneration => Interlocked.Read(ref _connectionGeneration);

    /// <summary>Raised when <see cref="State"/> changes.</summary>
    public event Action<WireConnectionState, Exception?>? StateChanged;

    /// <summary>
    /// Creates a wire client over an already-created JSON-RPC transport.
    /// </summary>
    public DotCraftWireClient(IJsonRpcTransport transport, DotCraftWireClientOptions? options = null)
    {
        _transport = transport;
        _options = options ?? new DotCraftWireClientOptions();
    }

    /// <summary>
    /// Starts the background reader loop.
    /// </summary>
    public void Start()
    {
        _readerTask ??= Task.Run(ReadLoopAsync);
        SetState(WireConnectionState.Ready);
    }

    /// <summary>
    /// Registers a handler for one server-initiated JSON-RPC request method.
    /// </summary>
    public IDisposable RegisterServerRequestHandlerRaw(
        string method,
        Func<ServerRequest, CancellationToken, Task<object?>> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        _serverRequestHandlers[method] = handler;
        return new DisposableAction(() => _serverRequestHandlers.TryRemove(method, out _));
    }

    /// <summary>Registers a typed handler for one known server-initiated request.</summary>
    public IDisposable RegisterServerRequestHandler<TParams, TResult>(
        RpcRequest<TParams, TResult> descriptor,
        Func<TParams, CancellationToken, Task<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(handler);
        if (descriptor.Direction != RpcDirection.ServerToClient)
        {
            throw new ArgumentException("The descriptor must be a server-to-client request.", nameof(descriptor));
        }

        return RegisterServerRequestHandlerRaw(descriptor.Name, async (request, cancellationToken) =>
        {
            var parameters = request.Params.Deserialize<TParams>(DotCraftJson.Options)
                ?? throw new JsonException($"Request '{descriptor.Name}' had invalid parameters.");
            return await handler(parameters, cancellationToken);
        });
    }

    /// <summary>
    /// Registers a handler that receives every AppServer notification. Handlers must be fast and
    /// non-blocking; they run on the wire read loop. Returns a disposable that unregisters the handler.
    /// </summary>
    public IDisposable RegisterNotificationHandlerRaw(Action<AppServerNotification> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var id = Interlocked.Increment(ref _nextNotificationHandlerId);
        _notificationHandlers[id] = handler;
        return new DisposableAction(() => _notificationHandlers.TryRemove(id, out _));
    }

    /// <summary>Registers a typed handler for one known server notification.</summary>
    public IDisposable RegisterNotificationHandler<TParams>(
        RpcNotification<TParams> descriptor,
        Action<TParams> handler)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(handler);
        if (descriptor.Direction != RpcDirection.ServerToClient)
        {
            throw new ArgumentException("The descriptor must be a server-to-client notification.", nameof(descriptor));
        }

        return RegisterNotificationHandlerRaw(notification =>
        {
            if (!string.Equals(notification.Method, descriptor.Name, StringComparison.Ordinal))
            {
                return;
            }

            var parameters = notification.Params.Deserialize<TParams>(DotCraftJson.Options)
                ?? throw new JsonException($"Notification '{descriptor.Name}' had invalid parameters.");
            handler(parameters);
        });
    }

    /// <summary>
    /// Performs the AppServer initialize / initialized handshake.
    /// </summary>
    public async Task<InitializeResult> InitializeAsync(
        DotCraftClientOptions options,
        CancellationToken cancellationToken = default)
    {
        _initializeOptions = options;
        return await InitializeCoreAsync(options, cancellationToken, bypassReady: false);
    }

    private async Task<InitializeResult> InitializeCoreAsync(
        DotCraftClientOptions options,
        CancellationToken cancellationToken,
        bool bypassReady)
    {
        SetState(WireConnectionState.Initializing);
        var contractCapabilities = new ClientCapabilities
        {
            ApprovalSupport = options.ApprovalSupport,
            RequestUserInputSupport = options.RequestUserInputSupport,
            StreamingSupport = options.StreamingSupport,
            ConfigChange = options.ConfigChange,
            AppBindingVersion = 2,
            ExtensionData = options.ExtraCapabilities?.ToDictionary(
                pair => pair.Key,
                pair => JsonSerializer.SerializeToElement(pair.Value, DotCraftJson.Options))
        };
        var parameters = new InitializeParams
        {
            ClientInfo = new ClientInfo
            {
                Name = options.ClientName,
                Title = options.ClientTitle,
                Version = options.ClientVersion
            },
            Capabilities = contractCapabilities
        };
        var rawResult = await RequestRawCoreAsync(
            AppServerRpc.Initialize.Name,
            parameters,
            cancellationToken,
            Timeout.InfiniteTimeSpan,
            bypassReady);
        var result = rawResult.Deserialize<InitializeResult>(DotCraftJson.Options)
            ?? throw new JsonException("Request 'initialize' returned an invalid result.");
        await NotifyRawCoreAsync(AppServerRpc.Initialized.Name, new RpcEmpty(), cancellationToken, bypassReady);
        Interlocked.Increment(ref _connectionGeneration);
        _ready.TrySetResult();
        SetState(WireConnectionState.Ready);
        return result;
    }

    /// <summary>Sends one cataloged client request and deserializes its typed result.</summary>
    public async Task<TResult> RequestAsync<TParams, TResult>(
        RpcRequest<TParams, TResult> descriptor,
        TParams parameters,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.Direction != RpcDirection.ClientToServer)
        {
            throw new ArgumentException("The descriptor must be a client-to-server request.", nameof(descriptor));
        }

        var result = await RequestRawAsync(descriptor.Name, parameters, cancellationToken, timeout);
        return result.Deserialize<TResult>(DotCraftJson.Options)
            ?? throw new JsonException($"Request '{descriptor.Name}' returned an invalid result.");
    }

    /// <summary>Sends one cataloged client notification.</summary>
    public Task NotifyAsync<TParams>(
        RpcNotification<TParams> descriptor,
        TParams parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.Direction != RpcDirection.ClientToServer)
        {
            throw new ArgumentException("The descriptor must be a client-to-server notification.", nameof(descriptor));
        }

        return NotifyRawAsync(descriptor.Name, parameters, cancellationToken);
    }

    /// <summary>
    /// Sends a JSON-RPC request and returns the raw result JSON.
    /// </summary>
    public async Task<JsonElement> RequestRawAsync(
        string method,
        object? parameters = null,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        return await RequestRawCoreAsync(method, parameters, cancellationToken, timeout, bypassReady: false);
    }

    private async Task<JsonElement> RequestRawCoreAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken,
        TimeSpan? timeout,
        bool bypassReady)
    {
        ThrowIfDisposed();
        var effectiveTimeout = timeout ?? _options.DefaultRequestTimeout;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token, cancellationToken);
        if (effectiveTimeout != Timeout.InfiniteTimeSpan)
        {
            cts.CancelAfter(effectiveTimeout);
        }

        if (!bypassReady && !_ready.Task.IsCompletedSuccessfully)
        {
            if (Interlocked.Increment(ref _queuedRequests) > _options.MaxReconnectQueueSize)
            {
                Interlocked.Decrement(ref _queuedRequests);
                throw new WireReconnectQueueFullException(_options.MaxReconnectQueueSize);
            }
            try
            {
                await _ready.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException) when (
                effectiveTimeout != Timeout.InfiniteTimeSpan &&
                !cancellationToken.IsCancellationRequested &&
                !_disposeCts.IsCancellationRequested)
            {
                throw new WireRequestTimeoutException(method, effectiveTimeout);
            }
            finally
            {
                Interlocked.Decrement(ref _queuedRequests);
            }
        }

        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        try
        {
            var request = new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["method"] = method
            };
            if (parameters is not null)
                request["params"] = parameters;
            await _transport.WriteAsync(request, cts.Token);
            await using var registration = cts.Token.Register(() => tcs.TrySetCanceled(cts.Token));
            try
            {
                return await tcs.Task;
            }
            catch (OperationCanceledException) when (
                effectiveTimeout != Timeout.InfiniteTimeSpan &&
                !cancellationToken.IsCancellationRequested &&
                !_disposeCts.IsCancellationRequested)
            {
                throw new WireRequestTimeoutException(method, effectiveTimeout);
            }
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    /// <summary>
    /// Sends a JSON-RPC notification.
    /// </summary>
    public Task NotifyRawAsync(
        string method,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        return NotifyRawCoreAsync(method, parameters, cancellationToken, bypassReady: false);
    }

    private async Task NotifyRawCoreAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken,
        bool bypassReady)
    {
        ThrowIfDisposed();
        if (!bypassReady)
        {
            await _ready.Task.WaitAsync(cancellationToken);
        }
        await _transport.WriteAsync(new
        {
            jsonrpc = "2.0",
            method,
            @params = parameters ?? new { }
        }, cancellationToken);
    }

    /// <summary>
    /// Reads AppServer notifications until the connection closes or cancellation is requested.
    /// </summary>
    public IAsyncEnumerable<AppServerNotification> ReadNotificationsAsync(CancellationToken cancellationToken = default) =>
        _notifications.Reader.ReadAllAsync(cancellationToken);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SetState(WireConnectionState.Closed);
        await _disposeCts.CancelAsync();
        await _transport.DisposeAsync();
        if (_readerTask is not null)
        {
            try
            {
                await _readerTask;
            }
            catch
            {
                // Reader shutdown is best-effort.
            }
        }

        _disposeCts.Dispose();
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (!_disposeCts.IsCancellationRequested)
            {
                JsonDocument? document;
                try
                {
                    document = await _transport.ReadAsync(_disposeCts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (document is null)
                {
                    var closed = new IOException("Wire transport disconnected.");
                    FailPending(closed);
                    SetState(WireConnectionState.Disconnected, closed);
                    if (await TryReconnectAsync(closed))
                    {
                        continue;
                    }
                    break;
                }

                await DispatchAsync(document);
            }
        }
        catch (Exception ex)
        {
            FailPending(ex);
            SetState(WireConnectionState.Disconnected, ex);
            if (await TryReconnectAsync(ex))
            {
                await ReadLoopAsync();
            }
        }
        finally
        {
            if (!_disposed && State != WireConnectionState.Disconnected)
            {
                SetState(WireConnectionState.Disconnected);
            }
            _notifications.Writer.TryComplete();
            foreach (var pending in _pending.Values)
            {
                pending.TrySetCanceled();
            }
        }
    }

    private async Task DispatchAsync(JsonDocument document)
    {
        var root = document.RootElement;
        var hasMethod = root.TryGetProperty("method", out var methodElement);
        var hasId = root.TryGetProperty("id", out var idElement) &&
                    idElement.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;

        if (!hasMethod && hasId)
        {
            DispatchResponse(root, idElement);
            return;
        }

        if (hasMethod)
        {
            var method = methodElement.GetString() ?? string.Empty;
            var parameters = root.TryGetProperty("params", out var paramsElement)
                ? paramsElement.Clone()
                : JsonSerializer.SerializeToElement(new { }, DotCraftJson.Options);

            if (hasId)
            {
                await DispatchServerRequestAsync(idElement.Clone(), method, parameters);
                return;
            }

            var notification = new AppServerNotification(method, parameters);
            foreach (var handler in _notificationHandlers.Values)
            {
                try
                {
                    handler(notification);
                }
                catch
                {
                    // Notification handlers must not break the read loop.
                }
            }

            _notifications.Writer.TryWrite(notification);
        }
    }

    private void DispatchResponse(JsonElement root, JsonElement idElement)
    {
        if (idElement.ValueKind != JsonValueKind.Number || !idElement.TryGetInt64(out var id))
        {
            return;
        }

        if (!_pending.TryRemove(id, out var tcs))
        {
            return;
        }

        if (root.TryGetProperty("error", out var errorElement))
        {
            tcs.TrySetException(JsonRpcException.FromError(errorElement));
            return;
        }

        var result = root.TryGetProperty("result", out var resultElement)
            ? resultElement.Clone()
            : JsonSerializer.SerializeToElement(new { }, DotCraftJson.Options);
        tcs.TrySetResult(result);
    }

    private async Task DispatchServerRequestAsync(JsonElement id, string method, JsonElement parameters)
    {
        if (!_serverRequestHandlers.TryGetValue(method, out var handler))
        {
            await SendServerErrorAsync(id, -32601, $"Method not handled: {method}");
            return;
        }

        try
        {
            var result = await handler(new ServerRequest(id, method, parameters), _disposeCts.Token);
            await _transport.WriteAsync(new
            {
                jsonrpc = "2.0",
                id,
                result = result ?? new { }
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            await SendServerErrorAsync(id, -32603, ex.Message);
        }
    }

    private Task SendServerErrorAsync(JsonElement id, int code, string message) =>
        _transport.WriteAsync(new
        {
            jsonrpc = "2.0",
            id,
            error = new { code, message }
        }, CancellationToken.None);

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DotCraftWireClient));
        }
    }

    private async Task<bool> TryReconnectAsync(Exception disconnectError)
    {
        if (_disposed || !_options.AutoReconnect ||
            _initializeOptions is null || _transport is not IReconnectableJsonRpcTransport reconnectable)
        {
            return false;
        }

        _ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delay = _options.ReconnectInitialDelay;
        SetState(WireConnectionState.Reconnecting, disconnectError);
        while (!_disposeCts.IsCancellationRequested)
        {
            var jitter = 0.8 + Random.Shared.NextDouble() * 0.4;
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(delay.TotalMilliseconds * jitter), _disposeCts.Token);
                await reconnectable.ReconnectAsync(_disposeCts.Token);
                var handshake = InitializeCoreAsync(_initializeOptions, _disposeCts.Token, bypassReady: true);
                while (!handshake.IsCompleted)
                {
                    using var message = await _transport.ReadAsync(_disposeCts.Token)
                        ?? throw new IOException("Wire transport closed during reconnect initialization.");
                    await DispatchAsync(message);
                    await Task.WhenAny(handshake, Task.Delay(1, _disposeCts.Token));
                }
                await handshake;
                return true;
            }
            catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception reconnectError)
            {
                FailPending(reconnectError);
                SetState(WireConnectionState.ReconnectError, reconnectError);
                delay = TimeSpan.FromMilliseconds(Math.Min(
                    delay.TotalMilliseconds * 2,
                    _options.ReconnectMaxDelay.TotalMilliseconds));
                SetState(WireConnectionState.Reconnecting, reconnectError);
            }
        }
        return false;
    }

    private void FailPending(Exception error)
    {
        foreach (var pending in _pending.Values)
        {
            pending.TrySetException(error);
        }
        _pending.Clear();
    }

    private void SetState(WireConnectionState state, Exception? error = null)
    {
        State = state;
        StateChanged?.Invoke(state, error);
    }

    private static TaskCompletionSource CompletedReadySource()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }

    private sealed class DisposableAction(Action dispose) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                dispose();
            }
        }
    }
}

/// <summary>
/// Server-initiated JSON-RPC request.
/// </summary>
public sealed record ServerRequest(JsonElement Id, string Method, JsonElement Params);
