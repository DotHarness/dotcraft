using DotCraft.Sessions;

namespace DotCraft.Automations.Protocol;

/// <summary>Session operations required by the automation runner.</summary>
public interface IAutomationSessionClient
{
    string DataPath { get; }
    Task<string> CreateThreadAsync(string channelName, string userId, ThreadConfiguration config, CancellationToken ct, string? displayName = null);
    Task<ThreadWorktreeInfo> EnsureRunWorktreeAsync(string threadId, string runId, CancellationToken ct);
    Task<SessionThread?> TryGetThreadAsync(string threadId, CancellationToken ct);
    IAsyncEnumerable<SessionEvent> SubmitTurnAsync(string threadId, string message, CancellationToken ct, TurnTriggerInfo? trigger = null,
        Func<CancellationToken, Task>? beforeAdmission = null);
}

/// <summary>Optional conservative cleanup of managed run worktrees.</summary>
public interface IAutomationWorktreeRetentionClient
{
    Task<IReadOnlyList<ThreadWorktreeStatus>> ListManagedWorktreesAsync(CancellationToken ct);
    Task RemoveRunWorktreeAsync(string runId, string? threadId, CancellationToken ct);
}

