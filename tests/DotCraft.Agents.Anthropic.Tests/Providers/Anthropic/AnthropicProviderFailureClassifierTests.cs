using System.Net;
using DotCraft.Agents;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed class AnthropicProviderFailureClassifierTests
{
    [Fact]
    public void Classify_GatewayErrorWrappingBedrockValidation_IsNotReplayed()
    {
        var exception = new HttpRequestException(
            """Status Code: BadGateway {"type":"error","error":{"type":"api_error","message":"BadGatewayError - {\"type\":\"bedrock\",\"code\":\"ValidationException\",\"message\":\"Invalid request parameters.\"}"}}""",
            null,
            HttpStatusCode.BadGateway);

        var failure = AnthropicProviderFailureClassifier.Instance.Classify(exception);

        Assert.Equal(ProviderFailureKind.BadRequest, failure.Kind);
        Assert.False(failure.AllowsTransportReplay);
    }
}
