using DotCraft.Agents;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class StreamRetryPresentationTests
{
    [Fact]
    public void NamesTheCauseWhenTheProviderReportedCapacity()
    {
        var presentation = StreamRetryPresentation.For(Notification(
            new ProviderFailure(ProviderFailureKind.ServerOverloaded)));

        Assert.Equal(StreamRetryPresentation.ServerBusyKey, presentation.MessageKey);
        Assert.Equal("Server is busy, reconnecting... 2/5", presentation.FallbackText);
    }

    [Fact]
    public void NamesTheCauseWhenTheProviderThrottled()
    {
        var presentation = StreamRetryPresentation.For(Notification(
            new ProviderFailure(ProviderFailureKind.Other, HttpStatus: 429)));

        Assert.Equal(StreamRetryPresentation.ServerBusyKey, presentation.MessageKey);
    }

    [Fact]
    public void NamesOnlyTheRemedyWhenTheCauseIsUnknown()
    {
        var presentation = StreamRetryPresentation.For(Notification(
            new ProviderFailure(ProviderFailureKind.HttpConnectionFailed)));

        Assert.Equal(StreamRetryPresentation.ReconnectingKey, presentation.MessageKey);
        Assert.Equal("Reconnecting... 2/5", presentation.FallbackText);
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

    [Fact]
    public void OmitsClassificationWhenNoneWasProduced()
    {
        var presentation = StreamRetryPresentation.For(Notification(classification: null));

        Assert.Equal(StreamRetryPresentation.ReconnectingKey, presentation.MessageKey);
        Assert.False(presentation.Params.ContainsKey("providerError"));
    }

    private static ModelStreamRetryNotification Notification(ProviderFailure? classification) =>
        new(2, 5, new IOException("stream closed"), classification);
}
