using DotCraft.Configuration;
using DotCraft.Modules;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DotCraft.Tests.Modules;

public sealed class ModuleRegistryTests
{
    [Fact]
    public void SelectPrimaryModule_IgnoresCapabilityModulesAndHonorsThePreferredHost()
    {
        var registry = new ModuleRegistry();
        registry.RegisterModule(new FakeModule("automations", priority: 50, canBePrimaryHost: false, enabled: true));
        registry.RegisterModule(new FakeModule("cli", priority: 0, canBePrimaryHost: true, enabled: true));
        var preferred = new ModuleRegistry();
        preferred.RegisterModule(new FakeModule("app-server", priority: 100, canBePrimaryHost: true, enabled: true));
        preferred.RegisterModule(new FakeModule("cli", priority: 0, canBePrimaryHost: true, enabled: true));

        Assert.Equal("cli", registry.SelectPrimaryModule(new AppConfig())?.Name);
        Assert.Equal("cli", preferred.SelectPrimaryModule(new AppConfig(), "cli")?.Name);
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
