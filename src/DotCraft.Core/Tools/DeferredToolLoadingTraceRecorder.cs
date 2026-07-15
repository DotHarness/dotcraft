using DotCraft.Tracing;

namespace DotCraft.Tools;

internal static class DeferredToolLoadingTraceRecorder
{
    public const string OpenAIResponsesToolSearchOutputWireShape = "openai_responses_tool_search_output";
    public const string AnthropicToolReferenceWireShape = "anthropic_tool_reference";

    public static void RecordNewActivations(
        DeferredToolLoadingTraceContext? traceContext,
        string query,
        int requestedMaxResults,
        IReadOnlyList<DeferredToolEntry> entries,
        IReadOnlySet<string>? activatedBefore,
        string wireShape)
    {
        if (traceContext == null || activatedBefore == null || entries.Count == 0)
            return;

        var newTools = entries
            .Where(entry => !activatedBefore.Contains(
                DeferredToolActivationIndex.GetIdentityKey(entry)))
            .Select(static entry => new DeferredToolLoadingTraceTool(
                CanonicalToolIdentityMetadataResolver.TryGet(entry.Tool, out var canonicalName, out _)
                    ? canonicalName.Name
                    : entry.Tool.Name,
                entry.Source,
                entry.Namespace))
            .ToArray();
        if (newTools.Length == 0)
            return;

        var sessionKey = TracingChatClient.CurrentSessionKey ?? TracingChatClient.GetActiveSessionKey();
        if (string.IsNullOrWhiteSpace(sessionKey))
            return;

        traceContext.Collector.RecordDeferredToolLoading(
            sessionKey,
            newTools,
            traceContext.Strategy,
            traceContext.EffectiveMode,
            traceContext.ProviderProtocol,
            traceContext.Trigger,
            query,
            traceContext.DeferredToolCount,
            requestedMaxResults,
            traceContext.MaxSearchResults,
            wireShape);
    }
}
