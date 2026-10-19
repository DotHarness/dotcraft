using Microsoft.Extensions.Logging;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private void RecordThreadGitInfo(SessionThread thread, string workspace)
    {
        // A Remote Tool Host executes elsewhere, so this host's checkout says nothing about the turn.
        if (agentFactory.RemoteToolHostClient is not null)
            return;
        _ = RecordThreadGitInfoAsync(thread, workspace);
    }

    private async Task RecordThreadGitInfoAsync(SessionThread thread, string workspace)
    {
        try
        {
            var info = await ThreadGitInfoProbe.ReadAsync(workspace, CancellationToken.None).ConfigureAwait(false);
            if (Equals(info, thread.GitInfo) || IsPendingPermanentDeletion(thread.Id))
                return;

            thread.GitInfo = info;
            if (!thread.Ephemeral)
                await persistence.SaveThreadBaselineAsync(thread, CancellationToken.None).ConfigureAwait(false);
            ThreadUpdatedForBroadcast?.Invoke(thread);
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Failed to record Git context for thread {ThreadId}.", thread.Id);
        }
    }
}
