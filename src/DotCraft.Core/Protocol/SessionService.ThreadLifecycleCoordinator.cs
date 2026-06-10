using DotCraft.Abstractions;

namespace DotCraft.Protocol;

public sealed partial class SessionService
{
    private sealed class ThreadLifecycleCoordinator(SessionService owner)
    {
        public async Task<SessionThread> ResumeAsync(string threadId, CancellationToken ct)
        {
            if (owner._runtimeRegistry.TryGetThread(threadId, out var cached))
            {
                if (cached.Status == ThreadStatus.Archived)
                    throw new InvalidOperationException($"Thread '{threadId}' is archived and cannot be resumed.");

                if (cached.Status != ThreadStatus.Active)
                {
                    var previousStatus = cached.Status;
                    cached.Status = ThreadStatus.Active;
                    cached.LastActiveAt = DateTimeOffset.UtcNow;
                    await owner.PersistThreadIfMaterializedAsync(cached, ct);
                    owner.GetOrCreateBroker(threadId).PublishThreadStatusChanged(previousStatus, cached.Status);
                }

                await owner.EnsurePerThreadAgentIfMissingAsync(threadId, cached, ct);

                var resumedByChannel = ChannelSessionScope.Current?.Channel ?? cached.OriginChannel;
                owner.GetOrCreateBroker(threadId).PublishThreadEvent(SessionEventType.ThreadResumed,
                    new ThreadResumedPayload { Thread = cached, ResumedBy = resumedByChannel });
                return cached;
            }

            var thread = await owner.Persistence.LoadThreadAsync(threadId, ct)
                ?? throw new KeyNotFoundException($"Thread '{threadId}' not found.");

            if (thread.Status == ThreadStatus.Archived)
                throw new InvalidOperationException($"Thread '{threadId}' is archived and cannot be resumed.");

            thread.Status = ThreadStatus.Active;
            thread.LastActiveAt = DateTimeOffset.UtcNow;

            owner._runtimeRegistry.SetThread(thread);
            var broker = owner.GetOrCreateBroker(thread.Id);

            await owner.EnsurePerThreadAgentIfMissingAsync(thread.Id, thread, ct);

            await owner.PersistThreadWithMaterializationAsync(thread, ct);
            var resumedBy = ChannelSessionScope.Current?.Channel ?? thread.OriginChannel;
            broker.PublishThreadEvent(SessionEventType.ThreadResumed,
                new ThreadResumedPayload { Thread = thread, ResumedBy = resumedBy });

            return thread;
        }

        public async Task PauseAsync(string threadId, CancellationToken ct)
        {
            var thread = await owner.GetOrLoadThreadAsync(threadId, ct);
            if (thread.Status == ThreadStatus.Paused)
                return;

            var previousStatus = thread.Status;
            thread.Status = ThreadStatus.Paused;
            await owner.PersistThreadStatusAsync(thread, ct);
            owner.GetOrCreateBroker(threadId).PublishThreadStatusChanged(previousStatus, thread.Status);
        }

        public async Task ArchiveAsync(string threadId, CancellationToken ct)
        {
            var root = await owner.GetOrLoadThreadAsync(threadId, ct);
            ThrowIfDirectSubAgentLifecycleOperation(root, "archive");

            foreach (var id in await CollectSubAgentSubtreeIdsAsync(root.Id, ct))
            {
                var thread = await owner.GetOrLoadThreadAsync(id, ct);
                await ArchiveCoreAsync(thread, ct);
            }
        }

        public async Task UnarchiveAsync(string threadId, CancellationToken ct)
        {
            var root = await owner.GetOrLoadThreadAsync(threadId, ct);
            ThrowIfDirectSubAgentLifecycleOperation(root, "unarchive");

            foreach (var id in await CollectSubAgentSubtreeIdsAsync(root.Id, ct))
            {
                var thread = await owner.GetOrLoadThreadAsync(id, ct);
                await UnarchiveCoreAsync(thread, ct);
            }
        }

        public async Task DeletePermanentlyAsync(string threadId, CancellationToken ct)
        {
            var normalizedThreadId = threadId.Trim();
            if (normalizedThreadId.Length == 0)
                throw new ArgumentException("threadId is required.", nameof(threadId));

            var root = await owner.GetOrLoadThreadAsync(normalizedThreadId, ct);
            ThrowIfDirectSubAgentLifecycleOperation(root, "delete");
            var subtreeIds = await CollectSubAgentSubtreeIdsAsync(normalizedThreadId, ct);
            var deleteOrder = subtreeIds.Reverse().ToList();
            foreach (var id in deleteOrder)
                owner._threadsPendingPermanentDeletion[id] = 0;

            try
            {
                foreach (var id in deleteOrder)
                    await DeleteCoreAsync(id, ct);
            }
            catch
            {
                foreach (var id in deleteOrder)
                    owner._threadsPendingPermanentDeletion.TryRemove(id, out _);
                throw;
            }
        }

        public async Task RenameAsync(string threadId, string displayName, CancellationToken ct)
        {
            var thread = await owner.GetOrLoadThreadAsync(threadId, ct);
            var previous = thread.DisplayName;
            thread.DisplayName = displayName;
            await owner.PersistThreadWithMaterializationAsync(thread, ct);
            if (previous != displayName)
                owner.ThreadRenamedForBroadcast?.Invoke(thread);
        }

        private static string? GetSubAgentParentThreadId(SessionThread thread)
        {
            var sourceParent = thread.Source.SubAgent?.ParentThreadId?.Trim();
            if (!string.IsNullOrWhiteSpace(sourceParent))
                return sourceParent;
            var context = thread.ChannelContext?.Trim();
            return string.IsNullOrWhiteSpace(context) ? null : context;
        }

        private static void ThrowIfDirectSubAgentLifecycleOperation(SessionThread thread, string operation)
        {
            if (!IsSubAgentThread(thread))
                return;
            var parentId = GetSubAgentParentThreadId(thread);
            if (string.IsNullOrWhiteSpace(parentId))
                return;
            throw new InvalidOperationException(
                $"SubAgent child thread '{thread.Id}' cannot be {operation}d directly; manage its parent thread '{parentId}' instead.");
        }

        private async Task<IReadOnlyList<string>> CollectSubAgentSubtreeIdsAsync(string rootThreadId, CancellationToken ct)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            async Task VisitAsync(string id)
            {
                ct.ThrowIfCancellationRequested();
                if (!seen.Add(id))
                    return;

                result.Add(id);
                var children = await owner.Persistence.ListSubAgentChildrenAsync(id, includeClosed: true, ct);
                foreach (var child in children)
                    await VisitAsync(child.ChildThreadId);
            }

            await VisitAsync(rootThreadId);
            return result;
        }

        private async Task ArchiveCoreAsync(SessionThread thread, CancellationToken ct)
        {
            if (thread.Status == ThreadStatus.Archived)
                return;

            var previousStatus = thread.Status;
            thread.Status = ThreadStatus.Archived;
            owner.ClearThreadAgentCaches(thread.Id);
            owner.ForgetContextPages(thread.Id);
            if (owner.BackgroundTerminalService != null)
                await owner.BackgroundTerminalService.CleanThreadAsync(thread.Id, ct);

            await owner.PersistThreadStatusAsync(thread, ct);
            PublishThreadStatusChanged(thread.Id, previousStatus, thread.Status);
        }

        private async Task UnarchiveCoreAsync(SessionThread thread, CancellationToken ct)
        {
            if (thread.Status == ThreadStatus.Active)
                return;

            var previousStatus = thread.Status;
            thread.Status = ThreadStatus.Active;
            thread.LastActiveAt = DateTimeOffset.UtcNow;
            await owner.PersistThreadStatusAsync(thread, ct);
            PublishThreadStatusChanged(thread.Id, previousStatus, thread.Status);
        }

        private async Task DeleteCoreAsync(string threadId, CancellationToken ct)
        {
            var ephemeral = false;
            if (owner._runtimeRegistry.TryGetThread(threadId, out var thread))
            {
                ephemeral = thread.Ephemeral;
                foreach (var turn in thread.Turns.Where(t => t.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput))
                {
                    var key = new TurnKey(threadId, turn.Id);
                    if (owner._runningTurns.TryRemove(key, out var turnCts))
                        await turnCts.CancelAsync();
                    owner._pendingApprovals.TryRemove(key, out _);
                    owner._pendingUserInputRequests.TryRemove(key, out _);
                }
            }

            if (!ephemeral)
                await owner.Persistence.DeleteThreadCascadeAsync(threadId, ct);

            if (owner._runtimeRegistry.TryRemove(threadId, out var runtime))
                await runtime.DisposeAsync();
            owner._threadEventBrokers.TryRemove(threadId, out _);
            owner._materializedThreads.TryRemove(threadId, out _);
            owner._turnsSinceConsolidation.TryRemove(threadId, out _);
            owner._activeAutoMemoryConsolidations.TryRemove(threadId, out _);
            owner._pendingAutoMemoryConsolidations.TryRemove(threadId, out _);
            owner.InvalidatePromptRequestSnapshot(threadId, "thread_deleted");
            owner._contextUsageAnchors.TryRemove(threadId, out _);
            owner.ForgetContextPages(threadId);
            if (owner.BackgroundTerminalService != null)
                await owner.BackgroundTerminalService.CleanThreadAsync(threadId, ct);

            owner.ThreadDeletedForBroadcast?.Invoke(threadId);
        }

        private void PublishThreadStatusChanged(string threadId, ThreadStatus previousStatus, ThreadStatus newStatus)
        {
            owner.GetOrCreateBroker(threadId).PublishThreadStatusChanged(previousStatus, newStatus);
            owner.ThreadStatusChangedForBroadcast?.Invoke(threadId, previousStatus, newStatus);
        }
    }
}
