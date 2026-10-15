using System.Text.RegularExpressions;
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

    public int? YieldTimeMs { get; init; }

    public int MaxOutputChars { get; init; } = 10000;

    /// <summary>Signalled when a waiting foreground command should continue in the background at once.</summary>
    public CancellationToken HandOverToken { get; init; }

    /// <summary>Raise a completion notice if the model is handed this terminal while it still runs.</summary>
    public bool NotifyOnCompletion { get; init; }

    public Regex? WaitFor { get; init; }
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

    /// <summary>Output starts after earlier unread output that is now only in the log.</summary>
    public bool Lossy { get; init; }

    /// <summary>The model will receive a completion notice when this running terminal exits.</summary>
    public bool CompletionNoticeArmed { get; init; }

    public string? MatchedLine { get; init; }

    public bool WaitingForInput { get; init; }

    public bool Interactive { get; init; }
}

public static class BackgroundTerminalEventTypes
{
    /// <summary>A terminal handed to the model while running has exited without the model seeing it.</summary>
    public const string CompletionNotice = "completionNotice";

    /// <summary>A terminal handed to the model appears to be waiting for input and no model call is waiting on it.</summary>
    public const string StallNotice = "stallNotice";

    public const string Stalled = "stalled";
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

    /// <summary>
    /// Model-facing exchange: writes optional input, waits, and returns only output after the model's
    /// read cursor. Input of only end-of-text and line endings stops the terminal instead. A match of
    /// <paramref name="waitFor"/> in unread output ends the wait early.
    /// </summary>
    Task<BackgroundTerminalSnapshot> InteractAsync(string sessionId, string input, int waitMs, int? maxOutputChars = null, Regex? waitFor = null, CancellationToken ct = default);

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
