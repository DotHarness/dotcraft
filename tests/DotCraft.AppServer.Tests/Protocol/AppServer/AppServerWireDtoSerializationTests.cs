using System.Text.Json;
using Contract = DotCraft.Protocol.AppServer;
using DotCraft.Sessions.Wire;
using Xunit;

namespace DotCraft.Core.Tests.Protocol.AppServer;

public sealed class AppServerWireDtoSerializationTests
{
    [Fact]
    public void ContractDtos_PreserveTheirWireShapeAndOmitUnsetFields()
    {
        Assert.Equal(
            "{\"kind\":\"channelRejected\",\"channelName\":\"test-channel\",\"message\":\"Channel is not available.\"}",
            Serialize(new Contract.SystemEventNotification
            {
                Kind = "channelRejected",
                ChannelName = "test-channel",
                Message = "Channel is not available."
            }));

        Assert.Equal(
            "{\"threadId\":\"thread-1\",\"turnId\":\"turn-1\",\"itemId\":\"item-1\",\"delta\":\"chunk\"}",
            Serialize(new Contract.ItemDeltaNotification
            {
                ThreadId = "thread-1",
                TurnId = "turn-1",
                ItemId = "item-1",
                Delta = "chunk"
            }));

        Assert.Equal(
            "{\"source\":\"automations\",\"jobId\":\"job-1\",\"result\":\"done\",\"tokenUsage\":{\"inputTokens\":4,\"outputTokens\":2}}",
            Serialize(new Contract.SystemJobResultNotification
            {
                Source = "automations",
                JobId = "job-1",
                Result = "done",
                TokenUsage = new Contract.SystemJobTokenUsage { InputTokens = 4, OutputTokens = 2 }
            }));

        using var input = JsonDocument.Parse("{\"type\":\"object\",\"x-extra\":true}");
        using var output = JsonDocument.Parse("{\"type\":\"string\"}");
        Assert.Equal(
            "{\"name\":\"lookup\",\"description\":\"Looks up a value\",\"inputSchema\":{\"type\":\"object\",\"x-extra\":true},\"outputSchema\":{\"type\":\"string\"}}",
            Serialize(new Contract.McpRuntimeTool
            {
                Name = "lookup",
                Description = "Looks up a value",
                InputSchema = input.RootElement.Clone(),
                OutputSchema = output.RootElement.Clone()
            }));
    }

    private static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, SessionWireJsonOptions.Default);
}
