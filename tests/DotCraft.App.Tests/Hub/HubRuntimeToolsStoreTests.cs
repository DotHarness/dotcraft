using System.Text.Json;
using DotCraft.Hub;
using Xunit;

namespace DotCraft.Tests.Hub;

public sealed class HubRuntimeToolsStoreTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "DotCraftRuntimeTools_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task MergeAndSave_ConcurrentUpdatesPersistBothFields()
    {
        Directory.CreateDirectory(_tempDir);
        var nodeBin = Path.Combine(_tempDir, "node");
        File.WriteAllText(nodeBin, string.Empty);
        var modulesDir = Directory.CreateDirectory(Path.Combine(_tempDir, "modules")).FullName;
        var paths = Enumerable.Range(0, 16)
            .Select(index => Path.Combine(_tempDir, index.ToString(), "runtime.json"))
            .ToArray();
        var stores = paths.Select(path => new HubRuntimeToolsStore(path)).ToArray();
        using var barrier = new Barrier(2);

        Task<Exception?[]> Update(HubRuntimeToolsRequest update) => Task.Factory.StartNew(
            () => stores.Select(store =>
            {
                barrier.SignalAndWait();
                return (Exception?)Record.Exception(() => store.MergeAndSave(update));
            }).ToArray(),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        var results = await Task.WhenAll(
            Update(new HubRuntimeToolsRequest { NodeBin = nodeBin }),
            Update(new HubRuntimeToolsRequest { ModulesDir = modulesDir }));

        Assert.All(results.SelectMany(errors => errors), error => Assert.Null(error));
        foreach (var path in paths)
        {
            var tools = JsonSerializer.Deserialize<HubRuntimeToolsDocument>(
                File.ReadAllText(path), HubJson.Options)!.RuntimeTools;
            Assert.Equal(nodeBin, tools.NodeBin);
            Assert.Equal(modulesDir, tools.ModulesDir);
        }
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);
}
