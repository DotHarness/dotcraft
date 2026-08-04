using SessionTurn = DotCraft.Sessions.SessionTurn;
using ThreadSource = DotCraft.Sessions.ThreadSource;
namespace DotCraft.Sessions;

/// <summary>
/// A Thread is a persistent conversation between one user and one agent, tied to a workspace.
/// </summary>
public sealed class SessionThread
{
    /// <summary>
    /// Globally unique identifier. Format: thread_{yyyyMMdd}_{6-char-random}.
    /// Assigned by Session Core on creation. Immutable after creation.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Absolute path to the workspace this Thread belongs to.
    /// </summary>
    public string WorkspacePath { get; set; } = string.Empty;

    /// <summary>
    /// Opaque user identifier from the originating channel. Null for system-initiated threads.
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Name of the channel that created this Thread (e.g., "qq", "acp", "cli").
    /// Informational only; does not restrict which channels can resume the Thread.
    /// </summary>
    public string OriginChannel { get; set; } = string.Empty;

    /// <summary>
    /// Channel-specific context key stored on creation (e.g., "group:123456" or "user:789" for QQ,
    /// "chat:abc" for WeCom). Null for channels that have no sub-context (CLI, ACP).
    /// Used by FindThreadsAsync to isolate threads per context.
    /// </summary>
    public string? ChannelContext { get; set; }

    /// <summary>
    /// Human-readable label. Defaults to the first user message text (truncated).
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Describes whether this is a top-level user thread or a subagent child thread.
    /// </summary>
    public ThreadSource Source { get; set; } = ThreadSource.User();

    /// <summary>
    /// Source thread id when this thread was created by forking another thread.
    /// Null for ordinary top-level threads and subagent child threads.
    /// </summary>
    public string? ForkedFromId { get; set; }

    /// <summary>
    /// True when the thread is process-local and must not be persisted or indexed.
    /// Used for transient handoff flows that have not been promoted to durable state.
    /// </summary>
    public bool Ephemeral { get; set; }

    /// <summary>
    /// Metadata for a Git worktree managed by DotCraft and bound to this thread.
    /// Null for ordinary local-workspace threads.
    /// </summary>
    public ThreadWorktreeInfo? Worktree { get; set; }

    public ThreadStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Updated when a Turn starts or completes.
    /// </summary>
    public DateTimeOffset LastActiveAt { get; set; }

    /// <summary>
    /// Extensible key-value pairs for channel-specific data.
    /// Session Core preserves but does not interpret Metadata.
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = [];

    /// <summary>
    /// Server: Session Core manages conversation history (default).
    /// Client: The adapter provides message history with each SubmitInput call.
    /// </summary>
    public HistoryMode HistoryMode { get; set; } = HistoryMode.Server;

    /// <summary>
    /// Per-thread agent configuration. New server-managed threads capture workspace defaults at creation time.
    /// Null is only expected for older persisted threads or externally constructed test fixtures.
    /// </summary>
    public ThreadConfiguration? Configuration { get; set; }

    /// <summary>
    /// Ordered list of Turns. Append-only.
    /// </summary>
    public List<SessionTurn> Turns { get; set; } = [];

    /// <summary>
    /// Highest Turn sequence ever allocated in this Thread, including Turns later removed by rollback.
    /// Runtime-only; rollout replay reconstructs it from turn_started records.
    /// </summary>
    internal int TurnSequenceHighWatermark { get; set; }

    /// <summary>
    /// Required rollout schema version for protocol-native provider history.
    /// </summary>
    internal int ProviderHistorySchemaVersion { get; set; }

    /// <summary>
    /// FIFO user inputs waiting for the current running turn to complete.
    /// </summary>
    public List<QueuedTurnInput> QueuedInputs { get; set; } = [];
}
