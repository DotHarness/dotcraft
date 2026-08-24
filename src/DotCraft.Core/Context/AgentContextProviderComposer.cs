using DotCraft.Agents;
using DotCraft.Contributions;
using DotCraft.Tracing;
using Microsoft.Extensions.Logging;

namespace DotCraft.Context;

/// <summary>Builds the ordered <see cref="AIContextProvider"/> list one agent runs with, from the pre-send context contribution point alone.</summary>
internal static class AgentContextProviderComposer
{
    /// <summary>Composes the contributed providers in resolved order; a contribution that throws or declines costs only its own provider.</summary>
    /// <returns>The provider list, always containing exactly one provider for the <c>memory</c> target.</returns>
    /// <remarks>
    /// The memory target is the system prompt, so an unfilled slot — never registered, or replaced by a
    /// contribution that declined — falls back to the kernel's own provider rather than shipping an agent with no prompt.
    /// </remarks>
    internal static IReadOnlyList<AIContextProvider> Compose(
        IContributionView? contributions,
        AgentContextRequest request,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        var entries = contributions?.ResolveEntries<IAgentContextSource>(request.ThreadId) ?? [];
        var providers = new List<AIContextProvider>(entries.Count + 2);
        var memorySlot = -1;
        var memoryFilled = false;

        foreach (var entry in entries)
        {
            var occupiesMemory = entry.Occupies(AgentContextSourceNames.Memory);
            if (memorySlot < 0
                && (occupiesMemory || entry.Order > AgentContextSourceCatalog.MemoryOrder))
            {
                memorySlot = providers.Count;
            }

            AIContextProvider? provider;
            try
            {
                provider = entry.Contribution.CreateProvider(request);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(
                    ex,
                    "Agent context contribution {ContributionType} failed to create a provider for thread {ThreadId}; skipping it.",
                    entry.Contribution.GetType().FullName,
                    request.ThreadId ?? "(host)");
                continue;
            }

            if (provider is null)
                continue;

            memoryFilled |= occupiesMemory;
            providers.Add(provider);
        }

        if (!memoryFilled)
            providers.Insert(memorySlot < 0 ? providers.Count : memorySlot, request.RequireBuiltInProvider()());

        if (request.TraceCollector is { } traceCollector)
        {
            providers.Add(new SessionMetadataRecordingContextProvider(
                traceCollector,
                request.PromptInputs?.ToolNames ?? []));
        }

        return providers;
    }

    /// <summary>Records the prompt-cache baseline against the instructions every provider assembled, not just the built-in's.</summary>
    private sealed class SessionMetadataRecordingContextProvider(
        TraceCollector traceCollector,
        IReadOnlyList<string> toolNames) : AIContextProvider
    {
        protected override ValueTask<AIContext> InvokingCoreAsync(
            InvokingContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sessionKey = TracingChatClient.CurrentSessionKey ?? TracingChatClient.GetActiveSessionKey();
            if (!string.IsNullOrWhiteSpace(sessionKey))
                traceCollector.RecordSessionMetadata(sessionKey, context.AIContext.Instructions, toolNames);

            return ValueTask.FromResult(context.AIContext);
        }
    }
}
