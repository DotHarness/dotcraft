using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using DotCraft.Sdk.AppServer;

namespace DotCraft.Sdk.Wire;

/// <summary>
/// Transport-agnostic JSON-RPC client for DotCraft AppServer.
/// </summary>
public sealed class DotCraftWireClient : IAsyncDisposable
{
    private readonly IJsonRpcTransport _transport;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly ConcurrentDictionary<string, Func<ServerRequest, CancellationToken, Task<object?>>> _serverRequestHandlers = new(StringComparer.Ordinal);
    private readonly Channel<AppServerNotification> _notifications = Channel.CreateUnbounded<AppServerNotification>();
    private readonly CancellationTokenSource _disposeCts = new();
    private long _nextId;
    private Task? _readerTask;
    private bool _disposed;

    /// <summary>
    /// Creates a wire client over an already-created JSON-RPC transport.
    /// </summary>
    public DotCraftWireClient(IJsonRpcTransport transport)
    {
        _transport = transport;
    }

    /// <summary>
    /// Starts the background reader loop.
    /// </summary>
    public void Start()
    {
        _readerTask ??= Task.Run(ReadLoopAsync);
    }

    /// <summary>
    /// Registers a handler for one server-initiated JSON-RPC request method.
    /// </summary>
    public IDisposable RegisterServerRequestHandler(
        string method,
        Func<ServerRequest, CancellationToken, Task<object?>> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        _serverRequestHandlers[method] = handler;
        return new DisposableAction(() => _serverRequestHandlers.TryRemove(method, out _));
    }

    /// <summary>
    /// Performs the AppServer initialize / initialized handshake.
    /// </summary>
    public async Task<AppServerInitializeResult> InitializeAsync(
        DotCraftClientOptions options,
        CancellationToken cancellationToken = default)
    {
        var capabilities = new Dictionary<string, object?>
        {
            ["approvalSupport"] = options.ApprovalSupport,
            ["requestUserInputSupport"] = options.RequestUserInputSupport,
            ["streamingSupport"] = options.StreamingSupport,
            ["configChange"] = options.ConfigChange
        };
        if (options.ExtraCapabilities is not null)
        {
            foreach (var pair in options.ExtraCapabilities)
            {
                capabilities[pair.Key] = pair.Value;
            }
        }

        var result = await SendRequestAsync("initialize", new
        {
            clientInfo = new
            {
                name = options.ClientName,
                title = options.ClientTitle,
                version = options.ClientVersion
            },
            capabilities
        }, cancellationToken);
        await SendNotificationAsync("initialized", new { }, cancellationToken);
        return ParseInitializeResult(result);
    }

    /// <summary>
    /// Sends a JSON-RPC request and returns the raw result JSON.
    /// </summary>
    public async Task<JsonElement> SendRequestAsync(
        string method,
        object? parameters = null,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        ThrowIfDisposed();
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        try
        {
            await _transport.WriteAsync(new
            {
                jsonrpc = "2.0",
                id,
                method,
                @params = parameters ?? new { }
            }, cancellationToken);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token, cancellationToken);
            cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(30));
            await using var registration = cts.Token.Register(() => tcs.TrySetCanceled(cts.Token));
            return await tcs.Task;
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    /// <summary>
    /// Sends a JSON-RPC notification.
    /// </summary>
    public Task SendNotificationAsync(
        string method,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _transport.WriteAsync(new
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
                    break;
                }

                await DispatchAsync(document);
            }
        }
        catch (Exception ex)
        {
            foreach (var pending in _pending.Values)
            {
                pending.TrySetException(ex);
            }
        }
        finally
        {
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

            _notifications.Writer.TryWrite(new AppServerNotification(method, parameters));
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

    private static AppServerInitializeResult ParseInitializeResult(JsonElement result)
    {
        var serverInfo = result.TryGetProperty("serverInfo", out var serverInfoElement)
            ? serverInfoElement
            : default;
        var capabilities = result.TryGetProperty("capabilities", out var capabilitiesElement)
            ? capabilitiesElement
            : default;

        return new AppServerInitializeResult(
            new AppServerServerInfo(
                JsonElementReaders.ReadString(serverInfo, "name") ?? "dotcraft",
                JsonElementReaders.ReadString(serverInfo, "version") ?? "",
                JsonElementReaders.ReadString(serverInfo, "protocolVersion") ?? "",
                ReadStringArray(serverInfo, "extensions")),
            new AppServerServerCapabilities(
                ReadBoolean(capabilities, "threadManagement"),
                ReadBoolean(capabilities, "threadSubscriptions"),
                ReadBoolean(capabilities, "dynamicToolRebind"),
                ReadBoolean(capabilities, "appBinding"),
                ReadBoolean(capabilities, "modelCatalogManagement"),
                capabilities.ValueKind == JsonValueKind.Undefined
                    ? JsonSerializer.SerializeToElement(new { }, DotCraftJson.Options)
                    : capabilities.Clone()),
            result.Clone());
    }

    private static bool ReadBoolean(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.True;

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var values) ||
            values.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return values
            .EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString()!)
            .ToArray();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DotCraftWireClient));
        }
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
