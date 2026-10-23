using System.Text.Json;
using DotCraft.Mcp;
using ModelContextProtocol.Protocol;
using DotCraft.AppServer;
using DotCraft.Sessions.Wire;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class McpElicitationConformanceTests
{
    [Fact]
    public async Task FormElicitation_MapsClientAnswersAndDeclinesInvalidContentOrDisconnects()
    {
        await using var manager = new McpClientManager();
        using var harness = new AppServerTestHarness(mcpClientManager: manager);
        await harness.InitializeAsync();

        foreach (var action in new[] { "decline", "cancel" })
        {
            harness.Transport.ApprovalHandler = (_, _) => InMemoryTransport.BuildClientResponse(1, new { action });
            var terminal = await manager.RequestElicitationAsync("reviews", FormRequest());
            Assert.Equal(action, terminal.Action);
            Assert.Null(terminal.Content);
        }

        harness.Transport.ApprovalHandler = (_, _) =>
            InMemoryTransport.BuildClientResponse(1, new { action = "accept", content = new { project = "dotcraft" } });
        var accepted = await manager.RequestElicitationAsync("reviews", FormRequest());
        Assert.Equal("accept", accepted.Action);
        Assert.Equal("dotcraft", ((JsonElement)accepted.Content!["project"]).GetString());

        harness.Transport.ApprovalHandler = (_, _) =>
            InMemoryTransport.BuildClientResponse(1, new { action = "accept", content = new { unexpected = true } });
        var invalid = await manager.RequestElicitationAsync("reviews", FormRequest());
        Assert.Equal("decline", invalid.Action);
        Assert.Null(invalid.Content);

        harness.Transport.ApprovalHandler = null;
        harness.Transport.ApprovalHandlerAsync = (_, _) =>
            Task.FromException<AppServerIncomingMessage>(new IOException("client disconnected"));
        var disconnected = await manager.RequestElicitationAsync("reviews", FormRequest());
        Assert.Equal("decline", disconnected.Action);
        Assert.Null(disconnected.Content);
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
