namespace DotCraft.Protocol;

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
                thread.Configuration = config;
                owner.SetThreadAgent(threadId, await owner.BuildAgentForThreadAsync(thread, ct));
                await owner.PersistThreadWithMaterializationAsync(thread, ct);
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
}
