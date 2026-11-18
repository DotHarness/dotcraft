using DotCraft.Configuration;
using Xunit;

namespace DotCraft.Tests.Configuration;

public sealed class ConfigurationServiceUserReloadTests
{
    [Fact]
    public async Task UserLayerWriteThroughOneRuntime_IsAppliedAndAnnouncedByAnother()
    {
        using var a = new ConfigurationServiceFixture();
        var b = OtherWorkspace(a, workspace: null);

        await a.WriteAsync("Tools.Lsp.Enabled", "true");
        Assert.False(b.Monitor.Current.Tools.Lsp.Enabled);

        await b.Service.ReloadUserLayerAsync();

        Assert.True(b.Monitor.Current.Tools.Lsp.Enabled);
        var change = Assert.Single(b.Events);
        Assert.Equal(ConfigurationService.ConfigReloadSource, change.Source);
        Assert.Equal(["Tools.Lsp.Enabled"], change.Regions);

        await b.Service.ReloadUserLayerAsync();
        Assert.Single(b.Events);
    }

    [Fact]
    public async Task UserLayerChange_ShadowedByTheWorkspaceLayer_ChangesNothing()
    {
        using var a = new ConfigurationServiceFixture();
        var b = OtherWorkspace(a, workspace: """{ "Tools": { "Lsp": { "Enabled": false } } }""");

        await a.WriteAsync("Tools.Lsp.Enabled", "true");
        await b.Service.ReloadUserLayerAsync();

        Assert.False(b.Monitor.Current.Tools.Lsp.Enabled);
        Assert.Empty(b.Events);
    }

    [Fact]
    public async Task UnreadableUserLayer_KeepsTheLastAppliedStateUntilItIsValid()
    {
        using var a = new ConfigurationServiceFixture();
        var b = OtherWorkspace(a, workspace: null);

        ConfigurationServiceFixture.WriteFile(a.UserPath, """{ "Tools": { "Lsp": """);
        await b.Service.ReloadUserLayerAsync();
        Assert.Empty(b.Events);

        ConfigurationServiceFixture.WriteFile(a.UserPath, """{ "Tools": { "Lsp": { "Enabled": true } } }""");
        await b.Service.ReloadUserLayerAsync();

        Assert.True(b.Monitor.Current.Tools.Lsp.Enabled);
        Assert.Equal(["Tools.Lsp.Enabled"], Assert.Single(b.Events).Regions);
    }

    [Fact]
    public async Task InvalidUserLayerValue_IsNotApplied()
    {
        using var a = new ConfigurationServiceFixture();
        var b = OtherWorkspace(a, workspace: null);
        var before = b.Monitor.Current.ProjectDocMaxBytes;

        ConfigurationServiceFixture.WriteFile(a.UserPath, """{ "ProjectDocMaxBytes": -1 }""");
        await b.Service.ReloadUserLayerAsync();

        Assert.Equal(before, b.Monitor.Current.ProjectDocMaxBytes);
        Assert.Empty(b.Events);
    }

    [Fact]
    public async Task LocalWrite_FirstAppliesThePendingUserLayerChange()
    {
        using var a = new ConfigurationServiceFixture();
        var b = OtherWorkspace(a, workspace: null);

        await a.WriteAsync("Tools.Lsp.Enabled", "true");
        await b.WriteAsync("Hub.Port", "4200");

        Assert.True(b.Monitor.Current.Tools.Lsp.Enabled);
        Assert.Equal(
            [(ConfigurationService.ConfigReloadSource, "Tools.Lsp.Enabled"), ("test", "Hub.Port")],
            b.Events.Select(e => (e.Source, Assert.Single(e.Regions))));
    }

    [Fact]
    public async Task UserLayerSubAgentProfileChange_ReloadsProfilesAndAnnouncesSubAgent()
    {
        using var a = new ConfigurationServiceFixture();
        var b = OtherWorkspace(a, workspace: null);
        var invalidations = 0;
        b.Service.RegisterSubsystemHandler(ConfigurationSubsystems.ThreadAgents, (_, _) =>
        {
            invalidations++;
            return Task.CompletedTask;
        });

        SubAgentProfilesPersistence.SaveState(
            a.UserPath,
            [],
            enableExternalCliSessionResume: false,
            [new SubAgentProfile { Name = "reviewer", Runtime = "cli-oneshot", Bin = "reviewer" }]);
        await b.Service.ReloadUserLayerAsync();

        Assert.Equal("reviewer", Assert.Single(b.Monitor.Current.SubAgentProfiles).Name);
        Assert.Contains(ConfigChangeRegions.SubAgent, Assert.Single(b.Events).Regions);
        Assert.Equal(1, invalidations);
    }

    private static Runtime OtherWorkspace(ConfigurationServiceFixture shared, string? workspace)
    {
        var workspacePath = shared.Scratch(Path.Combine("other", ".craft", "config.json"));
        if (workspace != null)
            ConfigurationServiceFixture.WriteFile(workspacePath, workspace);
        var monitor = new AppConfigMonitor(AppConfig.LoadWithGlobalFallback(workspacePath, shared.UserPath));
        var runtime = new Runtime(
            monitor,
            new ConfigurationService(ConfigSchemaRegistrations.CreateDescriptorRegistry(), monitor, shared.UserPath, workspacePath));
        monitor.Changed += (_, args) => runtime.Events.Add(args);
        return runtime;
    }

    private sealed record Runtime(AppConfigMonitor Monitor, ConfigurationService Service)
    {
        public List<AppConfigChangedEventArgs> Events { get; } = [];

        public Task<ConfigWriteResult> WriteAsync(string keyPath, string valueJson) =>
            Service.WriteAsync([ConfigurationServiceFixture.Edit(keyPath, valueJson)], null, null, "test");
    }
}
