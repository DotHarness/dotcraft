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

    private sealed class TestProvider(string protocol) : IModelProvider
    {
        public IReadOnlyCollection<string> Protocols { get; } = [protocol];

        public IChatClient CreateChatClient(EffectiveModelRuntime runtime) => new TestChatClient();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
    }

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
