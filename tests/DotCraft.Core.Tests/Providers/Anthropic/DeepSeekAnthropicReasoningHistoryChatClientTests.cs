using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Anthropic;
using AnthropicBetaContentBlockParam = Anthropic.Models.Beta.Messages.BetaContentBlockParam;
using DotCraft.Agents;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Agents;

public sealed class DeepSeekAnthropicReasoningHistoryChatClientTests
{
    [Fact]
    public async Task GetResponseAsync_AnnotatesReasoningHistoryAsThinkingBlock()
    {
        using var inner = new CapturingChatClient();
        using var client = new DeepSeekAnthropicReasoningHistoryChatClient(inner, CreateAdapter());
        var reasoning = new TextReasoningContent("need status") { ProtectedData = "signature-data" };
        var text = new TextContent("visible text");
        var call = new FunctionCallContent(
            "call-1",
            "ReadFile",
            new Dictionary<string, object?> { ["path"] = "a.txt" });
        var message = new ChatMessage(ChatRole.Assistant, (IList<AIContent>)[reasoning, text, call])
        {
            RawRepresentation = new object(),
            MessageId = "msg-1"
        };

        await client.GetResponseAsync([message]);

        var prepared = Assert.Single(inner.LastMessages);
        Assert.Equal("msg-1", prepared.MessageId);
        Assert.Null(prepared.RawRepresentation);
        var preparedReasoning = Assert.IsType<TextReasoningContent>(prepared.Contents[0]);
        Assert.Equal("need status", preparedReasoning.Text);
        Assert.Equal("signature-data", preparedReasoning.ProtectedData);
        var raw = Assert.IsType<AnthropicBetaContentBlockParam>(preparedReasoning.RawRepresentation);
        Assert.True(raw.TryPickThinking(out var thinking));
        Assert.Equal("need status", thinking.Thinking);
        Assert.Equal("signature-data", thinking.Signature);
        Assert.Same(text, prepared.Contents[1]);
        Assert.Same(call, prepared.Contents[2]);
    }

    [Fact]
    public async Task ProviderAdapters_OfficialAnthropicDoesNotApplyDeepSeekHistoryAdapter()
    {
        using var inner = new CapturingChatClient();
        using var client = ProviderChatClientAdapters.CreateRequestAdaptedClient(
            inner,
            new AppConfig(),
            CreateRuntime(
                ModelProviderProtocols.Anthropic,
                "claude-opus-4-7",
                "https://api.anthropic.com"));
        var reasoning = new TextReasoningContent("native anthropic reasoning");

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.Assistant, (IList<AIContent>)[reasoning])]);

        var message = Assert.Single(inner.LastMessages);
        var capturedReasoning = Assert.Single(message.Contents.OfType<TextReasoningContent>());
        Assert.Same(reasoning, capturedReasoning);
        Assert.Null(capturedReasoning.RawRepresentation);
    }

    [Fact]
    public async Task ProviderAdapters_OpenAICompatibleDeepSeekDoesNotApplyAnthropicHistoryAdapter()
    {
        using var inner = new CapturingChatClient();
        using var client = ProviderChatClientAdapters.CreateRequestAdaptedClient(
            inner,
            new AppConfig(),
            CreateRuntime(
                ModelProviderProtocols.OpenAIChatCompletions,
                "deepseek-v4-pro",
                "https://api.deepseek.com/v1"));
        var reasoning = new TextReasoningContent("openai-compatible reasoning");

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.Assistant, (IList<AIContent>)[reasoning])]);

        var message = Assert.Single(inner.LastMessages);
        var capturedReasoning = Assert.Single(message.Contents.OfType<TextReasoningContent>());
        Assert.Same(reasoning, capturedReasoning);
        Assert.Null(capturedReasoning.RawRepresentation);
    }

    [Fact]
    public async Task ProviderAdapters_DeepSeekAnthropicSerializesReasoningHistoryAsThinking()
    {
        var handler = new CaptureHandler();
        var anthropicClient = new AnthropicClient
        {
            HttpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") },
            ApiKey = "test-key"
        };
        using var client = ProviderChatClientAdapters.CreateRequestAdaptedClient(
            anthropicClient.Beta.AsIChatClient("deepseek-v4-pro"),
            new AppConfig(),
            CreateRuntime(
                ModelProviderProtocols.Anthropic,
                "deepseek-v4-pro",
                "https://api.deepseek.com/anthropic"));

        await client.GetResponseAsync(
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(
                ChatRole.Assistant,
                (IList<AIContent>)
                [
                    new TextReasoningContent("need a tool"),
                    new FunctionCallContent("call_1", "ReadFile", new Dictionary<string, object?>())
                ])
        ]);

        Assert.NotNull(handler.LastRequestJson);
        Assert.DoesNotContain("redacted_thinking", handler.LastRequestJson, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(handler.LastRequestJson);
        var content = document.RootElement
            .GetProperty("messages")[1]
            .GetProperty("content");
        Assert.Equal("thinking", content[0].GetProperty("type").GetString());
        Assert.Equal("need a tool", content[0].GetProperty("thinking").GetString());
        Assert.Equal("tool_use", content[1].GetProperty("type").GetString());
    }

    private static ModelThinkingAdapterCatalog.AnthropicMessageContentAdapterData CreateAdapter()
    {
        var adapter = new ModelThinkingAdapterCatalog.AnthropicMessageContentAdapterData();
        adapter.Models.Add("deepseek");
        adapter.ReasoningHistoryBlockType = DeepSeekAnthropicReasoningHistoryChatClient.ThinkingBlockType;
        return adapter;
    }

    private static EffectiveModelRuntime CreateRuntime(
        string protocol,
        string model,
        string endpoint) =>
        new(
            "provider",
            model,
            protocol,
            "Provider",
            "test-key",
            endpoint,
            600,
            null,
            IsImplicit: false,
            ModelProviderCapabilities.ForProtocol(protocol));

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? LastRequestJson { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                        "id": "msg_reasoning_history_test",
                        "type": "message",
                        "role": "assistant",
                        "model": "deepseek-v4-pro",
                        "content": [{
                            "type": "text",
                            "text": "ok"
                        }],
                        "stop_reason": "end_turn",
                        "usage": {
                            "input_tokens": 10,
                            "output_tokens": 1
                        }
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }

    private sealed class CapturingChatClient : IChatClient
    {
        public IReadOnlyList<ChatMessage> LastMessages { get; private set; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastMessages = chatMessages.ToList();
            return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "ok")]));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastMessages = chatMessages.ToList();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");
            await Task.CompletedTask;
        }

        public object? GetService(System.Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }
}
