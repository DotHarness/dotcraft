using System.Net;
using System.Text;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed class ProviderHistoryWireTests
{
    [Theory]
    [InlineData(ModelProviderProtocols.OpenAIChatCompletions)]
    [InlineData(ModelProviderProtocols.OpenAIResponses)]
    [InlineData(ModelProviderProtocols.Anthropic)]
    public async Task MixedProviderHistory_SendsPortableToolPairsWithoutForeignNativeState(string protocol)
    {
        using var transport = new CaptureTransport(protocol);
        var runtime = new EffectiveModelRuntime("target", "test-model", protocol, "Target", "test-key",
            "https://provider.invalid/v1", 30, 1024, ModelProviderCapabilities.ForProtocol(protocol));
        IModelProvider provider = protocol == ModelProviderProtocols.Anthropic
            ? new AnthropicClientProvider(transport)
            : new OpenAIClientProvider(httpTransport: transport);
        var config = new AppConfig();
        config.Reasoning.Enabled = false;
        var builder = new ChatClientBuilder(provider.CreateChatClient(runtime));
        ProviderChatClientAdapters.UseProviderAdapters(
            builder, config, runtime, config.Reasoning, config.Speed, config.PromptCaching);
        using var client = builder.Build();

        await client.GetResponseAsync(MixedHistory());

        var body = transport.Body!;
        Assert.DoesNotContain("private-", body);
        Assert.DoesNotContain("source-item-", body);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        switch (protocol)
        {
            case ModelProviderProtocols.OpenAIResponses:
            {
                var input = root.GetProperty("input").EnumerateArray().ToArray();
                Assert.DoesNotContain(input, item => item.GetProperty("type").GetString() == "reasoning");
                var calls = input.Where(item => item.GetProperty("type").GetString() == "function_call").ToArray();
                var results = input.Where(item => item.GetProperty("type").GetString() == "function_call_output").ToArray();
                AssertPairs(calls.Select(item => item.GetProperty("call_id").GetString()),
                    results.Select(item => item.GetProperty("call_id").GetString()));
                for (var i = 0; i < calls.Length; i++)
                {
                    Assert.Equal("catalog", calls[i].GetProperty("namespace").GetString());
                    Assert.Equal("lookup", calls[i].GetProperty("name").GetString());
                    using var arguments = JsonDocument.Parse(calls[i].GetProperty("arguments").GetString()!);
                    Assert.Equal(i, arguments.RootElement.GetProperty("index").GetInt32());
                    Assert.Equal($"result-{i}", results[i].GetProperty("output").GetString());
                }
                break;
            }
            case ModelProviderProtocols.OpenAIChatCompletions:
            {
                var messages = root.GetProperty("messages").EnumerateArray().ToArray();
                var calls = messages.Where(message => message.TryGetProperty("tool_calls", out _))
                    .SelectMany(message => message.GetProperty("tool_calls").EnumerateArray()).ToArray();
                var results = messages.Where(message => message.GetProperty("role").GetString() == "tool").ToArray();
                AssertPairs(calls.Select(call => call.GetProperty("id").GetString()),
                    results.Select(result => result.GetProperty("tool_call_id").GetString()));
                for (var i = 0; i < calls.Length; i++)
                {
                    var function = calls[i].GetProperty("function");
                    Assert.Equal("catalog__lookup", function.GetProperty("name").GetString());
                    using var arguments = JsonDocument.Parse(function.GetProperty("arguments").GetString()!);
                    Assert.Equal(i, arguments.RootElement.GetProperty("index").GetInt32());
                    Assert.Equal($"result-{i}", results[i].GetProperty("content").GetString());
                }
                break;
            }
            case ModelProviderProtocols.Anthropic:
            {
                var blocks = root.GetProperty("messages").EnumerateArray()
                    .SelectMany(message => message.GetProperty("content").EnumerateArray()).ToArray();
                Assert.DoesNotContain(blocks, block => block.GetProperty("type").GetString() is "thinking" or "redacted_thinking");
                var calls = blocks.Where(block => block.GetProperty("type").GetString() == "tool_use").ToArray();
                var results = blocks.Where(block => block.GetProperty("type").GetString() == "tool_result").ToArray();
                AssertPairs(calls.Select(call => call.GetProperty("id").GetString()),
                    results.Select(result => result.GetProperty("tool_use_id").GetString()));
                for (var i = 0; i < calls.Length; i++)
                {
                    Assert.Equal("catalog__lookup", calls[i].GetProperty("name").GetString());
                    Assert.Equal(i, calls[i].GetProperty("input").GetProperty("index").GetInt32());
                    Assert.Contains($"result-{i}", results[i].GetProperty("content").GetRawText());
                }
                break;
            }
        }
        for (var i = 0; i < 3; i++)
            Assert.Contains($"visible-{i}", body);
    }

    private static void AssertPairs(IEnumerable<string?> callIds, IEnumerable<string?> resultIds)
    {
        var calls = callIds.ToArray();
        Assert.Equal(3, calls.Length);
        Assert.Equal(calls, resultIds);
        Assert.Equal(3, calls.Distinct().Count());
        Assert.All(calls, id => Assert.Matches("^[a-zA-Z0-9_-]{1,64}$", id!));
    }

    private static IReadOnlyList<ChatMessage> MixedHistory()
    {
        string[] protocols = [
            ModelProviderProtocols.OpenAIChatCompletions,
            ModelProviderProtocols.OpenAIResponses,
            ModelProviderProtocols.Anthropic
        ];
        var messages = new List<ChatMessage>();
        for (var i = 0; i < protocols.Length; i++)
        {
            var callId = $"source.{i}|{new string('x', 80)}";
            var call = new FunctionCallContent(callId, "lookup", new Dictionary<string, object?> { ["index"] = i })
            {
                AdditionalProperties = new()
                {
                    [ProviderFunctionCallMetadata.NamespaceKey] = "catalog",
                    ["dotcraft.tool.provider_flat_name"] = "catalog__lookup",
                    ["openai.responses.item_id"] = $"fc_source-item-{i}"
                }
            };
            var assistant = new ChatMessage(ChatRole.Assistant, [
                new TextReasoningContent($"private-thought-{i}") { ProtectedData = $"private-signature-{i}" },
                new TextContent($"visible-{i}"), call
            ]) { MessageId = $"msg_source-item-message-{i}" };
            ProviderMessageProjection.Stamp(assistant, new($"source-{i}", protocols[i], "source-model"));
            messages.Add(new ChatMessage(ChatRole.User, $"question-{i}"));
            messages.Add(assistant);
            messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(callId, $"result-{i}")]));
        }
        messages.Add(new ChatMessage(ChatRole.User, "continue"));
        return messages;
    }

    private sealed class CaptureTransport(string protocol) : HttpMessageHandler, IProviderHttpTransport
    {
        private HttpClient? _client;
        public string? Body { get; private set; }

        public HttpClient CreateClient(string providerId, Uri providerEndpoint) =>
            _client ??= new HttpClient(this, disposeHandler: false);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var response = protocol switch
            {
                ModelProviderProtocols.OpenAIResponses =>
                    "data: {\"type\":\"response.completed\",\"sequence_number\":1,\"response\":{\"id\":\"resp_test\",\"object\":\"response\",\"created_at\":1,\"status\":\"completed\",\"model\":\"test-model\",\"output\":[]}}\n\n",
                ModelProviderProtocols.Anthropic =>
                    """{"id":"msg_test","type":"message","role":"assistant","model":"test-model","content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","usage":{"input_tokens":1,"output_tokens":1}}""",
                _ =>
                    """{"id":"chat_test","object":"chat.completion","created":1,"model":"test-model","choices":[{"index":0,"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}]}"""
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8,
                    protocol == ModelProviderProtocols.OpenAIResponses ? "text/event-stream" : "application/json")
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _client?.Dispose();
            base.Dispose(disposing);
        }
    }
}
