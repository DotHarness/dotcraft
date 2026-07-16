using System.Text.Json;
using DotCraft.Mcp;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using ModelContextProtocol.Protocol;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class McpElicitationConformanceTests
{
    [Theory]
    [InlineData("decline")]
    [InlineData("cancel")]
    public async Task FormElicitation_PreservesTerminalClientAction(string clientAction)
    {
        await using var manager = new McpClientManager();
        using var harness = new AppServerTestHarness(mcpClientManager: manager);
        await harness.InitializeAsync();
        harness.Transport.ApprovalHandler = (_, _) =>
            InMemoryTransport.BuildClientResponse(1, new { action = clientAction });

        var result = await manager.RequestElicitationAsync("reviews", FormRequest());

        Assert.Equal(clientAction, result.Action);
        Assert.Null(result.Content);
    }

    [Fact]
    public async Task FormElicitation_AcceptsSchemaValidContent()
    {
        await using var manager = new McpClientManager();
        using var harness = new AppServerTestHarness(mcpClientManager: manager);
        await harness.InitializeAsync();
        harness.Transport.ApprovalHandler = (_, _) =>
            InMemoryTransport.BuildClientResponse(1, new
            {
                action = "accept",
                content = new { project = "dotcraft" }
            });

        var result = await manager.RequestElicitationAsync("reviews", FormRequest());

        Assert.Equal("accept", result.Action);
        Assert.Equal("dotcraft", ((JsonElement)result.Content!["project"]).GetString());
    }

    [Fact]
    public async Task FormElicitation_InvalidAcceptedContent_IsDeclined()
    {
        await using var manager = new McpClientManager();
        using var harness = new AppServerTestHarness(mcpClientManager: manager);
        await harness.InitializeAsync();
        harness.Transport.ApprovalHandler = (_, _) =>
            InMemoryTransport.BuildClientResponse(1, new
            {
                action = "accept",
                content = new { unexpected = true }
            });

        var result = await manager.RequestElicitationAsync("reviews", FormRequest());

        Assert.Equal("decline", result.Action);
        Assert.Null(result.Content);
    }

    [Fact]
    public async Task FormElicitation_ClientDisconnect_IsDeclined()
    {
        await using var manager = new McpClientManager();
        using var harness = new AppServerTestHarness(mcpClientManager: manager);
        await harness.InitializeAsync();
        harness.Transport.ApprovalHandlerAsync = (_, _) =>
            Task.FromException<AppServerIncomingMessage>(new IOException("client disconnected"));

        var result = await manager.RequestElicitationAsync("reviews", FormRequest());

        Assert.Equal("decline", result.Action);
        Assert.Null(result.Content);
    }

    private static ElicitRequestParams FormRequest() => new()
    {
        Mode = "form",
        ElicitationId = "elicit-1",
        Message = "Choose a project",
        RequestedSchema = JsonSerializer.Deserialize<ElicitRequestParams.RequestSchema>("""
            {
              "type": "object",
              "properties": {
                "project": { "type": "string" }
              },
              "required": ["project"]
            }
            """, SessionWireJsonOptions.Default)!
    };
}
