using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

/// <summary>One ordered change to the invocation's neutral conversation history.</summary>
public sealed record AgentHistoryUpdate(IReadOnlyList<ChatMessage> Messages, bool IsReplacement = false);

/// <summary>
/// Owns incremental history and completes its persistence barrier before execution continues.
/// </summary>
public interface IAgentHistoryObserver
{
    ValueTask OnHistoryChangedAsync(AgentHistoryUpdate update, CancellationToken cancellationToken);
}

internal sealed class AgentInvocationHistory(IList<ChatMessage> target, IAgentHistoryObserver? observer)
{
    private readonly List<ChatMessage>? _history = observer is null ? target.ToList() : null;
    public bool LoopObserved { get; set; }

    public async ValueTask AppendAsync(IEnumerable<ChatMessage> messages, CancellationToken ct)
    {
        var batch = messages.Select(message => message.Clone()).ToArray();
        if (batch.Length == 0) return;
        if (observer is not null)
            await observer.OnHistoryChangedAsync(new AgentHistoryUpdate(batch), ct);
        else
            _history!.AddRange(batch);
    }

    public async ValueTask ReplaceAsync(IReadOnlyList<ChatMessage> messages, CancellationToken ct)
    {
        var replacement = messages.Select(message => message.Clone()).ToArray();
        if (observer is not null)
            await observer.OnHistoryChangedAsync(new AgentHistoryUpdate(replacement, IsReplacement: true), ct);
        else
        {
            _history!.Clear();
            _history.AddRange(replacement);
        }
    }

    public void Commit()
    {
        if (_history is null) return;
        target.Clear();
        foreach (var message in _history) target.Add(message);
    }
}

internal static class AgentHistoryRuntimeScope
{
    private static readonly AsyncLocal<AgentInvocationHistory?> Ambient = new();
    public static AgentInvocationHistory? Current => Ambient.Value;
    public static IDisposable Set(AgentInvocationHistory history)
    {
        var previous = Ambient.Value;
        Ambient.Value = history;
        return new RestoreScope(() => Ambient.Value = previous);
    }

    public static ValueTask AppendAsync(IEnumerable<ChatMessage> messages, CancellationToken ct) =>
        Current?.AppendAsync(messages, ct) ?? ValueTask.CompletedTask;
}
