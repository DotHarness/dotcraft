namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private sealed class ThreadIndexCoordinator(SessionService owner)
    {
        public async Task<IReadOnlyList<ThreadSummary>> FindAsync(
            SessionIdentity identity,
            bool includeArchived,
            IReadOnlyList<string>? crossChannelOrigins,
            CancellationToken ct,
            bool includeSubAgents,
            ThreadDiscoveryScope scope)
        {
            var all = await owner.Persistence.LoadIndexAsync(ct);
            var hasCross = crossChannelOrigins is { Count: > 0 };
            var mergedById = new Dictionary<string, ThreadSummary>(StringComparer.OrdinalIgnoreCase);
            foreach (var summary in all)
                mergedById[summary.Id] = summary;
            foreach (var thread in owner._runtimeRegistry.Values.Select(runtime => runtime.Thread))
            {
                if (thread.Ephemeral)
                    continue;

                var summary = ThreadSummary.FromThread(thread);
                summary.Runtime = owner.GetThreadRuntimeSnapshot(thread);
                mergedById[thread.Id] = summary;
            }

            var merged = mergedById.Values.ToList();

            return merged
                .Where(s =>
                {
                    if (!(includeArchived || s.Status != ThreadStatus.Archived))
                        return false;
                    if (!string.Equals(s.WorkspacePath, identity.WorkspacePath, StringComparison.OrdinalIgnoreCase))
                        return false;
                    if (!includeSubAgents && IsSubAgentSummary(s))
                        return false;
                    if (includeSubAgents
                        && IsSubAgentSummary(s)
                        && IsHiddenByArchivedParent(s, mergedById, includeArchived))
                    {
                        return false;
                    }

                    if (scope == ThreadDiscoveryScope.Workspace)
                        return true;

                    if (includeSubAgents
                        && IsSubAgentSummary(s)
                        && (identity.UserId == null || s.UserId == identity.UserId))
                    {
                        return true;
                    }

                    // userId and channelContext apply together for the native identity path only.
                    // Cron/heartbeat threads use synthetic userIds (e.g. cron:jobId) while Desktop uses local;
                    // they are included only via crossChannelOrigins (workspace + originChannel).
                    var identityMatch =
                        (identity.UserId == null || s.UserId == identity.UserId)
                        && (identity.ChannelContext == null
                            ? s.ChannelContext == null
                            : s.ChannelContext == identity.ChannelContext);

                    if (identityMatch)
                        return true;

                    if (!hasCross)
                        return false;

                    return OriginChannelInList(s.OriginChannel, crossChannelOrigins!);
                })
                .OrderByDescending(s => s.LastActiveAt)
                .ToList();
        }

        public async Task<int> CountWorkspaceThreadsAsync(string workspacePath, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(workspacePath))
                return 0;

            var all = await owner.Persistence.LoadIndexAsync(ct);
            var mergedById = new Dictionary<string, ThreadSummary>(StringComparer.OrdinalIgnoreCase);
            foreach (var summary in all)
                mergedById[summary.Id] = summary;
            foreach (var thread in owner._runtimeRegistry.Values.Select(runtime => runtime.Thread))
            {
                if (thread.Ephemeral)
                    continue;
                mergedById[thread.Id] = ThreadSummary.FromThread(thread);
            }

            return mergedById.Values.Count(s =>
                string.Equals(s.WorkspacePath, workspacePath, StringComparison.OrdinalIgnoreCase)
                && !ThreadVisibility.IsInternal(s)
                && !IsSubAgentSummary(s));
        }

        private static bool IsSubAgentSummary(ThreadSummary summary) =>
            string.Equals(summary.Source.Kind, ThreadSourceKinds.SubAgent, StringComparison.OrdinalIgnoreCase)
            || string.Equals(summary.OriginChannel, SubAgentThreadOrigin.ChannelName, StringComparison.OrdinalIgnoreCase);

        private static string? GetSubAgentParentThreadId(ThreadSummary summary)
        {
            var sourceParent = summary.Source.SubAgent?.ParentThreadId?.Trim();
            if (!string.IsNullOrWhiteSpace(sourceParent))
                return sourceParent;
            var context = summary.ChannelContext?.Trim();
            return string.IsNullOrWhiteSpace(context) ? null : context;
        }

        private static bool IsHiddenByArchivedParent(
            ThreadSummary summary,
            IReadOnlyDictionary<string, ThreadSummary> summariesById,
            bool includeArchived)
        {
            if (includeArchived)
                return false;
            var parentId = GetSubAgentParentThreadId(summary);
            return !string.IsNullOrWhiteSpace(parentId)
                && summariesById.TryGetValue(parentId, out var parent)
                && parent.Status == ThreadStatus.Archived;
        }

        private static bool OriginChannelInList(string originChannel, IReadOnlyList<string> origins)
        {
            foreach (var o in origins)
            {
                if (string.Equals(o, originChannel, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
