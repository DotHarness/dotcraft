using System.Text;
using DotCraft.Agents;
using Xunit;

namespace DotCraft.ModelService.Tests;

public sealed class ProviderHttpUsageTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(17, false)]
    [InlineData(4096, false)]
    [InlineData(1, true)]
    public void AnthropicStreamCombinesMessageAndDeltaUsage(int chunk, bool auto)
    {
        var observer = auto ? AnthropicHttpUsageObserver.CreateAuto() : AnthropicHttpUsageObserver.Create(true);
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

    [Theory]
    [InlineData(1)]
    [InlineData(17)]
    public void AutoReaderFindsResponsesUsageWithoutContentType(int chunk)
    {
        var observer = OpenAIHttpUsageObserver.CreateAuto();
        var bytes = Encoding.UTF8.GetBytes("\r\nevent: response.completed\ndata: {\"type\":\"response.completed\",\"response\":{\"usage\":{\"input_tokens\":2400,\"output_tokens\":120,\"input_tokens_details\":{\"cached_tokens\":1800},\"output_tokens_details\":{\"reasoning_tokens\":64}}}}\n\n");
        for (var offset = 0; offset < bytes.Length; offset += chunk)
            observer.Append(bytes.AsSpan(offset, Math.Min(chunk, bytes.Length - offset)));
        observer.Complete();
        Assert.Equal(new ProviderHttpUsage(2400, 120, 1800, ReasoningTokens: 64), observer.Usage);
    }

    [Fact]
    public void AutoReaderFindsJsonUsageAfterLeadingWhitespace()
    {
        var observer = OpenAIHttpUsageObserver.CreateAuto();
        var bytes = Encoding.UTF8.GetBytes(" \t\r\n{\"usage\":{\"input_tokens\":30,\"output_tokens\":8}}");
        foreach (var value in bytes)
            observer.Append([value]);
        observer.Complete();
        Assert.Equal(new ProviderHttpUsage(30, 8), observer.Usage);
    }
}
