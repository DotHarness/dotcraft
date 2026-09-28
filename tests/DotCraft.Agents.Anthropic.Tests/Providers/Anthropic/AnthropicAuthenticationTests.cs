using System.Net;
using System.Net.Http.Headers;
using System.Text;
using DotCraft.Agents;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Agents;

[CollectionDefinition(AnthropicAuthenticationCollection.Name, DisableParallelization = true)]
public sealed class AnthropicAuthenticationCollection
{
    public const string Name = "AnthropicAuthenticationEnvironment";
}

[Collection(AnthropicAuthenticationCollection.Name)]
public sealed class AnthropicAuthenticationTests
{
    [Fact]
    public async Task ApiKeyRequestsDoNotIncludeAmbientAuthToken()
    {
        var originalToken = Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN");
        try
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN", "unrelated-token");
            using var handler = new CaptureHandler();
            using var httpClient = new HttpClient(handler);
            var provider = new AnthropicClientProvider();
            var runtime = new EffectiveModelRuntime(
                ProviderId: "test-provider",
                Model: "test-model",
                Protocol: ModelProviderProtocols.Anthropic,
                DisplayName: "Test provider",
                ApiKey: "configured-key",
                EndPoint: "https://example.invalid",
                NetworkTimeoutSeconds: 10,
                MaxOutputTokens: 1024,
                Capabilities: ModelProviderCapabilities.ForProtocol(ModelProviderProtocols.Anthropic));
            using var client = provider.GetAnthropicClient(runtime)
                .WithOptions(options => options with { HttpClient = httpClient })
                .Beta.AsIChatClient(runtime.Model, 1024);

            await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Probe.")]);

            Assert.NotNull(handler.Headers);
            Assert.Equal("configured-key", Assert.Single(handler.Headers.GetValues("x-api-key")));
            Assert.False(handler.Headers.Contains("Authorization"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN", originalToken);
        }
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public HttpRequestHeaders? Headers { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Headers = request.Headers;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"id":"msg_probe","type":"message","role":"assistant","model":"test-model","content":[],"stop_reason":"end_turn","stop_sequence":null,"usage":{"input_tokens":1,"output_tokens":1}}
                    """, Encoding.UTF8, "application/json")
            });
        }
    }
}
