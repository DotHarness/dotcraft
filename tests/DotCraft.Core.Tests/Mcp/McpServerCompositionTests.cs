using DotCraft.Mcp;

namespace DotCraft.Core.Tests.Mcp;

public sealed class McpServerCompositionTests
{
    [Fact]
    public void Compose_NullThreadList_InheritsUserServersAndAppendsBinding()
    {
        var effective = McpServerComposition.Compose(
            "thread-1",
            null,
            [Server("workspace"), PluginServer("plugin:review", "review")],
            [BindingServer("board-binding", "board")]);

        Assert.Collection(
            effective,
            server => Assert.True(server.Origin.IsWorkspace),
            server => Assert.True(server.Origin.IsPlugin),
            server =>
            {
                Assert.Equal("binding:board-binding:board", server.Name);
                Assert.True(server.Origin.IsBinding);
                Assert.Equal("board-binding", server.Origin.BindingId);
                Assert.Equal("board", server.Origin.DeclaredName);
            });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Compose_ThreadDisableOrReplacement_DoesNotRemoveBinding(bool replace)
    {
        McpServerConfig[] threadServers = replace ? [Server("thread-only")] : [];

        var effective = McpServerComposition.Compose(
            "thread-1",
            threadServers,
            [Server("workspace")],
            [BindingServer("board-binding", "board")]);

        Assert.DoesNotContain(effective, server => server.Name == "workspace");
        var binding = Assert.Single(effective, server => server.Origin.IsBinding);
        Assert.Equal("board-binding", binding.Origin.BindingId);
        if (replace)
        {
            var replacement = Assert.Single(effective, server => server.Origin.IsThread);
            Assert.Equal("thread-1", replacement.Origin.ThreadId);
            Assert.Equal("thread-only", replacement.Origin.DeclaredName);
        }
    }

    [Fact]
    public async Task Compose_BindingRuntimeNameCannotBeShadowedInManager()
    {
        var effective = McpServerComposition.Compose(
            "thread-1",
            null,
            [Server("binding:board-binding:board")],
            [BindingServer("board-binding", "board")]);
        await using var manager = new McpClientManager();

        await manager.ConnectAsync(effective);
        var configs = await manager.ListConfigsAsync();

        Assert.Equal(2, configs.Count);
        Assert.Contains(configs, server => server.Origin.IsWorkspace);
        Assert.Contains(configs, server => server.Origin.IsBinding && server.Name == "binding:board-binding:board~2");
    }

    private static McpServerConfig Server(string name) => new() { Name = name, Enabled = false };

    private static McpServerConfig PluginServer(string name, string declaredName) => new()
    {
        Name = name,
        Enabled = false,
        Origin = McpServerOrigin.Plugin("review-plugin", "Review", declaredName)
    };

    private static McpServerConfig BindingServer(string bindingId, string declaredName) => new()
    {
        Name = declaredName,
        Enabled = false,
        Origin = McpServerOrigin.Binding(bindingId, declaredName)
    };
}
