using DotCraft.AppServer;
using DotCraft.Hub;

namespace DotCraft.Tests.Hub;

public sealed class ManagedAppServerRegistryTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(),
        "DotCraftManagedRegistry_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void CleanupStaleFiles_RemovesGuardLeftByKilledManagedAppServer()
    {
        var craftPath = Path.Combine(_tempDir, ".craft");
        Directory.CreateDirectory(craftPath);
        var lockPath = AppServerWorkspaceLock.GetLockFilePath(craftPath);
        var json = System.Text.Json.JsonSerializer.Serialize(new AppServerLockInfo(
            Pid: 999999,
            WorkspacePath: _tempDir,
            ManagedByHub: true,
            HubApiBaseUrl: "http://127.0.0.1:43000",
            StartedAt: DateTimeOffset.UtcNow,
            Version: "test",
            Endpoints: new Dictionary<string, string>()), HubJson.Options);
        File.WriteAllText(lockPath, json);
        File.WriteAllText(lockPath + ".guard", string.Empty);

        AppServerWorkspaceLock.CleanupStaleFiles(craftPath);

        Assert.False(File.Exists(lockPath));
        Assert.False(File.Exists(lockPath + ".guard"));
    }

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
    public async Task List_ReturnsPersistedWorkspaceWhenLockOwnerIsStillRunning()
    {
        var registryPath = Path.Combine(_tempDir, "hub", "appservers.json");
        var workspace = CreateWorkspace("running-workspace");
        var wsUrl = "ws://127.0.0.1:43123/ws?token=x";
        var lockPath = AppServerWorkspaceLock.GetLockFilePath(Path.Combine(workspace, ".craft"));
        var lockInfo = new AppServerLockInfo(
            Pid: Environment.ProcessId,
            WorkspacePath: workspace,
            ManagedByHub: true,
            HubApiBaseUrl: "http://127.0.0.1:43000",
            StartedAt: DateTimeOffset.UtcNow,
            Version: "test",
            Endpoints: new Dictionary<string, string> { ["appServerWebSocket"] = wsUrl });
        File.WriteAllText(lockPath, System.Text.Json.JsonSerializer.Serialize(lockInfo, HubJson.Options));
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
    public async Task Ensure_CleansStaleWorkspaceLockBeforeReportingStopped()
    {
        var workspace = CreateWorkspace("stale-lock-workspace");
        var lockPath = AppServerWorkspaceLock.GetLockFilePath(Path.Combine(workspace, ".craft"));
        WriteWorkspaceLock(lockPath, workspace, pid: 999999, wsUrl: "ws://127.0.0.1:43123/ws?token=x");
        File.WriteAllText(lockPath + ".guard", string.Empty);

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
        Assert.False(File.Exists(lockPath));
        Assert.False(File.Exists(lockPath + ".guard"));
    }

    [Fact]
    public async Task Ensure_ReusesHealthyExternalWorkspaceLock()
    {
        var registryPath = Path.Combine(_tempDir, "hub", "appservers.json");
        var workspace = CreateWorkspace("external-healthy-workspace");
        var wsUrl = "ws://127.0.0.1:43123/ws?token=x";
        var lockPath = AppServerWorkspaceLock.GetLockFilePath(Path.Combine(workspace, ".craft"));
        WriteWorkspaceLock(lockPath, workspace, pid: Environment.ProcessId, wsUrl: wsUrl);

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
        WriteWorkspaceLock(lockPath, workspace, pid: Environment.ProcessId, wsUrl: "ws://127.0.0.1:43123/ws?token=x");

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
    public async Task Dispose_DoesNotMarkAdoptedExternalWorkspaceStopped()
    {
        var registryPath = Path.Combine(_tempDir, "hub", "appservers.json");
        var workspace = CreateWorkspace("external-dispose-workspace");
        var wsUrl = "ws://127.0.0.1:43123/ws?token=x";
        var lockPath = AppServerWorkspaceLock.GetLockFilePath(Path.Combine(workspace, ".craft"));
        WriteWorkspaceLock(lockPath, workspace, pid: Environment.ProcessId, wsUrl: wsUrl);

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
    public void AddRuntimeTools_ForwardsBuiltInPluginCatalogsAsEnvironmentOverride()
    {
        var env = new Dictionary<string, string?>(StringComparer.Ordinal);
        var catalog = Path.Combine(_tempDir, "resources", "plugins", "dotcraft-bundled", "catalog.json");

        ManagedAppServerRegistry.AddRuntimeTools(
            new HubRuntimeToolsRequest { BuiltInPluginCatalogs = $" {catalog} " },
            env);

        Assert.Equal(catalog, env["DOTCRAFT_BUILTIN_PLUGIN_CATALOGS"]);
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
