using DotCraft.Hub;

namespace DotCraft.Tests.Hub;

public sealed class HubAppServerRegistryStoreTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(),
        "DotCraftHubRegistry_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void SaveAndLoad_PreservesKnownAppServerMetadata()
    {
        var path = Path.Combine(_tempDir, "hub", "appservers.json");
        var store = new HubAppServerRegistryStore(path);
        var record = new HubAppServerRegistryRecord(
            WorkspacePath: @"F:\dotcraft",
            CanonicalWorkspacePath: @"F:\dotcraft",
            DisplayName: "dotcraft",
            State: HubAppServerStates.Unhealthy,
            Pid: 123,
            Endpoints: new Dictionary<string, string> { ["appServerWebSocket"] = "ws://127.0.0.1:43123/ws?token=x" },
            ServiceStatus: new Dictionary<string, HubServiceStatus> { ["appServerWebSocket"] = new("allocated", "ws://127.0.0.1:43123/ws?token=x") },
            ServerVersion: "0.1.5",
            StartedByHub: true,
            LastStartedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            LastSeenAt: DateTimeOffset.UtcNow,
            LastExitedAt: null,
            ExitCode: null,
            LastError: "probe failed",
            RecentStderr: "stderr");

        store.Save([record]);

        var loaded = store.Load();
        var loadedRecord = Assert.Single(loaded.Values);
        Assert.Equal(record.CanonicalWorkspacePath, loadedRecord.CanonicalWorkspacePath);
        Assert.Equal(HubAppServerStates.Unhealthy, loadedRecord.State);
        Assert.Equal("probe failed", loadedRecord.LastError);
        Assert.Equal("ws://127.0.0.1:43123/ws?token=x", loadedRecord.Endpoints["appServerWebSocket"]);
    }

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
