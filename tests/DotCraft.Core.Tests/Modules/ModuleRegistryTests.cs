using DotCraft.Abstractions;
using DotCraft.Configuration;
using DotCraft.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace DotCraft.Tests.Modules;

public sealed class ModuleRegistryTests
{
    [Fact]
    public void SelectPrimaryModule_IgnoresEnabledCapabilityModules()
    {
        var registry = new ModuleRegistry();
        registry.RegisterModule(new FakeModule("automations", priority: 50, canBePrimaryHost: false, enabled: true));
        registry.RegisterModule(new FakeModule("cli", priority: 0, canBePrimaryHost: true, enabled: true));

        var primary = registry.SelectPrimaryModule(new AppConfig());

        Assert.NotNull(primary);
        Assert.Equal("cli", primary!.Name);
    }

    [Fact]
    public void SelectPrimaryModule_UsesPreferredPrimaryHostWhenProvided()
    {
        var registry = new ModuleRegistry();
        registry.RegisterModule(new FakeModule("gateway", priority: 100, canBePrimaryHost: true, enabled: true));
        registry.RegisterModule(new FakeModule("cli", priority: 0, canBePrimaryHost: true, enabled: true));

        var primary = registry.SelectPrimaryModule(new AppConfig(), "cli");

        Assert.NotNull(primary);
        Assert.Equal("cli", primary!.Name);
    }

    private sealed class FakeModule(string name, int priority, bool canBePrimaryHost, bool enabled) : ModuleBase
    {
        public override string Name => name;
        public override int Priority => priority;
        public override bool CanBePrimaryHost => canBePrimaryHost;
        public override bool IsEnabled(AppConfig config) => enabled;
        public override void ConfigureServices(IServiceCollection services, ModuleContext context) { }
    }
}
