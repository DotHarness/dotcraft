using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using DotCraft.Workspaces;
using DotCraft.Tools;

namespace DotCraft.Mcp;

public sealed class McpServerStatusSnapshot
{
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string StartupState { get; set; } = "idle";
    public int ToolCount { get; set; }
    public int ResourceCount { get; set; }
    public int ResourceTemplateCount { get; set; }
    public string? LastError { get; set; }
    public string AuthStatus { get; set; } = McpAuthenticationStatuses.Unsupported;
    public string? FailureReason { get; set; }
    public string Transport { get; set; } = "stdio";
    public McpServerOrigin Origin { get; set; } = McpServerOrigin.Workspace();
    public bool ReadOnly { get; set; }
}

public sealed class McpServerStatusChangedEventArgs : EventArgs
{
    public McpServerStatusSnapshot Status { get; init; } = new();
}

public sealed record McpServerInventorySnapshot(
    Implementation? ServerInfo,
    ServerCapabilities? ServerCapabilities,
    string? ServerInstructions,
    IReadOnlyList<AIFunction> Tools,
    IReadOnlyList<Resource> Resources,
    IReadOnlyList<ResourceTemplate> ResourceTemplates,
    long Generation);

public sealed partial class McpClientManager : IMcpToolInvocationCoordinator, IAsyncDisposable
{
    internal const double DefaultStartupTimeoutSeconds = 30;

    private sealed class ServerRuntimeState
    {
        public McpServerConfig Config { get; set; } = new();
        public McpConnectionIdentity ConnectionIdentity { get; set; }
        public CancellationTokenSource? StartupCancellation { get; set; }
        public IAsyncDisposable? Client { get; set; }
        public McpClient? ProtocolClient { get; set; }
        public bool HasSessionId { get; set; }
        public McpServerStatusSnapshot Status { get; set; } = new();
        public List<AIFunction> CachedTools { get; set; } = [];
        public IReadOnlyList<Resource> CachedResources { get; set; } = [];
        public IReadOnlyList<ResourceTemplate> CachedResourceTemplates { get; set; } = [];
        public long Generation { get; set; }
        public Task<McpToolInvocationTarget?>? StaleSessionRefreshTask { get; set; }
        public int ActiveOperations { get; set; }
        public List<IAsyncDisposable> RetiredClients { get; } = [];
    }

    private readonly ILogger<McpClientManager>? _logger;
    private readonly string? _userDataPath;
    private readonly Func<McpServerConfig, CancellationToken, Task<McpConnectionResult>> _connectServer;
    private readonly Dictionary<string, ServerRuntimeState> _servers = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<AITool> _toolsSnapshot = [];
    private IReadOnlyDictionary<string, string> _toolServerMapSnapshot =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<McpServerConfig> _serverConfigsSnapshot = [];
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCts = new();
    private long _nextGeneration;
    private bool _disposed;
    internal IMcpElicitationHandler? ElicitationHandler { get; }
    internal Func<CancellationToken, Task>? WaitForRuntimeRefresh { get; init; }

    public IReadOnlyList<AITool> Tools => Volatile.Read(ref _toolsSnapshot);

    public string? UserDataPath => _userDataPath;
    public IReadOnlyList<McpServerConfig> ServerConfigs => Volatile.Read(ref _serverConfigsSnapshot);
    public IReadOnlyDictionary<string, string> ToolServerMap => Volatile.Read(ref _toolServerMapSnapshot);

    public event EventHandler<McpServerStatusChangedEventArgs>? StatusChanged;

    public async Task<McpServerInventorySnapshot?> GetInventoryAsync(
        string serverName,
        CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            if (!_servers.TryGetValue(serverName, out var state))
                return null;
            return new McpServerInventorySnapshot(
                state.ProtocolClient?.ServerInfo,
                state.ProtocolClient?.ServerCapabilities,
                state.ProtocolClient?.ServerInstructions,
                state.CachedTools.ToArray(),
                state.CachedResources.ToArray(),
                state.CachedResourceTemplates.ToArray(),
                state.Generation);
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <summary>Refreshing resources never changes startup readiness.</summary>
    public async Task RefreshResourceInventoryAsync(
        string serverName,
        CancellationToken cancellationToken = default)
    {
        await using var session = await GetProtocolSessionAsync(serverName, expectedGeneration: null, cancellationToken);
        var resourcesTask = TryListResourcesAsync(session.Client, serverName, session.CancellationToken);
        var templatesTask = TryListResourceTemplatesAsync(session.Client, serverName, session.CancellationToken);
        await Task.WhenAll(resourcesTask, templatesTask);

        McpServerStatusSnapshot? snapshot = null;
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            if (_servers.TryGetValue(serverName, out var state)
                && state.Generation == session.Generation
                && state.Status.StartupState == "ready")
            {
                state.CachedResources = await resourcesTask;
                state.CachedResourceTemplates = await templatesTask;
                state.Status.ResourceCount = state.CachedResources.Count;
                state.Status.ResourceTemplateCount = state.CachedResourceTemplates.Count;
                snapshot = CloneStatus(state.Status);
            }
        }
        finally
        {
            _mutex.Release();
        }

        if (snapshot != null)
            OnStatusChanged(snapshot);
    }

    /// <summary>The caller remains responsible for thread authority, approval, lifecycle recording, and model-result normalization.</summary>
    public async Task<CallToolResult> CallToolAsync(
        string serverName,
        string toolName,
        IReadOnlyDictionary<string, object?>? arguments = null,
        long? expectedGeneration = null,
        CancellationToken cancellationToken = default,
        ToolInvocationContext? context = null)
    {
        await using var session = await GetProtocolSessionAsync(serverName, expectedGeneration, cancellationToken);
        using var invocation = new McpInvocationScope(context, session.Client.Completion);
        return await session.Client.CallToolAsync(
            toolName,
            arguments,
            progress: null,
            options: null,
            session.CancellationToken);
    }

    public async Task<ReadResourceResult> ReadResourceAsync(
        string serverName,
        string uri,
        CancellationToken cancellationToken = default)
        => await ReadResourceAsync(serverName, uri, expectedGeneration: null, cancellationToken);

    /// <summary>Fails when the server reconnected since <paramref name="expectedGeneration"/>, so a live view never crosses a reconnect.</summary>
    public async Task<ReadResourceResult> ReadResourceAsync(
        string serverName,
        string uri,
        long expectedGeneration,
        CancellationToken cancellationToken = default)
        => await ReadResourceAsync(serverName, uri, (long?)expectedGeneration, cancellationToken);

    private async Task<ReadResourceResult> ReadResourceAsync(
        string serverName,
        string uri,
        long? expectedGeneration,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(uri))
            throw new ArgumentException("A resource URI is required.", nameof(uri));

        await using var session = await GetProtocolSessionAsync(serverName, expectedGeneration, cancellationToken);
        return await session.Client.ReadResourceAsync(uri, options: null, session.CancellationToken);
    }

    public async Task<long?> GetGenerationAsync(
        string serverName,
        CancellationToken cancellationToken = default)
    {
        if (WaitForRuntimeRefresh is { } prepare)
            await prepare(cancellationToken).ConfigureAwait(false);
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            return _servers.TryGetValue(serverName, out var state) ? state.Generation : null;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public McpClientManager(ILogger<McpClientManager>? logger = null, IMcpElicitationHandler? elicitationHandler = null)
    {
        _connectServer = ConnectClientAndListToolsAsync;
        _logger = logger;
        ElicitationHandler = elicitationHandler;
    }

    public McpClientManager(DotCraftPaths? paths, ILogger<McpClientManager>? logger = null, IMcpElicitationHandler? elicitationHandler = null)
        : this(logger, elicitationHandler)
    {
        _userDataPath = paths?.UserData.RootPath;
    }

    internal McpClientManager(
        Func<McpServerConfig, CancellationToken, Task<McpConnectionResult>> connectServer,
        ILogger<McpClientManager>? logger = null,
        string? userDataPath = null)
    {
        _connectServer = connectServer;
        _logger = logger;
        _userDataPath = userDataPath;
    }

    public async Task<McpServerConfig?> GetConfigAsync(string name, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            return _servers.TryGetValue(name, out var state) ? state.Config.Clone() : null;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<IReadOnlyList<McpServerConfig>> ListConfigsAsync(CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            return _servers.Values
                .Select(s => s.Config.Clone())
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<IReadOnlyList<McpServerStatusSnapshot>> ListStatusesAsync(CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            return _servers.Values
                .Select(s => CloneStatus(s.Status))
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <summary>Observes startup without initiating connections.</summary>
    public async Task WaitForStartupCompletionAsync(CancellationToken cancellationToken = default)
    {
        var deadline = DateTimeOffset.UtcNow + await GetCurrentStartupWaitBudgetAsync(cancellationToken);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!await HasStartingServersAsync(cancellationToken))
                return;

            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
                return;

            var delay = remaining < TimeSpan.FromMilliseconds(50)
                ? remaining
                : TimeSpan.FromMilliseconds(50);
            await Task.Delay(delay, cancellationToken);
        }
    }

    public async Task<McpServerStatusSnapshot> TestAsync(McpServerConfig config, CancellationToken cancellationToken = default)
    {
        var status = CreateStatus(config, config.Enabled ? "starting" : "disabled");
        if (!config.Enabled)
            return status;

        IAsyncDisposable? client = null;
        try
        {
            using var timeoutCts = CreateStartupTimeoutToken(config, cancellationToken);
            var result = await _connectServer(config, timeoutCts.Token);
            client = result.Client;
            status.StartupState = "ready";
            status.ToolCount = result.Tools.Count;
            status.AuthStatus = result.AuthStatus;
            status.FailureReason = result.FailureReason;
            return status;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            status.StartupState = "error";
            status.LastError = CreateStartupTimeoutMessage(config);
            return status;
        }
        catch (Exception ex)
        {
            status.StartupState = "error";
            status.LastError = ex.Message;
            return status;
        }
        finally
        {
            if (client != null)
                await DisposeClientAsync(client).ConfigureAwait(false);
        }
    }

    Task<McpToolInvocationTarget?> IMcpToolInvocationCoordinator.TryGetInvocationTargetAsync(
        string serverName,
        string toolName,
        CancellationToken cancellationToken) =>
        TryGetInvocationTargetAsync(serverName, toolName, cancellationToken);

    Task<McpToolInvocationTarget?> IMcpToolInvocationCoordinator.RefreshToolAfterStaleSessionAsync(
        string serverName,
        string toolName,
        long observedGeneration,
        CancellationToken cancellationToken) =>
        RefreshToolAfterStaleSessionAsync(serverName, toolName, observedGeneration, cancellationToken);

    internal async Task<McpToolInvocationTarget?> TryGetInvocationTargetAsync(
        string serverName,
        string toolName,
        CancellationToken cancellationToken = default)
    {
        if (WaitForRuntimeRefresh is { } prepare)
            await prepare(cancellationToken).ConfigureAwait(false);
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            return _servers.TryGetValue(serverName, out var state)
                ? CreateInvocationTargetUnsafe(state, toolName)
                : null;
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task<ProtocolSession> GetProtocolSessionAsync(
        string serverName,
        long? expectedGeneration,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(serverName))
            throw new ArgumentException("An MCP server name is required.", nameof(serverName));

        if (WaitForRuntimeRefresh is { } prepare)
            await prepare(cancellationToken).ConfigureAwait(false);
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (!_servers.TryGetValue(serverName, out var state))
                throw new KeyNotFoundException($"MCP server '{serverName}' was not found.");
            if (expectedGeneration.HasValue && state.Generation != expectedGeneration.Value)
            {
                throw new InvalidOperationException(
                    $"MCP server '{serverName}' generation {expectedGeneration.Value} is no longer active.");
            }
            if (!string.Equals(state.Status.StartupState, "ready", StringComparison.Ordinal) ||
                state.ProtocolClient == null)
            {
                throw new InvalidOperationException(
                    $"MCP server '{serverName}' is not currently available ({state.Status.StartupState}).");
            }

            state.ActiveOperations++;
            var session = new ProtocolSession(this, state, state.ProtocolClient, state.Generation,
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCts.Token));
            if (_sessions.Count == 0)
                _sessionsDrained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _sessions.Add(session);
            return session;
        }
        finally
        {
            _mutex.Release();
        }
    }

    internal async Task<McpToolInvocationTarget?> RefreshToolAfterStaleSessionAsync(
        string serverName,
        string toolName,
        long observedGeneration,
        CancellationToken cancellationToken = default)
    {
        Task<McpToolInvocationTarget?> refreshTask;

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (!_servers.TryGetValue(serverName, out var state) || !CanRefreshAfterStaleSession(state))
                return null;

            if (state.Generation != observedGeneration)
                return CreateInvocationTargetUnsafe(state, toolName);

            if (state.StaleSessionRefreshTask is not { IsCompleted: false } activeRefresh)
            {
                var config = state.Config.Clone();
                activeRefresh = RefreshToolAfterStaleSessionCoreAsync(config, toolName, observedGeneration);
                state.StaleSessionRefreshTask = activeRefresh;
                TrackBackgroundTaskUnsafe(activeRefresh);
            }

            refreshTask = activeRefresh;
        }
        finally
        {
            _mutex.Release();
        }

        return await refreshTask.WaitAsync(cancellationToken);
    }

    private async Task<McpToolInvocationTarget?> RefreshToolAfterStaleSessionCoreAsync(
        McpServerConfig config,
        string toolName,
        long observedGeneration)
    {
        IAsyncDisposable? client = null;
        try
        {
            _logger?.LogWarning(
                "MCP stale session detected for {ServerName}/{ToolName}; reconnecting MCP client",
                config.Name,
                toolName);

            using var timeoutCts = CreateStartupTimeoutToken(config, _lifetimeCts.Token);
            var result = await _connectServer(config, timeoutCts.Token);
            client = result.Client;
            var applyResult = await ApplyStaleSessionRefreshResultAsync(
                config,
                observedGeneration,
                toolName,
                result.Tools,
                client,
                result.HasSessionId,
                result.ProtocolClient,
                result.Resources ?? [],
                result.ResourceTemplates ?? [],
                _lifetimeCts.Token);

            if (applyResult.Accepted)
                client = null;

            return applyResult.Target;
        }
        catch (OperationCanceledException ex) when (!_lifetimeCts.IsCancellationRequested)
        {
            var message = CreateStartupTimeoutMessage(config);
            _logger?.LogWarning(ex, "MCP stale session recovery timed out for {ServerName}/{ToolName}", config.Name, toolName);
            await MarkStaleSessionRefreshFailureAsync(config, observedGeneration, message, CancellationToken.None);
            throw new TimeoutException(message, ex);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "MCP stale session recovery failed for {ServerName}/{ToolName}", config.Name, toolName);
            await MarkStaleSessionRefreshFailureAsync(config, observedGeneration, ex.Message, CancellationToken.None);
            throw;
        }
        finally
        {
            if (client != null)
                await DisposeClientAsync(client).ConfigureAwait(false);
        }
    }

    private async Task<(McpToolInvocationTarget? Target, bool Accepted)> ApplyStaleSessionRefreshResultAsync(
        McpServerConfig config,
        long observedGeneration,
        string toolName,
        IReadOnlyList<AIFunction> tools,
        IAsyncDisposable client,
        bool hasSessionId,
        McpClient? protocolClient,
        IReadOnlyList<Resource> resources,
        IReadOnlyList<ResourceTemplate> resourceTemplates,
        CancellationToken cancellationToken)
    {
        IAsyncDisposable? previousClient = null;
        McpServerStatusSnapshot? snapshot = null;
        McpToolInvocationTarget? target = null;
        var accepted = false;

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            if (!_disposed && _servers.TryGetValue(config.Name, out var state))
            {
                if (state.Generation == observedGeneration && CanRefreshAfterStaleSession(state))
                {
                    previousClient = state.Client;
                    state.Client = client;
                    state.ProtocolClient = protocolClient;
                    state.HasSessionId = hasSessionId;
                    state.CachedTools = [.. tools];
                    state.CachedResources = resources;
                    state.CachedResourceTemplates = resourceTemplates;
                    state.Generation = NextGenerationUnsafe();
                    state.Status = CreateStatus(state.Config, "ready");
                    state.Status.ToolCount = state.CachedTools.Count;
                    state.Status.ResourceCount = state.CachedResources.Count;
                    state.Status.ResourceTemplateCount = state.CachedResourceTemplates.Count;
                    state.StaleSessionRefreshTask = null;
                    RebuildToolIndexUnsafe();
                    target = CreateInvocationTargetUnsafe(state, toolName);
                    snapshot = CloneStatus(state.Status);
                    accepted = true;
                }
                else
                {
                    target = CreateInvocationTargetUnsafe(state, toolName);
                }
            }
        }
        finally
        {
            _mutex.Release();
        }

        if (previousClient != null)
            await DisposeClientAsync(previousClient, expectedSessionMayBeExpired: true);

        if (snapshot != null)
            OnStatusChanged(snapshot);

        if (accepted)
        {
            _logger?.LogInformation(
                "MCP stale session recovery reconnected {ServerName} with {ToolCount} tools (generation {Generation}, sessionIdPresent: {HasSessionId})",
                config.Name,
                tools.Count,
                target?.Generation,
                hasSessionId);
        }

        return (target, accepted);
    }

    private async Task MarkStaleSessionRefreshFailureAsync(
        McpServerConfig config,
        long observedGeneration,
        string lastError,
        CancellationToken cancellationToken)
    {
        IAsyncDisposable? previousClient = null;
        McpServerStatusSnapshot? snapshot = null;

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            if (_servers.TryGetValue(config.Name, out var state) &&
                state.Generation == observedGeneration &&
                CanRefreshAfterStaleSession(state))
            {
                previousClient = state.Client;
                state.Client = null;
                state.ProtocolClient = null;
                state.HasSessionId = false;
                state.CachedTools.Clear();
                state.CachedResources = [];
                state.CachedResourceTemplates = [];
                state.Generation = NextGenerationUnsafe();
                state.Status = CreateStatus(state.Config, "error");
                state.Status.LastError = lastError;
                state.StaleSessionRefreshTask = null;
                RebuildToolIndexUnsafe();
                snapshot = CloneStatus(state.Status);
            }
        }
        finally
        {
            _mutex.Release();
        }

        if (previousClient != null)
            await DisposeClientAsync(previousClient, expectedSessionMayBeExpired: true);

        if (snapshot != null)
            OnStatusChanged(snapshot);
    }

    private static bool CanRefreshAfterStaleSession(ServerRuntimeState state) =>
        state.Config.Enabled &&
        string.Equals(state.Config.NormalizedTransport, "streamableHttp", StringComparison.OrdinalIgnoreCase);

    private static McpToolInvocationTarget? CreateInvocationTargetUnsafe(ServerRuntimeState state, string toolName)
    {
        if (state.Status.StartupState != "ready")
            return null;

        var tool = state.CachedTools.FirstOrDefault(tool => string.Equals(tool.Name, toolName, StringComparison.Ordinal));
        return tool == null
            ? null
            : new McpToolInvocationTarget(
                state.Config.Name,
                tool.Name,
                state.Config.NormalizedTransport,
                state.Generation,
                tool,
                state.HasSessionId,
                GetToolTimeout(state.Config));
    }

    private void RebuildToolIndexUnsafe()
    {
        var nextTools = new List<AITool>();
        var nextToolServerMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var state in _servers.Values
                     .Where(s => s.Status.StartupState == "ready")
                     .OrderBy(s => s.Config.Name, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var tool in state.CachedTools)
            {
                nextTools.Add(new McpSessionRecoveringTool(this, state.Config.Name, tool));
                nextToolServerMap[tool.Name] = state.Config.Name;
            }
            state.Status.ToolCount = state.CachedTools.Count;
        }

        Volatile.Write(ref _toolsSnapshot, nextTools);
        Volatile.Write(ref _toolServerMapSnapshot, nextToolServerMap);
        Volatile.Write(ref _serverConfigsSnapshot, _servers.Values.Select(static s => s.Config.Clone()).ToArray());
    }

    private static McpServerStatusSnapshot CreateStatus(McpServerConfig config, string startupState) =>
        new()
        {
            Name = config.Name,
            Enabled = config.Enabled,
            StartupState = startupState,
            ToolCount = 0,
            ResourceCount = 0,
            ResourceTemplateCount = 0,
            LastError = null,
            AuthStatus = config.Enabled && McpProtocolClientFactory.HasConfiguredBearerToken(config)
                ? McpAuthenticationStatuses.BearerToken
                : McpAuthenticationStatuses.Unsupported,
            FailureReason = null,
            Transport = config.NormalizedTransport,
            Origin = config.Origin.Clone(),
            ReadOnly = config.ReadOnly
        };

    private static McpServerStatusSnapshot CloneStatus(McpServerStatusSnapshot status) =>
        new()
        {
            Name = status.Name,
            Enabled = status.Enabled,
            StartupState = status.StartupState,
            ToolCount = status.ToolCount,
            ResourceCount = status.ResourceCount,
            ResourceTemplateCount = status.ResourceTemplateCount,
            LastError = status.LastError,
            AuthStatus = status.AuthStatus,
            FailureReason = status.FailureReason,
            Transport = status.Transport,
            Origin = status.Origin.Clone(),
            ReadOnly = status.ReadOnly
        };

    private async Task<TimeSpan> GetCurrentStartupWaitBudgetAsync(CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            var starting = _servers.Values
                .Where(s => IsStarting(s.Status))
                .Select(s => GetStartupTimeout(s.Config))
                .ToList();

            return starting.Count == 0
                ? TimeSpan.Zero
                : starting.Max() + TimeSpan.FromMilliseconds(250);
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task<bool> HasStartingServersAsync(CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            return _servers.Values.Any(s => IsStarting(s.Status));
        }
        finally
        {
            _mutex.Release();
        }
    }

    private static bool IsStarting(McpServerStatusSnapshot status) =>
        status.Enabled && string.Equals(status.StartupState, "starting", StringComparison.OrdinalIgnoreCase);

    private async Task UpdateStatusAsync(
        string name,
        long generation,
        McpServerStatusSnapshot status,
        CancellationToken cancellationToken)
    {
        McpServerStatusSnapshot? snapshot = null;
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            if (_servers.TryGetValue(name, out var state) && state.Generation == generation)
            {
                state.Status = status;
                snapshot = CloneStatus(state.Status);
            }
        }
        finally
        {
            _mutex.Release();
        }

        if (snapshot != null)
            OnStatusChanged(snapshot);
    }

    private async Task<bool> ApplyConnectResultAsync(
        McpServerConfig config,
        long generation,
        McpServerStatusSnapshot status,
        IReadOnlyList<AIFunction> tools,
        IAsyncDisposable? client,
        bool hasSessionId,
        McpClient? protocolClient,
        IReadOnlyList<Resource> resources,
        IReadOnlyList<ResourceTemplate> resourceTemplates,
        CancellationToken cancellationToken)
    {
        IAsyncDisposable? previousClient = null;
        McpServerStatusSnapshot? snapshot = null;
        var accepted = false;

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            if (!_disposed && _servers.TryGetValue(config.Name, out var state) && state.Generation == generation)
            {
                previousClient = state.Client;
                state.Client = status.StartupState == "ready" ? client : null;
                state.ProtocolClient = status.StartupState == "ready" ? protocolClient : null;
                state.HasSessionId = status.StartupState == "ready" && hasSessionId;
                state.CachedTools = status.StartupState == "ready" ? [.. tools] : [];
                state.CachedResources = status.StartupState == "ready" ? resources : [];
                state.CachedResourceTemplates = status.StartupState == "ready" ? resourceTemplates : [];
                state.Status = status;
                state.Status.ResourceCount = state.CachedResources.Count;
                state.Status.ResourceTemplateCount = state.CachedResourceTemplates.Count;
                state.Status.Origin = state.Config.Origin.Clone();
                state.StaleSessionRefreshTask = null;
                RebuildToolIndexUnsafe();
                snapshot = CloneStatus(state.Status);
                accepted = status.StartupState == "ready";
            }
        }
        finally
        {
            _mutex.Release();
        }

        if (previousClient != null)
        {
            if (await RetireClientAsync(config.Name, generation, previousClient, cancellationToken)
                is { } disposable)
            {
                await DisposeClientAsync(disposable);
            }
        }

        if (snapshot != null)
            OnStatusChanged(snapshot);

        return accepted;
    }

    private void OnStatusChanged(McpServerStatusSnapshot status)
    {
        StatusChanged?.Invoke(this, new McpServerStatusChangedEventArgs
        {
            Status = CloneStatus(status)
        });
    }

    private static void CollectClientUnsafe(ServerRuntimeState state, List<IAsyncDisposable> clients)
    {
        if (state.Client != null)
        {
            if (state.ActiveOperations == 0)
                clients.Add(state.Client);
            else
                state.RetiredClients.Add(state.Client);
        }

        if (state.ActiveOperations == 0 && state.RetiredClients.Count > 0)
        {
            clients.AddRange(state.RetiredClients);
            state.RetiredClients.Clear();
        }

        state.Client = null;
        state.ProtocolClient = null;
        state.HasSessionId = false;
        state.CachedTools.Clear();
        state.CachedResources = [];
        state.CachedResourceTemplates = [];
        state.StaleSessionRefreshTask = null;
    }

    private async Task DisposeClientsAsync(IEnumerable<IAsyncDisposable> clients)
    {
        foreach (var client in clients)
            await DisposeClientAsync(client).ConfigureAwait(false);
    }

    private async Task DisposeClientAsync(IAsyncDisposable client, bool expectedSessionMayBeExpired = false)
    {
        try
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (expectedSessionMayBeExpired)
                _logger?.LogDebug(ex, "MCP stale-session client disposal error ignored");
            else
                _logger?.LogWarning(ex, "MCP client disposal error");
        }
    }

    private long NextGenerationUnsafe() => unchecked(++_nextGeneration);

    private async Task<IAsyncDisposable?> RetireClientAsync(
        string serverName,
        long generation,
        IAsyncDisposable client,
        CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            if (_servers.TryGetValue(serverName, out var state) && state.Generation == generation
                && state.ActiveOperations > 0)
            {
                state.RetiredClients.Add(client);
                return null;
            }
            return client;
        }
        finally
        {
            _mutex.Release();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(McpClientManager));
    }

    private static CancellationTokenSource CreateStartupTimeoutToken(
        McpServerConfig config,
        CancellationToken cancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(GetStartupTimeout(config));
        return cts;
    }

    private static TimeSpan GetStartupTimeout(McpServerConfig config)
    {
        var seconds = config.StartupTimeoutSec is > 0
            ? config.StartupTimeoutSec.Value
            : DefaultStartupTimeoutSeconds;
        return TimeSpan.FromSeconds(seconds);
    }

    private static TimeSpan? GetToolTimeout(McpServerConfig config) =>
        config.ToolTimeoutSec is > 0 ? TimeSpan.FromSeconds(config.ToolTimeoutSec.Value) : null;

    private static bool HasSessionId(McpClient client) =>
        !string.IsNullOrWhiteSpace(client.SessionId);

    private static string CreateStartupTimeoutMessage(McpServerConfig config) =>
        $"MCP server '{config.Name}' startup timed out after {GetStartupTimeout(config).TotalSeconds:0.###}s.";

    private async Task<McpConnectionResult> ConnectClientAndListToolsAsync(
        McpServerConfig server,
        CancellationToken cancellationToken)
    {
        var recoveredFromStaleSession = false;
        for (var attempt = 0; ; attempt++)
        {
            var tokenStore = McpOAuthTokenStore.Create(server, _userDataPath);
            var hadOAuthTokens = McpProtocolClientFactory.IsOAuthCandidate(server)
                                 && await tokenStore.HasTokensAsync(cancellationToken);
            var client = await McpProtocolClientFactory.CreateAsync(
                server,
                ElicitationHandler,
                oauthOptions: null,
                userDataPath: _userDataPath,
                cancellationToken: cancellationToken);
            var hasSessionId = HasSessionId(client);
            try
            {
                var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
                return new McpConnectionResult(
                    client,
                    [.. tools.Cast<AIFunction>()],
                    hasSessionId,
                    recoveredFromStaleSession,
                    client,
                    AuthStatus: McpProtocolClientFactory.HasConfiguredBearerToken(server)
                        ? McpAuthenticationStatuses.BearerToken
                        : hadOAuthTokens
                            ? McpAuthenticationStatuses.OAuth
                            : McpAuthenticationStatuses.Unsupported);
            }
            catch (Exception ex) when (attempt == 0 && ShouldRetryConnectAfterStaleSession(server, ex, hasSessionId))
            {
                recoveredFromStaleSession = true;
                await DisposeClientBestEffortAsync(client);
            }
            catch
            {
                await client.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
    }

    private static bool ShouldRetryConnectAfterStaleSession(
        McpServerConfig server,
        Exception exception,
        bool hasSessionId) =>
        exception is not OperationCanceledException &&
        string.Equals(server.NormalizedTransport, "streamableHttp", StringComparison.OrdinalIgnoreCase) &&
        McpStaleSessionDetector.IsStaleSessionFailure(exception, hasSessionId);

    private async Task<IReadOnlyList<Resource>> TryListResourcesAsync(
        McpClient client,
        string serverName,
        CancellationToken cancellationToken)
    {
        try
        {
            var resources = await client.ListResourcesAsync(cancellationToken: cancellationToken);
            return resources.Select(static resource => resource.ProtocolResource).ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Optional MCP resource enumeration failed for {ServerName}", serverName);
            return [];
        }
    }

    private async Task<IReadOnlyList<ResourceTemplate>> TryListResourceTemplatesAsync(
        McpClient client,
        string serverName,
        CancellationToken cancellationToken)
    {
        try
        {
            var templates = await client.ListResourceTemplatesAsync(cancellationToken: cancellationToken);
            return templates.Select(static template => template.ProtocolResourceTemplate).ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(
                ex,
                "Optional MCP resource-template enumeration failed for {ServerName}",
                serverName);
            return [];
        }
    }

    private static async Task DisposeClientBestEffortAsync(IAsyncDisposable client)
    {
        try
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // The session may already be expired; do not block the fresh initialize retry.
        }
    }

    private static McpAuthenticationRequiredException? FindAuthenticationRequired(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is McpAuthenticationRequiredException authenticationRequired)
                return authenticationRequired;
            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.Flatten().InnerExceptions)
                {
                    if (FindAuthenticationRequired(inner) is { } nested)
                        return nested;
                }
            }
        }
        return null;
    }

}
