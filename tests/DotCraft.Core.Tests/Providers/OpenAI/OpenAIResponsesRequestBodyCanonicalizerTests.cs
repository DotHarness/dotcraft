using System.ClientModel.Primitives;
using System.Text;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Tracing;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using OpenAI.Responses;
using Xunit;
using DeferredToolRegistry = DotCraft.Tools.DeferredToolActivationIndex;

#pragma warning disable OPENAI001

namespace DotCraft.Tests.Agents;

public sealed class OpenAIResponsesRequestBodyCanonicalizerTests
{
    [Fact]
    public void SdkGeneratedResponse_HasUniqueKeysAndPreservesPromptCacheFields()
    {
        var registry = new DeferredToolRegistry([AIFunctionFactory.Create(
            (string path) => $"read {path}",
            name: "ReadFile",
            description: "Read a file")]);
        var previous = TracingChatClient.CurrentSessionKey;
        try
        {
            TracingChatClient.CurrentSessionKey = "thread-cache-key";
            using var requestContext = TestModelProviderRegistry.PushRequestContext("thread-cache-key");
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

            var original = ModelReaderWriter.Write(options).ToMemory();
            using var document = JsonDocument.Parse(original);
            var root = document.RootElement;

            Assert.Equal(1, CountTopLevelKeyOccurrences(root, "input"));
            Assert.Equal(1, CountTopLevelKeyOccurrences(root, "tools"));
            Assert.Null(OpenAIResponsesRequestBodyCanonicalizer.Canonicalize(original));
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
    public void Canonicalize_RemovesDuplicateResponsePatchKeys()
    {
        const string original =
            """{"model":"gpt-test","input":[{"id":"old"}],"tools":[{"name":"old"}],"input":[{"id":"current"}],"tools":[{"name":"current"}]}""";

        var rewritten = OpenAIResponsesRequestBodyCanonicalizer.Canonicalize(Encoding.UTF8.GetBytes(original));

        Assert.NotNull(rewritten);
        using var document = JsonDocument.Parse(rewritten!.Value);
        Assert.Equal(1, CountTopLevelKeyOccurrences(document.RootElement, "input"));
        Assert.Equal(1, CountTopLevelKeyOccurrences(document.RootElement, "tools"));
        Assert.Equal("current", document.RootElement.GetProperty("input")[0].GetProperty("id").GetString());
        Assert.Equal("current", document.RootElement.GetProperty("tools")[0].GetProperty("name").GetString());
    }

    [Fact]
    public void Canonicalize_ReturnsNullWhenTopLevelKeysAreAlreadyUnique()
    {
        var rewritten = OpenAIResponsesRequestBodyCanonicalizer.Canonicalize(
            """{"model":"gpt-test","input":[],"tools":[],"store":false,"stream":true}"""u8.ToArray());

        Assert.Null(rewritten);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{not json")]
    public void Canonicalize_ReturnsNullForInvalidOrUnsupportedBodies(string json)
    {
        Assert.Null(OpenAIResponsesRequestBodyCanonicalizer.Canonicalize(Encoding.UTF8.GetBytes(json)));
    }

    private static int CountTopLevelKeyOccurrences(JsonElement root, string key) =>
        root.EnumerateObject().Count(property => string.Equals(property.Name, key, StringComparison.Ordinal));
}
