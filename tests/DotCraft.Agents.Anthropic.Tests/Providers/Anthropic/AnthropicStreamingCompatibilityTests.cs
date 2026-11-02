using System.Net;
using System.Text;
using DotCraft.Agents;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed class AnthropicStreamingCompatibilityTests
{
    [Fact]
    public async Task UnknownEvents_PreserveTextAndUsage()
    {
        using var http = new HttpClient(new StreamHandler());
        var runtime = new EffectiveModelRuntime("test", "test-model", ModelProviderProtocols.Anthropic,
            "Test", "configured-key", "https://example.invalid", 10, 1024,
            ModelProviderCapabilities.ForProtocol(ModelProviderProtocols.Anthropic));
        using var client = new AnthropicClientProvider(new Transport(http)).CreateChatClient(runtime);
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "Probe")]))
            updates.Add(update);

        Assert.Equal("answer", string.Concat(updates.Select(u => u.Text)));
        var response = updates.ToChatResponse();
        Assert.Equal(12, response.Usage?.InputTokenCount);
        Assert.Equal(8, response.Usage?.OutputTokenCount);
    }

    private sealed class Transport(HttpClient client) : IProviderHttpTransport
    {
        public HttpClient CreateClient(string providerId, Uri providerEndpoint) => client;
    }

    private sealed class StreamHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Stream, Encoding.UTF8, "text/event-stream")
            });
        }
    }

    private const string Stream = """
        event: message_start
        data: {"type":"message_start","message":{"id":"msg_1","type":"message","role":"assistant","model":"test-model","content":[],"stop_reason":null,"usage":{"input_tokens":12,"output_tokens":0}}}

        event: future_event
        data: {"type":"future_event","value":"must-not-be-output"}

        event: content_block_start
        data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"answer"}}

        event: content_block_stop
        data: {"type":"content_block_stop","index":0}

        event: message_delta
        data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":8}}

        event: message_stop
        data: {"type":"message_stop"}


        """;
}
