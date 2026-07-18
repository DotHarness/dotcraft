using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Anthropic;
using DotCraft.Agents;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Agents;

public sealed class AnthropicThinkingChatClientTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"anthropic_thinking_client_{Guid.NewGuid():N}");

    public AnthropicThinkingChatClientTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            Path.Combine(_root, ModelThinkingAdapterCatalog.FileName),
            """
            {
              "anthropicThinking": {
                "adapters": [
                  {
                    "models": ["test-xhigh-model"],
                    "thinking": {
                      "type": "adaptive",
                      "display": "fromReasoningOutput"
                    },
                    "outputConfig": {
                      "effort": "fromReasoningEffort",
                      "effortMap": { "extraHigh": "xhigh" }
                    }
                  },
                  {
                    "models": ["test-max-model"],
                    "thinking": {
                      "type": "adaptive",
                      "display": "fromReasoningOutput"
                    },
                    "outputConfig": {
                      "effort": "fromReasoningEffort",
                      "effortMap": { "extraHigh": "max" }
                    }
                  }
                ]
              }
            }
            """);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task GetResponseAsync_ConfiguredAdapterSerializesAdaptiveThinking()
    {
        var handler = new CaptureHandler();
        var config = CreateConfig(
            enabled: true,
            effort: ReasoningEffort.High,
            output: ReasoningOutput.Full);
        var client = CreateClient(handler, config, model: "provider/test-xhigh-model");

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], new ChatOptions
        {
            Reasoning = config.Reasoning.ToOptions()
        });

        using var document = JsonDocument.Parse(handler.LastRequestJson!);
        var root = document.RootElement;
        var thinking = root.GetProperty("thinking");
        Assert.Equal("adaptive", thinking.GetProperty("type").GetString());
        Assert.Equal("summarized", thinking.GetProperty("display").GetString());
        Assert.False(thinking.TryGetProperty("budget_tokens", out _));
        Assert.Equal("high", root.GetProperty("output_config").GetProperty("effort").GetString());
    }

    [Fact]
    public async Task GetResponseAsync_BetaClientSerializesAdaptiveThinking()
    {
        var handler = new CaptureHandler();
        var config = CreateConfig(
            enabled: true,
            effort: ReasoningEffort.High,
            output: ReasoningOutput.Full);
        var client = CreateBetaClient(handler, config, model: "test-xhigh-model");

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], new ChatOptions
        {
            Reasoning = config.Reasoning.ToOptions()
        });

        using var document = JsonDocument.Parse(handler.LastRequestJson!);
        var root = document.RootElement;
        Assert.Equal("adaptive", root.GetProperty("thinking").GetProperty("type").GetString());
        Assert.Equal("summarized", root.GetProperty("thinking").GetProperty("display").GetString());
        Assert.Equal("high", root.GetProperty("output_config").GetProperty("effort").GetString());
    }

    [Fact]
    public async Task GetResponseAsync_ReasoningOutputNoneSerializesOmittedDisplay()
    {
        var handler = new CaptureHandler();
        var config = CreateConfig(
            enabled: true,
            effort: ReasoningEffort.ExtraHigh,
            output: ReasoningOutput.None);
        var client = CreateClient(handler, config, model: "test-xhigh-model");

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], new ChatOptions
        {
            Reasoning = config.Reasoning.ToOptions()
        });

        using var document = JsonDocument.Parse(handler.LastRequestJson!);
        var root = document.RootElement;
        Assert.Equal("omitted", root.GetProperty("thinking").GetProperty("display").GetString());
        Assert.Equal("xhigh", root.GetProperty("output_config").GetProperty("effort").GetString());
    }

    [Fact]
    public async Task GetResponseAsync_MapsExtraHighToConfiguredMaxEffort()
    {
        var handler = new CaptureHandler();
        var config = CreateConfig(
            enabled: true,
            effort: ReasoningEffort.ExtraHigh,
            output: ReasoningOutput.Full);
        var client = CreateClient(handler, config, model: "test-max-model");

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], new ChatOptions
        {
            Reasoning = config.Reasoning.ToOptions()
        });

        using var document = JsonDocument.Parse(handler.LastRequestJson!);
        Assert.Equal("max", document.RootElement.GetProperty("output_config").GetProperty("effort").GetString());
    }

    [Fact]
    public void PrepareOptions_UnlistedModelLeavesRequestShapeUnchanged()
    {
        var config = CreateConfig(
            enabled: true,
            effort: ReasoningEffort.High,
            output: ReasoningOutput.Full);
        var client = new AnthropicThinkingChatClient(
            new CaptureChatClient(),
            config,
            "unlisted-model",
            "https://api.example.test/anthropic");
        var options = new ChatOptions { Reasoning = config.Reasoning.ToOptions() };

        var prepared = client.PrepareOptions(options);

        Assert.Same(options, prepared);
        Assert.Null(prepared!.RawRepresentationFactory);
    }

    [Fact]
    public void PrepareOptions_ExplicitOnlyWithoutRequestReasoningLeavesOptionsUnchanged()
    {
        var config = CreateConfig(
            enabled: true,
            effort: ReasoningEffort.High,
            output: ReasoningOutput.Full);
        var client = new AnthropicThinkingChatClient(
            new CaptureChatClient(),
            config,
            "test-xhigh-model",
            "https://api.example.test/anthropic",
            useDefaultReasoning: false);
        var options = new ChatOptions();

        var prepared = client.PrepareOptions(options);

        Assert.Same(options, prepared);
        Assert.Null(prepared!.RawRepresentationFactory);
    }

    private static AnthropicThinkingChatClient CreateClient(
        CaptureHandler handler,
        AppConfig config,
        string model)
    {
        var anthropicClient = new AnthropicClient
        {
            HttpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") },
            ApiKey = "test-key"
        };

        return new AnthropicThinkingChatClient(
            anthropicClient.AsIChatClient(model),
            config,
            model,
            "http://localhost",
            defaultMaxOutputTokens: 64_000);
    }

    private static AnthropicThinkingChatClient CreateBetaClient(
        CaptureHandler handler,
        AppConfig config,
        string model)
    {
        var anthropicClient = new AnthropicClient
        {
            HttpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") },
            ApiKey = "test-key"
        };

        return new AnthropicThinkingChatClient(
            anthropicClient.Beta.AsIChatClient(model),
            config,
            model,
            "http://localhost",
            defaultMaxOutputTokens: 64_000);
    }

    private AppConfig CreateConfig(
        bool enabled,
        ReasoningEffort effort,
        ReasoningOutput output) =>
        new()
        {
            WorkspaceConfigPath = Path.Combine(_root, "config.json"),
            Reasoning = new AppConfig.ReasoningConfig
            {
                Enabled = enabled,
                Effort = effort,
                Output = output
            }
        };

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
                        "id": "msg_thinking_test",
                        "type": "message",
                        "role": "assistant",
                        "model": "test-xhigh-model",
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

    private sealed class CaptureChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("ok")]);
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
