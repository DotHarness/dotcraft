using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Context.Compaction;
using Microsoft.Extensions.AI;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Context.Compaction;

public sealed class MessageTokenEstimatorTests
{
    [Fact]
    public void EstimateContent_TextUsesUtf8BytesAndImagesAFixedCost()
    {
        Assert.Equal(3, MessageTokenEstimator.EstimateContent(new TextContent("hello world")));
        Assert.Equal(2, MessageTokenEstimator.EstimateContent(new TextContent("你好")));
        Assert.Equal(2000, MessageTokenEstimator.EstimateContent(new DataContent(new byte[] { 1, 2, 3 }, "image/png")));
    }

    [Fact]
    public void EstimateContent_FunctionResultImageShapes_HaveEqualBoundedCost()
    {
        var contents = new List<AIContent>
        {
            new TextContent("Screenshots captured"),
            new DataContent(new byte[750_000], "image/png"),
            new DataContent(new byte[1_000_000], "image/jpeg")
        };
        var json = JsonSerializer.SerializeToElement(contents, SessionPersistenceJsonOptions.Default);
        var estimates = new object[] { contents, contents.ToArray(), json }
            .Select(result => MessageTokenEstimator.EstimateContent(
                new FunctionResultContent("call-images", result)))
            .ToArray();

        Assert.All(estimates, estimate => Assert.InRange(estimate, 4_000, 20_000));
        Assert.All(estimates, estimate => Assert.Equal(estimates[0], estimate));
    }

    [Fact]
    public void EstimateContent_JsonFunctionResult_IgnoresImageBase64ButCountsOtherData()
    {
        static int Estimate(int bytes, string mediaType)
        {
            var contents = new List<AIContent> { new DataContent(new byte[bytes], mediaType) };
            var json = JsonSerializer.SerializeToElement(contents, SessionPersistenceJsonOptions.Default);
            return MessageTokenEstimator.EstimateContent(new FunctionResultContent("call-data", json));
        }

        Assert.Equal(Estimate(1_000, "image/png"), Estimate(1_000_000, "image/png"));
        Assert.True(Estimate(100_000, "application/octet-stream") > Estimate(1_000, "application/octet-stream"));
    }

    [Fact]
    public void ComputePrefixFingerprint_JsonFunctionResultMatchesStructuredResult()
    {
        var contents = new List<AIContent>
        {
            new TextContent("Screenshot captured"),
            new DataContent(new byte[] { 1, 2, 3 }, "image/png")
        };
        var structured = new ChatMessage(
            ChatRole.Tool,
            [new FunctionResultContent("call-image", contents)]);
        var json = JsonSerializer.SerializeToElement(contents, SessionPersistenceJsonOptions.Default);
        var restored = new ChatMessage(
            ChatRole.Tool,
            [new FunctionResultContent("call-image", json)]);

        Assert.Equal(
            MessageTokenEstimator.ComputePrefixFingerprint([structured], 1),
            MessageTokenEstimator.ComputePrefixFingerprint([restored], 1));
    }

    [Fact]
    public void EstimateContent_ToolContentCountsNamesArgumentsAndPayloads()
    {
        var args = new Dictionary<string, object?> { ["path"] = "README.md" };
        Assert.True(
            MessageTokenEstimator.EstimateContent(new FunctionCallContent("call-1", "ReadFile", args))
            > MessageTokenEstimator.RoughTokenCount("ReadFile" + JsonSerializer.Serialize(args)));

        var text = new string('x', 400);
        Assert.True(
            MessageTokenEstimator.EstimateContent(new FunctionResultContent("call-1", text))
            > MessageTokenEstimator.RoughTokenCount(text));

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
    public void EstimateDelta_ImagePayloadsStayBoundedByImageCost()
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
        Assert.InRange(
            MessageTokenEstimator.EstimateModelVisibleBytes(
                new ChatMessage(ChatRole.User, [new DataContent(imageBytes, "image/png")])),
            8_000,
            20_000);
    }

    [Fact]
    public void EstimateDelta_RangeMatchesMaterializedSliceAndEstimateAddsASafetyPad()
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
        Assert.True(MessageTokenEstimator.Estimate(messages) > MessageTokenEstimator.EstimateDelta(messages));
    }
}
