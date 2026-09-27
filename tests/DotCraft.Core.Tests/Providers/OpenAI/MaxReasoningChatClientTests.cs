using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;
using OpenAI;
using Xunit;

#pragma warning disable OPENAI001

namespace DotCraft.Core.Tests.Agents;

public sealed class MaxReasoningChatClientTests
{
    [Theory]
    [InlineData(false, ModelReasoningEffort.Max, null, "max")]
    [InlineData(true, ModelReasoningEffort.Max, null, "max")]
    [InlineData(false, ModelReasoningEffort.Ultra, null, "max")]
    [InlineData(true, ModelReasoningEffort.Ultra, null, "max")]
    [InlineData(false, ModelReasoningEffort.Max, ReasoningEffort.High, "high")]
    [InlineData(true, ModelReasoningEffort.ExtraHigh, null, "xhigh")]
    public async Task ChatCompletions_SendsEffectiveEffort(bool streaming, ModelReasoningEffort effort,
        ReasoningEffort? explicitEffort, string expected)
    {
        using var handler = new CaptureHandler(streaming);
        using var http = new HttpClient(handler);
        var sdk = new OpenAIClient(new ApiKeyCredential("test-key"), new OpenAIClientOptions
        {
            Transport = new HttpClientPipelineTransport(http),
            Endpoint = new Uri("https://provider.invalid/v1")
        });
        using var client = new OpenAIMaxReasoningChatClient(sdk.GetChatClient("test-model").AsIChatClient());
        var options = new ChatOptions();
        new AppConfig.ReasoningConfig { Enabled = true, Effort = effort }.ApplyTo(options);
        if (explicitEffort != null)
            options.Reasoning!.Effort = explicitEffort;
        ChatMessage[] messages = [new(ChatRole.User, "hello")];
        if (streaming)
        {
            await foreach (var _ in client.GetStreamingResponseAsync(messages, options)) { }
        }
        else
        {
            await client.GetResponseAsync(messages, options);
        }
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal(expected, body.RootElement.GetProperty("reasoning_effort").GetString());
        Assert.DoesNotContain("dotcraft.reasoningEffort", handler.Body!);
    }

    private sealed class CaptureHandler(bool streaming) : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            const string completion = """{"id":"chat-1","object":"chat.completion","created":1,"model":"test-model","choices":[{"index":0,"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}]}""";
            const string chunk = """{"id":"chat-1","object":"chat.completion.chunk","created":1,"model":"test-model","choices":[{"index":0,"delta":{"role":"assistant","content":"ok"},"finish_reason":"stop"}]}""";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(streaming ? $"data: {chunk}\n\ndata: [DONE]\n\n" : completion,
                    Encoding.UTF8, streaming ? "text/event-stream" : "application/json")
            };
        }
    }
}
