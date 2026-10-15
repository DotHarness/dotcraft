using Microsoft.Extensions.Logging;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private async Task DeleteSideChatsAsync(string sourceThreadId, CancellationToken ct)
    {
        var sourceId = sourceThreadId.Trim();
        foreach (var runtime in _runtimeRegistry.Values)
        {
            var thread = runtime.Thread;
            if (!string.Equals(thread.ForkedFromId, sourceId, StringComparison.Ordinal)
                || CacheSharingFork.PurposeOf(thread) != ThreadForkPurpose.SideChat)
            {
                continue;
            }

            try
            {
                await DeleteThreadPermanentlyAsync(thread.Id, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger?.LogWarning(ex, "Failed to delete side chat {SideChatId} of thread {ThreadId}.", thread.Id, sourceId);
            }
        }
    }
}
