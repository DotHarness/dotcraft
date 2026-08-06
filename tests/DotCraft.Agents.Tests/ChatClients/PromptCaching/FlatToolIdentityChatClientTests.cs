using DotCraft.Agents;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed class FlatToolIdentityChatClientTests
{
    [Fact]
    public async Task GetResponseAsync_UsesPersistedFlatAliasWithoutParsingCompositeName()
    {
        var inner = new CapturingChatClient();
        using var client = new FlatToolIdentityChatClient(inner);
        var call = new FunctionCallContent("call-1", "get_me")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["openai.responses.function_call.namespace"] = "mcp__code_host_apps",
                ["dotcraft.tool.provider_flat_name"] = "mcp__code_host_apps__get_me"
            }
        };

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.Assistant, [call])
        ]);

        var sent = Assert.Single(inner.Messages!);
        var projected = Assert.IsType<FunctionCallContent>(Assert.Single(sent.Contents));
        Assert.Equal("mcp__code_host_apps__get_me", projected.Name);
        Assert.Equal("get_me", call.Name);
    }

    private sealed class CapturingChatClient : IChatClient
    {
        public IReadOnlyList<ChatMessage>? Messages { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Messages = messages.ToArray();
            return Task.FromResult(new ChatResponse([]));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Messages = messages.ToArray();
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
