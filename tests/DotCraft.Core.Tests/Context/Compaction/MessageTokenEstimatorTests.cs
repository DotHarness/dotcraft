using System.Text.Json;
using DotCraft.Context.Compaction;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Context.Compaction;

public sealed class MessageTokenEstimatorTests
{
    [Fact]
    public void EstimateContent_Text()
    {
        var content = new TextContent("hello world"); // 11 UTF-8 bytes -> ceil(11/4) = 3
        Assert.Equal(3, MessageTokenEstimator.EstimateContent(content));
    }

    [Fact]
    public void EstimateContent_CjkTextUsesUtf8Bytes()
    {
        var content = new TextContent("你好"); // 6 UTF-8 bytes -> ceil(6/4) = 2
        Assert.Equal(2, MessageTokenEstimator.EstimateContent(content));
    }

    [Fact]
    public void EstimateContent_ImageUsesFixedCost()
    {
        var content = new DataContent(new byte[] { 1, 2, 3 }, "image/png");
        Assert.Equal(2000, MessageTokenEstimator.EstimateContent(content));
    }

    [Fact]
    public void EstimateModelVisibleBytes_ReplacesLargeMediaPayload()
    {
        var media = new DataContent(new byte[1_000_000], "image/png");
        var message = new ChatMessage(ChatRole.User, [media]);

        var bytes = MessageTokenEstimator.EstimateModelVisibleBytes(message);

        Assert.InRange(bytes, 8_000, 20_000);
    }

    [Fact]
    public void EstimateContent_FunctionCallIncludesNameAndArgs()
    {
        var args = new Dictionary<string, object?> { ["path"] = "README.md" };
        var call = new FunctionCallContent("call-1", "ReadFile", args);
        var tokens = MessageTokenEstimator.EstimateContent(call);
        Assert.True(tokens > MessageTokenEstimator.RoughTokenCount("ReadFile" + JsonSerializer.Serialize(args)));
    }

    [Fact]
    public void EstimateContent_FunctionResultUsesSerializedPayload()
    {
        var fr = new FunctionResultContent("call-1", "file contents go here");
        Assert.True(MessageTokenEstimator.EstimateContent(fr) > 0);
    }

    [Fact]
    public void EstimateContent_FunctionResultStringStillCounted()
    {
        var text = new string('x', 400);
        var fr = new FunctionResultContent("call-1", text);

        Assert.True(MessageTokenEstimator.EstimateContent(fr) > MessageTokenEstimator.RoughTokenCount(text));
    }

    [Fact]
    public void EstimateContent_FunctionResultListWithImage_DoesNotScaleWithImageBytes()
    {
        var imageBytes = new byte[1_000_000];
        var fr = new FunctionResultContent(
            "call-1",
            (IList<AIContent>)
            [
                new TextContent("Image: screenshot.png (1,000,000 bytes, image/png)"),
                new DataContent(imageBytes, "image/png")
            ]);

        var tokens = MessageTokenEstimator.EstimateContent(fr);

        Assert.InRange(tokens, 2_000, 20_000);
        Assert.True(tokens < imageBytes.Length / 16);
    }

    [Fact]
    public void EstimateContent_FunctionResultIncludesDenseStructuredPayload()
    {
        var payload = new Dictionary<string, object?>
        {
            ["items"] = Enumerable.Range(0, 50).Select(i => new Dictionary<string, object?>
            {
                ["id"] = i,
                ["ok"] = true,
                ["path"] = $"src/file-{i}.cs",
            }).ToArray(),
        };
        var result = new FunctionResultContent("call-1", payload);

        Assert.True(
            MessageTokenEstimator.EstimateContent(result)
            > MessageTokenEstimator.RoughTokenCount(JsonSerializer.Serialize(payload)));
    }

    [Fact]
    public void EstimateDelta_ImageToolResult_StaysBoundedByImageTokenCost()
    {
        var imageBytes = new byte[1_000_000];
        var message = new ChatMessage(
            ChatRole.Tool,
            (IList<AIContent>)
            [
                new FunctionResultContent(
                    "call-1",
                    (IList<AIContent>)
                    [
                        new TextContent("Image: screenshot.png (1,000,000 bytes, image/png)"),
                        new DataContent(imageBytes, "image/png")
                    ])
            ]);

        var tokens = MessageTokenEstimator.EstimateDelta([message]);

        Assert.InRange(tokens, 2_000, 20_000);
        Assert.True(tokens < imageBytes.Length / 16);
    }

    [Fact]
    public void EstimateDelta_RangeMatchesMaterializedSlice()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "prefix"),
            new(ChatRole.Assistant, "middle"),
            new(ChatRole.Tool, [new FunctionResultContent("call-1", "result")]),
            new(ChatRole.User, new string('x', 128))
        };

        var expected = MessageTokenEstimator.EstimateDelta(messages.Skip(1).Take(2).ToList());
        var actual = MessageTokenEstimator.EstimateDelta(messages, startIndex: 1, count: 2);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Estimate_AppliesSafetyPad()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "hello world"),
        };
        Assert.True(MessageTokenEstimator.Estimate(messages) > MessageTokenEstimator.EstimateDelta(messages));
    }
}
