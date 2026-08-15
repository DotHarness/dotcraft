using DotCraft.AppServer;
using DotCraft.Hub;
using Xunit;

namespace DotCraft.Tests.Hub;

public sealed class ManagedAppServerRegistryTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(),
        "DotCraftManagedRegistry_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task List_HidesPersistedStoppedAndExitedRecordsFromPreviousHubProcess()
    {
        var registryPath = Path.Combine(_tempDir, "hub", "appservers.json");
        var stoppedWorkspace = CreateWorkspace("stopped-workspace");
        var exitedWorkspace = CreateWorkspace("exited-workspace");
        var store = new HubAppServerRegistryStore(registryPath);
        store.Save([
            CreateRegistryRecord(stoppedWorkspace, HubAppServerStates.Stopped),
            CreateRegistryRecord(exitedWorkspace, HubAppServerStates.Exited)
        ]);

        await using var registry = new ManagedAppServerRegistry(
            new HubEventBus(),
            "http://127.0.0.1:43000",
            "hub-token",
            registryPath: registryPath);

        Assert.Empty(registry.List());
    }

    [Fact]
    public async Task List_RefreshesPersistedStartingRecordWithoutLiveLockToExited()
    {
        var registryPath = Path.Combine(_tempDir, "hub", "appservers.json");
        var workspace = CreateWorkspace("starting-workspace");
        var store = new HubAppServerRegistryStore(registryPath);
        store.Save([CreateRegistryRecord(workspace, HubAppServerStates.Starting)]);

        await using var registry = new ManagedAppServerRegistry(
            new HubEventBus(),
            "http://127.0.0.1:43000",
            "hub-token",
            registryPath: registryPath);

        Assert.Empty(registry.List());
        var persisted = Assert.Single(store.Load().Values);
        Assert.Equal(HubAppServerStates.Exited, persisted.State);
        Assert.False(persisted.StartedByHub);
    }

    [Fact]
    public async Task List_ReturnsPersistedWorkspaceWhenLockOwnerIsStillRunning()
    {
        var registryPath = Path.Combine(_tempDir, "hub", "appservers.json");
        var workspace = CreateWorkspace("running-workspace");
        var wsUrl = "ws://127.0.0.1:43123/ws?token=x";
        using var workspaceLock = AcquireWorkspaceLock(workspace, wsUrl);
        var store = new HubAppServerRegistryStore(registryPath);
        store.Save([CreateRegistryRecord(workspace, HubAppServerStates.Stopped)]);

        await using var registry = new ManagedAppServerRegistry(
            new HubEventBus(),
            "http://127.0.0.1:43000",
            "hub-token",
            registryPath: registryPath);

        var listed = Assert.Single(registry.List());
        Assert.Equal(HubAppServerStates.Running, listed.State);
        Assert.Equal(Environment.ProcessId, listed.Pid);
        Assert.Equal(wsUrl, listed.Endpoints["appServerWebSocket"]);
    }

    [Fact]
    public async Task Ensure_ReportsStoppedWithoutMutatingUnheldWorkspaceLock()
    {
        var workspace = CreateWorkspace("stale-lock-workspace");
        var lockPath = AppServerWorkspaceLock.GetLockFilePath(Path.Combine(workspace, ".craft"));
        WriteWorkspaceLock(lockPath, workspace, pid: 999999, wsUrl: "ws://127.0.0.1:43123/ws?token=x");

        await using var registry = new ManagedAppServerRegistry(
            new HubEventBus(),
            "http://127.0.0.1:43000",
            "hub-token");

        var response = await registry.EnsureAsync(new EnsureAppServerRequest
        {
            WorkspacePath = workspace,
            StartIfMissing = false
        }, CancellationToken.None);

        Assert.Equal(HubAppServerStates.Stopped, response.State);
        Assert.True(File.Exists(lockPath));
    }

    [Fact]
    public async Task Ensure_ReusesHealthyExternalWorkspaceLock()
    {
        var registryPath = Path.Combine(_tempDir, "hub", "appservers.json");
        var workspace = CreateWorkspace("external-healthy-workspace");
        var wsUrl = "ws://127.0.0.1:43123/ws?token=x";
        using var workspaceLock = AcquireWorkspaceLock(workspace, wsUrl);

        await using var registry = new ManagedAppServerRegistry(
            new HubEventBus(),
            "http://127.0.0.1:43000",
            "hub-token",
            registryPath: registryPath)
        {
            ExistingAppServerProbeAsync = (_, _) => Task.FromResult<string?>(null)
        };

        var response = await registry.EnsureAsync(new EnsureAppServerRequest
        {
            WorkspacePath = workspace,
            StartIfMissing = true
        }, CancellationToken.None);

        Assert.Equal(HubAppServerStates.Running, response.State);
        Assert.Equal(Environment.ProcessId, response.Pid);
        Assert.False(response.StartedByHub);
        Assert.Equal(wsUrl, response.Endpoints["appServerWebSocket"]);
        Assert.Equal("external", response.ServiceStatus["appServerWebSocket"].State);

        var inspected = registry.GetByWorkspace(workspace);
        Assert.Equal(HubAppServerStates.Running, inspected.State);
        Assert.False(inspected.StartedByHub);

        var listed = Assert.Single(registry.List());
        Assert.Equal(HubAppServerStates.Running, listed.State);
        Assert.False(listed.StartedByHub);

        var stopResponse = await registry.StopAsync(workspace, CancellationToken.None);
        Assert.Equal(HubAppServerStates.Running, stopResponse.State);
        Assert.False(stopResponse.StartedByHub);
        Assert.Equal(wsUrl, stopResponse.Endpoints["appServerWebSocket"]);

        listed = Assert.Single(registry.List());
        Assert.Equal(HubAppServerStates.Running, listed.State);
        Assert.False(listed.StartedByHub);
    }

    [Fact]
    public async Task Ensure_RejectsLiveWorkspaceLockWhenEndpointProbeFails()
    {
        var workspace = CreateWorkspace("external-unhealthy-workspace");
        var lockPath = AppServerWorkspaceLock.GetLockFilePath(Path.Combine(workspace, ".craft"));
        using var workspaceLock = AcquireWorkspaceLock(workspace, "ws://127.0.0.1:43123/ws?token=x");

        await using var registry = new ManagedAppServerRegistry(
            new HubEventBus(),
            "http://127.0.0.1:43000",
            "hub-token")
        {
            ExistingAppServerProbeAsync = (_, _) => Task.FromResult<string?>("probe failed")
        };

        var ex = await Assert.ThrowsAsync<HubProtocolException>(() => registry.EnsureAsync(new EnsureAppServerRequest
        {
            WorkspacePath = workspace,
            StartIfMissing = true
        }, CancellationToken.None));

        Assert.Equal("workspaceLocked", ex.Code);
        Assert.Equal("probe failed", ex.Details?.GetType().GetProperty("reason")?.GetValue(ex.Details));
        Assert.True(File.Exists(lockPath));
    }

    [Fact]
    public async Task Ensure_DisposesStartedProcessWhenStartupProbeFails()
    {
        var registryPath = Path.Combine(_tempDir, "hub", "appservers.json");
        var workspace = CreateWorkspace("probe-failure-workspace");
        var process = new FakeManagedAppServerProcess(
            processId: 12345,
            exitCode: 42,
            recentStderr: "probe stderr");

        await using var registry = new ManagedAppServerRegistry(
            new HubEventBus(),
            "http://127.0.0.1:43000",
            "hub-token",
            registryPath: registryPath)
        {
            StartAppServerProcessAsync = (_, _, _, _) => Task.FromResult<IManagedAppServerProcess>(process),
            ManagedWebSocketProbeAsync = (_, _, _) => Task.FromException(new InvalidOperationException("probe failed"))
        };

        var ex = await Assert.ThrowsAsync<HubProtocolException>(() => registry.EnsureAsync(new EnsureAppServerRequest
        {
            WorkspacePath = workspace,
            StartIfMissing = true
        }, CancellationToken.None));

        Assert.Equal("appServerStartFailed", ex.Code);
        Assert.True(process.Disposed);

        var inspected = registry.GetByWorkspace(workspace);
        Assert.Equal(HubAppServerStates.Exited, inspected.State);
        Assert.Equal("probe failed", inspected.LastError);
        Assert.Equal(12345, inspected.Pid);
        Assert.Equal(42, inspected.ExitCode);
        Assert.Equal("probe stderr", inspected.RecentStderr);
    }

    [Fact]
    public async Task Ensure_PreservesStructuredProcessStartupFailure()
    {
        var workspace = CreateWorkspace("stdio-startup-failure-workspace");
        await using var registry = new ManagedAppServerRegistry(
            new HubEventBus(),
            "http://127.0.0.1:43000",
            "hub-token")
        {
            StartAppServerProcessAsync = (_, _, _, _) => Task.FromException<IManagedAppServerProcess>(
                new DotCraft.CLI.AppServerProcessStartupException(
                    "stdioInitialize",
                    17,
                    "workspace lock is already held",
                    new EndOfStreamException("transport closed")))
        };

        var error = await Assert.ThrowsAsync<HubProtocolException>(() => registry.EnsureAsync(
            new EnsureAppServerRequest
            {
                WorkspacePath = workspace,
                StartIfMissing = true
            },
            CancellationToken.None));

        Assert.Equal("appServerStartFailed", error.Code);
        Assert.Equal("stdioInitialize", Detail(error, "stage"));
        Assert.Equal("processExited", Detail(error, "failureKind"));
        Assert.Equal(17, Detail(error, "exitCode"));
        Assert.Equal("workspace lock is already held", Detail(error, "recentStderr"));
    }

    [Fact]
    public async Task Ensure_DoesNotFailWhenRegistryPersistenceFails()
    {
        var workspace = CreateWorkspace("persistence-failure-workspace");
        var blockedParent = Path.Combine(_tempDir, "blocked-registry-parent");
        File.WriteAllText(blockedParent, "not a directory");
        var registryPath = Path.Combine(blockedParent, "appservers.json");
        var process = new FakeManagedAppServerProcess(
            processId: Environment.ProcessId,
            exitCode: null,
            recentStderr: string.Empty);
        AppServerWorkspaceLock? workspaceLock = null;

        await using var registry = new ManagedAppServerRegistry(
            new HubEventBus(),
            "http://127.0.0.1:43000",
            "hub-token",
            registryPath: registryPath)
        {
            StartAppServerProcessAsync = (_, canonical, _, _) =>
            {
                workspaceLock = AcquireWorkspaceLock(canonical, "ws://127.0.0.1:43123/ws?token=x");
                return Task.FromResult<IManagedAppServerProcess>(process);
            },
            ManagedWebSocketProbeAsync = (_, _, _) => Task.CompletedTask
        };

        var response = await registry.EnsureAsync(new EnsureAppServerRequest
        {
            WorkspacePath = workspace,
            StartIfMissing = true
        }, CancellationToken.None);

        Assert.Equal(HubAppServerStates.Running, response.State);
        Assert.Equal(Environment.ProcessId, response.Pid);
        Assert.True(response.StartedByHub);
        workspaceLock?.DeleteAfterDispose();
    }

    [Fact]
    public async Task Dispose_DoesNotMarkAdoptedExternalWorkspaceStopped()
    {
        var registryPath = Path.Combine(_tempDir, "hub", "appservers.json");
        var workspace = CreateWorkspace("external-dispose-workspace");
        var wsUrl = "ws://127.0.0.1:43123/ws?token=x";
        using var workspaceLock = AcquireWorkspaceLock(workspace, wsUrl);

        var registry = new ManagedAppServerRegistry(
            new HubEventBus(),
            "http://127.0.0.1:43000",
            "hub-token",
            registryPath: registryPath)
        {
            ExistingAppServerProbeAsync = (_, _) => Task.FromResult<string?>(null)
        };

        await registry.EnsureAsync(new EnsureAppServerRequest
        {
            WorkspacePath = workspace,
            StartIfMissing = true
        }, CancellationToken.None);

        await registry.DisposeAsync();

        var persisted = Assert.Single(new HubAppServerRegistryStore(registryPath).Load().Values);
        Assert.Equal(HubAppServerStates.Running, persisted.State);
        Assert.False(persisted.StartedByHub);
        Assert.Equal(wsUrl, persisted.Endpoints["appServerWebSocket"]);
    }

    [Fact]
    public void AddRuntimeTools_ForwardsRipgrepPathAsEnvironmentOverride()
    {
        var env = new Dictionary<string, string?>(StringComparer.Ordinal);

        ManagedAppServerRegistry.AddRuntimeTools(
            new HubRuntimeToolsRequest { RipgrepPath = " C:/Tools/rg.exe " },
            env);

        Assert.Equal("C:/Tools/rg.exe", env["DOTCRAFT_RG_PATH"]);
    }

    [Fact]
    public void AddRuntimeTools_ForwardsBuiltInPluginRootsAsEnvironmentOverride()
    {
        var env = new Dictionary<string, string?>(StringComparer.Ordinal);
        var roots = Path.Combine(_tempDir, "resources", "plugins", "dotcraft-bundled", "plugins");

        ManagedAppServerRegistry.AddRuntimeTools(
            new HubRuntimeToolsRequest { BuiltInPluginRoots = $" {roots} " },
            env);

        Assert.Equal(roots, env["DOTCRAFT_BUILTIN_PLUGIN_ROOTS"]);
    }

    [Fact]
    public void RuntimeToolsMerge_AcceptsHttpsDefaultPluginRegistryUrl()
    {
        var merged = HubRuntimeToolsStore.Merge(
            new HubRuntimeToolsRequest(),
            new HubRuntimeToolsRequest { DefaultPluginRegistryUrl = " https://example.test/registry.zip " });

        Assert.Equal("https://example.test/registry.zip", merged.DefaultPluginRegistryUrl);
    }

    [Fact]
    public void RuntimeToolsMerge_RejectsHttpDefaultPluginRegistryUrl()
    {
        var merged = HubRuntimeToolsStore.Merge(
            new HubRuntimeToolsRequest { DefaultPluginRegistryUrl = "https://existing.test/registry.zip" },
            new HubRuntimeToolsRequest { DefaultPluginRegistryUrl = "http://example.test/registry.zip" });

        Assert.Equal("https://existing.test/registry.zip", merged.DefaultPluginRegistryUrl);
    }

    [Fact]
    public void HubPaths_ResolvesDefaultChatWorkspaceUnderCraftHome()
    {
        var paths = HubPaths.Resolve(_tempDir);

        Assert.Equal(
            Path.Combine(_tempDir, ".craft", "workspaces", "chats"),
            paths.DefaultChatWorkspacePath);
    }

    [Fact]
    public void DefaultChatWorkspace_EnsureCreatesSkeletonAndPreservesConfig()
    {
        var paths = HubPaths.Resolve(_tempDir);
        var workspace = DefaultChatWorkspace.Ensure(paths);
        var craftPath = Path.Combine(workspace, ".craft");
        var configPath = Path.Combine(craftPath, "config.json");

        Assert.Equal(Path.GetFullPath(paths.DefaultChatWorkspacePath), workspace);
        Assert.True(Directory.Exists(Path.Combine(craftPath, "memory")));
        Assert.True(Directory.Exists(Path.Combine(craftPath, "skills")));
        Assert.True(Directory.Exists(Path.Combine(craftPath, "security")));
        Assert.Equal("{}" + Environment.NewLine, File.ReadAllText(configPath));

        File.WriteAllText(configPath, "{\"keep\":true}" + Environment.NewLine);
        DefaultChatWorkspace.Ensure(paths);

        Assert.Equal("{\"keep\":true}" + Environment.NewLine, File.ReadAllText(configPath));
    }

    [Fact]
    public async Task Ensure_StartsDefaultChatWorkspaceCreatedByHelper()
    {
        var registryPath = Path.Combine(_tempDir, "hub", "appservers.json");
        var workspace = DefaultChatWorkspace.Ensure(HubPaths.Resolve(_tempDir));
        var process = new FakeManagedAppServerProcess(
            processId: Environment.ProcessId,
            exitCode: null,
            recentStderr: string.Empty);
        AppServerWorkspaceLock? workspaceLock = null;
        File.WriteAllText(
            AppServerWorkspaceLock.GetLockFilePath(Path.Combine(workspace, ".craft")),
            "orphaned lock metadata");

        await using var registry = new ManagedAppServerRegistry(
            new HubEventBus(),
            "http://127.0.0.1:43000",
            "hub-token",
            registryPath: registryPath)
        {
            StartAppServerProcessAsync = (_, canonical, _, _) =>
            {
                workspaceLock = AcquireWorkspaceLock(canonical, "ws://127.0.0.1:43123/ws?token=x");
                return Task.FromResult<IManagedAppServerProcess>(process);
            },
            ManagedWebSocketProbeAsync = (_, _, _) => Task.CompletedTask
        };

        var response = await registry.EnsureAsync(new EnsureAppServerRequest
        {
            WorkspacePath = workspace,
            StartIfMissing = true
        }, CancellationToken.None);

        Assert.Equal(HubAppServerStates.Running, response.State);
        Assert.Equal(Path.GetFullPath(workspace), response.CanonicalWorkspacePath);
        Assert.True(response.StartedByHub);
        workspaceLock?.DeleteAfterDispose();
    }

    private string CreateWorkspace(string name)
    {
        var path = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(Path.Combine(path, ".craft"));
        return path;
    }

    private static void WriteWorkspaceLock(string lockPath, string workspace, int pid, string wsUrl)
    {
        var lockInfo = new AppServerLockInfo(
            Pid: pid,
            WorkspacePath: workspace,
            ManagedByHub: true,
            HubApiBaseUrl: "http://127.0.0.1:43000",
            StartedAt: DateTimeOffset.UtcNow,
            Version: "test",
            Endpoints: new Dictionary<string, string> { ["appServerWebSocket"] = wsUrl });
        File.WriteAllText(lockPath, System.Text.Json.JsonSerializer.Serialize(lockInfo, HubJson.Options));
    }

    private static AppServerWorkspaceLock AcquireWorkspaceLock(string workspace, string wsUrl)
    {
        var paths = new DotCraft.Workspaces.DotCraftPaths(
            workspace,
            Path.Combine(workspace, ".craft"),
            userDataPath: null);
        Assert.True(AppServerWorkspaceLock.TryAcquire(paths, out var workspaceLock, out _));
        workspaceLock!.Publish(new AppServerLockInfo(
            Pid: Environment.ProcessId,
            WorkspacePath: workspace,
            ManagedByHub: true,
            HubApiBaseUrl: "http://127.0.0.1:43000",
            StartedAt: DateTimeOffset.UtcNow,
            Version: "test",
            Endpoints: new Dictionary<string, string> { ["appServerWebSocket"] = wsUrl }));
        return workspaceLock;
    }

    private static object? Detail(HubProtocolException error, string name) =>
        error.Details?.GetType().GetProperty(name)?.GetValue(error.Details);

    private static HubAppServerRegistryRecord CreateRegistryRecord(string workspacePath, string state) => new(
        WorkspacePath: workspacePath,
        CanonicalWorkspacePath: workspacePath,
        DisplayName: Path.GetFileName(workspacePath),
        State: state,
        Pid: null,
        Endpoints: new Dictionary<string, string>(),
        ServiceStatus: new Dictionary<string, HubServiceStatus>(),
        ServerVersion: null,
        StartedByHub: true,
        LastStartedAt: null,
        LastSeenAt: DateTimeOffset.UtcNow,
        LastExitedAt: state is HubAppServerStates.Stopped ? null : DateTimeOffset.UtcNow,
        ExitCode: null,
        LastError: null,
        RecentStderr: null);

    private sealed class FakeManagedAppServerProcess(
        int processId,
        int? exitCode,
        string recentStderr) : IManagedAppServerProcess
    {
        public bool IsRunning => !Disposed;

        public int? ExitCode => exitCode;

        public string RecentStderr => recentStderr;

        public int ProcessId => processId;

        public string? ServerVersion => "test-version";

        public bool Disposed { get; private set; }

        public event Action? OnCrashed;

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }

        public void RaiseCrashed() => OnCrashed?.Invoke();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // ignored
        }
    }
}
