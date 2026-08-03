using System.Security.Cryptography;
using System.Text;
using DotCraft.Hosting;
using DotCraft.Protocol;
using DotCraft.Sessions;
using AutomationTask = DotCraft.Automations.Abstractions.AutomationTask;

namespace DotCraft.Automations.Protocol;

/// <summary>
/// In-process wrapper over <see cref="ISessionService"/> for use by the automations orchestrator.
/// </summary>
public sealed class AutomationSessionClient(ISessionService sessionService, DotCraftPaths paths)
{
    /// <summary>Host project workspace root (same as <see cref="SessionIdentity.WorkspacePath"/> for automations).</summary>
    public string ProjectWorkspacePath => paths.WorkspacePath;

    public string GetTaskWorktreeBranchName(string taskId) =>
        "dotcraft/task-" + SanitizeTaskIdForWorktree(taskId);

    public string GetTaskWorktreePath(string taskId) =>
        Path.Combine(paths.WorkspacePath, ".craft", "worktrees", "task-" + SanitizeTaskIdForWorktree(taskId));

    /// <summary>
    /// Creates a new thread or resumes an existing one for the same workspace + channel + user.
    /// Configures <see cref="ThreadConfiguration"/> (workspace override, tool profile, approval policy).
    /// </summary>
    public async Task<string> CreateOrResumeThreadAsync(
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

        var existing = await sessionService.FindThreadsAsync(identity, includeArchived: false, ct: ct);
        var match = existing.FirstOrDefault(t =>
            string.Equals(t.OriginChannel, channelName, StringComparison.OrdinalIgnoreCase));

        if (match != null)
        {
            await sessionService.UpdateThreadConfigurationAsync(match.Id, config, ct);
            await sessionService.EnsureThreadLoadedAsync(match.Id, ct);
            return match.Id;
        }

        var thread = await sessionService.CreateThreadAsync(
            identity,
            config,
            displayName: displayName,
            ct: ct);
        return thread.Id;
    }

    public async Task<ThreadWorktreeInfo> EnsureTaskWorktreeAsync(
        string threadId,
        string taskId,
        CancellationToken ct)
    {
        var result = await sessionService.EnsureManagedWorktreeAsync(
            new WorktreeEnsureOptions
            {
                ThreadId = threadId,
                BranchName = GetTaskWorktreeBranchName(taskId),
                Path = GetTaskWorktreePath(taskId),
                BaseRef = "HEAD",
                OwnerKind = "automationTask",
                OwnerId = taskId
            },
            ct);
        return result.Worktree;
    }

    public Task ConfigureTaskExecutionWorkspaceAsync(
        string threadId,
        string? executionWorkspacePath,
        CancellationToken ct) =>
        sessionService.ConfigureThreadExecutionWorkspaceAsync(
            new ThreadExecutionWorkspaceOptions
            {
                ThreadId = threadId,
                ExecutionWorkspaceOverride = executionWorkspacePath,
                ClearWorktree = true
            },
            ct);

    public Task RemoveTaskWorktreeAsync(
        string taskId,
        string? threadId,
        CancellationToken ct) =>
        sessionService.RemoveManagedWorktreeAsync(
            new WorktreeRemoveOptions
            {
                ThreadId = threadId,
                WorkspacePath = paths.WorkspacePath,
                BranchName = GetTaskWorktreeBranchName(taskId),
                Path = GetTaskWorktreePath(taskId),
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
        TurnTriggerInfo? trigger = null)
    {
        // The scope only needs to be active while SubmitInputAsync synchronously
        // builds the UserMessage item; we can release it before enumerating the
        // event stream.
        IAsyncEnumerable<SessionEvent> stream;
        if (trigger != null)
        {
            using (TurnTriggerScope.Set(trigger))
            {
                stream = sessionService.SubmitInputAsync(threadId, message, sender: null, messages: null, ct);
            }
        }
        else
        {
            stream = sessionService.SubmitInputAsync(threadId, message, sender: null, messages: null, ct);
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
    /// so the orchestrator can mark tasks whose binding target is gone as failed without throwing.
    /// </summary>
    public async Task<SessionThread?> TryGetThreadAsync(string threadId, CancellationToken ct)
    {
        try
        {
            return await sessionService.GetThreadAsync(threadId, ct);
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

    private static string SanitizeTaskIdForWorktree(string taskId)
    {
        var value = string.IsNullOrWhiteSpace(taskId) ? "task" : taskId.Trim();
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
            slug = "task";

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
