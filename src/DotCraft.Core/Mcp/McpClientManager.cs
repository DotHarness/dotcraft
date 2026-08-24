using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using DotCraft.Workspaces;

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

/// <summary>Raw MCP tool and resource inventory for one runtime generation.</summary>
/// <param name="ServerInfo">The server implementation reported during initialization.</param>
/// <param name="ServerInstructions">The untrusted server instructions reported during initialization.</param>
/// <param name="Tools">The raw-generation MCP tool wrappers.</param>
/// <param name="Resources">The raw resources returned for the generation.</param>
/// <param name="ResourceTemplates">The raw resource templates returned for the generation.</param>
/// <param name="Generation">The live manager generation from which the inventory was captured.</param>
public sealed record McpServerInventorySnapshot(
    Implementation? ServerInfo,
    string? ServerInstructions,
    IReadOnlyList<AIFunction> Tools,
    IReadOnlyList<Resource> Resources,
    IReadOnlyList<ResourceTemplate> ResourceTemplates,
    long Generation);

public sealed class McpClientManager : IMcpToolInvocationCoordinator, IAsyncDisposable
{
    internal const double DefaultStartupTimeoutSeconds = 30;

    private sealed class ServerRuntimeState
    {
        public McpServerConfig Config { get; set; } = new();
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
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCts = new();
    private long _nextGeneration;
    private string? _effectiveConfigFingerprint;
    private bool _disposed;
    private Func<string, ElicitRequestParams?, CancellationToken, ValueTask<ElicitResult>>? _elicitationHandler;

    public IReadOnlyList<AITool> Tools => Volatile.Read(ref _toolsSnapshot);

    /// <summary>Gets the optional user-owned directory used for MCP authentication state.</summary>
    public string? UserDataPath => _userDataPath;
    public IReadOnlyDictionary<string, string> ToolServerMap => Volatile.Read(ref _toolServerMapSnapshot);

    public event EventHandler<McpServerStatusChangedEventArgs>? StatusChanged;

    /// <summary>Returns raw MCP inventory cached for a live server generation.</summary>
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

    /// <summary>Refreshes optional resources for a ready server without changing startup readiness.</summary>
    public async Task RefreshResourceInventoryAsync(
        string serverName,
        CancellationToken cancellationToken = default)
    {
        await using var session = await GetProtocolSessionAsync(serverName, expectedGeneration: null, cancellationToken);
        var resourcesTask = TryListResourcesAsync(session.Client, serverName, cancellationToken);
        var templatesTask = TryListResourceTemplatesAsync(session.Client, serverName, cancellationToken);
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

    /// <summary>
    /// Configures the host elicitation broker used by subsequently created MCP sessions.
    /// Unknown or unavailable client interactions are rejected safely.
    /// </summary>
    public void ConfigureElicitationHandler(
        Func<string, ElicitRequestParams?, CancellationToken, ValueTask<ElicitResult>>? handler) =>
        _elicitationHandler = handler;

    internal ValueTask<ElicitResult> RequestElicitationAsync(
        string serverName,
        ElicitRequestParams? request,
        CancellationToken cancellationToken = default) =>
        _elicitationHandler == null
            ? ValueTask.FromResult(new ElicitResult { Action = "decline" })
            : _elicitationHandler(serverName, request, cancellationToken);

    /// <summary>
    /// Calls a raw MCP tool through the live protocol session. The caller remains responsible for
    /// thread authority, approval, lifecycle recording, and model-result normalization.
    /// </summary>
    public async Task<CallToolResult> CallToolAsync(
        string serverName,
        string toolName,
        IReadOnlyDictionary<string, object?>? arguments = null,
        long? expectedGeneration = null,
        CancellationToken cancellationToken = default)
    {
        await using var session = await GetProtocolSessionAsync(serverName, expectedGeneration, cancellationToken);
        return await session.Client.CallToolAsync(
            toolName,
            arguments,
            progress: null,
            options: null,
            cancellationToken);
    }

    /// <summary>
    /// Reads a raw MCP resource through the live protocol session without producing a tool item.
    /// </summary>
    public async Task<ReadResourceResult> ReadResourceAsync(
        string serverName,
        string uri,
        CancellationToken cancellationToken = default)
        => await ReadResourceAsync(serverName, uri, expectedGeneration: null, cancellationToken);

    /// <summary>
    /// Reads a raw MCP resource only when the named server still has the expected runtime
    /// generation. This prevents a live view from crossing a reconnect boundary.
    /// </summary>
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
        return await session.Client.ReadResourceAsync(uri, options: null, cancellationToken);
    }

    /// <summary>Gets the current live generation for a server.</summary>
    public async Task<long?> GetGenerationAsync(
        string serverName,
        CancellationToken cancellationToken = default)
    {
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

    public McpClientManager(ILogger<McpClientManager>? logger = null)
    {
        _connectServer = ConnectClientAndListToolsAsync;
        _logger = logger;
    }

    public McpClientManager(DotCraftPaths paths, ILogger<McpClientManager>? logger = null)
        : this(logger)
    {
        _userDataPath = paths.UserData.RootPath;
    }

    internal McpClientManager(
        Func<McpServerConfig, CancellationToken, Task<McpConnectionResult>> connectServer,
        ILogger<McpClientManager>? logger = null)
    {
        _connectServer = connectServer;
        _logger = logger;
    }

    public async Task ConnectAsync(IEnumerable<McpServerConfig> servers, CancellationToken cancellationToken = default)
    {
        var configs = servers
            .Select(s => s.Clone())
            .Where(s => !string.IsNullOrWhiteSpace(s.Name))
            .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        var fingerprint = ComputeConfigFingerprint(configs);
        var clientsToDispose = new List<IAsyncDisposable>();
        var statusesToNotify = new List<McpServerStatusSnapshot>();
        var connectWork = new List<(McpServerConfig Config, long Generation)>();

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (string.Equals(_effectiveConfigFingerprint, fingerprint, StringComparison.Ordinal))
                return;
            _effectiveConfigFingerprint = fingerprint;
            CollectClientsUnsafe(clientsToDispose);
            _servers.Clear();

            foreach (var config in configs)
            {
                var generation = NextGenerationUnsafe();
                var startupState = config.Enabled ? "starting" : "disabled";
                var state = new ServerRuntimeState
                {
                    Config = config,
                    Status = CreateStatus(config, startupState),
                    Generation = generation
                };

                _servers[config.Name] = state;
                statusesToNotify.Add(CloneStatus(state.Status));

                if (config.Enabled)
                    connectWork.Add((config.Clone(), generation));
            }

            RebuildToolIndexUnsafe();
            _effectiveConfigFingerprint = ComputeConfigFingerprint(
                _servers.Values.Select(static state => state.Config));
        }
        finally
        {
            _mutex.Release();
        }

        foreach (var status in statusesToNotify)
            OnStatusChanged(status);

        await DisposeClientsAsync(clientsToDispose);

        foreach (var (config, generation) in connectWork)
            StartConnectInBackground(config, generation);
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

    /// <summary>
    /// Waits until all currently enabled MCP servers have left the startup phase.
    /// This does not initiate connections; it only observes the current runtime state.
    /// </summary>
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

    public async Task<McpServerStatusSnapshot> UpsertAsync(McpServerConfig config, CancellationToken cancellationToken = default)
    {
        var clone = config.Clone();
        var clientsToDispose = new List<IAsyncDisposable>();
        McpServerStatusSnapshot status;
        long generation;

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            generation = NextGenerationUnsafe();
            if (_servers.TryGetValue(clone.Name, out var existing))
            {
                CollectClientUnsafe(existing, clientsToDispose);
                existing.Config = clone;
                existing.Generation = generation;
                existing.Status = CreateStatus(existing.Config, existing.Config.Enabled ? "starting" : "disabled");
                status = CloneStatus(existing.Status);
            }
            else
            {
                var state = new ServerRuntimeState
                {
                    Config = clone,
                    Status = CreateStatus(clone, clone.Enabled ? "starting" : "disabled"),
                    Generation = generation
                };
                _servers[clone.Name] = state;
                status = CloneStatus(state.Status);
            }

            RebuildToolIndexUnsafe();
        }
        finally
        {
            _mutex.Release();
        }

        OnStatusChanged(status);
        await DisposeClientsAsync(clientsToDispose);

        if (clone.Enabled)
            StartConnectInBackground(clone.Clone(), generation);

        return status;
    }

    public async Task<bool> RemoveAsync(string name, CancellationToken cancellationToken = default)
    {
        var clientsToDispose = new List<IAsyncDisposable>();

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (!_servers.Remove(name, out var state))
                return false;

            CollectClientUnsafe(state, clientsToDispose);
            RebuildToolIndexUnsafe();
            _effectiveConfigFingerprint = ComputeConfigFingerprint(
                _servers.Values.Select(static candidate => candidate.Config));
        }
        finally
        {
            _mutex.Release();
        }

        await DisposeClientsAsync(clientsToDispose);
        return true;
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
                await DisposeClientAsync(client);
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
            return new ProtocolSession(this, state, state.ProtocolClient, state.Generation);
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
                await DisposeClientAsync(client);
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
            if (_servers.TryGetValue(config.Name, out var state))
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

    private void StartConnectInBackground(McpServerConfig config, long generation)
    {
        var lifetimeToken = _lifetimeCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await ConnectServerAsync(config, generation, lifetimeToken);
            }
            catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested)
            {
                // Runtime is shutting down.
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Unhandled MCP background connection error for {ServerName}", config.Name);
            }
        }, CancellationToken.None);
    }

    private async Task ConnectServerAsync(McpServerConfig config, long generation, CancellationToken lifetimeToken)
    {
        await UpdateStatusAsync(config.Name, generation, CreateStatus(config, "starting"), lifetimeToken);

        IAsyncDisposable? client = null;
        IReadOnlyList<AIFunction> tools = [];
        IReadOnlyList<Resource> resources = [];
        IReadOnlyList<ResourceTemplate> resourceTemplates = [];
        var hasSessionId = false;
        McpClient? protocolClient = null;
        McpServerStatusSnapshot status;
        try
        {
            using var timeoutCts = CreateStartupTimeoutToken(config, lifetimeToken);
            var result = await _connectServer(config, timeoutCts.Token);
            client = result.Client;
            tools = result.Tools;
            hasSessionId = result.HasSessionId;
            protocolClient = result.ProtocolClient;
            resources = result.Resources ?? [];
            resourceTemplates = result.ResourceTemplates ?? [];

            if (result.RecoveredFromStaleSession)
            {
                _logger?.LogInformation(
                    "MCP connection to {ServerName} recovered from stale session during startup/listTools",
                    config.Name);
            }

            status = CreateStatus(config, "ready");
            status.ToolCount = tools.Count;
            status.AuthStatus = result.AuthStatus;
            status.FailureReason = result.FailureReason;
            _logger?.LogInformation(
                "MCP connected to {ServerName} with {ToolCount} tools",
                config.Name,
                tools.Count);
        }
        catch (OperationCanceledException) when (!lifetimeToken.IsCancellationRequested)
        {
            status = CreateStatus(config, "error");
            status.LastError = CreateStartupTimeoutMessage(config);

            _logger?.LogError("MCP connection to {ServerName} timed out", config.Name);
        }
        catch (Exception ex)
        {
            status = CreateStatus(config, "error");
            status.LastError = ex.Message;
            if (FindAuthenticationRequired(ex) is { } authenticationRequired)
            {
                status.AuthStatus = McpAuthenticationStatuses.NotLoggedIn;
                status.FailureReason = authenticationRequired.ReauthenticationRequired
                    ? "reauthenticationRequired"
                    : null;
            }

            _logger?.LogError(ex, "MCP connection to {ServerName} failed", config.Name);
        }

        var accepted = await ApplyConnectResultAsync(
            config,
            generation,
            status,
            tools,
            client,
            hasSessionId,
            protocolClient,
            resources,
            resourceTemplates,
            lifetimeToken);
        if (accepted)
            client = null;

        if (client != null)
            await DisposeClientAsync(client);
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
            AuthStatus = config.Enabled && HasConfiguredBearerToken(config)
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
            if (_servers.TryGetValue(config.Name, out var state) && state.Generation == generation)
            {
                previousClient = state.Client;
                state.Client = status.StartupState == "ready" ? client : null;
                state.ProtocolClient = status.StartupState == "ready" ? protocolClient : null;
                state.HasSessionId = status.StartupState == "ready" && hasSessionId;
                state.CachedTools = status.StartupState == "ready" ? [.. tools] : [];
                state.CachedResources = status.StartupState == "ready" ? resources : [];
                state.CachedResourceTemplates = status.StartupState == "ready" ? resourceTemplates : [];
                state.Status.ResourceCount = state.CachedResources.Count;
                state.Status.ResourceTemplateCount = state.CachedResourceTemplates.Count;
                state.Status = status;
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

    private void CollectClientsUnsafe(List<IAsyncDisposable> clients)
    {
        foreach (var state in _servers.Values)
            CollectClientUnsafe(state, clients);
    }

    private async Task DisposeClientsAsync(IEnumerable<IAsyncDisposable> clients)
    {
        foreach (var client in clients)
            await DisposeClientAsync(client);
    }

    private async Task DisposeClientAsync(IAsyncDisposable client, bool expectedSessionMayBeExpired = false)
    {
        try
        {
            await client.DisposeAsync();
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

    private static string ComputeConfigFingerprint(IEnumerable<McpServerConfig> configs)
    {
        var builder = new StringBuilder();
        foreach (var config in configs.OrderBy(static value => value.Name, StringComparer.OrdinalIgnoreCase))
        {
            AppendFingerprintValue(builder, config.Name);
            AppendFingerprintValue(builder, config.Enabled.ToString());
            AppendFingerprintValue(builder, config.NormalizedTransport);
            AppendFingerprintValue(builder, config.Command);
            foreach (var argument in config.Arguments)
                AppendFingerprintValue(builder, argument);
            foreach (var pair in config.EnvironmentVariables.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                AppendFingerprintValue(builder, pair.Key);
                AppendFingerprintValue(builder, pair.Value);
            }
            foreach (var name in config.EnvVars.OrderBy(static value => value, StringComparer.Ordinal))
                AppendFingerprintValue(builder, name);
            AppendFingerprintValue(builder, config.Cwd);
            AppendFingerprintValue(builder, config.Url);
            AppendFingerprintValue(builder, config.BearerTokenEnvVar);
            foreach (var pair in config.Headers.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                AppendFingerprintValue(builder, pair.Key);
                AppendFingerprintValue(builder, pair.Value);
            }
            foreach (var pair in config.EnvHttpHeaders.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                AppendFingerprintValue(builder, pair.Key);
                AppendFingerprintValue(builder, pair.Value);
            }
            AppendFingerprintValue(builder, config.StartupTimeoutSec?.ToString(System.Globalization.CultureInfo.InvariantCulture));
            AppendFingerprintValue(builder, config.ToolTimeoutSec?.ToString(System.Globalization.CultureInfo.InvariantCulture));
            AppendFingerprintValue(builder, config.Origin.Kind);
            AppendFingerprintValue(builder, config.Origin.PluginId);
            AppendFingerprintValue(builder, config.Origin.ThreadId);
            AppendFingerprintValue(builder, config.Origin.BindingId);
            AppendFingerprintValue(builder, config.Origin.DeclaredName);
        }

        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes(builder.ToString())));
    }

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

    private async ValueTask ReleaseProtocolSessionAsync(ServerRuntimeState state)
    {
        List<IAsyncDisposable>? clients = null;
        await _mutex.WaitAsync();
        try
        {
            if (state.ActiveOperations > 0)
                state.ActiveOperations--;
            if (state.ActiveOperations == 0 && state.RetiredClients.Count > 0)
            {
                clients = [.. state.RetiredClients];
                state.RetiredClients.Clear();
            }
        }
        finally
        {
            _mutex.Release();
        }

        if (clients != null)
            await DisposeClientsAsync(clients);
    }

    private static void AppendFingerprintValue(StringBuilder builder, string? value)
    {
        value ??= string.Empty;
        builder.Append(value.Length).Append(':').Append(value).Append(';');
    }

    private sealed class ProtocolSession(
        McpClientManager owner,
        ServerRuntimeState state,
        McpClient client,
        long generation) : IAsyncDisposable
    {
        private int _disposed;

        public McpClient Client { get; } = client;
        public long Generation { get; } = generation;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                await owner.ReleaseProtocolSessionAsync(state);
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
            var hadOAuthTokens = IsOAuthCandidate(server)
                                 && await tokenStore.HasTokensAsync(cancellationToken);
            var client = await CreateClientAsync(
                server,
                _elicitationHandler == null
                    ? null
                    : (request, ct) => RequestElicitationAsync(server.Name, request, ct),
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
                    AuthStatus: HasConfiguredBearerToken(server)
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
                await client.DisposeAsync();
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
            await client.DisposeAsync();
        }
        catch
        {
            // The session may already be expired; do not block the fresh initialize retry.
        }
    }

    internal static async Task<McpClient> CreateClientAsync(
        McpServerConfig server,
        Func<ElicitRequestParams?, CancellationToken, ValueTask<ElicitResult>>? elicitationHandler,
        ClientOAuthOptions? oauthOptions,
        string? userDataPath,
        CancellationToken cancellationToken)
    {
        IClientTransport transport;

        if (server.NormalizedTransport == "streamableHttp")
        {
            if (string.IsNullOrWhiteSpace(server.Url))
                throw new InvalidOperationException($"MCP server '{server.Name}' has transport 'streamableHttp' but no Url configured.");

            var options = new HttpClientTransportOptions
            {
                Endpoint = new Uri(server.Url),
                Name = server.Name,
                TransportMode = HttpTransportMode.AutoDetect,
            };

            if (oauthOptions == null)
            {
                var tokenStore = McpOAuthTokenStore.Create(server, userDataPath);
                if (IsOAuthCandidate(server))
                {
                    var hadTokens = await tokenStore.HasTokensAsync(cancellationToken);
                    oauthOptions = new ClientOAuthOptions
                    {
                        RedirectUri = new Uri("http://127.0.0.1/callback"),
                        TokenCache = tokenStore,
                        AuthorizationCallbackHandler = (_, _) =>
                            throw new McpAuthenticationRequiredException(hadTokens)
                    };
                }
            }

            options.OAuth = oauthOptions;

            var headers = new Dictionary<string, string>(server.Headers, StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(server.BearerTokenEnvVar))
            {
                var token = Environment.GetEnvironmentVariable(server.BearerTokenEnvVar);
                if (string.IsNullOrWhiteSpace(token))
                    throw new InvalidOperationException($"MCP server '{server.Name}' requires env var '{server.BearerTokenEnvVar}'.");
                headers["Authorization"] = $"Bearer {token}";
            }

            foreach (var (headerName, envVarName) in server.EnvHttpHeaders)
            {
                var value = Environment.GetEnvironmentVariable(envVarName);
                if (string.IsNullOrWhiteSpace(value))
                    throw new InvalidOperationException($"MCP server '{server.Name}' requires env var '{envVarName}' for header '{headerName}'.");
                headers[headerName] = value;
            }

            if (headers.Count > 0)
                options.AdditionalHeaders = headers;

            if (server.Origin.IsBinding)
            {
                // A binding endpoint is an authority boundary. Following a redirect could move the
                // one-time bearer to a different origin without a fresh activation decision.
                var handler = new HttpClientHandler { AllowAutoRedirect = false };
                transport = new HttpClientTransport(options, new HttpClient(handler), loggerFactory: null, ownsHttpClient: true);
            }
            else
            {
                transport = new HttpClientTransport(options);
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(server.Command))
                throw new InvalidOperationException($"MCP server '{server.Name}' has transport 'stdio' but no Command configured.");

            var environmentVariables = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in server.EnvironmentVariables)
                environmentVariables[key] = value;

            foreach (var envVar in server.EnvVars.Where(v => !string.IsNullOrWhiteSpace(v)))
                environmentVariables[envVar] = Environment.GetEnvironmentVariable(envVar);

            var options = new StdioClientTransportOptions
            {
                Command = server.Command,
                Name = server.Name,
                Arguments = server.Arguments is { Count: > 0 } ? server.Arguments : null,
                EnvironmentVariables = environmentVariables.Count > 0 ? environmentVariables : null,
                InheritEnvironmentVariables = true,
                WorkingDirectory = string.IsNullOrWhiteSpace(server.Cwd) ? null : server.Cwd
            };

            transport = new StdioClientTransport(options);
        }

        var clientOptions = CreateClientOptions(elicitationHandler);
        return await McpClient.CreateAsync(transport, clientOptions, cancellationToken: cancellationToken);
    }

    private static bool HasConfiguredBearerToken(McpServerConfig server) =>
        !string.IsNullOrWhiteSpace(server.BearerTokenEnvVar)
        || server.Headers.Keys.Any(static name =>
            string.Equals(name, "Authorization", StringComparison.OrdinalIgnoreCase))
        || server.EnvHttpHeaders.Keys.Any(static name =>
            string.Equals(name, "Authorization", StringComparison.OrdinalIgnoreCase));

    private static bool IsOAuthCandidate(McpServerConfig server) =>
        string.Equals(server.NormalizedTransport, "streamableHttp", StringComparison.Ordinal)
        && !HasConfiguredBearerToken(server);

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

    internal static McpClientOptions CreateClientOptions(
        Func<ElicitRequestParams?, CancellationToken, ValueTask<ElicitResult>>? elicitationHandler)
    {
        var options = new McpClientOptions
        {
            ProtocolVersion = "2025-06-18",
            Capabilities = new ClientCapabilities
            {
#pragma warning disable MCPEXP001 // Stable MCP extensions use the SDK's extension transport hook.
                Extensions = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    [McpAppMetadataParser.ExtensionIdentifier] = JsonSerializer.SerializeToElement(new
                    {
                        mimeTypes = new[] { McpAppMetadataParser.HtmlMimeType }
                    })
                }
#pragma warning restore MCPEXP001
            }
        };
        if (elicitationHandler is not null)
            options.Handlers = new McpClientHandlers { ElicitationHandler = elicitationHandler };
        return options;
    }

    public async ValueTask DisposeAsync()
    {
        var clientsToDispose = new List<IAsyncDisposable>();
        _lifetimeCts.Cancel();

        await _mutex.WaitAsync();
        try
        {
            if (_disposed)
                return;

            _disposed = true;
            CollectClientsUnsafe(clientsToDispose);
            _servers.Clear();
            Volatile.Write(ref _toolsSnapshot, []);
            Volatile.Write(
                ref _toolServerMapSnapshot,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }
        finally
        {
            _mutex.Release();
            _mutex.Dispose();
            _lifetimeCts.Dispose();
        }

        await DisposeClientsAsync(clientsToDispose);
    }
}
