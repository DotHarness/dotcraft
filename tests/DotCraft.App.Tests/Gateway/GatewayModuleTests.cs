using DotCraft.Configuration;
using DotCraft.Gateway;

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
}
