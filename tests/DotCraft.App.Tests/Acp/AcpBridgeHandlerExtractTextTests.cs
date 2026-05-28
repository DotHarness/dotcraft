using System.Text.Json;
using DotCraft.Acp;
using DotCraft.Protocol;

namespace DotCraft.App.Tests.Acp;

public sealed class AcpBridgeHandlerExtractTextTests
{
    [Theory]
    [MemberData(nameof(TextPayloadCases))]
    public void ExtractTextFromPayload_ReturnsTextForSupportedPayloads(object payload, string expected)
    {
        Assert.Equal(expected, AcpBridgeHandler.ExtractTextFromPayload(payload));
    }

    [Fact]
    public void ExtractTextFromPayload_JsonElementMissingText_ReturnsNull()
    {
        var el = JsonSerializer.SerializeToElement(new { other = 1 });
        Assert.Null(AcpBridgeHandler.ExtractTextFromPayload(el));
    }

    public static IEnumerable<object[]> TextPayloadCases()
    {
        yield return [JsonSerializer.SerializeToElement(new { text = "hello" }), "hello"];
        yield return [new UserMessagePayload { Text = "typed" }, "typed"];
        yield return [new AgentMessagePayload { Text = "agent" }, "agent"];
    }
}
