using System.Collections.Concurrent;
using System.Security.Cryptography;
using DotCraft.AppServer;
using DotCraft.CLI;
using DotCraft.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotCraft.Hub;

/// <summary>
/// Tracks and supervises Hub-managed workspace AppServer processes.
/// </summary>
public sealed partial class ManagedAppServerRegistry : IAsyncDisposable
{
    internal static readonly StringComparer WorkspaceComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly ConcurrentDictionary<string, ManagedEntry> _entries;
    private readonly HubEventBus _events;
    private readonly string _hubApiBaseUrl;
    private readonly string _hubToken;
    private readonly string? _dotcraftBin;
    private readonly HubAppServerRegistryStore? _store;
    private readonly HubRuntimeToolsStore? _runtimeToolsStore;
    private readonly ConcurrentDictionary<string, HubAppServerRegistryRecord> _persisted;
    private readonly ILogger<ManagedAppServerRegistry> _logger;
    private readonly CancellationTokenSource _stopping = new();
    private bool _disposed;

    internal Func<AppServerLockInfo, CancellationToken, Task<string?>> ExistingAppServerProbeAsync { get; set; } =
        ProbeExistingAppServerAsync;

    internal Func<string?, string, IReadOnlyDictionary<string, string?>, CancellationToken, Task<IManagedAppServerProcess>> StartAppServerProcessAsync { get; set; }

    public ManagedAppServerRegistry(
        HubEventBus events,
        string hubApiBaseUrl,
        string hubToken,
        string? dotcraftBin = null,
        string? registryPath = null,
        string? runtimeToolsPath = null,
        ILogger<ManagedAppServerRegistry>? logger = null)
    {
        _entries = new ConcurrentDictionary<string, ManagedEntry>(WorkspaceComparer);
        _events = events;
        _hubApiBaseUrl = hubApiBaseUrl;
        _hubToken = hubToken;
        _dotcraftBin = dotcraftBin;
        _store = string.IsNullOrWhiteSpace(registryPath) ? null : new HubAppServerRegistryStore(registryPath);
        _runtimeToolsStore = string.IsNullOrWhiteSpace(runtimeToolsPath) ? null : new HubRuntimeToolsStore(runtimeToolsPath);
        _logger = logger ?? NullLogger<ManagedAppServerRegistry>.Instance;
        StartAppServerProcessAsync = async (bin, workspace, environment, cancellationToken) =>
            new AppServerProcessAdapter(await AppServerProcess.StartAsync(
                dotcraftBin: bin,
                workspacePath: workspace,
                environmentVariables: environment,
                createNoWindow: true,
                attachWindowsJob: true,
                ct: cancellationToken,
                logger: _logger));
        _persisted = new ConcurrentDictionary<string, HubAppServerRegistryRecord>(WorkspaceComparer);
        foreach (var record in _store?.Load().Values ?? [])
        {
            if (WithLiveLock(record) is { } live)
                _persisted[live.CanonicalWorkspacePath] = live;
        }
    }

    public void BeginShutdown() => _stopping.Cancel();

    public async Task<HubAppServerResponse> EnsureAsync(EnsureAppServerRequest request, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        using var start = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopping.Token);
        cancellationToken = start.Token;
        cancellationToken.ThrowIfCancellationRequested();
        var (workspacePath, canonical, craftPath) = ResolveWorkspace(request.WorkspacePath);
        using var workspaceScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["WorkspacePath"] = canonical
        });
        var entry = _entries.GetOrAdd(canonical, _ => new ManagedEntry(workspacePath, canonical));

        await entry.Mutex.WaitAsync(cancellationToken);
        try
        {
            RefreshExited(entry);
            if (entry.Process is { IsRunning: true } && entry.State == HubAppServerStates.Running)
            {
                var effectiveRuntimeTools = MergeRuntimeTools(request.RuntimeTools);
                var runtimeRestartReason = GetRuntimeToolsRestartReason(entry, effectiveRuntimeTools);
                if (runtimeRestartReason is not null)
                    ApplyRuntimeToolsRestartRequiredStatus(entry, effectiveRuntimeTools, runtimeRestartReason);

                entry.LastSeenAt = DateTimeOffset.UtcNow;
                Persist(entry);
                return entry.ToResponse();
            }

            if (await TryUseExistingWorkspaceLockAsync(entry, craftPath, cancellationToken) is { } existingBeforeStart)
                return existingBeforeStart;

            if (!request.StartIfMissing)
            {
                entry.State = HubAppServerStates.Stopped;
                entry.LastSeenAt = DateTimeOffset.UtcNow;
                Persist(entry);
                return entry.ToResponse();
            }

            if (entry.Process is { })
            {
                await StopManagedProcessesAsync(entry);
            }

            if (await TryUseExistingWorkspaceLockAsync(entry, craftPath, cancellationToken) is { } existing)
                return existing;

            var plan = BuildServicePlan(canonical, craftPath, MergeRuntimeTools(request.RuntimeTools));
            entry.AdoptedExternalLock = false;
            entry.State = HubAppServerStates.Starting;
            entry.LastError = null;
            entry.RecentStderr = null;
            entry.ExitCode = null;
            entry.Endpoints = plan.ResponseEndpoints;
            entry.ServiceStatus = plan.ServiceStatus;
            entry.LastSeenAt = DateTimeOffset.UtcNow;
            Persist(entry);
            _events.Publish("appserver.starting", canonical, new { endpoints = plan.ResponseEndpoints });

            IManagedAppServerProcess? startedProcess = null;
            try
            {
                var process = await StartAppServerProcessAsync(
                    _dotcraftBin,
                    canonical,
                    plan.Environment,
                    cancellationToken);
                startedProcess = process;

                cancellationToken.ThrowIfCancellationRequested();
                VerifyManagedLock(craftPath, process.ProcessId, canonical);

                process.OnCrashed += () => OnProcessCrashed(entry);
                entry.Process = process;
                startedProcess = null;
                entry.State = HubAppServerStates.Running;
                entry.Pid = process.ProcessId;
                entry.ServerVersion = process.ServerVersion;
                entry.StartedByHub = true;
                entry.StartedInCurrentHubProcess = true;
                entry.TypeScriptNodeBin = plan.TypeScriptNodeBin;
                entry.TypeScriptNodeRunAsNode = plan.TypeScriptNodeRunAsNode;
                entry.TypeScriptModulesDir = plan.TypeScriptModulesDir;
                entry.BuiltInPluginRoots = plan.BuiltInPluginRoots;
                entry.DefaultPluginRegistryUrl = plan.DefaultPluginRegistryUrl;
                entry.RecentStderr = process.RecentStderr;
                entry.LastStartedAt = DateTimeOffset.UtcNow;
                entry.LastSeenAt = DateTimeOffset.UtcNow;
                Persist(entry);
                _events.Publish("appserver.running", canonical, new { pid = process.ProcessId, endpoints = plan.ResponseEndpoints });
                _logger.LogInformation("Managed AppServer started with process {ProcessId}", process.ProcessId);
                return entry.ToResponse();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Managed AppServer failed during startup");
                if (startedProcess is not null)
                    await DisposeStartedProcessAfterFailureAsync(entry, startedProcess);

                var startupFailure = ex as AppServerProcessStartupException;
                if (startupFailure is not null)
                {
                    entry.ExitCode = startupFailure.ExitCode;
                    entry.RecentStderr = startupFailure.RecentStderr;
                }

                entry.State = HubAppServerStates.Exited;
                entry.LastError = ex.Message;
                Persist(entry);
                _events.Publish("appserver.exited", canonical, new
                {
                    error = ex.Message,
                    pid = entry.Pid,
                    exitCode = entry.ExitCode,
                    stderr = entry.RecentStderr
                });

                if (ex is HubProtocolException or OperationCanceledException)
                    throw;

                throw new HubProtocolException(
                    "appServerStartFailed",
                    "Managed AppServer failed during startup.",
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        workspacePath = canonical,
                        error = ex.Message,
                        stage = startupFailure?.Stage ?? "readiness",
                        failureKind = startupFailure?.FailureKind ?? ClassifyStartupFailure(ex),
                        exitCode = entry.ExitCode,
                        recentStderr = entry.RecentStderr
                    });
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

        foreach (var record in _persisted.Values)
        {
            if (WithLiveLock(record) is { } live)
            {
                _persisted[live.CanonicalWorkspacePath] = live;
                responses[live.CanonicalWorkspacePath] = live.ToResponse();
            }
            else
            {
                _persisted.TryRemove(record.CanonicalWorkspacePath, out _);
            }
        }

        foreach (var entry in _entries.Values)
        {
            if (entry.AdoptedExternalLock)
                RefreshAdoptedExternalEntry(entry, Path.Combine(entry.CanonicalWorkspacePath, ".craft"));
            else
                RefreshExited(entry);

            if (!ShouldListEntry(entry))
                continue;

            Track(entry);
            responses[entry.CanonicalWorkspacePath] = entry.ToResponse();
        }

        PersistAll();

        return responses.Values
            .OrderBy(e => e.CanonicalWorkspacePath, WorkspaceComparer)
            .ToArray();
    }

    internal IReadOnlySet<string> RunningWorkspaces() =>
        List()
            .Where(appServer => appServer.State == HubAppServerStates.Running)
            .Select(appServer => appServer.CanonicalWorkspacePath)
            .ToHashSet(WorkspaceComparer);

    private static bool ShouldListEntry(ManagedEntry entry) =>
        entry.StartedInCurrentHubProcess ||
        entry.State is HubAppServerStates.Running
            or HubAppServerStates.Starting
            or HubAppServerStates.Stopping;

    public HubAppServerResponse GetByWorkspace(string workspacePath)
    {
        var (resolvedWorkspace, canonical, craftPath) = ResolveWorkspace(workspacePath);
        if (_entries.TryGetValue(canonical, out var entry))
        {
            if (entry.AdoptedExternalLock)
                RefreshAdoptedExternalEntry(entry, craftPath);
            else
                RefreshExited(entry);

            return entry.ToResponse();
        }

        var lockInfo = AppServerWorkspaceLock.TryRead(AppServerWorkspaceLock.GetLockFilePath(craftPath));
        if (lockInfo is { } info
            && AppServerWorkspaceLock.IsHeld(AppServerWorkspaceLock.GetLockFilePath(craftPath)))
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
                false,
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
            if (entry.AdoptedExternalLock)
            {
                RefreshAdoptedExternalEntry(entry, craftPath);
                Persist(entry);
                return entry.ToResponse();
            }

            if (entry.Process is { })
            {
                entry.State = HubAppServerStates.Stopping;
                Persist(entry);
                await StopManagedProcessesAsync(entry);
            }

            entry.State = HubAppServerStates.Stopped;
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
        ThrowIfDisposed();
        using var restart = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopping.Token);
        cancellationToken = restart.Token;
        cancellationToken.ThrowIfCancellationRequested();
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
        BeginShutdown();
        await Task.WhenAll(_entries.Values.Select(StopEntryForDisposeAsync));
        _stopping.Dispose();
    }

    private async Task StopEntryForDisposeAsync(ManagedEntry entry)
    {
        await entry.Mutex.WaitAsync();
        try
        {
            if (entry.AdoptedExternalLock)
            {
                RefreshAdoptedExternalEntry(entry, Path.Combine(entry.CanonicalWorkspacePath, ".craft"));
                Persist(entry);
                return;
            }

            await StopManagedProcessesAsync(entry);
            entry.State = HubAppServerStates.Stopped;
            Persist(entry);
            _events.Publish("appserver.exited", entry.CanonicalWorkspacePath, new { hubStopping = true });
        }
        finally
        {
            entry.Mutex.Release();
            entry.Mutex.Dispose();
        }
    }

    private HubRuntimeToolsRequest MergeRuntimeTools(HubRuntimeToolsRequest? runtimeTools)
    {
        if (_runtimeToolsStore == null)
            return HubRuntimeToolsStore.Merge(new HubRuntimeToolsRequest(), runtimeTools);
        return _runtimeToolsStore.MergeAndSave(runtimeTools);
    }

    private static string? GetRuntimeToolsRestartReason(
        ManagedEntry entry,
        HubRuntimeToolsRequest runtimeTools)
    {
        var nodeBin = NormalizeOptionalPath(runtimeTools.NodeBin);
        var modulesDir = NormalizeOptionalPath(runtimeTools.ModulesDir);
        var builtInPluginRoots = NormalizeOptionalValue(runtimeTools.BuiltInPluginRoots);
        var defaultPluginRegistryUrl = NormalizeOptionalValue(runtimeTools.DefaultPluginRegistryUrl);
        if (!string.Equals(entry.TypeScriptNodeBin, nodeBin, StringComparison.OrdinalIgnoreCase))
            return "TypeScript channel Node runtime changed; restart the AppServer to apply it.";
        if (entry.TypeScriptNodeRunAsNode != (runtimeTools.NodeRunAsNode == true))
            return "TypeScript channel Node launch mode changed; restart the AppServer to apply it.";
        if (!string.Equals(entry.TypeScriptModulesDir, modulesDir, StringComparison.OrdinalIgnoreCase))
            return "TypeScript channel modules directory changed; restart the AppServer to apply it.";
        if (!string.Equals(entry.BuiltInPluginRoots, builtInPluginRoots, StringComparison.OrdinalIgnoreCase))
            return "Built-in plugin roots changed; restart the AppServer to apply them.";
        if (!string.Equals(entry.DefaultPluginRegistryUrl, defaultPluginRegistryUrl, StringComparison.OrdinalIgnoreCase))
            return "Default plugin registry URL changed; restart the AppServer to apply it.";
        return null;
    }

    private static void ApplyRuntimeToolsRestartRequiredStatus(
        ManagedEntry entry,
        HubRuntimeToolsRequest runtimeTools,
        string reason)
    {
        var statusKey = reason.StartsWith("TypeScript", StringComparison.Ordinal)
            ? "typescriptRuntime"
            : "builtInPlugins";
        var url = statusKey == "typescriptRuntime"
            ? NormalizeOptionalPath(runtimeTools.ModulesDir)
            : NormalizeOptionalValue(runtimeTools.DefaultPluginRegistryUrl)
              ?? NormalizeOptionalValue(runtimeTools.BuiltInPluginRoots);
        entry.ServiceStatus = WithServiceStatus(
            entry.ServiceStatus,
            statusKey,
            new HubServiceStatus(
                "restartRequired",
                Url: url,
                Reason: reason));
    }

    private static async Task DisposeStartedProcessAfterFailureAsync(
        ManagedEntry entry,
        IManagedAppServerProcess process)
    {
        try
        {
            await process.DisposeAsync();
        }
        finally
        {
            entry.RecentStderr = process.RecentStderr;
            entry.ExitCode = process.ExitCode;
            entry.Pid = process.ProcessId;
        }
    }

    private static async Task StopManagedProcessesAsync(ManagedEntry entry)
    {
        if (entry.Process is { } process)
        {
            await process.DisposeAsync();
            entry.RecentStderr = process.RecentStderr;
            entry.ExitCode = process.ExitCode;
            entry.Process = null;
            entry.AdoptedExternalLock = false;
            entry.Pid = null;
        }
    }

    private static string? NormalizeOptionalPath(string? path)
        => string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path.Trim());

    private static string? NormalizeOptionalValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ClassifyStartupFailure(Exception exception) => exception switch
    {
        TimeoutException => "timeout",
        OperationCanceledException => "cancelled",
        IOException => "transportError",
        _ => "startupError"
    };

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

    private void OnProcessCrashed(ManagedEntry entry)
    {
        RefreshExited(entry);
        Persist(entry);
        _events.Publish("appserver.exited", entry.CanonicalWorkspacePath, new
        {
            pid = entry.Pid,
            exitCode = entry.ExitCode,
            stderr = entry.RecentStderr
        });
        _logger.LogError(
            "Managed AppServer process {ProcessId} exited unexpectedly with code {ExitCode} for workspace {WorkspacePath}",
            entry.Pid,
            entry.ExitCode,
            entry.CanonicalWorkspacePath);
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
        }
        catch
        {
            entry.State = HubAppServerStates.Exited;
        }
    }

    private static HubAppServerRegistryRecord? WithLiveLock(HubAppServerRegistryRecord record)
    {
        var lockPath = AppServerWorkspaceLock.GetLockFilePath(Path.Combine(record.CanonicalWorkspacePath, ".craft"));
        if (AppServerWorkspaceLock.TryRead(lockPath) is not { } live || !AppServerWorkspaceLock.IsHeld(lockPath))
            return null;

        return record with
        {
            State = HubAppServerStates.Running,
            Pid = live.Pid,
            Endpoints = live.Endpoints,
            ServiceStatus = ExternalServiceStatus(live.Endpoints),
            ServerVersion = live.Version,
            StartedByHub = false,
            LastSeenAt = DateTimeOffset.UtcNow
        };
    }

    private void Persist(ManagedEntry entry)
    {
        Track(entry);
        PersistAll();
    }

    private void Track(ManagedEntry entry)
    {
        if (entry.State is HubAppServerStates.Running)
            _persisted[entry.CanonicalWorkspacePath] = entry.ToRegistryRecord();
        else
            _persisted.TryRemove(entry.CanonicalWorkspacePath, out _);
    }

    private void PersistAll()
    {
        try
        {
            _store?.Save(_persisted.Values);
        }
        catch
        {
            // Registry persistence is best-effort; live memory and workspace locks remain authoritative.
        }
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

        var canonical = CanonicalizeWorkspacePath(fullPath);
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

    internal static string CanonicalizeWorkspacePath(string path) =>
        new DirectoryInfo(Path.GetFullPath(path)).FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

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
}
