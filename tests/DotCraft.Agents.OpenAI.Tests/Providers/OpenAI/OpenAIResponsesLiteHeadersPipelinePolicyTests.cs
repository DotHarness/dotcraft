using System.ClientModel.Primitives;
using DotCraft.Agents;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed class OpenAIResponsesLiteHeadersPipelinePolicyTests
{
    [Theory]
    [InlineData("https://chatgpt.com/backend-api/codex/responses")]
    [InlineData("https://chatgpt.com/backend-api/codex/responses/compact")]
    [InlineData("https://chatgpt.com/backend-api/codex/responses/?stream=true")]
    public void AppliesLiteHeaderToSupportedResponsesPaths(string uri)
    {
        using var message = CreateMessage(uri);

        var applied = OpenAIResponsesLiteHeadersPipelinePolicy.Apply(message);

        Assert.True(applied);
        Assert.True(message.Request.Headers.TryGetValue(
            OpenAIResponsesLiteHeadersPipelinePolicy.ResponsesLiteHeader,
            out var value));
        Assert.Equal("true", value);
        if (new Uri(uri).AbsolutePath.TrimEnd('/').EndsWith("/responses", StringComparison.Ordinal))
        {
            Assert.True(message.Request.Headers.TryGetValue(
                OpenAIResponsesLiteHeadersPipelinePolicy.AcceptHeader,
                out var accept));
            Assert.Equal(OpenAIResponsesLiteHeadersPipelinePolicy.EventStreamContentType, accept);
        }
        else
        {
            Assert.False(message.Request.Headers.TryGetValue(
                OpenAIResponsesLiteHeadersPipelinePolicy.AcceptHeader,
                out _));
        }
    }

    [Fact]
    public void CompactPreservesExistingAcceptHeader()
    {
        using var message = CreateMessage(
            "https://chatgpt.com/backend-api/codex/responses/compact");
        message.Request.Headers.Set("Accept", "application/json");

        Assert.True(OpenAIResponsesLiteHeadersPipelinePolicy.Apply(message));

        Assert.True(message.Request.Headers.TryGetValue("Accept", out var accept));
        Assert.Equal("application/json", accept);
    }

    [Theory]
    [InlineData("https://chatgpt.com/backend-api/codex/models")]
    [InlineData("https://chatgpt.com/backend-api/codex/responses/other")]
    [InlineData("https://chatgpt.com/backend-api/codex/responses-compact")]
    public void RejectsOtherPaths(string uri)
    {
        using var message = CreateMessage(uri);

        var exception = Assert.Throws<InvalidOperationException>(
            () => OpenAIResponsesLiteHeadersPipelinePolicy.Apply(message));

        Assert.Contains("only supports", exception.Message, StringComparison.Ordinal);
        Assert.False(message.Request.Headers.TryGetValue(
            OpenAIResponsesLiteHeadersPipelinePolicy.ResponsesLiteHeader,
            out _));
    }

    private static PipelineMessage CreateMessage(string uri)
    {
        var message = ClientPipeline.Create(new ClientPipelineOptions()).CreateMessage();
        message.Request.Uri = new Uri(uri);
        return message;
    }
}
