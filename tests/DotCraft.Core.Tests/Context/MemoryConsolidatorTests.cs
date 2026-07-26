using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Memory;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Context;

public sealed class MemoryConsolidatorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "MemoryLegacy_" + Guid.NewGuid().ToString("N")[..8]);

    public MemoryConsolidatorTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); }
        catch { }
    }

    [Fact]
    public async Task ConsolidateAsync_ProviderTimeoutReturnsFailedProviderTimeout()
    {
        var consolidator = new MemoryConsolidator(
            new ThrowingChatClient(new OperationCanceledException("provider timeout")),
            new MemoryStore(_tempDir));

        var result = await consolidator.ConsolidateAsync(
            [new ChatMessage(ChatRole.User, "remember blue")]);

        Assert.Equal(MemoryConsolidationOutcome.Failed, result.Outcome);
        Assert.Equal("provider_timeout", result.Message);
    }

    [Fact]
    public async Task ConsolidateAsync_UserCancellationRethrows()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var consolidator = new MemoryConsolidator(
            new ThrowingChatClient(new OperationCanceledException(cts.Token)),
            new MemoryStore(_tempDir));

        await Assert.ThrowsAsync<OperationCanceledException>(() => consolidator.ConsolidateAsync(
            [new ChatMessage(ChatRole.User, "remember blue")],
            cts.Token));
    }

    [Fact]
    public async Task ConsolidateAsync_ExplicitOnlyAdapterDoesNotInjectDefaultReasoning()
    {
        var config = new AppConfig
        {
            ProviderId = "test",
            ProviderPreferences = new() { ["test"] = new ModelPreference { Model = "claude-opus-4-8"  } },
            Reasoning = new AppConfig.ReasoningConfig
            {
                Enabled = true,
                Effort = ReasoningEffort.High,
                Output = ReasoningOutput.Full
            }
        };
        var capture = new CaptureOptionsChatClient("{}");
        var client = ProviderChatClientAdapters.CreateRequestAdaptedClient(
            capture,
            config,
            Runtime(ModelProviderProtocols.Anthropic, "claude-opus-4-8"),
            useDefaultReasoning: false);
        var consolidator = new MemoryConsolidator(
            client,
            new MemoryStore(_tempDir));

        await consolidator.ConsolidateAsync(
            [new ChatMessage(ChatRole.User, "remember blue")]);

        Assert.NotNull(capture.Options);
        Assert.Null(capture.Options!.Reasoning);
        Assert.Null(capture.Options.RawRepresentationFactory);
    }

    private static EffectiveModelRuntime Runtime(string protocol, string model) =>
        new(
            ProviderId: protocol,
            Model: model,
            Protocol: protocol,
            DisplayName: protocol,
            ApiKey: "test-key",
            EndPoint: "http://localhost",
            NetworkTimeoutSeconds: 60,
            MaxOutputTokens: 64_000,
            IsImplicit: false,
            Capabilities: ModelProviderCapabilities.ForProtocol(protocol));

    private sealed class ThrowingChatClient(Exception exception) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw exception;

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw exception;

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class CaptureOptionsChatClient(string responseText) : IChatClient
    {
        public ChatOptions? Options { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Options = options;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
