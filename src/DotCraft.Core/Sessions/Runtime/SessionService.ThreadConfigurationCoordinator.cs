using SessionThread = DotCraft.Sessions.SessionThread;
namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private sealed class ThreadConfigurationCoordinator(SessionService owner)
    {
        public async Task SetModeAsync(string threadId, string mode, CancellationToken ct)
        {
            var thread = await owner.GetOrLoadThreadAsync(threadId, ct);
            using (await owner.AcquireThreadAgentLockAsync(threadId, ct))
            {
                thread.Configuration ??= new ThreadConfiguration();
                thread.Configuration.Mode = mode;

                owner.SetThreadAgent(threadId, await owner.BuildAgentForThreadAsync(thread, ct));

                await owner.PersistThreadWithMaterializationAsync(thread, ct);
            }
        }

        public async Task UpdateAsync(string threadId, ThreadConfiguration config, CancellationToken ct)
        {
            var thread = await owner.GetOrLoadThreadAsync(threadId, ct);
            using (await owner.AcquireThreadAgentLockAsync(threadId, ct))
            {
                thread.Configuration = ThreadWorkspaceResolver.Apply(
                    thread.WorkspacePath,
                    config,
                    config.Cwd,
                    config.RuntimeWorkspaceRoots);
                await owner.AgentFactory.ReleaseThreadToolResourcesAsync(threadId, ct);
                owner.SetThreadAgent(threadId, await owner.BuildAgentForThreadAsync(thread, ct));
                await owner.PersistThreadWithMaterializationAsync(thread, ct);
                owner.ThreadUpdatedForBroadcast?.Invoke(thread);
            }
        }

        public async Task<SessionThread> UpdateWorkspaceAsync(
            string threadId,
            string? cwd,
            IReadOnlyList<string>? runtimeWorkspaceRoots,
            CancellationToken ct)
        {
            var thread = await owner.GetOrLoadThreadAsync(threadId, ct);
            if (cwd == null && runtimeWorkspaceRoots == null)
                return thread;

            using (await owner.AcquireThreadAgentLockAsync(threadId, ct))
            {
                var previousCwd = thread.Configuration?.Cwd;
                var previousRoots = thread.Configuration?.RuntimeWorkspaceRoots;
                thread.Configuration = ThreadWorkspaceResolver.Apply(
                    thread.WorkspacePath,
                    thread.Configuration,
                    cwd,
                    runtimeWorkspaceRoots);

                if (string.Equals(previousCwd, thread.Configuration.Cwd, PathComparison)
                    && RootsEqual(previousRoots, thread.Configuration.RuntimeWorkspaceRoots))
                {
                    return thread;
                }

                await owner.AgentFactory.ReleaseThreadToolResourcesAsync(threadId, ct);
                owner.SetThreadAgent(threadId, await owner.BuildAgentForThreadAsync(thread, ct));
                await owner.PersistThreadWithMaterializationAsync(thread, ct);
                owner.ThreadUpdatedForBroadcast?.Invoke(thread);
                return thread;
            }
        }

        public async Task RefreshAgentAsync(string threadId, CancellationToken ct)
        {
            var thread = await owner.GetOrLoadThreadAsync(threadId, ct);
            using (await owner.AcquireThreadAgentLockAsync(threadId, ct))
                owner.SetThreadAgent(threadId, await owner.BuildAgentForThreadAsync(thread, ct));
        }

        public void InvalidateAgents()
        {
            owner._forcePerThreadAgents = true;
            owner.ClearAllThreadAgentCaches();
        }
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static bool RootsEqual(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left == null || right == null || left.Count != right.Count)
            return false;
        for (var i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i], right[i], PathComparison))
                return false;
        }

        return true;
    }
}
