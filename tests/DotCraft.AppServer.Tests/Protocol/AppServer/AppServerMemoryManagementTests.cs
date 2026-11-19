using DotCraft.AppServer;
using DotCraft.Memory;
using DotCraft.Sessions;
using DotCraft.Workspaces;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerMemoryManagementTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"appserver_memory_management_{Guid.NewGuid():N}");
    private readonly string _craft;

    public AppServerMemoryManagementTests()
    {
        _craft = Path.Combine(_root, "workspace", ".craft");
        Directory.CreateDirectory(_craft);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { }
    }

    [Fact]
    public async Task ListStores_ReturnsTheWorkspaceScopeAndAttachedStoresWithoutCreatingAny()
    {
        var memoryStore = new MemoryStore(_craft);
        Directory.CreateDirectory(MemoryScopes.StoreDirectory(new ThreadConfiguration { MemoryScope = "nightly-report" }, _craft));
        Directory.CreateDirectory(Path.Combine(_craft, "memory-stores", "team-kb"));
        using var harness = CreateHarness(memoryStore);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.MemoryListStores, new { }));

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var stores = response.RootElement.GetProperty("result").GetProperty("stores").EnumerateArray()
            .Select(static store => (
                store.GetProperty("kind").GetString(),
                store.GetProperty("displayName").GetString(),
                store.GetProperty("writable").GetBoolean()))
            .ToArray();
        Assert.Equal(
            [("workspace", "workspace", true), ("scope", "nightly-report", true), ("attached", "team-kb", false)],
            stores);
    }

    private AppServerTestHarness CreateHarness(MemoryStore memoryStore)
    {
        var management = new MemoryManagementService(
            new DotCraftPaths(Path.GetDirectoryName(_craft)!, _craft, userDataPath: null),
            memoryStore);
        return new AppServerTestHarness(memoryStore: memoryStore, memoryManagement: management);
    }
}
