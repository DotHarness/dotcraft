using DotCraft.Agents;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class StreamRetryPresentationTests
{
    public static TheoryData<ProviderFailure?, string> MessageCases => new()
    {
        { new ProviderFailure(ProviderFailureKind.ServerOverloaded), StreamRetryPresentation.ServerBusyKey },
        { new ProviderFailure(ProviderFailureKind.Other, HttpStatus: 429), StreamRetryPresentation.ServerBusyKey },
        { new ProviderFailure(ProviderFailureKind.HttpConnectionFailed), StreamRetryPresentation.ReconnectingKey },
        { null, StreamRetryPresentation.ReconnectingKey }
    };

    [Theory]
    [MemberData(nameof(MessageCases))]
    public void NamesTheCauseOnlyWhenTheProviderReportedCapacityOrThrottling(ProviderFailure? classification, string expectedKey)
    {
        var presentation = StreamRetryPresentation.For(Notification(classification));

        Assert.Equal(expectedKey, presentation.MessageKey);
        Assert.Equal(classification != null, presentation.Params.ContainsKey("providerError"));
    }

    [Fact]
    public void CarriesAttemptProgressAndClassificationAsParams()
    {
        var presentation = StreamRetryPresentation.For(Notification(
            new ProviderFailure(ProviderFailureKind.RateLimitExceeded, HttpStatus: 429)));

        Assert.Equal(2, presentation.Params["attempt"]);
        Assert.Equal(5, presentation.Params["max"]);
        Assert.Equal("rateLimitExceeded", presentation.Params["providerError"]);
        Assert.Equal(429, presentation.Params["httpStatus"]);
        Assert.Equal("stream closed", presentation.Params["detail"]);
    }

    private static ModelStreamRetryNotification Notification(ProviderFailure? classification) =>
        new(2, 5, new IOException("stream closed"), classification);
}
