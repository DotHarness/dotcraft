using DotCraft.Agents;
using Microsoft.Extensions.AI;

namespace DotCraft.Context.Compaction;

internal sealed class ReactiveCompactionState
{
    public ProviderRequestContext? ProviderContext { get; set; }
    public ChatOptions? Options { get; set; }
    public PromptRequestSnapshot? Snapshot { get; set; }

    public IReadOnlyList<ChatMessage> GetHistory(IReadOnlyList<ChatMessage> fallback) =>
        Snapshot?.Messages ?? fallback;

    public IDisposable? RestoreProviderScope() => ProviderContext is null
        ? null
        : ProviderRequestContextScope.Push(ProviderContext);
}
