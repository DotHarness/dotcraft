using DotCraft.Agents;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed class ModelProviderRegistryTests
{
    [Fact]
    public void Resolve_NormalizesAndReturnsUniqueProvider()
    {
        var provider = new TestProvider(ModelProviderProtocols.OpenAIResponses);
        var registry = new ModelProviderRegistry([provider]);

        Assert.Same(provider, registry.Resolve(" OPENAI-RESPONSES "));
        Assert.Equal([ModelProviderProtocols.OpenAIResponses], registry.Protocols);
    }

    [Fact]
    public void Constructor_RejectsDuplicateProtocolOwnership()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new ModelProviderRegistry([
            new TestProvider(ModelProviderProtocols.Anthropic),
            new TestProvider(ModelProviderProtocols.Anthropic)
        ]));

        Assert.Contains(ModelProviderProtocols.Anthropic, error.Message);
    }

    [Fact]
    public void Resolve_ReportsMissingExplicitRegistration()
    {
        var registry = new ModelProviderRegistry([]);

        var error = Assert.Throws<ModelProviderNotRegisteredException>(
            () => registry.Resolve(ModelProviderProtocols.OpenAIChatCompletions));

        Assert.Equal(ModelProviderProtocols.OpenAIChatCompletions, error.Protocol);
        Assert.Equal("UnsupportedModelProvider", ModelProviderNotRegisteredException.ErrorCode);
    }

    [Fact]
    public void CreateChatClient_DelegatesImmutableRuntime()
    {
        var provider = new TestProvider(ModelProviderProtocols.Anthropic);
        var registry = new ModelProviderRegistry([provider]);
        var runtime = CreateRuntime(ModelProviderProtocols.Anthropic);

        var client = registry.CreateChatClient(runtime);

        Assert.Same(provider.Client, client);
        Assert.Same(runtime, provider.LastRuntime);
    }

    [Fact]
    public void GetService_ResolvesOptionalProviderCapability()
    {
        var capability = new TestCapability();
        var provider = new TestProvider(ModelProviderProtocols.OpenAIResponses, capability);
        var registry = new ModelProviderRegistry([provider]);

        Assert.Same(
            capability,
            registry.GetService<TestCapability>(ModelProviderProtocols.OpenAIResponses));
    }

    private static EffectiveModelRuntime CreateRuntime(string protocol) => new(
        "provider",
        "model",
        protocol,
        "Provider",
        "key",
        "https://example.test",
        30,
        null,
        false,
        ModelProviderCapabilities.ForProtocol(protocol));

    private sealed class TestProvider(string protocol, object? capability = null) : IModelProvider
    {
        public TestChatClient Client { get; } = new();
        public IReadOnlyCollection<string> Protocols { get; } = [protocol];
        public EffectiveModelRuntime? LastRuntime { get; private set; }

        public IChatClient CreateChatClient(EffectiveModelRuntime runtime)
        {
            LastRuntime = runtime;
            return Client;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey is null && capability != null && serviceType.IsInstanceOfType(capability)
                ? capability
                : null;
    }

    private sealed class TestCapability;

    private sealed class TestChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([]));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Empty();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }

        private static async IAsyncEnumerable<ChatResponseUpdate> Empty()
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
