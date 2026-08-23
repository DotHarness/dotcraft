using System.Reflection;
using System.Reflection.Emit;
using DotCraft.Runtime;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Runtime.Plugins;

public sealed class PluginObjectGraphGuardTests
{
    [Fact]
    public void HostResponseWithNestedCollectibleObject_IsRejected()
    {
        var payloadType = CreateCollectibleType();
        var response = ResponseWith(Activator.CreateInstance(payloadType)!);

        Assert.Throws<NotSupportedException>(() =>
            PluginObjectGraphGuard.EnsureHostOwnedGraph(response, "chat response"));
    }

    [Fact]
    public void EmptyGenericContainerAndTypeTokenForCollectibleType_AreRejected()
    {
        var payloadType = CreateCollectibleType();
        var list = Activator.CreateInstance(typeof(List<>).MakeGenericType(payloadType))!;

        Assert.Throws<NotSupportedException>(() =>
            PluginObjectGraphGuard.EnsureHostOwnedGraph(ResponseWith(list), "chat response"));
        Assert.Throws<NotSupportedException>(() =>
            PluginObjectGraphGuard.EnsureHostOwnedGraph(ResponseWith(payloadType), "chat response"));
    }

    [Fact]
    public void OrdinaryHostResponseGraph_IsAccepted()
    {
        var response = ResponseWith(new Dictionary<string, object?>
        {
            ["count"] = 2,
            ["values"] = new[] { "one", "two" }
        });

        PluginObjectGraphGuard.EnsureHostOwnedGraph(response, "chat response");
    }

    [Fact]
    public async Task ChatClientRejectsNestedCollectibleStateForUnaryAndStreamingResponses()
    {
        var payloadType = CreateCollectibleType();
        var payload = Activator.CreateInstance(payloadType)!;
        var calls = new PluginCallGate();
        var invocation = new PluginInvocation("test.plugin", "generation-1", calls);
        var client = new PluginChatClient(new LeakingChatClient(payload), invocation);

        await Assert.ThrowsAsync<PluginContributionException>(() =>
            client.GetResponseAsync([]));
        await Assert.ThrowsAsync<PluginContributionException>(async () =>
        {
            await foreach (var _ in client.GetStreamingResponseAsync([]))
            {
            }
        });

        client.Dispose();
        Assert.Empty(await invocation.DisposeCapturedTargetsAsync());
    }

    private static ChatResponse ResponseWith(object value) =>
        new(new ChatMessage(ChatRole.Assistant, "ok"))
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["payload"] = value
            }
        };

    private static Type CreateCollectibleType()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"PluginPayload_{Guid.NewGuid():N}"),
            AssemblyBuilderAccess.RunAndCollect);
        var type = assembly
            .DefineDynamicModule("PluginPayload")
            .DefineType("PluginPayload", TypeAttributes.Public | TypeAttributes.Class)
            .CreateType()!;
        Assert.True(type.Assembly.IsCollectible);
        return type;
    }

    private sealed class LeakingChatClient(object payload) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ResponseWith(payload));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield return new ChatResponseUpdate(ChatRole.Assistant, "ok")
            {
                AdditionalProperties = new AdditionalPropertiesDictionary
                {
                    ["payload"] = payload
                }
            };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
