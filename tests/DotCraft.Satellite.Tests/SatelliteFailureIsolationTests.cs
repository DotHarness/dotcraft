using DotCraft.Satellite.Services;
using Xunit;

namespace DotCraft.Satellite.Tests;

public sealed class SatelliteFailureIsolationTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "DotCraftSatelliteLog_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void TrayCallbackFailure_IsLoggedAndDoesNotEscape()
    {
        var guard = new SatelliteCallbackGuard(new SatelliteLog(_directory));

        guard.Run("tray.update", () => throw new InvalidOperationException("tray failed"));

        var log = Assert.Single(Directory.GetFiles(_directory, "dotcraft-satellite-*.log"));
        Assert.Contains("ui.callback.failed", File.ReadAllText(log), StringComparison.Ordinal);
    }

    [Fact]
    public async Task IslandCallbackFailure_DisablesOnlyTheSurface()
    {
        var guard = new SatelliteCallbackGuard(new SatelliteLog(_directory));
        var disabled = false;

        await guard.RunAsync(
            "island.update",
            () => Task.FromException(new InvalidOperationException("island failed")),
            () => disabled = true);

        Assert.True(disabled);
        var log = Assert.Single(Directory.GetFiles(_directory, "dotcraft-satellite-*.log"));
        Assert.Contains("island.update", File.ReadAllText(log), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (Exception) { }
    }
}
