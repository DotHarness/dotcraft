namespace DotCraft.ContextExport;

/// <summary>
/// Controls the Markdown shape emitted by the context export command.
/// </summary>
public enum ContextExportProfile
{
    /// <summary>
    /// A compact handoff document intended for another coding agent.
    /// </summary>
    Handoff,

    /// <summary>
    /// A fuller chronological transcript intended for audit or debugging.
    /// </summary>
    Transcript
}

/// <summary>
/// Controls whether persisted tool and command results are included in exports.
/// </summary>
public enum ContextExportToolResultMode
{
    /// <summary>
    /// Omit tool and command results while preserving call metadata.
    /// </summary>
    None,

    /// <summary>
    /// Include short bounded previews of tool and command results.
    /// </summary>
    Summary,

    /// <summary>
    /// Include complete persisted tool and command results.
    /// </summary>
    Full
}

/// <summary>
/// Controls how workspace memory history is included in exports.
/// </summary>
public enum ContextExportHistoryMode
{
    /// <summary>
    /// Omit <c>HISTORY.md</c>.
    /// </summary>
    None,

    /// <summary>
    /// Include the tail of <c>HISTORY.md</c>.
    /// </summary>
    Tail,

    /// <summary>
    /// Include the full <c>HISTORY.md</c>.
    /// </summary>
    Full
}

/// <summary>
/// Filters context search results by thread status.
/// </summary>
public enum ContextSearchStatusFilter
{
    /// <summary>
    /// Include non-archived threads.
    /// </summary>
    Active,

    /// <summary>
    /// Include archived threads only.
    /// </summary>
    Archived,

    /// <summary>
    /// Include active, paused, archived, and unbound trace sessions.
    /// </summary>
    All
}

/// <summary>
/// Options for exporting a DotCraft session, memory, and continuity context.
/// </summary>
public sealed class ContextExportOptions
{
    /// <summary>
    /// The thread id to export.
    /// </summary>
    public string ThreadId { get; init; } = string.Empty;

    /// <summary>
    /// Workspace path, or a direct path to the workspace <c>.craft</c> directory.
    /// Defaults to the current directory when unset.
    /// </summary>
    public string? WorkspacePath { get; init; }

    /// <summary>
    /// Export profile. Defaults to <see cref="ContextExportProfile.Handoff"/>.
    /// </summary>
    public ContextExportProfile Profile { get; init; } = ContextExportProfile.Handoff;

    /// <summary>
    /// Tool result inclusion mode. Defaults to <see cref="ContextExportToolResultMode.Summary"/>.
    /// </summary>
    public ContextExportToolResultMode ToolResults { get; init; } = ContextExportToolResultMode.Summary;

    /// <summary>
    /// Memory history inclusion mode. Defaults to <see cref="ContextExportHistoryMode.Tail"/>.
    /// </summary>
    public ContextExportHistoryMode History { get; init; } = ContextExportHistoryMode.Tail;

    /// <summary>
    /// Maximum characters included from the tail of <c>HISTORY.md</c>.
    /// </summary>
    public int HistoryTailChars { get; init; } = 12_000;

    /// <summary>
    /// Maximum characters included in summary previews.
    /// </summary>
    public int ToolResultPreviewChars { get; init; } = 800;
}

/// <summary>
/// Result of exporting context to Markdown.
/// </summary>
public sealed class ContextExportResult
{
    /// <summary>
    /// Exported thread id.
    /// </summary>
    public string ThreadId { get; init; } = string.Empty;

    /// <summary>
    /// Markdown document.
    /// </summary>
    public string Markdown { get; init; } = string.Empty;

    /// <summary>
    /// Canonical rollout JSONL path used for the export, when found.
    /// </summary>
    public string? RolloutPath { get; init; }

    /// <summary>
    /// Non-fatal recovery or data-quality warnings encountered during export.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// Options for searching DotCraft session and trace context.
/// </summary>
public sealed class ContextSearchOptions
{
    /// <summary>
    /// Workspace path, or a direct path to the workspace <c>.craft</c> directory.
    /// Defaults to the current directory when unset.
    /// </summary>
    public string? WorkspacePath { get; init; }

    /// <summary>
    /// Free-text query used to search thread metadata, trace events, and rollout snippets.
    /// </summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>
    /// Maximum number of hits returned. Defaults to 10.
    /// </summary>
    public int Limit { get; init; } = 10;

    /// <summary>
    /// Thread status filter. Defaults to <see cref="ContextSearchStatusFilter.All"/>.
    /// </summary>
    public ContextSearchStatusFilter Status { get; init; } = ContextSearchStatusFilter.All;

    /// <summary>
    /// Maximum characters included in each evidence preview.
    /// </summary>
    public int PreviewChars { get; init; } = 320;
}

/// <summary>
/// Result of searching context evidence.
/// </summary>
public sealed class ContextSearchResult
{
    /// <summary>
    /// Query text used for the search.
    /// </summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>
    /// Matching context hits ordered by score.
    /// </summary>
    public IReadOnlyList<ContextSearchHit> Hits { get; init; } = [];

    /// <summary>
    /// Non-fatal recovery or data-quality warnings encountered during search.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// One matching thread or trace session returned by context search.
/// </summary>
public sealed class ContextSearchHit
{
    /// <summary>
    /// Thread id when bound to a thread; otherwise the unbound trace session key.
    /// </summary>
    public string ThreadId { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable thread label when available.
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Thread status, or <c>unbound</c> for trace sessions without a thread binding.
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// Most recent activity timestamp when known.
    /// </summary>
    public DateTimeOffset? LastActiveAt { get; init; }

    /// <summary>
    /// Aggregate relevance score.
    /// </summary>
    public double Score { get; init; }

    /// <summary>
    /// Canonical rollout JSONL path when the hit is exportable.
    /// </summary>
    public string? RolloutPath { get; init; }

    /// <summary>
    /// Convenience command for exporting this hit when a rollout is available.
    /// </summary>
    public string? ExportCommand { get; init; }

    /// <summary>
    /// Short evidence previews explaining why the hit matched.
    /// </summary>
    public IReadOnlyList<ContextSearchEvidence> Evidence { get; init; } = [];
}

/// <summary>
/// One short evidence preview associated with a context search hit.
/// </summary>
public sealed class ContextSearchEvidence
{
    /// <summary>
    /// Evidence source, such as <c>threads</c>, <c>trace_events</c>, or <c>rollout</c>.
    /// </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// Source-specific identifier, such as an event id, session key, or rollout line number.
    /// </summary>
    public string SourceId { get; init; } = string.Empty;

    /// <summary>
    /// Timestamp of the evidence when known.
    /// </summary>
    public DateTimeOffset? Timestamp { get; init; }

    /// <summary>
    /// Bounded text preview for the matching evidence.
    /// </summary>
    public string Preview { get; init; } = string.Empty;
}
