using DotCraft.Channels;
using DotCraft.Visualizations;
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
                thread.LastActiveAt = DateTimeOffset.UtcNow;
                await owner.PersistThreadWithMaterializationAsync(thread, ct);
            }

            await owner.EnsurePerThreadAgentIfMissingAsync(thread.Id, thread, ct);

            var resumedBy = ChannelSessionScope.Current?.Channel ?? thread.OriginChannel;
            owner.GetOrCreateBroker(thread.Id).PublishThreadEvent(SessionEventType.ThreadResumed,
                new ThreadResumedPayload { Thread = thread, ResumedBy = resumedBy });
            await owner.ContributionLifecycle.ThreadResumedAsync(thread, ct);

            return thread;
        }

        public async Task<bool> ArchiveAsync(string threadId, CancellationToken ct)
        {
            var archived = false;
            await owner.WithThreadForArchiveAsync(threadId, async (root, loaded, commandCt) =>
            {
                ThrowIfDirectSubAgentLifecycleOperation(root, "archive");
                archived = root.Status != ThreadStatus.Archived;
                await ArchiveTreeAsync(root, loaded, commandCt);
            }, ct);
            return archived;
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

        public Task ArchiveSubAgentTreeForCloseAsync(string childThreadId, CancellationToken ct) =>
            owner.WithThreadForArchiveAsync(childThreadId, async (root, loaded, commandCt) =>
            {
                if (!IsSubAgentThread(root))
                    throw new InvalidOperationException($"Thread '{childThreadId}' is not a SubAgent child thread.");

                await ArchiveTreeAsync(root, loaded, commandCt);
            }, ct);

        private async Task ArchiveTreeAsync(SessionThread root, bool loaded, CancellationToken ct)
        {
            var subtree = await CollectSubAgentSubtreeIdsAsync(root.Id, includeClosed: true, ct);
            var worktrees = new List<SessionThread>();
            var canRemoveWorktrees = await ArchiveCoreAsync(root, loaded, worktrees, ct);
            foreach (var id in subtree.Skip(1))
            {
                await owner.WithThreadForArchiveAsync(id, async (thread, childLoaded, childCt) =>
                {
                    canRemoveWorktrees &= await ArchiveCoreAsync(thread, childLoaded, worktrees, childCt);
                }, ct);
            }

            if (canRemoveWorktrees)
            {
                foreach (var thread in worktrees)
                    await owner.Worktrees.ReleaseOnArchiveAsync(thread, ct);
            }
        }

        public async Task DeletePermanentlyAsync(string threadId, CancellationToken ct, string? failedStartupParentId = null)
        {
            var prepared = new List<(string Id, SessionThread? Thread)>();
            try
            {
                await PrepareAsync(threadId, isRoot: true);
                if (failedStartupParentId is null)
                {
                    var subtree = await CollectSubAgentSubtreeIdsAsync(threadId, includeClosed: true, ct);
                    foreach (var childId in subtree.Skip(1))
                        await PrepareAsync(childId, isRoot: false);
                }

                while (prepared.Count != 0)
                {
                    var (id, thread) = prepared[^1];
                    await DeleteCoreAsync(id, thread, ct);
                    prepared.RemoveAt(prepared.Count - 1);
                }
            }
            finally
            {
                foreach (var (id, _) in prepared)
                    owner._runtimeRegistry.ClearPendingPermanentDeletion(id);
            }

            Task PrepareAsync(string id, bool isRoot) =>
                owner.WithExistingThreadAsync(id, async (thread, _, commandCt) =>
                {
                    if (isRoot)
                    {
                        if (thread is null)
                        {
                            if (failedStartupParentId is not null)
                                return;
                            throw new ThreadNotFoundException(id);
                        }

                        var parentId = owner.Persistence.GetSubAgentParentThreadId(id)
                            ?? (IsSubAgentThread(thread) ? GetSubAgentParentThreadId(thread) : null);
                        if (failedStartupParentId is null)
                            ThrowIfDirectSubAgentLifecycleOperation(id, parentId, "delete");
                        else
                        {
                            if (!string.Equals(parentId, failedStartupParentId, StringComparison.Ordinal))
                                throw new InvalidOperationException("Failed startup cleanup requires the owning parent thread.");
                            await owner.Persistence.SetThreadSpawnEdgeStatusAsync(
                                failedStartupParentId, id, ThreadSpawnEdgeStatus.Closed, commandCt);
                        }
                    }

                    owner._runtimeRegistry.MarkPendingPermanentDeletion(id);
                    prepared.Add((id, thread));
                }, ct, forDeletion: true);
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
            if (IsSubAgentThread(thread))
                ThrowIfDirectSubAgentLifecycleOperation(thread.Id, GetSubAgentParentThreadId(thread), operation);
        }

        private static void ThrowIfDirectSubAgentLifecycleOperation(string threadId, string? parentId, string operation)
        {
            if (!string.IsNullOrWhiteSpace(parentId))
                throw new InvalidOperationException(
                    $"SubAgent child thread '{threadId}' cannot be {operation}d directly; manage its parent thread '{parentId}' instead.");
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

        private async Task<bool> ArchiveCoreAsync(
            SessionThread thread, bool loaded, List<SessionThread> worktrees, CancellationToken ct)
        {
            if (thread.Status == ThreadStatus.Archived)
                return true;

            var previousStatus = thread.Status;
            bool interruptedTurn;
            Task<bool> terminalCleanup;
            using (await owner.AcquireThreadAgentLockAsync(thread.Id, ct))
            {
                thread.Status = ThreadStatus.Archived;
                owner._runtimeRegistry.TryGetRuntime(thread.Id, out var runtime);
                interruptedTurn = InterruptArchiveTurns(thread, runtime);
                terminalCleanup = StopArchivedThreadTerminalsAsync(thread.Id);
                owner.ClearThreadAgentCaches(thread.Id);
                if (runtime != null)
                    await runtime.ReleaseMcpAsync();
            }
            owner.ToolDispatchPolicyRegistry?.Remove(thread.Id);
            await owner.AgentFactory.ReleaseThreadToolResourcesAsync(thread.Id, ct);
            owner.ForgetContextPages(thread.Id);

            if (!loaded || owner.IsMaterialized(thread.Id))
                await owner.Persistence.ArchiveStoredThreadAsync(thread.Id, ct);
            PublishThreadStatusChanged(thread.Id, previousStatus, thread.Status);

            if (thread.Worktree != null)
                worktrees.Add(thread);
            return !interruptedTurn && terminalCleanup.IsCompletedSuccessfully && terminalCleanup.Result;
        }

        private bool InterruptArchiveTurns(SessionThread thread, ThreadRuntime? runtime)
        {
            var interruptedTurn = false;
            if (runtime != null)
            {
                foreach (var turn in runtime.Turns.Values)
                {
                    interruptedTurn = true;
                    try
                    {
                        turn.Interrupt();
                    }
                    catch (AggregateException ex)
                    {
                        owner.Logger?.LogWarning(ex, "A turn cancellation callback failed while archiving thread {ThreadId}.", thread.Id);
                    }
                }
            }

            return interruptedTurn || IsSubAgentThread(thread) && SubAgentSessionControl.InterruptRunningChild(thread.Id);
        }

        private async Task<bool> StopArchivedThreadTerminalsAsync(string threadId)
        {
            if (owner.BackgroundTerminalService is not { } terminals)
                return true;

            try
            {
                await terminals.CleanThreadAsync(threadId);
                return true;
            }
            catch (Exception ex)
            {
                owner.Logger?.LogWarning(ex, "Could not stop all background terminals for archived thread {ThreadId}.", threadId);
                return false;
            }
        }

        private async Task UnarchiveCoreAsync(SessionThread thread, CancellationToken ct)
        {
            if (thread.Status == ThreadStatus.Active)
                return;

            var previousStatus = thread.Status;
            thread.Status = ThreadStatus.Active;
            thread.LastActiveAt = DateTimeOffset.UtcNow;
            if (owner._runtimeRegistry.TryGetRuntime(thread.Id, out var runtime))
                runtime.McpRefresh = new ThreadMcpRefresh();
            try
            {
                await owner.Worktrees.RestoreCheckoutAsync(thread, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                owner.Logger?.LogWarning(ex, "Could not check out the worktree of thread {ThreadId} again.", thread.Id);
            }
            await owner.PersistThreadStatusAsync(thread, ct);
            PublishThreadStatusChanged(thread.Id, previousStatus, thread.Status);
        }

        private async Task DeleteCoreAsync(string threadId, SessionThread? deletingThread, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (owner._runtimeRegistry.TryGetRuntime(threadId, out var runtime))
            {
                foreach (var turn in runtime.Thread.Turns.Where(t => t.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput))
                {
                    if (runtime.TryRemoveTurn(turn.Id, out var turnRuntime))
                    {
                        if (turnRuntime.Cancellation != null)
                            await turnRuntime.Cancellation.CancelAsync();
                        turnRuntime.Dispose();
                    }
                }
            }

            if (deletingThread != null)
            {
                if (IsSubAgentThread(deletingThread))
                    SubAgentSessionControl.InterruptRunningChild(threadId);
                await owner.ContributionLifecycle.ThreadDeletingAsync(deletingThread, ct);
            }

            if (owner.BackgroundTerminalService != null)
                await owner.BackgroundTerminalService.DeleteThreadArtifactsAsync(threadId, ct);
            if (deletingThread != null)
                await owner.Worktrees.ReleaseOnDeleteAsync(deletingThread, ct);

            if (deletingThread?.WorkspacePath is { Length: > 0 } workspacePath)
            {
                var cleanup = ToolResultProcessor.CleanupThreadArtifacts(workspacePath, owner.DataPath, threadId);
                if (cleanup.Errors > 0)
                {
                    owner.Logger?.LogWarning(
                        "Failed to delete {Count} tool-result artifact directories for thread {ThreadId}; cleanup will be retried.",
                        cleanup.Errors,
                        threadId);
                }

                var visualizationCleanup = VisualizationAssetStore.DeleteThreadDirectory(workspacePath, owner.DataPath, threadId);
                if (visualizationCleanup.Errors > 0)
                {
                    owner.Logger?.LogWarning(
                        "Failed to delete the visualization directory for thread {ThreadId}: {Error}",
                        threadId,
                        visualizationCleanup.ErrorMessages[0]);
                }
            }

            if (owner._runtimeRegistry.TryRemove(threadId, out var removedRuntime))
                await removedRuntime.DisposeAsync();
            await owner.AgentFactory.ReleaseThreadToolResourcesAsync(threadId, ct);

            if (deletingThread?.Ephemeral != true)
                await owner.Persistence.DeleteThreadCascadeAsync(threadId, ct);

            owner._sessionApprovalScopes.RemoveThread(threadId);
            owner.McpAppTransientContexts?.ClearThread(threadId);
            owner.ThreadDeletedForBroadcast?.Invoke(threadId);
            owner._runtimeRegistry.ForgetBroker(threadId);
            owner.ToolDispatchPolicyRegistry?.Remove(threadId);
            owner.InvalidatePromptRequestSnapshot(threadId, "thread_deleted");
            owner.ClearContextUsageAnchor(threadId);
            owner.ForgetContextPages(threadId);
            // Strictly after the deleting observation above, so a thread-scoped contributor still sees its own thread's deletion.
            owner.ContributionLifecycle.ReleaseThreadContributions(threadId);
        }

        private void PublishThreadStatusChanged(string threadId, ThreadStatus previousStatus, ThreadStatus newStatus)
        {
            owner.GetOrCreateBroker(threadId).PublishThreadStatusChanged(previousStatus, newStatus);
            owner.ThreadStatusChangedForBroadcast?.Invoke(threadId, previousStatus, newStatus);
        }
    }
}
