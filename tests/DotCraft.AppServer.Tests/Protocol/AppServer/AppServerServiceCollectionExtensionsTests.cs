using DotCraft.AppServer;
using DotCraft.Tools;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DotCraft.Tests.AppServer;

public sealed class AppServerServiceCollectionExtensionsTests
{
    [Fact]
    public void AddDotCraftAppServer_RegistersOneSharedClientCapabilityGraph()
    {
        var services = new ServiceCollection();
        services.AddDotCraftAppServer();
        services.AddDotCraftAppServer();

        using var provider = services.BuildServiceProvider();

        Assert.Same(
            provider.GetRequiredService<WireNodeReplProxy>(),
            provider.GetRequiredService<INodeReplProxy>());
        Assert.Single(provider.GetServices<WireDynamicToolProxy>());
        Assert.Same(
            provider.GetRequiredService<AppServerPluginManagementState>(),
            provider.GetRequiredService<AppServerPluginManagementState>());
    }
}
