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

    private string CreateWorkspace(string name)
    {
        var path = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(Path.Combine(path, ".craft"));
        return path;
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
