using System.Text.Json;
using DotCraft.Agents;
using Microsoft.Extensions.AI;
using Xunit;

#pragma warning disable OPENAI001, MEAI001

namespace DotCraft.Tests.Agents;

public sealed partial class OpenAIResponsesToolSearchChatClientTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StreamingImages_StageAndPartialDoNotConsumeFinalResult(bool finalResponseOnly)
    {
        var bytes = CreateImageBytes("image/png");
        var item = new { type = "image_generation_call", id = "ig_final", status = "completed", result = Convert.ToBase64String(bytes) };
        var updates = new List<OpenAI.Responses.StreamingResponseUpdate>
        {
            CreateStreamingUpdate("""{"type":"response.image_generation_call.in_progress","sequence_number":1,"item_id":"ig_final","output_index":0}"""),
            CreateStreamingUpdate(JsonSerializer.Serialize(new { type = "response.image_generation_call.partial_image", sequence_number = 2, item_id = "ig_final", output_index = 0, partial_image_index = 0, partial_image_b64 = Convert.ToBase64String(bytes) })),
            CreateStreamingUpdate("""{"type":"response.image_generation_call.completed","sequence_number":3,"item_id":"ig_final","output_index":0}""")
        };
        if (!finalResponseOnly)
        {
            var done = JsonSerializer.Serialize(new { type = "response.output_item.done", sequence_number = 4, output_index = 0, item });
            updates.Add(CreateStreamingUpdate(done));
            updates.Add(CreateStreamingUpdate(done));
        }
        updates.Add(CreateStreamingUpdate(JsonSerializer.Serialize(new
        {
            type = "response.completed", sequence_number = 5,
            response = new { id = "resp_image", created_at = 1, model = "gpt-test", status = "completed", output = new[] { item } }
        })));
        using var client = CreateClient(new FakeChatClient(new ChatResponse()), new FakeToolSearchTransport(updates.ToArray()));
        var result = await CollectStreamingAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "Generate") ]));
        var contents = result.SelectMany(update => update.Contents).ToArray();
        var image = Assert.Single(contents.OfType<HostedImageGenerationContent>());
        Assert.Equal(bytes, image.ImageBytes);
        Assert.True(image.Succeeded);
        Assert.Empty(contents.OfType<ImageGenerationToolResultContent>());
    }
}
