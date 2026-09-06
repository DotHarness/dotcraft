using DotCraft.Channels;
using DotCraft.Tools;
using Microsoft.Extensions.Logging;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private sealed class ThreadLifecycleCoordinator(SessionService owner)
    {
        public async Task<SessionThread> ResumeAsync(string threadId, CancellationToken ct)
        {
            var wasLoaded = owner._runtimeRegistry.TryGetThread(threadId, out _);
            var thread = await owner.GetOrLoadThreadAsync(threadId, ct);

            if (thread.Status == ThreadStatus.Archived)
                throw new InvalidOperationException($"Thread '{threadId}' is archived and cannot be resumed.");

            if (!wasLoaded)
            {
                thread.Status = ThreadStatus.Active;
                thread.LastActiveAt = DateTimeOffset.UtcNow;
                await owner.PersistThreadWithMaterializationAsync(thread, ct);
            }
            else if (thread.Status != ThreadStatus.Active)
            {
                var previousStatus = thread.Status;
                thread.Status = ThreadStatus.Active;
                thread.LastActiveAt = DateTimeOffset.UtcNow;
                await owner.PersistThreadIfMaterializedAsync(thread, ct);
                owner.GetOrCreateBroker(threadId).PublishThreadStatusChanged(previousStatus, thread.Status);
            }

            await owner.EnsurePerThreadAgentIfMissingAsync(thread.Id, thread, ct);

            var resumedBy = ChannelSessionScope.Current?.Channel ?? thread.OriginChannel;
            owner.GetOrCreateBroker(thread.Id).PublishThreadEvent(SessionEventType.ThreadResumed,
                new ThreadResumedPayload { Thread = thread, ResumedBy = resumedBy });
            await owner.ContributionLifecycle.ThreadResumedAsync(thread, ct);

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

            foreach (var id in await CollectSubAgentSubtreeIdsAsync(root.Id, includeClosed: true, ct))
            {
                var thread = await owner.GetOrLoadThreadAsync(id, ct);
                await ArchiveCoreAsync(thread, ct);
            }
        }

        public async Task UnarchiveAsync(string threadId, CancellationToken ct)
        {
            var root = await owner.GetOrLoadThreadAsync(threadId, ct);
            ThrowIfDirectSubAgentLifecycleOperation(root, "unarchive");

            foreach (var id in await CollectSubAgentSubtreeIdsAsync(root.Id, includeClosed: false, ct))
            {
                var thread = await owner.GetOrLoadThreadAsync(id, ct);
                await UnarchiveCoreAsync(thread, ct);
            }
        }

        public async Task ArchiveSubAgentTreeForCloseAsync(string childThreadId, CancellationToken ct)
        {
            var root = await owner.GetOrLoadThreadAsync(childThreadId, ct);
            if (!IsSubAgentThread(root))
                throw new InvalidOperationException($"Thread '{childThreadId}' is not a SubAgent child thread.");

            foreach (var id in await CollectSubAgentSubtreeIdsAsync(root.Id, includeClosed: true, ct))
            {
                var thread = await owner.GetOrLoadThreadAsync(id, ct);
                await ArchiveCoreAsync(thread, ct);
            }
        }

        public async Task<IReadOnlyList<string>> PreparePermanentDeletionAsync(string threadId, CancellationToken ct)
        {
            var normalizedThreadId = threadId.Trim();
            if (normalizedThreadId.Length == 0)
                throw new ArgumentException("threadId is required.", nameof(threadId));

            var root = await owner.GetOrLoadThreadAsync(normalizedThreadId, ct);
            ThrowIfDirectSubAgentLifecycleOperation(root, "delete");
            var subtreeIds = await CollectSubAgentSubtreeIdsAsync(normalizedThreadId, includeClosed: true, ct);
            var deleteOrder = subtreeIds.Reverse().ToList();
            foreach (var id in deleteOrder)
            {
                var thread = await owner.GetOrLoadThreadAsync(id, ct);
                owner._runtimeRegistry.SetThread(thread);
                owner._runtimeRegistry.MarkPendingPermanentDeletion(id);
            }

            return deleteOrder;
        }

        public async Task ExecutePermanentDeletionAsync(IReadOnlyList<string> deleteOrder, CancellationToken ct)
        {
            try
            {
                foreach (var id in deleteOrder)
                    await DeleteCoreAsync(id, ct);
            }
            catch
            {
                foreach (var id in deleteOrder)
                    owner._runtimeRegistry.ClearPendingPermanentDeletion(id);
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

        private async Task<IReadOnlyList<string>> CollectSubAgentSubtreeIdsAsync(
            string rootThreadId,
            bool includeClosed,
            CancellationToken ct)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            async Task VisitAsync(string id)
            {
                ct.ThrowIfCancellationRequested();
                if (!seen.Add(id))
                    return;

                result.Add(id);
                var children = await owner.Persistence.ListSubAgentChildrenAsync(id, includeClosed, ct);
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
            owner.ToolDispatchPolicyRegistry?.Remove(thread.Id);
            await owner.AgentFactory.ReleaseThreadToolResourcesAsync(thread.Id, ct);
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
            string? workspacePath = null;
            if (owner._runtimeRegistry.TryGetRuntime(threadId, out var runtime))
            {
                var thread = runtime.Thread;
                ephemeral = thread.Ephemeral;
                workspacePath = thread.WorkspacePath;
                foreach (var turn in thread.Turns.Where(t => t.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput))
                {
                    if (runtime.TryRemoveTurn(turn.Id, out var turnRuntime))
                    {
                        if (turnRuntime.Cancellation != null)
                            await turnRuntime.Cancellation.CancelAsync();
                        turnRuntime.Dispose();
                    }
                }
            }

            if (workspacePath is null)
            {
                var persisted = await owner.Persistence.LoadThreadAsync(threadId, ct);
                if (persisted is not null)
                {
                    ephemeral = persisted.Ephemeral;
                    workspacePath = persisted.WorkspacePath;
                }
            }

            var deletingThread = owner._runtimeRegistry.TryGetRuntime(threadId, out var deletingRuntime)
                ? deletingRuntime.Thread
                : await owner.Persistence.LoadThreadAsync(threadId, ct);
            if (deletingThread != null)
                await owner.ContributionLifecycle.ThreadDeletingAsync(deletingThread, ct);

            if (owner.BackgroundTerminalService != null)
                await owner.BackgroundTerminalService.DeleteThreadArtifactsAsync(threadId, ct);

            if (!string.IsNullOrWhiteSpace(workspacePath))
            {
                var cleanup = ToolResultProcessor.CleanupThreadArtifacts(workspacePath, owner.DataPath, threadId);
                if (cleanup.Errors > 0)
                {
                    owner.Logger?.LogWarning(
                        "Failed to delete {Count} tool-result artifact directories for thread {ThreadId}; cleanup will be retried.",
                        cleanup.Errors,
                        threadId);
                }
            }

            if (!ephemeral)
                await owner.Persistence.DeleteThreadCascadeAsync(threadId, ct);

            if (owner._runtimeRegistry.TryRemove(threadId, out var removedRuntime))
                await removedRuntime.DisposeAsync();
            owner.ToolDispatchPolicyRegistry?.Remove(threadId);
            await owner.AgentFactory.ReleaseThreadToolResourcesAsync(threadId, ct);
            owner.InvalidatePromptRequestSnapshot(threadId, "thread_deleted");
            owner.ClearContextUsageAnchor(threadId);
            owner.ForgetContextPages(threadId);
            // Strictly after the deleting observation above, so a thread-scoped contributor still sees its own thread's deletion.
            owner.ContributionLifecycle.ReleaseThreadContributions(threadId);

            owner.ThreadDeletedForBroadcast?.Invoke(threadId);
        }

        private void PublishThreadStatusChanged(string threadId, ThreadStatus previousStatus, ThreadStatus newStatus)
        {
            owner.GetOrCreateBroker(threadId).PublishThreadStatusChanged(previousStatus, newStatus);
            owner.ThreadStatusChangedForBroadcast?.Invoke(threadId, previousStatus, newStatus);
        }
    }
}
