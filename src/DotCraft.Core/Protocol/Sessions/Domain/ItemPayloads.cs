using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DotCraft.Plugins;

namespace DotCraft.Protocol;

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
    /// <c>"memoryConsolidated"</c>, and <c>"forked"</c>. Leaving this as a string
    /// keeps future kinds additive without rev'ing the wire protocol.
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
