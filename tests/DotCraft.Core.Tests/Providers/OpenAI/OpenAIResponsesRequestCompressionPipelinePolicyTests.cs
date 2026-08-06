using System.ClientModel;
using System.ClientModel.Primitives;
using System.Security.Cryptography;
using System.Text;
using DotCraft.Agents;
using Xunit;
using ZstdSharp;

namespace DotCraft.Tests.Agents;

public sealed class OpenAIResponsesRequestCompressionPipelinePolicyTests
{
    private static readonly byte[] JsonBody = Encoding.UTF8.GetBytes(
        "{\"model\":\"gpt-5.6\",\"input\":[{\"type\":\"message\",\"role\":\"user\",\"content\":\"hello\"}]}");

    [Fact]
    public void CompressesSamplingResponsesWithZstdLevelThree()
    {
        using var message = CreateMessage(
            "https://chatgpt.com/backend-api/codex/responses",
            JsonBody);

        var compressed = OpenAIResponsesRequestCompressionPipelinePolicy.Compress(message);

        Assert.True(compressed);
        Assert.True(message.Request.Headers.TryGetValue(
            OpenAIResponsesRequestCompressionPipelinePolicy.ContentEncodingHeader,
            out var encoding));
        Assert.Equal(OpenAIResponsesRequestCompressionPipelinePolicy.ZstdContentEncoding, encoding);
        Assert.Equal(JsonBody, Decompress(ReadContent(message)));
        Assert.True(OpenAIResponsesRequestCompressionPipelinePolicy.TryGetOriginalBody(
            message,
            out var original,
            out var sha256));
        Assert.Equal(JsonBody, original!.ToArray());
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(JsonBody)).ToLowerInvariant(),
            sha256);
    }

    [Fact]
    public void RejectsAnAlreadyCompressedMessage()
    {
        using var message = CreateMessage(
            "https://chatgpt.com/backend-api/codex/responses",
            JsonBody);
        Assert.True(OpenAIResponsesRequestCompressionPipelinePolicy.Compress(message));
        var exception = Assert.Throws<InvalidOperationException>(
            () => OpenAIResponsesRequestCompressionPipelinePolicy.Compress(message));

        Assert.Contains("must be unencoded", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsExistingContentEncoding()
    {
        using var message = CreateMessage(
            "https://chatgpt.com/backend-api/codex/responses",
            JsonBody);
        message.Request.Headers.Set(
            OpenAIResponsesRequestCompressionPipelinePolicy.ContentEncodingHeader,
            "gzip");

        var exception = Assert.Throws<InvalidOperationException>(
            () => OpenAIResponsesRequestCompressionPipelinePolicy.Compress(message));

        Assert.Contains("must be unencoded", exception.Message, StringComparison.Ordinal);
        Assert.Equal(JsonBody, ReadContent(message));
        Assert.True(message.Request.Headers.TryGetValue("Content-Encoding", out var encoding));
        Assert.Equal("gzip", encoding);
    }

    [Fact]
    public async Task AsyncCompressionMatchesSyncContract()
    {
        using var message = CreateMessage(
            "https://chatgpt.com/backend-api/codex/responses",
            JsonBody);

        var compressed = await OpenAIResponsesRequestCompressionPipelinePolicy.CompressAsync(message);

        Assert.True(compressed);
        Assert.Equal(JsonBody, Decompress(ReadContent(message)));
    }

    [Theory]
    [InlineData("https://chatgpt.com/backend-api/codex/models")]
    [InlineData("https://chatgpt.com/backend-api/codex/responses/other")]
    [InlineData("https://chatgpt.com/backend-api/codex/responses/compact")]
    public void IgnoresNonSamplingPaths(string uri)
    {
        using var message = CreateMessage(uri, JsonBody);

        Assert.False(OpenAIResponsesRequestCompressionPipelinePolicy.Compress(message));
        Assert.Equal(JsonBody, ReadContent(message));
        Assert.False(message.Request.Headers.TryGetValue("Content-Encoding", out _));
    }

    [Fact]
    public void RejectsMissingBody()
    {
        using var message = ClientPipeline.Create(new ClientPipelineOptions()).CreateMessage();
        message.Request.Uri = new Uri("https://chatgpt.com/backend-api/codex/responses");

        var exception = Assert.Throws<InvalidOperationException>(
            () => OpenAIResponsesRequestCompressionPipelinePolicy.Compress(message));

        Assert.Contains("must contain a JSON body", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsNonObjectJsonBody()
    {
        using var message = CreateMessage(
            "https://chatgpt.com/backend-api/codex/responses",
            "[]"u8.ToArray());

        var exception = Assert.Throws<InvalidDataException>(
            () => OpenAIResponsesRequestCompressionPipelinePolicy.Compress(message));

        Assert.Contains("JSON object", exception.Message, StringComparison.Ordinal);
    }

    private static PipelineMessage CreateMessage(string uri, byte[] content)
    {
        var message = ClientPipeline.Create(new ClientPipelineOptions()).CreateMessage();
        message.Request.Uri = new Uri(uri);
        message.Request.Content = BinaryContent.Create(BinaryData.FromBytes(content));
        return message;
    }

    private static byte[] ReadContent(PipelineMessage message)
    {
        using var stream = new MemoryStream();
        message.Request.Content!.WriteTo(stream, message.CancellationToken);
        return stream.ToArray();
    }

    private static byte[] Decompress(byte[] compressed)
    {
        using var decompressor = new Decompressor();
        return decompressor.Unwrap(compressed).ToArray();
    }
}
