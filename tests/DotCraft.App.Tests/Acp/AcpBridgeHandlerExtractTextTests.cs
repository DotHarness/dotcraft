using System.Text.Json;
using DotCraft.Acp;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.App.Tests.Acp;

public sealed class AcpBridgeHandlerExtractTextTests
{
    [Theory]
    [MemberData(nameof(TextPayloadCases))]
    public void ExtractTextFromPayload_ReturnsTextForSupportedPayloads(object payload, string? expected)
    {
        Assert.Equal(expected, AcpBridgeHandler.ExtractTextFromPayload(payload));
    }

    public static IEnumerable<object?[]> TextPayloadCases()
    {
        yield return [JsonSerializer.SerializeToElement(new { text = "hello" }), "hello"];
        yield return [JsonSerializer.SerializeToElement(new { other = 1 }), null];
        yield return [new UserMessagePayload { Text = "typed" }, "typed"];
        yield return [new AgentMessagePayload { Text = "agent" }, "agent"];
    }
}
