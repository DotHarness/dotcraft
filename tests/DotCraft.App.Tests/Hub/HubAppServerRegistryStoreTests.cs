using DotCraft.Hub;
using Xunit;

namespace DotCraft.Tests.Hub;

public sealed class HubAppServerRegistryStoreTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(),
        "DotCraftHubRegistry_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Load_ReturnsEmptyForMissingOrInvalidRegistry()
    {
        var path = Path.Combine(_tempDir, "hub", "appservers.json");
        var store = new HubAppServerRegistryStore(path);
        Assert.Empty(store.Load());

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "not-json");
        Assert.Empty(store.Load());
    }

    [Fact]
    public async Task Save_AllowsConcurrentWritesToSameStore()
    {
        var path = Path.Combine(_tempDir, "hub", "appservers.json");
        var store = new HubAppServerRegistryStore(path);

        var tasks = Enumerable.Range(0, 64)
            .Select(index => Task.Run(() =>
                store.Save([CreateRecord(Path.Combine(_tempDir, "workspace-" + index))])))
            .ToArray();

        await Task.WhenAll(tasks);

        var loadedRecord = Assert.Single(store.Load().Values);
        Assert.False(string.IsNullOrWhiteSpace(loadedRecord.CanonicalWorkspacePath));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".*.tmp"));
    }

    private static HubAppServerRegistryRecord CreateRecord(string workspacePath) => new(
        WorkspacePath: workspacePath,
        CanonicalWorkspacePath: workspacePath,
        DisplayName: Path.GetFileName(workspacePath),
        State: HubAppServerStates.Running,
        Pid: Environment.ProcessId,
        Endpoints: new Dictionary<string, string> { ["appServerWebSocket"] = "ws://127.0.0.1:43123/ws?token=x" },
        ServiceStatus: new Dictionary<string, HubServiceStatus> { ["appServerWebSocket"] = new("allocated", "ws://127.0.0.1:43123/ws?token=x") },
        ServerVersion: "test",
        StartedByHub: true,
        LastStartedAt: DateTimeOffset.UtcNow,
        LastSeenAt: DateTimeOffset.UtcNow);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
        }
    }
}
