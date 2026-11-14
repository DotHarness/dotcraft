using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Context.Compaction;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Core.Tests.Agents;

public sealed class OpenAIResponsesNativeTokenEstimatorTests
{
    [Fact]
    public void TransportEscapingAndMetadataDoNotInflateContentBudget()
    {
        var text = string.Concat(Enumerable.Repeat("\"\\\n汉字", 100));
        object[] items =
        [
            new { type = "message", content = new[] { new { type = "input_text", text } } },
            new { type = "custom_tool_call", name = "echo", input = text },
            new { type = "custom_tool_call_output", call_id = "call", output = text }
        ];
        var expectedBytes = Encoding.UTF8.GetByteCount(text) * 3L
            + Encoding.UTF8.GetByteCount("echofunctionscall");
        var expectedTokens = (expectedBytes + 3) / 4;
        var escaped = JsonSerializer.SerializeToElement(items);
        var unescaped = JsonSerializer.SerializeToElement(items, new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true
        });
        var decorated = JsonSerializer.SerializeToNode(items)!.AsArray();
        foreach (var item in decorated)
        {
            item!["id"] = new string('i', 10_000);
            item["internal_chat_message_metadata_passthrough"] = new string('m', 10_000);
        }

        Assert.Equal(expectedTokens, Estimate(escaped));
        Assert.Equal(expectedTokens, Estimate(unescaped));
        Assert.Equal(expectedTokens, Estimate(JsonSerializer.SerializeToElement(decorated)));
    }

    [Fact]
    public void NormalizesProtectedContentAndCountsOnlyPendingTailAsIncrement()
    {
        var protectedContent = new string('A', 4_000);
        var items = JsonSerializer.SerializeToElement(new object[]
        {
            new { type = "reasoning", encrypted_content = protectedContent },
            new { type = "reasoning", encrypted_content = new string('A', 100) },
            new { type = "compaction", encrypted_content = protectedContent },
            new
            {
                type = "function_call_output", call_id = "call",
                output = new[] { new { type = "encrypted_content", encrypted_content = protectedContent } }
            }
        }).EnumerateArray().ToArray();
        var expectedBytes = 2_350 + 1_334 + 2_250 + Encoding.UTF8.GetByteCount("call");
        var baseline = OpenAIResponsesNativeTokenEstimator.Estimate(items, [], null);
        Assert.Equal((expectedBytes + 3L) / 4, baseline);

        var options = new ChatOptions
        {
            Instructions = "Preserve the project constraints.",
            Tools = [AIFunctionFactory.Create((string value) => value, name: "echo")]
        };
        var withControls = OpenAIResponsesNativeTokenEstimator.Estimate(items, [], options);
        ChatMessage[] tail = [new(ChatRole.User, "Continue with the pending input.")];
        var withTail = OpenAIResponsesNativeTokenEstimator.Estimate(items, tail, options);

        Assert.True(withControls > baseline);
        Assert.Equal(MessageTokenEstimator.EstimateDelta(tail), withTail - withControls);
    }

    [Theory]
    [InlineData("input_image", "image_url", "image/png", 2_000)]
    [InlineData("input_audio", "audio_url", "audio/wav", 1_000)]
    public void MediaBudgetCountsEachPartWithoutItsPayload(string type, string property, string mimeType, int tokenCost)
    {
        JsonElement Message(int payloadLength, int partCount) =>
            JsonSerializer.SerializeToElement(new[]
            {
                new
                {
                    type = "message",
                    content = Enumerable.Range(0, partCount).Select(_ => new Dictionary<string, string>
                    {
                        ["type"] = type,
                        [property] = $"data:{mimeType};base64," + new string('A', payloadLength)
                    }).ToArray()
                }
            });

        Assert.Equal(tokenCost, Estimate(Message(4, 1)));
        Assert.Equal(tokenCost, Estimate(Message(100_000, 1)));
        Assert.Equal(tokenCost * 2, Estimate(Message(100_000, 2)));
    }

    private static long Estimate(JsonElement items) =>
        OpenAIResponsesNativeTokenEstimator.Estimate(items.EnumerateArray().ToArray(), [], null);
}
