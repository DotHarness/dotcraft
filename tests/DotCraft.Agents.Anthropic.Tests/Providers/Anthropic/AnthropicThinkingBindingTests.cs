using System.Net;
using System.Text;
using System.Text.Json;
using Anthropic;
using DotCraft.Agents;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;
using Xunit;
using BetaMessageCreateParams = Anthropic.Models.Beta.Messages.MessageCreateParams;

namespace DotCraft.Tests.Agents;

public sealed class AnthropicThinkingBindingTests
{
    private const string Model = "claude-opus-5-5";
    private const string BindingBeta = "thinking-binding-controls-2026-08-01";
    private const string ExistingBeta = "fast-mode-2026-02-01";

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Tool_changes_preserve_signed_history_and_enable_binding(bool streaming, bool remove)
    {
        using var handler = new CaptureHandler(streaming);
        using var http = new HttpClient(handler);
        var sdk = new AnthropicClient { HttpClient = http, ApiKey = "test", MaxRetries = 0 };
        using var client = new AnthropicThinkingChatClient(sdk.Beta.AsIChatClient(Model, 1024), Adapter(true), Model);
        var reasoning = new TextReasoningContent("prior reasoning") { ProtectedData = "original-signature" };
        var assistant = new ChatMessage(ChatRole.Assistant, [reasoning, new TextContent("prior answer")]);
        ChatMessage[] history = [new(ChatRole.User, "start"), assistant, new(ChatRole.User, "continue")];
        var contents = assistant.Contents.ToArray();
        var options = Options();
        options.Tools = [Tool("original description")];
        await Send(client, history, options, streaming);
        options.Tools = remove ? [] : [Tool("updated description")];
        await Send(client, history, options, streaming);

        Assert.Equal(2, handler.Requests.Count);
        foreach (var request in handler.Requests)
        {
            AssertBinding(request.Body);
            Assert.Contains(BindingBeta, request.Betas);
            var replay = request.Body.GetProperty("messages").EnumerateArray()
                .Single(message => message.GetProperty("role").GetString() == "assistant");
            var thinking = replay.GetProperty("content").EnumerateArray()
                .Single(block => block.GetProperty("type").GetString() == "thinking");
            Assert.Equal("original-signature", thinking.GetProperty("signature").GetString());
        }
        Assert.Equal("original description", handler.Requests[0].Body.GetProperty("tools")[0].GetProperty("description").GetString());
        if (remove)
        {
            Assert.False(handler.Requests[1].Body.TryGetProperty("tools", out var tools) && tools.GetArrayLength() > 0);
        }
        else
        {
            Assert.Equal("updated description", handler.Requests[1].Body.GetProperty("tools")[0].GetProperty("description").GetString());
        }
        Assert.Equal(contents, assistant.Contents);
        Assert.Same(reasoning, assistant.Contents[0]);
        Assert.Equal("prior reasoning", reasoning.Text);
        Assert.Equal("original-signature", reasoning.ProtectedData);
    }

    [Fact]
    public async Task Binding_preserves_existing_betas_and_request_settings()
    {
        using var handler = new CaptureHandler(false);
        using var http = new HttpClient(handler);
        var sdk = new AnthropicClient { HttpClient = http, ApiKey = "test", MaxRetries = 0 };
        using var client = new AnthropicThinkingChatClient(sdk.Beta.AsIChatClient(Model, 1024), Adapter(true), Model);
        var raw = new BetaMessageCreateParams
        {
            Model = Model,
            MaxTokens = 1024,
            Messages = [],
            Betas = [ExistingBeta, BindingBeta]
        };
        var options = Options();
        options.RawRepresentationFactory = _ => raw;
        await Send(client, [new(ChatRole.User, "continue")], options, false);

        var request = Assert.Single(handler.Requests);
        AssertBinding(request.Body);
        Assert.Equal(new[] { ExistingBeta, BindingBeta }.Order(), request.Betas.Order());
        Assert.Equal("omitted", request.Body.GetProperty("thinking").GetProperty("display").GetString());
        Assert.Equal("high", request.Body.GetProperty("output_config").GetProperty("effort").GetString());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Unsupported_or_disabled_thinking_does_not_enable_binding(bool supported, bool disabled)
    {
        using var handler = new CaptureHandler(false);
        using var http = new HttpClient(handler);
        var sdk = new AnthropicClient { HttpClient = http, ApiKey = "test", MaxRetries = 0 };
        using var client = new AnthropicThinkingChatClient(sdk.Beta.AsIChatClient(Model, 1024), Adapter(supported), Model);
        var options = Options();
        if (disabled)
            options.Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None };
        options.RawRepresentationFactory = _ => new BetaMessageCreateParams
        {
            Model = Model, MaxTokens = 1024, Messages = [], Betas = [ExistingBeta]
        };
        await Send(client, [new(ChatRole.User, "continue")], options, false);

        var request = Assert.Single(handler.Requests);
        var thinking = request.Body.GetProperty("thinking");
        Assert.Equal(disabled ? "disabled" : "adaptive", thinking.GetProperty("type").GetString());
        Assert.False(thinking.TryGetProperty("block_binding", out _));
        Assert.Equal(new[] { ExistingBeta }, request.Betas);
    }

    [Theory]
    [InlineData("https://api.anthropic.com", false, true)]
    [InlineData("https://proxy.example", false, false)]
    [InlineData("https://model-service.example", true, true)]
    public async Task Provider_pipeline_uses_resolved_transport_support(string endpoint, bool remote, bool expected)
    {
        using var handler = new CaptureHandler(false);
        using var http = new HttpClient(handler);
        var runtime = new EffectiveModelRuntime(
            "test", Model, ModelProviderProtocols.Anthropic, "Test", "key", endpoint, 10, 1024,
            ModelProviderCapabilities.ForProtocol(ModelProviderProtocols.Anthropic), IsRemote: remote);
        if (remote)
        {
            var adaptation = new ProviderRequestAdaptation(false, false, Adapter(true), null);
            runtime = runtime with
            {
                RequestAdaptation = JsonSerializer.Deserialize<ProviderRequestAdaptation>(JsonSerializer.Serialize(adaptation))
            };
        }
        using var client = new AnthropicClientProvider(new Transport(http)).CreateChatClient(runtime);
        await Send(client, [new(ChatRole.User, "continue")], Options(), false);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(expected, request.Body.GetProperty("thinking").TryGetProperty("block_binding", out _));
        Assert.Equal(expected, request.Betas.Contains(BindingBeta));
        if (expected)
            AssertBinding(request.Body);
    }

    private static ModelThinkingAdapterCatalog.AnthropicThinkingAdapterData Adapter(bool supported) => new()
    {
        ThinkingType = "adaptive",
        ThinkingDisplay = "fromReasoningOutput",
        OutputConfigEffort = "fromReasoningEffort",
        SupportsThinkingBlockBinding = supported
    };

    private static ChatOptions Options() => new()
    {
        Reasoning = new ReasoningOptions { Effort = ReasoningEffort.High, Output = ReasoningOutput.None }
    };

    private static AITool Tool(string description) => AIFunctionFactory.CreateDeclaration("Probe", description,
        JsonSerializer.Deserialize<JsonElement>("""{"type":"object","properties":{}}"""));

    private static async Task Send(IChatClient client, ChatMessage[] history, ChatOptions options, bool streaming)
    {
        if (streaming)
        {
            await foreach (var _ in client.GetStreamingResponseAsync(history, options)) { }
        }
        else
        {
            await client.GetResponseAsync(history, options);
        }
    }

    private static void AssertBinding(JsonElement body) => Assert.Equal("drop_block",
        body.GetProperty("thinking").GetProperty("block_binding").GetProperty("prefix_mismatch_behavior").GetString());

    private sealed class Transport(HttpClient client) : IProviderHttpTransport
    {
        public HttpClient CreateClient(string providerId, Uri providerEndpoint) => client;
    }

    private sealed class CaptureHandler(bool streaming) : HttpMessageHandler
    {
        public List<(JsonElement Body, string[] Betas)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(cancellationToken));
            var betas = request.Headers.TryGetValues("anthropic-beta", out var values)
                ? values.SelectMany(value => value.Split(',', StringSplitOptions.TrimEntries)).ToArray() : [];
            Requests.Add((body, betas));
            const string message = """
                {"id":"msg_test","type":"message","role":"assistant","model":"claude-opus-5-5","content":[],"stop_reason":"end_turn","usage":{"input_tokens":10,"output_tokens":1}}
                """;
            var response = streaming
                ? $"event: message_start\ndata: {{\"type\":\"message_start\",\"message\":{message}}}\n\nevent: message_delta\ndata: {{\"type\":\"message_delta\",\"delta\":{{\"stop_reason\":\"end_turn\",\"stop_sequence\":null}},\"usage\":{{\"output_tokens\":1}}}}\n\nevent: message_stop\ndata: {{\"type\":\"message_stop\"}}\n\n"
                : message;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, streaming ? "text/event-stream" : "application/json")
            };
        }
    }
}
