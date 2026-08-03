using System.Text.Json;
using DotCraft.Acp;
using Xunit;

namespace DotCraft.App.Tests.Acp;

public sealed class AcpBridgeHandlerWireRoutingTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void ParamsForIdeExtForward_StripsThreadId_KeepsPayload()
    {
        var wire = JsonSerializer.SerializeToElement(new
        {
            threadId = "acp_sess_1",
            path = "/workspace/a.txt",
            offset = (int?)null,
            limit = (int?)null
        }, JsonOptions);

        var ide = AcpBridgeHandler.ParamsForIdeExtForward(wire);
        Assert.NotNull(ide);
        var json = JsonSerializer.Serialize(ide, JsonOptions);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.TryGetProperty("threadId", out _));
        Assert.Equal("/workspace/a.txt", doc.RootElement.GetProperty("path").GetString());

        var withoutThreadId = JsonSerializer.SerializeToElement(new { path = "/b" }, JsonOptions);
        var passthrough = AcpBridgeHandler.ParamsForIdeExtForward(withoutThreadId);
        var passthroughJson = JsonSerializer.Serialize(passthrough, JsonOptions);
        using var passthroughDoc = JsonDocument.Parse(passthroughJson);
        Assert.Equal("/b", passthroughDoc.RootElement.GetProperty("path").GetString());
    }
}
