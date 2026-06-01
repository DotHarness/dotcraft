using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Gateway;
using DotCraft.Hosting;
using DotCraft.Protocol.AppServer;
using DotCraft.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace DotCraft.Tests.Gateway;

public sealed class GatewayModuleTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsEnabled_FollowsAutomationDefaults(bool automationsEnabled)
    {
        var config = new AppConfig();
        if (!automationsEnabled)
            config.SetSection("Automations", new DotCraft.Automations.AutomationsConfig { Enabled = false });

        var module = new GatewayModule();

        Assert.Equal(automationsEnabled, module.IsEnabled(config));
    }

    [Fact]
    public void ConfigureServices_RegistersRuntimeContextPromptProvider()
    {
        var services = new ServiceCollection();
        var module = new GatewayModule();

        module.ConfigureServices(services, new ModuleContext
        {
            Config = new AppConfig(),
            Paths = new DotCraftPaths
            {
                WorkspacePath = Directory.GetCurrentDirectory(),
                CraftPath = Path.Combine(Directory.GetCurrentDirectory(), ".craft")
            }
        });

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<WireRuntimeAdditionalContextProvider>());
        Assert.Contains(
            provider.GetServices<IThreadSystemPromptContextProvider>(),
            p => p.ContextPageKey == ContextPageKeys.RuntimeAdditionalContext());
    }
}
