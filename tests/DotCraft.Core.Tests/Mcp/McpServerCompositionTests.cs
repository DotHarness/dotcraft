using DotCraft.Mcp;
using McpServerConfig = DotCraft.Mcp.McpServerConfig;
using McpServerOrigin = DotCraft.Mcp.McpServerOrigin;
using Xunit;

namespace DotCraft.Core.Tests.Mcp;

public sealed class McpServerCompositionTests
{
    [Fact]
    public void Compose_NullThreadList_InheritsWorkspaceAndPluginServers()
    {
        var effective = McpServerComposition.Compose(
            "thread-1",
            null,
            [Server("workspace"), PluginServer("plugin:review", "review")]);

        Assert.Collection(
            effective,
            server => Assert.True(server.Origin.IsWorkspace),
            server => Assert.True(server.Origin.IsPlugin));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Compose_ThreadDisableOrReplacement_OverridesInheritedServers(bool replace)
    {
        McpServerConfig[] threadServers = replace ? [Server("thread-only")] : [];

        var effective = McpServerComposition.Compose(
            "thread-1",
            threadServers,
            [Server("workspace")]);

        Assert.DoesNotContain(effective, server => server.Name == "workspace");
        if (replace)
        {
            var replacement = Assert.Single(effective, server => server.Origin.IsThread);
            Assert.Equal("thread-1", replacement.Origin.ThreadId);
            Assert.Equal("thread-only", replacement.Origin.DeclaredName);
        }
        else
        {
            Assert.Empty(effective);
        }
    }

    [Fact]
    public void Compose_AdditionalServers_MergeAdditivelyWithoutReplacingInheritedServers()
    {
        var additional = new McpServerConfig
        {
            Name = "node_repl",
            Enabled = true,
            Transport = "streamableHttp",
            Url = "http://127.0.0.1:43210/mcp/route-1"
        };

        var effective = McpServerComposition.Compose(
            "thread-1",
            null,
            [Server("workspace"), PluginServer("plugin:review", "review")],
            [additional]);

        Assert.Equal(3, effective.Count);
        Assert.Contains(effective, server => server.Name == "workspace" && server.Origin.IsWorkspace);
        Assert.Contains(effective, server => server.Name == "plugin:review" && server.Origin.IsPlugin);
        var runtimeServer = Assert.Single(effective, server => server.Name == "node_repl");
        Assert.True(runtimeServer.Origin.IsThread);
        Assert.Equal("thread-1", runtimeServer.Origin.ThreadId);
        Assert.Equal("node_repl", runtimeServer.Origin.DeclaredName);
    }

    private static McpServerConfig Server(string name) => new() { Name = name, Enabled = false };

    private static McpServerConfig PluginServer(string name, string declaredName) => new()
    {
        Name = name,
        Enabled = false,
        Origin = McpServerOrigin.Plugin("review-plugin", "Review", declaredName)
    };
}
