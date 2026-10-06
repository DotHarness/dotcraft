using DotCraft.Configuration;
using DotCraft.Hub;
using Xunit;

namespace DotCraft.Tests.Configuration;

public sealed class ConfigurationServiceApplyTests
{
    [Fact]
    public async Task Write_AssignsChangedValuesOntoTheRuntimeInstance_AndRaisesOneEvent()
    {
        using var fixture = new ConfigurationServiceFixture(user: """{ "Hub": { "Port": 4100 } }""");
        var lsp = fixture.Monitor.Current.Tools.Lsp;
        var hub = fixture.Monitor.Current.GetSection<HubConfig>("Hub");
        Assert.Equal(4100, hub.Port);

        var result = await fixture.Service.WriteAsync(
            [
                ConfigurationServiceFixture.Edit("Tools.Lsp.Enabled", "true"),
                ConfigurationServiceFixture.Edit("Hub.Port", "4200"),
                ConfigurationServiceFixture.Edit("InstantInterruptEnabled", "true")
            ],
            filePath: null,
            expectedVersion: null,
            source: "config/batchWrite");

        Assert.Same(lsp, fixture.Monitor.Current.Tools.Lsp);
        Assert.True(lsp.Enabled);
        Assert.Same(hub, fixture.Monitor.Current.GetSection<HubConfig>("Hub"));
        Assert.Equal(4200, hub.Port);
        var change = Assert.Single(fixture.Events);
        Assert.Equal("config/batchWrite", change.Source);
        Assert.Equal(["Hub.Port", "Tools.Lsp.Enabled"], change.Regions.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Write_RunsEachSubsystemHandlerOnceWithItsChangedKeyPaths()
    {
        using var fixture = new ConfigurationServiceFixture();
        var calls = new List<(string Subsystem, IReadOnlyList<string> KeyPaths)>();
        fixture.Service.RegisterSubsystemHandler("dreams", (keyPaths, _) =>
        {
            calls.Add(("dreams", keyPaths));
            return Task.CompletedTask;
        });
        fixture.Service.RegisterSubsystemHandler("lsp", (keyPaths, _) =>
        {
            calls.Add(("lsp", keyPaths));
            return Task.CompletedTask;
        });

        await fixture.Service.WriteAsync(
            [
                ConfigurationServiceFixture.Edit("Dreams.Enabled", "true"),
                ConfigurationServiceFixture.Edit("Dreams.AutoApply", "true"),
                ConfigurationServiceFixture.Edit("Memory.Enabled", "false"),
                ConfigurationServiceFixture.Edit("InstantInterruptEnabled", "false")
            ],
            filePath: null,
            expectedVersion: null,
            source: "test");

        var call = Assert.Single(calls);
        Assert.Equal("dreams", call.Subsystem);
        Assert.Equal(
            ["Dreams.AutoApply", "Dreams.Enabled", "Memory.Enabled"],
            call.KeyPaths.Order(StringComparer.Ordinal));
        Assert.False(fixture.Monitor.Current.Memory.Enabled);
        Assert.False(fixture.Monitor.Current.InstantInterruptEnabled);
    }

    [Fact]
    public async Task Write_WithoutEffectiveChange_RaisesNoEventAndRunsNoHandler()
    {
        using var fixture = new ConfigurationServiceFixture();
        var handlerCalls = 0;
        fixture.Service.RegisterSubsystemHandler("dreams", (_, _) =>
        {
            handlerCalls++;
            return Task.CompletedTask;
        });

        var result = await fixture.WriteAsync("Memory.Enabled", "true");

        Assert.Empty(fixture.Events);
        Assert.Equal(0, handlerCalls);
        Assert.True(ConfigurationServiceFixture.ReadFile(fixture.WorkspacePath)["Memory"]!["Enabled"]!.GetValue<bool>());
    }

    [Fact]
    public void ReloadMetadata_HotFieldsAreReadAtUseTime_AndRestartsHaveARegisteredHandler()
    {
        string[] useTimeFields =
        [
            "InstantInterruptEnabled",
            "ProjectDocMaxBytes",
            "Permissions.DefaultApprovalPolicy",
            "PromptSuggestions.Enabled",
            "SubAgent.DefaultWaitTimeoutMs",
            "SubAgent.DisabledProfiles",
            "SubAgent.EnableExternalCliSessionResume",
            "SubAgent.MaxConcurrentSubAgents",
            "SubAgent.MaxDepth",
            "SubAgent.MaxWaitTimeoutMs",
            "SubAgent.MinWaitTimeoutMs",
            "Tools.Shell.Policy.Rules",
            "WelcomeSuggestions.Enabled"
        ];
        var fields = ConfigSchemaRegistrations.CreateDescriptorRegistry().Fields;

        Assert.Equal(
            useTimeFields.Order(StringComparer.Ordinal),
            fields.Where(f => f.Reload == ReloadBehavior.Hot).Select(f => f.KeyPath).Order(StringComparer.Ordinal));
        Assert.All(
            fields.Where(f => f.Reload == ReloadBehavior.SubsystemRestart),
            f => Assert.Contains(f.SubsystemKey, ConfigurationSubsystems.Keys));
    }
}
