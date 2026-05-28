namespace DotCraft.Protocol;

/// <summary>
/// Result payload for a server-side conversation reset (for example <c>/new</c>).
/// </summary>
public sealed record ThreadResetResult
{
    /// <summary>
    /// Newly created thread bound to the caller identity.
    /// </summary>
    public required SessionThread Thread { get; init; }

    /// <summary>
    /// Threads archived as part of the reset operation.
    /// </summary>
    public IReadOnlyList<string> ArchivedThreadIds { get; init; } = [];

    /// <summary>
    /// Whether the new thread metadata is created lazily and has not been materialized to disk yet.
    /// </summary>
    public bool CreatedLazily { get; init; } = true;
}
