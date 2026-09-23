using System.Text;
using DotCraft.Agents;
using Xunit;

namespace DotCraft.ModelService.Tests;

public sealed class ProviderHttpUsageTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(17)]
    [InlineData(4096)]
    public void AnthropicStreamCombinesMessageAndDeltaUsage(int chunk)
    {
        var observer = AnthropicHttpUsageObserver.Create(true);
        var bytes = Encoding.UTF8.GetBytes("event: message_start\ndata: {\"message\":{\"usage\":{\"input_tokens\":3,\"cache_read_input_tokens\":11,\"cache_creation_input_tokens\":5,\"output_tokens\":1}}}\n\ndata: {\"usage\":{\"output_tokens\":27}}\n\n");
        for (var offset = 0; offset < bytes.Length; offset += chunk)
            observer.Append(bytes.AsSpan(offset, Math.Min(chunk, bytes.Length - offset)));
        observer.Complete();
        Assert.Equal(new ProviderHttpUsage(19, 27, 11, 5), observer.Usage);
    }

    [Fact]
    public void JsonReaderIgnoresToolContentAndReadsUsageAcrossByteBoundaries()
    {
        var observer = OpenAIHttpUsageObserver.Create(false);
        var bytes = Encoding.UTF8.GetBytes("{\"output\":[{\"usage\":{\"input_tokens\":999},\"text\":\"中文\"}],\"usage\":{\"input_tokens\":30,\"output_tokens\":8,\"output_tokens_details\":{\"reasoning_tokens\":4}}}");
        foreach (var value in bytes)
            observer.Append([value]);
        observer.Complete();
        Assert.Equal(new ProviderHttpUsage(30, 8, ReasoningTokens: 4), observer.Usage);
    }
}
