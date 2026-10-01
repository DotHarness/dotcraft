using System.Text.Json;
using System.Text.Json.Nodes;
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

    [Theory]
    [InlineData("/workspace/.craft/generated_images/thread/ig_saved.png", "/workspace/.craft/generated_images/thread")]
    [InlineData("C:\\data\\generated_images\\thread\\ig_saved.png", "C:\\data\\generated_images\\thread")]
    public void CreateResponseRequest_InsertsSavedPathHintAfterHostedImageCall(string savedPath, string directory)
    {
        using var document = JsonDocument.Parse(CreateRequestJson(
            "gpt-test",
            [
                new ChatMessage(ChatRole.Assistant, [CreateSavedImage("ig_saved", savedPath)]),
                new ChatMessage(ChatRole.User, "send it as a file")
            ],
            new ChatOptions()));

        var input = document.RootElement.GetProperty("input").EnumerateArray().ToArray();
        Assert.Equal(3, input.Length);
        Assert.Equal("image_generation_call", input[0].GetProperty("type").GetString());
        Assert.Equal("message", input[1].GetProperty("type").GetString());
        Assert.Equal("developer", input[1].GetProperty("role").GetString());
        Assert.StartsWith("msg_", input[1].GetProperty("id").GetString(), StringComparison.Ordinal);
        var part = Assert.Single(input[1].GetProperty("content").EnumerateArray());
        Assert.Equal("input_text", part.GetProperty("type").GetString());
        Assert.Contains($"{directory} as {savedPath}", part.GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.Equal("user", input[2].GetProperty("role").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1100)]
    public void CreateResponseRequest_OmitsSavedPathHintWithoutUsablePath(int? pathLength)
    {
        var savedPath = pathLength is { } length ? "/" + new string('a', length) + ".png" : null;
        using var document = JsonDocument.Parse(CreateRequestJson(
            "gpt-test",
            [new ChatMessage(ChatRole.Assistant, [CreateSavedImage("ig_saved", savedPath)])],
            new ChatOptions()));

        var item = Assert.Single(document.RootElement.GetProperty("input").EnumerateArray());
        Assert.Equal("image_generation_call", item.GetProperty("type").GetString());
    }

    [Fact]
    public void CreateResponseRequest_InsertsSavedPathHintIntoCanonicalInput()
    {
        var canonicalInput = new JsonArray
        {
            new JsonObject { ["type"] = "image_generation_call", ["id"] = "ig_saved", ["status"] = "completed" },
            new JsonObject { ["type"] = "function_call", ["id"] = "fc_1", ["call_id"] = "call_1", ["name"] = "SendFile", ["arguments"] = "{}" }
        };
        var messages = new[]
        {
            new ChatMessage(ChatRole.Assistant, [
                CreateSavedImage("ig_saved", "/workspace/.craft/generated_images/thread/ig_saved.png"),
                new FunctionCallContent("call_1", "SendFile")
            ])
        };

        var first = ResponsesToolSearchMapper.CreateResponseRequest(
            "gpt-test", messages, new ChatOptions(), canonicalInput: canonicalInput.DeepClone().AsArray());
        var second = ResponsesToolSearchMapper.CreateResponseRequest(
            "gpt-test", messages, new ChatOptions(), canonicalInput: canonicalInput.DeepClone().AsArray());
        var firstJson = SerializeOptions(first.Options);
        using var document = JsonDocument.Parse(firstJson);

        var input = document.RootElement.GetProperty("input").EnumerateArray().ToArray();
        Assert.Equal(["image_generation_call", "message", "function_call"],
            input.Select(item => item.GetProperty("type").GetString()));
        Assert.Contains("/workspace/.craft/generated_images/thread/ig_saved.png",
            input[1].GetProperty("content")[0].GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.Equal(firstJson, SerializeOptions(second.Options));
    }

    private static HostedImageGenerationContent CreateSavedImage(string id, string? savedPath) =>
        new()
        {
            Id = id,
            Status = "completed",
            ImageBytes = CreateImageBytes("image/png"),
            SavedPath = savedPath
        };
}
