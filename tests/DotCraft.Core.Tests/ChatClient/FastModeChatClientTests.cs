using System.ClientModel.Primitives;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Anthropic.Models.Beta;
using AnthropicBetaMessageCreateParams = Anthropic.Models.Beta.Messages.MessageCreateParams;
using DotCraft.Agents;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;
using OpenAI.Responses;
using Xunit;

#pragma warning disable OPENAI001, MEAI001

namespace DotCraft.Core.Tests.Agents;

public sealed class FastModeChatClientTests
{
    [Fact]
    public void ResponsesFast_AddsPriorityServiceTier()
    {
        using var inner = new CaptureChatClient();
        using var client = new FastModeChatClient(
            inner,
            new AppConfig(),
            ModelProviderProtocols.OpenAIResponses,
            "gpt-5.4",
            InferenceSpeed.Fast);

        var prepared = client.PrepareOptions(new ChatOptions());
        var raw = Assert.IsType<CreateResponseOptions>(prepared!.RawRepresentationFactory!(inner));
        using var json = JsonDocument.Parse(ModelReaderWriter.Write(raw).ToString());

        Assert.Equal("priority", json.RootElement.GetProperty("service_tier").GetString());
    }

    [Theory]
    [InlineData(InferenceSpeed.Standard, ModelProviderProtocols.OpenAIResponses, "gpt-5.4")]
    [InlineData(InferenceSpeed.Fast, ModelProviderProtocols.OpenAIChatCompletions, "gpt-5.4")]
    [InlineData(InferenceSpeed.Fast, ModelProviderProtocols.OpenAIResponses, "gpt-5.4-mini")]
    public void UnsupportedOrStandard_DoesNotPatchRequest(
        InferenceSpeed speed,
        string protocol,
        string model)
    {
        using var inner = new CaptureChatClient();
        using var client = new FastModeChatClient(inner, new AppConfig(), protocol, model, speed);

        Assert.Null(client.PrepareOptions(new ChatOptions())?.RawRepresentationFactory);
    }

    [Fact]
    public void AnthropicFast_AddsSpeedAndBetaWithoutDroppingExistingBetas()
    {
        using var inner = new CaptureChatClient();
        using var client = new FastModeChatClient(
            inner,
            new AppConfig(),
            ModelProviderProtocols.Anthropic,
            "claude-opus-4-8",
            InferenceSpeed.Fast);
        var options = new ChatOptions
        {
            RawRepresentationFactory = _ => new AnthropicBetaMessageCreateParams
            {
                Model = "claude-opus-4-8",
                MaxTokens = 1024,
                Messages = [],
                Betas = [AnthropicBeta.PromptCaching2024_07_31]
            }
        };

        var prepared = client.PrepareOptions(options);
        var raw = Assert.IsType<AnthropicBetaMessageCreateParams>(prepared!.RawRepresentationFactory!(inner));

        Assert.Equal("fast", raw.Speed?.ToString().Trim('"'));
        var betas = raw.Betas?.Select(static beta => beta.ToString().Trim('"')).ToArray() ?? [];
        Assert.Contains("prompt-caching-2024-07-31", betas);
        Assert.Single(betas, beta => beta == "fast-mode-2026-02-01");
    }

    [Fact]
    public void ResponsesFast_UsesWorkspaceModelCatalogOverride()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dotcraft_fast_catalog_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, ModelCatalog.FileName), """
                {
                  "models": {
                    "custom-fast": {
                      "fast": { "protocols": ["openai-responses"] }
                    }
                  }
                }
                """);
            var config = new AppConfig { WorkspaceConfigPath = Path.Combine(directory, "config.json") };
            using var inner = new CaptureChatClient();
            using var client = new FastModeChatClient(
                inner,
                config,
                ModelProviderProtocols.OpenAIResponses,
                "custom-fast-v1",
                InferenceSpeed.Fast);

            var prepared = client.PrepareOptions(new ChatOptions());
            var raw = Assert.IsType<CreateResponseOptions>(prepared!.RawRepresentationFactory!(inner));
            using var json = JsonDocument.Parse(ModelReaderWriter.Write(raw).ToString());
            Assert.Equal("priority", json.RootElement.GetProperty("service_tier").GetString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class CaptureChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("ok")]);
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    private sealed class FastModeChatClient : IDisposable
    {
        private readonly IChatClient _client;
        private readonly ProviderPipelineOptions _options;

        public FastModeChatClient(
            IChatClient inner,
            AppConfig config,
            string protocol,
            string model,
            InferenceSpeed speed)
        {
            var runtime = new EffectiveModelRuntime(
                "test",
                model,
                protocol,
                "test",
                "test-key",
                protocol == ModelProviderProtocols.Anthropic
                    ? "https://api.anthropic.com"
                    : "https://api.openai.com/v1",
                30,
                null,
                false,
                ModelProviderCapabilities.ForProtocol(protocol));
            var effectiveSpeed = ModelCatalog.SupportsFast(config, protocol, model)
                ? speed
                : InferenceSpeed.Standard;
            _options = new ProviderPipelineOptions(
                runtime,
                null,
                null,
                false,
                effectiveSpeed.ToString(),
                false,
                null);
            _client = protocol == ModelProviderProtocols.Anthropic
                ? new AnthropicFastModeChatClient(inner, runtime)
                : new OpenAIFastModeChatClient(inner);
        }

        public ChatOptions? PrepareOptions(ChatOptions? options)
        {
            using var scope = ProviderPipelineOptionsScope.Push(_options);
            return _client switch
            {
                AnthropicFastModeChatClient anthropic => anthropic.PrepareOptions(options),
                OpenAIFastModeChatClient openAI => openAI.PrepareOptions(options),
                _ => options
            };
        }

        public void Dispose() => _client.Dispose();
    }
}
