using DotCraft.Agents;
using DotCraft.Configuration;

namespace DotCraft.Tests.Agents;

/// <summary>Supplies the request-scoped policy and diagnostics the Anthropic cache client reads.</summary>
internal static class PromptCachePolicyScope
{
    internal static IDisposable Use(bool enabled = true, string? ttl = null) =>
        ProviderPipelineOptionsScope.Push(new ProviderPipelineOptions(
            new EffectiveModelRuntime(
                "anthropic",
                "claude-opus-4-1",
                ModelProviderProtocols.Anthropic,
                "anthropic",
                string.Empty,
                string.Empty,
                60,
                null,
                ModelProviderCapabilities.ForProtocol(ModelProviderProtocols.Anthropic)),
            null,
            null,
            false,
            "Standard",
            enabled,
            ttl));

    internal static IDisposable UseDiagnostics(IModelRuntimeDiagnostics diagnostics) =>
        ProviderRequestContextScope.Push(new ProviderRequestContext(
            new ProviderConversationIdentity(
                "thread", "thread", null, null, null, "run", ProviderRequestKind.Turn, 0, "test", null),
            Diagnostics: diagnostics));
}
