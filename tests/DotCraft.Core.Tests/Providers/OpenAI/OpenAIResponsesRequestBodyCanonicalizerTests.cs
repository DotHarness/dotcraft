using System.ClientModel.Primitives;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Tracing;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using OpenAI.Responses;

#pragma warning disable OPENAI001

namespace DotCraft.Tests.Agents;

public sealed class OpenAIResponsesRequestBodyCanonicalizerTests
{
    [Fact]
    public void Canonicalize_RemovesDuplicateResponsePatchKeysAndPreservesPromptCacheFields()
    {
        var registry = new DeferredToolRegistry([AIFunctionFactory.Create(
            (string path) => $"read {path}",
            name: "ReadFile",
            description: "Read a file")]);
        var previous = TracingChatClient.CurrentSessionKey;
        try
        {
            TracingChatClient.CurrentSessionKey = "thread-cache-key";
            var options = ResponsesToolSearchMapper.CreateResponseOptions(
                "gpt-test",
                [new ChatMessage(ChatRole.User, "think carefully")],
                new ChatOptions
                {
                    Tools = [new NativeToolSearchTool(registry)],
                    Reasoning = new ReasoningOptions
                    {
                        Effort = ReasoningEffort.High,
                        Output = ReasoningOutput.Summary
                    }
                });

            var original = SerializeOptions(options);

            Assert.Equal(2, CountTopLevelKeyOccurrences(original, "input"));
            Assert.Equal(2, CountTopLevelKeyOccurrences(original, "tools"));

            var rewritten = OpenAIResponsesRequestBodyCanonicalizer.Canonicalize(original);

            Assert.NotNull(rewritten);
            Assert.Equal(1, CountTopLevelKeyOccurrences(rewritten!, "input"));
            Assert.Equal(1, CountTopLevelKeyOccurrences(rewritten!, "tools"));

            using var document = JsonDocument.Parse(rewritten!);
            var root = document.RootElement;
            Assert.False(root.GetProperty("store").GetBoolean());
            Assert.True(root.GetProperty("stream").GetBoolean());
            Assert.Equal("thread-cache-key", root.GetProperty("prompt_cache_key").GetString());
            Assert.Equal("high", root.GetProperty("reasoning").GetProperty("effort").GetString());
            Assert.Contains(
                root.GetProperty("include").EnumerateArray(),
                item => item.GetString() == "reasoning.encrypted_content");

            var input = Assert.Single(root.GetProperty("input").EnumerateArray());
            Assert.Equal("message", input.GetProperty("type").GetString());
            Assert.Equal("user", input.GetProperty("role").GetString());
            Assert.Equal("think carefully", input.GetProperty("content")[0].GetProperty("text").GetString());

            var tool = Assert.Single(root.GetProperty("tools").EnumerateArray());
            Assert.Equal("tool_search", tool.GetProperty("type").GetString());
        }
        finally
        {
            TracingChatClient.CurrentSessionKey = previous;
        }
    }

    [Fact]
    public void Canonicalize_ReturnsNullWhenTopLevelKeysAreAlreadyUnique()
    {
        var rewritten = OpenAIResponsesRequestBodyCanonicalizer.Canonicalize(
            """{"model":"gpt-test","input":[],"tools":[],"store":false,"stream":true}""");

        Assert.Null(rewritten);
    }

    [Fact]
    public void RemoveTopLevelFields_RemovesUnsupportedFieldAndPreservesOtherFields()
    {
        var rewritten = OpenAIResponsesRequestBodyCanonicalizer.RemoveTopLevelFields(
            """{"model":"gpt-test","max_output_tokens":12000,"input":[],"stream":true}""",
            "max_output_tokens");

        Assert.NotNull(rewritten);
        using var document = JsonDocument.Parse(rewritten!);
        Assert.False(document.RootElement.TryGetProperty("max_output_tokens", out _));
        Assert.Equal("gpt-test", document.RootElement.GetProperty("model").GetString());
        Assert.True(document.RootElement.GetProperty("stream").GetBoolean());
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{not json")]
    public void Canonicalize_ReturnsNullForInvalidOrUnsupportedBodies(string json)
    {
        Assert.Null(OpenAIResponsesRequestBodyCanonicalizer.Canonicalize(json));
    }

    private static string SerializeOptions(CreateResponseOptions options) =>
        ModelReaderWriter.Write(options).ToString();

    private static int CountTopLevelKeyOccurrences(string json, string key)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject()
            .Count(property => string.Equals(property.Name, key, StringComparison.Ordinal));
    }
}
