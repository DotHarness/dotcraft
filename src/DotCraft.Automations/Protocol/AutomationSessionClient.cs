using DotCraft.Hosting;
using DotCraft.Protocol;

namespace DotCraft.Automations.Protocol;

/// <summary>
/// In-process wrapper over <see cref="ISessionService"/> for use by the automations orchestrator.
/// </summary>
public sealed class AutomationSessionClient(ISessionService sessionService, DotCraftPaths paths)
{
    /// <summary>Host project workspace root (same as <see cref="SessionIdentity.WorkspacePath"/> for automations).</summary>
    public string ProjectWorkspacePath => paths.WorkspacePath;

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
}
