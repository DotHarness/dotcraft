using DotCraft.Security.ShellCommands;

namespace DotCraft.Tools.BackgroundTerminals;

/// <summary>
/// Status values for a server-managed background terminal session.
/// </summary>
public static class BackgroundTerminalStatus
{
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Killed = "killed";
    public const string TimedOut = "timedOut";
    public const string Lost = "lost";
}

/// <summary>
/// Request used to start a background-terminal capable command.
/// </summary>
public sealed record BackgroundTerminalStartRequest
{
    public string ThreadId { get; init; } = "workspace";

    public string? TurnId { get; init; }

    public string? CallId { get; init; }

    public string Command { get; init; } = string.Empty;

    public string WorkingDirectory { get; init; } = string.Empty;

    public string Source { get; init; } = "host";

    public ShellIdentity? Shell { get; init; }

    public ShellStdinSession? StdinSession { get; init; }

    public bool RunInBackground { get; init; }

    public bool Interactive { get; init; }

    public int TimeoutSeconds { get; init; } = 300;

    public int YieldTimeMs { get; init; } = 1000;

    public int MaxOutputChars { get; init; } = 10000;
}

/// <summary>
/// Snapshot returned by terminal operations.
/// </summary>
public sealed record BackgroundTerminalSnapshot
{
    public string SessionId { get; init; } = string.Empty;

    public string ThreadId { get; init; } = string.Empty;

    public string? TurnId { get; init; }

    public string? CallId { get; init; }

    public string Command { get; init; } = string.Empty;

    public string WorkingDirectory { get; init; } = string.Empty;

    public string Source { get; init; } = "host";

    public string Status { get; init; } = BackgroundTerminalStatus.Running;

    public string Output { get; init; } = string.Empty;

    public string OutputPath { get; init; } = string.Empty;

    public int? ExitCode { get; init; }

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public long WallTimeMs { get; init; }

    public int OriginalOutputChars { get; init; }

    public bool Truncated { get; init; }

    public string? BackgroundReason { get; init; }
}

/// <summary>
/// Notification raised when a terminal lifecycle event occurs.
/// </summary>
public sealed record BackgroundTerminalEvent
{
    public string EventType { get; init; } = string.Empty;

    public required BackgroundTerminalSnapshot Terminal { get; init; }

    public string? Delta { get; init; }
}

/// <summary>
/// Service contract for server-managed background terminals.
/// </summary>
public interface IBackgroundTerminalService
{
    event Action<BackgroundTerminalEvent>? TerminalEvent;

    Task<BackgroundTerminalSnapshot> StartAsync(BackgroundTerminalStartRequest request, CancellationToken ct = default);

    Task<BackgroundTerminalSnapshot> ReadAsync(string sessionId, int waitMs = 0, int? maxOutputChars = null, CancellationToken ct = default);

    Task<BackgroundTerminalSnapshot> WriteStdinAsync(string sessionId, string input, int yieldTimeMs = 1000, int? maxOutputChars = null, CancellationToken ct = default);

    ShellStdinSession? GetStdinSession(string sessionId);

    Task<IReadOnlyList<BackgroundTerminalSnapshot>> ListAsync(string? threadId = null, CancellationToken ct = default);

    Task<BackgroundTerminalSnapshot> StopAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Stops active terminals for a thread without deleting persisted artifacts.
    /// </summary>
    Task<IReadOnlyList<BackgroundTerminalSnapshot>> CleanThreadAsync(string threadId, CancellationToken ct = default);

    /// <summary>
    /// Permanently removes all terminal artifacts for a thread after stopping active terminals.
    /// This operation is idempotent and best effort.
    /// </summary>
    Task<IReadOnlyList<string>> DeleteThreadArtifactsAsync(string threadId, CancellationToken ct = default);

    /// <summary>
    /// Removes completed terminal artifacts older than the configured retention period.
    /// Running terminals are never removed.
    /// </summary>
    Task<int> CleanupExpiredArtifactsAsync(CancellationToken ct = default);
}
