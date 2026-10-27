using DotCraft.Mcp;
using McpServerConfig = DotCraft.Mcp.McpServerConfig;
using McpServerOrigin = DotCraft.Mcp.McpServerOrigin;
using Xunit;

namespace DotCraft.Core.Tests.Mcp;

public sealed class McpServerSelectorTests
{
    [Theory]
    [InlineData("p4", "alpha:p4")]
    [InlineData("beta:p4", "beta:p4")]
    public void Resolve_SelectsPluginServersByDeclaredOrRuntimeName(string selector, string expected)
    {
        McpServerConfig[] servers = [PluginServer("beta", "p4"), PluginServer("alpha", "p4")];

        Assert.Equal([expected], McpServerSelector.Resolve([selector], servers));
    }

    [Fact]
    public void Resolve_ConfiguredServerWithTheExactNameWinsOverPluginDeclaration()
    {
        McpServerConfig[] servers = [PluginServer("p4", "p4"), new() { Name = "p4" }];

        Assert.Equal(["p4"], McpServerSelector.Resolve(["p4"], servers));
    }

    private static McpServerConfig PluginServer(string pluginId, string declaredName) => new()
    {
        Name = $"{pluginId}:{declaredName}",
        Origin = McpServerOrigin.Plugin(pluginId, null, declaredName)
    };
}
