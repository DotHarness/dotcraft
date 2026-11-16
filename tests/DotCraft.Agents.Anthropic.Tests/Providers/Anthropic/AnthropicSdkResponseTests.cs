using System.Net;
using System.Text;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed class AnthropicSdkResponseTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Thinking_usage_preserves_inclusive_output_and_reasoning_counts(bool streaming)
    {
        using var handler = new ResponseHandler(streaming);
        using var http = new HttpClient(handler);
        using var client = Client(http);

        var response = await Send(client, streaming);

        Assert.Equal(12, response.Usage?.InputTokenCount);
        Assert.Equal(8, response.Usage?.OutputTokenCount);
        Assert.Equal(5, response.Usage?.ReasoningTokenCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Json_schema_without_required_is_sent_as_structured_output(bool streaming)
    {
        var schema = JsonSerializer.Deserialize<JsonElement>(
            """{"type":"object","properties":{"answer":{"type":"string"}},"additionalProperties":false}""");
        using var handler = new ResponseHandler(streaming);
        using var http = new HttpClient(handler);
        using var client = Client(http);

        await Send(client, streaming, new ChatOptions { ResponseFormat = new ChatResponseFormatJson(schema) });

        var format = handler.Request.GetProperty("output_config").GetProperty("format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.True(JsonElement.DeepEquals(schema, format.GetProperty("schema")));
    }

    private static IChatClient Client(HttpClient http)
    {
        var runtime = new EffectiveModelRuntime("test", "test-model", ModelProviderProtocols.Anthropic,
            "Test", "test-key", "https://example.invalid", 10, 1024,
            ModelProviderCapabilities.ForProtocol(ModelProviderProtocols.Anthropic));
        return new AnthropicClientProvider(new Transport(http)).CreateChatClient(runtime);
    }

    private sealed class Transport(HttpClient client) : IProviderHttpTransport
    {
        public HttpClient CreateClient(string providerId, Uri providerEndpoint) => client;
    }

    private static async Task<ChatResponse> Send(IChatClient client, bool streaming, ChatOptions? options = null)
    {
        ChatMessage[] messages = [new(ChatRole.User, "Probe")];
        if (!streaming)
            return await client.GetResponseAsync(messages, options);
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync(messages, options))
            updates.Add(update);
        return updates.ToChatResponse();
    }

    private sealed class ResponseHandler(bool streaming) : HttpMessageHandler
    {
        public JsonElement Request { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(cancellationToken));
            const string message = """{"id":"msg_1","type":"message","role":"assistant","model":"test-model","content":[],"stop_reason":"end_turn","usage":{"input_tokens":12,"output_tokens":8,"output_tokens_details":{"thinking_tokens":5}}}""";
            var body = streaming
                ? """
                    event: message_start
                    data: {"type":"message_start","message":{"id":"msg_1","type":"message","role":"assistant","model":"test-model","content":[],"stop_reason":null,"usage":{"input_tokens":12,"output_tokens":0}}}

                    event: message_delta
                    data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"input_tokens":12,"output_tokens":8,"output_tokens_details":{"thinking_tokens":5}}}

                    event: message_stop
                    data: {"type":"message_stop"}
                    """ + "\n\n"
                : message;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, streaming ? "text/event-stream" : "application/json")
            };
        }
    }
}
