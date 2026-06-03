using System.Collections.Concurrent;
using System.Text.Json;
using DotCraft.Sdk.AppBinding;
using DotCraft.Sdk.Hub;
using DotCraft.Sdk.Wire;

namespace DotCraft.Sdk.AppServer;

/// <summary>
/// High-level DotCraft AppServer SDK client.
/// </summary>
public sealed class DotCraftClient : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, Func<DynamicToolCall, CancellationToken, Task<DynamicToolResult>>> _dynamicToolHandlers = new(StringComparer.Ordinal);
    private Func<DynamicToolCall, CancellationToken, Task<DynamicToolResult>>? _fallbackDynamicToolHandler;
    private readonly ApprovalHandler? _approvalHandler;
    private readonly UserInputHandler? _userInputHandler;

    private DotCraftClient(DotCraftWireClient wire, AppServerInitializeResult initializeResult, DotCraftClientOptions options)
    {
        Wire = wire;
        ServerInfo = initializeResult.ServerInfo;
        Capabilities = initializeResult.Capabilities;
        _approvalHandler = options.ApprovalHandler;
        _userInputHandler = options.UserInputHandler;
        Threads = new DotCraftThreadClient(this);
        Turns = new DotCraftTurnClient(this);
        Models = new DotCraftModelClient(this);
        AppBindings = new DotCraftAppBindingClient(this);
        Wire.RegisterServerRequestHandler("item/tool/call", HandleDynamicToolCallAsync);
        Wire.RegisterServerRequestHandler("item/approval/request", HandleApprovalRequestAsync);
        Wire.RegisterServerRequestHandler("item/tool/requestUserInput", HandleUserInputRequestAsync);
    }

    /// <summary>
    /// Low-level JSON-RPC wire client.
    /// </summary>
    public DotCraftWireClient Wire { get; }

    /// <summary>
    /// Server identity returned during initialize.
    /// </summary>
    public AppServerServerInfo ServerInfo { get; }

    /// <summary>
    /// Server capabilities returned during initialize.
    /// </summary>
    public AppServerServerCapabilities Capabilities { get; }

    /// <summary>
    /// Thread operations.
    /// </summary>
    public DotCraftThreadClient Threads { get; }

    /// <summary>
    /// Turn operations.
    /// </summary>
    public DotCraftTurnClient Turns { get; }

    /// <summary>
    /// Model catalog operations.
    /// </summary>
    public DotCraftModelClient Models { get; }

    /// <summary>
    /// App Binding operations.
    /// </summary>
    public DotCraftAppBindingClient AppBindings { get; }

    /// <summary>
    /// Connects to a Hub-managed workspace AppServer.
    /// </summary>
    public static async Task<DotCraftClient> ConnectLocalAsync(
        string workspacePath,
        DotCraftLocalClientOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        var value = options ?? new DotCraftLocalClientOptions();
        var hub = new HubClient(new DotCraftHubClientOptions
        {
            DotCraftBin = value.DotCraftBin,
            HubLockPath = value.HubLockPath,
            StartupTimeout = value.HubStartupTimeout,
            StartHubIfMissing = true
        });
        var ensured = await hub.EnsureAppServerAsync(
            workspacePath,
            new HubEnsureAppServerOptions
            {
                Client = new HubClientInfo
                {
                    Name = value.ClientName,
                    Version = value.ClientVersion
                },
                StartIfMissing = true
            },
            cancellationToken);
        if (!ensured.Endpoints.TryGetValue("appServerWebSocket", out var wsUrl) || string.IsNullOrWhiteSpace(wsUrl))
        {
            throw new InvalidOperationException("Hub response did not include endpoints.appServerWebSocket.");
        }

        return await ConnectRemoteAsync(wsUrl, token: null, value, cancellationToken);
    }

    /// <summary>
    /// Connects directly to an AppServer WebSocket endpoint.
    /// </summary>
    public static async Task<DotCraftClient> ConnectRemoteAsync(
        string appServerUrl,
        string? token = null,
        DotCraftClientOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var transport = await WebSocketJsonRpcTransport.ConnectAsync(appServerUrl, token, cancellationToken);
        return await ConnectAsync(transport, options, cancellationToken);
    }

    /// <summary>
    /// Connects over a custom transport.
    /// </summary>
    public static async Task<DotCraftClient> ConnectAsync(
        IJsonRpcTransport transport,
        DotCraftClientOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveOptions = options ?? new DotCraftClientOptions();
        if (effectiveOptions.UserInputHandler is not null)
        {
            effectiveOptions.RequestUserInputSupport = true;
        }

        var wire = new DotCraftWireClient(transport);
        wire.Start();
        var initializeResult = await wire.InitializeAsync(effectiveOptions, cancellationToken);
        return new DotCraftClient(wire, initializeResult, effectiveOptions);
    }

    /// <summary>
    /// Sends a raw AppServer request and deserializes the result.
    /// </summary>
    public async Task<T> RequestAsync<T>(
        string method,
        object? parameters = null,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        var result = await Wire.SendRequestAsync(method, parameters, cancellationToken, timeout);
        return result.Deserialize<T>(DotCraftJson.Options)
               ?? throw new InvalidOperationException($"DotCraft AppServer returned an empty result for '{method}'.");
    }

    /// <summary>
    /// Sends a raw AppServer request and returns the result JSON.
    /// </summary>
    public Task<JsonElement> RequestAsync(
        string method,
        object? parameters = null,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null) =>
        Wire.SendRequestAsync(method, parameters, cancellationToken, timeout);

    /// <summary>
    /// Registers one catch-all runtime dynamic tool handler.
    /// </summary>
    public IDisposable RegisterDynamicToolHandler(
        Func<DynamicToolCall, CancellationToken, Task<DynamicToolResult>> handler)
    {
        _fallbackDynamicToolHandler = handler;
        return new DisposableAction(() =>
        {
            if (ReferenceEquals(_fallbackDynamicToolHandler, handler))
            {
                _fallbackDynamicToolHandler = null;
            }
        });
    }

    /// <summary>
    /// Registers a runtime dynamic tool handler for a specific thread/namespace/tool.
    /// </summary>
    public IDisposable RegisterDynamicToolHandler(
        string threadId,
        string? @namespace,
        string toolName,
        Func<DynamicToolCall, CancellationToken, Task<DynamicToolResult>> handler)
    {
        var key = ToolKey(threadId, @namespace, toolName);
        _dynamicToolHandlers[key] = handler;
        return new DisposableAction(() => _dynamicToolHandlers.TryRemove(key, out _));
    }

    /// <summary>
    /// Reads AppServer notifications.
    /// </summary>
    public IAsyncEnumerable<AppServerNotification> ReadNotificationsAsync(CancellationToken cancellationToken = default) =>
        Wire.ReadNotificationsAsync(cancellationToken);

    /// <inheritdoc />
    public ValueTask DisposeAsync() => Wire.DisposeAsync();

    private async Task<object?> HandleDynamicToolCallAsync(ServerRequest request, CancellationToken cancellationToken)
    {
        var parameters = request.Params;
        var call = new DynamicToolCall(
            JsonElementReaders.ReadString(parameters, "threadId") ?? string.Empty,
            JsonElementReaders.ReadString(parameters, "turnId"),
            JsonElementReaders.ReadString(parameters, "callId"),
            JsonElementReaders.ReadString(parameters, "namespace"),
            JsonElementReaders.ReadString(parameters, "tool") ?? string.Empty,
            parameters.TryGetProperty("arguments", out var args)
                ? args.Clone()
                : JsonSerializer.SerializeToElement(new { }, DotCraftJson.Options));

        var key = ToolKey(call.ThreadId, call.Namespace, call.Tool);
        var handler = _dynamicToolHandlers.TryGetValue(key, out var keyed)
            ? keyed
            : _fallbackDynamicToolHandler;
        if (handler is null)
        {
            return new DynamicToolResult(
                false,
                ErrorCode: "UnsupportedTool",
                ErrorMessage: "No handler registered for this runtime dynamic tool.");
        }

        try
        {
            return await handler(call, cancellationToken);
        }
        catch (Exception ex)
        {
            return new DynamicToolResult(
                false,
                ErrorCode: "AdapterToolCallFailed",
                ErrorMessage: ex.Message);
        }
    }

    private async Task<object?> HandleApprovalRequestAsync(ServerRequest request, CancellationToken cancellationToken)
    {
        if (_approvalHandler is null)
        {
            return new { decision = ApprovalDecision.Accept.Value };
        }

        var approval = new ApprovalRequest(
            JsonElementReaders.ReadString(request.Params, "threadId") ?? string.Empty,
            JsonElementReaders.ReadString(request.Params, "turnId"),
            JsonElementReaders.ReadString(request.Params, "callId"),
            request.Params);
        var decision = await _approvalHandler(approval, cancellationToken);
        return new { decision = decision.Value };
    }

    private async Task<object?> HandleUserInputRequestAsync(ServerRequest request, CancellationToken cancellationToken)
    {
        if (_userInputHandler is null)
        {
            return new { answers = new Dictionary<string, object?>() };
        }

        var input = new UserInputRequest(
            JsonElementReaders.ReadString(request.Params, "threadId") ?? string.Empty,
            JsonElementReaders.ReadString(request.Params, "turnId"),
            request.Params);
        var response = await _userInputHandler(input, cancellationToken);
        return new { answers = response.Answers };
    }

    private static string ToolKey(string threadId, string? @namespace, string toolName) =>
        $"{threadId}\u0000{@namespace ?? string.Empty}\u0000{toolName}";

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
/// Thread operation surface.
/// </summary>
public sealed class DotCraftThreadClient(DotCraftClient client)
{
    /// <summary>
    /// Starts a new thread.
    /// </summary>
    public async Task<DotCraftThread> StartAsync(DotCraftThreadStartRequest request, CancellationToken cancellationToken = default)
    {
        var result = await client.RequestAsync("thread/start", new
        {
            identity = ToIdentityPayload(request.Identity),
            request.DisplayName,
            request.HistoryMode,
            request.DynamicTools,
            request.AdditionalContext,
            config = request.Config
        }, cancellationToken);
        var thread = result.TryGetProperty("thread", out var threadElement) ? threadElement : result;
        var id = JsonElementReaders.ReadString(thread, "id")
                 ?? JsonElementReaders.ReadString(thread, "threadId")
                 ?? JsonElementReaders.ReadString(result, "threadId")
                 ?? JsonElementReaders.ReadNestedString(result, "thread", "id")
                 ?? throw new InvalidOperationException("DotCraft AppServer did not return a thread id.");
        return new DotCraftThread(client, id, thread.Clone());
    }

    /// <summary>
    /// Resumes an existing thread.
    /// </summary>
    public async Task<DotCraftThread> ResumeAsync(DotCraftThreadResumeRequest request, CancellationToken cancellationToken = default)
    {
        var result = await client.RequestAsync("thread/resume", new
        {
            request.ThreadId,
            request.DynamicTools,
            request.AdditionalContext
        }, cancellationToken);
        var thread = result.TryGetProperty("thread", out var threadElement) ? threadElement : result;
        var id = JsonElementReaders.ReadString(thread, "id")
                 ?? JsonElementReaders.ReadString(thread, "threadId")
                 ?? request.ThreadId;
        return new DotCraftThread(client, id, thread.Clone());
    }

    /// <summary>
    /// Lists threads visible to the connected workspace.
    /// </summary>
    public async Task<IReadOnlyList<DotCraftThreadSummary>> ListAsync(bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var result = await client.RequestAsync("thread/list", new { includeArchived }, cancellationToken);
        var array = result.TryGetProperty("threads", out var threads)
            ? threads
            : result.TryGetProperty("data", out var data)
                ? data
                : result.TryGetProperty("items", out var items)
                    ? items
                    : result;
        if (array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return array.EnumerateArray()
            .Where(thread => thread.ValueKind == JsonValueKind.Object)
            .Select(thread => new DotCraftThreadSummary(
                JsonElementReaders.ReadString(thread, "id")
                ?? JsonElementReaders.ReadString(thread, "threadId")
                ?? string.Empty,
                JsonElementReaders.ReadString(thread, "status"),
                JsonElementReaders.ReadString(thread, "displayName"),
                thread.Clone()))
            .Where(summary => summary.Id.Length > 0)
            .ToArray();
    }

    /// <summary>
    /// Subscribes this connection to thread events.
    /// </summary>
    public Task SubscribeAsync(string threadId, bool replayRecent = false, CancellationToken cancellationToken = default) =>
        client.RequestAsync("thread/subscribe", new { threadId, replayRecent }, cancellationToken);

    /// <summary>
    /// Reads a thread snapshot.
    /// </summary>
    public async Task<DotCraftThreadReadResult> ReadAsync(
        string threadId,
        bool includeTurns = false,
        int? turnLimit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["threadId"] = threadId,
            ["includeTurns"] = includeTurns
        };
        if (turnLimit.HasValue)
            payload["turnLimit"] = turnLimit.Value;
        if (!string.IsNullOrWhiteSpace(cursor))
            payload["cursor"] = cursor;

        var result = await client.RequestAsync("thread/read", payload, cancellationToken);
        var thread = result.TryGetProperty("thread", out var threadElement) ? threadElement : result;
        JsonElement? turnPage = result.TryGetProperty("turnPage", out var turnPageElement)
            ? turnPageElement.Clone()
            : null;
        var id = JsonElementReaders.ReadString(thread, "id") ?? JsonElementReaders.ReadString(thread, "threadId") ?? threadId;
        return new DotCraftThreadReadResult(id, thread.Clone(), turnPage);
    }

    private static object ToIdentityPayload(SessionIdentity identity) => new
    {
        channelName = identity.ChannelName,
        userId = identity.UserId,
        workspacePath = identity.WorkspacePath,
        channelContext = identity.ChannelContext
    };
}

/// <summary>
/// Turn operation surface.
/// </summary>
public sealed class DotCraftTurnClient(DotCraftClient client)
{
    /// <summary>
    /// Starts a turn.
    /// </summary>
    public async Task<DotCraftTurnStartResult> StartAsync(
        string threadId,
        IReadOnlyList<TurnInputPart> input,
        object? sender = null,
        string? modelId = null,
        CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["threadId"] = threadId,
            ["input"] = input
        };
        if (sender is not null)
        {
            payload["sender"] = sender;
        }

        if (!string.IsNullOrWhiteSpace(modelId))
        {
            payload["modelId"] = modelId;
        }

        var result = await client.RequestAsync("turn/start", payload, cancellationToken);
        return new DotCraftTurnStartResult(ExtractTurnId(result), result.Clone());
    }

    /// <summary>
    /// Enqueues input when a thread is busy.
    /// </summary>
    public async Task<DotCraftTurnEnqueueResult> EnqueueAsync(
        string threadId,
        IReadOnlyList<TurnInputPart> input,
        object? sender = null,
        CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["threadId"] = threadId,
            ["input"] = input
        };
        if (sender is not null)
        {
            payload["sender"] = sender;
        }

        var result = await client.RequestAsync("turn/enqueue", payload, cancellationToken);
        var queuedInputId = JsonElementReaders.ReadString(result, "queuedInputId")
                            ?? JsonElementReaders.ReadNestedString(result, "queuedInput", "id");
        return new DotCraftTurnEnqueueResult(queuedInputId, result.Clone());
    }

    /// <summary>
    /// Interrupts a running turn.
    /// </summary>
    public Task InterruptAsync(string threadId, string turnId, CancellationToken cancellationToken = default) =>
        client.RequestAsync("turn/interrupt", new { threadId, turnId }, cancellationToken);

    private static string? ExtractTurnId(JsonElement result) =>
        JsonElementReaders.ReadString(result, "turnId")
        ?? JsonElementReaders.ReadString(result, "id")
        ?? JsonElementReaders.ReadNestedString(result, "turn", "id")
        ?? JsonElementReaders.ReadNestedString(result, "turn", "turnId");
}

/// <summary>
/// Model catalog operation surface.
/// </summary>
public sealed class DotCraftModelClient(DotCraftClient client)
{
    /// <summary>
    /// Lists available models from the connected AppServer.
    /// </summary>
    public async Task<IReadOnlyList<ModelInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        var result = await client.RequestAsync("model/list", new { }, cancellationToken);
        var modelsElement = result.TryGetProperty("models", out var models)
            ? models
            : result.TryGetProperty("items", out var items)
                ? items
                : result;
        if (modelsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return modelsElement.EnumerateArray()
            .Select(model => new ModelInfo(
                JsonElementReaders.ReadString(model, "id")
                ?? JsonElementReaders.ReadString(model, "modelId")
                ?? JsonElementReaders.ReadString(model, "name")
                ?? "unknown",
                JsonElementReaders.ReadString(model, "displayName")
                ?? JsonElementReaders.ReadString(model, "name")
                ?? JsonElementReaders.ReadString(model, "id")
                ?? "Unknown model",
                JsonElementReaders.ReadString(model, "provider")))
            .Where(model => !string.Equals(model.Id, "unknown", StringComparison.Ordinal))
            .ToArray();
    }
}
