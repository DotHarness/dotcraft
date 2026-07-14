using System.Collections.Concurrent;
using Microsoft.Extensions.AI;

namespace DotCraft.Protocol;

/// <summary>
/// Holds MCP App model context in memory until the next accepted turn. Values are intentionally
/// excluded from thread persistence and disappear on process restart.
/// </summary>
public sealed class McpAppTransientContextStore
{
    private readonly ConcurrentDictionary<string, PendingContext> _byView = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IReadOnlyList<AIContent>> _byQueuedInput = new(StringComparer.Ordinal);

    /// <summary>Replaces one live View's pending context.</summary>
    public void Set(string viewHandle, string threadId, IReadOnlyList<AIContent> content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewHandle);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentNullException.ThrowIfNull(content);
        _byView[viewHandle] = new PendingContext(threadId, content.ToArray(), DateTimeOffset.UtcNow);
    }

    /// <summary>Clears pending context for a View.</summary>
    public bool ClearView(string viewHandle) => _byView.TryRemove(viewHandle, out _);

    /// <summary>Consumes pending context for one View.</summary>
    public IReadOnlyList<AIContent> TakeForView(string viewHandle, string threadId)
    {
        if (!_byView.TryRemove(viewHandle, out var pending)
            || !string.Equals(pending.ThreadId, threadId, StringComparison.Ordinal))
        {
            return [];
        }

        return pending.Content;
    }

    /// <summary>Consumes all pending View context for a normal user turn.</summary>
    public IReadOnlyList<AIContent> TakeForThread(string threadId)
    {
        var selected = new List<PendingContext>();
        foreach (var (handle, pending) in _byView)
        {
            if (!string.Equals(pending.ThreadId, threadId, StringComparison.Ordinal))
                continue;
            if (_byView.TryRemove(handle, out var removed))
                selected.Add(removed);
        }

        return selected
            .OrderBy(static pending => pending.UpdatedAt)
            .SelectMany(static pending => pending.Content)
            .ToArray();
    }

    /// <summary>
    /// Consumes a View context and attaches it to an in-memory queued input sidecar.
    /// </summary>
    public void CaptureForQueuedInput(string viewHandle, string threadId, string queuedInputId)
    {
        var context = TakeForView(viewHandle, threadId);
        if (context.Count > 0)
            _byQueuedInput[queuedInputId] = context;
    }

    /// <summary>Consumes context previously attached to a queued input.</summary>
    public IReadOnlyList<AIContent> TakeForQueuedInput(string queuedInputId) =>
        _byQueuedInput.TryRemove(queuedInputId, out var context) ? context : [];

    /// <summary>Clears a queued input sidecar after removal or cancellation.</summary>
    public bool ClearQueuedInput(string queuedInputId) => _byQueuedInput.TryRemove(queuedInputId, out _);

    /// <summary>Clears all pending View context for a thread.</summary>
    public void ClearThread(string threadId)
    {
        foreach (var (handle, pending) in _byView)
        {
            if (string.Equals(pending.ThreadId, threadId, StringComparison.Ordinal))
                _byView.TryRemove(handle, out _);
        }
    }

    private sealed record PendingContext(
        string ThreadId,
        IReadOnlyList<AIContent> Content,
        DateTimeOffset UpdatedAt);
}
