namespace DotCraft.Sessions;

/// <summary>
/// Payload for SystemNotice items. Used to mark maintenance and lifecycle
/// events such as context compaction, long-term memory consolidation, and
/// fork boundaries so clients can render persistent dividers in the
/// conversation timeline.
/// </summary>
public sealed record SystemNoticePayload
{
    /// <summary>
    /// Notice classifier. Known values include <c>"compacted"</c>,
    /// <c>"memoryConsolidated"</c>, <c>"forked"</c>, and <c>"remoteRoute"</c>. Leaving this as a
    /// string keeps future kinds additive without rev'ing the wire protocol.
    /// </summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>
    /// How compaction was triggered. One of: <c>"auto"</c>, <c>"reactive"</c>,
    /// <c>"manual"</c>.
    /// </summary>
    public string Trigger { get; init; } = string.Empty;

    /// <summary>
    /// Which compaction mode actually ran. Current persisted notices use
    /// <c>"partial"</c>; older histories may contain <c>"micro"</c>.
    /// </summary>
    public string Mode { get; init; } = string.Empty;

    /// <summary>
    /// Approximate input token count right before compaction ran.
    /// </summary>
    public long TokensBefore { get; init; }

    /// <summary>
    /// Approximate input token count after compaction ran.
    /// </summary>
    public long TokensAfter { get; init; }

    /// <summary>
    /// Percent of the effective context window still available after compaction (0.0 - 1.0).
    /// </summary>
    public double PercentLeftAfter { get; init; }

    /// <summary>
    /// Number of tool results cleared before summary compaction (0 when only
    /// the partial summary ran).
    /// </summary>
    public int ClearedToolResults { get; init; }

    /// <summary>
    /// Source thread id for fork boundary notices.
    /// </summary>
    public string? SourceThreadId { get; init; }

    /// <summary>
    /// Why the notice was raised. <c>"remoteRoute"</c> notices use <c>"connected"</c>,
    /// <c>"disconnected"</c>, or <c>"leaseLost"</c>.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Who caused the change. <c>"remoteRoute"</c> notices use <c>"client"</c>, <c>"agent"</c>, or
    /// <c>"system"</c>.
    /// </summary>
    public string? Initiator { get; init; }

    /// <summary>
    /// Remote Tool Host id for <c>"remoteRoute"</c> notices.
    /// </summary>
    public string? HostId { get; init; }

    /// <summary>
    /// Remote Tool Host display name for <c>"remoteRoute"</c> notices, when one is known.
    /// </summary>
    public string? HostName { get; init; }

    /// <summary>
    /// Remote workspace id for <c>"remoteRoute"</c> notices.
    /// </summary>
    public string? WorkspaceId { get; init; }

    /// <summary>
    /// Remote workspace display name for <c>"remoteRoute"</c> notices, when one is known.
    /// </summary>
    public string? WorkspaceName { get; init; }
}

/// <summary>
/// Payload for Error items.
/// </summary>
public sealed record ErrorPayload
{
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Machine-readable error code (e.g., "agent_error", "timeout").
    /// </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>
    /// Whether this error terminates the Turn.
    /// </summary>
    public bool Fatal { get; init; }
}
