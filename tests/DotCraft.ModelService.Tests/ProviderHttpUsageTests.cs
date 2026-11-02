using System.Text;
using DotCraft.Agents;
using Xunit;

namespace DotCraft.ModelService.Tests;

public sealed class ProviderHttpUsageTests
{
    [Theory]
    [InlineData(1, false)]
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

    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 17)]
    [InlineData(true, 1)]
    [InlineData(true, 17)]
    public void BillingFieldsRemainSeparateFromTokenUsageAcrossChunks(bool stream, int chunk)
    {
        const string json = """{"service_tier":"fast","output":[{"service_tier":"flex","usage":{"cost":999,"input_tokens":999}}],"usage":{"input_tokens":100,"output_tokens":8,"input_tokens_details":{"cached_tokens":20,"cache_write_tokens":10},"cost":0.01234567}}""";
        var body = stream ? "data: {\"type\":\"response.completed\",\"response\":" + json + "}\n\n" : json;
        var observer = OpenAIHttpUsageObserver.Create(stream);
        var bytes = Encoding.UTF8.GetBytes(body);
        for (var offset = 0; offset < bytes.Length; offset += chunk)
            observer.Append(bytes.AsSpan(offset, Math.Min(chunk, bytes.Length - offset)));
        observer.Complete();

        Assert.Equal(new ProviderHttpUsage(100, 8, 20, 10), observer.Usage);
        Assert.Equal(0.01234567m, observer.ReportedCostUsd);
        Assert.Equal("fast", observer.ServiceTier);
    }

    [Theory]
    [InlineData("{\"usage\":{\"cost\":0}}", "0")]
    [InlineData("{\"openrouter_metadata\":{\"usage\":{\"cost\":0.0375}}}", "0.0375")]
    [InlineData("{\"response\":{\"openrouter_metadata\":{\"usage\":{\"cost\":0.0375}}}}", "0.0375")]
    public void ReportedAmountDoesNotInventTokenUsage(string body, string expected)
    {
        var observer = OpenAIHttpUsageObserver.Create(false);
        observer.Append(Encoding.UTF8.GetBytes(body));
        observer.Complete();

        Assert.Null(observer.Usage);
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), observer.ReportedCostUsd);
    }

    [Fact]
    public void MissingTerminalTierDoesNotReuseTheCreatedResponseTier()
    {
        var observer = OpenAIHttpUsageObserver.Create(true);
        observer.Append("data: {\"type\":\"response.created\",\"response\":{\"service_tier\":\"default\"}}\n\ndata: {\"type\":\"response.completed\",\"response\":{\"usage\":{\"input_tokens\":10}}}\n\n"u8);
        observer.Complete();

        Assert.Null(observer.ServiceTier);
        Assert.Equal(10, observer.Usage!.InputTokens);
    }

    [Theory]
    [InlineData(false, "{\"usage\":{\"total_tokens\":10}}", false)]
    [InlineData(true, "{\"usage\":{\"total_tokens\":10}}", false)]
    [InlineData(false, "{\"usage\":{\"input_tokens\":0,\"output_tokens\":0}}", true)]
    [InlineData(true, "{\"usage\":{\"input_tokens\":0,\"output_tokens\":0}}", true)]
    public void OnlyRecognizedTokenFieldsCreateUsage(bool anthropic, string body, bool hasUsage)
    {
        var observer = anthropic ? AnthropicHttpUsageObserver.Create(false) : OpenAIHttpUsageObserver.Create(false);
        observer.Append(Encoding.UTF8.GetBytes(body));
        observer.Complete();

        Assert.Equal(hasUsage, observer.Usage is not null);
    }

    [Fact]
    public void TierWithoutUsageDoesNotInventAFreeCall()
    {
        var observer = OpenAIHttpUsageObserver.Create(false);
        observer.Append("{\"service_tier\":\"priority\"}"u8);
        observer.Complete();

        Assert.Null(observer.Usage);
        Assert.Null(observer.ReportedCostUsd);
        Assert.Equal("priority", observer.ServiceTier);
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
