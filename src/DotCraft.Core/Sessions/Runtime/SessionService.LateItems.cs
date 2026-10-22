using DotCraft.Tools;
using DotCraft.Tools.BackgroundTerminals;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private async Task SettleOrphanedCommandsAsync(SessionThread thread, CancellationToken ct)
    {
        var settledResident = false;
        foreach (var turn in thread.Turns.Where(ThreadWorkingSet.IsTerminal))
        {
            foreach (var item in turn.Items)
                settledResident |= await TrySettleOrphanedCommandAsync(item, ct);
        }

        var settledOmitted = new List<ThreadRolloutRecord>();
        if (thread.OmittedTurnCount > 0)
        {
            var residentIds = thread.Turns.Select(static turn => turn.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var entry in await persistence.ReadUnfinishedCommandItemsAsync(thread.Id, ct))
            {
                if (!residentIds.Contains(entry.TurnId) && await TrySettleOrphanedCommandAsync(entry.Item, ct))
                    settledOmitted.Add(RolloutChangeRecords.ItemAppended(entry.TurnId, entry.Item));
            }
        }

        if (settledOmitted.Count > 0)
            await persistence.AppendThreadChangesAsync(thread, settledOmitted, deleteUnreferencedAttachments: false, ct);
        if (settledResident)
            await persistence.SaveThreadAsync(thread, ct);
    }

    private async Task<bool> TrySettleOrphanedCommandAsync(SessionItem item, CancellationToken ct)
    {
        if (item.Type != ItemType.CommandExecution
            || item.Status == ItemStatus.Completed
            || item.AsCommandExecution is not { } payload)
            return false;

        BackgroundTerminalSnapshot? terminal = null;
        if (!string.IsNullOrEmpty(payload.SessionId) && BackgroundTerminalService is { } terminals)
        {
            try
            {
                terminal = await terminals.ReadAsync(payload.SessionId, ct: ct);
            }
            catch (KeyNotFoundException)
            {
            }
            if (terminal?.Status == BackgroundTerminalStatus.Running)
                return false;
        }

        item.Status = ItemStatus.Completed;
        item.CompletedAt = terminal?.CompletedAt ?? DateTimeOffset.UtcNow;
        item.Payload = terminal is null
            ? payload with { Status = "lost" }
            : payload with
            {
                Status = CommandExecutionTracker.StatusOf(terminal),
                AggregatedOutput = ShellTools.FormatForegroundSnapshot(terminal),
                ExitCode = terminal.ExitCode,
                OriginalOutputChars = terminal.OriginalOutputChars,
                Truncated = terminal.Truncated
            };
        return true;
    }

    private async Task PersistItemAfterTurnAsync(string threadId, string turnId, SessionItem item)
    {
        var thread = await GetOrLoadThreadAsync(threadId, CancellationToken.None);
        if (ThreadWorkingSet.FindResident(thread, turnId) is not { } turn)
        {
            if (thread.OmittedTurnCount > 0
                && await persistence.ReadProjectedItemAsync(thread.Id, turnId, item.Id, CancellationToken.None) is not null)
            {
                await persistence.AppendThreadChangesAsync(
                    thread,
                    [RolloutChangeRecords.ItemAppended(turnId, item)],
                    deleteUnreferencedAttachments: false,
                    CancellationToken.None);
            }
            return;
        }
        if (turn.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput)
            return;

        var index = turn.Items.FindIndex(i => string.Equals(i.Id, item.Id, StringComparison.Ordinal));
        if (index < 0)
            return;

        turn.Items[index] = item;
        await PersistThreadWithMaterializationAsync(thread, CancellationToken.None);
    }

    private async Task EmitTerminalInteractionAsync(
        string threadId,
        string turnId,
        string callId,
        CommandExecutionTerminalInteraction interaction)
    {
        SessionThread thread;
        try
        {
            thread = await GetOrLoadThreadAsync(threadId, CancellationToken.None);
        }
        catch (KeyNotFoundException)
        {
            return;
        }

        var item = (await ReadTurnAsync(thread, turnId, CancellationToken.None))?
            .Items.FirstOrDefault(i => i.Type == ItemType.CommandExecution
                && string.Equals(i.AsCommandExecution?.CallId, callId, StringComparison.Ordinal));
        if (item is not null)
            GetOrCreateBroker(threadId).PublishItemDelta(turnId, item.Id, interaction);
    }
}
