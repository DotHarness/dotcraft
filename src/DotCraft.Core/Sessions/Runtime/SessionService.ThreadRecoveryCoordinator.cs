namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    /// <inheritdoc />
    public async Task<ThreadRecoveryPackage> ExportThreadRecoveryAsync(
        string threadId,
        CancellationToken ct = default)
    {
        var work = await InvokeThreadCommandAsync(
            threadId,
            _ => Task.FromResult(ThreadRecovery.StartExport(threadId, ct)),
            ct);
        return await work.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<string> RestoreThreadRecoveryAsync(
        string packagePath,
        string expectedThreadId,
        CancellationToken ct = default) =>
        ThreadRecovery.RestoreAsync(packagePath, expectedThreadId, ct);

    private sealed class ThreadRecoveryCoordinator(SessionService owner)
    {
        public Task<ThreadRecoveryPackage> StartExport(
            string threadId,
            CancellationToken ct)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

            if (!owner._runtimeRegistry.TryGetRuntime(threadId, out var runtime))
                throw new KeyNotFoundException($"Thread '{threadId}' not found.");

            if (runtime.Maintenance != null
                || runtime.Thread.Turns.Any(static turn =>
                    turn.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput))
            {
                throw new InvalidOperationException(
                    $"A Turn or Thread maintenance operation is in progress on Thread '{threadId}'.");
            }

            var recoveryState = new ThreadMaintenanceState("recoveryExport");
            if (!runtime.TrySetMaintenance(recoveryState))
            {
                recoveryState.Dispose();
                throw new InvalidOperationException(
                    $"A Thread maintenance operation is in progress on Thread '{threadId}'.");
            }

            var maintenance = new ThreadMaintenanceRegistration(owner, threadId, recoveryState);
            return Task.Run(async () =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, maintenance.Token);
                try
                {
                    return await owner.Persistence.ExportRecoveryAsync(
                        threadId,
                        owner.AgentFactory.RuntimeContext.WorkspacePath,
                        linked.Token).ConfigureAwait(false);
                }
                finally
                {
                    await maintenance.CompleteAsync().ConfigureAwait(false);
                }
            }, CancellationToken.None);
        }

        public async Task<string> RestoreAsync(
            string packagePath,
            string expectedThreadId,
            CancellationToken ct)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(expectedThreadId);
            if (owner._runtimeRegistry.TryGetThread(expectedThreadId, out _))
            {
                throw new ThreadRecoveryException(
                    ThreadRecoveryErrorCodes.TargetExists,
                    $"Thread '{expectedThreadId}' already exists in memory.");
            }

            var restored = await owner.Persistence.RestoreRecoveryAsync(
                packagePath,
                expectedThreadId,
                owner.AgentFactory.RuntimeContext.WorkspacePath,
                ct);
            owner._runtimeRegistry.ClearPendingPermanentDeletion(expectedThreadId);
            return restored;
        }
    }
}
