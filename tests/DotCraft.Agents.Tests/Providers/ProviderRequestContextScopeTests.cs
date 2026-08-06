using DotCraft.Agents;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed class ProviderRequestContextScopeTests
{
    [Fact]
    public void Push_NestsAndRestoresContexts()
    {
        var outer = Create("outer");
        var inner = Create("inner");

        Assert.Null(ProviderRequestContextScope.Current);
        using (ProviderRequestContextScope.Push(outer))
        {
            Assert.Same(outer, ProviderRequestContextScope.Current);
            using (ProviderRequestContextScope.Push(inner))
                Assert.Same(inner, ProviderRequestContextScope.Current);
            Assert.Same(outer, ProviderRequestContextScope.Current);
        }
        Assert.Null(ProviderRequestContextScope.Current);
    }

    [Fact]
    public async Task Push_FlowsAcrossAsyncContinuations()
    {
        var context = Create("thread");

        using (ProviderRequestContextScope.Push(context))
        {
            await Task.Yield();
            Assert.Same(context, ProviderRequestContextScope.Current);
        }
    }

    private static ProviderRequestContext Create(string threadId) => new(
        new ProviderConversationIdentity(
            threadId,
            threadId,
            null,
            null,
            "turn",
            "window",
            ProviderRequestKind.Turn,
            0,
            "test",
            null));
}
