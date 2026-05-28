using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using Spectre.Console;

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
    public string Transport { get; set; } = "stdio";
    public McpServerOrigin Origin { get; set; } = McpServerOrigin.Workspace();
    public bool ReadOnly { get; set; }
}

public sealed class McpServerStatusChangedEventArgs : EventArgs
{
    public McpServerStatusSnapshot Status { get; init; } = new();
}

public sealed class McpClientManager : IAsyncDisposable
{
    private const double DefaultStartupTimeoutSeconds = 5;

    private sealed class ServerRuntimeState
    {
        public McpServerConfig Config { get; set; } = new();
        public McpClient? Client { get; set; }
        public McpServerStatusSnapshot Status { get; set; } = new();
        public List<McpClientTool> CachedTools { get; set; } = [];
        public long Generation { get; set; }
    }

    private readonly ILogger<McpClientManager>? _logger;
    private readonly Dictionary<string, ServerRuntimeState> _servers = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<McpClientTool> _toolsSnapshot = [];
    private IReadOnlyDictionary<string, string> _toolServerMapSnapshot =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCts = new();
    private long _nextGeneration;
    private bool _disposed;

    public IReadOnlyList<McpClientTool> Tools => Volatile.Read(ref _toolsSnapshot);
    public IReadOnlyDictionary<string, string> ToolServerMap => Volatile.Read(ref _toolServerMapSnapshot);

    public event EventHandler<McpServerStatusChangedEventArgs>? StatusChanged;

    public McpClientManager(ILogger<McpClientManager>? logger = null)
    {
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
        var clientsToDispose = new List<McpClient>();
        var statusesToNotify = new List<McpServerStatusSnapshot>();
        var connectWork = new List<(McpServerConfig Config, long Generation)>();

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
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
        var clientsToDispose = new List<McpClient>();
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
        var clientsToDispose = new List<McpClient>();

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (!_servers.Remove(name, out var state))
                return false;

            CollectClientUnsafe(state, clientsToDispose);
            RebuildToolIndexUnsafe();
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

        McpClient? client = null;
        try
        {
            using var timeoutCts = CreateStartupTimeoutToken(config, cancellationToken);
            client = await CreateClientAsync(config, timeoutCts.Token);
            var tools = await client.ListToolsAsync(cancellationToken: timeoutCts.Token);
            status.StartupState = "ready";
            status.ToolCount = tools.Count;
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

        McpClient? client = null;
        IReadOnlyList<McpClientTool> tools = [];
        McpServerStatusSnapshot status;
        try
        {
            using var timeoutCts = CreateStartupTimeoutToken(config, lifetimeToken);
            client = await CreateClientAsync(config, timeoutCts.Token);
            tools = [.. await client.ListToolsAsync(cancellationToken: timeoutCts.Token)];

            status = CreateStatus(config, "ready");
            status.ToolCount = tools.Count;
            _logger?.LogInformation(
                "MCP connected to {ServerName} with {ToolCount} tools",
                config.Name,
                tools.Count);
            TryWriteMcpConsoleLine(
                $"[grey][[MCP]][/] [green]Connected to {Markup.Escape(config.Name)} ({tools.Count} tools)[/]");
        }
        catch (OperationCanceledException) when (!lifetimeToken.IsCancellationRequested)
        {
            status = CreateStatus(config, "error");
            status.LastError = CreateStartupTimeoutMessage(config);

            _logger?.LogError("MCP connection to {ServerName} timed out", config.Name);
            TryWriteMcpConsoleLine(
                $"[grey][[MCP]][/] [red]Failed to connect to {Markup.Escape(config.Name)}: {Markup.Escape(status.LastError)}[/]");
        }
        catch (Exception ex)
        {
            status = CreateStatus(config, "error");
            status.LastError = ex.Message;

            _logger?.LogError(ex, "MCP connection to {ServerName} failed", config.Name);
            TryWriteMcpConsoleLine(
                $"[grey][[MCP]][/] [red]Failed to connect to {Markup.Escape(config.Name)}: {Markup.Escape(ex.Message)}[/]");
        }

        var accepted = await ApplyConnectResultAsync(config, generation, status, tools, client, lifetimeToken);
        if (accepted)
            client = null;

        if (client != null)
            await DisposeClientAsync(client);
    }

    private void RebuildToolIndexUnsafe()
    {
        var nextTools = new List<McpClientTool>();
        var nextToolServerMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var state in _servers.Values
                     .Where(s => s.Status.StartupState == "ready")
                     .OrderBy(s => s.Config.Name, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var tool in state.CachedTools)
            {
                nextTools.Add(tool);
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
        IReadOnlyList<McpClientTool> tools,
        McpClient? client,
        CancellationToken cancellationToken)
    {
        McpClient? previousClient = null;
        McpServerStatusSnapshot? snapshot = null;
        var accepted = false;

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            if (_servers.TryGetValue(config.Name, out var state) && state.Generation == generation)
            {
                previousClient = state.Client;
                state.Client = status.StartupState == "ready" ? client : null;
                state.CachedTools = status.StartupState == "ready" ? [.. tools] : [];
                state.Status = status;
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
            await DisposeClientAsync(previousClient);

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

    private static void TryWriteMcpConsoleLine(string markup)
    {
        try
        {
            AnsiConsole.MarkupLine(markup);
        }
        catch (ObjectDisposedException)
        {
            // Console output is best-effort; tests and embedded hosts may replace or close it.
        }
        catch (IOException)
        {
            // Best-effort console output.
        }
    }

    private static void CollectClientUnsafe(ServerRuntimeState state, List<McpClient> clients)
    {
        if (state.Client != null)
            clients.Add(state.Client);

        state.Client = null;
        state.CachedTools.Clear();
    }

    private void CollectClientsUnsafe(List<McpClient> clients)
    {
        foreach (var state in _servers.Values)
            CollectClientUnsafe(state, clients);
    }

    private async Task DisposeClientsAsync(IEnumerable<McpClient> clients)
    {
        foreach (var client in clients)
            await DisposeClientAsync(client);
    }

    private async Task DisposeClientAsync(McpClient client)
    {
        try
        {
            await client.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "MCP client disposal error");
        }
    }

    private long NextGenerationUnsafe() => unchecked(++_nextGeneration);

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

    private static string CreateStartupTimeoutMessage(McpServerConfig config) =>
        $"MCP server '{config.Name}' startup timed out after {GetStartupTimeout(config).TotalSeconds:0.###}s.";

    private static async Task<McpClient> CreateClientAsync(McpServerConfig server, CancellationToken cancellationToken)
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
            };

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

            transport = new HttpClientTransport(options);
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
                EnvironmentVariables = environmentVariables.Count > 0 ? environmentVariables : null
            };

            if (!string.IsNullOrWhiteSpace(server.Cwd))
            {
                var prop = typeof(StdioClientTransportOptions).GetProperty("WorkingDirectory")
                    ?? typeof(StdioClientTransportOptions).GetProperty("CurrentDirectory");
                if (prop is { CanWrite: true })
                    prop.SetValue(options, server.Cwd);
            }

            transport = new StdioClientTransport(options);
        }

        return await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        var clientsToDispose = new List<McpClient>();
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
