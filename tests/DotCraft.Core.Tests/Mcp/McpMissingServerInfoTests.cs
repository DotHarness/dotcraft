using DotCraft.Mcp;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Mcp;

public sealed partial class McpStreamableHttpSessionRecoveryIntegrationTests
{
    [Fact]
    public async Task MissingServerInfo_PreservesConnectedInventoryAndToolExecution()
    {
        await using var server = MockStreamableHttpMcpServer.Start(new MockStreamableHttpMcpServer.Options
        {
            MissingServerInfo = true
        });
        await using var manager = new McpClientManager();
        var config = CreateServerConfig(server);
        await manager.ConnectAsync([config]);
        await manager.WaitForStartupCompletionAsync();
        var inventory = await manager.GetInventoryAsync(config.Name);
        Assert.NotNull(inventory);
        Assert.Null(inventory.ServerInfo);
        Assert.Single(inventory.Tools);
        var tool = Assert.IsAssignableFrom<AIFunction>(Assert.Single(manager.Tools));
        Assert.NotNull(await tool.InvokeAsync(new AIFunctionArguments()));
        Assert.Single(server.ToolCallRequests);
    }
}
