namespace DotCraft.Sessions;

public sealed record ThreadListQuery
{
    public required SessionIdentity Identity { get; init; }
    public ThreadDiscoveryScope Scope { get; init; } = ThreadDiscoveryScope.Identity;
    public bool IncludeArchived { get; init; }
    public bool IncludeSubAgents { get; init; }
    public bool IncludeInternal { get; init; }
    public IReadOnlyList<string>? CrossChannelOrigins { get; init; }
    public string? ChannelName { get; init; }
    public string? Query { get; init; }
    public ThreadListCursor? Cursor { get; init; }
    public int? Limit { get; init; }
}

public sealed record ThreadListCursor(DateTimeOffset LastActiveAt, string ThreadId);

public sealed record ThreadListPage(IReadOnlyList<ThreadSummary> Items, int TotalMatched, bool HasMore);
