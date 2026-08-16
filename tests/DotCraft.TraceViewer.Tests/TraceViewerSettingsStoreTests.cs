using Xunit;

namespace DotCraft.TraceViewer.Tests;

public sealed class TraceViewerSettingsStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "dotcraft-trace-viewer-settings-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Appearance_and_recent_workspace_persist_without_overwriting_each_other()
    {
        var store = new TraceViewerSettingsStore(Path.Combine(_root, "settings.json"));
        var workspacePath = Path.Combine(_root, "workspace");

        Assert.Equal(AppearancePreference.System, store.Load().Appearance);

        store.SaveRecentWorkspace(workspacePath);
        store.SaveAppearance(AppearancePreference.Light);

        var settings = store.Load();
        Assert.Equal(workspacePath, settings.RecentWorkspacePath);
        Assert.Equal(AppearancePreference.Light, settings.Appearance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
