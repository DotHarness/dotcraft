using System.Text.Json;
using DotCraft.AppServer;
using DotCraft.Mcp;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class McpToolApprovalModeProtocolTests : IDisposable
{
    private readonly string _craft = Path.Combine(Path.GetTempPath(), $"mcp_tool_approval_{Guid.NewGuid():N}", ".craft");

    public McpToolApprovalModeProtocolTests() => Directory.CreateDirectory(_craft);

    [Fact]
    public async Task Upsert_SetsMode_KeepsItWhenOmitted_AndRejectsUnknownValues()
    {
        await using var manager = new McpClientManager();
        using var harness = new AppServerTestHarness(mcpClientManager: manager, workspaceCraftPath: _craft);
        await harness.InitializeAsync();

        using (var set = await UpsertAsync(harness, new { name = "inventory", enabled = false, command = "inventory-mcp-server", toolApprovalMode = "approve" }))
            Assert.Equal("approve", ResultMode(set));
        Assert.Equal(McpToolApprovalMode.Approve, SavedMode());

        using (var omitted = await UpsertAsync(harness, new { name = "inventory", enabled = false, command = "inventory-mcp-server" }))
            Assert.Equal("approve", ResultMode(omitted));
        Assert.Equal(McpToolApprovalMode.Approve, SavedMode());

        using (var reset = await UpsertAsync(harness, new { name = "inventory", enabled = false, command = "inventory-mcp-server", toolApprovalMode = "auto" }))
            Assert.Equal("auto", ResultMode(reset));
        Assert.Equal(McpToolApprovalMode.Auto, SavedMode());

        using var rejected = await UpsertAsync(harness, new { name = "inventory", enabled = false, command = "inventory-mcp-server", toolApprovalMode = "always" });
        AppServerTestHarness.AssertIsErrorResponse(rejected, AppServerErrors.McpServerValidationFailedCode);
        Assert.Equal(McpToolApprovalMode.Auto, SavedMode());
    }

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(_craft)!, recursive: true); }
        catch { }
    }

    private McpToolApprovalMode SavedMode() =>
        McpScopeStore.Read(Path.Combine(_craft, "config.json"), "workspace").Single().ToolApprovalMode;

    private static string? ResultMode(JsonDocument response) =>
        response.RootElement.GetProperty("result").GetProperty("server").GetProperty("toolApprovalMode").GetString();

    private static async Task<JsonDocument> UpsertAsync(AppServerTestHarness harness, object server)
    {
        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.McpUpsert,
            JsonSerializer.SerializeToElement(new { scope = "workspace", server })));
        while (true)
        {
            var message = await harness.Transport.ReadNextSentAsync();
            if (message.RootElement.TryGetProperty("result", out _) || message.RootElement.TryGetProperty("error", out _))
                return message;
            message.Dispose();
        }
    }
}
