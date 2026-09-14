namespace DotCraft.Agents;

internal sealed class AuxiliaryProviderRequestScope : IDisposable
{
    private readonly IDisposable[] _scopes;

    public AuxiliaryProviderRequestScope(
        ProviderConversationIdentity identity,
        IModelRuntimeDiagnostics? diagnostics = null)
    {
        if (identity.RequestKind == ProviderRequestKind.Turn)
            throw new ArgumentException("An auxiliary request cannot use the Turn request kind.", nameof(identity));

        _scopes =
        [
            ProviderRequestContextScope.Push(new ProviderRequestContext(
                identity, Diagnostics: diagnostics, ConversationState: new ProviderConversationState(identity))),
            ModelStreamAttemptRuntimeScope.Suppress(),
            AgentHistoryRuntimeScope.Suppress(),
            StreamingSamplingRuntimeScope.Suppress(),
            StreamingGuidanceRuntimeScope.Suppress(),
            StreamingToolInvocationRuntimeScope.Suppress()
        ];
    }

    public void Dispose()
    {
        for (var index = _scopes.Length - 1; index >= 0; index--)
            _scopes[index].Dispose();
    }
}
