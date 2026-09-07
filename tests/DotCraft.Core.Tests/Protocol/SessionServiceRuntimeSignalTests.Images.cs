using System.ClientModel.Primitives;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using OpenAI.Responses;
using Xunit;

#pragma warning disable OPENAI001, MEAI001

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class SessionServiceRuntimeSignalTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("missing")]
    [InlineData("invalid")]
    [InlineData("interrupted")]
    [InlineData("cancelled")]
    public async Task ImageLifecycle_RealAdapterPersistsTerminalState(string outcome)
    {
        var sdk = new ResponsesClient("sk-test");
        using var client = new OpenAIResponsesToolSearchChatClient(sdk, "gpt-test", new FakeChatClient([]), new ImageEventTransport(outcome));
        await using var factory = CreateAgentFactory(client);
        var service = CreateService(factory, client);
        var thread = await service.CreateThreadAsync(MakeIdentity());
        var events = await CollectAsync(service.SubmitInputAsync(thread.Id, [new TextContent("Generate") ]));
        var loaded = await new ThreadStore(_tempDir).LoadThreadAsync(thread.Id);
        var image = Assert.Single(Assert.Single(loaded!.Turns).Items, item => item.Type == ItemType.ImageGeneration);
        Assert.Equal(ItemStatus.Completed, image.Status);
        var payload = Assert.IsType<ImageGenerationPayload>(image.Payload);
        Assert.Equal(outcome == "success" ? "completed" : "failed", payload.Status);
        Assert.Single(events, e => e.EventType == SessionEventType.ItemCompleted && e.ItemPayload?.Type == ItemType.ImageGeneration);
        if (outcome == "success")
        {
            Assert.Equal(ImageEventTransport.Png, payload.Result);
            Assert.Equal(Convert.FromBase64String(ImageEventTransport.Png), await File.ReadAllBytesAsync(payload.SavedPath!));
            Assert.Equal("saved", payload.SaveStatus);
        }
        else
        {
            Assert.Null(payload.SavedPath);
            Assert.NotNull(payload.ErrorCode);
        }
    }

    private sealed class ImageEventTransport(string outcome) : IResponsesToolSearchTransport
    {
        public const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aX1cAAAAASUVORK5CYII=";

        public async IAsyncEnumerable<StreamingResponseUpdate> CreateResponseStreamingAsync(CreateResponseOptions options,
            [EnumeratorCancellation] CancellationToken ct)
        {
            yield return Parse("""{"type":"response.image_generation_call.in_progress","sequence_number":1,"item_id":"ig_lifecycle","output_index":0}""");
            if (outcome == "interrupted") throw new InvalidOperationException("Stream interrupted.");
            if (outcome == "cancelled") throw new OperationCanceledException();
            if (outcome != "missing")
            {
                yield return Parse(JsonSerializer.Serialize(new
                {
                    type = "response.output_item.done", sequence_number = 2, output_index = 0,
                    item = new { type = "image_generation_call", id = "ig_lifecycle", status = "completed", result = outcome == "invalid" ? "invalid!" : Png }
                }));
            }
            await Task.CompletedTask;
        }

        private static StreamingResponseUpdate Parse(string json) =>
            ModelReaderWriter.Read<StreamingResponseUpdate>(BinaryData.FromString(json))!;
    }
}
