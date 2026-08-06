using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Providers.OpenAI;

public sealed class OpenAIResponsesProviderHistoryIdentityTests
{
    [Fact]
    public void CaptureOpaqueSnapshot_PreservesProviderAndConversationIdentity()
    {
        var context = new OpenAIResponsesProviderHistoryContext(
            new ProviderConversationIdentity(
                CurrentThreadId: "thread-history",
                RootThreadId: "thread-root",
                ParentThreadId: null,
                ForkedFromThreadId: null,
                TurnId: "turn-history",
                ContextWindowId: "window-history",
                RequestKind: ProviderRequestKind.Turn,
                TurnStartedAtUnixMs: 1,
                ThreadSource: "test",
                SubagentKind: null),
            providerId: "provider-history",
            ProviderHistorySnapshot.Empty("window-history"),
            coveredMessages: [],
            appendAsync: null,
            replaceAsync: null,
            abortAsync: null);

        var snapshot = context.CaptureOpaqueSnapshot();

        Assert.Equal("provider-history", snapshot.Identity.ProviderId);
        Assert.Equal(ModelProviderProtocols.OpenAIResponses, snapshot.Identity.Protocol);
        Assert.Equal("thread-history", snapshot.Identity.ThreadId);
        Assert.Equal("turn-history", snapshot.Identity.TurnId);
        Assert.Equal("window-history", snapshot.Identity.GenerationId);
        Assert.Equal("window-history", snapshot.Identity.ContextWindowId);
    }
}
