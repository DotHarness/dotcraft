using System.Collections.Concurrent;
using System.Text.Json;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Sdk.AppBinding;
using DotCraft.Sdk.Hub;
using DotCraft.Sdk.Wire;

namespace DotCraft.Sdk;

/// <summary>High-level DotCraft AppServer SDK client.</summary>
public sealed class DotCraftClient : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, Func<DynamicToolCallParams, CancellationToken, Task<DynamicToolCallResult>>> _dynamicToolHandlers = new(StringComparer.Ordinal);
    private Func<DynamicToolCallParams, CancellationToken, Task<DynamicToolCallResult>>? _defaultDynamicToolHandler;

    private DotCraftClient(DotCraftWireClient wire, InitializeResult initializeResult)
    {
        Wire = wire;
        ServerInfo = initializeResult.ServerInfo;
        Capabilities = initializeResult.Capabilities;
        Threads = new DotCraftThreadClient(this);
        Turns = new DotCraftTurnClient(this);
        Providers = new DotCraftProviderClient(this);
        Models = new DotCraftModelClient(this);
        AgentProfiles = new DotCraftAgentProfileClient(this);
        McpRuntime = new DotCraftMcpRuntimeClient(this);
        AppBindings = new DotCraftAppBindingClient(this);
        Wire.RegisterItemToolCallHandler(HandleDynamicToolCallAsync);
    }

    /// <summary>Low-level JSON-RPC wire client.</summary>
    public DotCraftWireClient Wire { get; }

    /// <summary>Server identity returned during initialize.</summary>
    public ServerInfo ServerInfo { get; }

    /// <summary>Server capabilities returned during initialize.</summary>
    public ServerCapabilities Capabilities { get; }

    /// <summary>Thread operations.</summary>
    public DotCraftThreadClient Threads { get; }

    /// <summary>Turn operations.</summary>
    public DotCraftTurnClient Turns { get; }

    /// <summary>Configured provider discovery operations.</summary>
    public DotCraftProviderClient Providers { get; }

    /// <summary>Model catalog operations.</summary>
    public DotCraftModelClient Models { get; }

    /// <summary>Agent Profile discovery, validation, mutation, and thread refresh operations.</summary>
    public DotCraftAgentProfileClient AgentProfiles { get; }

    /// <summary>Source-aware MCP runtime and control operations.</summary>
    public DotCraftMcpRuntimeClient McpRuntime { get; }

    /// <summary>App Binding operations.</summary>
    public DotCraftAppBindingClient AppBindings { get; }

    /// <summary>Connects to a Hub-managed workspace AppServer.</summary>
    public static async Task<DotCraftClient> ConnectLocalAsync(
        string workspacePath,
        DotCraftLocalOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        var value = options ?? new DotCraftLocalOptions();
        var hub = CreateHubClient(value);
        var ensured = await hub.EnsureAppServerAsync(
            workspacePath,
            CreateHubEnsureOptions(value),
            cancellationToken).ConfigureAwait(false);
        return await ConnectHubEndpointAsync(ensured, value, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Connects to the current user's default Chat workspace AppServer.</summary>
    public static async Task<DotCraftClient> ConnectLocalChatAsync(
        DotCraftLocalOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var value = options ?? new DotCraftLocalOptions();
        var hub = CreateHubClient(value);
        var ensured = await hub.EnsureDefaultChatAppServerAsync(
            CreateHubEnsureOptions(value),
            cancellationToken).ConfigureAwait(false);
        return await ConnectHubEndpointAsync(ensured, value, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Connects directly to an AppServer WebSocket endpoint.</summary>
    public static async Task<DotCraftClient> ConnectRemoteAsync(
        string appServerUrl,
        DotCraftRemoteOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var value = options ?? new DotCraftRemoteOptions();
        return await ConnectRemoteCoreAsync(appServerUrl, value.Token, value, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Connects over a custom transport.</summary>
    public static Task<DotCraftClient> ConnectAsync(
        IJsonRpcTransport transport,
        DotCraftClientOptions? options = null,
        CancellationToken cancellationToken = default) =>
        ConnectCoreAsync(transport, options, defaultAutoReconnect: false, cancellationToken);

    /// <summary>Sends an unknown extension request and deserializes its result.</summary>
    public async Task<T> RequestRawAsync<T>(
        string method,
        object? parameters = null,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        var result = await Wire.RequestRawAsync(method, parameters, cancellationToken, timeout).ConfigureAwait(false);
        return result.Deserialize<T>(DotCraftJson.Options)
               ?? throw new ProtocolViolationException($"Extension request '{method}' returned an invalid result.");
    }

    /// <summary>Sends an unknown extension request and returns its raw result.</summary>
    public Task<JsonElement> RequestRawAsync(
        string method,
        object? parameters = null,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null) =>
        Wire.RequestRawAsync(method, parameters, cancellationToken, timeout);

    /// <summary>Sends one cataloged AppServer request.</summary>
    public Task<TResult> RequestAsync<TParams, TResult>(
        RpcRequest<TParams, TResult> descriptor,
        TParams parameters,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null) =>
        Wire.RequestAsync(descriptor, parameters, cancellationToken, timeout);

    /// <summary>Registers one catch-all runtime dynamic tool handler.</summary>
    public IDisposable RegisterDynamicToolHandler(
        Func<DynamicToolCallParams, CancellationToken, Task<DynamicToolCallResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _defaultDynamicToolHandler = handler;
        return new DisposableAction(() =>
        {
            if (ReferenceEquals(_defaultDynamicToolHandler, handler))
                _defaultDynamicToolHandler = null;
        });
    }

    /// <summary>Registers a runtime dynamic tool handler for a specific thread, namespace, and tool.</summary>
    public IDisposable RegisterDynamicToolHandler(
        string threadId,
        string? @namespace,
        string toolName,
        Func<DynamicToolCallParams, CancellationToken, Task<DynamicToolCallResult>> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(handler);
        var key = ToolKey(threadId, @namespace, toolName);
        _dynamicToolHandlers[key] = handler;
        return new DisposableAction(() => _dynamicToolHandlers.TryRemove(key, out _));
    }

    /// <summary>Reads raw AppServer notifications for unknown extensions and diagnostics.</summary>
    public IAsyncEnumerable<AppServerNotification> ReadNotificationsAsync(CancellationToken cancellationToken = default) =>
        Wire.ReadNotificationsAsync(cancellationToken);

    /// <inheritdoc />
    public ValueTask DisposeAsync() => Wire.DisposeAsync();

    private static async Task<DotCraftClient> ConnectCoreAsync(
        IJsonRpcTransport transport,
        DotCraftClientOptions? options,
        bool defaultAutoReconnect,
        CancellationToken cancellationToken)
    {
        var value = options ?? new DotCraftClientOptions();
        ValidateCallbacks(value);
        var wire = new DotCraftWireClient(transport, new DotCraftWireClientOptions
        {
            AutoReconnect = value.AutoReconnect ?? defaultAutoReconnect
        });
        if (value.ApprovalHandler is not null)
            wire.RegisterItemApprovalRequestHandler((parameters, token) => value.ApprovalHandler(parameters, token));
        if (value.UserInputHandler is not null)
            wire.RegisterItemToolRequestUserInputHandler((parameters, token) => value.UserInputHandler(parameters, token));
        wire.Start();
        var initializeResult = await wire.InitializeAsync(value, cancellationToken).ConfigureAwait(false);
        return new DotCraftClient(wire, initializeResult);
    }

    private async Task<DynamicToolCallResult> HandleDynamicToolCallAsync(
        DynamicToolCallParams call,
        CancellationToken cancellationToken)
    {
        var key = ToolKey(call.ThreadId, call.Namespace, call.Tool);
        var handler = _dynamicToolHandlers.TryGetValue(key, out var keyed) ? keyed : _defaultDynamicToolHandler;
        if (handler is null)
        {
            return new DynamicToolCallResult
            {
                Success = false,
                ErrorCode = "UnsupportedTool",
                ErrorMessage = "No handler registered for this runtime dynamic tool."
            };
        }

        try
        {
            return await handler(call, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new DynamicToolCallResult
            {
                Success = false,
                ErrorCode = "AdapterToolCallFailed",
                ErrorMessage = ex.Message
            };
        }
    }

    private static void ValidateCallbacks(DotCraftClientOptions options)
    {
        if (options.ApprovalSupport && options.ApprovalHandler is null)
            throw new InvalidOperationException("ApprovalSupport requires an ApprovalHandler.");
        if (options.RequestUserInputSupport && options.UserInputHandler is null)
            throw new InvalidOperationException("RequestUserInputSupport requires a UserInputHandler.");
        if (options.ApprovalHandler is not null)
            options.ApprovalSupport = true;
        if (options.UserInputHandler is not null)
            options.RequestUserInputSupport = true;
    }

    private static HubClient CreateHubClient(DotCraftLocalOptions options) => new(new DotCraftHubClientOptions
    {
        Executable = options.Executable,
        UserProfilePath = options.UserProfilePath,
        HubLockPath = options.HubLockPath,
        StartupTimeout = options.HubStartupTimeout,
        StartHubIfMissing = true
    });

    private static HubEnsureAppServerOptions CreateHubEnsureOptions(DotCraftLocalOptions options) => new()
    {
        Client = new HubClientInfo { Name = options.ClientName, Version = options.ClientVersion },
        StartIfMissing = true
    };

    private static async Task<DotCraftClient> ConnectHubEndpointAsync(
        HubAppServerResponse ensured,
        DotCraftLocalOptions options,
        CancellationToken cancellationToken)
    {
        if (!ensured.Endpoints.TryGetValue("appServerWebSocket", out var wsUrl) || string.IsNullOrWhiteSpace(wsUrl))
            throw new InvalidOperationException("Hub response did not include endpoints.appServerWebSocket.");
        return await ConnectRemoteCoreAsync(wsUrl, token: null, options, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<DotCraftClient> ConnectRemoteCoreAsync(
        string appServerUrl,
        string? token,
        DotCraftClientOptions options,
        CancellationToken cancellationToken)
    {
        var transport = await WebSocketJsonRpcTransport.ConnectAsync(appServerUrl, token, cancellationToken).ConfigureAwait(false);
        return await ConnectCoreAsync(transport, options, defaultAutoReconnect: true, cancellationToken).ConfigureAwait(false);
    }

    private static string ToolKey(string threadId, string? @namespace, string toolName) =>
        $"{threadId}\u0000{@namespace ?? string.Empty}\u0000{toolName}";

    private sealed class DisposableAction(Action dispose) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                dispose();
        }
    }
}

/// <summary>High-level Agent Profile management surface.</summary>
public sealed class DotCraftAgentProfileClient(DotCraftClient client)
{
    /// <summary>Lists Agent Profiles, optionally including invalid entries and filtering by source.</summary>
    public Task<AgentProfileListResult> ListAsync(
        AgentProfileListParams parameters,
        CancellationToken cancellationToken = default) =>
        client.RequestAsync(AppServerRpc.AgentProfileList, parameters, cancellationToken);

    /// <summary>Reads one Agent Profile and its diagnostics and compiled configuration.</summary>
    public Task<AgentProfileReadResult> ReadAsync(
        AgentProfileReadParams parameters,
        CancellationToken cancellationToken = default) =>
        client.RequestAsync(AppServerRpc.AgentProfileRead, parameters, cancellationToken);

    /// <summary>Validates raw Agent Profile Markdown without persisting it.</summary>
    public Task<AgentProfileValidateResult> ValidateAsync(
        AgentProfileValidateParams parameters,
        CancellationToken cancellationToken = default) =>
        client.RequestAsync(AppServerRpc.AgentProfileValidate, parameters, cancellationToken);

    /// <summary>Creates or replaces a writable Agent Profile.</summary>
    public Task<AgentProfileUpsertResult> UpsertAsync(
        AgentProfileUpsertParams parameters,
        CancellationToken cancellationToken = default) =>
        client.RequestAsync(AppServerRpc.AgentProfileUpsert, parameters, cancellationToken);

    /// <summary>Removes a writable Agent Profile.</summary>
    public Task<AgentProfileRemoveResult> RemoveAsync(
        AgentProfileRemoveParams parameters,
        CancellationToken cancellationToken = default) =>
        client.RequestAsync(AppServerRpc.AgentProfileRemove, parameters, cancellationToken);

    /// <summary>Refreshes a thread from the current resolved Agent Profile.</summary>
    public Task<AgentProfileRefreshThreadResult> RefreshThreadAsync(
        AgentProfileRefreshThreadParams parameters,
        CancellationToken cancellationToken = default) =>
        client.RequestAsync(AppServerRpc.AgentProfileRefreshThread, parameters, cancellationToken);
}

/// <summary>Thread operation surface.</summary>
public sealed class DotCraftThreadClient(DotCraftClient client)
{
    /// <summary>Starts a new thread and returns its high-level handle.</summary>
    public async Task<DotCraftThread> StartAsync(ThreadStartParams parameters, CancellationToken cancellationToken = default)
    {
        var result = await client.Wire.ThreadStartAsync(parameters, cancellationToken).ConfigureAwait(false);
        return new DotCraftThread(client, result.Thread);
    }

    /// <summary>Resumes an existing thread and returns its high-level handle.</summary>
    public async Task<DotCraftThread> ResumeAsync(ThreadResumeParams parameters, CancellationToken cancellationToken = default)
    {
        var result = await client.Wire.ThreadResumeAsync(parameters, cancellationToken).ConfigureAwait(false);
        return new DotCraftThread(client, result.Thread);
    }

    /// <summary>Lists threads visible to the supplied required identity.</summary>
    public Task<ThreadListResult> ListAsync(ThreadListParams parameters, CancellationToken cancellationToken = default) =>
        client.Wire.ThreadListAsync(parameters, cancellationToken);

    /// <summary>Reads a typed thread snapshot.</summary>
    public Task<ThreadReadResult> ReadAsync(ThreadReadParams parameters, CancellationToken cancellationToken = default) =>
        client.Wire.ThreadReadAsync(parameters, cancellationToken);

    /// <summary>Convenience overload for reading a typed thread snapshot.</summary>
    public Task<ThreadReadResult> ReadAsync(
        string threadId,
        CancellationToken cancellationToken = default) =>
        ReadAsync(new ThreadReadParams
        {
            ThreadId = threadId
        }, cancellationToken);

    /// <summary>Reads one typed page of Turn metadata without Items.</summary>
    public Task<ThreadTurnsListResult> ListTurnsAsync(
        ThreadTurnsListParams parameters,
        CancellationToken cancellationToken = default) =>
        client.Wire.ThreadTurnsListAsync(parameters, cancellationToken);

    /// <summary>Reads one typed page of Items, optionally restricted to one Turn.</summary>
    public Task<ThreadItemsListResult> ListItemsAsync(
        ThreadItemsListParams parameters,
        CancellationToken cancellationToken = default) =>
        client.Wire.ThreadItemsListAsync(parameters, cancellationToken);

    /// <summary>Subscribes this connection to thread events.</summary>
    public Task SubscribeAsync(string threadId, bool replayRecent = false, CancellationToken cancellationToken = default) =>
        client.Wire.ThreadSubscribeAsync(new ThreadSubscribeParams
        {
            ThreadId = threadId,
            ReplayRecent = replayRecent
        }, cancellationToken);

    /// <summary>Reads the complete typed thread configuration.</summary>
    public async Task<ThreadConfiguration> ReadModelConfigurationAsync(
        string threadId,
        CancellationToken cancellationToken = default)
    {
        var result = await ReadAsync(threadId, cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Thread.Configuration
               ?? throw new InvalidOperationException($"DotCraft thread '{threadId}' has no configuration snapshot.");
    }

    /// <summary>
    /// Replaces model-selection fields while preserving every other Optional state from the latest
    /// configuration, then returns the authoritative configuration read back from the server.
    /// </summary>
    public async Task<ThreadConfiguration> UpdateModelConfigurationAsync(
        string threadId,
        string? providerId,
        string? model,
        ReasoningConfig? reasoning,
        string? speed,
        ThreadContextWindowConfig? contextWindow,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        var current = await ReadModelConfigurationAsync(threadId, cancellationToken).ConfigureAwait(false);
        var updated = CopyModelConfiguration(current, providerId, model, reasoning, speed, contextWindow);
        await client.Wire.ThreadConfigUpdateAsync(new ThreadConfigUpdateParams
        {
            ThreadId = threadId,
            Config = updated
        }, cancellationToken).ConfigureAwait(false);
        return await ReadModelConfigurationAsync(threadId, cancellationToken).ConfigureAwait(false);
    }

    private static ThreadConfiguration CopyModelConfiguration(
        ThreadConfiguration current,
        string? providerId,
        string? model,
        ReasoningConfig? reasoning,
        string? speed,
        ThreadContextWindowConfig? contextWindow)
    {
        var result = new ThreadConfiguration
        {
            AgentBuilderTargetId = current.AgentBuilderTargetId,
            AgentBuilderTargetSource = current.AgentBuilderTargetSource,
            AgentControlToolAccess = current.AgentControlToolAccess,
            AgentInstructions = current.AgentInstructions,
            AgentProfileFingerprint = current.AgentProfileFingerprint,
            AgentProfileId = current.AgentProfileId,
            AgentProfileSource = current.AgentProfileSource,
            AllowedAgentControlTools = current.AllowedAgentControlTools,
            ApprovalPolicy = current.ApprovalPolicy,
            ApprovalTimeoutSeconds = current.ApprovalTimeoutSeconds,
            AutomationTaskDirectory = current.AutomationTaskDirectory,
            ContextWindow = contextWindow,
            CustomTools = current.CustomTools,
            Cwd = current.Cwd,
            ExecutionWorkspaceOverride = current.ExecutionWorkspaceOverride,
            Extensions = current.Extensions,
            McpPolicy = current.McpPolicy,
            McpServers = current.McpServers,
            Mode = current.Mode,
            Model = model,
            OverrideBasePrompt = current.OverrideBasePrompt,
            PluginPolicy = current.PluginPolicy,
            ProviderId = providerId,
            Reasoning = reasoning,
            RequireApprovalOutsideWorkspace = current.RequireApprovalOutsideWorkspace,
            RoleInstructions = current.RoleInstructions,
            RuntimeWorkspaceRoots = current.RuntimeWorkspaceRoots,
            SkillsPolicy = current.SkillsPolicy,
            Speed = speed,
            TeamsPolicy = current.TeamsPolicy,
            ToolAllowList = current.ToolAllowList,
            ToolDenyList = current.ToolDenyList,
            ToolPolicy = current.ToolPolicy,
            ToolProfile = current.ToolProfile,
            UseToolProfileOnly = current.UseToolProfileOnly,
            WorkspaceOverride = current.WorkspaceOverride,
            ExtensionData = current.ExtensionData is null
                ? null
                : new Dictionary<string, JsonElement>(current.ExtensionData, StringComparer.Ordinal)
        };
        return result;
    }
}

/// <summary>Turn operation surface.</summary>
public sealed class DotCraftTurnClient(DotCraftClient client)
{
    /// <summary>Starts a turn.</summary>
    public Task<TurnStartResult> StartAsync(TurnStartParams parameters, CancellationToken cancellationToken = default) =>
        client.Wire.TurnStartAsync(parameters, cancellationToken);

    /// <summary>Convenience overload for starting a turn.</summary>
    public Task<TurnStartResult> StartAsync(
        string threadId,
        IReadOnlyList<InputPart> input,
        SenderContext? sender = null,
        CancellationToken cancellationToken = default) =>
        StartAsync(new TurnStartParams { ThreadId = threadId, Input = input, Sender = sender }, cancellationToken);

    /// <summary>Enqueues input when a thread is busy.</summary>
    public Task<TurnEnqueueResult> EnqueueAsync(TurnEnqueueParams parameters, CancellationToken cancellationToken = default) =>
        client.Wire.TurnEnqueueAsync(parameters, cancellationToken);

    /// <summary>Convenience overload for enqueuing input.</summary>
    public Task<TurnEnqueueResult> EnqueueAsync(
        string threadId,
        IReadOnlyList<InputPart> input,
        SenderContext? sender = null,
        CancellationToken cancellationToken = default) =>
        EnqueueAsync(new TurnEnqueueParams { ThreadId = threadId, Input = input, Sender = sender }, cancellationToken);

    /// <summary>Interrupts a running turn.</summary>
    public Task<RpcEmpty> InterruptAsync(TurnInterruptParams parameters, CancellationToken cancellationToken = default) =>
        client.Wire.TurnInterruptAsync(parameters, cancellationToken);

    /// <summary>Convenience overload for interrupting a running turn.</summary>
    public Task<RpcEmpty> InterruptAsync(string threadId, string turnId, CancellationToken cancellationToken = default) =>
        InterruptAsync(new TurnInterruptParams { ThreadId = threadId, TurnId = turnId }, cancellationToken);
}

/// <summary>Provider operation surface.</summary>
public sealed class DotCraftProviderClient(DotCraftClient client)
{
    /// <summary>Lists configured providers.</summary>
    public Task<ProviderListResult> ListAsync(CancellationToken cancellationToken = default) =>
        client.Wire.ProviderListAsync(new ProviderListParams(), cancellationToken);
}

/// <summary>Model catalog operation surface.</summary>
public sealed class DotCraftModelClient(DotCraftClient client)
{
    /// <summary>Gets available models and their typed capabilities.</summary>
    public Task<ModelListResult> GetCatalogAsync(string? providerId = null, CancellationToken cancellationToken = default) =>
        client.Wire.ModelListAsync(new ModelListParams { ProviderId = providerId }, cancellationToken);

    /// <summary>Gets available models using the complete protocol parameters.</summary>
    public Task<ModelListResult> GetCatalogAsync(ModelListParams parameters, CancellationToken cancellationToken = default) =>
        client.Wire.ModelListAsync(parameters, cancellationToken);
}
