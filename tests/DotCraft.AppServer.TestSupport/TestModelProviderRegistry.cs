using DotCraft.Agents;

namespace DotCraft.Tests;

public static class TestModelProviderRegistry
{
    public static ChatClientRegistry Create() => new(
        [new OpenAIClientProvider(), new AnthropicClientProvider()]);

    public static IDisposable PushRequestContext(
        string threadId,
        IProviderConversationHistory? history = null,
        IModelRuntimeDiagnostics? diagnostics = null)
    {
        var identity = new ProviderConversationIdentity(
            threadId,
            threadId,
            ParentThreadId: null,
            ForkedFromThreadId: null,
            TurnId: "turn-test",
            ContextWindowId: threadId,
            ProviderRequestKind.Turn,
            TurnStartedAtUnixMs: 1,
            ThreadSource: "test",
            SubagentKind: null);
        return ProviderRequestContextScope.Push(new ProviderRequestContext(
            identity,
            history,
            history as IProviderCompactionBridge,
            diagnostics,
            new ProviderConversationState(identity)));
    }
}
