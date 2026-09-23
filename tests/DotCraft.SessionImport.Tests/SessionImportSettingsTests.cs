using System.Text.Json.Nodes;
using DotCraft.Protocol.AppServer;

namespace DotCraft.SessionImport.Tests;

public sealed class SessionImportSettingsTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeSessionService _sessions = new();
    private readonly FakeImportSource _source = new(SessionImportSources.ClaudeCode);
    private readonly string _workspace;
    private readonly string _craft;
    private readonly string _userConfig;

    public SessionImportSettingsTests()
    {
        _workspace = _temp.CreateDirectory("repo");
        _craft = _temp.CreateDirectory("repo", ".craft");
        _userConfig = _temp.Combine("home", ".craft", "config.json");
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void UpdateWritesOnlyTheSyncFieldsOfTheUserConfiguration()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_userConfig)!);
        File.WriteAllText(_userConfig, "{\"Model\":\"gpt-5\",\"sessionImport\":{\"MaxSessionsPerSource\":10,\"syncEnabled\":false}}");
        var service = CreateService();

        var updated = service.UpdateSettings(syncEnabled: true, sources: [SessionImportSources.Codex]);
        var partial = service.UpdateSettings(syncEnabled: null, sources: null);

        var root = JsonNode.Parse(File.ReadAllText(_userConfig))!.AsObject();
        Assert.Equal("gpt-5", root["Model"]!.GetValue<string>());
        var section = root["sessionImport"]!.AsObject();
        Assert.Equal(10, section["MaxSessionsPerSource"]!.GetValue<int>());
        Assert.True(section["syncEnabled"]!.GetValue<bool>());
        Assert.Equal(new[] { "codex" }, section["Sources"]!.AsArray().Select(static node => node!.GetValue<string>()));
        Assert.True(updated.SyncEnabled);
        Assert.Equal(new[] { "codex" }, partial.Sources);
        Assert.False(partial.WorkspaceOptOut);
        Assert.True(CreateService().GetSettings().SyncEnabled);
        Assert.Throws<ArgumentException>(() => service.UpdateSettings(null, ["unknown-agent"]));
    }

    [Fact]
    public void WorkspaceConfigurationCanOptOutOfSync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_userConfig)!);
        File.WriteAllText(_userConfig, "{\"SessionImport\":{\"SyncEnabled\":true}}");
        File.WriteAllText(Path.Combine(_craft, "config.json"), "{\"SessionImport\":{\"SyncEnabled\":false}}");
        var service = CreateService();

        var settings = service.GetSettings();

        Assert.True(settings.SyncEnabled);
        Assert.True(settings.WorkspaceOptOut);
        Assert.False(service.IsSyncActive);
    }

    [Fact]
    public async Task SyncRuntimeImportsOnStartAndRecordsTheSyncTime()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_userConfig)!);
        File.WriteAllText(_userConfig, "{\"SessionImport\":{\"SyncEnabled\":true}}");
        _source.Put("s1", turnCount: 1, hash: "h1", _workspace, DateTimeOffset.UtcNow.AddHours(-1));
        var service = CreateService();
        var completion = new TaskCompletionSource<ImportSessionsCompletedNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Completed += notification => completion.TrySetResult(notification);
        var runtime = new SessionImportSyncRuntime(service) { StartDelay = TimeSpan.Zero };

        runtime.Start();
        var completed = await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await runtime.StopAsync();

        Assert.Equal("sync", completed.Trigger);
        Assert.Equal("imported", Assert.Single(completed.Outcomes).Status);
        Assert.Equal(completed.StartedAt, service.GetSettings().LastSyncAt.Value, TimeSpan.FromMilliseconds(1));
    }

    private SessionImportService CreateService()
    {
        var service = new SessionImportService(
            new SessionImportServiceOptions(_workspace, _craft, new SessionImportConfig()),
            new SessionImportSettingsStore(_userConfig, Path.Combine(_craft, "config.json")),
            [_source]);
        service.SetSessionService(_sessions);
        return service;
    }
}
