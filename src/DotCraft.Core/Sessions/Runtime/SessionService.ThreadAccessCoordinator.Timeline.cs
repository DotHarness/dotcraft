namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private sealed partial class ThreadAccessCoordinator
    {
        public Task<ThreadHistoryBootstrap> ReadThreadHistoryBootstrapAsync(string threadId, CancellationToken ct)
        {
            var normalized = NormalizeRequiredThreadId(threadId);
            ThrowIfEphemeral(normalized);
            return owner.Persistence.ReadThreadHistoryBootstrapAsync(normalized, ct);
        }

        public async Task<ThreadTimelinePage> ListThreadTimelineAsync(
            string threadId, ThreadTimelineCursor? cursor, int limit, CancellationToken ct)
        {
            var normalized = NormalizeRequiredThreadId(threadId);
            ThrowIfEphemeral(normalized);
            var page = await owner.Persistence.ListThreadTimelineAsync(normalized, cursor, limit, ct);
            if (!owner._runtimeRegistry.TryGetRuntime(normalized, out _))
            {
                foreach (var entry in page.Data)
                {
                    if (entry.Turn is { Status: TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput } turn)
                        turn.Status = TurnStatus.Cancelled;
                }
            }
            return page;
        }
    }
}
