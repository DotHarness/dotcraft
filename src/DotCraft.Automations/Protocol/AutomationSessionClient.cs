using DotCraft.Workspaces;
using System.Security.Cryptography;
using System.Text;
using DotCraft.Sessions;

namespace DotCraft.Automations.Protocol;

/// <summary>
/// In-process wrapper over <see cref="ISessionService"/> for use by the automation runner.
/// </summary>
public sealed class AutomationSessionClient(ISessionService sessionService, DotCraftPaths paths) : IAutomationSessionClient, IAutomationWorktreeRetentionClient
{
    /// <summary>Host project workspace root (same as <see cref="SessionIdentity.WorkspacePath"/> for automations).</summary>
    public string ProjectWorkspacePath => paths.WorkspacePath;

    public string DataPath => paths.Data.RootPath;

    public string GetRunWorktreeBranchName(string runId) =>
        "dotcraft/automation-" + SanitizeRunIdForWorktree(runId);

    public string GetRunWorktreePath(string runId) =>
        paths.Data.Resolve("worktrees", "automation-" + SanitizeRunIdForWorktree(runId));

    /// <summary>
    /// Creates a fresh thread for one independent automation run.
    /// Configures <see cref="ThreadConfiguration"/> (workspace override, tool profile, approval policy).
    /// </summary>
    public async Task<string> CreateThreadAsync(
        string channelName,
        string userId,
        ThreadConfiguration config,
        CancellationToken ct,
        string? displayName = null)
    {
        var identity = new SessionIdentity
        {
            ChannelName = channelName,
            UserId = userId,
            WorkspacePath = paths.WorkspacePath
        };

        var thread = await sessionService.CreateThreadAsync(
            identity,
            config,
            displayName: displayName,
            ct: ct);
        return thread.Id;
    }

    public async Task<ThreadWorktreeInfo> EnsureRunWorktreeAsync(
        string threadId,
        string runId,
        CancellationToken ct)
    {
        var result = await sessionService.EnsureManagedWorktreeAsync(
            new WorktreeEnsureOptions
            {
                ThreadId = threadId,
                BranchName = GetRunWorktreeBranchName(runId),
                Path = GetRunWorktreePath(runId),
                BaseRef = "HEAD",
                OwnerKind = "automationRun",
                OwnerId = runId
            },
            ct);
        return result.Worktree;
    }

    public Task RemoveRunWorktreeAsync(
        string runId,
        string? threadId,
        CancellationToken ct) =>
        sessionService.RemoveManagedWorktreeAsync(
            new WorktreeRemoveOptions
            {
                ThreadId = threadId,
                WorkspacePath = paths.WorkspacePath,
                BranchName = GetRunWorktreeBranchName(runId),
                Path = GetRunWorktreePath(runId),
                DeleteBranch = true
            },
            ct);

    public Task<IReadOnlyList<ThreadWorktreeStatus>> ListManagedWorktreesAsync(CancellationToken ct) =>
        sessionService.ListWorktreesAsync(
            new SessionIdentity
            {
                WorkspacePath = paths.WorkspacePath
            },
            ct);

    /// <summary>
    /// Submits a turn and yields session events until the turn reaches a terminal state.
    /// Optionally annotates the synthesized user message with automation trigger metadata
    /// so clients can render a "Sent via automation" affordance.
    /// </summary>
    public async IAsyncEnumerable<SessionEvent> SubmitTurnAsync(
        string threadId,
        string message,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct,
        TurnTriggerInfo? trigger = null,
        Func<CancellationToken, Task>? beforeAdmission = null)
    {
        // The scope only needs to be active while SubmitInputAsync synchronously
        // builds the UserMessage item; we can release it before enumerating the
        // event stream.
        IAsyncEnumerable<SessionEvent> stream;
        while (true)
        {
            if (beforeAdmission != null) await beforeAdmission(ct);
            try
            {
                using var scope = trigger == null ? null : TurnTriggerScope.Set(trigger);
                stream = sessionService.SubmitInputAsync(threadId, message, sender: null, messages: null, ct);
                break;
            }
            catch (InvalidOperationException)
            {
                var current = await sessionService.GetThreadAsync(threadId, ct);
                if (!current.Turns.Any(t => t.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput)) throw;
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }

        await foreach (var evt in stream.WithCancellation(ct))
        {
            yield return evt;
            if (evt.EventType is SessionEventType.TurnCompleted
                or SessionEventType.TurnFailed
                or SessionEventType.TurnCancelled)
                yield break;
        }
    }

    /// <summary>
    /// Attempts to load a thread by id. Returns null when the thread does not exist or has been deleted,
    /// so the runner can mark runs whose binding target is gone as failed without throwing.
    /// </summary>
    public async Task<SessionThread?> TryGetThreadAsync(string threadId, CancellationToken ct)
    {
        try
        {
            var thread = await sessionService.GetThreadAsync(threadId, ct);
            if (thread.Status != ThreadStatus.Archived)
                await sessionService.EnsureThreadLoadedAsync(threadId, ct);
            return thread;
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Cancels the active turn on the thread, if any.
    /// </summary>
    public async Task InterruptAsync(string threadId, CancellationToken ct)
    {
        var thread = await sessionService.GetThreadAsync(threadId, ct);
        var running = thread.Turns.LastOrDefault(t =>
            t.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput);
        if (running != null)
            await sessionService.CancelTurnAsync(threadId, running.Id, ct);
    }

    private static string SanitizeRunIdForWorktree(string runId)
    {
        var value = string.IsNullOrWhiteSpace(runId) ? "run" : runId.Trim();
        var chars = new List<char>(value.Length);
        var previousDash = false;
        foreach (var ch in value.ToLowerInvariant())
        {
            var next = char.IsLetterOrDigit(ch) ? ch : '-';
            if (next == '-')
            {
                if (previousDash)
                    continue;
                previousDash = true;
            }
            else
            {
                previousDash = false;
            }

            chars.Add(next);
        }

        var slug = new string(chars.ToArray()).Trim('-');
        if (string.IsNullOrWhiteSpace(slug))
            slug = "run";

        var canonical = value.ToLowerInvariant();
        var changed = !string.Equals(slug, canonical, StringComparison.Ordinal);
        if (slug.Length > 48)
        {
            slug = slug[..48].TrimEnd('-');
            changed = true;
        }

        return changed ? AppendHash(slug, value) : slug;
    }

    private static string AppendHash(string slug, string value)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant()[..8];
        var prefixLength = Math.Min(slug.Length, Math.Max(1, 48 - hash.Length - 1));
        return $"{slug[..prefixLength].TrimEnd('-')}-{hash}";
    }
}
