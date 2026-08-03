using System.Collections.Concurrent;
using DotCraft.Mcp;
using DotCraft.Tools;

namespace DotCraft.AppServer;

/// <summary>
/// Owns opaque MCP App View capabilities for one AppServer connection. No caller-supplied
/// server, generation, definition, or binding identity is trusted after a handle is issued.
/// </summary>
internal sealed class McpAppViewRegistry : IDisposable
{
    internal const int MaxViewsPerThread = 8;
    internal const int MaxViewsPerConnection = 32;
    internal const int MaxConcurrentToolCalls = 4;
    internal const int MaxRequestsPerMinute = 60;

    private readonly ConcurrentDictionary<string, McpAppViewState> _views = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private int _disposed;

    public McpAppViewState Add(McpAppViewState state)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_views.Count >= MaxViewsPerConnection
                || _views.Values.Count(view => string.Equals(view.ThreadId, state.ThreadId, StringComparison.Ordinal)) >= MaxViewsPerThread)
                throw McpAppViewErrors.Create("limit_exceeded", "The active MCP App View limit was reached.");
            if (!_views.TryAdd(state.Handle, state))
                throw McpAppViewErrors.Create("protocol_error", "An MCP App View handle could not be created.");
            return state;
        }
    }

    public McpAppViewState Get(string handle)
    {
        if (string.IsNullOrWhiteSpace(handle) || !_views.TryGetValue(handle, out var state))
            throw McpAppViewErrors.Create("stale", "The MCP App View is no longer available.");
        return state;
    }

    public bool Close(string handle, out McpAppViewState? state)
    {
        lock (_gate)
            return _views.TryRemove(handle, out state);
    }

    public IReadOnlyList<McpAppViewState> Snapshot() => _views.Values.ToArray();

    public void Dispose()
    {
        lock (_gate)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            foreach (var view in _views.Values)
                view.Dispose();
            _views.Clear();
        }
    }
}

internal sealed class McpAppViewState : IDisposable
{
    private readonly object _rateLock = new();
    private readonly Queue<DateTimeOffset> _toolCalls = new();
    private readonly Queue<DateTimeOffset> _messages = new();
    private int _disposed;

    public required string Handle { get; init; }
    public required string ThreadId { get; init; }
    public required string TurnId { get; init; }
    public required string SourceItemId { get; init; }
    public required string ServerName { get; init; }
    public required string Origin { get; init; }
    public required long Generation { get; init; }
    public required ToolName ToolName { get; init; }
    public required ToolDefinitionId DefinitionId { get; init; }
    public required RuntimeBindingId RuntimeBindingId { get; init; }
    public required long SnapshotRevision { get; init; }
    public required long BindingRevision { get; init; }
    public required string RawSourceToolId { get; init; }
    public required Uri ResourceUri { get; init; }
    public required McpClientManager Manager { get; init; }
    public SemaphoreSlim ToolCallSlots { get; } = new(McpAppViewRegistry.MaxConcurrentToolCalls);

    public void CheckToolRate() => CheckRate(_toolCalls, "tool_call_rate_limited");
    public void CheckMessageRate() => CheckRate(_messages, "message_rate_limited");

    private void CheckRate(Queue<DateTimeOffset> samples, string code)
    {
        lock (_rateLock)
        {
            var now = DateTimeOffset.UtcNow;
            while (samples.TryPeek(out var sample) && now - sample >= TimeSpan.FromMinutes(1))
                samples.Dequeue();
            if (samples.Count >= McpAppViewRegistry.MaxRequestsPerMinute)
                throw McpAppViewErrors.Create(code, "The MCP App View request rate limit was reached.");
            samples.Enqueue(now);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        ToolCallSlots.Dispose();
    }
}

internal static class McpAppViewErrors
{
    public static AppServerException Create(string code, string fallbackText, string? detail = null) =>
        new(AppServerErrors.InvalidParamsCode, fallbackText, new AppServerErrorData
        {
            Code = code,
            MessageKey = $"errors.mcpApp.{code}",
            FallbackText = fallbackText,
            Detail = detail
        });
}
