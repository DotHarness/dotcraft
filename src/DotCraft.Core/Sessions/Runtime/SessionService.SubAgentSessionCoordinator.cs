using DotCraft.Channels;
using DotCraft.Agents;
using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private sealed class SubAgentSessionCoordinator(SessionService owner)
    {
        public async Task UpsertThreadSpawnEdgeAsync(ThreadSpawnEdge edge, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            await owner.Persistence.UpsertThreadSpawnEdgeAsync(edge, ct);
            owner.SubAgentGraphChangedForBroadcast?.Invoke(edge.ParentThreadId, edge.ChildThreadId);
        }

        public async Task SetThreadSpawnEdgeStatusAsync(
            string parentThreadId,
            string childThreadId,
            string status,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            await owner.Persistence.SetThreadSpawnEdgeStatusAsync(parentThreadId, childThreadId, status, ct);
            owner.SubAgentGraphChangedForBroadcast?.Invoke(parentThreadId, childThreadId);
        }

        public Task<IReadOnlyList<ThreadSpawnEdge>> ListChildrenAsync(
            string parentThreadId,
            bool includeClosed,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return owner.Persistence.ListSubAgentChildrenAsync(parentThreadId, includeClosed, ct);
        }

        public async Task AddMailboxEntryAsync(SubAgentMailboxEntry entry, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            await owner.Persistence.AddSubAgentMailboxEntryAsync(entry, ct);
            owner._subAgentCommunicationRuntime.PublishMailbox(entry.RootThreadId, entry.TargetAgentPath);
        }

        public Task<IReadOnlyList<SubAgentMailboxEntry>> ListPendingMailboxAsync(
            string rootThreadId,
            string targetAgentPath,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return owner.Persistence.ListPendingSubAgentMailboxAsync(rootThreadId, targetAgentPath, ct);
        }

        public async Task MarkMailboxDeliveredAsync(
            string rootThreadId,
            IReadOnlyList<string> entryIds,
            DateTimeOffset deliveredAt,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            await owner.Persistence.MarkSubAgentMailboxDeliveredAsync(rootThreadId, entryIds, deliveredAt, ct);
        }
    }
}
