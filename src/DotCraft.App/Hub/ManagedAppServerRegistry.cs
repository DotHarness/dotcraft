using System.Collections.Concurrent;
using System.Security.Cryptography;
using DotCraft.AppServer;
using DotCraft.CLI;
using DotCraft.Common;
using DotCraft.Configuration;
using DotCraft.Protocol.AppServer;
using Microsoft.AspNetCore.Http;

namespace DotCraft.Hub;

/// <summary>
/// Tracks and supervises Hub-managed workspace AppServer processes.
/// </summary>
public sealed class ManagedAppServerRegistry : IAsyncDisposable
{
    private static readonly StringComparer WorkspaceComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly ConcurrentDictionary<string, ManagedEntry> _entries;
    private readonly HubEventBus _events;
    private readonly string _hubApiBaseUrl;
    private readonly string _hubToken;
    private readonly string? _dotcraftBin;
    private readonly HubAppServerRegistryStore? _store;
    private readonly HubRuntimeToolsStore? _runtimeToolsStore;
    private readonly ConcurrentDictionary<string, HubAppServerRegistryRecord> _persisted;
    private readonly CancellationTokenSource _healthCts = new();
    private Task? _healthTask;
    private bool _disposed;

    public ManagedAppServerRegistry(
        HubEventBus events,
        string hubApiBaseUrl,
        string hubToken,
        string? dotcraftBin = null,
        string? registryPath = null,
        string? runtimeToolsPath = null)
    {
        _entries = new ConcurrentDictionary<string, ManagedEntry>(WorkspaceComparer);
        _events = events;
        _hubApiBaseUrl = hubApiBaseUrl;
        _hubToken = hubToken;
        _dotcraftBin = dotcraftBin;
        _store = string.IsNullOrWhiteSpace(registryPath) ? null : new HubAppServerRegistryStore(registryPath);
        _runtimeToolsStore = string.IsNullOrWhiteSpace(runtimeToolsPath) ? null : new HubRuntimeToolsStore(runtimeToolsPath);
        _persisted = new ConcurrentDictionary<string, HubAppServerRegistryRecord>(
            _store?.Load() ?? new Dictionary<string, HubAppServerRegistryRecord>(WorkspaceComparer),
            WorkspaceComparer);
    }

    public void StartHealthChecks(TimeSpan? interval = null)
    {
        ThrowIfDisposed();
        if (_healthTask is not null)
            return;

        _healthTask = Task.Run(() => RunHealthLoopAsync(interval ?? TimeSpan.FromSeconds(10), _healthCts.Token));
    }

    public async Task<HubAppServerResponse> EnsureAsync(EnsureAppServerRequest request, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var (workspacePath, canonical, craftPath) = ResolveWorkspace(request.WorkspacePath);
        var entry = _entries.GetOrAdd(canonical, _ => new ManagedEntry(workspacePath, canonical));

        await entry.Mutex.WaitAsync(cancellationToken);
        try
        {
            RefreshExited(entry);
            if (entry.Process is { IsRunning: true } && entry.State == HubAppServerStates.Running)
            {
                var effectiveRuntimeTools = MergeRuntimeTools(request.RuntimeTools);
                var runtimeRestartReason = GetTypeScriptRuntimeRestartReason(entry, effectiveRuntimeTools);
                if (runtimeRestartReason is not null)
                    ApplyTypeScriptRuntimeRestartRequiredStatus(entry, effectiveRuntimeTools, runtimeRestartReason);

                entry.LastSeenAt = DateTimeOffset.UtcNow;
                Persist(entry);
                return entry.ToResponse();
            }

            if (!request.StartIfMissing)
            {
                entry.State = HubAppServerStates.Stopped;
                entry.LastSeenAt = DateTimeOffset.UtcNow;
                Persist(entry);
                return entry.ToResponse();
            }

            if (entry.Process is { })
            {
                await StopManagedProcessesAsync(entry, craftPath);
            }

            ThrowIfExternalLockIsLive(entry, craftPath);

            var plan = BuildServicePlan(canonical, craftPath, MergeRuntimeTools(request.RuntimeTools));
            entry.State = HubAppServerStates.Starting;
            entry.LastError = null;
            entry.RecentStderr = null;
            entry.ExitCode = null;
            entry.Endpoints = plan.ResponseEndpoints;
            entry.ServiceStatus = plan.ServiceStatus;
            entry.LastSeenAt = DateTimeOffset.UtcNow;
            Persist(entry);
            _events.Publish("appserver.starting", canonical, new { endpoints = plan.ResponseEndpoints });

            try
            {
                var process = await AppServerProcess.StartAsync(
                    dotcraftBin: _dotcraftBin,
                    workspacePath: canonical,
                    environmentVariables: plan.Environment,
                    createNoWindow: true,
                    attachWindowsJob: true,
                    ct: cancellationToken);

                await ProbeWebSocketAsync(plan.WebSocketProbeUrl, plan.WebSocketToken, cancellationToken);
                VerifyManagedLock(craftPath, process.ProcessId, canonical);

                process.OnCrashed += () => OnProcessCrashed(entry);
                entry.Process = process;
                entry.State = HubAppServerStates.Running;
                entry.Pid = process.ProcessId;
                entry.ServerVersion = process.ServerVersion;
                entry.StartedByHub = true;
                entry.StartedInCurrentHubProcess = true;
                entry.TypeScriptNodeBin = plan.TypeScriptNodeBin;
                entry.TypeScriptNodeRunAsNode = plan.TypeScriptNodeRunAsNode;
                entry.TypeScriptModulesDir = plan.TypeScriptModulesDir;
                entry.RecentStderr = process.RecentStderr;
                entry.LastStartedAt = DateTimeOffset.UtcNow;
                entry.LastSeenAt = DateTimeOffset.UtcNow;
                Persist(entry);
                _events.Publish("appserver.running", canonical, new { pid = process.ProcessId, endpoints = plan.ResponseEndpoints });
                return entry.ToResponse();
            }
            catch (Exception ex) when (ex is not HubProtocolException)
            {
                entry.State = HubAppServerStates.Exited;
                entry.LastError = ex.Message;
                entry.LastExitedAt = DateTimeOffset.UtcNow;
                Persist(entry);
                _events.Publish("appserver.exited", canonical, new { error = ex.Message });
                throw new HubProtocolException(
                    "appServerStartFailed",
                    "Managed AppServer failed during startup.",
                    StatusCodes.Status500InternalServerError,
                    new { workspacePath = canonical, error = ex.Message });
            }
        }
        finally
        {
            entry.Mutex.Release();
        }
    }

    public IReadOnlyList<HubAppServerResponse> List()
    {
        var responses = new Dictionary<string, HubAppServerResponse>(WorkspaceComparer);

        foreach (var pair in _persisted)
        {
            var refreshed = RefreshPersistedRecord(pair.Value);
            _persisted[refreshed.CanonicalWorkspacePath] = refreshed;
            if (ShouldListPersistedRecord(refreshed))
                responses[refreshed.CanonicalWorkspacePath] = refreshed.ToResponse();
        }

        foreach (var entry in _entries.Values)
        {
            RefreshExited(entry);
            _persisted[entry.CanonicalWorkspacePath] = entry.ToRegistryRecord();
            if (ShouldListEntry(entry))
            {
                responses[entry.CanonicalWorkspacePath] = entry.ToResponse();
            }
            else if (responses.TryGetValue(entry.CanonicalWorkspacePath, out var response)
                     && response.State != HubAppServerStates.Running)
            {
                responses.Remove(entry.CanonicalWorkspacePath);
            }
        }

        PersistAll();

        return responses.Values
            .OrderBy(e => e.CanonicalWorkspacePath, WorkspaceComparer)
            .ToArray();
    }

    private static bool ShouldListEntry(ManagedEntry entry) =>
        entry.StartedInCurrentHubProcess ||
        entry.State is HubAppServerStates.Running
            or HubAppServerStates.Starting
            or HubAppServerStates.Stopping
            or HubAppServerStates.Unhealthy;

    private static bool ShouldListPersistedRecord(HubAppServerRegistryRecord record) =>
        record.State == HubAppServerStates.Running;

    public HubAppServerResponse GetByWorkspace(string workspacePath)
    {
        var (resolvedWorkspace, canonical, craftPath) = ResolveWorkspace(workspacePath);
        if (_entries.TryGetValue(canonical, out var entry))
        {
            RefreshExited(entry);
            return entry.ToResponse();
        }

        var lockInfo = AppServerWorkspaceLock.TryRead(AppServerWorkspaceLock.GetLockFilePath(craftPath));
        if (lockInfo is { } info && info.IsProcessAlive())
        {
            return new HubAppServerResponse(
                resolvedWorkspace,
                canonical,
                HubAppServerStates.Running,
                info.Pid,
                info.Endpoints,
                info.Endpoints.ToDictionary(
                    pair => pair.Key,
                    pair => new HubServiceStatus("external", pair.Value),
                    StringComparer.OrdinalIgnoreCase),
                info.Version,
                info.ManagedByHub,
                null,
                null,
                null);
        }

        return new HubAppServerResponse(
            resolvedWorkspace,
            canonical,
            HubAppServerStates.Stopped,
            null,
            new Dictionary<string, string>(),
            new Dictionary<string, HubServiceStatus>(),
            null,
            false,
            null,
            null,
            null);
    }

    public async Task<HubAppServerResponse> StopAsync(string workspacePath, CancellationToken cancellationToken)
    {
        var (_, canonical, craftPath) = ResolveWorkspace(workspacePath);
        if (!_entries.TryGetValue(canonical, out var entry))
            return GetByWorkspace(workspacePath);

        await entry.Mutex.WaitAsync(cancellationToken);
        try
        {
            if (entry.Process is { })
            {
                entry.State = HubAppServerStates.Stopping;
                Persist(entry);
                await StopManagedProcessesAsync(entry, craftPath);
            }

            entry.State = HubAppServerStates.Stopped;
            entry.LastExitedAt = DateTimeOffset.UtcNow;
            Persist(entry);
            _events.Publish("appserver.exited", canonical, new { stopped = true });
            return entry.ToResponse();
        }
        finally
        {
            entry.Mutex.Release();
        }
    }

    public async Task<HubAppServerResponse> RestartAsync(
        string workspacePath,
        HubRuntimeToolsRequest? runtimeTools,
        CancellationToken cancellationToken)
    {
        await StopAsync(workspacePath, cancellationToken);
        return await EnsureAsync(new EnsureAppServerRequest
        {
            WorkspacePath = workspacePath,
            StartIfMissing = true,
            RuntimeTools = runtimeTools
        }, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        await _healthCts.CancelAsync();
        if (_healthTask is not null)
        {
            try { await _healthTask; }
            catch (OperationCanceledException) { }
        }

        foreach (var entry in _entries.Values)
        {
            await entry.Mutex.WaitAsync();
            try
            {
                await StopManagedProcessesAsync(entry, Path.Combine(entry.CanonicalWorkspacePath, ".craft"));
                entry.State = HubAppServerStates.Stopped;
                entry.LastExitedAt = DateTimeOffset.UtcNow;
                Persist(entry);
                _events.Publish("appserver.exited", entry.CanonicalWorkspacePath, new { hubStopping = true });
            }
            finally
            {
                entry.Mutex.Release();
                entry.Mutex.Dispose();
            }
        }

        _healthCts.Dispose();
    }

    private ServicePlan BuildServicePlan(
        string canonicalWorkspacePath,
        string craftPath,
        HubRuntimeToolsRequest? runtimeTools)
    {
        var configPath = Path.Combine(craftPath, "config.json");
        var config = AppConfig.LoadWithGlobalFallback(configPath);
        var endpoints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var status = new Dictionary<string, HubServiceStatus>(StringComparer.OrdinalIgnoreCase);
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal);
        var usedPorts = new HashSet<int>();

        var wsPort = AllocateUniquePort(usedPorts);
        var wsHost = "127.0.0.1";
        var wsToken = CreateToken();
        var wsProbeUrl = $"ws://{wsHost}:{wsPort}/ws";
        var wsResponseUrl = $"{wsProbeUrl}?token={Uri.EscapeDataString(wsToken)}";
        endpoints["appServerWebSocket"] = wsResponseUrl;
        status["appServerWebSocket"] = new HubServiceStatus("allocated", wsResponseUrl);

        environment[ManagedAppServerEnvironment.ManagedFlag] = "1";
        environment[ManagedAppServerEnvironment.HubApiBaseUrl] = _hubApiBaseUrl;
        environment[ManagedAppServerEnvironment.HubToken] = _hubToken;
        environment[ManagedAppServerEnvironment.WebSocketHost] = wsHost;
        environment[ManagedAppServerEnvironment.WebSocketPort] = wsPort.ToString();
        environment[ManagedAppServerEnvironment.WebSocketToken] = wsToken;

        _events.Publish("port.allocated", canonicalWorkspacePath, new { service = "appServerWebSocket", port = wsPort });

        if (config.DashBoard.Enabled && config.Tracing.Enabled)
        {
            var port = AllocateUniquePort(usedPorts);
            var url = $"http://127.0.0.1:{port}/dashboard";
            endpoints["dashboard"] = url;
            status["dashboard"] = new HubServiceStatus("allocated", url);
            environment[ManagedAppServerEnvironment.DashboardHost] = "127.0.0.1";
            environment[ManagedAppServerEnvironment.DashboardPort] = port.ToString();
            _events.Publish("port.allocated", canonicalWorkspacePath, new { service = "dashboard", port });
        }
        else
        {
            status["dashboard"] = new HubServiceStatus("disabled", Reason: "Dashboard or tracing is disabled.");
        }

        AddRuntimeTools(runtimeTools, environment, status);

        return new ServicePlan(
            environment,
            endpoints,
            status,
            wsProbeUrl,
            wsToken,
            NormalizeOptionalPath(runtimeTools?.NodeBin),
            runtimeTools?.NodeRunAsNode == true,
            NormalizeOptionalPath(runtimeTools?.ModulesDir));
    }

    internal static void AddRuntimeTools(
        HubRuntimeToolsRequest? runtimeTools,
        Dictionary<string, string?> environment,
        Dictionary<string, HubServiceStatus>? status = null)
    {
        if (!string.IsNullOrWhiteSpace(runtimeTools?.RipgrepPath))
            environment["DOTCRAFT_RG_PATH"] = runtimeTools.RipgrepPath.Trim();

        var nodeBin = NormalizeOptionalPath(runtimeTools?.NodeBin);
        var modulesDir = NormalizeOptionalPath(runtimeTools?.ModulesDir);
        if (!string.IsNullOrWhiteSpace(nodeBin))
            environment["DOTCRAFT_NODE_BIN"] = nodeBin;
        if (runtimeTools?.NodeRunAsNode == true)
            environment["DOTCRAFT_NODE_RUN_AS_NODE"] = "1";
        if (!string.IsNullOrWhiteSpace(modulesDir))
            environment["DOTCRAFT_MODULES_DIR"] = modulesDir;
        if (!string.IsNullOrWhiteSpace(runtimeTools?.BuiltInPluginRoots))
            environment["DOTCRAFT_BUILTIN_PLUGIN_ROOTS"] = runtimeTools.BuiltInPluginRoots.Trim();

        if (status != null)
        {
            status["typescriptRuntime"] =
                !string.IsNullOrWhiteSpace(nodeBin) && !string.IsNullOrWhiteSpace(modulesDir)
                    ? new HubServiceStatus("allocated", Reason: "TypeScript channel runtime hints are available.")
                    : new HubServiceStatus("unavailable", Reason: "TypeScript channel runtime is not configured. Launch DotCraft Desktop once or run 'dotcraft hub set-runtime'.");
        }
    }

    private static void ThrowIfExternalLockIsLive(ManagedEntry entry, string craftPath)
    {
        var lockPath = AppServerWorkspaceLock.GetLockFilePath(craftPath);
        var info = AppServerWorkspaceLock.TryRead(lockPath);
        if (info is null || !info.IsProcessAlive())
            return;

        if (entry.Process is { IsRunning: true } && entry.Process.ProcessId == info.Pid)
            return;

        throw new HubProtocolException(
            "workspaceLocked",
            "A live process appears to own the workspace AppServer lock.",
            StatusCodes.Status409Conflict,
            new { workspacePath = entry.CanonicalWorkspacePath, lockPath, pid = info.Pid });
    }

    private static void VerifyManagedLock(string craftPath, int expectedPid, string canonicalWorkspacePath)
    {
        var lockPath = AppServerWorkspaceLock.GetLockFilePath(craftPath);
        var info = AppServerWorkspaceLock.TryRead(lockPath);
        if (info is { ManagedByHub: true } && info.Pid == expectedPid && info.IsProcessAlive())
            return;

        throw new HubProtocolException(
            "appServerUnhealthy",
            "Managed AppServer did not publish the expected workspace lock.",
            StatusCodes.Status500InternalServerError,
            new { workspacePath = canonicalWorkspacePath, expectedPid, lockPath });
    }

    private HubRuntimeToolsRequest MergeRuntimeTools(HubRuntimeToolsRequest? runtimeTools)
    {
        if (_runtimeToolsStore == null)
            return HubRuntimeToolsStore.Merge(new HubRuntimeToolsRequest(), runtimeTools);
        return _runtimeToolsStore.MergeAndSave(runtimeTools);
    }

    private static string? GetTypeScriptRuntimeRestartReason(
        ManagedEntry entry,
        HubRuntimeToolsRequest runtimeTools)
    {
        var nodeBin = NormalizeOptionalPath(runtimeTools.NodeBin);
        var modulesDir = NormalizeOptionalPath(runtimeTools.ModulesDir);
        if (!string.Equals(entry.TypeScriptNodeBin, nodeBin, StringComparison.OrdinalIgnoreCase))
            return "TypeScript channel Node runtime changed; restart the AppServer to apply it.";
        if (entry.TypeScriptNodeRunAsNode != (runtimeTools.NodeRunAsNode == true))
            return "TypeScript channel Node launch mode changed; restart the AppServer to apply it.";
        if (!string.Equals(entry.TypeScriptModulesDir, modulesDir, StringComparison.OrdinalIgnoreCase))
            return "TypeScript channel modules directory changed; restart the AppServer to apply it.";
        return null;
    }

    private static void ApplyTypeScriptRuntimeRestartRequiredStatus(
        ManagedEntry entry,
        HubRuntimeToolsRequest runtimeTools,
        string reason)
    {
        entry.ServiceStatus = WithServiceStatus(
            entry.ServiceStatus,
            "typescriptRuntime",
            new HubServiceStatus(
                "restartRequired",
                Url: NormalizeOptionalPath(runtimeTools.ModulesDir),
                Reason: reason));
    }

    private static async Task StopManagedProcessesAsync(ManagedEntry entry, string craftPath)
    {
        if (entry.Process is { } process)
        {
            await process.DisposeAsync();
            entry.RecentStderr = process.RecentStderr;
            entry.ExitCode = process.ExitCode;
            entry.Process = null;
            entry.Pid = null;
            CleanupWorkspaceLock(craftPath);
        }
    }

    private static string? NormalizeOptionalPath(string? path)
        => string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path.Trim());

    private static IReadOnlyDictionary<string, HubServiceStatus> WithServiceStatus(
        IReadOnlyDictionary<string, HubServiceStatus> current,
        string key,
        HubServiceStatus value)
    {
        var next = new Dictionary<string, HubServiceStatus>(current, StringComparer.OrdinalIgnoreCase)
        {
            [key] = value
        };
        return next;
    }

    private static void CleanupWorkspaceLock(string craftPath)
    {
        AppServerWorkspaceLock.CleanupStaleFiles(craftPath);
    }

    private static async Task ProbeWebSocketAsync(string wsUrl, string token, CancellationToken cancellationToken)
    {
        await using var connection = await WebSocketClientConnection.ConnectAsync(new Uri(wsUrl), token, cancellationToken);
        var response = await connection.Wire.InitializeAsync(
            clientName: "dotcraft-hub",
            clientVersion: AppVersion.Informational,
            approvalSupport: false,
            streamingSupport: true);
        response.Dispose();
    }

    private void OnProcessCrashed(ManagedEntry entry)
    {
        RefreshExited(entry);
        entry.LastExitedAt = DateTimeOffset.UtcNow;
        Persist(entry);
        _events.Publish("appserver.exited", entry.CanonicalWorkspacePath, new
        {
            pid = entry.Pid,
            exitCode = entry.ExitCode,
            stderr = entry.RecentStderr
        });
    }

    private static void RefreshExited(ManagedEntry entry)
    {
        if (entry.Process is not { } process)
            return;

        try
        {
            if (process.IsRunning)
                return;

            entry.State = HubAppServerStates.Exited;
            entry.ExitCode = process.ExitCode;
            entry.RecentStderr = process.RecentStderr;
            entry.Pid = process.ProcessId;
            entry.LastExitedAt ??= DateTimeOffset.UtcNow;
        }
        catch
        {
            entry.State = HubAppServerStates.Exited;
            entry.LastExitedAt ??= DateTimeOffset.UtcNow;
        }
    }

    private async Task RunHealthLoopAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
                await CheckHealthOnceAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    internal async Task CheckHealthOnceAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in _entries.Values)
        {
            if (entry.Process is null)
                continue;

            await entry.Mutex.WaitAsync(cancellationToken);
            try
            {
                if (entry.Process is null || entry.State != HubAppServerStates.Running)
                    continue;

                var unhealthyReason = await GetUnhealthyReasonAsync(entry, cancellationToken);
                if (unhealthyReason is null)
                {
                    entry.LastSeenAt = DateTimeOffset.UtcNow;
                    Persist(entry);
                    continue;
                }

                entry.State = HubAppServerStates.Unhealthy;
                entry.LastError = unhealthyReason;
                entry.LastSeenAt = DateTimeOffset.UtcNow;
                Persist(entry);
                _events.Publish("appserver.unhealthy", entry.CanonicalWorkspacePath, new
                {
                    pid = entry.Pid,
                    error = unhealthyReason
                });
            }
            finally
            {
                entry.Mutex.Release();
            }
        }
    }

    private static async Task<string?> GetUnhealthyReasonAsync(ManagedEntry entry, CancellationToken cancellationToken)
    {
        if (entry.Process is not { } process)
            return null;

        if (!process.IsRunning)
        {
            RefreshExited(entry);
            return "Managed AppServer process exited.";
        }

        var craftPath = Path.Combine(entry.CanonicalWorkspacePath, ".craft");
        var lockInfo = AppServerWorkspaceLock.TryRead(AppServerWorkspaceLock.GetLockFilePath(craftPath));
        if (lockInfo is null)
            return "Workspace AppServer lock is missing.";
        if (lockInfo.Pid != process.ProcessId || !lockInfo.ManagedByHub || !lockInfo.IsProcessAlive())
            return "Workspace AppServer lock no longer matches the managed process.";

        if (!entry.Endpoints.TryGetValue("appServerWebSocket", out var wsUrl) || string.IsNullOrWhiteSpace(wsUrl))
            return "Managed AppServer WebSocket endpoint is missing.";

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            await using var connection = await WebSocketClientConnection.ConnectAsync(new Uri(wsUrl), null, cts.Token);
            var response = await connection.Wire.InitializeAsync(
                clientName: "dotcraft-hub-health",
                clientVersion: AppVersion.Informational,
                approvalSupport: false,
                streamingSupport: true);
            response.Dispose();
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return "Managed AppServer WebSocket health probe failed: " + ex.Message;
        }
    }

    private HubAppServerRegistryRecord RefreshPersistedRecord(HubAppServerRegistryRecord record)
    {
        if (Directory.Exists(Path.Combine(record.CanonicalWorkspacePath, ".craft")))
        {
            var lockPath = AppServerWorkspaceLock.GetLockFilePath(Path.Combine(record.CanonicalWorkspacePath, ".craft"));
            var info = AppServerWorkspaceLock.TryRead(lockPath);
            if (info is { } live && live.IsProcessAlive())
            {
                var refreshed = record with
                {
                    State = HubAppServerStates.Running,
                    Pid = live.Pid,
                    Endpoints = live.Endpoints,
                    ServiceStatus = live.Endpoints.ToDictionary(
                        pair => pair.Key,
                        pair => new HubServiceStatus("external", pair.Value),
                        StringComparer.OrdinalIgnoreCase),
                    ServerVersion = live.Version,
                    StartedByHub = false,
                    LastSeenAt = DateTimeOffset.UtcNow
                };
                _persisted[refreshed.CanonicalWorkspacePath] = refreshed;
                return refreshed;
            }
        }

        if (record.State is HubAppServerStates.Running
            or HubAppServerStates.Unhealthy
            or HubAppServerStates.Starting)
        {
            var refreshed = record with
            {
                State = HubAppServerStates.Exited,
                StartedByHub = false,
                LastExitedAt = record.LastExitedAt ?? DateTimeOffset.UtcNow
            };
            _persisted[refreshed.CanonicalWorkspacePath] = refreshed;
            return refreshed;
        }

        return record;
    }

    private void Persist(ManagedEntry entry)
    {
        _persisted[entry.CanonicalWorkspacePath] = entry.ToRegistryRecord();
        PersistAll();
    }

    private void PersistAll()
    {
        _store?.Save(_persisted.Values);
    }

    private static (string WorkspacePath, string CanonicalWorkspacePath, string CraftPath) ResolveWorkspace(string workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            throw new HubProtocolException(
                "workspaceNotFound",
                "Workspace path is required.",
                StatusCodes.Status400BadRequest);
        }

        var fullPath = Path.GetFullPath(workspacePath);
        if (!Directory.Exists(fullPath))
        {
            throw new HubProtocolException(
                "workspaceNotFound",
                "Workspace path does not exist.",
                StatusCodes.Status404NotFound,
                new { workspacePath = fullPath });
        }

        var canonical = new DirectoryInfo(fullPath).FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var craftPath = Path.Combine(canonical, ".craft");
        if (!Directory.Exists(craftPath))
        {
            throw new HubProtocolException(
                "workspaceNotFound",
                "DotCraft workspace not found.",
                StatusCodes.Status404NotFound,
                new { workspacePath = canonical, craftPath });
        }

        return (fullPath, canonical, craftPath);
    }

    private static int AllocateUniquePort(HashSet<int> usedPorts)
    {
        for (var attempt = 0; attempt < 32; attempt++)
        {
            var port = HubPortAllocator.AllocateLoopbackPort();
            if (usedPorts.Add(port))
                return port;
        }

        throw new HubProtocolException(
            "portUnavailable",
            "Hub could not allocate a required local port.",
            StatusCodes.Status500InternalServerError);
    }

    private static string CreateToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ManagedAppServerRegistry));
    }

    private sealed record ServicePlan(
        IReadOnlyDictionary<string, string?> Environment,
        IReadOnlyDictionary<string, string> ResponseEndpoints,
        IReadOnlyDictionary<string, HubServiceStatus> ServiceStatus,
        string WebSocketProbeUrl,
        string WebSocketToken,
        string? TypeScriptNodeBin,
        bool TypeScriptNodeRunAsNode,
        string? TypeScriptModulesDir);

    private sealed class ManagedEntry
    {
        public ManagedEntry(string workspacePath, string canonicalWorkspacePath)
        {
            WorkspacePath = workspacePath;
            CanonicalWorkspacePath = canonicalWorkspacePath;
        }

        public SemaphoreSlim Mutex { get; } = new(1, 1);

        public string WorkspacePath { get; }

        public string CanonicalWorkspacePath { get; }

        public string State { get; set; } = HubAppServerStates.Stopped;

        public AppServerProcess? Process { get; set; }

        public string? TypeScriptNodeBin { get; set; }

        public bool TypeScriptNodeRunAsNode { get; set; }

        public string? TypeScriptModulesDir { get; set; }

        public int? Pid { get; set; }

        public string? ServerVersion { get; set; }

        public bool StartedByHub { get; set; }

        public bool StartedInCurrentHubProcess { get; set; }

        public int? ExitCode { get; set; }

        public string? LastError { get; set; }

        public string? RecentStderr { get; set; }

        public DateTimeOffset? LastStartedAt { get; set; }

        public DateTimeOffset? LastSeenAt { get; set; }

        public DateTimeOffset? LastExitedAt { get; set; }

        public IReadOnlyDictionary<string, string> Endpoints { get; set; } =
            new Dictionary<string, string>();

        public IReadOnlyDictionary<string, HubServiceStatus> ServiceStatus { get; set; } =
            new Dictionary<string, HubServiceStatus>();

        public HubAppServerResponse ToResponse() => new(
            WorkspacePath,
            CanonicalWorkspacePath,
            State,
            Pid,
            Endpoints,
            ServiceStatus,
            ServerVersion,
            StartedByHub,
            ExitCode,
            LastError,
            RecentStderr);

        public HubAppServerRegistryRecord ToRegistryRecord() => new(
            WorkspacePath,
            CanonicalWorkspacePath,
            Path.GetFileName(CanonicalWorkspacePath),
            State,
            Pid,
            Endpoints,
            ServiceStatus,
            ServerVersion,
            StartedByHub,
            LastStartedAt,
            LastSeenAt,
            LastExitedAt,
            ExitCode,
            LastError,
            RecentStderr);
    }
}
