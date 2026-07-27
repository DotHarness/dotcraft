using Microsoft.Extensions.AI;

namespace DotCraft.Protocol;

/// <summary>
/// Holds MCP App model context in memory until the next accepted turn. Values are intentionally
/// excluded from thread persistence and disappear on process restart.
/// </summary>
public sealed class McpAppTransientContextStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, PendingContext> _byView = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<AIContent>> _byQueuedInput = new(StringComparer.Ordinal);

    /// <summary>Replaces one live View's pending context.</summary>
    public void Set(string viewHandle, string threadId, IReadOnlyList<AIContent> content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewHandle);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentNullException.ThrowIfNull(content);
        lock (_gate)
            _byView[viewHandle] = new PendingContext(threadId, content.ToArray(), DateTimeOffset.UtcNow);
    }

    /// <summary>Clears pending context for a View.</summary>
    public bool ClearView(string viewHandle)
    {
        lock (_gate)
            return _byView.Remove(viewHandle);
    }

    /// <summary>Consumes pending context for one View.</summary>
    public IReadOnlyList<AIContent> TakeForView(string viewHandle, string threadId)
    {
        lock (_gate)
        {
            if (!_byView.TryGetValue(viewHandle, out var pending)
                || !string.Equals(pending.ThreadId, threadId, StringComparison.Ordinal))
            {
                return [];
            }

            _byView.Remove(viewHandle);
            return pending.Content;
        }
    }

    /// <summary>Consumes all pending View context for a normal user turn.</summary>
    public IReadOnlyList<AIContent> TakeForThread(string threadId)
    {
        lock (_gate)
        {
            var selected = _byView
                .Where(entry => string.Equals(entry.Value.ThreadId, threadId, StringComparison.Ordinal))
                .ToArray();
            foreach (var (handle, _) in selected)
                _byView.Remove(handle);

            return selected
                .Select(static entry => entry.Value)
                .OrderBy(static pending => pending.UpdatedAt)
                .SelectMany(static pending => pending.Content)
                .ToArray();
        }
    }

    /// <summary>
    /// Consumes a View context and attaches it to an in-memory queued input sidecar.
    /// </summary>
    public void CaptureForQueuedInput(string viewHandle, string threadId, string queuedInputId)
    {
        lock (_gate)
        {
            if (!_byView.TryGetValue(viewHandle, out var pending)
                || !string.Equals(pending.ThreadId, threadId, StringComparison.Ordinal))
            {
                return;
            }

            _byView.Remove(viewHandle);
            if (pending.Content.Count > 0)
                _byQueuedInput[queuedInputId] = pending.Content;
        }
    }

    /// <summary>Consumes context previously attached to a queued input.</summary>
    public IReadOnlyList<AIContent> TakeForQueuedInput(string queuedInputId)
    {
        lock (_gate)
        {
            if (!_byQueuedInput.Remove(queuedInputId, out var context))
                return [];
            return context;
        }
    }

    /// <summary>Clears a queued input sidecar after removal or cancellation.</summary>
    public bool ClearQueuedInput(string queuedInputId)
    {
        lock (_gate)
            return _byQueuedInput.Remove(queuedInputId);
    }

    /// <summary>Clears all pending View context for a thread.</summary>
    public void ClearThread(string threadId)
    {
        lock (_gate)
        {
            var handles = _byView
                .Where(entry => string.Equals(entry.Value.ThreadId, threadId, StringComparison.Ordinal))
                .Select(static entry => entry.Key)
                .ToArray();
            foreach (var handle in handles)
                _byView.Remove(handle);
        }
    }

    private sealed record PendingContext(
        string ThreadId,
        IReadOnlyList<AIContent> Content,
        DateTimeOffset UpdatedAt);
}
