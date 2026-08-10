namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    /// <inheritdoc />
    public Task<ThreadRecoveryPackage> ExportThreadRecoveryAsync(
        string threadId,
        CancellationToken ct = default) =>
        ThreadRecovery.ExportAsync(threadId, ct);

    /// <inheritdoc />
    public Task<string> RestoreThreadRecoveryAsync(
        string packagePath,
        string expectedThreadId,
        CancellationToken ct = default) =>
        ThreadRecovery.RestoreAsync(packagePath, expectedThreadId, ct);

    private sealed class ThreadRecoveryCoordinator(SessionService owner)
    {
        public async Task<ThreadRecoveryPackage> ExportAsync(
            string threadId,
            CancellationToken ct)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

            await owner.GetOrLoadThreadAsync(threadId, ct);
            if (!owner._runtimeRegistry.TryGetRuntime(threadId, out var runtime))
                throw new KeyNotFoundException($"Thread '{threadId}' not found.");

            ThreadMaintenanceState recoveryState;
            lock (runtime.TurnStartLock)
            {
                if (runtime.Maintenance != null
                    || runtime.Thread.Turns.Any(static turn =>
                        turn.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput))
                {
                    throw new InvalidOperationException(
                        $"A Turn or Thread maintenance operation is in progress on Thread '{threadId}'.");
                }

                recoveryState = new ThreadMaintenanceState("recoveryExport");
                if (!runtime.TrySetMaintenance(recoveryState))
                {
                    recoveryState.Dispose();
                    throw new InvalidOperationException(
                        $"A Thread maintenance operation is in progress on Thread '{threadId}'.");
                }
            }

            try
            {
                return await owner.Persistence.ExportRecoveryAsync(
                    threadId,
                    owner.AgentFactory.RuntimeContext.WorkspacePath,
                    ct);
            }
            finally
            {
                runtime.TryClearMaintenance(recoveryState);
                recoveryState.Dispose();
            }
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
